using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SCOEPurposeListM")]
    public class COEPurposeListController : BaseController // ← changed
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public COEPurposeListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/COEPurposeList.cshtml");
        }

        [HttpGet]
        public JsonResult GetCOEPurposeList()
        {
            string sql = @"SELECT id, coeCode, coeName
                          FROM s_coe
                          WHERE dtDeleted IS NULL
                          ORDER BY id DESC";
            var coePurposes = _db.Query<COEPurposeListModel>(sql).ToList();
            return Json(new { data = coePurposes });
        }

        [HttpGet]
        public JsonResult GetCOEPurpose(int id)
        {
            string sql = @"SELECT id, coeCode, coeName
                          FROM s_coe
                          WHERE id = @Id AND isActive = 1";
            var coePurpose = _db.QueryFirstOrDefault<COEPurposeListModel>(sql, new { Id = id });
            return Json(coePurpose);
        }

        [HttpPost]
        public JsonResult AddCOEPurpose(COEPurposeListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_coe
                                    WHERE coeCode = @coeCode
                                    AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { coeCode = model.coeCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "COE code already exists!" });

                string sql = @"INSERT INTO s_coe (coeCode, coeName, isActive, dtAdded, addedByUser)
                              VALUES (@coeCode, @coeName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    coeCode = model.coeCode,
                    coeName = model.coeName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_coe", newId, "CREATED",
                    $"Added COE purpose: {model.coeCode} - {model.coeName}");

                return Json(new { success = true, message = "COE purpose added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding COE purpose: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateCOEPurpose(COEPurposeListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_coe
                                    WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "COE purpose record not found or has been deleted" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_coe
                                            WHERE coeCode = @coeCode
                                            AND id != @id
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    coeCode = model.coeCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "COE code already exists!" });

                string sql = @"UPDATE s_coe
                              SET coeCode = @coeCode,
                                  coeName = @coeName,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    coeCode = model.coeCode,
                    coeName = model.coeName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_coe", model.id, "UPDATED",
                    $"Updated COE purpose: {model.coeCode} - {model.coeName}");

                return Json(new { success = true, message = "COE purpose updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating COE purpose: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteCOEPurpose(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_coe
                               SET dtDeleted = NOW(), 
                                   isActive = 0,
                                   deletedByUser = @deletedByUser
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_coe", id, "DELETED",
                    $"COE purpose soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "COE purpose deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedCOEPurpose()
        {
            string sql = @"SELECT id, coeCode, coeName
                          FROM s_coe
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY id DESC";
            var coePurposes = _db.Query<COEPurposeListModel>(sql).ToList();
            return Json(new { data = coePurposes });
        }

        [HttpPost]
        public JsonResult RestoreCOEPurpose(int id)
        {
            try
            {
                string sql = @"UPDATE s_coe
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

                _auditTrail.Log("s_coe", id, "RESTORED", "COE purpose restored");

                return Json(new { success = true, message = "COE purpose restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}