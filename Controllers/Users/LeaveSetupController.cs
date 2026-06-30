using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Globalization;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FSLeaveSetupM")]
    public class LeaveSetupController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public LeaveSetupController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Users/Partials/_LeaveSetup.cshtml");
        }

        public IActionResult GetLeaveSetup(string employeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(employeeNo))
                {
                    return PartialView("~/Views/Users/Partials/_LeaveSetup.cshtml", new List<LeaveSetupModel>());
                }

                var employeeName = _db.QueryFirstOrDefault<string>(
                    @"SELECT CONCAT(lastName, ', ', firstName, ' ', COALESCE(middleName, '')) 
                      FROM e_basicinfo WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                var leaveSetups = GetLeaveSetupData(employeeNo, false);

                ViewBag.EmployeeNo = employeeNo;
                ViewBag.EmployeeName = employeeName ?? "Unknown Employee";

                return PartialView("~/Views/Users/Partials/_LeaveSetup.cshtml", leaveSetups);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveSetup: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_LeaveSetup.cshtml", new List<LeaveSetupModel>());
            }
        }

        [HttpGet]
        public JsonResult GetLeaveTypes()
        {
            try
            {
                var sql = @"
                    SELECT id, leaveCode, leaveName, leaveCredits, isActive
                    FROM s_leave 
                    WHERE isActive = 1 
                    AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                    ORDER BY leaveName";

                var leaveTypes = _db.Query<LeaveTypeModel>(sql).ToList();
                return Json(leaveTypes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveTypes: {ex.Message}");
                return Json(new List<LeaveTypeModel>());
            }
        }

        [HttpGet]
        public JsonResult GetLeaveCredits(string leaveCode)
        {
            try
            {
                var sql = @"
                    SELECT leaveCode, leaveName, leaveCredits 
                    FROM s_leave 
                    WHERE leaveCode = @LeaveCode 
                    AND isActive = 1 
                    AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')";

                var leaveType = _db.QueryFirstOrDefault<dynamic>(sql, new { LeaveCode = leaveCode });

                if (leaveType == null)
                {
                    return Json(new { success = false, leaveCredits = 0 });
                }

                return Json(new
                {
                    success = true,
                    leaveCredits = leaveType.leaveCredits ?? 0
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveCredits: {ex.Message}");
                return Json(new { success = false, leaveCredits = 0 });
            }
        }

        [HttpPost]
        public JsonResult SaveLeaveSetup([FromBody] LeaveSetupDto model)
        {
            try
            {
                if (!ValidateLeaveSetup(model, out string validationMessage))
                {
                    return Json(new { success = false, message = validationMessage });
                }

                var leaveType = _db.QueryFirstOrDefault<LeaveTypeModel>(
                    @"SELECT * FROM s_leave 
                      WHERE leaveCode = @LeaveCode 
                      AND isActive = 1 
                      AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                    new { LeaveCode = model.LeaveCode });

                if (leaveType == null)
                {
                    return Json(new { success = false, message = "Invalid leave type selected." });
                }

                // Validate leave credits against maximum allowed
                var maxCredits = leaveType.LeaveCredits ?? 0;
                if (maxCredits > 0 && model.RemainingBalance > maxCredits)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Leave credits cannot exceed {maxCredits:F2}. The selected leave type has a maximum of {maxCredits:F2} credits."
                    });
                }

                var isMaternityOrPaternity = IsMaternityOrPaternity(leaveType.LeaveName);

                if (!ProcessDates(model, isMaternityOrPaternity, out var dateFields, out string dateError))
                {
                    return Json(new { success = false, message = dateError });
                }

                if (model.Id.HasValue && model.Id > 0)
                {
                    return UpdateLeaveSetup(model, dateFields);
                }
                else
                {
                    return InsertLeaveSetup(model, dateFields);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveLeaveSetup: {ex.Message}");
                return Json(new { success = false, message = "Error saving leave setup: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult SoftDeleteLeaveSetup(int id, string reason = "")
        {
            try
            {
                if (!RecordExists(id))
                {
                    return Json(new { success = false, message = "Leave setup record not found or already deleted!" });
                }

                var sql = @"
                    UPDATE e_leave 
                    SET dtDeleted = NOW(), isActive = 0
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new { Id = id });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_leave", id, "DELETED",
                        $"Leave setup soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Leave setup deleted successfully!" })
                    : Json(new { success = false, message = "Failed to delete leave setup." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SoftDeleteLeaveSetup: {ex.Message}");
                return Json(new { success = false, message = "Error deleting leave setup: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreLeaveSetup(int id)
        {
            try
            {
                var existingRecord = _db.QueryFirstOrDefault<EmployeeLeaveModel>(
                    "SELECT * FROM e_leave WHERE id = @Id AND (dtDeleted IS NOT NULL AND dtDeleted != '0000-00-00 00:00:00')",
                    new { Id = id });

                if (existingRecord == null)
                {
                    return Json(new { success = false, message = "Leave setup record not found or not deleted!" });
                }

                var sql = @"
                    UPDATE e_leave 
                    SET dtDeleted = NULL, deletedByUser = NULL, isActive = 1, dtLastModified = NOW()
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new { Id = id });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_leave", id, "RESTORED", "Leave setup restored");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Leave setup restored successfully!" })
                    : Json(new { success = false, message = "Failed to restore leave setup." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreLeaveSetup: {ex.Message}");
                return Json(new { success = false, message = "Error restoring leave setup: " + ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetLeaveSetupById(int id)
        {
            try
            {
                var sql = BuildLeaveSetupQuery("WHERE el.id = @Id");
                var leaveSetup = _db.QueryFirstOrDefault<LeaveSetupModel>(sql, new { Id = id });

                return leaveSetup != null
                    ? Json(new { success = true, data = leaveSetup })
                    : Json(new { success = false, message = "Leave setup not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveSetupById: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving leave setup: " + ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetLeaveSetupData(string employeeNo)
        {
            try
            {
                var leaveSetups = GetLeaveSetupData(employeeNo, false);
                return Json(new { data = leaveSetups });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveSetupData: {ex.Message}");
                return Json(new { data = new List<LeaveSetupModel>() });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedLeaveSetupData(string employeeNo)
        {
            try
            {
                var leaveSetups = GetLeaveSetupData(employeeNo, true);
                return Json(new { data = leaveSetups });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeletedLeaveSetupData: {ex.Message}");
                return Json(new { data = new List<LeaveSetupModel>() });
            }
        }

        // HELPER METHODS

        private bool ValidateLeaveSetup(LeaveSetupDto model, out string message)
        {
            message = string.Empty;

            if (model == null || string.IsNullOrEmpty(model.EmployeeNo) || string.IsNullOrEmpty(model.LeaveCode))
            {
                message = "Invalid data provided.";
                return false;
            }

            return true;
        }

        private bool ProcessDates(LeaveSetupDto model, bool isMaternityOrPaternity,
            out (string dateEntitled, string dateFrom, string dateTo) dateFields, out string errorMessage)
        {
            dateFields = (null, null, null);
            errorMessage = string.Empty;

            if (isMaternityOrPaternity)
            {
                if (string.IsNullOrEmpty(model.DateFrom))
                {
                    errorMessage = "Date From is required for Maternity/Paternity leave.";
                    return false;
                }

                if (!DateTime.TryParseExact(model.DateFrom, "yyyy/MM/dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime parsedDateFrom))
                {
                    errorMessage = "Invalid date from format.";
                    return false;
                }

                var calculatedDateTo = CalculateDateTo(parsedDateFrom, (int)model.RemainingBalance);

                dateFields = (null, parsedDateFrom.ToString("yyyy-MM-dd"), calculatedDateTo.ToString("yyyy-MM-dd"));
            }
            else
            {
                if (string.IsNullOrEmpty(model.DateEntitled))
                {
                    errorMessage = "Date Entitled is required for this leave type.";
                    return false;
                }

                if (!DateTime.TryParseExact(model.DateEntitled, "yyyy/MM/dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime parsedDateEntitled))
                {
                    errorMessage = "Invalid date entitled format.";
                    return false;
                }

                dateFields = (parsedDateEntitled.ToString("yyyy-MM-dd"), null, null);
            }

            return true;
        }

        private JsonResult UpdateLeaveSetup(LeaveSetupDto model, (string dateEntitled, string dateFrom, string dateTo) dateFields)
        {
            var existingRecord = _db.QueryFirstOrDefault<EmployeeLeaveModel>(
                "SELECT * FROM e_leave WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = model.Id });

            if (existingRecord == null)
            {
                return Json(new { success = false, message = "Leave setup record not found or has been deleted!" });
            }

            var oldBalance = GetLatestBalance(model.EmployeeNo, model.LeaveCode);

            var sql = @"
                UPDATE e_leave 
                SET leaveCode = @LeaveCode, dateEntitled = @DateEntitled, dateFrom = @DateFrom, 
                    dateTo = @DateTo, leaveDays = @LeaveDays, isActive = @IsActive, dtLastModified = NOW()
                WHERE id = @Id";

            var rowsAffected = _db.Execute(sql, new
            {
                Id = model.Id,
                LeaveCode = model.LeaveCode,
                DateEntitled = dateFields.dateEntitled,
                DateFrom = dateFields.dateFrom,
                DateTo = dateFields.dateTo,
                LeaveDays = model.RemainingBalance,
                IsActive = model.IsActive ? 1 : 0
            });

            if (rowsAffected > 0)
            {
                UpdateLeaveBalance(model.EmployeeNo, model.LeaveCode, model.RemainingBalance, oldBalance, true);

                _auditTrail.Log("e_leave", model.Id.Value, "UPDATED",
                    $"Updated leave setup: {model.LeaveCode} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Leave setup updated successfully!" });
            }

            return Json(new { success = false, message = "Failed to update leave setup." });
        }

        private JsonResult InsertLeaveSetup(LeaveSetupDto model, (string dateEntitled, string dateFrom, string dateTo) dateFields)
        {
            var existingLeave = _db.QueryFirstOrDefault<EmployeeLeaveModel>(
                "SELECT * FROM e_leave WHERE employeeNo = @EmployeeNo AND leaveCode = @LeaveCode AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { EmployeeNo = model.EmployeeNo, LeaveCode = model.LeaveCode });

            if (existingLeave != null)
            {
                return Json(new { success = false, message = "Leave setup already exists for this leave type!" });
            }

            var sql = @"
                INSERT INTO e_leave (employeeNo, isLeave, leaveCode, dateEntitled, dateFrom, dateTo, leaveDays, isAccumulated, isActive, dtAdded)
                VALUES (@EmployeeNo, 1, @LeaveCode, @DateEntitled, @DateFrom, @DateTo, @LeaveDays, 0, @IsActive, NOW());
                SELECT LAST_INSERT_ID();";

            int newId = _db.QuerySingle<int>(sql, new
            {
                EmployeeNo = model.EmployeeNo,
                LeaveCode = model.LeaveCode,
                DateEntitled = dateFields.dateEntitled,
                DateFrom = dateFields.dateFrom,
                DateTo = dateFields.dateTo,
                LeaveDays = model.RemainingBalance,
                IsActive = model.IsActive ? 1 : 0
            });

            if (newId > 0)
            {
                UpdateLeaveBalance(model.EmployeeNo, model.LeaveCode, model.RemainingBalance, 0, false);

                _auditTrail.Log("e_leave", newId, "CREATED",
                    $"Added leave setup: {model.LeaveCode} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Leave setup added successfully!" });
            }

            return Json(new { success = false, message = "Failed to add leave setup." });
        }

        private DateTime CalculateDateTo(DateTime dateFrom, double leaveDays)
        {
            return dateFrom.AddDays(leaveDays - 1);
        }

        private void UpdateLeaveBalance(string employeeNo, string leaveCode, double newBalance, double oldBalance, bool isUpdate)
        {
            try
            {
                var existingBalance = _db.QueryFirstOrDefault<LeaveBalanceModel>(
                    @"SELECT * FROM m_leave 
                      WHERE employeeNo = @EmployeeNo AND leaveCode = @LeaveCode 
                      AND id = (SELECT MAX(id) FROM m_leave WHERE employeeNo = @EmployeeNo AND leaveCode = @LeaveCode)",
                    new { EmployeeNo = employeeNo, LeaveCode = leaveCode });

                var sql = @"
                    INSERT INTO m_leave (employeeNo, rq_leaveID, leaveCode, statusName, beginningBalance, accrual, usedCredits, availableBalance, dateMonth, dateYear, isActive, dtAdded, addedByUser)
                    VALUES (@EmployeeNo, 0, @LeaveCode, 'NEW LEAVE', @BeginningBalance, @Accrual, 0, @AvailableBalance, @DateMonth, @DateYear, 1, NOW(), @AddedByUser )";

                if (isUpdate && existingBalance != null)
                {
                    var accrualDifference = newBalance - oldBalance;
                    _db.Execute(sql, new
                    {
                        EmployeeNo = employeeNo,
                        LeaveCode = leaveCode,
                        BeginningBalance = existingBalance.AvailableBalance,
                        Accrual = accrualDifference,
                        AvailableBalance = newBalance,
                        DateMonth = DateTime.Now.ToString("yyyy -MM"),
                        DateYear = DateTime.Now.Year,
                        AddedByUser = EmployeeNo
                    });
                }
                else
                {
                    _db.Execute(sql, new
                    {
                        EmployeeNo = employeeNo,
                        LeaveCode = leaveCode,
                        BeginningBalance = 0,
                        Accrual = newBalance,
                        AvailableBalance = newBalance,
                        DateMonth = DateTime.Now.ToString("yyyy-MM"),
                        DateYear = DateTime.Now.Year,
                        AddedByUser = EmployeeNo
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateLeaveBalance: {ex.Message}");
                throw new Exception("Error updating leave balance: " + ex.Message);
            }
        }

        private List<LeaveSetupModel> GetLeaveSetupData(string employeeNo, bool isDeleted)
        {
            var whereClause = isDeleted
                ? "WHERE el.employeeNo = @EmployeeNo AND (el.dtDeleted IS NOT NULL AND el.dtDeleted != '0000-00-00 00:00:00')"
                : "WHERE el.employeeNo = @EmployeeNo AND el.isActive = 1 AND (el.dtDeleted IS NULL OR el.dtDeleted = '0000-00-00 00:00:00')";

            var sql = BuildLeaveSetupQuery(whereClause);
            return _db.Query<LeaveSetupModel>(sql, new { EmployeeNo = employeeNo }).ToList();
        }

        private string BuildLeaveSetupQuery(string whereClause)
        {
            return $@"
                SELECT 
                    el.id, el.employeeNo, el.leaveCode, sl.leaveName,
                    DATE_FORMAT(el.dateEntitled, '%Y/%m/%d') as dateEntitled,
                    DATE_FORMAT(el.dateFrom, '%Y/%m/%d') as dateFrom,
                    DATE_FORMAT(el.dateTo, '%Y/%m/%d') as dateTo,
                    el.leaveDays as remainingDays,
                    COALESCE(ml.availableBalance, 0) as remainingBalance,
                    COALESCE(ml.beginningBalance, 0) as beginningBalance,
                    COALESCE(ml.usedCredits, 0) as usedCredits,
                    COALESCE(ml.accrual, 0) as accrual,
                    COALESCE(ml.availableBalance, 0) as availableBalance,
                    el.isActive, el.dtAdded, el.addedByUser,
                    el.dtLastModified, el.lastModifiedByUser,
                    el.dtDeleted, el.deletedByUser
                FROM e_leave el
                LEFT JOIN s_leave sl ON el.leaveCode = sl.leaveCode
                LEFT JOIN (
                    SELECT employeeNo, leaveCode, availableBalance, beginningBalance, usedCredits, accrual
                    FROM m_leave ml1
                    WHERE id = (
                        SELECT MAX(id) FROM m_leave ml2 
                        WHERE ml2.employeeNo = ml1.employeeNo AND ml2.leaveCode = ml1.leaveCode
                    )
                ) ml ON el.employeeNo = ml.employeeNo AND el.leaveCode = ml.leaveCode
                {whereClause}
                ORDER BY 
                    CASE WHEN LOWER(sl.leaveName) LIKE '%maternity%' OR LOWER(sl.leaveName) LIKE '%paternity%' THEN 0 ELSE 1 END, 
                    sl.leaveName ASC";
        }

        private double GetLatestBalance(string employeeNo, string leaveCode)
        {
            var balance = _db.QueryFirstOrDefault<LeaveBalanceModel>(
                @"SELECT availableBalance FROM m_leave 
                  WHERE employeeNo = @EmployeeNo AND leaveCode = @LeaveCode 
                  AND id = (SELECT MAX(id) FROM m_leave WHERE employeeNo = @EmployeeNo AND leaveCode = @LeaveCode)",
                new { EmployeeNo = employeeNo, LeaveCode = leaveCode });

            return balance?.AvailableBalance ?? 0;
        }

        private bool RecordExists(int id)
        {
            var record = _db.QueryFirstOrDefault<EmployeeLeaveModel>(
                "SELECT * FROM e_leave WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = id });

            return record != null;
        }

        private bool IsMaternityOrPaternity(string leaveName)
        {
            return leaveName.ToLower().Contains("maternity") || leaveName.ToLower().Contains("paternity");
        }
    }
}