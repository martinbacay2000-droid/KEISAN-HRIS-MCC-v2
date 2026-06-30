using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FemployeeM")]
    public class UsersController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public UsersController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            ViewBag.RoleCode = RoleCode;
            return View();
        }

        public IActionResult EmployeeProfile()
        {
            ViewBag.RoleCode = RoleCode;
            ViewBag.CoeAccess = CoeAccessHelper.GetCoeAccess(RoleCode).ToString();
            ViewBag.CanPrintPhilHealthCert = PhilHealthCertAccessHelper.CanPrint(RoleCode);
            return View();
        }

        public IActionResult EmployeeBenefitSettings()
        {
            return View();
        }

        public IActionResult EmployeeApproverSettings()
        {
            return View();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Data scope + hidden employees filters — now delegated to DataScopeHelper
        // Table alias "e" matches this controller's e_basicinfo alias
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
        // Get employee list with filters AND dynamic row-level security
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public JsonResult GetUserList(string status, string employmentStatus, string branch, string department, string rank)
        {
            try
            {
                var query = new StringBuilder(@"
                    SELECT
                        e.id,
                        e.employeeNo,
                        CONCAT(e.lastName, ', ', e.firstName, ' ', LEFT(IFNULL(e.middleName,''), 1), '.') AS employeeName,
                        sp.positionName,
                        sr.rankCode,
                        sr.rankName,
                        sb.branchName,
                        ep.profilePicturePath,
                        e.dateHired,
                        e.dateOfEmpTermInitial,
                        e.dateOfEmpTermRehired,
                        e.isActive
                    FROM e_basicinfo e
                    LEFT JOIN s_position sp ON sp.positionCode = e.positionCode
                    LEFT JOIN s_rank sr ON sr.rankCode = e.rankCode
                    LEFT JOIN s_branch sb ON sb.branchCode = e.branchCode
                    LEFT JOIN e_profile ep ON ep.employeeNo = e.employeeNo AND ep.isActive = 1
                    WHERE e.employmentStatus <> 'TEMPORARY1'");

                var parameters = new DynamicParameters();

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenEmployeesFilter(query, parameters);

                if (string.IsNullOrWhiteSpace(status) || status.Equals("All", StringComparison.OrdinalIgnoreCase))
                    query.Append(" AND e.isActive IS NOT NULL");
                else
                {
                    query.Append(" AND e.isActive = @status");
                    parameters.Add("@status", status);
                }

                if (!string.IsNullOrWhiteSpace(employmentStatus) && !employmentStatus.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND e.employmentStatus = @employmentStatus");
                    parameters.Add("@employmentStatus", employmentStatus);
                }

                if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND e.branchCode = @branch");
                    parameters.Add("@branch", branch);
                }

                if (!string.IsNullOrWhiteSpace(department) && !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND e.departmentCode = @department");
                    parameters.Add("@department", department);
                }

                if (!string.IsNullOrWhiteSpace(rank) && !rank.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND e.rankCode = @rank");
                    parameters.Add("@rank", rank);
                }

                query.Append(" ORDER BY e.lastName, e.firstName");

                var employees = _db.Query<dynamic>(query.ToString(), parameters).ToList();
                return Json(new { data = employees });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetUserList: {ex.Message}");
                return Json(new { data = new List<object>(), error = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Get single employee WITH security check
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public JsonResult GetEmployee(string employeeNo)
        {
            try
            {
                if (!CanViewEmployee(employeeNo))
                    return Json(new { error = "Access denied. You don't have permission to view this employee." });

                var sql = @"
                    SELECT
                        e.*,
                        sp.positionName,
                        sr.rankName,
                        sb.branchName,
                        sd.departmentName,
                        su.unitName,
                        ses.employmentStatusName,
                        CONCAT(IFNULL(sup.firstName, ''), ' ', IFNULL(CONCAT(sup.middleName, ' '), ''), IFNULL(sup.lastName, '')) as supervisorName
                    FROM e_basicinfo e
                    LEFT JOIN s_position sp ON e.positionCode = sp.positionCode
                    LEFT JOIN s_rank sr ON e.rankCode = sr.rankCode
                    LEFT JOIN s_branch sb ON e.branchCode = sb.branchCode
                    LEFT JOIN s_department sd ON e.departmentCode = sd.departmentCode
                    LEFT JOIN s_unit su ON e.unitCode = su.unitCode
                    LEFT JOIN s_employmentstatus ses ON e.employmentStatus = ses.employmentStatusCode
                    LEFT JOIN e_basicinfo sup ON e.supervisorNo = sup.employeeNo
                    WHERE e.employeeNo = @employeeNo";

                return Json(_db.QueryFirstOrDefault<EmployeeBasicInfoModel>(sql, new { employeeNo }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployee: {ex.Message}");
                return Json(null);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Delete / Restore WITH permission checks
        // ─────────────────────────────────────────────────────────────────────

        [HttpPost]
        public JsonResult DeleteUser(string employeeNo, string reason = "")
        {
            try
            {
                var accessLevel = GetModuleAccessLevel("FemployeeM");
                if (accessLevel != "FULL")
                    return Json(new { success = false, message = "You don't have permission to delete employees." });

                if (!CanViewEmployee(employeeNo))
                    return Json(new { success = false, message = "Access denied. You cannot delete this employee." });

                var employee = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT CONCAT(firstName, ' ', lastName) as employeeName FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { employeeNo });

                if (employee == null)
                    return Json(new { success = false, message = "Employee not found!" });

                _db.Execute(@"
                    UPDATE e_basicinfo
                    SET dtDeleted = NOW(), isActive = 0, deletedByUser = @deletedBy
                    WHERE employeeNo = @employeeNo",
                    new { employeeNo, deletedBy = EmployeeNo });

                _auditTrail.Log("e_basicinfo", 0, "DELETED",
                    $"Deleted employee {employeeNo} - {employee.employeeName}{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Employee deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteUser: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreUser(string employeeNo)
        {
            try
            {
                var accessLevel = GetModuleAccessLevel("FemployeeM");
                if (accessLevel != "FULL")
                    return Json(new { success = false, message = "You don't have permission to restore employees." });

                var employee = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT CONCAT(firstName, ' ', lastName) as employeeName FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { employeeNo });

                if (employee == null)
                    return Json(new { success = false, message = "Employee not found!" });

                _db.Execute(@"
                    UPDATE e_basicinfo
                    SET isActive = 1, dtDeleted = NULL, deletedByUser = NULL
                    WHERE employeeNo = @employeeNo",
                    new { employeeNo });

                _auditTrail.Log("e_basicinfo", 0, "RESTORED",
                    $"Restored employee {employeeNo} - {employee.employeeName}");

                return Json(new { success = true, message = "Employee restored successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreUser: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private string GetModuleAccessLevel(string moduleCode)
        {
            if (RoleCode == "RL-000000") return "FULL";

            var json = HttpContext.Session.GetString("ROLE_ACCESS");
            if (string.IsNullOrEmpty(json)) return "NO_ACCESS";

            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict != null && dict.ContainsKey(moduleCode) ? dict[moduleCode] : "NO_ACCESS";
        }

        // Legacy partial view methods — kept for compatibility
        public IActionResult GetPersonalInfo(string employeeNo)
        {
            if (!CanViewEmployee(employeeNo))
                return Unauthorized();

            var employee = _db.QueryFirstOrDefault<userPersonalInfo>(
                "SELECT * FROM e_personalinfo WHERE employeeNo = @employeeNo", new { employeeNo });
            return PartialView("Partials/_PersonalInfo", employee);
        }

        public IActionResult GetEducationalBackground(string employeeNo)
        {
            if (!CanViewEmployee(employeeNo))
                return Unauthorized();

            var employee = _db.QueryFirstOrDefault<userEducationalBackground>(
                "SELECT * FROM e_basicinfo WHERE employeeNo = @employeeNo", new { employeeNo });
            return PartialView("Partials/_EducationalBackground", employee);
        }
    }
}