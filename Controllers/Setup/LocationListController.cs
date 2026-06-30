using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SlocationListM")]
    public class LocationListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public LocationListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/LocationList.cshtml");
        }

        [HttpGet]
        public JsonResult GetLocationList()
        {
            string sql = @"SELECT id, locationCode, locationName
                          FROM s_location
                          WHERE dtDeleted IS NULL
                          ORDER BY id DESC";
            var locations = _db.Query<LocationListModel>(sql).ToList();
            return Json(new { data = locations });
        }

        [HttpGet]
        public JsonResult GetLocation(int id)
        {
            string sql = @"SELECT id, locationCode, locationName
                          FROM s_location
                          WHERE id = @Id AND isActive = 1";
            var location = _db.QueryFirstOrDefault<LocationListModel>(sql, new { Id = id });
            return Json(location);
        }

        [HttpPost]
        public JsonResult AddLocation(LocationListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_location
                                    WHERE locationCode = @locationCode
                                    AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { locationCode = model.locationCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Location code already exists!" });

                string sql = @"INSERT INTO s_location (locationCode, locationName, isActive, dtAdded, addedByUser)
                              VALUES (@locationCode, @locationName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    locationCode = model.locationCode,
                    locationName = model.locationName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_location", newId, "CREATED",
                    $"Added location: {model.locationCode} - {model.locationName}");

                return Json(new { success = true, message = "Location added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding location: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateLocation(LocationListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_location
                                    WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Location record not found or has been deleted" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_location
                                            WHERE locationCode = @locationCode
                                            AND id != @id
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    locationCode = model.locationCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Location code already exists!" });

                string sql = @"UPDATE s_location
                              SET locationCode = @locationCode,
                                  locationName = @locationName,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    locationCode = model.locationCode,
                    locationName = model.locationName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_location", model.id, "UPDATED",
                    $"Updated location: {model.locationCode} - {model.locationName}");

                return Json(new { success = true, message = "Location updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating location: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteLocation(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_location
                               SET dtDeleted = NOW(), 
                                   isActive = 0,
                                   deletedByUser = @deletedByUser
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_location", id, "DELETED",
                    $"Location soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Location deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedLocation()
        {
            string sql = @"SELECT id, locationCode, locationName
                          FROM s_location
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY id DESC";
            var locations = _db.Query<LocationListModel>(sql).ToList();
            return Json(new { data = locations });
        }

        [HttpPost]
        public JsonResult RestoreLocation(int id)
        {
            try
            {
                string sql = @"UPDATE s_location
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

                _auditTrail.Log("s_location", id, "RESTORED", "Location restored");

                return Json(new { success = true, message = "Location restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}