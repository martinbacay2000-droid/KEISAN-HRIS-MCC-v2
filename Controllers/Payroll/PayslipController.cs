using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Models.Payroll;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    [ModuleAuthorize("FPayslip")]
    public class PayslipController : Controller
    {
        private readonly IDbConnection _db;

        public PayslipController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Payslip/Payslip.cshtml");
        }

        // ── GET: Payslip List ─────────────────────────────────────────────────
        [HttpGet]
        public JsonResult GetPayslipList(string cutOffType, string dateYear, string dateMonth)
        {
            var currentEmployeeNo = HttpContext.Session.GetString("employeeNo");
            var isFull = AccessHelper.GetAccess(HttpContext, "FPayslip") == "FULL";

            var sb = new StringBuilder(@"
                SELECT
                    p.employeeNo                                                                AS employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ',
                           LEFT(IFNULL(b.middleName,''), 1), '.')                              AS fullName,
                    CONCAT(DATE_FORMAT(p.dateFrom,'%m/%d/%Y'),
                           ' - ',
                           DATE_FORMAT(p.dateTo,'%m/%d/%Y'))                                   AS payPeriod,
                    p.statusName                                                                AS statusName,
                    IFNULL((
                        SELECT pl.isAccepted
                        FROM p_biometricsline pl
                        WHERE pl.employeeNo = p.employeeNo
                          AND pl.cutOffType = p.cutOffType
                          AND pl.dateMonth  = p.dateMonth
                          AND pl.dateYear   = p.dateYear
                          AND pl.isActive   = 1
                        LIMIT 1
                    ), 0)                                                                       AS isAccepted,
                    (
                        SELECT pl.dateStatus
                        FROM p_biometricsline pl
                        WHERE pl.employeeNo = p.employeeNo
                          AND pl.cutOffType = p.cutOffType
                          AND pl.dateMonth  = p.dateMonth
                          AND pl.dateYear   = p.dateYear
                          AND pl.isActive   = 1
                        LIMIT 1
                    )                                                                           AS dateStatus,
                    p.cutOffType                                                                AS cutOffType,
                    p.dateMonth                                                                 AS dateMonth,
                    p.dateYear                                                                  AS dateYear

                FROM p_biometrics p
                JOIN e_basicinfo b ON b.employeeNo = p.employeeNo

                WHERE p.isActive   = 1
                  AND p.statusName = 'Posted'
            ");

            var parameters = new DynamicParameters();

            // ── Scope: READ sees only own rows, FULL sees all ─────────────────
            if (!isFull)
            {
                sb.Append(" AND p.employeeNo = @currentEmployeeNo ");
                parameters.Add("@currentEmployeeNo", currentEmployeeNo);
            }

            if (!string.IsNullOrWhiteSpace(dateYear))
            {
                sb.Append(" AND p.dateYear = @dateYear ");
                parameters.Add("@dateYear", dateYear);
            }

            if (!string.IsNullOrWhiteSpace(dateMonth))
            {
                sb.Append(" AND p.dateMonth = @dateMonth ");
                parameters.Add("@dateMonth", dateMonth);
            }

            if (!string.IsNullOrWhiteSpace(cutOffType))
            {
                sb.Append(" AND p.cutOffType = @cutOffType ");
                parameters.Add("@cutOffType", cutOffType);
            }

            sb.Append(" ORDER BY b.lastName, p.dateYear DESC, p.dateMonth DESC ;");

            var result = _db.Query<PayslipListModel>(sb.ToString(), parameters).ToList();
            return Json(new { data = result });
        }

        // ── POST: Accept Payslip ──────────────────────────────────────────────
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public JsonResult AcceptPayslip([FromBody] AcceptPayslipRequest model)
        {
            try
            {
                // Guard: model itself is null (deserialization failed / empty body)
                if (model == null)
                    return Json(new { success = false, message = "Invalid request. No data received." });

                // Guard: required fields missing or empty
                if (string.IsNullOrWhiteSpace(model.employeeNo) ||
                    string.IsNullOrWhiteSpace(model.cutOffType) ||
                    string.IsNullOrWhiteSpace(model.dateMonth) ||
                    string.IsNullOrWhiteSpace(model.dateYear))
                    return Json(new { success = false, message = "Missing payslip information. Please select a cutoff period first." });

                var currentEmployeeNo = HttpContext.Session.GetString("employeeNo");

                // Guard: session expired
                if (string.IsNullOrWhiteSpace(currentEmployeeNo))
                    return Json(new { success = false, message = "Session expired. Please login again." });

                var isFull = AccessHelper.GetAccess(HttpContext, "FPayslip") == "FULL";

                // READ users can only accept their own payslip
                if (!isFull && model.employeeNo != currentEmployeeNo)
                    return Json(new { success = false, message = "You are not authorized to accept this payslip." });

                // Confirm the payslip row exists in p_biometricsline
                var lineExists = _db.QueryFirstOrDefault<int?>(@"
                    SELECT id FROM p_biometricsline
                    WHERE employeeNo = @employeeNo
                      AND cutOffType = @cutOffType
                      AND dateMonth  = @dateMonth
                      AND CAST(dateYear AS CHAR) = @dateYear
                      AND isActive   = 1
                    LIMIT 1",
                    new
                    {
                        employeeNo = model.employeeNo,
                        cutOffType = model.cutOffType,
                        dateMonth = model.dateMonth,
                        dateYear = model.dateYear
                    });

                if (lineExists == null)
                    return Json(new { success = false, message = "Payslip record not found in the system." });

                // Check not already accepted
                var alreadyAccepted = _db.QueryFirstOrDefault<int>(@"
                    SELECT IFNULL(isAccepted, 0)
                    FROM p_biometricsline
                    WHERE employeeNo = @employeeNo
                      AND cutOffType = @cutOffType
                      AND dateMonth  = @dateMonth
                      AND CAST(dateYear AS CHAR) = @dateYear
                      AND isActive   = 1
                    LIMIT 1",
                    new
                    {
                        employeeNo = model.employeeNo,
                        cutOffType = model.cutOffType,
                        dateMonth = model.dateMonth,
                        dateYear = model.dateYear
                    });

                if (alreadyAccepted == 1)
                    return Json(new { success = false, message = "Payslip has already been accepted." });

                // Update ALL matching rows for this employee + cutoff period
                var rowsAffected = _db.Execute(@"
                    UPDATE p_biometricsline
                    SET isAccepted         = 1,
                        dateStatus         = NOW(),
                        dtLastModified     = NOW(),
                        lastModifiedByUser = @acceptedBy
                    WHERE employeeNo = @employeeNo
                      AND cutOffType = @cutOffType
                      AND dateMonth  = @dateMonth
                      AND CAST(dateYear AS CHAR) = @dateYear
                      AND isActive   = 1",
                    new
                    {
                        acceptedBy = currentEmployeeNo,
                        employeeNo = model.employeeNo,
                        cutOffType = model.cutOffType,
                        dateMonth = model.dateMonth,
                        dateYear = model.dateYear
                    });

                if (rowsAffected == 0)
                    return Json(new { success = false, message = "No records were updated. Please try again." });

                return Json(new { success = true, message = "Payslip accepted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error accepting payslip: {ex.Message}" });
            }
        }
    }

    // ── Request model for AcceptPayslip ───────────────────────────────────────
    public class AcceptPayslipRequest
    {
        public string? employeeNo { get; set; }
        public string? cutOffType { get; set; }
        public string? dateMonth { get; set; }
        public string? dateYear { get; set; }
    }

    // ── Query result model ────────────────────────────────────────────────────
    public class PayslipListModel
    {
        public string? employeeNo { get; set; }
        public string? fullName { get; set; }
        public string? payPeriod { get; set; }
        public string? statusName { get; set; }
        public int isAccepted { get; set; }
        public string? dateStatus { get; set; }
        public string? cutOffType { get; set; }
        public string? dateMonth { get; set; }
        public string? dateYear { get; set; }
    }
}