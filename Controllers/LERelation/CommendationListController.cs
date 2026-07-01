using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Models.LERelation;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.LERelation
{
    [ModuleAuthorize("FCommendation")]
    public class CommendationListController : BaseController
    {
        private readonly IDbConnection _db;

        public CommendationListController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/LERelation/CommendationList.cshtml");
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
        public JsonResult GetCommendationList(string status = "active", string employeeNo = "", string department = "")
        {
            try
            {
                var isActive = status.Equals("active", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

                var query = new StringBuilder(@"
                    SELECT c.id, c.employeeNo, c.commendationCode, c.activity,
                           DATE_FORMAT(c.dateissued, '%Y/%m/%d') as dateissued,
                           c.addedby as issuedBy, c.remarks, c.dtAdded,
                           CONCAT(IFNULL(e.firstName, ''), ' ',
                                  IFNULL(CONCAT(e.middleName, ' '), ''),
                                  IFNULL(e.lastName, '')) as employeeName,
                           IFNULL(s.commendationName, '') as commendationType
                    FROM e_commendation c
                    LEFT JOIN e_basicinfo e ON c.employeeNo = e.employeeNo
                    LEFT JOIN s_commendation s ON c.commendationCode = s.commendationCode
                    WHERE c.isActive = @isActive");

                var parameters = new DynamicParameters();
                parameters.Add("@isActive", isActive);

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenEmployeesFilter(query, parameters);

                if (!string.IsNullOrWhiteSpace(employeeNo))
                {
                    query.Append(" AND c.employeeNo = @empFilter");
                    parameters.Add("@empFilter", employeeNo);
                }

                if (!string.IsNullOrWhiteSpace(department) &&
                    !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND e.departmentCode = @department");
                    parameters.Add("@department", department);
                }

                query.Append(" ORDER BY c.dtAdded DESC");

                var commendations = _db.Query<CommendationListModel>(query.ToString(), parameters).ToList();
                return Json(new { data = commendations });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCommendationList: {ex.Message}");
                return Json(new { data = new List<CommendationListModel>(), error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetCommendation(int id)
        {
            try
            {
                var sql = @"
                    SELECT c.id, c.employeeNo, c.commendationCode, c.activity,
                           DATE_FORMAT(c.dateissued, '%Y/%m/%d') as dateissued,
                           c.addedby as issuedBy, c.remarks,
                           CONCAT(IFNULL(e.firstName, ''), ' ',
                                  IFNULL(CONCAT(e.middleName, ' '), ''),
                                  IFNULL(e.lastName, '')) as employeeName
                    FROM e_commendation c
                    LEFT JOIN e_basicinfo e ON c.employeeNo = e.employeeNo
                    WHERE c.id = @Id AND c.isActive = 1";

                var record = _db.QueryFirstOrDefault<CommendationListModel>(sql, new { Id = id });
                if (record == null)
                    return Json(null);

                // Row-level security: ensure caller may view this employee
                if (!CanViewEmployee(record.employeeNo))
                    return Json(new { error = "Access denied. You don't have permission to view this record." });

                return Json(record);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCommendation: {ex.Message}");
                return Json(null);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Employee list — scoped so only visible employees appear in dropdowns
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public JsonResult GetEmployeeList()
        {
            try
            {
                var query = new StringBuilder(@"
                    SELECT e.employeeNo,
                           CONCAT(IFNULL(e.firstName, ''), ' ',
                                  IFNULL(CONCAT(e.middleName, ' '), ''),
                                  IFNULL(e.lastName, '')) as employeeName
                    FROM e_basicinfo e
                    WHERE e.isActive = 1");

                var parameters = new DynamicParameters();

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenEmployeesFilter(query, parameters);

                query.Append(" ORDER BY e.firstName, e.lastName");

                return Json(_db.Query(query.ToString(), parameters).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public JsonResult GetCommendationTypes()
        {
            try
            {
                var sql = @"
                    SELECT commendationCode, commendationName
                    FROM s_commendation
                    WHERE isActive = 1
                    ORDER BY commendationName";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCommendationTypes: {ex.Message}");
                return Json(new List<object>());
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ADD
        // ─────────────────────────────────────────────────────────────────────

        [HttpPost]
        public JsonResult AddCommendation(CommendationListModel model, IFormFile attachment)
        {
            try
            {
                // Validate employee exists
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Row-level security: only allow if caller can manage this employee
                if (!CanViewEmployee(model.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to add a commendation for this employee." });

                // Validate commendation type exists
                if (!RecordExists("s_commendation", "commendationCode", model.commendationCode))
                    return Json(new { success = false, message = "Invalid commendation type!" });

                var sql = @"
                    INSERT INTO e_commendation (employeeNo, commendationCode, activity, dateissued,
                                               addedby, remarks, isActive, dtAdded)
                    VALUES (@employeeNo, @commendationCode, @activity, @dateissued,
                           @issuedBy, @remarks, 1, NOW())";

                _db.Execute(sql, new
                {
                    model.employeeNo,
                    model.commendationCode,
                    model.activity,
                    model.dateissued,
                    model.issuedBy,
                    remarks = model.remarks ?? ""
                });

                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Commendation saved but attachment failed: {uploadResult.message}" });
                }

                return Json(new { success = true, message = "Commendation added successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddCommendation: {ex.Message}");
                return Json(new { success = false, message = $"Error adding commendation: {ex.Message}" });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // UPDATE
        // ─────────────────────────────────────────────────────────────────────

        [HttpPost]
        public JsonResult UpdateCommendation(CommendationListModel model, IFormFile attachment)
        {
            try
            {
                // Check if the record exists
                if (!RecordExists("e_commendation", "id", model.id.ToString(), true))
                    return Json(new { success = false, message = "Commendation record not found!" });

                // Validate employee exists
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Row-level security check
                if (!CanViewEmployee(model.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to update this employee's commendation." });

                // Validate commendation type exists
                if (!RecordExists("s_commendation", "commendationCode", model.commendationCode))
                    return Json(new { success = false, message = "Invalid commendation type!" });

                var sql = @"
                    UPDATE e_commendation
                    SET employeeNo = @employeeNo,
                        commendationCode = @commendationCode,
                        activity = @activity,
                        dateissued = @dateissued,
                        addedby = @issuedBy,
                        remarks = @remarks,
                        dtModified = NOW()
                    WHERE id = @id";

                _db.Execute(sql, new
                {
                    model.id,
                    model.employeeNo,
                    model.commendationCode,
                    model.activity,
                    model.dateissued,
                    model.issuedBy,
                    remarks = model.remarks ?? ""
                });

                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Commendation updated but attachment failed: {uploadResult.message}" });
                }

                return Json(new { success = true, message = "Commendation updated successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateCommendation: {ex.Message}");
                return Json(new { success = false, message = $"Error updating commendation: {ex.Message}" });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SOFT DELETE
        // ─────────────────────────────────────────────────────────────────────

        [HttpPost]
        public JsonResult DeleteCommendation(int id)
        {
            try
            {
                // Fetch record first to verify ownership scope
                var record = _db.QueryFirstOrDefault<CommendationListModel>(
                    "SELECT employeeNo FROM e_commendation WHERE id = @id AND isActive = 1", new { id });

                if (record == null)
                    return Json(new { success = false, message = "Commendation record not found!" });

                if (!CanViewEmployee(record.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to delete this record." });

                _db.Execute("UPDATE e_commendation SET isActive = 0 WHERE id = @Id", new { Id = id });
                return Json(new { success = true, message = "Commendation deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteCommendation: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // RESTORE
        // ─────────────────────────────────────────────────────────────────────

        [HttpPost]
        public JsonResult RestoreCommendation(int id)
        {
            try
            {
                var record = _db.QueryFirstOrDefault<CommendationListModel>(
                    "SELECT employeeNo FROM e_commendation WHERE id = @id", new { id });

                if (record == null)
                    return Json(new { success = false, message = "Commendation record not found!" });

                if (!CanViewEmployee(record.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to restore this record." });

                _db.Execute("UPDATE e_commendation SET isActive = 1 WHERE id = @Id", new { Id = id });
                return Json(new { success = true, message = "Commendation restored successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreCommendation: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetCommendationAttachments(string employeeNo)
        {
            try
            {
                if (!CanViewEmployee(employeeNo))
                    return Json(new { error = "Access denied." });

                var sql = @"
                    SELECT id, attachmentPath, dtAdded
                    FROM e_attachment
                    WHERE employeeNo = @employeeNo
                    AND attachmentTypeCode = 'COMMENDATION'
                    AND isActive = 1
                    ORDER BY dtAdded DESC";

                return Json(_db.Query(sql, new { employeeNo }).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCommendationAttachments: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        [Route("CommendationTab/GetCommendationTab")]
        public IActionResult GetCommendationTab(string employeeNo, string mode = "EDIT")
        {
            // Row-level security
            if (!string.IsNullOrWhiteSpace(employeeNo) && !CanViewEmployee(employeeNo))
                return Content("<div class='alert alert-danger'>Access denied. You do not have permission to view this employee's commendations.</div>");

            var model = new CommendationListModel
            {
                employeeNo = employeeNo ?? string.Empty
            };

            return PartialView("~/Views/Users/Partials/_Commendation.cshtml", model);
        }

        [HttpGet]
        public JsonResult GetCommendationByEmployee(string employeeNo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(employeeNo))
                    return Json(new { data = new List<CommendationListModel>() });

                // Row-level security
                if (!CanViewEmployee(employeeNo))
                    return Json(new { data = new List<CommendationListModel>(), error = "Access denied." });

                var sql = @"
                    SELECT
                        c.id,
                        c.employeeNo,
                        c.commendationCode,
                        c.activity,
                        DATE_FORMAT(c.dateissued, '%Y/%m/%d') AS dateissued,
                        c.addedby                             AS issuedBy,
                        c.remarks,
                        c.dtAdded,
                        IFNULL(s.commendationName, '')        AS commendationType
                    FROM e_commendation c
                    LEFT JOIN s_commendation s ON c.commendationCode = s.commendationCode
                    WHERE c.employeeNo = @employeeNo
                      AND c.isActive   = 1
                    ORDER BY c.dateissued DESC, c.dtAdded DESC";

                var records = _db.Query<CommendationListModel>(sql, new { employeeNo }).AsList();
                return Json(new { data = records });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCommendationByEmployee: {ex.Message}");
                return Json(new { data = new List<CommendationListModel>(), error = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private bool RecordExists(string table, string column, string value, bool checkActive = false)
        {
            var sql = $"SELECT COUNT(*) FROM {table} WHERE {column} = @value";
            if (checkActive) sql += " AND isActive = 1";
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

                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "commendations");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                    file.CopyTo(stream);

                var sql = @"
                    INSERT INTO e_attachment (employeeNo, attachmentDescription, attachmentTypeCode,
                                             attachmentPath, isActive, dtAdded)
                    VALUES (@employeeNo, 'Commendation', 'COMMENDATION', @attachmentPath, 1, NOW())";

                _db.Execute(sql, new
                {
                    employeeNo,
                    attachmentPath = $"/uploads/commendations/{fileName}"
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