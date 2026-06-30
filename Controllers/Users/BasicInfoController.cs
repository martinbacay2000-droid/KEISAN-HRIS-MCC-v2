using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FBasicM")]
    public class BasicInfoController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public BasicInfoController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View();
        }

        // Get basic info for view/edit
        public IActionResult GetBasicInfo(string employeeNo, string mode = "EDIT")
        {
            try
            {
                if (mode == "CREATE" || string.IsNullOrEmpty(employeeNo))
                {
                    // Return empty model for New Employee
                    return PartialView("~/Views/Users/Partials/_BasicInfo.cshtml", new EmployeeBasicInfoModel());
                }

                var sql = @"
                    SELECT e.*, 
                           s.employmentStatusName,
                           sp.positionName,
                           sb.branchName,
                           sd.departmentName,
                           sr.rankName,
                           e.isActive
                    FROM e_basicinfo e
                    LEFT JOIN s_employmentstatus s ON e.employmentStatus = s.employmentStatusCode
                    LEFT JOIN s_position sp ON e.positionCode = sp.positionCode
                    LEFT JOIN s_branch sb ON e.branchCode = sb.branchCode
                    LEFT JOIN s_department sd ON e.departmentCode = sd.departmentCode
                    LEFT JOIN s_rank sr ON e.rankCode = sr.rankCode
                    WHERE e.employeeNo = @employeeNo";

                var employee = _db.QueryFirstOrDefault<EmployeeBasicInfoModel>(sql, new { employeeNo });

                if (employee == null)
                    employee = new EmployeeBasicInfoModel();

                return PartialView("~/Views/Users/Partials/_BasicInfo.cshtml", employee);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetBasicInfo: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_BasicInfo.cshtml", new EmployeeBasicInfoModel());
            }
        }

        // Create new employee with basic info
        [HttpPost]
        public JsonResult CreateBasicInfo(EmployeeBasicInfoModel model)
        {
            IDbTransaction trans = null;
            try
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(model.employeeNo))
                    return Json(new { success = false, message = "Employee number is required!" });

                if (string.IsNullOrWhiteSpace(model.firstName) || string.IsNullOrWhiteSpace(model.lastName))
                    return Json(new { success = false, message = "First name and last name are required!" });

                // Check if employee number already exists
                if (EmployeeExists(model.employeeNo))
                    return Json(new { success = false, message = "Employee number already exists!" });

                // Begin transaction
                if (_db.State != ConnectionState.Open)
                    ((IDbConnection)_db).Open();

                trans = _db.BeginTransaction();

                // Insert employee basic info
                var employeeSql = @"
                    INSERT INTO e_basicinfo (
                        employeeNo, employmentStatus, lastName, firstName, middleName, suffix,
                        dateHired, positionCode, rankCode, branchCode, departmentCode,
                        probationaryStartDate, probationaryEndDate, dateOfRegApp,
                        dateOfEmpTermInitial, reason4TermInitial, remarksInitial,
                        isRetired, isActive, dtAdded, addedByUser
                    ) VALUES (
                        @employeeNo, @employmentStatus, @lastName, @firstName, @middleName, @suffix,
                        @dateHired, @positionCode, @rankCode, @branchCode, @departmentCode,
                        @probationaryStartDate, @probationaryEndDate, @dateOfRegApp,
                        @dateOfEmpTermInitial, @reason4TermInitial, @remarksInitial,
                        @isRetired, 1, NOW(), @addedByUser
                    );
                    SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(employeeSql, new
                {
                    model.employeeNo,
                    model.isActive,
                    employmentStatus = model.employmentStatus ?? "",
                    lastName = model.lastName ?? "",
                    firstName = model.firstName ?? "",
                    middleName = model.middleName ?? "",
                    suffix = model.suffix ?? "",
                    dateHired = string.IsNullOrEmpty(model.dateHired) ? null : model.dateHired,
                    positionCode = model.positionCode ?? "",
                    rankCode = model.rankCode ?? "",
                    branchCode = model.branchCode ?? "",
                    departmentCode = model.departmentCode ?? "",
                    probationaryStartDate = string.IsNullOrEmpty(model.probationaryStartDate) ? null : model.probationaryStartDate,
                    probationaryEndDate = string.IsNullOrEmpty(model.probationaryEndDate) ? null : model.probationaryEndDate,
                    dateOfRegApp = string.IsNullOrEmpty(model.dateOfRegApp) ? null : model.dateOfRegApp,
                    dateOfEmpTermInitial = string.IsNullOrEmpty(model.dateOfEmpTermInitial) ? null : model.dateOfEmpTermInitial,
                    reason4TermInitial = model.reason4TermInitial ?? "",
                    remarksInitial = model.remarksInitial ?? "",
                    isRetired = model.isRetired,
                    addedByUser = EmployeeNo
                }, trans);

                // Insert user login account
                string defaultRoleCode = "GIVE ROLE ACCESS";

                var userSql = @"
                    INSERT INTO s_user (
                        userCode, username, password, lastName, firstName, middleName,
                        positionName, roleCode, isActive, islock, attempt, isScheduleUploader, dtAdded, addedByUser
                    ) VALUES (
                        @userCode, NULL, CAST(AES_ENCRYPT(@password, 'portal123') AS CHAR),
                        @lastName, @firstName, @middleName, NULL, @roleCode,
                        1, 0, 0, 0, NOW(), @addedByUser
                    )";

                _db.Execute(userSql, new
                {
                    userCode = model.employeeNo,
                    password = "DefaultPass123!",
                    lastName = model.lastName ?? "",
                    firstName = model.firstName ?? "",
                    middleName = string.IsNullOrWhiteSpace(model.middleName) ? null : model.middleName,
                    roleCode = defaultRoleCode,
                    addedByUser = EmployeeNo
                }, trans);

                trans.Commit();

                // Log to audit trail
                _auditTrail.Log("e_basicinfo", newId, "CREATED",
                    $"Created employee {model.employeeNo} - {model.firstName} {model.lastName}");

                return Json(new
                {
                    success = true,
                    message = "Employee and user account created successfully!",
                    employeeNo = model.employeeNo
                });
            }
            catch (Exception ex)
            {
                trans?.Rollback();
                Console.WriteLine($"Error in CreateBasicInfo: {ex.Message}");
                return Json(new { success = false, message = $"Error creating employee: {ex.Message}" });
            }
            finally
            {
                trans?.Dispose();
            }
        }

        // Update employee basic info
        [HttpPost]
        public JsonResult UpdateBasicInfo(EmployeeBasicInfoModel model)
        {
            try
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(model.firstName) || string.IsNullOrWhiteSpace(model.lastName))
                    return Json(new { success = false, message = "First name and last name are required!" });

                // Check if employee exists
                if (!RecordExists("e_basicinfo", "id", model.Id.ToString(), false))
                    return Json(new { success = false, message = "Employee not found!" });

                var sql = @"
                    UPDATE e_basicinfo 
                    SET employeeNo = @employeeNo,
                        employmentStatus = @employmentStatus,
                        lastName = @lastName,
                        firstName = @firstName,
                        middleName = @middleName,
                        suffix = @suffix,
                        dateHired = @dateHired,
                        positionCode = @positionCode,
                        rankCode = @rankCode,
                        branchCode = @branchCode,
                        departmentCode = @departmentCode,
                        probationaryStartDate = @probationaryStartDate,
                        probationaryEndDate = @probationaryEndDate,
                        dateOfRegApp = @dateOfRegApp,
                        dateOfEmpTermInitial = @dateOfEmpTermInitial,
                        reason4TermInitial = @reason4TermInitial,
                        remarksInitial = @remarksInitial,
                        isRetired = @isRetired,
                        isActive = @isActive, 
                        dtLastModified = NOW(),
                        lastModifiedByUser = @lastModifiedByUser
                    WHERE Id = @Id";

                _db.Execute(sql, new
                {
                    model.Id,
                    model.employeeNo,
                    model.isActive,
                    employmentStatus = model.employmentStatus ?? "",
                    lastName = model.lastName ?? "",
                    firstName = model.firstName ?? "",
                    middleName = model.middleName ?? "",
                    suffix = model.suffix ?? "",
                    dateHired = string.IsNullOrEmpty(model.dateHired) ? null : model.dateHired,
                    positionCode = model.positionCode ?? "",
                    rankCode = model.rankCode ?? "",
                    branchCode = model.branchCode ?? "",
                    departmentCode = model.departmentCode ?? "",
                    probationaryStartDate = string.IsNullOrEmpty(model.probationaryStartDate) ? null : model.probationaryStartDate,
                    probationaryEndDate = string.IsNullOrEmpty(model.probationaryEndDate) ? null : model.probationaryEndDate,
                    dateOfRegApp = string.IsNullOrEmpty(model.dateOfRegApp) ? null : model.dateOfRegApp,
                    dateOfEmpTermInitial = string.IsNullOrEmpty(model.dateOfEmpTermInitial) ? null : model.dateOfEmpTermInitial,
                    reason4TermInitial = model.reason4TermInitial ?? "",
                    remarksInitial = model.remarksInitial ?? "",
                    isRetired = model.isRetired,
                    lastModifiedByUser = EmployeeNo
                });

                // Log to audit trail
                _auditTrail.Log("e_basicinfo", model.Id, "UPDATED",
                    $"Updated employee {model.employeeNo} - {model.firstName} {model.lastName}");

                return Json(new { success = true, message = "Employee basic info updated successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateBasicInfo: {ex.Message}");
                return Json(new { success = false, message = $"Error updating employee info: {ex.Message}" });
            }
        }

        // Check if employee number exists
        [HttpGet]
        public JsonResult CheckEmployeeNo(string employeeNo, int? excludeId = null)
        {
            try
            {
                return Json(new { exists = EmployeeExists(employeeNo, excludeId) });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CheckEmployeeNo: {ex.Message}");
                return Json(new { exists = false });
            }
        }

        // Helper: Check if a record exists in a table
        private bool RecordExists(string table, string column, string value, bool checkActive = true)
        {
            var sql = $"SELECT COUNT(*) FROM {table} WHERE {column} = @value";
            if (checkActive) sql += " AND isActive = 1";
            return _db.QuerySingle<int>(sql, new { value }) > 0;
        }

        // Helper: Check if employee number already exists
        private bool EmployeeExists(string employeeNo, int? excludeId = null)
        {
            var sql = "SELECT COUNT(*) FROM e_basicinfo WHERE employeeNo = @employeeNo AND isActive = 1";
            if (excludeId.HasValue)
                sql += " AND id != @excludeId";
            return _db.QuerySingle<int>(sql, new { employeeNo, excludeId }) > 0;
        }
    }
}