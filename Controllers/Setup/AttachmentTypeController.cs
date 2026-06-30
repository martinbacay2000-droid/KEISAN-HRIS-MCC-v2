using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SattachmenttypeM")]
    public class AttachmentTypeController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public AttachmentTypeController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/AttachmentType.cshtml");
        }

        [HttpGet]
        public JsonResult GetAttachmentTypeList()
        {
            string sql = @"SELECT id, attachmentTypeCode, attachmentTypeName 
                          FROM s_attachmenttype 
                          WHERE dtDeleted IS NULL 
                          ORDER BY id DESC";
            var attachmentTypes = _db.Query<AttachmentTypeModel>(sql).ToList();
            return Json(new { data = attachmentTypes });
        }

        [HttpGet]
        public JsonResult GetAttachmentType(int id)
        {
            string sql = @"SELECT id, attachmentTypeCode, attachmentTypeName 
                          FROM s_attachmenttype 
                          WHERE id = @Id AND isActive = 1";
            var attachmentType = _db.QueryFirstOrDefault<AttachmentTypeModel>(sql, new { Id = id });
            return Json(attachmentType);
        }

        [HttpPost]
        public JsonResult AddAttachmentType(AttachmentTypeModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_attachmenttype 
                                   WHERE attachmentTypeCode = @attachmentTypeCode 
                                   AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { attachmentTypeCode = model.attachmentTypeCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Attachment Type code already exists!" });

                string sql = @"INSERT INTO s_attachmenttype (attachmentTypeCode, attachmentTypeName, isActive, dtAdded, addedByUser) 
                              VALUES (@attachmentTypeCode, @attachmentTypeName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    attachmentTypeCode = model.attachmentTypeCode,
                    attachmentTypeName = model.attachmentTypeName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_attachmenttype", newId, "CREATED",
                    $"Added attachment type: {model.attachmentTypeCode} - {model.attachmentTypeName}");

                return Json(new { success = true, message = "Attachment Type added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding attachment type: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateAttachmentType(AttachmentTypeModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_attachmenttype 
                                   WHERE id = @id AND dtDeleted IS NULL";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                    return Json(new { success = false, message = "Attachment Type record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_attachmenttype 
                                            WHERE attachmentTypeCode = @attachmentTypeCode 
                                            AND id != @id 
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    attachmentTypeCode = model.attachmentTypeCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Attachment Type code already exists!" });

                string sql = @"UPDATE s_attachmenttype 
                              SET attachmentTypeCode = @attachmentTypeCode, 
                                  attachmentTypeName = @attachmentTypeName, 
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    attachmentTypeCode = model.attachmentTypeCode,
                    attachmentTypeName = model.attachmentTypeName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_attachmenttype", model.id, "UPDATED",
                    $"Updated attachment type: {model.attachmentTypeCode} - {model.attachmentTypeName}");

                return Json(new { success = true, message = "Attachment Type updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating attachment type: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteAttachmentType(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_attachmenttype 
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_attachmenttype", id, "DELETED",
                    $"Attachment type soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Attachment Type deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedAttachmentTypeList()
        {
            string sql = @"SELECT id, attachmentTypeCode, attachmentTypeName 
                          FROM s_attachmenttype 
                          WHERE dtDeleted IS NOT NULL 
                          ORDER BY id DESC";
            var attachmentTypes = _db.Query<AttachmentTypeModel>(sql).ToList();
            return Json(new { data = attachmentTypes });
        }

        [HttpPost]
        public JsonResult RestoreAttachmentType(int id)
        {
            try
            {
                string sql = @"UPDATE s_attachmenttype 
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

                _auditTrail.Log("s_attachmenttype", id, "RESTORED", "Attachment type restored");

                return Json(new { success = true, message = "Attachment Type restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}