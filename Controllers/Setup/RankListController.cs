using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SrankM")]
    public class RankListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public RankListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/RankList.cshtml");
        }

        [HttpGet]
        public JsonResult GetRankList()
        {
            string sql = @"SELECT id, rankCode, rankName
                          FROM s_rank
                          WHERE dtDeleted IS NULL
                          ORDER BY id DESC";
            var rank = _db.Query<RankListModel>(sql).ToList();
            return Json(new { data = rank });
        }

        [HttpGet]
        public JsonResult GetRank(int id)
        {
            string sql = @"SELECT id, rankCode, rankName
                          FROM s_rank
                          WHERE id = @Id AND isActive = 1";
            var rank = _db.QueryFirstOrDefault<RankListModel>(sql, new { Id = id });
            return Json(rank);
        }

        [HttpPost]
        public JsonResult AddRank(RankListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_rank
                                    WHERE rankCode = @rankCode
                                    AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { rankCode = model.rankCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Rank code already exists!" });

                string sql = @"INSERT INTO s_rank (rankCode, rankName, isActive, dtAdded, addedByUser)
                              VALUES (@rankCode, @rankName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    rankCode = model.rankCode,
                    rankName = model.rankName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_rank", newId, "CREATED",
                    $"Added rank: {model.rankCode} - {model.rankName}");

                return Json(new { success = true, message = "Rank added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding rank: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateRank(RankListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_rank
                                    WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Rank record not found or has been deleted" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_rank
                                            WHERE rankCode = @rankCode
                                            AND id != @id
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    rankCode = model.rankCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Rank code already exists!" });

                string sql = @"UPDATE s_rank
                              SET rankCode = @rankCode,
                                  rankName = @rankName,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    rankCode = model.rankCode,
                    rankName = model.rankName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_rank", model.id, "UPDATED",
                    $"Updated rank: {model.rankCode} - {model.rankName}");

                return Json(new { success = true, message = "Rank updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating rank: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteRank(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_rank
                               SET dtDeleted = NOW(), 
                                   isActive = 0,
                                   deletedByUser = @deletedByUser
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_rank", id, "DELETED",
                    $"Rank soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Rank deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedRank()
        {
            string sql = @"SELECT id, rankCode, rankName
                          FROM s_rank
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY id DESC";
            var rank = _db.Query<RankListModel>(sql).ToList();
            return Json(new { data = rank });
        }

        [HttpPost]
        public JsonResult RestoreRank(int id)
        {
            try
            {
                string sql = @"UPDATE s_rank
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

                _auditTrail.Log("s_rank", id, "RESTORED", "Rank restored");

                return Json(new { success = true, message = "Rank restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}