using Dapper;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{

    public class SetupControllerHelper : Controller
    {
        private readonly IDbConnection _db;

        public SetupControllerHelper(IDbConnection db)
        {
            _db = db;
        }

        /// Get all active employee ranks
        /// Used by: Role Data Scope configuration
        [HttpGet]
        public JsonResult GetEmployeeRank()
        {
            try
            {
                var ranks = _db.Query(@"
                    SELECT 
                        rankCode, 
                        rankName 
                    FROM s_rank 
                    WHERE isActive = 1 
                    ORDER BY rankName");

                return Json(ranks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeRank: {ex.Message}");
                return Json(new List<object>());
            }
        }

        /// Get all active branches
        /// Used by: Role Data Scope configuration, Employee filters
        [HttpGet]
        public JsonResult GetEmployeeBranch()
        {
            try
            {
                var branches = _db.Query(@"
                    SELECT 
                        branchCode, 
                        branchName 
                    FROM s_branch 
                    WHERE isActive = 1 
                    ORDER BY branchName");

                return Json(branches);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeBranch: {ex.Message}");
                return Json(new List<object>());
            }
        }

        /// Get all active departments
        /// Used by: Role Data Scope configuration, Employee filters
        [HttpGet]
        public JsonResult GetEmployeeDepartment()
        {
            try
            {
                var departments = _db.Query(@"
                    SELECT 
                        departmentCode, 
                        departmentName 
                    FROM s_department 
                    WHERE isActive = 1 
                    ORDER BY departmentName");

                return Json(departments);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeDepartment: {ex.Message}");
                return Json(new List<object>());
            }
        }

        /// Get all active positions
        /// Used by: Role Data Scope configuration, Employee filters
        [HttpGet]
        public JsonResult GetEmployeePosition()
        {
            try
            {
                var positions = _db.Query(@"
                    SELECT 
                        positionCode, 
                        positionName 
                    FROM s_position 
                    WHERE isActive = 1 
                    ORDER BY positionName");

                return Json(positions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeePosition: {ex.Message}");
                return Json(new List<object>());
            }
        }

        /// Get all active employment statuses
        /// Used by: Role Data Scope configuration, Employee filters
        [HttpGet]
        public JsonResult GetEmploymentStatuses()
        {
            try
            {
                var statuses = _db.Query(@"
                    SELECT 
                        employmentStatusCode, 
                        employmentStatusName 
                    FROM s_employmentstatus 
                    WHERE isActive = 1 
                    ORDER BY employmentStatusName");

                return Json(statuses);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmploymentStatuses: {ex.Message}");
                return Json(new List<object>());
            }
        }

        /// Get all active units (if you use this in your system)
        [HttpGet]
        public JsonResult GetEmployeeUnit()
        {
            try
            {
                var units = _db.Query(@"
                    SELECT 
                        unitCode, 
                        unitName 
                    FROM s_unit 
                    WHERE isActive = 1 
                    ORDER BY unitName");

                return Json(units);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeUnit: {ex.Message}");
                return Json(new List<object>());
            }
        }

        /// Get filtered data based on search term (for Select2 AJAX)
        /// This is an enhanced version that supports searching
        [HttpGet]
        public JsonResult GetEmployeeRankSearch(string search = "")
        {
            try
            {
                var query = @"
                    SELECT 
                        rankCode as id, 
                        rankName as text 
                    FROM s_rank 
                    WHERE isActive = 1";

                if (!string.IsNullOrWhiteSpace(search))
                {
                    query += " AND (rankCode LIKE @search OR rankName LIKE @search)";
                }

                query += " ORDER BY rankName LIMIT 50";

                var ranks = _db.Query(query, new { search = $"%{search}%" });

                return Json(new { results = ranks });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeRankSearch: {ex.Message}");
                return Json(new { results = new List<object>() });
            }
        }

        /// Get all modules for role access configuration
        [HttpGet]
        public JsonResult GetAllModules()
        {
            try
            {
                var modules = _db.Query(@"
                    SELECT 
                        moduleCode,
                        moduleName,
                        moduleType
                    FROM s_module 
                    WHERE isActive = 1 
                    ORDER BY 
                        CASE moduleType
                            WHEN 'FEATURE' THEN 1
                            WHEN 'FILE' THEN 2
                            WHEN 'SETUP' THEN 3
                            WHEN 'TRANSACTION' THEN 4
                            WHEN 'REPORT' THEN 5
                            ELSE 6
                        END,
                        moduleName");

                return Json(modules);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAllModules: {ex.Message}");
                return Json(new List<object>());
            }
        }

        /// Verify data scope configuration for a role
        /// Useful for debugging
        [HttpGet]
        public JsonResult VerifyRoleDataScope(string roleCode)
        {
            try
            {
                var roleInfo = _db.QueryFirstOrDefault<dynamic>(@"
                    SELECT 
                        r.roleCode,
                        r.roleName,
                        r.scopeType,
                        r.allowedRanks,
                        r.allowedBranches,
                        r.allowedDepartments,
                        r.allowedPositions,
                        r.allowedEmploymentStatuses,
                        COUNT(DISTINCT u.userCode) as userCount,
                        COUNT(DISTINCT ra.moduleCode) as moduleAccessCount
                    FROM s_role r
                    LEFT JOIN s_user u ON u.roleCode = r.roleCode AND u.isActive = 1
                    LEFT JOIN s_roleaccess ra ON ra.roleCode = r.roleCode
                    WHERE r.roleCode = @roleCode
                    GROUP BY r.roleCode
                ", new { roleCode });

                if (roleInfo == null)
                {
                    return Json(new { success = false, message = "Role not found" });
                }

                // Parse and expand the filter values
                var details = new
                {
                    roleCode = roleInfo.roleCode,
                    roleName = roleInfo.roleName,
                    scopeType = roleInfo.scopeType,
                    userCount = roleInfo.userCount,
                    moduleAccessCount = roleInfo.moduleAccessCount,
                    filters = new
                    {
                        ranks = ParseAndGetNames("s_rank", "rankCode", "rankName", roleInfo.allowedRanks),
                        branches = ParseAndGetNames("s_branch", "branchCode", "branchName", roleInfo.allowedBranches),
                        departments = ParseAndGetNames("s_department", "departmentCode", "departmentName", roleInfo.allowedDepartments),
                        positions = ParseAndGetNames("s_position", "positionCode", "positionName", roleInfo.allowedPositions),
                        employmentStatuses = ParseAndGetNames("s_employmentstatus", "employmentStatusCode", "employmentStatusName", roleInfo.allowedEmploymentStatuses)
                    }
                };

                return Json(new { success = true, data = details });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in VerifyRoleDataScope: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// Helper method to parse comma-separated codes and get their names
        private List<object> ParseAndGetNames(string tableName, string codeColumn, string nameColumn, string csvCodes)
        {
            if (string.IsNullOrWhiteSpace(csvCodes))
                return new List<object>();

            try
            {
                var codes = csvCodes.Split(',').Select(c => c.Trim()).ToList();
                var placeholders = string.Join(",", codes.Select((_, i) => $"@code{i}"));

                var parameters = new DynamicParameters();
                for (int i = 0; i < codes.Count; i++)
                {
                    parameters.Add($"@code{i}", codes[i]);
                }

                var query = $@"
                    SELECT 
                        {codeColumn} as code,
                        {nameColumn} as name
                    FROM {tableName}
                    WHERE {codeColumn} IN ({placeholders})
                    ORDER BY {nameColumn}";

                return _db.Query<object>(query, parameters).ToList();
            }
            catch
            {
                return new List<object>();
            }
        }

        /// Get role statistics
        /// Useful for admin dashboard
        [HttpGet]
        public JsonResult GetRoleStatistics()
        {
            try
            {
                var stats = _db.Query(@"
                    SELECT 
                        r.scopeType,
                        COUNT(DISTINCT r.roleCode) as roleCount,
                        COUNT(DISTINCT u.userCode) as userCount
                    FROM s_role r
                    LEFT JOIN s_user u ON u.roleCode = r.roleCode AND u.isActive = 1
                    WHERE r.isActive = 1
                    GROUP BY r.scopeType
                    ORDER BY roleCount DESC
                ");

                var total = _db.QueryFirst<dynamic>(@"
                    SELECT 
                        COUNT(DISTINCT roleCode) as totalRoles,
                        (SELECT COUNT(*) FROM s_user WHERE isActive = 1) as totalUsers
                    FROM s_role 
                    WHERE isActive = 1
                ");

                return Json(new
                {
                    success = true,
                    summary = total,
                    breakdown = stats
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetRoleStatistics: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}