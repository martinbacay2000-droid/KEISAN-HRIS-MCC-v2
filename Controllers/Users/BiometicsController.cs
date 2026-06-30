using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("TbiometricsM")]
    public class BiometricsController : BaseController 
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public BiometricsController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Users/Biometrics.cshtml");
        }

        // Get all biometrics records (active or deleted based on filter)
        [HttpGet]
        public JsonResult GetBiometricsList()
        {
            try
            {
                var sql = @"
                    SELECT b.id, b.employeeNo, b.accessNo, b.isActive, b.dtAdded,
                           CONCAT(IFNULL(e.firstName, ''), ' ', 
                                  IFNULL(CONCAT(e.middleName, ' '), ''), 
                                  IFNULL(e.lastName, '')) as employeeName
                    FROM e_biometrics b
                    LEFT JOIN e_basicinfo e ON b.employeeNo = e.employeeNo
                    WHERE b.isActive = 1
                    ORDER BY b.dtAdded DESC";

                var biometrics = _db.Query<BiometricsModel>(sql).ToList();
                return Json(new { data = biometrics });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetBiometricsList: {ex.Message}");
                return Json(new { data = new List<BiometricsModel>(), error = ex.Message });
            }
        }

        // Get deleted biometrics records
        [HttpGet]
        public JsonResult GetDeletedBiometricsList()
        {
            try
            {
                var sql = @"
                    SELECT b.id, b.employeeNo, b.accessNo, b.isActive, b.dtAdded,
                           CONCAT(IFNULL(e.firstName, ''), ' ', 
                                  IFNULL(CONCAT(e.middleName, ' '), ''), 
                                  IFNULL(e.lastName, '')) as employeeName
                    FROM e_biometrics b
                    LEFT JOIN e_basicinfo e ON b.employeeNo = e.employeeNo
                    WHERE b.isActive = 0 
                    ORDER BY b.dtAdded DESC";

                var biometrics = _db.Query<BiometricsModel>(sql).ToList();
                return Json(new { data = biometrics });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeletedBiometricsList: {ex.Message}");
                return Json(new { data = new List<BiometricsModel>() });
            }
        }

        // Get single biometric record by ID
        [HttpGet]
        public JsonResult GetBiometric(int id)
        {
            try
            {
                var sql = @"
                    SELECT b.id, b.employeeNo, b.accessNo, b.isActive,
                           CONCAT(IFNULL(e.firstName, ''), ' ', 
                                  IFNULL(CONCAT(e.middleName, ' '), ''), 
                                  IFNULL(e.lastName, '')) as employeeName
                    FROM e_biometrics b
                    LEFT JOIN e_basicinfo e ON b.employeeNo = e.employeeNo
                    WHERE b.id = @Id AND b.isActive = 1";

                return Json(_db.QueryFirstOrDefault<BiometricsModel>(sql, new { Id = id }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetBiometric: {ex.Message}");
                return Json(null);
            }
        }

        // Get list of active employees for dropdown
        [HttpGet]
        public JsonResult GetEmployeeList()
        {
            try
            {
                var sql = @"
                    SELECT employeeNo, 
                           CONCAT(IFNULL(firstName, ''), ' ', 
                                  IFNULL(CONCAT(middleName, ' '), ''), 
                                  IFNULL(lastName, '')) as employeeName
                    FROM e_basicinfo 
                    WHERE isActive = 1 
                    ORDER BY firstName, lastName";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        // Add new biometric record
        [HttpPost]
        public JsonResult AddBiometric(BiometricsModel model)
        {
            try
            {
                // Validate employee exists
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Check if employee already has biometric record
                if (BiometricExists(model.employeeNo))
                    return Json(new { success = false, message = "Employee already has a biometric record!" });

                // Validate access number
                if (string.IsNullOrEmpty(model.accessNo?.Trim()))
                    return Json(new { success = false, message = "Access number is required!" });

                // Check if access number already exists
                if (AccessNoExists(model.accessNo.Trim()))
                    return Json(new { success = false, message = "Access number already exists!" });

                // Insert new biometric record with addedByUser from session
                var sql = @"
                    INSERT INTO e_biometrics (employeeNo, accessNo, isActive, dtAdded, addedByUser) 
                    VALUES (@employeeNo, @accessNo, 1, NOW(), @addedByUser);
                    SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    model.employeeNo,
                    accessNo = model.accessNo ?? "",
                    addedByUser = EmployeeNo // Using session EmployeeNo from BaseController
                });

                // Log to audit trail
                _auditTrail.Log("e_biometrics", newId, "CREATED",
                    $"Added biometric record for {model.employeeNo}, Access No: {model.accessNo}");

                return Json(new { success = true, message = "Biometric record added successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddBiometric: {ex.Message}");
                return Json(new { success = false, message = $"Error adding biometric record: {ex.Message}" });
            }
        }

        // Update existing biometric record
        [HttpPost]
        public JsonResult UpdateBiometric(BiometricsModel model)
        {
            try
            {
                // Check if the record exists and is active
                if (!RecordExists("e_biometrics", "id", model.id.ToString(), true))
                    return Json(new { success = false, message = "Biometric record not found!" });

                // Validate access number
                if (string.IsNullOrEmpty(model.accessNo?.Trim()))
                    return Json(new { success = false, message = "Access number is required!" });

                // Check if access number already exists for other records
                if (AccessNoExists(model.accessNo.Trim(), model.id))
                    return Json(new { success = false, message = "Access number already exists!" });

                // Update biometric record with lastModifiedByUser from session
                var sql = @"
                    UPDATE e_biometrics 
                    SET accessNo = @accessNo, 
                        dtLastModified = NOW(),
                        lastModifiedByUser = @lastModifiedByUser
                    WHERE id = @id";

                _db.Execute(sql, new
                {
                    model.id,
                    accessNo = model.accessNo ?? "",
                    lastModifiedByUser = EmployeeNo // Using session EmployeeNo from BaseController
                });

                // Log to audit trail
                _auditTrail.Log("e_biometrics", model.id, "UPDATED",
                    $"Updated biometric record for {model.employeeNo}, Access No: {model.accessNo}");

                return Json(new { success = true, message = "Biometric record updated successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateBiometric: {ex.Message}");
                return Json(new { success = false, message = $"Error updating biometric record: {ex.Message}" });
            }
        }

        // Soft delete biometric record
        [HttpPost]
        public JsonResult DeleteBiometric(int id, string reason = "")
        {
            try
            {
                // Get employee info before deleting
                var biometric = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo, accessNo FROM e_biometrics WHERE id = @id",
                    new { id });

                if (biometric == null)
                    return Json(new { success = false, message = "Biometric record not found!" });

                // Soft delete with deletedByUser from session
                var sql = @"
                    UPDATE e_biometrics 
                    SET isActive = 0, 
                        dtDeleted = NOW(),
                        deletedByUser = @deletedByUser
                    WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // Using session EmployeeNo from BaseController
                });

                // Log to audit trail
                _auditTrail.Log("e_biometrics", id, "DELETED",
                    $"Deleted biometric record for {biometric.employeeNo}, Access No: {biometric.accessNo}{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Biometric record deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteBiometric: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Restore deleted biometric record
        [HttpPost]
        public JsonResult RestoreBiometric(int id)
        {
            try
            {
                // Get employee info before restoring
                var biometric = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo, accessNo FROM e_biometrics WHERE id = @id",
                    new { id });

                if (biometric == null)
                    return Json(new { success = false, message = "Biometric record not found!" });

                // Restore with lastModifiedByUser from session
                var sql = @"
                    UPDATE e_biometrics 
                    SET isActive = 1, 
                        dtDeleted = NULL, 
                        deletedByUser = NULL,
                        dtLastModified = NOW(),
                        lastModifiedByUser = @lastModifiedByUser
                    WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    lastModifiedByUser = EmployeeNo // Using session EmployeeNo from BaseController
                });

                // Log to audit trail
                _auditTrail.Log("e_biometrics", id, "RESTORED",
                    $"Restored biometric record for {biometric.employeeNo}, Access No: {biometric.accessNo}");

                return Json(new { success = true, message = "Biometric record restored successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreBiometric: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Check if employee has biometric record
        [HttpGet]
        public JsonResult CheckEmployeeBiometric(string employeeNo)
        {
            try
            {
                return Json(new { exists = BiometricExists(employeeNo) });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CheckEmployeeBiometric: {ex.Message}");
                return Json(new { exists = false });
            }
        }

        // Validate access number uniqueness
        [HttpGet]
        public JsonResult ValidateAccessNo(string accessNo, int? excludeId = null)
        {
            try
            {
                return Json(new { isUnique = !AccessNoExists(accessNo, excludeId) });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ValidateAccessNo: {ex.Message}");
                return Json(new { isUnique = false });
            }
        }

        // Helper: Check if a record exists in a table
        private bool RecordExists(string table, string column, string value, bool checkActive = true)
        {
            var sql = $"SELECT COUNT(*) FROM {table} WHERE {column} = @value";
            if (checkActive) sql += " AND isActive = 1";
            return _db.QuerySingle<int>(sql, new { value }) > 0;
        }

        // Helper: Check if employee already has biometric record
        private bool BiometricExists(string employeeNo, int? excludeId = null)
        {
            var sql = "SELECT COUNT(*) FROM e_biometrics WHERE employeeNo = @employeeNo AND isActive = 1";
            if (excludeId.HasValue) sql += " AND id != @excludeId";
            return _db.QuerySingle<int>(sql, new { employeeNo, excludeId }) > 0;
        }

        // Helper: Check if access number already exists
        private bool AccessNoExists(string accessNo, int? excludeId = null)
        {
            var sql = "SELECT COUNT(*) FROM e_biometrics WHERE accessNo = @accessNo AND isActive = 1";
            if (excludeId.HasValue) sql += " AND id != @excludeId";
            return _db.QuerySingle<int>(sql, new { accessNo, excludeId }) > 0;
        }
    }
}