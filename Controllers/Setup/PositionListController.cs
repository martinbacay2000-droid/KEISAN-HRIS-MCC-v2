using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SpositionM")]
    public class PositionListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public PositionListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/PositionList.cshtml");
        }

        [HttpGet]
        public JsonResult GetPositionList()
        {
            string sql = @"SELECT id, positionCode, positionName
                          FROM s_position
                          WHERE dtDeleted IS NULL
                          ORDER BY id DESC";
            var positions = _db.Query<PositionListModel>(sql).ToList();
            return Json(new { data = positions });
        }

        [HttpGet]
        public JsonResult GetPosition(int id)
        {
            string sql = @"SELECT id, positionCode, positionName
                          FROM s_position
                          WHERE id = @Id AND isActive = 1";
            var position = _db.QueryFirstOrDefault<PositionListModel>(sql, new { Id = id });
            return Json(position);
        }

        [HttpPost]
        public JsonResult AddPosition(PositionListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_position
                                    WHERE positionCode = @positionCode
                                    AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { positionCode = model.positionCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Position code already exists!" });

                string sql = @"INSERT INTO s_position (positionCode, positionName, isActive, dtAdded, addedByUser)
                              VALUES (@positionCode, @positionName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    positionCode = model.positionCode,
                    positionName = model.positionName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_position", newId, "CREATED",
                    $"Added position: {model.positionCode} - {model.positionName}");

                return Json(new { success = true, message = "Position added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding position: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdatePosition(PositionListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_position
                                    WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Position record not found or has been deleted" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_position
                                            WHERE positionCode = @positionCode
                                            AND id != @id
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    positionCode = model.positionCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Position code already exists!" });

                string sql = @"UPDATE s_position
                              SET positionCode = @positionCode,
                                  positionName = @positionName,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    positionCode = model.positionCode,
                    positionName = model.positionName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_position", model.id, "UPDATED",
                    $"Updated position: {model.positionCode} - {model.positionName}");

                return Json(new { success = true, message = "Position updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating position: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeletePosition(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_position
                               SET dtDeleted = NOW(), 
                                   isActive = 0,
                                   deletedByUser = @deletedByUser
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_position", id, "DELETED",
                    $"Position soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Position deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedPosition()
        {
            string sql = @"SELECT id, positionCode, positionName
                          FROM s_position
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY id DESC";
            var positions = _db.Query<PositionListModel>(sql).ToList();
            return Json(new { data = positions });
        }

        [HttpPost]
        public JsonResult RestorePosition(int id)
        {
            try
            {
                string sql = @"UPDATE s_position
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

                _auditTrail.Log("s_position", id, "RESTORED", "Position restored");

                return Json(new { success = true, message = "Position restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}