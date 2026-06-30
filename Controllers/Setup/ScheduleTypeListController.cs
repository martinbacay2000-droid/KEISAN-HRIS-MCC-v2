using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SscheduletyeplistM")]
    public class ScheduleTypeListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public ScheduleTypeListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/ScheduleTypeList.cshtml");
        }

        [HttpGet]
        public JsonResult GetScheduleTypeList()
        {
            string sql = @"SELECT id, scheduleTypeCode, scheduleTypeName 
                          FROM s_scheduletype 
                          WHERE isActive = 1 
                          ORDER BY id DESC";
            var scheduleTypes = _db.Query<ScheduleTypeListModel>(sql).ToList();
            return Json(new { data = scheduleTypes });
        }

        [HttpGet]
        public JsonResult GetScheduleType(int id)
        {
            string sql = @"SELECT id, scheduleTypeCode, scheduleTypeName 
                          FROM s_scheduletype 
                          WHERE id = @Id AND isActive = 1";
            var scheduleType = _db.QueryFirstOrDefault<ScheduleTypeListModel>(sql, new { Id = id });
            return Json(scheduleType);
        }

        [HttpPost]
        public JsonResult AddScheduleType(ScheduleTypeListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_scheduletype 
                                   WHERE scheduleTypeCode = @scheduleTypeCode 
                                   AND isActive = 1";
                int existingCount = _db.QuerySingle<int>(checkSql, new { scheduleTypeCode = model.scheduleTypeCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Schedule Type code already exists!" });

                string sql = @"INSERT INTO s_scheduletype (scheduleTypeCode, scheduleTypeName, isActive, dtAdded, addedByUser) 
                              VALUES (@scheduleTypeCode, @scheduleTypeName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    scheduleTypeCode = model.scheduleTypeCode,
                    scheduleTypeName = model.scheduleTypeName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_scheduletype", newId, "CREATED",
                    $"Added schedule type: {model.scheduleTypeCode} - {model.scheduleTypeName}");

                return Json(new { success = true, message = "Schedule Type added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding schedule type: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateScheduleType(ScheduleTypeListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_scheduletype 
                                   WHERE id = @id AND isActive = 1";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                    return Json(new { success = false, message = "Schedule Type record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_scheduletype 
                                            WHERE scheduleTypeCode = @scheduleTypeCode 
                                            AND id != @id 
                                            AND isActive = 1";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    scheduleTypeCode = model.scheduleTypeCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Schedule Type code already exists!" });

                string sql = @"UPDATE s_scheduletype 
                              SET scheduleTypeCode = @scheduleTypeCode, 
                                  scheduleTypeName = @scheduleTypeName,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    scheduleTypeCode = model.scheduleTypeCode,
                    scheduleTypeName = model.scheduleTypeName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_scheduletype", model.id, "UPDATED",
                    $"Updated schedule type: {model.scheduleTypeCode} - {model.scheduleTypeName}");

                return Json(new { success = true, message = "Schedule Type updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating schedule type: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteScheduleType(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_scheduletype 
                              SET isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_scheduletype", id, "DELETED",
                    $"Schedule Type soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Schedule Type deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedScheduleTypeList()
        {
            string sql = @"SELECT id, scheduleTypeCode, scheduleTypeName 
                          FROM s_scheduletype 
                          WHERE isActive = 0 
                          ORDER BY id DESC";
            var scheduleTypes = _db.Query<ScheduleTypeListModel>(sql).ToList();
            return Json(new { data = scheduleTypes });
        }

        [HttpPost]
        public JsonResult RestoreScheduleType(int id)
        {
            try
            {
                string sql = @"UPDATE s_scheduletype 
                              SET isActive = 1,
                                  deletedByUser = NULL,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_scheduletype", id, "RESTORED", "Schedule Type restored");

                return Json(new { success = true, message = "Schedule Type restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}