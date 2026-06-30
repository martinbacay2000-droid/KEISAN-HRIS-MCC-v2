using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SroleM")]
    public class RoleController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public RoleController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/Role.cshtml");
        }

        [HttpGet]
        public JsonResult GetRolelist()
        {
            string sql = @"
                SELECT id, roleCode, roleName, scopeType
                FROM s_role
                WHERE isActive = '1'
                ORDER BY roleName";
            var role = _db.Query<dynamic>(sql).ToList();
            return Json(new { data = role });
        }

        [HttpGet]
        public IActionResult GetRoleAccess(string roleCode)
        {
            var data = _db.Query(@"
                SELECT
                    m.moduleCode,
                    m.moduleName,
                    m.moduleType,
                    COALESCE(ra.accessLevel,'NO_ACCESS') AS accessLevel
                FROM s_module m
                LEFT JOIN s_roleaccess ra
                    ON ra.moduleCode = m.moduleCode
                   AND ra.roleCode = @roleCode
                WHERE m.isActive = 1
                ORDER BY
                    CASE m.moduleType
                        WHEN 'FEATURE' THEN 1
                        WHEN 'FILE' THEN 2
                        WHEN 'SETUP' THEN 3
                        WHEN 'TRANSACTION' THEN 4
                        WHEN 'REPORT' THEN 5
                        ELSE 6
                    END,
                    m.moduleName
            ", new { roleCode });

            return Json(data);
        }

        [HttpGet]
        public IActionResult GetRoleDataScope(string roleCode)
        {
            var sql = @"
                SELECT
                    roleCode,
                    scopeType,
                    allowedRanks,
                    allowedBranches,
                    allowedDepartments,
                    allowedPositions,
                    allowedEmploymentStatuses
                FROM s_role
                WHERE roleCode = @roleCode
                LIMIT 1";

            var scope = _db.QueryFirstOrDefault<dynamic>(sql, new { roleCode });

            if (scope == null)
            {
                return Json(new
                {
                    roleCode,
                    scopeType = "OWN_ONLY",
                    allowedRanks = "",
                    allowedBranches = "",
                    allowedDepartments = "",
                    allowedPositions = "",
                    allowedEmploymentStatuses = ""
                });
            }

            return Json(scope);
        }

        [HttpGet]
        public JsonResult GetRoleHiddenEmployees(string roleCode)
        {
            try
            {
                var sql = @"
                    SELECT hiddenEmployees
                    FROM s_role
                    WHERE roleCode = @roleCode
                    LIMIT 1";

                var result = _db.QueryFirstOrDefault<string>(sql, new { roleCode });

                return Json(new { success = true, hiddenEmployees = result ?? "" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetEmployeesForHiding(string roleCode, string search = "")
        {
            try
            {
                var sql = @"
                    SELECT
                        e.employeeNo as id,
                        CONCAT(e.lastName, ', ', e.firstName, ' ', IFNULL(e.middleName, '')) as text,
                        e.positionCode,
                        sp.positionName,
                        e.branchCode,
                        sb.branchName
                    FROM e_basicinfo e
                    LEFT JOIN s_position sp ON sp.positionCode = e.positionCode
                    LEFT JOIN s_branch sb ON sb.branchCode = e.branchCode
                    WHERE e.isActive = 1";

                if (!string.IsNullOrWhiteSpace(search))
                {
                    sql += @" AND (
                        e.employeeNo LIKE @search OR
                        e.firstName LIKE @search OR
                        e.lastName LIKE @search OR
                        e.middleName LIKE @search
                    )";
                }

                sql += " ORDER BY e.lastName, e.firstName LIMIT 100";

                var employees = _db.Query(sql, new { search = $"%{search}%" });

                return Json(new { results = employees });
            }
            catch (Exception ex)
            {
                return Json(new { results = new List<object>(), error = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult SaveRoleHiddenEmployees([FromBody] RoleHiddenEmployeesSaveModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.RoleCode))
                return Json(new { success = false, message = "Invalid data" });

            try
            {
                var hiddenEmployeesStr = model.HiddenEmployees != null && model.HiddenEmployees.Any()
                    ? string.Join(",", model.HiddenEmployees)
                    : null;

                _db.Execute(@"
                    UPDATE s_role
                    SET hiddenEmployees = @hiddenEmployees,
                        dtLastModified = NOW(),
                        lastModifiedByUser = @lastModifiedByUser
                    WHERE roleCode = @roleCode
                ", new
                {
                    roleCode = model.RoleCode,
                    hiddenEmployees = hiddenEmployeesStr,
                    lastModifiedByUser = EmployeeNo
                });

                _auditTrail.Log("s_role", 0, "UPDATED",
                    $"Updated hidden employees for role: {model.RoleCode}");

                return Json(new { success = true, message = "Hidden employees updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult SaveRoleDataScope([FromBody] RoleDataScopeSaveModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.RoleCode))
                return BadRequest(new { success = false, message = "Invalid data" });

            try
            {
                // OWN_AND_ASSIGNED does not use any of the filter columns —
                // visibility is fully derived at runtime from e_approver.
                // We still clear the filter columns to avoid confusion.
                var allowedRanks = model.AllowedRanks != null && model.AllowedRanks.Any()
                    ? string.Join(",", model.AllowedRanks) : null;

                var allowedBranches = model.AllowedBranches != null && model.AllowedBranches.Any()
                    ? string.Join(",", model.AllowedBranches) : null;

                var allowedDepartments = model.AllowedDepartments != null && model.AllowedDepartments.Any()
                    ? string.Join(",", model.AllowedDepartments) : null;

                var allowedPositions = model.AllowedPositions != null && model.AllowedPositions.Any()
                    ? string.Join(",", model.AllowedPositions) : null;

                var allowedEmploymentStatuses = model.AllowedEmploymentStatuses != null && model.AllowedEmploymentStatuses.Any()
                    ? string.Join(",", model.AllowedEmploymentStatuses) : null;

                _db.Execute(@"
                    UPDATE s_role
                    SET scopeType = @scopeType,
                        allowedRanks = @allowedRanks,
                        allowedBranches = @allowedBranches,
                        allowedDepartments = @allowedDepartments,
                        allowedPositions = @allowedPositions,
                        allowedEmploymentStatuses = @allowedEmploymentStatuses,
                        dtLastModified = NOW(),
                        lastModifiedByUser = @lastModifiedByUser
                    WHERE roleCode = @roleCode
                ", new
                {
                    roleCode = model.RoleCode,
                    scopeType = model.ScopeType,
                    allowedRanks,
                    allowedBranches,
                    allowedDepartments,
                    allowedPositions,
                    allowedEmploymentStatuses,
                    lastModifiedByUser = EmployeeNo
                });

                _auditTrail.Log("s_role", 0, "UPDATED",
                    $"Updated data scope for role: {model.RoleCode} - Scope: {model.ScopeType}");

                return Ok(new { success = true, message = "Data access configuration saved successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult SaveRoleAccess([FromBody] RoleAccessSaveModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.RoleCode))
                return BadRequest(new { success = false, message = "Invalid data" });

            if (_db.State != ConnectionState.Open)
                _db.Open();

            using var tran = _db.BeginTransaction();

            try
            {
                _db.Execute(
                    "DELETE FROM s_roleaccess WHERE roleCode = @roleCode",
                    new { roleCode = model.RoleCode },
                    transaction: tran
                );

                foreach (var item in model.Items.Where(x => x.AccessLevel != "NO_ACCESS"))
                {
                    _db.Execute(@"
                        INSERT INTO s_roleaccess (roleCode, moduleCode, accessLevel)
                        VALUES (@roleCode, @moduleCode, @accessLevel)
                    ", new
                    {
                        roleCode = model.RoleCode,
                        moduleCode = item.ModuleCode,
                        accessLevel = item.AccessLevel
                    }, transaction: tran);
                }

                tran.Commit();

                _auditTrail.Log("s_role", 0, "UPDATED",
                    $"Updated module access for role: {model.RoleCode}");

                return Ok(new { success = true, message = "Module access updated successfully" });
            }
            catch (Exception ex)
            {
                tran.Rollback();
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult InsertRole(string roleCode, string roleName)
        {
            if (string.IsNullOrEmpty(roleCode) || string.IsNullOrEmpty(roleName))
                return BadRequest(new { success = false, message = "Role Code and Role Name are required" });

            try
            {
                var exists = _db.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM s_role WHERE roleCode = @roleCode",
                    new { roleCode }
                ) > 0;

                if (exists)
                    return BadRequest(new { success = false, message = "Role code already exists" });

                _db.Execute(@"
                    INSERT INTO s_role (roleCode, roleName, scopeType, isActive, dtAdded, addedByUser)
                    VALUES (@roleCode, @roleName, 'OWN_ONLY', 1, NOW(), @addedByUser)
                ", new { roleCode, roleName, addedByUser = EmployeeNo });

                _auditTrail.Log("s_role", 0, "CREATED",
                    $"Added role: {roleCode} - {roleName}");

                return Ok(new { success = true, message = "Role created successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteRole(string roleCode)
        {
            if (string.IsNullOrEmpty(roleCode))
                return Json(new { success = false, message = "Invalid role code" });

            if (_db.State != ConnectionState.Open)
                _db.Open();

            using var transaction = _db.BeginTransaction();

            try
            {
                var userCount = await _db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM s_user WHERE roleCode = @roleCode AND isActive = 1",
                    new { roleCode }, transaction);

                if (userCount > 0)
                    return Json(new { success = false, message = $"Cannot delete role. {userCount} user(s) are currently assigned to this role." });

                await _db.ExecuteAsync("DELETE FROM s_roleaccess WHERE roleCode = @roleCode", new { roleCode }, transaction);
                await _db.ExecuteAsync("DELETE FROM s_role WHERE roleCode = @roleCode", new { roleCode }, transaction);

                transaction.Commit();

                _auditTrail.Log("s_role", 0, "DELETED", $"Role permanently deleted: {roleCode}");

                return Json(new { success = true, message = "Role deleted successfully" });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}