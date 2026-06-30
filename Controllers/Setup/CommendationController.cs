using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("ScommendationM")]
    public class CommendationController : BaseController // ← changed
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public CommendationController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/Commendation.cshtml");
        }

        [HttpGet]
        public JsonResult GetCommendationList()
        {
            string sql = @"SELECT id, commendationCode, commendationName
                          FROM s_commendation
                          WHERE isActive = 1
                          ORDER BY id DESC";
            var commendations = _db.Query<CommendationModel>(sql).ToList();
            return Json(new { data = commendations });
        }

        [HttpGet]
        public JsonResult GetCommendation(int id)
        {
            string sql = @"SELECT id, commendationCode, commendationName
                          FROM s_commendation
                          WHERE id = @Id AND isActive = 1";
            var commendation = _db.QueryFirstOrDefault<CommendationModel>(sql, new { Id = id });
            return Json(commendation);
        }

        [HttpPost]
        public JsonResult AddCommendation(CommendationModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_commendation
                                    WHERE commendationCode = @commendationCode
                                    AND isActive = 1";
                int existingCount = _db.QuerySingle<int>(checkSql, new { commendationCode = model.commendationCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Commendation code already exists!" });

                string sql = @"INSERT INTO s_commendation (commendationCode, commendationName, isActive, dtAdded, modifiedBy)
                              VALUES (@commendationCode, @commendationName, 1, NOW(), @modifiedBy);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    commendationCode = model.commendationCode,
                    commendationName = model.commendationName,
                    modifiedBy = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_commendation", newId, "CREATED",
                    $"Added commendation: {model.commendationCode} - {model.commendationName}");

                return Json(new { success = true, message = "Commendation added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding commendation: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateCommendation(CommendationModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_commendation
                                    WHERE id = @id AND isActive = 1";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Commendation record not found or has been deleted" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_commendation
                                            WHERE commendationCode = @commendationCode
                                            AND id != @id
                                            AND isActive = 1";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    commendationCode = model.commendationCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Commendation code already exists!" });

                string sql = @"UPDATE s_commendation
                              SET commendationCode = @commendationCode,
                                  commendationName = @commendationName,
                                  dtModified = NOW(),
                                  modifiedBy = @modifiedBy
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    commendationCode = model.commendationCode,
                    commendationName = model.commendationName,
                    modifiedBy = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_commendation", model.id, "UPDATED",
                    $"Updated commendation: {model.commendationCode} - {model.commendationName}");

                return Json(new { success = true, message = "Commendation updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating commendation: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteCommendation(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_commendation
                               SET isActive = 0, 
                                   dtModified = NOW(),
                                   modifiedBy = @modifiedBy
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    modifiedBy = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_commendation", id, "DELETED",
                    $"Commendation soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Commendation deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedCommendation()
        {
            string sql = @"SELECT id, commendationCode, commendationName
                          FROM s_commendation
                          WHERE isActive = 0
                          ORDER BY id DESC";
            var commendations = _db.Query<CommendationModel>(sql).ToList();
            return Json(new { data = commendations });
        }

        [HttpPost]
        public JsonResult RestoreCommendation(int id)
        {
            try
            {
                string sql = @"UPDATE s_commendation
                              SET isActive = 1,
                                  dtModified = NOW(),
                                  modifiedBy = @modifiedBy
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    modifiedBy = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_commendation", id, "RESTORED", "Commendation restored");

                return Json(new { success = true, message = "Commendation restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}