using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SleaveM")]
    public class LeaveListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public LeaveListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/LeaveList.cshtml");
        }

        [HttpGet]
        public JsonResult GetLeaveList()
        {
            string sql = @"SELECT id, leaveCode, leaveName, leaveCredits 
                          FROM s_leave 
                          WHERE dtDeleted IS NULL 
                          ORDER BY id DESC";
            var leaves = _db.Query<LeaveListModel>(sql).ToList();
            return Json(new { data = leaves });
        }

        [HttpGet]
        public JsonResult GetLeave(int id)
        {
            string sql = @"SELECT id, leaveCode, leaveName, leaveCredits 
                          FROM s_leave 
                          WHERE id = @Id AND isActive = 1";
            var leave = _db.QueryFirstOrDefault<LeaveListModel>(sql, new { Id = id });
            return Json(leave);
        }

        [HttpPost]
        public JsonResult AddLeave(LeaveListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_leave 
                                   WHERE leaveCode = @leaveCode 
                                   AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { leaveCode = model.leaveCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Leave code already exists!" });

                string sql = @"INSERT INTO s_leave (leaveCode, leaveName, leaveCredits, isActive, dtAdded, addedByUser) 
                              VALUES (@leaveCode, @leaveName, @leaveCredits, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    leaveCode = model.leaveCode,
                    leaveName = model.leaveName,
                    leaveCredits = model.leaveCredits,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_leave", newId, "CREATED",
                    $"Added leave: {model.leaveCode} - {model.leaveName}");

                return Json(new { success = true, message = "Leave added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding leave: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateLeave(LeaveListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_leave 
                                   WHERE id = @id AND dtDeleted IS NULL";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                    return Json(new { success = false, message = "Leave record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_leave 
                                            WHERE leaveCode = @leaveCode 
                                            AND id != @id 
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    leaveCode = model.leaveCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Leave code already exists!" });

                string sql = @"UPDATE s_leave 
                              SET leaveCode = @leaveCode, 
                                  leaveName = @leaveName,
                                  leaveCredits = @leaveCredits, 
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    leaveCode = model.leaveCode,
                    leaveName = model.leaveName,
                    leaveCredits = model.leaveCredits,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_leave", model.id, "UPDATED",
                    $"Updated leave: {model.leaveCode} - {model.leaveName}");

                return Json(new { success = true, message = "Leave updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating leave: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteLeave(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_leave 
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_leave", id, "DELETED",
                    $"Leave soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Leave deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedLeaveList()
        {
            string sql = @"SELECT id, leaveCode, leaveName, leaveCredits 
                          FROM s_leave 
                          WHERE dtDeleted IS NOT NULL 
                          ORDER BY id DESC";
            var leaves = _db.Query<LeaveListModel>(sql).ToList();
            return Json(new { data = leaves });
        }

        [HttpPost]
        public JsonResult RestoreLeave(int id)
        {
            try
            {
                string sql = @"UPDATE s_leave 
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

                _auditTrail.Log("s_leave", id, "RESTORED", "Leave restored");

                return Json(new { success = true, message = "Leave restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}