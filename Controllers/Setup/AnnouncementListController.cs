using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("AAnnouncementM")]
    public class AnnouncementListController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public AnnouncementListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/AnnouncementList.cshtml");
        }

        // Get all active announcement records
        [HttpGet]
        public JsonResult GetAnnouncementList()
        {
            try
            {
                string sql = @"SELECT id, announcementTitle, announcement, dateStart, dateEnd, 
                              isActive, addedByUser, dtAdded
                              FROM s_announcement
                              WHERE isActive = 1
                              ORDER BY dtAdded DESC";

                var announcements = _db.Query<AnnouncementListModel>(sql).ToList();
                return Json(new { data = announcements });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAnnouncementList: {ex.Message}");
                return Json(new { data = new List<AnnouncementListModel>() });
            }
        }

        // Get all inactive/deleted announcement records
        [HttpGet]
        public JsonResult GetDeletedAnnouncementList()
        {
            try
            {
                string sql = @"SELECT id, announcementTitle, announcement, dateStart, dateEnd, 
                              isActive, addedByUser, dtAdded
                              FROM s_announcement
                              WHERE isActive = 0
                              ORDER BY dtAdded DESC";

                var announcements = _db.Query<AnnouncementListModel>(sql).ToList();
                return Json(new { data = announcements });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeletedAnnouncementList: {ex.Message}");
                return Json(new { data = new List<AnnouncementListModel>() });
            }
        }

        // Get single announcement record by ID
        [HttpGet]
        public JsonResult GetAnnouncement(int id)
        {
            try
            {
                string sql = @"SELECT id, announcementTitle, announcement, dateStart, dateEnd,
                              isActive, addedByUser, dtAdded
                              FROM s_announcement
                              WHERE id = @Id AND isActive = 1";

                var announcement = _db.QueryFirstOrDefault<AnnouncementListModel>(sql, new { Id = id });
                return Json(announcement);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAnnouncement: {ex.Message}");
                return Json(null);
            }
        }

        // Get active announcements for dashboard/display use
        [HttpGet]
        public JsonResult GetActiveAnnouncements()
        {
            try
            {
                DateTime today = DateTime.Today;

                string sql = @"SELECT a.id, a.announcementTitle, a.announcement, a.dateStart, a.dateEnd, 
                          a.isActive, a.addedByUser, a.dtAdded,
                          CONCAT(b.firstName, ' ', b.lastName) AS addedByUserName
                      FROM s_announcement a
                      LEFT JOIN e_basicinfo b ON b.employeeNo = a.addedByUser
                      WHERE a.isActive = 1 
                      AND (a.dateStart IS NULL OR a.dateStart <= @today)
                      AND (a.dateEnd IS NULL OR a.dateEnd >= @today)
                      ORDER BY a.dtAdded DESC";

                var announcements = _db.Query(sql, new { today }).ToList();
                return Json(new { success = true, data = announcements });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetActiveAnnouncements: {ex.Message}");
                return Json(new { success = false, message = "Unable to load announcements", data = new List<object>() });
            }
        }

        // Add new announcement
        [HttpPost]
        public JsonResult AddAnnouncement(AnnouncementListModel model)
        {
            try
            {
                // Validate date range
                if (model.dateStart.HasValue && model.dateEnd.HasValue)
                {
                    if (model.dateEnd < model.dateStart)
                        return Json(new { success = false, message = "End date cannot be earlier than start date!" });
                }

                string sql = @"INSERT INTO s_announcement 
                              (announcementTitle, announcement, dateStart, dateEnd, isActive, dtAdded, addedByUser)
                              VALUES 
                              (@announcementTitle, @announcement, @dateStart, @dateEnd, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    announcementTitle = model.announcementTitle,
                    announcement = model.announcement,
                    dateStart = model.dateStart,
                    dateEnd = model.dateEnd,
                    addedByUser = EmployeeNo
                });

                _auditTrail.Log("s_announcement", newId, "CREATED",
                    $"Added announcement: {model.announcementTitle}");

                return Json(new { success = true, message = "Announcement added successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddAnnouncement: {ex.Message}");
                return Json(new { success = false, message = $"Error adding announcement: {ex.Message}" });
            }
        }

        // Update existing announcement
        [HttpPost]
        public JsonResult UpdateAnnouncement(AnnouncementListModel model)
        {
            try
            {
                // Check if the record exists and is active
                var exists = _db.QuerySingle<int>(
                    "SELECT COUNT(*) FROM s_announcement WHERE id = @id AND isActive = 1",
                    new { model.id });

                if (exists == 0)
                    return Json(new { success = false, message = "Announcement record not found!" });

                // Validate date range
                if (model.dateStart.HasValue && model.dateEnd.HasValue)
                {
                    if (model.dateEnd < model.dateStart)
                        return Json(new { success = false, message = "End date cannot be earlier than start date!" });
                }

                string sql = @"UPDATE s_announcement
                              SET announcementTitle       = @announcementTitle,
                                  announcement            = @announcement,
                                  dateStart               = @dateStart,
                                  dateEnd                 = @dateEnd,
                                  dtLastModified          = NOW(),
                                  lastModifiedByUser      = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    model.id,
                    announcementTitle = model.announcementTitle,
                    announcement = model.announcement,
                    dateStart = model.dateStart,
                    dateEnd = model.dateEnd,
                    lastModifiedByUser = EmployeeNo
                });

                _auditTrail.Log("s_announcement", model.id, "UPDATED",
                    $"Updated announcement: {model.announcementTitle}");

                return Json(new { success = true, message = "Announcement updated successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateAnnouncement: {ex.Message}");
                return Json(new { success = false, message = $"Error updating announcement: {ex.Message}" });
            }
        }

        // Soft delete announcement
        [HttpPost]
        public JsonResult DeleteAnnouncement(int id, string reason = "")
        {
            try
            {
                // Get announcement info before deleting
                var announcement = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT announcementTitle FROM s_announcement WHERE id = @id",
                    new { id });

                if (announcement == null)
                    return Json(new { success = false, message = "Announcement record not found!" });

                string sql = @"UPDATE s_announcement 
                              SET isActive        = 0,
                                  dtDeleted       = NOW(),
                                  deletedByUser   = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo
                });

                _auditTrail.Log("s_announcement", id, "DELETED",
                    $"Deleted announcement: {announcement.announcementTitle}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "Announcement deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteAnnouncement: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Restore soft-deleted announcement
        [HttpPost]
        public JsonResult RestoreAnnouncement(int id)
        {
            try
            {
                // Get announcement info before restoring
                var announcement = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT announcementTitle FROM s_announcement WHERE id = @id",
                    new { id });

                if (announcement == null)
                    return Json(new { success = false, message = "Announcement record not found!" });

                string sql = @"UPDATE s_announcement
                              SET isActive            = 1,
                                  dtDeleted           = NULL,
                                  deletedByUser       = NULL,
                                  dtLastModified      = NOW(),
                                  lastModifiedByUser  = @lastModifiedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    lastModifiedByUser = EmployeeNo
                });

                _auditTrail.Log("s_announcement", id, "RESTORED",
                    $"Restored announcement: {announcement.announcementTitle}");

                return Json(new { success = true, message = "Announcement restored successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreAnnouncement: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}