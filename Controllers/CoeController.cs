using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    /// <summary>
    /// Generates Certificate of Employment PDFs.
    ///   GET /Coe/WithoutCompensation?employeeNo=XX&amp;purpose=visa+application
    ///   GET /Coe/WithCompensation?employeeNo=XX&amp;purpose=car+loan+application
    ///
    /// Access: FemployeeM (any non-NO_ACCESS level).
    /// Salary data additionally gated by FSPayrollDetailsM READWRITE or FULL.
    /// </summary>
    [ModuleAuthorize("FemployeeM")]
    public class CoeController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly CoePdfService _coePdfService;

        // Must match the allowanceCode value in s_allowance table exactly
        private const string BasicAllowanceCode = "Basic Allowance";

        public CoeController(IDbConnection db, CoePdfService coePdfService)
        {
            _db = db;
            _coePdfService = coePdfService;
        }

        // ── COE WITHOUT Compensation ─────────────────────────────────────────────

        [HttpGet]
        public IActionResult WithoutCompensation(string employeeNo, string purpose = "")
        {
            if (!CoeAccessHelper.CanPrintWithout(RoleCode))
                return Forbid();

            if (string.IsNullOrWhiteSpace(employeeNo))
                return BadRequest("Employee number is required.");

            var data = BuildCoeData(employeeNo, purpose, withCompensation: false);
            if (data == null)
                return NotFound($"Employee '{employeeNo}' not found.");

            var pdfBytes = _coePdfService.GenerateCoe(data);
            var fileName = $"COE_{SanitizeFileName(data.FullName)}_{DateTime.Now:yyyyMMdd}.pdf";

            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
            return File(pdfBytes, "application/pdf");
        }

        // ── COE WITH Compensation ────────────────────────────────────────────────

        [HttpGet]
        public IActionResult WithCompensation(string employeeNo, string purpose = "", decimal monthlyIncentive = 0)
        {
            // Fetch target employee's rankCode for the rank-aware access check
            var empRankCode = _db.QueryFirstOrDefault<string>(@"
                SELECT rankCode FROM e_basicinfo
                WHERE employeeNo = @employeeNo
                  AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                LIMIT 1",
                new { employeeNo });

            if (!CoeAccessHelper.CanPrintWithForEmployee(RoleCode, empRankCode))
                return Forbid();

            if (string.IsNullOrWhiteSpace(employeeNo))
                return BadRequest("Employee number is required.");

            bool canViewSalary = AccessHelper.CanCreate(HttpContext, "FSPayrollDetailsM");

            var data = BuildCoeData(employeeNo, purpose, withCompensation: true,
                                    canViewSalary: canViewSalary, monthlyIncentive: monthlyIncentive);
            if (data == null)
                return NotFound($"Employee '{employeeNo}' not found.");

            var pdfBytes = _coePdfService.GenerateCoe(data);
            var fileName = $"COE_Comp_{SanitizeFileName(data.FullName)}_{DateTime.Now:yyyyMMdd}.pdf";

            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
            return File(pdfBytes, "application/pdf");
        }

        // ── Data builder ─────────────────────────────────────────────────────────

        private CoeData? BuildCoeData(
            string employeeNo,
            string purpose,
            bool withCompensation,
            bool canViewSalary = false,
            decimal monthlyIncentive = 0)
        {
            // 1. Employee basic info
            var employee = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT
                    e.employeeNo,
                    e.firstName,
                    e.middleName,
                    e.lastName,
                    sp.positionName,
                    ses.employmentStatusName,
                    DATE_FORMAT(e.dateHired, '%M %d, %Y')               AS dateHired,
                    DATE_FORMAT(e.dateOfEmpTermInitial, '%M %d, %Y')    AS dateOfEmpTermInitial,
                    e.isActive,
                    sb.branchName
                FROM e_basicinfo e
                LEFT JOIN s_position         sp  ON sp.positionCode           = e.positionCode
                LEFT JOIN s_employmentstatus  ses ON ses.employmentStatusCode  = e.employmentStatus
                LEFT JOIN s_branch            sb  ON sb.branchCode             = e.branchCode
                WHERE e.employeeNo = @employeeNo
                  AND (e.dtDeleted IS NULL OR e.dtDeleted = '0000-00-00 00:00:00')",
                new { employeeNo });

            if (employee == null) return null;

            // Build FIRSTNAME MIDDLENAME LASTNAME (full middle name)
            string empFirst = ((string?)employee.firstName ?? "").Trim();
            string empMid = ((string?)employee.middleName ?? "").Trim();
            string empLast = ((string?)employee.lastName ?? "").Trim();
            string empMidInitial = string.IsNullOrWhiteSpace(empMid) ? "" : $"{empMid[0]}.";
            string empFullName = string.IsNullOrWhiteSpace(empMidInitial)
                ? $"{empFirst} {empLast}".Trim()
                : $"{empFirst} {empMidInitial} {empLast}".Trim();

            // Fetch gender for MR./MS. prefix
            var personalInfo = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT gender
                FROM e_personalinfo
                WHERE employeeNo = @employeeNo
                LIMIT 1",
                new { employeeNo });

            string empGender = ((string?)personalInfo?.gender ?? "").Trim().ToUpperInvariant();
            string genderPrefix = empGender switch
            {
                "MALE" => "MR.",
                "FEMALE" => "MS.",
                _ => "MR./MS."
            };

            // Fetch signatory info from logged-in user (session)
            //string signatoryName = "";
            //string signatoryTitle = "";

            //if (!string.IsNullOrWhiteSpace(EmployeeNo))
            //{
            //    var signatoryInfo = _db.QueryFirstOrDefault<dynamic>(@"
            //        SELECT eb.firstName, eb.middleName, eb.lastName, sp2.positionName
            //        FROM e_basicinfo eb
            //        INNER JOIN s_position sp2 ON eb.positionCode = sp2.positionCode
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

            //var data = new CoeData
            //{
            //    EmployeeNo = employeeNo,
            //    FullName = empFullName,
            //    GenderPrefix = genderPrefix,
            //    Position = (string?)employee.positionName,
            //    EmploymentStatus = (string?)employee.employmentStatusName,
            //    DateHired = (string?)employee.dateHired,
            //    Branch = (string?)employee.branchName,
            //    Purpose = string.IsNullOrWhiteSpace(purpose) ? "[purpose]" : purpose.Trim(),
            //    IssuedDate = DateTime.Now.ToString("MMMM dd, yyyy"),
            //    IssuedCity = "Quezon City",
            //    WithCompensation = withCompensation,
            //    MonthlyIncentiveAmount = monthlyIncentive > 0 ? monthlyIncentive : (decimal?)null,
            //    CompanyName = "BGISIS DEVELOPMENT CORPORATION (Luxent Hotel)",
            //    SignatoryName = signatoryName,
            //    SignatoryTitle = signatoryTitle
            //};

            bool isActive = ((int?)employee.isActive ?? 1) == 1;
            string? termDate = (string?)employee.dateOfEmpTermInitial;

            var data = new CoeData
            {
                EmployeeNo = employeeNo,
                FullName = empFullName,
                GenderPrefix = genderPrefix,
                Position = (string?)employee.positionName,
                EmploymentStatus = (string?)employee.employmentStatusName,
                DateHired = (string?)employee.dateHired,
                DateTerminated = !isActive && !string.IsNullOrWhiteSpace(termDate) ? termDate : null,
                IsActive = isActive,
                Branch = (string?)employee.branchName,
                Purpose = string.IsNullOrWhiteSpace(purpose) ? "[purpose]" : purpose.Trim(),
                IssuedDate = DateTime.Now.ToString("MMMM dd, yyyy"),
                IssuedCity = "Quezon City",
                WithCompensation = withCompensation,
                MonthlyIncentiveAmount = monthlyIncentive > 0 ? monthlyIncentive : (decimal?)null,
                CompanyName = "BGISIS DEVELOPMENT CORPORATION (Luxent Hotel)",
                LastName = empLast,
                SignatoryName = "EDITHA M. CARREON",
                SignatoryTitle = "DIRECTOR OF HUMAN RESOURCES"
            };

            if (!withCompensation) return data;

            // 2. Basic Monthly Pay — AES encrypted in DB
            if (canViewSalary)
            {
                var payroll = _db.QueryFirstOrDefault<dynamic>(@"
                    SELECT
                        CAST(
                            IFNULL(
                                CAST(AES_DECRYPT(basicMonthlyPay, 'portalkeisan') AS CHAR(200)),
                                0
                            ) AS DECIMAL(10,2)
                        ) AS basicMonthlyPay
                    FROM e_payrolldetails
                    WHERE employeeNo = @employeeNo
                      AND isActive   = 1
                    LIMIT 1",
                    new { employeeNo });

                data.BasicMonthlyPay = payroll != null
                    ? (decimal?)payroll.basicMonthlyPay
                    : 0m;
            }
            else
            {
                data.BasicMonthlyPay = 0m;
            }

            // 3. Basic Allowance — single row, most recent active record
            if (canViewSalary)
            {
                var allowance = _db.QueryFirstOrDefault<dynamic>(@"
                    SELECT
                        sa.allowanceName,
                        CAST(ea.allowanceAmount AS DECIMAL(10,2)) AS allowanceAmount
                    FROM e_allowance ea
                    LEFT JOIN s_allowance sa ON sa.allowanceCode = ea.allowanceCode
                    WHERE ea.employeeNo    = @employeeNo
                      AND ea.allowanceCode = @allowanceCode
                      AND ea.isActive      = 1
                      AND (ea.dtDeleted IS NULL OR ea.dtDeleted = '0000-00-00 00:00:00')
                    ORDER BY ea.id DESC
                    LIMIT 1",
                    new { employeeNo, allowanceCode = BasicAllowanceCode });

                data.BasicAllowanceName = allowance != null
                    ? (string?)allowance.allowanceName ?? "Monthly Allowance"
                    : "Monthly Allowance";

                data.BasicAllowanceAmount = allowance != null
                    ? (decimal?)allowance.allowanceAmount ?? 0m
                    : 0m;
            }
            else
            {
                data.BasicAllowanceName = "Monthly Allowance";
                data.BasicAllowanceAmount = 0m;
            }

            return data;
        }

        // ── Utility ──────────────────────────────────────────────────────────────

        private static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Employee";
            return string.Concat(name.Split(Path.GetInvalidFileNameChars()))
                         .Replace(" ", "_")
                         .Replace(",", "");
        }
    }
}