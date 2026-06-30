using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Attachments
{
    [ModuleAuthorize("FSAttachmentsM")]
    public class AttachmentsController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;
        private readonly IWebHostEnvironment _environment;

        public AttachmentsController(IDbConnection db, IAuditTrailService auditTrail, IWebHostEnvironment environment)
        {
            _db = db;
            _auditTrail = auditTrail;
            _environment = environment;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetAttachments(string employeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(employeeNo))
                {
                    return PartialView("~/Views/Users/Partials/_Attachments.cshtml",
                        new List<AttachmentsInfo>());
                }

                var employeeName = _db.QueryFirstOrDefault<string>(
                    @"SELECT CONCAT(lastName, ', ', firstName, ' ', COALESCE(middleName, '')) 
                      FROM e_basicinfo WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                var attachments = GetAttachmentsData(employeeNo, false);

                ViewBag.EmployeeNo = employeeNo;
                ViewBag.EmployeeName = employeeName ?? "Unknown Employee";

                return PartialView("~/Views/Users/Partials/_Attachments.cshtml", attachments);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAttachments: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_Attachments.cshtml",
                    new List<AttachmentsInfo>());
            }
        }

        [HttpGet]
        public JsonResult GetAttachmentsList(string employeeNo, string status = "active")
        {
            try
            {
                var isActive = status.Equals("active", StringComparison.OrdinalIgnoreCase);
                var attachments = GetAttachmentsData(employeeNo, !isActive);
                return Json(new { data = attachments });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAttachmentsList: {ex.Message}");
                return Json(new { data = new List<AttachmentsInfo>() });
            }
        }

        [HttpGet]
        public JsonResult GetAttachmentById(int id)
        {
            try
            {
                var sql = BuildAttachmentsQuery("WHERE att.id = @Id");
                var attachment = _db.QueryFirstOrDefault<AttachmentsInfo>(sql, new { Id = id });

                return attachment != null
                    ? Json(new { success = true, data = attachment })
                    : Json(new { success = false, message = "Attachment record not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAttachmentById: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving attachment: " + ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetAttachmentTypes()
        {
            try
            {
                var sql = @"
                    SELECT attachmentTypeCode, attachmentTypeName 
                    FROM s_attachmenttype 
                    WHERE isActive = 1 
                    ORDER BY attachmentTypeName";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAttachmentTypes: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public JsonResult GetEmployeeAttachments(string employeeNo)
        {
            try
            {
                var sql = @"
                    SELECT id, attachmentPath, attachmentDescription, dtAdded 
                    FROM e_attachment 
                    WHERE employeeNo = @employeeNo 
                    AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                    ORDER BY dtAdded DESC";

                return Json(_db.Query(sql, new { employeeNo }).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeAttachments: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpPost]
        public JsonResult AddAttachment(string employeeNo, string attachmentDescription, string attachmentTypeCode, IFormFile attachment)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrEmpty(employeeNo))
                    return Json(new { success = false, message = "Employee number is required!" });

                if (string.IsNullOrEmpty(attachmentDescription))
                    return Json(new { success = false, message = "Description is required!" });

                if (string.IsNullOrEmpty(attachmentTypeCode))
                    return Json(new { success = false, message = "Attachment type is required!" });

                // Handle attachment upload
                if (attachment == null || attachment.Length == 0)
                    return Json(new { success = false, message = "Please select a file to upload!" });

                var uploadResult = SaveAttachment(employeeNo, attachmentDescription, attachmentTypeCode, attachment);

                if (!uploadResult.success)
                    return Json(new { success = false, message = uploadResult.message });

                return Json(new { success = true, message = "Attachment uploaded successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddAttachment: {ex.Message}");
                return Json(new { success = false, message = $"Error adding attachment: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateAttachment(int id, string employeeNo, string attachmentDescription, string attachmentTypeCode, IFormFile attachment)
        {
            try
            {
                // Check if record exists
                var existingRecord = _db.QueryFirstOrDefault<AttachmentsInfo>(
                    "SELECT * FROM e_attachment WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                    new { Id = id });

                if (existingRecord == null)
                    return Json(new { success = false, message = "Attachment record not found!" });

                // Validate inputs
                if (string.IsNullOrEmpty(attachmentDescription))
                    return Json(new { success = false, message = "Description is required!" });

                if (string.IsNullOrEmpty(attachmentTypeCode))
                    return Json(new { success = false, message = "Attachment type is required!" });

                string attachmentPath = existingRecord.attachmentPath;

                // If new file is provided, upload it
                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = ProcessFileUpload(attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = uploadResult.message });

                    attachmentPath = uploadResult.filePath;
                }

                var sql = @"
                    UPDATE e_attachment 
                    SET attachmentDescription = @AttachmentDescription,
                        attachmentTypeCode = @AttachmentTypeCode,
                        attachmentPath = @AttachmentPath,
                        dtLastModified = NOW(),
                        lastModifiedByUser = @ModifiedByUser
                    WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    AttachmentDescription = attachmentDescription,
                    AttachmentTypeCode = attachmentTypeCode,
                    AttachmentPath = attachmentPath,
                    ModifiedByUser = EmployeeNo
                });

                _auditTrail.Log("e_attachment", id, "UPDATED",
                    $"Updated attachment: {attachmentDescription} - Employee: {employeeNo}");

                return Json(new { success = true, message = "Attachment updated successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateAttachment: {ex.Message}");
                return Json(new { success = false, message = $"Error updating attachment: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult InactiveAttachment(int id)
        {
            try
            {
                var sql = @"
                    UPDATE e_attachment 
                    SET dtDeleted = NOW(), 
                        isActive = 0, 
                        deletedByUser = @DeletedByUser
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new
                {
                    Id = id,
                    DeletedByUser = EmployeeNo
                });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_attachment", id, "DELETED", "Attachment soft deleted");
                }

                return Json(new { success = true, message = "Attachment deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InactiveAttachment: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreAttachment(int id)
        {
            try
            {
                var sql = @"
                    UPDATE e_attachment 
                    SET dtDeleted = NULL, 
                        deletedByUser = NULL, 
                        isActive = 1
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new { Id = id });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_attachment", id, "RESTORED", "Attachment restored");
                }

                return Json(new { success = true, message = "Attachment restored successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreAttachment: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // HELPER METHODS

        private List<AttachmentsInfo> GetAttachmentsData(string employeeNo, bool isDeleted)
        {
            var whereClause = isDeleted
                ? "WHERE att.employeeNo = @EmployeeNo AND (att.dtDeleted IS NOT NULL AND att.dtDeleted != '0000-00-00 00:00:00')"
                : "WHERE att.employeeNo = @EmployeeNo AND (att.dtDeleted IS NULL OR att.dtDeleted = '0000-00-00 00:00:00')";

            var sql = BuildAttachmentsQuery(whereClause);
            return _db.Query<AttachmentsInfo>(sql, new { EmployeeNo = employeeNo }).ToList();
        }

        private string BuildAttachmentsQuery(string whereClause)
        {
            return $@"
                SELECT 
                    att.id,
                    att.employeeNo,
                    att.attachmentDescription,
                    att.attachmentTypeCode,
                    COALESCE(atype.attachmentTypeName, att.attachmentTypeCode) AS attachmentTypeName,
                    att.attachmentPath,
                    att.isActive,
                    DATE_FORMAT(att.dtAdded, '%Y/%m/%d') AS dtAdded,
                    CONCAT(COALESCE(u.lastName, ''), ', ', COALESCE(u.firstName, '')) AS addedByUser,
                    DATE_FORMAT(att.dtLastModified, '%Y/%m/%d %H:%i:%s') AS dtLastModified,
                    att.lastModifiedByUser,
                    DATE_FORMAT(att.dtDeleted, '%Y/%m/%d %H:%i:%s') AS dtDeleted,
                    att.deletedByUser
                FROM e_attachment att
                LEFT JOIN s_user u ON u.userCode = att.addedByUser
                LEFT JOIN s_attachmenttype atype ON atype.attachmentTypeCode = att.attachmentTypeCode
                {whereClause}
                ORDER BY att.dtAdded DESC, att.id DESC";
        }

        private (bool success, string message, string filePath) SaveAttachment(string employeeNo, string description, string typeCode, IFormFile file)
        {
            try
            {
                var uploadResult = ProcessFileUpload(file);
                if (!uploadResult.success)
                    return (false, uploadResult.message, null);

                // Save attachment record to database
                var sql = @"
                    INSERT INTO e_attachment (employeeNo, attachmentDescription, attachmentTypeCode, 
                                             attachmentPath, isActive, dtAdded, addedByUser) 
                    VALUES (@employeeNo, @description, @typeCode, @attachmentPath, 1, NOW(), @addedByUser)";

                _db.Execute(sql, new
                {
                    employeeNo,
                    description,
                    typeCode,
                    attachmentPath = uploadResult.filePath,
                    addedByUser = EmployeeNo
                });

                return (true, "Attachment saved successfully", uploadResult.filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving attachment: {ex.Message}");
                return (false, $"Error saving attachment: {ex.Message}", null);
            }
        }

        private (bool success, string message, string filePath) ProcessFileUpload(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return (false, "No file provided", null);

                // Validate file size (5MB limit - same as commendation)
                if (file.Length > 5 * 1024 * 1024)
                    return (false, "File size exceeds 5MB limit", null);

                // Validate file extension (same as commendation)
                var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" };
                var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(fileExtension))
                    return (false, "Invalid file format. Allowed: PDF, JPG, PNG, DOC, DOCX", null);

                // Create uploads directory if it doesn't exist
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "attachments");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                // Generate unique filename (using GUID + original filename like commendation)
                var uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Save file to disk
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                return (true, "File uploaded successfully", "/uploads/attachments/" + uniqueFileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading file: {ex.Message}");
                return (false, $"Error uploading file: {ex.Message}", null);
            }
        }
    }
}