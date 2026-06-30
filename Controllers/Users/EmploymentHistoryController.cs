using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Globalization;

namespace KEISAN_HRIS_v2.Controllers.EmploymentHistory
{
    [ModuleAuthorize("FSEmploymentHistoryM")]
    public class EmploymentHistoryController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public EmploymentHistoryController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetEmploymentHistory(string employeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(employeeNo))
                {
                    return PartialView("~/Views/Users/Partials/_EmploymentHistory.cshtml",
                        new List<EmploymentHistoryInfo>());
                }

                var employeeName = _db.QueryFirstOrDefault<string>(
                    @"SELECT CONCAT(lastName, ', ', firstName, ' ', COALESCE(middleName, '')) 
                      FROM e_basicinfo WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                var employmentHistory = GetEmploymentHistoryData(employeeNo, false);

                ViewBag.EmployeeNo = employeeNo;
                ViewBag.EmployeeName = employeeName ?? "Unknown Employee";

                return PartialView("~/Views/Users/Partials/_EmploymentHistory.cshtml", employmentHistory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmploymentHistory: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_EmploymentHistory.cshtml",
                    new List<EmploymentHistoryInfo>());
            }
        }

        [HttpGet]
        public JsonResult GetEmploymentHistoryList(string employeeNo, string isactive)
        {
            try
            {
                // Convert isactive parameter: "2" means all, "1" means active, "0" means inactive
                bool? activeFilter = isactive == "2" ? null : isactive == "1";
                var employmentHistory = GetEmploymentHistoryData(employeeNo, false, activeFilter);
                return Json(new { data = employmentHistory });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmploymentHistoryList: {ex.Message}");
                return Json(new { data = new List<EmploymentHistoryInfo>() });
            }
        }

        [HttpGet]
        public JsonResult GetEmploymentHistoryById(int id)
        {
            try
            {
                var sql = BuildEmploymentHistoryQuery("WHERE eh.id = @Id");
                var employmentHistory = _db.QueryFirstOrDefault<EmploymentHistoryInfo>(sql, new { Id = id });

                return employmentHistory != null
                    ? Json(new { success = true, data = employmentHistory })
                    : Json(new { success = false, message = "Employment history record not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmploymentHistoryById: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving employment history: " + ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedEmploymentHistory(string employeeNo)
        {
            try
            {
                var employmentHistory = GetEmploymentHistoryData(employeeNo, true);
                return Json(new { data = employmentHistory });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeletedEmploymentHistory: {ex.Message}");
                return Json(new { data = new List<EmploymentHistoryInfo>() });
            }
        }

        [HttpPost]
        public JsonResult SaveEmploymentHistory([FromBody] EmploymentHistoryDto model)
        {
            try
            {
                if (!ValidateEmploymentHistory(model, out string validationMessage))
                {
                    return Json(new { success = false, message = validationMessage });
                }

                if (!ProcessDates(model, out DateTime fromDate, out DateTime toDate, out string dateError))
                {
                    return Json(new { success = false, message = dateError });
                }

                // Validate date range
                if (toDate < fromDate)
                {
                    return Json(new { success = false, message = "End date cannot be earlier than start date." });
                }

                if (model.Id.HasValue && model.Id > 0)
                {
                    return UpdateEmploymentHistory(model, fromDate, toDate);
                }
                else
                {
                    return InsertEmploymentHistory(model, fromDate, toDate);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveEmploymentHistory: {ex.Message}");
                return Json(new { success = false, message = "Error saving employment history: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult InactiveEmploymentHistory(int id, string remarks = "")
        {
            try
            {
                if (!RecordExists(id))
                {
                    return Json(new { success = false, message = "Employment history record not found or already deleted!" });
                }

                var sql = @"
                    UPDATE e_employmenthistory 
                    SET dtDeleted = NOW(), 
                        isActive = 0, 
                        deletedByUser = @DeletedByUser
                    WHERE id = @Id";

                var parameters = new
                {
                    Id = id,
                    DeletedByUser = EmployeeNo
                };

                var rowsAffected = _db.Execute(sql, parameters);

                if (rowsAffected > 0)
                {
                    var auditMessage = string.IsNullOrWhiteSpace(remarks)
                        ? "Employment history soft deleted"
                        : $"Employment history soft deleted. Reason: {remarks}";

                    _auditTrail.Log("e_employmenthistory", id, "DELETED", auditMessage);
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Employment history deleted successfully!" })
                    : Json(new { success = false, message = "Failed to delete employment history." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InactiveEmploymentHistory: {ex.Message}");
                return Json(new { success = false, message = "Error deleting employment history: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreEmploymentHistory(int id)
        {
            try
            {
                var existingRecord = _db.QueryFirstOrDefault<EmploymentHistoryInfo>(
                    "SELECT * FROM e_employmenthistory WHERE id = @Id AND (dtDeleted IS NOT NULL AND dtDeleted != '0000-00-00 00:00:00')",
                    new { Id = id });

                if (existingRecord == null)
                {
                    return Json(new { success = false, message = "Employment history record not found or not deleted!" });
                }

                var sql = @"
                    UPDATE e_employmenthistory 
                    SET dtDeleted = NULL, 
                        deletedByUser = NULL, 
                        isActive = 1, 
                        dtLastModified = NOW()
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new { Id = id });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_employmenthistory", id, "RESTORED", "Employment history restored");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Employment history restored successfully!" })
                    : Json(new { success = false, message = "Failed to restore employment history." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreEmploymentHistory: {ex.Message}");
                return Json(new { success = false, message = "Error restoring employment history: " + ex.Message });
            }
        }

        // HELPER METHODS

        private bool ValidateEmploymentHistory(EmploymentHistoryDto model, out string message)
        {
            message = string.Empty;

            if (model == null)
            {
                message = "Invalid data provided.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.EmployeeNo))
            {
                message = "Employee number is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.CompanyName))
            {
                message = "Company name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.Position))
            {
                message = "Position is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.FromDate))
            {
                message = "From date is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.ToDate))
            {
                message = "To date is required.";
                return false;
            }

            return true;
        }

        private bool ProcessDates(EmploymentHistoryDto model, out DateTime fromDate, out DateTime toDate, out string errorMessage)
        {
            fromDate = DateTime.MinValue;
            toDate = DateTime.MinValue;
            errorMessage = string.Empty;

            // Try multiple date formats
            string[] formats = { "yyyy/MM/dd", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy" };

            if (!DateTime.TryParseExact(model.FromDate, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out fromDate))
            {
                errorMessage = "Invalid from date format. Expected format: yyyy/MM/dd";
                return false;
            }

            if (!DateTime.TryParseExact(model.ToDate, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out toDate))
            {
                errorMessage = "Invalid to date format. Expected format: yyyy/MM/dd";
                return false;
            }

            return true;
        }

        private JsonResult UpdateEmploymentHistory(EmploymentHistoryDto model, DateTime fromDate, DateTime toDate)
        {
            var existingRecord = _db.QueryFirstOrDefault<EmploymentHistoryInfo>(
                "SELECT * FROM e_employmenthistory WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = model.Id });

            if (existingRecord == null)
            {
                return Json(new { success = false, message = "Employment history record not found or has been deleted!" });
            }

            var sql = @"
                UPDATE e_employmenthistory
                SET companyName = @CompanyName,
                    position = @Position,
                    address = @Address,
                    fromDate = @FromDate,
                    toDate = @ToDate,
                    JOBDESC = @JobDesc,
                    REMARKS = @Remarks,
                    dtLastModified = NOW(),
                    lastModifiedByUser = @ModifiedByUser
                WHERE id = @Id";

            var rowsAffected = _db.Execute(sql, new
            {
                Id = model.Id,
                CompanyName = model.CompanyName,
                Position = model.Position,
                Address = model.Address ?? string.Empty,
                FromDate = fromDate.ToString("yyyy-MM-dd"),
                ToDate = toDate.ToString("yyyy-MM-dd"),
                JobDesc = model.JobDesc ?? string.Empty,
                Remarks = model.Remarks ?? string.Empty,
                ModifiedByUser = EmployeeNo
            });

            if (rowsAffected > 0)
            {
                _auditTrail.Log("e_employmenthistory", model.Id.Value, "UPDATED",
                    $"Updated employment history: {model.CompanyName} - {model.Position} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Employment history updated successfully!" });
            }

            return Json(new { success = false, message = "Failed to update employment history." });
        }

        private JsonResult InsertEmploymentHistory(EmploymentHistoryDto model, DateTime fromDate, DateTime toDate)
        {
            var sql = @"
                INSERT INTO e_employmenthistory (
                    employeeNo, companyName, position, address, fromDate, toDate, 
                    JOBDESC, REMARKS, isActive, dtAdded, addedByUser
                )
                VALUES (
                    @EmployeeNo, @CompanyName, @Position, @Address, @FromDate, @ToDate,
                    @JobDesc, @Remarks, 1, NOW(), @AddedByUser
                );
                SELECT LAST_INSERT_ID();";

            int newId = _db.QuerySingle<int>(sql, new
            {
                EmployeeNo = model.EmployeeNo,
                CompanyName = model.CompanyName,
                Position = model.Position,
                Address = model.Address ?? string.Empty,
                FromDate = fromDate.ToString("yyyy-MM-dd"),
                ToDate = toDate.ToString("yyyy-MM-dd"),
                JobDesc = model.JobDesc ?? string.Empty,
                Remarks = model.Remarks ?? string.Empty,
                AddedByUser = EmployeeNo
            });

            if (newId > 0)
            {
                _auditTrail.Log("e_employmenthistory", newId, "CREATED",
                    $"Added employment history: {model.CompanyName} - {model.Position} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Employment history added successfully!" });
            }

            return Json(new { success = false, message = "Failed to add employment history." });
        }

        private List<EmploymentHistoryInfo> GetEmploymentHistoryData(string employeeNo, bool isDeleted, bool? isActiveFilter = null)
        {
            string whereClause;

            if (isDeleted)
            {
                whereClause = "WHERE eh.employeeNo = @EmployeeNo AND (eh.dtDeleted IS NOT NULL AND eh.dtDeleted != '0000-00-00 00:00:00')";
            }
            else if (isActiveFilter.HasValue)
            {
                whereClause = isActiveFilter.Value
                    ? "WHERE eh.employeeNo = @EmployeeNo AND eh.isActive = 1 AND (eh.dtDeleted IS NULL OR eh.dtDeleted = '0000-00-00 00:00:00')"
                    : "WHERE eh.employeeNo = @EmployeeNo AND eh.isActive = 0 AND (eh.dtDeleted IS NULL OR eh.dtDeleted = '0000-00-00 00:00:00')";
            }
            else
            {
                whereClause = "WHERE eh.employeeNo = @EmployeeNo AND (eh.dtDeleted IS NULL OR eh.dtDeleted = '0000-00-00 00:00:00')";
            }

            var sql = BuildEmploymentHistoryQuery(whereClause);
            return _db.Query<EmploymentHistoryInfo>(sql, new { EmployeeNo = employeeNo }).ToList();
        }

        private string BuildEmploymentHistoryQuery(string whereClause)
        {
            return $@"
                SELECT 
                    eh.id,
                    eh.employeeNo, 
                    eh.companyName,
                    eh.position,
                    DATE_FORMAT(eh.fromDate, '%Y/%m/%d') AS fromDate, 
                    DATE_FORMAT(eh.toDate, '%Y/%m/%d') AS toDate,
                    eh.address,
                    eh.JOBDESC,
                    eh.REMARKS,
                    eh.isActive,
                    DATE_FORMAT(eh.dtAdded, '%Y/%m/%d') AS dtAdded, 
                    CONCAT(COALESCE(u.lastName, ''), ', ', COALESCE(u.firstName, '')) AS addedByUser,
                    eh.dtLastModified,
                    eh.lastModifiedByUser,
                    eh.dtDeleted,
                    eh.deletedByUser
                FROM e_employmenthistory eh
                LEFT JOIN s_user u ON u.userCode = eh.addedByUser
                {whereClause}
                ORDER BY eh.fromDate DESC, eh.id DESC";
        }

        private bool RecordExists(int id)
        {
            var record = _db.QueryFirstOrDefault<EmploymentHistoryInfo>(
                "SELECT * FROM e_employmenthistory WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = id });

            return record != null;
        }
    }
}