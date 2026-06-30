using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("Sreason4terminationM")]
    public class ReasonForSeparationListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public ReasonForSeparationListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/ReasonForSeparationList.cshtml");
        }

        [HttpGet]
        public JsonResult GetReasonForSeparationList()
        {
            string sql = @"SELECT id, reason4TerminationCode, reason4TerminationName
                          FROM s_reason4termination
                          WHERE dtDeleted IS NULL
                          ORDER BY id DESC";
            var reasons = _db.Query<ReasonForSeparationListModel>(sql).ToList();
            return Json(new { data = reasons });
        }

        [HttpGet]
        public JsonResult GetReasonForSeparation(int id)
        {
            string sql = @"SELECT id, reason4TerminationCode, reason4TerminationName
                          FROM s_reason4termination
                          WHERE id = @Id AND isActive = 1";
            var reason = _db.QueryFirstOrDefault<ReasonForSeparationListModel>(sql, new { Id = id });
            return Json(reason);
        }

        [HttpPost]
        public JsonResult AddReasonForSeparation(ReasonForSeparationListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_reason4termination
                                    WHERE reason4TerminationCode = @reason4TerminationCode
                                    AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { reason4TerminationCode = model.reason4TerminationCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Reason for separation code already exists!" });

                string sql = @"INSERT INTO s_reason4termination (reason4TerminationCode, reason4TerminationName, isActive, dtAdded, addedByUser)
                              VALUES (@reason4TerminationCode, @reason4TerminationName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    reason4TerminationCode = model.reason4TerminationCode,
                    reason4TerminationName = model.reason4TerminationName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_reason4termination", newId, "CREATED",
                    $"Added reason for separation: {model.reason4TerminationCode} - {model.reason4TerminationName}");

                return Json(new { success = true, message = "Reason for separation added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding reason for separation: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateReasonForSeparation(ReasonForSeparationListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_reason4termination
                                    WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Reason for separation record not found or has been deleted" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_reason4termination
                                            WHERE reason4TerminationCode = @reason4TerminationCode
                                            AND id != @id
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    reason4TerminationCode = model.reason4TerminationCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Reason for separation code already exists!" });

                string sql = @"UPDATE s_reason4termination
                              SET reason4TerminationCode = @reason4TerminationCode,
                                  reason4TerminationName = @reason4TerminationName,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    reason4TerminationCode = model.reason4TerminationCode,
                    reason4TerminationName = model.reason4TerminationName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_reason4termination", model.id, "UPDATED",
                    $"Updated reason for separation: {model.reason4TerminationCode} - {model.reason4TerminationName}");

                return Json(new { success = true, message = "Reason for separation updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating reason for separation: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteReasonForSeparation(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_reason4termination
                               SET dtDeleted = NOW(), 
                                   isActive = 0,
                                   deletedByUser = @deletedByUser
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_reason4termination", id, "DELETED",
                    $"Reason for separation soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Reason for separation deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedReasonForSeparation()
        {
            string sql = @"SELECT id, reason4TerminationCode, reason4TerminationName
                          FROM s_reason4termination
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY id DESC";
            var reasons = _db.Query<ReasonForSeparationListModel>(sql).ToList();
            return Json(new { data = reasons });
        }

        [HttpPost]
        public JsonResult RestoreReasonForSeparation(int id)
        {
            try
            {
                string sql = @"UPDATE s_reason4termination
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

                _auditTrail.Log("s_reason4termination", id, "RESTORED", "Reason for separation restored");

                return Json(new { success = true, message = "Reason for separation restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}