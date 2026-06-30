using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FSPayrollDetailsM")]
    public class PayrollDetailsController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public PayrollDetailsController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        // ── Access helpers ────────────────────────────────────────────────────────
        // Salaries are visible to FULL and READWRITE only
        private bool CanViewSalaries
        {
            get
            {
                var accessLevel = AccessHelper.GetAccess(HttpContext, "FSPayrollDetailsM");
                var sessionEmployeeNo = HttpContext.Session.GetString("employeeNo");

                // READ: can only see salary if viewing their own record
                if (accessLevel == "READ")
                {
                    var employeeNoBeingViewed = HttpContext.Request.Query["employeeNo"].ToString()
                        ?? HttpContext.Request.Form["employeeNo"].ToString();
                    return employeeNoBeingViewed == sessionEmployeeNo;
                }

                // READWRITE or FULL: can see all salaries
                return accessLevel is "READWRITE" or "FULL";
            }
        }
        // Can edit payroll fields (non-salary): EDIT, READWRITE, FULL
        private bool CanEditPayroll => AccessHelper.CanEdit(HttpContext, "FSPayrollDetailsM");
        // Can save/create records: READWRITE, FULL
        private bool CanSavePayroll => AccessHelper.CanCreate(HttpContext, "FSPayrollDetailsM");
        // Full destructive actions: FULL only
        private bool CanFullAccess => AccessHelper.CanDelete(HttpContext, "FSPayrollDetailsM");

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetPayrollDetails(string employeeNo)
        {
            try
            {
                // Build conditional decryption based on salary visibility
                string salaryFields = CanViewSalaries
                    ? @"CAST(IFNULL(CAST(AES_DECRYPT(e.meritServicePay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS meritServicePay,
                        CAST(IFNULL(CAST(AES_DECRYPT(e.basicSalary,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS basicSalary,
                        CAST(IFNULL(CAST(AES_DECRYPT(e.basicMonthlyPay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS basicMonthlyPay,
                        CAST(IFNULL(CAST(AES_DECRYPT(e.dailyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS dailyRate,
                        CAST(IFNULL(CAST(AES_DECRYPT(e.hourlyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS hourlyRate"
                    : @"0 AS meritServicePay,
                        0 AS basicSalary,
                        0 AS basicMonthlyPay,
                        0 AS dailyRate,
                        0 AS hourlyRate";

                var employee = _db.QueryFirstOrDefault<userPayrollDetails>(
                    $@"SELECT
                        e.id,
                        e.employeeNo,
                        e.payrollBasis,
                        e.payrollType,
                        IFNULL(e.isMinimumWageEarner,0) as isMinimumWageEarner,
                        e.bankType,
                        b.bankName,
                        e.bankCode,
                        e.accountNo,
                        DATE_FORMAT(e.effectivityDate,'%Y/%m/%d') as effectivityDate,
                        {salaryFields},
                        e.sssNo,
                        e.philhealthNo,
                        e.hdmfNo,
                        e.tinNo,
                        e.contriPIFadditional,
                        e.mp2,
                        IFNULL(e.isNoLate,0) as isNoLate,
                        IFNULL(e.isNoOTPremium,0) as isNoOTPremium,
                        e.payrollGroup
                    FROM e_payrolldetails e
                    LEFT JOIN s_bank b ON b.bankCode = e.bankType AND b.isActive = 1
                    WHERE e.employeeNo = @employeeNo
                        AND e.isActive = 1",
                    new { employeeNo });

                if (employee == null)
                {
                    employee = new userPayrollDetails { employeeNo = employeeNo };
                }

                // Pass access flags to view
                ViewBag.CanViewSalaries = CanViewSalaries;
                ViewBag.CanEditPayroll = CanEditPayroll;
                ViewBag.CanSavePayroll = CanSavePayroll;
                ViewBag.CanFullAccess = CanFullAccess;

                return PartialView("~/Views/Users/Partials/_PayrollDetails.cshtml", employee);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPayrollDetails: {ex.Message}");
                ViewBag.CanViewSalaries = false;
                ViewBag.CanEditPayroll = false;
                ViewBag.CanSavePayroll = false;
                ViewBag.CanFullAccess = false;
                return PartialView("~/Views/Users/Partials/_PayrollDetails.cshtml",
                    new userPayrollDetails { employeeNo = employeeNo });
            }
        }

        [HttpGet]
        public JsonResult GetBankType()
        {
            try
            {
                string sql = @"SELECT bankCode AS bankType, bankName
                               FROM s_bank
                               WHERE dtDeleted IS NULL
                               AND isActive = 1
                               ORDER BY bankName";

                var banks = _db.Query<dynamic>(sql).ToList();
                return Json(banks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetBankType: {ex.Message}");
                return Json(new List<dynamic>());
            }
        }

        [HttpGet]
        public JsonResult GetRateHistory(string employeeNo)
        {
            try
            {
                // Build conditional decryption for rate history
                string salaryFields = CanViewSalaries
                    ? @"CAST(IFNULL(CAST(AES_DECRYPT(r.basicMonthlyPay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS basicMonthlyPay,
                        CAST(IFNULL(CAST(AES_DECRYPT(r.dailyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS dailyRate,
                        CAST(IFNULL(CAST(AES_DECRYPT(r.hourlyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS hourlyRate"
                    : @"0 AS basicMonthlyPay,
                        0 AS dailyRate,
                        0 AS hourlyRate";

                string sql = $@"SELECT
                              r.id,
                              r.employeeNo,
                              DATE_FORMAT(r.effectivityDate,'%Y/%m/%d') AS effectivityDate,
                              {salaryFields},
                              r.payrollBasis,
                              DATE_FORMAT(r.dtAdded,'%m/%d/%Y') AS dtAdded,
                              CONCAT(s.lastName, ', ', s.firstName) AS addedByUser
                              FROM e_rateHistory r
                              LEFT JOIN s_user s ON s.userCode = r.addedByUser
                              WHERE r.isActive = 1
                              AND r.employeeNo = @employeeNo
                              ORDER BY r.id DESC";

                var rateHistory = _db.Query<userPayrollDetails>(sql, new { employeeNo }).ToList();
                return Json(new { data = rateHistory });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetRateHistory: {ex.Message}");
                return Json(new { data = new List<userPayrollDetails>(), error = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult UpdatePayrollDetails(userPayrollDetails model)
        {
            // Must have at least EDIT access to touch payroll details
            if (!CanEditPayroll)
                return Json(new { success = false, message = "Unauthorized: You do not have permission to edit payroll details." });

            // Salary fields can only be saved if the user can view/edit salaries (READWRITE or FULL)
            // For EDIT-only users we zero out salary fields so they are not overwritten
            bool saveSalaries = CanViewSalaries;

            try
            {
                var employeeName = _db.QueryFirstOrDefault<string>(
                    "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { model.employeeNo });

                var exists = _db.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM e_payrolldetails WHERE employeeNo = @employeeNo AND isActive = 1",
                    new { model.employeeNo });

                if (exists == 0) // Insert new record
                {
                    // Only READWRITE / FULL can create records (CanSavePayroll)
                    if (!CanSavePayroll)
                        return Json(new { success = false, message = "Unauthorized: You do not have permission to create payroll records." });

                    string sql = @"
                    INSERT INTO e_payrolldetails (
                        employeeNo, isActive, isMinimumWageEarner, fixedNetPay,
                        meritServicePay, basicSalary, basicMonthlyPay, dailyRate, hourlyRate,
                        effectivityDate, payrollBasis, payrollType, mp2, contriPIFadditional,
                        tinNo, sssNo, philhealthNo, hdmfNo, bankType, bankCode, accountNo,
                        isNoLate, isNoOTPremium, payrollGroup, dtAdded, addedByUser
                    )
                    VALUES (
                        @employeeNo, 1, @isMinimumWageEarner, @fixedNetPay,
                        AES_ENCRYPT(@meritServicePay, 'portalkeisan'),
                        AES_ENCRYPT(@basicSalary, 'portalkeisan'),
                        AES_ENCRYPT(@basicMonthlyPay, 'portalkeisan'),
                        AES_ENCRYPT(@dailyRate, 'portalkeisan'),
                        AES_ENCRYPT(@hourlyRate, 'portalkeisan'),
                        @effectivityDate, @payrollBasis, @payrollType, @mp2, @contriPIFadditional,
                        @tinNo, @sssNo, @philhealthNo, @hdmfNo, @bankType, @bankCode, @accountNo,
                        @isNoLate, @isNoOTPremium, @payrollGroup, NOW(), @addedByUser
                    );

                    INSERT INTO e_ratehistory (
                        employeeNo, basicMonthlyPay, basicSalary, meritServicePay, dailyRate, hourlyRate,
                        payrollBasis, effectivityDate, isActive, dtAdded, addedByUser
                    )
                    VALUES (
                        @employeeNo,
                        AES_ENCRYPT(@basicMonthlyPay, 'portalkeisan'),
                        AES_ENCRYPT(@basicSalary, 'portalkeisan'),
                        AES_ENCRYPT(@meritServicePay, 'portalkeisan'),
                        AES_ENCRYPT(@dailyRate, 'portalkeisan'),
                        AES_ENCRYPT(@hourlyRate, 'portalkeisan'),
                        @payrollBasis, @effectivityDate, 1, NOW(), @addedByUser
                    );";

                    _db.Execute(sql, new
                    {
                        employeeNo = model.employeeNo,
                        isMinimumWageEarner = model.isMinimumWageEarner ?? false,
                        fixedNetPay = model.fixedNetPay ?? 0,
                        meritServicePay = saveSalaries ? (model.meritServicePay ?? 0) : 0,
                        basicSalary = saveSalaries ? (model.basicSalary ?? 0) : 0,
                        basicMonthlyPay = saveSalaries ? (model.basicMonthlyPay ?? 0) : 0,
                        dailyRate = saveSalaries ? (model.dailyRate ?? 0) : 0,
                        hourlyRate = saveSalaries ? (model.hourlyRate ?? 0) : 0,
                        effectivityDate = model.effectivityDate,
                        payrollBasis = model.payrollBasis,
                        payrollType = model.payrollType,
                        mp2 = model.mp2 ?? 0,
                        contriPIFadditional = model.contriPIFadditional ?? 0,
                        tinNo = model.tinNo,
                        sssNo = model.sssNo,
                        philhealthNo = model.philhealthNo,
                        hdmfNo = model.hdmfNo,
                        bankType = model.bankType,
                        bankCode = model.bankCode,
                        accountNo = model.accountNo,
                        isNoLate = model.isNoLate ?? false,
                        isNoOTPremium = model.isNoOTPremium ?? false,
                        payrollGroup = model.payrollGroup,
                        addedByUser = EmployeeNo
                    });

                    _auditTrail.Log("e_payrolldetails", 0, "CREATED",
                        $"Created payroll details for employee {model.employeeNo} - {employeeName}");

                    return Json(new { success = true, message = "Payroll details created successfully!" });
                }
                else // Update existing record
                {
                    string sqlHistory = "";

                    if (model.toInsertHistory == 1 && saveSalaries)
                    {
                        sqlHistory = @"
                        INSERT INTO e_ratehistory (
                            employeeNo, basicMonthlyPay, basicSalary, meritServicePay, dailyRate, hourlyRate,
                            payrollBasis, effectivityDate, isActive, dtAdded, addedByUser
                        )
                        VALUES (
                            @employeeNo,
                            AES_ENCRYPT(@basicMonthlyPay, 'portalkeisan'),
                            AES_ENCRYPT(@basicSalary, 'portalkeisan'),
                            AES_ENCRYPT(@meritServicePay, 'portalkeisan'),
                            AES_ENCRYPT(@dailyRate, 'portalkeisan'),
                            AES_ENCRYPT(@hourlyRate, 'portalkeisan'),
                            @payrollBasis, @effectivityDate, 1, NOW(), @addedByUser
                        );";
                    }

                    // Build salary SET clause conditionally to avoid overwriting with zeros
                    string salarySetClause = saveSalaries
                        ? @"meritServicePay = AES_ENCRYPT(@meritServicePay, 'portalkeisan'),
                            basicSalary     = AES_ENCRYPT(@basicSalary, 'portalkeisan'),
                            basicMonthlyPay = AES_ENCRYPT(@basicMonthlyPay, 'portalkeisan'),
                            dailyRate       = AES_ENCRYPT(@dailyRate, 'portalkeisan'),
                            hourlyRate      = AES_ENCRYPT(@hourlyRate, 'portalkeisan'),"
                        : ""; // EDIT-only: do NOT touch salary columns

                    string sql = $@"
                    UPDATE e_payrolldetails
                    SET isMinimumWageEarner = @isMinimumWageEarner,
                        fixedNetPay        = @fixedNetPay,
                        {salarySetClause}
                        effectivityDate    = @effectivityDate,
                        payrollBasis       = @payrollBasis,
                        payrollType        = @payrollType,
                        mp2                = @mp2,
                        contriPIFadditional = @contriPIFadditional,
                        tinNo              = @tinNo,
                        sssNo              = @sssNo,
                        philhealthNo       = @philhealthNo,
                        hdmfNo             = @hdmfNo,
                        bankType           = @bankType,
                        bankCode           = @bankCode,
                        accountNo          = @accountNo,
                        isNoLate           = @isNoLate,
                        isNoOTPremium      = @isNoOTPremium,
                        payrollGroup       = @payrollGroup,
                        dtLastModified     = NOW(),
                        lastModifiedByUser = @addedByUser
                    WHERE employeeNo = @employeeNo AND isActive = 1;"
                    + sqlHistory;

                    _db.Execute(sql, new
                    {
                        employeeNo = model.employeeNo,
                        isMinimumWageEarner = model.isMinimumWageEarner ?? false,
                        fixedNetPay = model.fixedNetPay ?? 0,
                        meritServicePay = saveSalaries ? (model.meritServicePay ?? 0) : 0,
                        basicSalary = saveSalaries ? (model.basicSalary ?? 0) : 0,
                        basicMonthlyPay = saveSalaries ? (model.basicMonthlyPay ?? 0) : 0,
                        dailyRate = saveSalaries ? (model.dailyRate ?? 0) : 0,
                        hourlyRate = saveSalaries ? (model.hourlyRate ?? 0) : 0,
                        effectivityDate = model.effectivityDate,
                        payrollBasis = model.payrollBasis,
                        payrollType = model.payrollType,
                        mp2 = model.mp2 ?? 0,
                        contriPIFadditional = model.contriPIFadditional ?? 0,
                        tinNo = model.tinNo,
                        sssNo = model.sssNo,
                        philhealthNo = model.philhealthNo,
                        hdmfNo = model.hdmfNo,
                        bankType = model.bankType,
                        bankCode = model.bankCode,
                        accountNo = model.accountNo,
                        isNoLate = model.isNoLate ?? false,
                        isNoOTPremium = model.isNoOTPremium ?? false,
                        payrollGroup = model.payrollGroup,
                        addedByUser = EmployeeNo
                    });

                    _auditTrail.Log("e_payrolldetails", model.id, "UPDATED",
                        $"Updated payroll details for employee {model.employeeNo} - {employeeName}");

                    return Json(new { success = true, message = "Payroll details updated successfully!" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdatePayrollDetails: {ex.Message}");
                return Json(new { success = false, message = $"Error updating record: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult RemoveHistory(int id, string reason = "")
        {
            // Only FULL access can remove history
            if (!CanFullAccess)
                return Json(new { success = false, message = "Unauthorized: Only users with Full Access can delete rate history." });

            try
            {
                var history = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo FROM e_rateHistory WHERE id = @id",
                    new { id });

                if (history == null)
                    return Json(new { success = false, message = "Rate history record not found!" });

                string sql = @"UPDATE e_rateHistory
                              SET dtDeleted    = NOW(),
                                  isActive     = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @id";

                _db.Execute(sql, new { id, deletedByUser = EmployeeNo });

                var employeeName = _db.QueryFirstOrDefault<string>(
                    "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { employeeNo = history.employeeNo });

                _auditTrail.Log("e_rateHistory", id, "DELETED",
                    $"Deleted rate history for employee {history.employeeNo} - {employeeName}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "Rate history deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RemoveHistory: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreHistory(int id)
        {
            // Only FULL access can restore history
            if (!CanFullAccess)
                return Json(new { success = false, message = "Unauthorized: Only users with Full Access can restore rate history." });

            try
            {
                var history = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo FROM e_rateHistory WHERE id = @id",
                    new { id });

                if (history == null)
                    return Json(new { success = false, message = "Rate history record not found!" });

                string sql = @"UPDATE e_rateHistory
                              SET dtDeleted    = NULL,
                                  isActive     = 1,
                                  deletedByUser = NULL
                              WHERE id = @id";

                _db.Execute(sql, new { id });

                var employeeName = _db.QueryFirstOrDefault<string>(
                    "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { employeeNo = history.employeeNo });

                _auditTrail.Log("e_rateHistory", id, "RESTORED",
                    $"Restored rate history for employee {history.employeeNo} - {employeeName}");

                return Json(new { success = true, message = "Rate history restored successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreHistory: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}