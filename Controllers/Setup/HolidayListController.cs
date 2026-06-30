using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SholidayM")]
    public class HolidayListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public HolidayListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/HolidayList.cshtml");
        }

        [HttpGet]
        public JsonResult GetHolidayList()
        {
            string sql = @"SELECT h.id, h.holidayName, h.holidayDate, h.holidayType, h.branchCode
                          FROM s_holiday h
                          WHERE h.dtDeleted IS NULL
                          ORDER BY h.id DESC";
            var holidays = _db.Query<HolidayListModel>(sql).ToList();
            return Json(new { data = holidays });
        }

        [HttpGet]
        public JsonResult GetBranches()
        {
            string sql = @"SELECT id, branchCode, branchName
                          FROM s_branch
                          WHERE dtDeleted IS NULL AND isActive = 1
                          ORDER BY branchName";
            var branches = _db.Query<BranchListModel>(sql).ToList();
            return Json(branches);
        }

        [HttpGet]
        public JsonResult GetHoliday(int id)
        {
            string sql = @"SELECT id, holidayName, holidayDate, holidayType, branchCode
                          FROM s_holiday
                          WHERE id = @Id AND isActive = 1";
            var holiday = _db.QueryFirstOrDefault<HolidayListModel>(sql, new { Id = id });
            return Json(holiday);
        }

        [HttpPost]
        public JsonResult AddHoliday(HolidayListModel model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.holidayName))
                    return Json(new { success = false, message = "Holiday name is required!" });

                if (string.IsNullOrWhiteSpace(model.holidayType))
                    return Json(new { success = false, message = "Holiday type is required!" });

                if (model.holidayDate == null)
                    return Json(new { success = false, message = "Holiday date is required!" });

                if (string.IsNullOrWhiteSpace(model.branchCode))
                    return Json(new { success = false, message = "At least one branch must be selected!" });

                var branchCodes = model.branchCode
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(b => b.Trim())
                    .ToList();

                string concatenatedBranches = string.Join(",", branchCodes);

                string checkSql = @"SELECT COUNT(*) FROM s_holiday
                                   WHERE holidayDate = @holidayDate
                                   AND holidayName = @holidayName
                                   AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new
                {
                    holidayDate = model.holidayDate,
                    holidayName = model.holidayName.Trim()
                });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Holiday with this name already exists on this date!" });

                string sql = @"INSERT INTO s_holiday (holidayName, holidayDate, holidayType, branchCode, isActive, dtAdded, addedByUser)
                              VALUES (@holidayName, @holidayDate, @holidayType, @branchCode, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    holidayName = model.holidayName.Trim(),
                    holidayDate = model.holidayDate,
                    holidayType = model.holidayType,
                    branchCode = concatenatedBranches,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_holiday", newId, "CREATED",
                    $"Added holiday: {model.holidayName.Trim()} on {model.holidayDate:yyyy-MM-dd} ({model.holidayType})");

                return Json(new { success = true, message = "Holiday added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding holiday: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateHoliday(HolidayListModel model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.holidayName))
                    return Json(new { success = false, message = "Holiday name is required!" });

                if (string.IsNullOrWhiteSpace(model.holidayType))
                    return Json(new { success = false, message = "Holiday type is required!" });

                if (model.holidayDate == null)
                    return Json(new { success = false, message = "Holiday date is required!" });

                if (string.IsNullOrWhiteSpace(model.branchCode))
                    return Json(new { success = false, message = "At least one branch must be selected!" });

                string checkSql = @"SELECT COUNT(*) FROM s_holiday
                                   WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Holiday record not found or has been deleted" });

                var branchCodes = model.branchCode
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(b => b.Trim())
                    .ToList();

                string concatenatedBranches = string.Join(",", branchCodes);

                string sql = @"UPDATE s_holiday
                              SET holidayName = @holidayName,
                                  holidayDate = @holidayDate,
                                  holidayType = @holidayType,
                                  branchCode = @branchCode,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    holidayName = model.holidayName.Trim(),
                    holidayDate = model.holidayDate,
                    holidayType = model.holidayType,
                    branchCode = concatenatedBranches,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_holiday", model.id, "UPDATED",
                    $"Updated holiday: {model.holidayName.Trim()} on {model.holidayDate:yyyy-MM-dd} ({model.holidayType})");

                return Json(new { success = true, message = "Holiday updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating holiday: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteHoliday(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_holiday
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_holiday", id, "DELETED",
                    $"Holiday soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Holiday deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedHoliday()
        {
            string sql = @"SELECT id, holidayName, holidayDate, holidayType, branchCode
                          FROM s_holiday
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY id DESC";
            var holidays = _db.Query<HolidayListModel>(sql).ToList();
            return Json(new { data = holidays });
        }

        [HttpPost]
        public JsonResult RestoreHoliday(int id)
        {
            try
            {
                string sql = @"UPDATE s_holiday
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

                _auditTrail.Log("s_holiday", id, "RESTORED", "Holiday restored");

                return Json(new { success = true, message = "Holiday restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}