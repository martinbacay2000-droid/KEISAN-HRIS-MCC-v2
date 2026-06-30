using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Globalization;

namespace KEISAN_HRIS_v2.Controllers.LicensesAndCertification
{
    [ModuleAuthorize("FSLicencesAndCertificationM")]
    public class LicensesAndCertificationController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public LicensesAndCertificationController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetLicensesAndCertification(string employeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(employeeNo))
                {
                    return PartialView("~/Views/Users/Partials/_LicensesAndCertification.cshtml",
                        new List<LicensesAndCertificationInfo>());
                }

                var employeeName = _db.QueryFirstOrDefault<string>(
                    @"SELECT CONCAT(lastName, ', ', firstName, ' ', COALESCE(middleName, '')) 
                      FROM e_basicinfo WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                var licenses = GetLicensesData(employeeNo, false);

                ViewBag.EmployeeNo = employeeNo;
                ViewBag.EmployeeName = employeeName ?? "Unknown Employee";

                return PartialView("~/Views/Users/Partials/_LicensesAndCertification.cshtml", licenses);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLicensesAndCertification: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_LicensesAndCertification.cshtml",
                    new List<LicensesAndCertificationInfo>());
            }
        }

        [HttpGet]
        public JsonResult GetLicensesAndCertificationList(string employeeNo, string isactive)
        {
            try
            {
                // Convert isactive parameter: "2" means all, "1" means active, "0" means inactive
                bool? activeFilter = isactive == "2" ? null : isactive == "1";
                var licenses = GetLicensesData(employeeNo, false, activeFilter);
                return Json(new { data = licenses });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLicensesAndCertificationList: {ex.Message}");
                return Json(new { data = new List<LicensesAndCertificationInfo>() });
            }
        }

        [HttpGet]
        public JsonResult GetLicenseById(int id)
        {
            try
            {
                var sql = BuildLicenseQuery("WHERE lc.id = @Id");
                var license = _db.QueryFirstOrDefault<LicensesAndCertificationInfo>(sql, new { Id = id });

                return license != null
                    ? Json(new { success = true, data = license })
                    : Json(new { success = false, message = "License/Certification record not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLicenseById: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving license/certification: " + ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedLicenses(string employeeNo)
        {
            try
            {
                var licenses = GetLicensesData(employeeNo, true);
                return Json(new { data = licenses });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeletedLicenses: {ex.Message}");
                return Json(new { data = new List<LicensesAndCertificationInfo>() });
            }
        }

        [HttpPost]
        public JsonResult SaveLicenseAndCertificate([FromBody] LicensesAndCertificationDto model)
        {
            try
            {
                if (!ValidateLicense(model, out string validationMessage))
                {
                    return Json(new { success = false, message = validationMessage });
                }

                if (!ProcessDates(model, out DateTime? registrationDate, out DateTime? issueDate,
                    out DateTime? validUntil, out string dateError))
                {
                    return Json(new { success = false, message = dateError });
                }

                // Validate date logic
                if (issueDate.HasValue && registrationDate.HasValue && issueDate < registrationDate)
                {
                    return Json(new { success = false, message = "Issue date cannot be earlier than registration date." });
                }

                if (validUntil.HasValue && issueDate.HasValue && validUntil < issueDate)
                {
                    return Json(new { success = false, message = "Valid until date cannot be earlier than issue date." });
                }

                if (model.Id.HasValue && model.Id > 0)
                {
                    return UpdateLicense(model, registrationDate, issueDate, validUntil);
                }
                else
                {
                    return InsertLicense(model, registrationDate, issueDate, validUntil);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveLicenseAndCertificate: {ex.Message}");
                return Json(new { success = false, message = "Error saving license/certification: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult InactiveLicense(int id, string remarks = "")
        {
            try
            {
                if (!RecordExists(id))
                {
                    return Json(new { success = false, message = "License/Certification record not found or already deleted!" });
                }

                var sql = @"
                    UPDATE e_licenseandcertificate 
                    SET dtDeleted = NOW(), 
                        isActive = 0, 
                        deletedByUser = @DeletedByUser
                    WHERE id = @Id";

                var parameters = new
                {
                    Id = id,
                    DeletedByUser = EmployeeNo
                };

                var rowsAffected = _db.Execute(sql, parameters);

                if (rowsAffected > 0)
                {
                    var auditMessage = string.IsNullOrWhiteSpace(remarks)
                        ? "License/Certification soft deleted"
                        : $"License/Certification soft deleted. Reason: {remarks}";

                    _auditTrail.Log("e_licenseandcertificate", id, "DELETED", auditMessage);
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "License/Certification deleted successfully!" })
                    : Json(new { success = false, message = "Failed to delete license/certification." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InactiveLicense: {ex.Message}");
                return Json(new { success = false, message = "Error deleting license/certification: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreLicense(int id)
        {
            try
            {
                var existingRecord = _db.QueryFirstOrDefault<LicensesAndCertificationInfo>(
                    "SELECT * FROM e_licenseandcertificate WHERE id = @Id AND (dtDeleted IS NOT NULL AND dtDeleted != '0000-00-00 00:00:00')",
                    new { Id = id });

                if (existingRecord == null)
                {
                    return Json(new { success = false, message = "License/Certification record not found or not deleted!" });
                }

                var sql = @"
                    UPDATE e_licenseandcertificate 
                    SET dtDeleted = NULL, 
                        deletedByUser = NULL, 
                        isActive = 1, 
                        dtLastModified = NOW()
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new { Id = id });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_licenseandcertificate", id, "RESTORED", "License/Certification restored");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "License/Certification restored successfully!" })
                    : Json(new { success = false, message = "Failed to restore license/certification." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreLicense: {ex.Message}");
                return Json(new { success = false, message = "Error restoring license/certification: " + ex.Message });
            }
        }

        // HELPER METHODS

        private bool ValidateLicense(LicensesAndCertificationDto model, out string message)
        {
            message = string.Empty;

            if (model == null)
            {
                message = "Invalid data provided.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.EmployeeNo))
            {
                message = "Employee number is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.LicenseAndCertificateNo))
            {
                message = "License/Certificate number is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.LicenseAndCertificateDescription))
            {
                message = "Description is required.";
                return false;
            }

            return true;
        }

        private bool ProcessDates(LicensesAndCertificationDto model, out DateTime? registrationDate,
            out DateTime? issueDate, out DateTime? validUntil, out string errorMessage)
        {
            registrationDate = null;
            issueDate = null;
            validUntil = null;
            errorMessage = string.Empty;

            // Try multiple date formats
            string[] formats = { "yyyy/MM/dd", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy" };

            // Registration Date (optional)
            if (!string.IsNullOrEmpty(model.RegistrationDate))
            {
                if (!DateTime.TryParseExact(model.RegistrationDate, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime tempRegDate))
                {
                    errorMessage = "Invalid registration date format. Expected format: yyyy/MM/dd";
                    return false;
                }
                registrationDate = tempRegDate;
            }

            // Issue Date (optional)
            if (!string.IsNullOrEmpty(model.IssueDate))
            {
                if (!DateTime.TryParseExact(model.IssueDate, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime tempIssueDate))
                {
                    errorMessage = "Invalid issue date format. Expected format: yyyy/MM/dd";
                    return false;
                }
                issueDate = tempIssueDate;
            }

            // Valid Until (optional)
            if (!string.IsNullOrEmpty(model.ValidUntil))
            {
                if (!DateTime.TryParseExact(model.ValidUntil, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime tempValidUntil))
                {
                    errorMessage = "Invalid valid until date format. Expected format: yyyy/MM/dd";
                    return false;
                }
                validUntil = tempValidUntil;
            }

            return true;
        }

        private JsonResult UpdateLicense(LicensesAndCertificationDto model, DateTime? registrationDate,
            DateTime? issueDate, DateTime? validUntil)
        {
            var existingRecord = _db.QueryFirstOrDefault<LicensesAndCertificationInfo>(
                "SELECT * FROM e_licenseandcertificate WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = model.Id });

            if (existingRecord == null)
            {
                return Json(new { success = false, message = "License/Certification record not found or has been deleted!" });
            }

            var sql = @"
                UPDATE e_licenseandcertificate
                SET licenseAndCertificateNo = @LicenseNo,
                    licenseAndCertificateDescription = @Description,
                    registrationDate = @RegistrationDate,
                    issueDate = @IssueDate,
                    validUntil = @ValidUntil,
                    licenseRemarks = @Remarks,
                    dtLastModified = NOW(),
                    lastModifiedByUser = @ModifiedByUser
                WHERE id = @Id";

            var rowsAffected = _db.Execute(sql, new
            {
                Id = model.Id,
                LicenseNo = model.LicenseAndCertificateNo,
                Description = model.LicenseAndCertificateDescription,
                RegistrationDate = registrationDate?.ToString("yyyy-MM-dd"),
                IssueDate = issueDate?.ToString("yyyy-MM-dd"),
                ValidUntil = validUntil?.ToString("yyyy-MM-dd"),
                Remarks = model.LicenseRemarks ?? string.Empty,
                ModifiedByUser = EmployeeNo
            });

            if (rowsAffected > 0)
            {
                _auditTrail.Log("e_licenseandcertificate", model.Id.Value, "UPDATED",
                    $"Updated license/certification: {model.LicenseAndCertificateNo} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "License/Certification updated successfully!" });
            }

            return Json(new { success = false, message = "Failed to update license/certification." });
        }

        private JsonResult InsertLicense(LicensesAndCertificationDto model, DateTime? registrationDate,
            DateTime? issueDate, DateTime? validUntil)
        {
            var sql = @"
                INSERT INTO e_licenseandcertificate (
                    employeeNo, licenseAndCertificateNo, licenseAndCertificateDescription, 
                    registrationDate, issueDate, validUntil, licenseRemarks, 
                    isActive, dtAdded, addedByUser
                )
                VALUES (
                    @EmployeeNo, @LicenseNo, @Description,
                    @RegistrationDate, @IssueDate, @ValidUntil, @Remarks,
                    1, NOW(), @AddedByUser
                );
                SELECT LAST_INSERT_ID();";

            int newId = _db.QuerySingle<int>(sql, new
            {
                EmployeeNo = model.EmployeeNo,
                LicenseNo = model.LicenseAndCertificateNo,
                Description = model.LicenseAndCertificateDescription,
                RegistrationDate = registrationDate?.ToString("yyyy-MM-dd"),
                IssueDate = issueDate?.ToString("yyyy-MM-dd"),
                ValidUntil = validUntil?.ToString("yyyy-MM-dd"),
                Remarks = model.LicenseRemarks ?? string.Empty,
                AddedByUser = EmployeeNo
            });

            if (newId > 0)
            {
                _auditTrail.Log("e_licenseandcertificate", newId, "CREATED",
                    $"Added license/certification: {model.LicenseAndCertificateNo} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "License/Certification added successfully!" });
            }

            return Json(new { success = false, message = "Failed to add license/certification." });
        }

        private List<LicensesAndCertificationInfo> GetLicensesData(string employeeNo, bool isDeleted, bool? isActiveFilter = null)
        {
            string whereClause;

            if (isDeleted)
            {
                whereClause = "WHERE lc.employeeNo = @EmployeeNo AND (lc.dtDeleted IS NOT NULL AND lc.dtDeleted != '0000-00-00 00:00:00')";
            }
            else if (isActiveFilter.HasValue)
            {
                whereClause = isActiveFilter.Value
                    ? "WHERE lc.employeeNo = @EmployeeNo AND lc.isActive = 1 AND (lc.dtDeleted IS NULL OR lc.dtDeleted = '0000-00-00 00:00:00')"
                    : "WHERE lc.employeeNo = @EmployeeNo AND lc.isActive = 0 AND (lc.dtDeleted IS NULL OR lc.dtDeleted = '0000-00-00 00:00:00')";
            }
            else
            {
                whereClause = "WHERE lc.employeeNo = @EmployeeNo AND (lc.dtDeleted IS NULL OR lc.dtDeleted = '0000-00-00 00:00:00')";
            }

            var sql = BuildLicenseQuery(whereClause);
            return _db.Query<LicensesAndCertificationInfo>(sql, new { EmployeeNo = employeeNo }).ToList();
        }

        private string BuildLicenseQuery(string whereClause)
        {
            return $@"
                SELECT 
                    lc.id,
                    lc.employeeNo, 
                    lc.licenseAndCertificateNo,
                    lc.licenseAndCertificateDescription,
                    DATE_FORMAT(lc.registrationDate, '%Y/%m/%d') AS registrationDate, 
                    DATE_FORMAT(lc.issueDate, '%Y/%m/%d') AS issueDate,
                    DATE_FORMAT(lc.validUntil, '%Y/%m/%d') AS validUntil,
                    lc.licenseRemarks,
                    lc.isActive,
                    DATE_FORMAT(lc.dtAdded, '%Y/%m/%d') AS dtAdded, 
                    CONCAT(COALESCE(u.lastName, ''), ', ', COALESCE(u.firstName, '')) AS addedByUser,
                    lc.dtLastModified,
                    lc.lastModifiedByUser,
                    lc.dtDeleted,
                    lc.deletedByUser
                FROM e_licenseandcertificate lc
                LEFT JOIN s_user u ON u.userCode = lc.addedByUser
                {whereClause}
                ORDER BY lc.id DESC";
        }

        private bool RecordExists(int id)
        {
            var record = _db.QueryFirstOrDefault<LicensesAndCertificationInfo>(
                "SELECT * FROM e_licenseandcertificate WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = id });

            return record != null;
        }
    }
}