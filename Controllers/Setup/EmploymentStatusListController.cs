using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SemploymentstatusM")]
    public class EmploymentStatusListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public EmploymentStatusListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/EmploymentStatusList.cshtml");
        }

        [HttpGet]
        public JsonResult GetEmploymentStatusList()
        {
            string sql = @"SELECT id, employmentStatusCode, employmentStatusName 
                          FROM s_employmentstatus 
                          WHERE dtDeleted IS NULL 
                          ORDER BY id DESC";
            var employmentStatuses = _db.Query<EmploymentStatusListModel>(sql).ToList();
            return Json(new { data = employmentStatuses });
        }

        [HttpGet]
        public JsonResult GetEmploymentStatus(int id)
        {
            string sql = @"SELECT id, employmentStatusCode, employmentStatusName 
                          FROM s_employmentstatus 
                          WHERE id = @Id AND isActive = 1";
            var employmentStatus = _db.QueryFirstOrDefault<EmploymentStatusListModel>(sql, new { Id = id });
            return Json(employmentStatus);
        }

        [HttpPost]
        public JsonResult AddEmploymentStatus(EmploymentStatusListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_employmentstatus 
                                   WHERE employmentStatusCode = @employmentStatusCode 
                                   AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { employmentStatusCode = model.employmentStatusCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Employment Status code already exists!" });

                string sql = @"INSERT INTO s_employmentstatus (employmentStatusCode, employmentStatusName, isActive, dtAdded, addedByUser) 
                              VALUES (@employmentStatusCode, @employmentStatusName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    employmentStatusCode = model.employmentStatusCode,
                    employmentStatusName = model.employmentStatusName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_employmentstatus", newId, "CREATED",
                    $"Added employment status: {model.employmentStatusCode} - {model.employmentStatusName}");

                return Json(new { success = true, message = "Employment Status added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding employment status: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateEmploymentStatus(EmploymentStatusListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_employmentstatus 
                                   WHERE id = @id AND dtDeleted IS NULL";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                    return Json(new { success = false, message = "Employment Status record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_employmentstatus 
                                            WHERE employmentStatusCode = @employmentStatusCode 
                                            AND id != @id 
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    employmentStatusCode = model.employmentStatusCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Employment Status code already exists!" });

                string sql = @"UPDATE s_employmentstatus 
                              SET employmentStatusCode = @employmentStatusCode, 
                                  employmentStatusName = @employmentStatusName, 
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    employmentStatusCode = model.employmentStatusCode,
                    employmentStatusName = model.employmentStatusName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_employmentstatus", model.id, "UPDATED",
                    $"Updated employment status: {model.employmentStatusCode} - {model.employmentStatusName}");

                return Json(new { success = true, message = "Employment Status updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating employment status: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteEmploymentStatus(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_employmentstatus 
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_employmentstatus", id, "DELETED",
                    $"Employment status soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Employment Status deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedEmploymentStatusList()
        {
            string sql = @"SELECT id, employmentStatusCode, employmentStatusName 
                          FROM s_employmentstatus 
                          WHERE dtDeleted IS NOT NULL 
                          ORDER BY id DESC";
            var employmentStatuses = _db.Query<EmploymentStatusListModel>(sql).ToList();
            return Json(new { data = employmentStatuses });
        }

        [HttpPost]
        public JsonResult RestoreEmploymentStatus(int id)
        {
            try
            {
                string sql = @"UPDATE s_employmentstatus 
                              SET dtDeleted = NULL, 
                                  isActive = 1,
                                  deletedByUser = NULL,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_employmentstatus", id, "RESTORED", "Employment status restored");

                return Json(new { success = true, message = "Employment Status restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}