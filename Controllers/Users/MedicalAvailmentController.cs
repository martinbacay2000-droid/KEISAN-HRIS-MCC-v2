using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Drawing;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FSMedicalAvailmentM")]
    public class MedicalAvailmentController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        // Inject the audit trail service
        public MedicalAvailmentController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Users/Partials/_MedicalAvailment.cshtml");
        }

        public IActionResult GetMedicalAvailment(string employeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(employeeNo))
                    return PartialView("~/Views/Users/Partials/_MedicalAvailment.cshtml", new List<MedicalAvailmentModel>());

                var employeeName = _db.QueryFirstOrDefault<string>(
                    @"SELECT CONCAT(lastName, ', ', firstName, ' ', COALESCE(middleName, '')) 
                      FROM e_basicinfo 
                      WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                ViewBag.EmployeeNo = employeeNo;
                ViewBag.EmployeeName = employeeName ?? "Unknown Employee";

                return PartialView("~/Views/Users/Partials/_MedicalAvailment.cshtml", new List<MedicalAvailmentModel>());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetMedicalAvailment: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_MedicalAvailment.cshtml", new List<MedicalAvailmentModel>());
            }
        }

        [HttpGet]
        public JsonResult GetMedicalAvailmentData(string employeeNo, string status = "active")
        {
            try
            {
                var query = new StringBuilder(@"
                    SELECT 
                        id, employeeNo, availeeNo, Name, AvaileeType, Relationship, AvailableInsurance,
                        0 as InPatient, 0 as OutPatient, 0 as Dental,
                        AvailableInsurance as Balance, dtAdded, addedBy, isActive
                    FROM e_availee
                    WHERE employeeNo = @EmployeeNo");

                query.Append(status == "active" ? " AND isActive = 1" : " AND isActive = 0");
                query.Append(" ORDER BY dtAdded DESC");

                var results = _db.Query(query.ToString(), new { EmployeeNo = employeeNo }).ToList();
                return Json(new { data = results });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetMedicalAvailmentData: {ex.Message}");
                return Json(new { data = new List<object>() });
            }
        }

        [HttpGet]
        public JsonResult GetMedicalAvailmentById(int id)
        {
            try
            {
                var sql = @"
                    SELECT 
                        id, employeeNo, availeeNo, Name, AvaileeType, Relationship, AvailableInsurance,
                        CASE WHEN isActive = 1 THEN true ELSE false END as isActive
                    FROM e_availee
                    WHERE id = @Id";

                var result = _db.QueryFirstOrDefault(sql, new { Id = id });

                if (result != null)
                {
                    var medicalAvailment = new
                    {
                        id = result.id,
                        employeeNo = result.employeeNo,
                        availeeNo = result.availeeNo,
                        name = result.Name,
                        availeeType = result.AvaileeType,
                        relationship = result.Relationship,
                        availableInsurance = result.AvailableInsurance,
                        isActive = Convert.ToBoolean(result.isActive)
                    };

                    return Json(new { success = true, data = medicalAvailment });
                }

                return Json(new { success = false, message = "Medical availment not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetMedicalAvailmentById: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving medical availment: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult SaveMedicalAvailment([FromBody] MedicalAvailmentDto model)
        {
            try
            {
                if (model == null || string.IsNullOrEmpty(model.EmployeeNo) || string.IsNullOrEmpty(model.Name))
                    return Json(new { success = false, message = "Invalid data provided." });

                string relationship = model.AvaileeType == "Principal" ? "Principal" : model.Relationship;

                if (model.Id.HasValue && model.Id > 0)
                    return UpdateAvailment(model, relationship);
                else
                    return AddAvailment(model, relationship);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveMedicalAvailment: {ex.Message}");
                return Json(new { success = false, message = "Error saving medical availment: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult SoftDeleteMedicalAvailment(int id, string reason = "") // Add reason parameter
        {
            try
            {
                var sql = @"
                    UPDATE e_availee 
                    SET isActive = 0, dtModified = NOW()
                    WHERE id = @Id AND isActive = 1";

                int rowsAffected = _db.Execute(sql, new { Id = id });

                if (rowsAffected > 0)
                {
                    // Log to audit trail with optional reason
                    _auditTrail.Log("e_availee", id, "DELETED",
                        $"Medical availment soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Medical availment deleted successfully!" })
                    : Json(new { success = false, message = "Failed to delete medical availment or record not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SoftDeleteMedicalAvailment: {ex.Message}");
                return Json(new { success = false, message = "Error deleting medical availment: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreMedicalAvailment(int id)
        {
            try
            {
                var sql = @"
                    UPDATE e_availee 
                    SET isActive = 1, dtModified = NOW()
                    WHERE id = @Id AND isActive = 0";

                int rowsAffected = _db.Execute(sql, new { Id = id });

                if (rowsAffected > 0)
                {
                    // Log to audit trail
                    _auditTrail.Log("e_availee", id, "RESTORED", "Medical availment restored");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Medical availment restored successfully!" })
                    : Json(new { success = false, message = "Failed to restore medical availment or record not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreMedicalAvailment: {ex.Message}");
                return Json(new { success = false, message = "Error restoring medical availment: " + ex.Message });
            }
        }

        // Private Helper Methods
        private JsonResult UpdateAvailment(MedicalAvailmentDto model, string relationship)
        {
            var sql = @"
                UPDATE e_availee 
                SET Name = @Name, AvaileeType = @AvaileeType, Relationship = @Relationship,
                    AvailableInsurance = @AvailableInsurance, dtModified = NOW()
                WHERE id = @Id AND isActive = 1";

            int rowsAffected = _db.Execute(sql, new
            {
                Id = model.Id,
                Name = model.Name,
                AvaileeType = model.AvaileeType,
                Relationship = relationship,
                AvailableInsurance = model.AvailableInsurance
            });

            if (rowsAffected > 0)
            {
                // Log to audit trail
                _auditTrail.Log("e_availee", model.Id.Value, "UPDATED",
                    $"Updated availee: {model.Name} - {model.AvaileeType}");
            }

            return rowsAffected > 0
                ? Json(new { success = true, message = "Medical availment updated successfully!" })
                : Json(new { success = false, message = "Failed to update medical availment." });
        }

        private JsonResult AddAvailment(MedicalAvailmentDto model, string relationship)
        {
            // Check if Principal already exists
            if (model.AvaileeType == "Principal")
            {
                var existingPrincipal = _db.QueryFirstOrDefault(
                    "SELECT id FROM e_availee WHERE employeeNo = @EmployeeNo AND AvaileeType = 'Principal' AND isActive = 1",
                    new { EmployeeNo = model.EmployeeNo });

                if (existingPrincipal != null)
                    return Json(new { success = false, message = "Principal availee already exists for this employee!" });
            }

            string availeeNo = GenerateAvaileeNo(model.EmployeeNo);

            var sql = @"
                INSERT INTO e_availee 
                (employeeNo, availeeNo, Name, AvaileeType, Relationship, AvailableInsurance, dtAdded, addedBy, isActive)
                VALUES (@EmployeeNo, @AvaileeNo, @Name, @AvaileeType, @Relationship, @AvailableInsurance, NOW(), @AddedBy, @IsActive);
                SELECT LAST_INSERT_ID();";

            int newId = _db.QuerySingle<int>(sql, new
            {
                EmployeeNo = model.EmployeeNo,
                AvaileeNo = availeeNo,
                Name = model.Name,
                AvaileeType = model.AvaileeType,
                Relationship = relationship,
                AvailableInsurance = model.AvailableInsurance,
                AddedBy = EmployeeNo,
                IsActive = model.IsActive ? 1 : 0
            });

            if (newId > 0)
            {
                // Log to audit trail
                _auditTrail.Log("e_availee", newId, "CREATED",
                    $"Added availee: {model.Name} - {model.AvaileeType}");
            }

            return newId > 0
                ? Json(new { success = true, message = "Medical availment added successfully!" })
                : Json(new { success = false, message = "Failed to add medical availment." });
        }

        private string GenerateAvaileeNo(string employeeNo)
        {
            try
            {
                int count = _db.QuerySingleOrDefault<int>(
                    "SELECT COUNT(*) FROM e_availee WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                return $"{employeeNo}-{(count + 1).ToString("D2")}";
            }
            catch
            {
                return $"{employeeNo}-{DateTime.Now:yyyyMMddHHmmss}";
            }
        }
    }
}