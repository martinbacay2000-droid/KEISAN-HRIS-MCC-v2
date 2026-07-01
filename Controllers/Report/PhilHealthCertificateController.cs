using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    /// <summary>
    /// Generates the PhilHealth Certificate of Contributions PDF.
    ///
    ///   GET /PhilHealthCertificate/Generate
    ///       ?employeeNo=XX
    ///       &amp;fromMonth=January
    ///       &amp;toMonth=July
    ///       &amp;year=2024
    ///
    /// Access: roleCode must contain "HR" (same rule as CoeAccessHelper).
    /// </summary>
    [ModuleAuthorize("FemployeeM")]
    public class PhilHealthCertificateController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly PhilHealthCertificatePdfService _pdfService;

        // Ordered month list used for range iteration
        private static readonly string[] MonthNames =
        [
            "January","February","March","April","May","June",
            "July","August","September","October","November","December"
        ];

        public PhilHealthCertificateController(IDbConnection db, PhilHealthCertificatePdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        // ── GET /PhilHealthCertificate/Generate ──────────────────────────────────
        [HttpGet]
        public IActionResult Generate(
            string employeeNo,
            string fromMonth,
            string toMonth,
            string fromYear,
            string toYear,
            string purpose = "")
        {
            // ── 1. Access check — roleCode must contain "HR" ──────────────────
            if (!PhilHealthCertAccessHelper.CanPrint(RoleCode))
                return Forbid();

            // ── 2. Basic input validation ─────────────────────────────────────
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

            // ── 3. Build the full list of (year, month) pairs in range ────────
            var yearMonthPairs = new List<(int Year, string Month)>();
            int curYear = fromYearInt;
            int curMonth = fromIdx;

            while (curYear < toYearInt || (curYear == toYearInt && curMonth <= toIdx))
            {
                yearMonthPairs.Add((curYear, MonthNames[curMonth]));
                curMonth++;
                if (curMonth > 11) { curMonth = 0; curYear++; }
            }

            // ── 4. Fetch employee name (FIRSTNAME MIDDLENAME LASTNAME) ─────────
            //       Also fetch branchCode for OR# lookup
            var employee = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT
                    e.firstName,
                    e.middleName,
                    e.lastName,
                    e.branchCode
                FROM e_basicinfo e
                WHERE e.employeeNo = @employeeNo
                  AND (e.dtDeleted IS NULL OR e.dtDeleted = '0000-00-00 00:00:00')",
                new { employeeNo });

            if (employee == null)
                return NotFound($"Employee '{employeeNo}' not found.");

            string branchCode = (string?)employee.branchCode ?? "";

            // Build FIRSTNAME MIDDLENAME LASTNAME (full middle name)
            string firstName = (string?)employee.firstName ?? "";
            string middleName = (string?)employee.middleName ?? "";
            string lastName = (string?)employee.lastName ?? "";

            string fullName = string.IsNullOrWhiteSpace(middleName)
                ? $"{firstName} {lastName}".Trim()
                : $"{firstName} {middleName} {lastName}".Trim();

            // ── 5. Fetch gender from e_personalinfo ───────────────────────────
            var personalInfo = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT gender
                FROM e_personalinfo
                WHERE employeeNo = @employeeNo
                LIMIT 1",
                new { employeeNo });

            string gender = (string?)personalInfo?.gender ?? "";
            string genderPrefix = ResolveGenderPrefix(gender);

            // ── 6. Fetch PHIC number from payroll details ─────────────────────
            var payroll = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT philhealthNo
                FROM e_payrolldetails
                WHERE employeeNo = @employeeNo
                  AND isActive   = 1
                LIMIT 1",
                new { employeeNo });

            string phicNo = (string?)payroll?.philhealthNo ?? "";

            // ── 6. Fetch signatory info from the logged-in user (session) ─────
            //       Name  : userFullName from session (as-is, e.g. "CARREON, EDITHA M.")
            //       Title : positionName via e_basicinfo → s_position for the session user
            //string signatoryName = "";
            //string signatoryTitle = "";

            //if (!string.IsNullOrWhiteSpace(EmployeeNo))
            //{
            //    var signatoryInfo = _db.QueryFirstOrDefault<dynamic>(@"
            //        SELECT eb.firstName, eb.middleName, eb.lastName, sp.positionName
            //        FROM e_basicinfo eb
            //        INNER JOIN s_position sp ON eb.positionCode = sp.positionCode
            //        WHERE eb.employeeNo = @employeeNo
            //          AND (eb.dtDeleted IS NULL OR eb.dtDeleted = '0000-00-00 00:00:00')
            //        LIMIT 1",
            //        new { employeeNo = EmployeeNo });

            //    if (signatoryInfo != null)
            //    {
            //        string sFirst = ((string?)signatoryInfo.firstName ?? "").Trim().ToUpperInvariant();
            //        string sMid = ((string?)signatoryInfo.middleName ?? "").Trim().ToUpperInvariant();
            //        string sLast = ((string?)signatoryInfo.lastName ?? "").Trim().ToUpperInvariant();

            //        signatoryName = string.IsNullOrWhiteSpace(sMid)
            //            ? $"{sFirst} {sLast}".Trim()
            //            : $"{sFirst} {sMid[0]}. {sLast}".Trim();

            //        signatoryTitle = ((string?)signatoryInfo.positionName ?? "").ToUpperInvariant();
            //    }
            //}

            string signatoryName = "Vince S. Carlos";
            string signatoryTitle = "DIRECTOR OF HUMAN RESOURCES";

            // ── 7. Fetch contribution amounts — grouped by year then month ─────
            var orRecords = _db.Query<dynamic>(@"
                SELECT
                    dateYear,
                    dateMonth,
                    `OR`           AS orNumber,
                    DATE_FORMAT(dateOfPayment, '%c/%e/%Y') AS datePaid
                FROM t_philhealth
                WHERE branchCode = @branchCode
                  AND dateYear   IN @allYears
                  AND dateMonth  IN @allMonths
                  AND isActive   = 1",
                new
                {
                    branchCode,
                    allYears = yearMonthPairs.Select(p => p.Year).Distinct().ToArray(),
                    allMonths = yearMonthPairs.Select(p => p.Month).Distinct().ToArray()
                })
                .ToDictionary(r => $"{(int)r.dateYear}|{(string)r.dateMonth}", r => r);

            // ── 8. Fetch contribution amounts — grouped by year then month ─────
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
                    ROUND(SUM(pbio.deductionPHIemployee), 2) AS eeContribution,
                    ROUND(SUM(pbio.deductionPHIemployer), 2) AS erContribution
                FROM p_biometrics pbio
                WHERE pbio.employeeNo = @employeeNo
                  AND ({whereClause})
                  AND pbio.isActive   = 1
                  AND pbio.statusName = 'POSTED'
                GROUP BY pbio.dateYear, pbio.dateMonth",
                contribParams)
                .ToDictionary(r => $"{(int)r.dateYear}|{(string)r.dateMonth}", r => r);

            // ── 9. Build contribution rows (one per year-month pair in range) ──
            var rows = new List<PhilHealthContributionRow>();

            foreach (var (pairYear, pairMonth) in yearMonthPairs)
            {
                var contribKey = $"{pairYear}|{pairMonth}";
                contributions.TryGetValue(contribKey, out var contrib);
                orRecords.TryGetValue(contribKey, out var or);

                rows.Add(new PhilHealthContributionRow
                {
                    Month = pairMonth,
                    Year = pairYear.ToString(),
                    EEContribution = contrib != null ? (decimal)contrib.eeContribution : 0m,
                    ERContribution = contrib != null ? (decimal)contrib.erContribution : 0m,
                    ReceiptNo = or != null ? (string?)or.orNumber : null,
                    DatePaid = or != null ? (string?)or.datePaid : null
                });
            }

            // ── 10. Build issued-date string (e.g. "26th day of March 2026") ──
            var today = DateTime.Now;
            string issuedDate =
                $"{PhilHealthCertificatePdfService.OrdinalSuffix(today.Day)} day of " +
                $"{today:MMMM yyyy}";

            // ── 11. Compose the data object ───────────────────────────────────
            var certData = new PhilHealthCertificateData
            {
                EmployeeNo = employeeNo,
                EmployeeName = fullName,
                GenderPrefix = genderPrefix,
                PhicNo = phicNo,
                Purpose = string.IsNullOrWhiteSpace(purpose) ? "" : purpose.Trim(),
                IssuedDate = issuedDate,
                IssuedCity = "Quezon City",
                CompanyName = "Company Name",
                SignatoryName = signatoryName,
                SignatoryTitle = signatoryTitle,
                Rows = rows
            };

            // ── 12. Generate PDF ──────────────────────────────────────────────
            var pdfBytes = _pdfService.Generate(certData);

            var safeName = SanitizeFileName(certData.EmployeeName);
            var fileName = $"PhilHealthCertificate_{safeName}_{fromYear}-{fromMonth}_{toYear}-{toMonth}.pdf";

            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
            return File(pdfBytes, "application/pdf");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps MALE/FEMALE (case-insensitive) to the correct salutation prefix.
        /// Falls back to "MR/MS." for unknown/null values.
        /// </summary>
        private static string ResolveGenderPrefix(string? gender)
        {
            if (string.IsNullOrWhiteSpace(gender))
                return "MR/MS.";

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