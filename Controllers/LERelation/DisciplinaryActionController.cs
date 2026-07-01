using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Models.LERelation;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.LERelation
{
    [ModuleAuthorize("FDisciplinaryAction")]
    public class DisciplinaryActionController : BaseController
    {
        private readonly IDbConnection _db;

        public DisciplinaryActionController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/LERelation/DisciplinaryAction.cshtml");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Data scope + hidden employees filters
        // Table alias "e" matches the e_basicinfo join alias used in queries below
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyDataScopeFilter(StringBuilder query, DynamicParameters parameters)
        {
            DataScopeHelper.ApplyDataScopeFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "e");
        }

        private void ApplyHiddenEmployeesFilter(StringBuilder query, DynamicParameters parameters)
        {
            DataScopeHelper.ApplyHiddenEmployeesFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "e");
        }

        private bool CanViewEmployee(string employeeNo)
        {
            return DataScopeHelper.CanViewEmployee(_db, EmployeeNo, RoleCode, employeeNo);
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET LIST — active or inactive, scoped to current user's data access
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public JsonResult GetDisciplinaryActionList(string status = "active", string employeeNo = "", string department = "")
        {
            try
            {
                var isActive = status.Equals("active", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

                var query = new StringBuilder(@"
                    SELECT
                        d.id,
                        d.employeeNo,
                        d.offense,
                        d.complainant,
                        d.section,
                        d.disciplinaryReason,
                        d.disciplinaryAction,
                        d.penalty,
                        DATE_FORMAT(d.dateIssued, '%m/%d/%Y') AS dateIssued,
                        d.addedByUser,
                        d.dtAdded,
                        CONCAT(
                            IFNULL(e.firstName, ''), ' ',
                            IFNULL(CONCAT(e.middleName, ' '), ''),
                            IFNULL(e.lastName, '')
                        ) AS employeeName
                    FROM e_disciplinaryaction d
                    LEFT JOIN e_basicinfo e ON e.employeeNo = d.employeeNo
                    WHERE d.isActive = @isActive");

                var parameters = new DynamicParameters();
                parameters.Add("@isActive", isActive);

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenEmployeesFilter(query, parameters);

                if (!string.IsNullOrWhiteSpace(employeeNo))
                {
                    query.Append(" AND d.employeeNo = @empFilter");
                    parameters.Add("@empFilter", employeeNo);
                }

                if (!string.IsNullOrWhiteSpace(department) &&
                    !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND e.departmentCode = @department");
                    parameters.Add("@department", department);
                }

                query.Append(" ORDER BY d.dtAdded DESC");

                var records = _db.Query<DisciplinaryActionModel>(query.ToString(), parameters).AsList();
                return Json(new { data = records });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDisciplinaryActionList: {ex.Message}");
                return Json(new { data = new List<DisciplinaryActionModel>(), error = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET SINGLE RECORD
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public JsonResult GetDisciplinaryAction(int id)
        {
            try
            {
                var sql = @"
                    SELECT
                        d.id,
                        d.employeeNo,
                        d.offense,
                        d.complainant,
                        d.section,
                        d.disciplinaryReason,
                        d.disciplinaryAction,
                        d.penalty,
                        DATE_FORMAT(d.dateIssued, '%m/%d/%Y') AS dateIssued,
                        d.addedByUser,
                        CONCAT(
                            IFNULL(e.firstName, ''), ' ',
                            IFNULL(CONCAT(e.middleName, ' '), ''),
                            IFNULL(e.lastName, '')
                        ) AS employeeName
                    FROM e_disciplinaryaction d
                    LEFT JOIN e_basicinfo e ON e.employeeNo = d.employeeNo
                    WHERE d.id = @id";

                var record = _db.QueryFirstOrDefault<DisciplinaryActionModel>(sql, new { id });
                if (record == null)
                    return Json(null);

                // Row-level security check
                if (!CanViewEmployee(record.employeeNo))
                    return Json(new { error = "Access denied. You don't have permission to view this record." });

                return Json(record);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDisciplinaryAction: {ex.Message}");
                return Json(null);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET EMPLOYEE LIST — scoped to current user's data access
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public JsonResult GetEmployeeList()
        {
            try
            {
                var query = new StringBuilder(@"
                    SELECT
                        e.employeeNo,
                        CONCAT(
                            IFNULL(e.firstName, ''), ' ',
                            IFNULL(CONCAT(e.middleName, ' '), ''),
                            IFNULL(e.lastName, '')
                        ) AS employeeName
                    FROM e_basicinfo e
                    WHERE e.isActive = 1");

                var parameters = new DynamicParameters();

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenEmployeesFilter(query, parameters);

                query.Append(" ORDER BY e.firstName, e.lastName");

                return Json(_db.Query(query.ToString(), parameters).AsList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ADD
        // ─────────────────────────────────────────────────────────────────────

        [HttpPost]
        public JsonResult AddDisciplinaryAction(DisciplinaryActionModel model, IFormFile attachment)
        {
            try
            {
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Row-level security check
                if (!CanViewEmployee(model.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to add a disciplinary action for this employee." });

                var sql = @"
                    INSERT INTO e_disciplinaryaction
                        (employeeNo, offense, complainant, section, disciplinaryReason,
                         disciplinaryAction, penalty, dateIssued, isActive, dtAdded, addedByUser)
                    VALUES
                        (@employeeNo, @offense, @complainant, @section, @disciplinaryReason,
                         @disciplinaryAction, @penalty, @dateIssued, 1, NOW(), @addedByUser)";

                _db.Execute(sql, new
                {
                    model.employeeNo,
                    offense = model.offense?.Trim() ?? "",
                    complainant = model.complainant?.Trim() ?? "",
                    section = model.section?.Trim() ?? "",
                    disciplinaryReason = model.disciplinaryReason?.Trim() ?? "",
                    disciplinaryAction = model.disciplinaryAction?.Trim() ?? "",
                    penalty = model.penalty?.Trim() ?? "",
                    model.dateIssued,
                    addedByUser = EmployeeNo
                });

                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Record saved but attachment failed: {uploadResult.message}" });
                }

                return Json(new { success = true, message = "Disciplinary Action added successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddDisciplinaryAction: {ex.Message}");
                return Json(new { success = false, message = $"Error adding record: {ex.Message}" });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // UPDATE
        // ─────────────────────────────────────────────────────────────────────

        [HttpPost]
        public JsonResult UpdateDisciplinaryAction(DisciplinaryActionModel model, IFormFile attachment)
        {
            try
            {
                if (!RecordExists("e_disciplinaryaction", "id", model.id.ToString()))
                    return Json(new { success = false, message = "Record not found!" });

                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Row-level security check
                if (!CanViewEmployee(model.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to update this employee's disciplinary action." });

                var sql = @"
                    UPDATE e_disciplinaryaction
                    SET employeeNo         = @employeeNo,
                        offense            = @offense,
                        complainant        = @complainant,
                        section            = @section,
                        disciplinaryReason = @disciplinaryReason,
                        disciplinaryAction = @disciplinaryAction,
                        penalty            = @penalty,
                        dateIssued         = @dateIssued,
                        dtModified         = NOW(),
                        modifiedByUser     = @modifiedByUser
                    WHERE id = @id";

                _db.Execute(sql, new
                {
                    model.id,
                    model.employeeNo,
                    offense = model.offense?.Trim() ?? "",
                    complainant = model.complainant?.Trim() ?? "",
                    section = model.section?.Trim() ?? "",
                    disciplinaryReason = model.disciplinaryReason?.Trim() ?? "",
                    disciplinaryAction = model.disciplinaryAction?.Trim() ?? "",
                    penalty = model.penalty?.Trim() ?? "",
                    model.dateIssued,
                    modifiedByUser = EmployeeNo
                });

                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Record updated but attachment failed: {uploadResult.message}" });
                }

                return Json(new { success = true, message = "Disciplinary Action updated successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateDisciplinaryAction: {ex.Message}");
                return Json(new { success = false, message = $"Error updating record: {ex.Message}" });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SOFT DELETE
        // ─────────────────────────────────────────────────────────────────────

        [HttpPost]
        public JsonResult DeleteDisciplinaryAction(int id)
        {
            try
            {
                var record = _db.QueryFirstOrDefault<DisciplinaryActionModel>(
                    "SELECT employeeNo FROM e_disciplinaryaction WHERE id = @id AND isActive = 1", new { id });

                if (record == null)
                    return Json(new { success = false, message = "Record not found!" });

                if (!CanViewEmployee(record.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to delete this record." });

                _db.Execute(@"
                    UPDATE e_disciplinaryaction
                    SET isActive = 0, dtDeleted = NOW(), deletedByuser = @deletedBy
                    WHERE id = @id",
                    new { id, deletedBy = EmployeeNo });

                return Json(new { success = true, message = "Disciplinary Action deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteDisciplinaryAction: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // RESTORE
        // ─────────────────────────────────────────────────────────────────────

        [HttpPost]
        public JsonResult RestoreDisciplinaryAction(int id)
        {
            try
            {
                var record = _db.QueryFirstOrDefault<DisciplinaryActionModel>(
                    "SELECT employeeNo FROM e_disciplinaryaction WHERE id = @id", new { id });

                if (record == null)
                    return Json(new { success = false, message = "Record not found!" });

                if (!CanViewEmployee(record.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to restore this record." });

                _db.Execute(@"
                    UPDATE e_disciplinaryaction
                    SET isActive = 1, dtDeleted = NULL, deletedByuser = NULL
                    WHERE id = @id",
                    new { id });

                return Json(new { success = true, message = "Disciplinary Action restored successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreDisciplinaryAction: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET ATTACHMENTS
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public JsonResult GetDisciplinaryAttachments(string employeeNo)
        {
            try
            {
                if (!CanViewEmployee(employeeNo))
                    return Json(new { error = "Access denied." });

                var sql = @"
                    SELECT id, attachmentPath, dtAdded
                    FROM e_attachment
                    WHERE employeeNo         = @employeeNo
                      AND attachmentTypeCode = 'DISCIPLINARY'
                      AND isActive           = 1
                    ORDER BY dtAdded DESC";

                return Json(_db.Query(sql, new { employeeNo }).AsList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDisciplinaryAttachments: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        [Route("DisciplinaryActionTab/GetDisciplinaryActionTab")]
        public IActionResult GetDisciplinaryActionTab(string employeeNo, string mode = "EDIT")
        {
            // Row-level security — deny if the logged-in user has no access to this employee
            if (!string.IsNullOrWhiteSpace(employeeNo) && !CanViewEmployee(employeeNo))
                return Content("<div class='alert alert-danger'>Access denied. You do not have permission to view this employee's disciplinary records.</div>");

            var model = new DisciplinaryActionModel
            {
                employeeNo = employeeNo ?? string.Empty
            };

            return PartialView("~/Views/Users/Partials/_DisciplinaryAction.cshtml", model);
        }

        // ─────────────────────────────────────────────────────────────────────
        // EMPLOYEE DETAILS TAB — JSON data endpoint
        // Returns active disciplinary records for a single employee.
        // Reuses the same SELECT columns as GetDisciplinaryActionList.
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public JsonResult GetDisciplinaryActionByEmployee(string employeeNo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(employeeNo))
                    return Json(new { data = new List<DisciplinaryActionModel>() });

                // Row-level security
                if (!CanViewEmployee(employeeNo))
                    return Json(new { data = new List<DisciplinaryActionModel>(), error = "Access denied." });

                var sql = @"
                    SELECT
                        d.id,
                        d.employeeNo,
                        d.offense,
                        d.complainant,
                        d.section,
                        d.disciplinaryReason,
                        d.disciplinaryAction,
                        d.penalty,
                        DATE_FORMAT(d.dateIssued, '%m/%d/%Y') AS dateIssued,
                        d.addedByUser,
                        d.dtAdded
                    FROM e_disciplinaryaction d
                    WHERE d.employeeNo = @employeeNo
                      AND d.isActive   = 1
                    ORDER BY d.dateIssued DESC, d.dtAdded DESC";

                var records = _db.Query<DisciplinaryActionModel>(sql, new { employeeNo }).AsList();
                return Json(new { data = records });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDisciplinaryActionByEmployee: {ex.Message}");
                return Json(new { data = new List<DisciplinaryActionModel>(), error = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private bool RecordExists(string table, string column, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var sql = $"SELECT COUNT(*) FROM {table} WHERE {column} = @value";
            return _db.QuerySingle<int>(sql, new { value }) > 0;
        }

        private (bool success, string message) SaveAttachment(string employeeNo, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return (false, "No file provided");

                if (file.Length > 5 * 1024 * 1024)
                    return (false, "File size exceeds 5MB limit");

                var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" };
                var fileExtension = Path.GetExtension(file.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                    return (false, "Invalid file format. Allowed: PDF, JPG, PNG, DOC, DOCX");

                var uploadsFolder = Path.Combine(
                    Directory.GetCurrentDirectory(), "wwwroot", "uploads", "disciplinaryaction");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                    file.CopyTo(stream);

                var sql = @"
                    INSERT INTO e_attachment
                        (employeeNo, attachmentDescription, attachmentTypeCode, attachmentPath, isActive, dtAdded)
                    VALUES
                        (@employeeNo, 'Disciplinary Action', 'DISCIPLINARY', @attachmentPath, 1, NOW())";

                _db.Execute(sql, new
                {
                    employeeNo,
                    attachmentPath = $"/uploads/disciplinaryaction/{fileName}"
                });

                return (true, "Attachment saved successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving attachment: {ex.Message}");
                return (false, $"Error saving attachment: {ex.Message}");
            }
        }
    }
}