using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SprojectM")]
    public class ProjectListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public ProjectListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/ProjectList.cshtml");
        }

        [HttpGet]
        public JsonResult GetProjectList()
        {
            string sql = @"SELECT id, projectCode, projectName
                          FROM s_project
                          WHERE dtDeleted IS NULL
                          ORDER BY id DESC";
            var project = _db.Query<ProjectListModel>(sql).ToList();
            return Json(new { data = project });
        }

        [HttpGet]
        public JsonResult GetProject(int id)
        {
            string sql = @"SELECT id, projectCode, projectName
                          FROM s_project
                          WHERE id = @Id AND isActive = 1";
            var project = _db.QueryFirstOrDefault<ProjectListModel>(sql, new { Id = id });
            return Json(project);
        }

        [HttpPost]
        public JsonResult AddProject(ProjectListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_project
                                    WHERE projectCode = @projectCode
                                    AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { projectCode = model.projectCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Project code already exists!" });

                string sql = @"INSERT INTO s_project (projectCode, projectName, isActive, dtAdded, addedByUser)
                              VALUES (@projectCode, @projectName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    projectCode = model.projectCode,
                    projectName = model.projectName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_project", newId, "CREATED",
                    $"Added project: {model.projectCode} - {model.projectName}");

                return Json(new { success = true, message = "Project added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding project: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateProject(ProjectListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_project
                                    WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Project record not found or has been deleted" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_project
                                            WHERE projectCode = @projectCode
                                            AND id != @id
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    projectCode = model.projectCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Project code already exists!" });

                string sql = @"UPDATE s_project
                              SET projectCode = @projectCode,
                                  projectName = @projectName,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    projectCode = model.projectCode,
                    projectName = model.projectName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_project", model.id, "UPDATED",
                    $"Updated project: {model.projectCode} - {model.projectName}");

                return Json(new { success = true, message = "Project updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating project: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteProject(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_project
                               SET dtDeleted = NOW(), 
                                   isActive = 0,
                                   deletedByUser = @deletedByUser
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_project", id, "DELETED",
                    $"Project soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Project deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedProject()
        {
            string sql = @"SELECT id, projectCode, projectName
                          FROM s_project
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY id DESC";
            var project = _db.Query<ProjectListModel>(sql).ToList();
            return Json(new { data = project });
        }

        [HttpPost]
        public JsonResult RestoreProject(int id)
        {
            try
            {
                string sql = @"UPDATE s_project
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

                _auditTrail.Log("s_project", id, "RESTORED", "Project restored");

                return Json(new { success = true, message = "Project restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}