using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FemployeeM")]
    public class PagIbigContributionController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly PagIbigContributionPdfService _pdfService;

        private static readonly string[] MonthNames =
        [
            "January","February","March","April","May","June",
            "July","August","September","October","November","December"
        ];

        public PagIbigContributionController(IDbConnection db, PagIbigContributionPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        [HttpGet]
        public IActionResult Generate(
            string employeeNo,
            string fromMonth,
            string toMonth,
            string fromYear,   // ← was: string year
            string toYear,     // ← NEW
            string purpose = "")
        {
            // ── 1. Access check ───────────────────────────────────────────────
            if (!PhilHealthCertAccessHelper.CanPrint(RoleCode))
                return Forbid();

            // ── 2. Input validation ───────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(employeeNo))
                return BadRequest("Employee number is required.");

            if (string.IsNullOrWhiteSpace(fromMonth) || string.IsNullOrWhiteSpace(toMonth) ||
                string.IsNullOrWhiteSpace(fromYear) || string.IsNullOrWhiteSpace(toYear))
                return BadRequest("fromMonth, toMonth, fromYear and toYear are required.");

            if (!int.TryParse(fromYear, out int fromYearInt) || !int.TryParse(toYear, out int toYearInt))
                return BadRequest("Invalid year values.");

            int fromIdx = Array.IndexOf(MonthNames, fromMonth);
            int toIdx = Array.IndexOf(MonthNames, toMonth);

            if (fromIdx < 0 || toIdx < 0)
                return BadRequest("Invalid month names.");

            if (toYearInt < fromYearInt || (toYearInt == fromYearInt && toIdx < fromIdx))
                return BadRequest("'To' date must be the same as or after 'From' date.");

            // ── 3. Build full list of (year, month) pairs in range ────────────
            var yearMonthPairs = new List<(int Year, string Month)>();
            int curYear = fromYearInt;
            int curMonth = fromIdx;

            while (curYear < toYearInt || (curYear == toYearInt && curMonth <= toIdx))
            {
                yearMonthPairs.Add((curYear, MonthNames[curMonth]));
                curMonth++;
                if (curMonth > 11) { curMonth = 0; curYear++; }
            }

            // ── 4. Fetch employee name ────────────────────────────────────────
            var employee = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT e.firstName, e.middleName, e.lastName
                FROM e_basicinfo e
                WHERE e.employeeNo = @employeeNo
                  AND (e.dtDeleted IS NULL OR e.dtDeleted = '0000-00-00 00:00:00')",
                new { employeeNo });

            if (employee == null)
                return NotFound($"Employee '{employeeNo}' not found.");

            string firstName = (string?)employee.firstName ?? "";
            string middleName = (string?)employee.middleName ?? "";
            string lastName = (string?)employee.lastName ?? "";

            string fullName = string.IsNullOrWhiteSpace(middleName)
                ? $"{firstName} {lastName}".Trim()
                : $"{firstName} {middleName} {lastName}".Trim();

            // ── 5. Fetch gender ───────────────────────────────────────────────
            var personalInfo = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT gender FROM e_personalinfo
                WHERE employeeNo = @employeeNo LIMIT 1",
                new { employeeNo });

            string gender = (string?)personalInfo?.gender ?? "";
            string genderPrefix = ResolveGenderPrefix(gender);

            // ── 6. Fetch HDMF number ──────────────────────────────────────────
            var payroll = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT hdmfNo FROM e_payrolldetails
                WHERE employeeNo = @employeeNo AND isActive = 1 LIMIT 1",
                new { employeeNo });

            string hdmfNo = (string?)payroll?.hdmfNo ?? "";

            // ── 7. Signatory ──────────────────────────────────────────────────
            string signatoryName = "Vince S. Carlos";
            string signatoryTitle = "DIRECTOR OF HUMAN RESOURCES";

            // ── 8. Fetch Pag-IBIG contributions — dynamic WHERE per year ──────
            var yearGroups = yearMonthPairs
                .GroupBy(p => p.Year)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Month).ToList());

            var yearClauses = yearGroups.Keys
                .Select((y, i) => $"(pbio.dateYear = {y} AND pbio.dateMonth IN @months{i})")
                .ToList();

            var whereClause = string.Join(" OR ", yearClauses);

            var contribParams = new DynamicParameters();
            contribParams.Add("employeeNo", employeeNo);
            int paramIdx = 0;
            foreach (var kvp in yearGroups)
            {
                contribParams.Add($"months{paramIdx}", kvp.Value);
                paramIdx++;
            }

            var contributions = _db.Query<dynamic>($@"
                SELECT
                    pbio.dateYear,
                    pbio.dateMonth,
                    ROUND(SUM(pbio.deductionPIFemployee), 2)                             AS eeContribution,
                    ROUND(SUM(pbio.deductionPIFemployer), 2)                             AS erContribution,
                    ROUND(SUM(pbio.deductionPIFemployee + pbio.deductionPIFemployer), 2) AS totalContribution,
                    ROUND(SUM(pbio.hdmfLoan), 2)                                         AS hdmfLoan
                FROM p_biometrics pbio
                WHERE pbio.employeeNo = @employeeNo
                  AND ({whereClause})
                  AND pbio.isActive   = 1
                  AND pbio.statusName = 'POSTED'
                GROUP BY pbio.dateYear, pbio.dateMonth",
                contribParams)
                .ToDictionary(r => $"{(int)r.dateYear}|{(string)r.dateMonth}", r => r);

            // ── 9. Build rows — one per year-month pair in range ──────────────
            var rows = new List<PagIbigContributionRow>();

            foreach (var (pairYear, pairMonth) in yearMonthPairs)
            {
                var key = $"{pairYear}|{pairMonth}";
                contributions.TryGetValue(key, out var c);

                rows.Add(new PagIbigContributionRow
                {
                    Month = pairMonth,
                    Year = pairYear.ToString(),  // ← now per-pair
                    EEContribution = c != null ? (decimal)c.eeContribution : 0m,
                    ERContribution = c != null ? (decimal)c.erContribution : 0m,
                    TotalContribution = c != null ? (decimal)c.totalContribution : 0m,
                    HdmfLoan = c != null ? (decimal)c.hdmfLoan : 0m
                });
            }

            // ── 10. Issued date ───────────────────────────────────────────────
            var today = DateTime.Now;
            string issued = $"{PagIbigContributionPdfService.OrdinalSuffix(today.Day)} day of {today:MMMM yyyy}";

            // ── 11. Compose data object ───────────────────────────────────────
            var reportData = new PagIbigContributionReportData
            {
                EmployeeNo = employeeNo,
                EmployeeName = fullName,
                GenderPrefix = genderPrefix,
                HdmfNo = hdmfNo,
                Purpose = string.IsNullOrWhiteSpace(purpose) ? "" : purpose.Trim(),
                IssuedDate = issued,
                IssuedCity = "Quezon City",
                CompanyName = "Company Name",
                SignatoryName = signatoryName,
                SignatoryTitle = signatoryTitle,
                Rows = rows
            };

            // ── 12. Generate and stream PDF ───────────────────────────────────
            var pdfBytes = _pdfService.Generate(reportData);
            var safeName = SanitizeFileName(reportData.EmployeeName);
            var fileName = $"PagIBIGContribution_{safeName}_{fromYear}-{fromMonth}_{toYear}-{toMonth}.pdf";

            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
            return File(pdfBytes, "application/pdf");
        }

        private static string ResolveGenderPrefix(string? gender)
        {
            if (string.IsNullOrWhiteSpace(gender)) return "MR/MS.";
            return gender.Trim().ToUpperInvariant() switch
            {
                "MALE" => "MR.",
                "FEMALE" => "MS.",
                _ => "MR/MS."
            };
        }

        private static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Employee";
            return string.Concat(name.Split(Path.GetInvalidFileNameChars()))
                         .Replace(" ", "_")
                         .Replace(",", "");
        }
    }
}