using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SbranchM")]
    public class BranchListController : BaseController // ← changed
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public BranchListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/BranchList.cshtml");
        }

        [HttpGet]
        public JsonResult GetBranchList()
        {
            string sql = @"SELECT id, branchCode, branchName
                          FROM s_branch
                          WHERE dtDeleted IS NULL
                          ORDER BY id DESC";
            var branch = _db.Query<BranchListModel>(sql).ToList();
            return Json(new { data = branch });
        }

        [HttpGet]
        public JsonResult GetBranch(int id)
        {
            string sql = @"SELECT id, branchCode, branchName
                          FROM s_branch
                          WHERE id = @Id AND isActive = 1";
            var branch = _db.QueryFirstOrDefault<BranchListModel>(sql, new { Id = id });
            return Json(branch);
        }

        [HttpPost]
        public JsonResult AddBranch(BranchListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_branch
                                    WHERE branchCode = @branchCode
                                    AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { branchCode = model.branchCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Branch code already exists!" });

                string sql = @"INSERT INTO s_branch (branchCode, branchName, isActive, dtAdded, addedByUser)
                              VALUES (@branchCode, @branchName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    branchCode = model.branchCode,
                    branchName = model.branchName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_branch", newId, "CREATED",
                    $"Added branch: {model.branchCode} - {model.branchName}");

                return Json(new { success = true, message = "Branch added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding branch: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateBranch(BranchListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_branch
                                    WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Branch record not found or has been deleted" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_branch
                                            WHERE branchCode = @branchCode
                                            AND id != @id
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    branchCode = model.branchCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Branch code already exists!" });

                string sql = @"UPDATE s_branch
                              SET branchCode = @branchCode,
                                  branchName = @branchName,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    branchCode = model.branchCode,
                    branchName = model.branchName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_branch", model.id, "UPDATED",
                    $"Updated branch: {model.branchCode} - {model.branchName}");

                return Json(new { success = true, message = "Branch updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating branch: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteBranch(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_branch
                               SET dtDeleted = NOW(), 
                                   isActive = 0,
                                   deletedByUser = @deletedByUser
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_branch", id, "DELETED",
                    $"Branch soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Branch deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedBranch()
        {
            string sql = @"SELECT id, branchCode, branchName
                          FROM s_branch
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY id DESC";
            var branch = _db.Query<BranchListModel>(sql).ToList();
            return Json(new { data = branch });
        }

        [HttpPost]
        public JsonResult RestoreBranch(int id)
        {
            try
            {
                string sql = @"UPDATE s_branch
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

                _auditTrail.Log("s_branch", id, "RESTORED", "Branch restored");

                return Json(new { success = true, message = "Branch restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}