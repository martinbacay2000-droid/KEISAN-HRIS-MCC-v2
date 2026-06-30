using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers
{
    [ModuleAuthorize("SadjustmentM")]
    public class AdjustmentListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public AdjustmentListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/AdjustmentList.cshtml");
        }

        [HttpGet]
        public JsonResult GetAdjustmentList()
        {
            string sql = @"SELECT id, adjustmentCode, adjustmentName, isTaxable 
                          FROM s_adjustment 
                          WHERE dtDeleted IS NULL 
                          ORDER BY id DESC";
            var adjustments = _db.Query<AdjustmentListModel>(sql).ToList();
            return Json(new { data = adjustments });
        }

        [HttpGet]
        public JsonResult GetAdjustment(int id)
        {
            string sql = @"SELECT id, adjustmentCode, adjustmentName, isTaxable 
                          FROM s_adjustment 
                          WHERE id = @Id AND isActive = 1";
            var adjustment = _db.QueryFirstOrDefault<AdjustmentListModel>(sql, new { Id = id });
            return Json(adjustment);
        }

        [HttpPost]
        public JsonResult AddAdjustment(AdjustmentListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_adjustment 
                                   WHERE adjustmentCode = @adjustmentCode 
                                   AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { adjustmentCode = model.adjustmentCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Adjustment code already exists!" });

                string sql = @"INSERT INTO s_adjustment (adjustmentCode, adjustmentName, isActive, isTaxable, dtAdded, addedByUser) 
                              VALUES (@adjustmentCode, @adjustmentName, 1, @isTaxable, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    adjustmentCode = model.adjustmentCode,
                    adjustmentName = model.adjustmentName,
                    isTaxable = model.isTaxable,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_adjustment", newId, "CREATED",
                    $"Added adjustment: {model.adjustmentCode} - {model.adjustmentName}");

                return Json(new { success = true, message = "Adjustment added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding adjustment: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateAdjustment(AdjustmentListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_adjustment 
                                   WHERE id = @id AND dtDeleted IS NULL";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                    return Json(new { success = false, message = "Adjustment record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_adjustment 
                                            WHERE adjustmentCode = @adjustmentCode 
                                            AND id != @id 
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    adjustmentCode = model.adjustmentCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Adjustment code already exists!" });

                string sql = @"UPDATE s_adjustment 
                              SET adjustmentCode = @adjustmentCode, 
                                  adjustmentName = @adjustmentName, 
                                  isTaxable = @isTaxable,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    adjustmentCode = model.adjustmentCode,
                    adjustmentName = model.adjustmentName,
                    isTaxable = model.isTaxable,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_adjustment", model.id, "UPDATED",
                    $"Updated adjustment: {model.adjustmentCode} - {model.adjustmentName}");

                return Json(new { success = true, message = "Adjustment updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating adjustment: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteAdjustment(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_adjustment 
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_adjustment", id, "DELETED",
                    $"Adjustment soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Adjustment deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedAdjustmentList()
        {
            string sql = @"SELECT id, adjustmentCode, adjustmentName, isTaxable 
                          FROM s_adjustment 
                          WHERE dtDeleted IS NOT NULL 
                          ORDER BY id DESC";
            var adjustments = _db.Query<AdjustmentListModel>(sql).ToList();
            return Json(new { data = adjustments });
        }

        [HttpPost]
        public JsonResult RestoreAdjustment(int id)
        {
            try
            {
                string sql = @"UPDATE s_adjustment 
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

                _auditTrail.Log("s_adjustment", id, "RESTORED", "Adjustment restored");

                return Json(new { success = true, message = "Adjustment restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}