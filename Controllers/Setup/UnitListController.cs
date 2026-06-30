using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SunitListM")]
    public class UnitListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public UnitListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/UnitList.cshtml");
        }

        [HttpGet]
        public JsonResult GetUnitList()
        {
            string sql = @"SELECT id, unitCode, unitName
                          FROM s_unit
                          WHERE dtDeleted IS NULL
                          ORDER BY id DESC";
            var unit = _db.Query<UnitListModel>(sql).ToList();
            return Json(new { data = unit });
        }

        [HttpGet]
        public JsonResult GetUnit(int id)
        {
            string sql = @"SELECT id, unitCode, unitName
                          FROM s_unit
                          WHERE id = @Id AND isActive = 1";
            var unit = _db.QueryFirstOrDefault<UnitListModel>(sql, new { Id = id });
            return Json(unit);
        }

        [HttpPost]
        public JsonResult AddUnit(UnitListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_unit
                                    WHERE unitCode = @unitCode
                                    AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { unitCode = model.unitCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Unit code already exists!" });

                string sql = @"INSERT INTO s_unit (unitCode, unitName, isActive, dtAdded, addedByUser)
                              VALUES (@unitCode, @unitName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    unitCode = model.unitCode,
                    unitName = model.unitName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_unit", newId, "CREATED",
                    $"Added unit: {model.unitCode} - {model.unitName}");

                return Json(new { success = true, message = "Unit added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding unit: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateUnit(UnitListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_unit
                                    WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Unit record not found or has been deleted" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_unit
                                            WHERE unitCode = @unitCode
                                            AND id != @id
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    unitCode = model.unitCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Unit code already exists!" });

                string sql = @"UPDATE s_unit
                              SET unitCode = @unitCode,
                                  unitName = @unitName,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    unitCode = model.unitCode,
                    unitName = model.unitName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_unit", model.id, "UPDATED",
                    $"Updated unit: {model.unitCode} - {model.unitName}");

                return Json(new { success = true, message = "Unit updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating unit: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteUnit(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_unit
                               SET dtDeleted = NOW(), 
                                   isActive = 0,
                                   deletedByUser = @deletedByUser
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_unit", id, "DELETED",
                    $"Unit soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Unit deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedUnit()
        {
            string sql = @"SELECT id, unitCode, unitName
                          FROM s_unit
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY id DESC";
            var unit = _db.Query<UnitListModel>(sql).ToList();
            return Json(new { data = unit });
        }

        [HttpPost]
        public JsonResult RestoreUnit(int id)
        {
            try
            {
                string sql = @"UPDATE s_unit
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

                _auditTrail.Log("s_unit", id, "RESTORED", "Unit restored");

                return Json(new { success = true, message = "Unit restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}