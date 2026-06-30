using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SdepartmentM")]
    public class DepartmentListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public DepartmentListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/DepartmentList.cshtml");
        }

        [HttpGet]
        public JsonResult GetDepartmentList()
        {
            string sql = @"SELECT id, departmentCode, departmentName 
                          FROM s_department 
                          WHERE dtDeleted IS NULL 
                          ORDER BY id DESC";
            var departments = _db.Query<DepartmentListModel>(sql).ToList();
            return Json(new { data = departments });
        }

        [HttpGet]
        public JsonResult GetDepartment(int id)
        {
            string sql = @"SELECT id, departmentCode, departmentName 
                          FROM s_department 
                          WHERE id = @Id AND isActive = 1";
            var department = _db.QueryFirstOrDefault<DepartmentListModel>(sql, new { Id = id });
            return Json(department);
        }

        [HttpPost]
        public JsonResult AddDepartment(DepartmentListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_department 
                                   WHERE departmentCode = @departmentCode 
                                   AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { departmentCode = model.departmentCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Department code already exists!" });

                string sql = @"INSERT INTO s_department (departmentCode, departmentName, isActive, dtAdded, addedByUser) 
                              VALUES (@departmentCode, @departmentName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    departmentCode = model.departmentCode,
                    departmentName = model.departmentName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_department", newId, "CREATED",
                    $"Added department: {model.departmentCode} - {model.departmentName}");

                return Json(new { success = true, message = "Department added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding department: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateDepartment(DepartmentListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_department 
                                   WHERE id = @id AND dtDeleted IS NULL";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                    return Json(new { success = false, message = "Department record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_department 
                                            WHERE departmentCode = @departmentCode 
                                            AND id != @id 
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    departmentCode = model.departmentCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Department code already exists!" });

                string sql = @"UPDATE s_department 
                              SET departmentCode = @departmentCode, 
                                  departmentName = @departmentName, 
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    departmentCode = model.departmentCode,
                    departmentName = model.departmentName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_department", model.id, "UPDATED",
                    $"Updated department: {model.departmentCode} - {model.departmentName}");

                return Json(new { success = true, message = "Department updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating department: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteDepartment(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_department 
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_department", id, "DELETED",
                    $"Department soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Department deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedDepartmentList()
        {
            string sql = @"SELECT id, departmentCode, departmentName 
                          FROM s_department 
                          WHERE dtDeleted IS NOT NULL 
                          ORDER BY id DESC";
            var departments = _db.Query<DepartmentListModel>(sql).ToList();
            return Json(new { data = departments });
        }

        [HttpPost]
        public JsonResult RestoreDepartment(int id)
        {
            try
            {
                string sql = @"UPDATE s_department 
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

                _auditTrail.Log("s_department", id, "RESTORED", "Department restored");

                return Json(new { success = true, message = "Department restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}