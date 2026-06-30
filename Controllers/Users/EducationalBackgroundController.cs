using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Globalization;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FSEducationalBackgroundM")]
    public class EducationalBackgroundController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public EducationalBackgroundController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetEducationalBackground(string employeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(employeeNo))
                {
                    return PartialView("~/Views/Users/Partials/_EducationalBackground.cshtml",
                        new List<EducationalBackgroundInfo>());
                }

                var employeeName = _db.QueryFirstOrDefault<string>(
                    @"SELECT CONCAT(lastName, ', ', firstName, ' ', COALESCE(middleName, '')) 
                      FROM e_basicinfo WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                var educationalBackground = GetEducationalBackgroundData(employeeNo, false);

                ViewBag.EmployeeNo = employeeNo;
                ViewBag.EmployeeName = employeeName ?? "Unknown Employee";

                return PartialView("~/Views/Users/Partials/_EducationalBackground.cshtml", educationalBackground);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEducationalBackground: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_EducationalBackground.cshtml",
                    new List<EducationalBackgroundInfo>());
            }
        }

        [HttpGet]
        public JsonResult GetEducationalBackgroundList(string employeeNo, string isactive)
        {
            try
            {
                // Convert isactive parameter: "2" means all, "1" means active, "0" means inactive
                bool? activeFilter = isactive == "2" ? null : isactive == "1";
                var educationalBackground = GetEducationalBackgroundData(employeeNo, false, activeFilter);
                return Json(new { data = educationalBackground });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEducationalBackgroundList: {ex.Message}");
                return Json(new { data = new List<EducationalBackgroundInfo>() });
            }
        }

        [HttpGet]
        public JsonResult GetEducationalBackgroundById(int id)
        {
            try
            {
                var sql = BuildEducationalBackgroundQuery("WHERE e.id = @Id");
                var educationalBackground = _db.QueryFirstOrDefault<EducationalBackgroundInfo>(sql, new { Id = id });

                return educationalBackground != null
                    ? Json(new { success = true, data = educationalBackground })
                    : Json(new { success = false, message = "Educational background record not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEducationalBackgroundById: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving educational background: " + ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedEducationalBackground(string employeeNo)
        {
            try
            {
                var educationalBackground = GetEducationalBackgroundData(employeeNo, true);
                return Json(new { data = educationalBackground });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeletedEducationalBackground: {ex.Message}");
                return Json(new { data = new List<EducationalBackgroundInfo>() });
            }
        }

        [HttpPost]
        public JsonResult SaveEducationalBackground([FromBody] EducationalBackgroundDto model)
        {
            try
            {
                if (!ValidateEducationalBackground(model, out string validationMessage))
                {
                    return Json(new { success = false, message = validationMessage });
                }

                if (model.Id.HasValue && model.Id > 0)
                {
                    return UpdateEducationalBackground(model);
                }
                else
                {
                    return InsertEducationalBackground(model);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveEducationalBackground: {ex.Message}");
                return Json(new { success = false, message = "Error saving educational background: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult InactiveEducationalBackground(int id, string remarks = "")
        {
            try
            {
                if (!RecordExists(id))
                {
                    return Json(new { success = false, message = "Educational background record not found or already deleted!" });
                }

                var sql = @"
                    UPDATE e_school 
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
                        ? "Educational background soft deleted"
                        : $"Educational background soft deleted. Reason: {remarks}";

                    _auditTrail.Log("e_school", id, "DELETED", auditMessage);
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Educational background deleted successfully!" })
                    : Json(new { success = false, message = "Failed to delete educational background." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InactiveEducationalBackground: {ex.Message}");
                return Json(new { success = false, message = "Error deleting educational background: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreEducationalBackground(int id)
        {
            try
            {
                var existingRecord = _db.QueryFirstOrDefault<EducationalBackgroundInfo>(
                    "SELECT * FROM e_school WHERE id = @Id AND (dtDeleted IS NOT NULL AND dtDeleted != '0000-00-00 00:00:00')",
                    new { Id = id });

                if (existingRecord == null)
                {
                    return Json(new { success = false, message = "Educational background record not found or not deleted!" });
                }

                var sql = @"
                    UPDATE e_school 
                    SET dtDeleted = NULL, 
                        deletedByUser = NULL, 
                        isActive = 1, 
                        dtLastModified = NOW()
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new { Id = id });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_school", id, "RESTORED", "Educational background restored");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Educational background restored successfully!" })
                    : Json(new { success = false, message = "Failed to restore educational background." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreEducationalBackground: {ex.Message}");
                return Json(new { success = false, message = "Error restoring educational background: " + ex.Message });
            }
        }

        // HELPER METHODS

        private bool ValidateEducationalBackground(EducationalBackgroundDto model, out string message)
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

            if (string.IsNullOrWhiteSpace(model.NameOfSchool))
            {
                message = "School name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.SchoolType))
            {
                message = "School type is required.";
                return false;
            }

            return true;
        }

        private JsonResult UpdateEducationalBackground(EducationalBackgroundDto model)
        {
            var existingRecord = _db.QueryFirstOrDefault<EducationalBackgroundInfo>(
                "SELECT * FROM e_school WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = model.Id });

            if (existingRecord == null)
            {
                return Json(new { success = false, message = "Educational background record not found or has been deleted!" });
            }

            var sql = @"
                UPDATE e_school
                SET nameOfSchool = @NameOfSchool,
                    schoolType = @SchoolType,
                    course = @Course,
                    yearGraduated = @YearGraduated,
                    unitsEarned = @UnitsEarned,
                    schoolAddress = @SchoolAddress,
                    attain = @Attain,
                    dtLastModified = NOW(),
                    lastModifiedByUser = @ModifiedByUser
                WHERE id = @Id";

            var rowsAffected = _db.Execute(sql, new
            {
                Id = model.Id,
                NameOfSchool = model.NameOfSchool,
                SchoolType = model.SchoolType,
                Course = model.Course ?? string.Empty,
                YearGraduated = model.YearGraduated ?? string.Empty,
                UnitsEarned = model.UnitsEarned ?? 0,
                SchoolAddress = model.SchoolAddress ?? string.Empty,
                Attain = model.Attain ?? string.Empty,
                ModifiedByUser = EmployeeNo
            });

            if (rowsAffected > 0)
            {
                _auditTrail.Log("e_school", model.Id.Value, "UPDATED",
                    $"Updated educational background: {model.NameOfSchool} - {model.SchoolType} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Educational background updated successfully!" });
            }

            return Json(new { success = false, message = "Failed to update educational background." });
        }

        private JsonResult InsertEducationalBackground(EducationalBackgroundDto model)
        {
            var sql = @"
                INSERT INTO e_school (
                    employeeNo, nameOfSchool, schoolType, course, yearGraduated, 
                    unitsEarned, schoolAddress, attain, isActive, dtAdded, addedByUser
                )
                VALUES (
                    @EmployeeNo, @NameOfSchool, @SchoolType, @Course, @YearGraduated,
                    @UnitsEarned, @SchoolAddress, @Attain, 1, NOW(), @AddedByUser
                );
                SELECT LAST_INSERT_ID();";

            int newId = _db.QuerySingle<int>(sql, new
            {
                EmployeeNo = model.EmployeeNo,
                NameOfSchool = model.NameOfSchool,
                SchoolType = model.SchoolType,
                Course = model.Course ?? string.Empty,
                YearGraduated = model.YearGraduated ?? string.Empty,
                UnitsEarned = model.UnitsEarned ?? 0,
                SchoolAddress = model.SchoolAddress ?? string.Empty,
                Attain = model.Attain ?? string.Empty,
                AddedByUser = EmployeeNo
            });

            if (newId > 0)
            {
                _auditTrail.Log("e_school", newId, "CREATED",
                    $"Added educational background: {model.NameOfSchool} - {model.SchoolType} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Educational background added successfully!" });
            }

            return Json(new { success = false, message = "Failed to add educational background." });
        }

        private List<EducationalBackgroundInfo> GetEducationalBackgroundData(string employeeNo, bool isDeleted, bool? isActiveFilter = null)
        {
            string whereClause;

            if (isDeleted)
            {
                whereClause = "WHERE e.employeeNo = @EmployeeNo AND (e.dtDeleted IS NOT NULL AND e.dtDeleted != '0000-00-00 00:00:00')";
            }
            else if (isActiveFilter.HasValue)
            {
                whereClause = isActiveFilter.Value
                    ? "WHERE e.employeeNo = @EmployeeNo AND e.isActive = 1 AND (e.dtDeleted IS NULL OR e.dtDeleted = '0000-00-00 00:00:00')"
                    : "WHERE e.employeeNo = @EmployeeNo AND e.isActive = 0 AND (e.dtDeleted IS NULL OR e.dtDeleted = '0000-00-00 00:00:00')";
            }
            else
            {
                whereClause = "WHERE e.employeeNo = @EmployeeNo AND (e.dtDeleted IS NULL OR e.dtDeleted = '0000-00-00 00:00:00')";
            }

            var sql = BuildEducationalBackgroundQuery(whereClause);
            return _db.Query<EducationalBackgroundInfo>(sql, new { EmployeeNo = employeeNo }).ToList();
        }

        private string BuildEducationalBackgroundQuery(string whereClause)
        {
            return $@"
                SELECT 
                    e.id,
                    e.employeeNo, 
                    e.nameOfSchool,
                    e.schoolType,
                    e.course,
                    e.yearGraduated,
                    IFNULL(e.unitsEarned, 0) AS unitsEarned,
                    e.schoolAddress,
                    e.attain,
                    e.isActive,
                    DATE_FORMAT(e.dtAdded, '%Y/%m/%d') AS dtAdded, 
                    CONCAT(COALESCE(u.lastName, ''), ', ', COALESCE(u.firstName, '')) AS addedByUser,
                    e.dtLastModified,
                    e.lastModifiedByUser,
                    e.dtDeleted,
                    e.deletedByUser
                FROM e_school e
                LEFT JOIN s_user u ON u.userCode = e.addedByUser
                {whereClause}
                ORDER BY e.yearGraduated DESC, e.id DESC";
        }

        private bool RecordExists(int id)
        {
            var record = _db.QueryFirstOrDefault<EducationalBackgroundInfo>(
                "SELECT * FROM e_school WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = id });

            return record != null;
        }
    }
}