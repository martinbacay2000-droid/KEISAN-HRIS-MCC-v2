using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SuserM")]
    public class UserListController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public UserListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/UserList.cshtml");
        }

        // ENHANCED DYNAMIC DATA SCOPE FILTER - WITH NULL SAFETY
        private void ApplyDataScopeFilter(StringBuilder query, DynamicParameters parameters)
        {
            var dataScope = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT 
                    scopeType, 
                    allowedRanks, 
                    allowedBranches, 
                    allowedDepartments,
                    allowedPositions,
                    allowedEmploymentStatuses
                FROM s_role
                WHERE roleCode = @roleCode AND isActive = 1
                LIMIT 1
            ", new { roleCode = RoleCode });

            var scopeType = dataScope?.scopeType ?? "OWN_ONLY";

            switch (scopeType)
            {
                case "OWN_ONLY":
                    query.Append(" AND u.userCode = @currentEmployeeNo");
                    parameters.Add("@currentEmployeeNo", EmployeeNo);
                    break;

                case "DEPARTMENT":
                    query.Append(@" AND u.userCode IN (
                        SELECT e.employeeNo 
                        FROM e_basicinfo e
                        WHERE e.departmentCode IN (
                            SELECT departmentCode FROM e_basicinfo WHERE employeeNo = @currentEmployeeNo
                        )
                    )");
                    parameters.Add("@currentEmployeeNo", EmployeeNo);
                    break;

                case "BRANCH":
                    string allowedBranchesStr = dataScope?.allowedBranches;
                    if (!string.IsNullOrWhiteSpace(allowedBranchesStr))
                    {
                        var branches = allowedBranchesStr.Split(',');
                        query.Append(@" AND u.userCode IN (
                            SELECT employeeNo FROM e_basicinfo WHERE branchCode IN @allowedBranches
                        )");
                        parameters.Add("@allowedBranches", branches);
                    }
                    break;

                case "RANK_FILTER":
                    string allowedRanksStr = dataScope?.allowedRanks;
                    if (!string.IsNullOrWhiteSpace(allowedRanksStr))
                    {
                        var ranks = allowedRanksStr.Split(',');
                        query.Append(@" AND u.userCode IN (
                            SELECT employeeNo FROM e_basicinfo WHERE rankCode IN @allowedRanks
                        )");
                        parameters.Add("@allowedRanks", ranks);
                    }
                    break;

                case "POSITION_FILTER":
                    string allowedPositionsStr = dataScope?.allowedPositions;
                    if (!string.IsNullOrWhiteSpace(allowedPositionsStr))
                    {
                        var positions = allowedPositionsStr.Split(',');
                        query.Append(@" AND u.userCode IN (
                            SELECT employeeNo FROM e_basicinfo WHERE positionCode IN @allowedPositions
                        )");
                        parameters.Add("@allowedPositions", positions);
                    }
                    break;

                case "EMPLOYMENT_STATUS":
                    string allowedEmploymentStatusesStr = dataScope?.allowedEmploymentStatuses;
                    if (!string.IsNullOrWhiteSpace(allowedEmploymentStatusesStr))
                    {
                        var statuses = allowedEmploymentStatusesStr.Split(',');
                        query.Append(@" AND u.userCode IN (
                            SELECT employeeNo FROM e_basicinfo WHERE employmentStatus IN @allowedEmploymentStatuses
                        )");
                        parameters.Add("@allowedEmploymentStatuses", statuses);
                    }
                    break;

                case "CUSTOM":
                    var customFilters = new List<string>();

                    string customRanksStr = dataScope?.allowedRanks;
                    if (!string.IsNullOrWhiteSpace(customRanksStr))
                    {
                        var ranks = customRanksStr.Split(',');
                        customFilters.Add("e.rankCode IN @allowedRanks");
                        parameters.Add("@allowedRanks", ranks);
                    }

                    string customBranchesStr = dataScope?.allowedBranches;
                    if (!string.IsNullOrWhiteSpace(customBranchesStr))
                    {
                        var branches = customBranchesStr.Split(',');
                        customFilters.Add("e.branchCode IN @allowedBranches");
                        parameters.Add("@allowedBranches", branches);
                    }

                    string customDepartmentsStr = dataScope?.allowedDepartments;
                    if (!string.IsNullOrWhiteSpace(customDepartmentsStr))
                    {
                        var departments = customDepartmentsStr.Split(',');
                        customFilters.Add("e.departmentCode IN @allowedDepartments");
                        parameters.Add("@allowedDepartments", departments);
                    }

                    string customPositionsStr = dataScope?.allowedPositions;
                    if (!string.IsNullOrWhiteSpace(customPositionsStr))
                    {
                        var positions = customPositionsStr.Split(',');
                        customFilters.Add("e.positionCode IN @allowedPositions");
                        parameters.Add("@allowedPositions", positions);
                    }

                    string customEmploymentStatusesStr = dataScope?.allowedEmploymentStatuses;
                    if (!string.IsNullOrWhiteSpace(customEmploymentStatusesStr))
                    {
                        var statuses = customEmploymentStatusesStr.Split(',');
                        customFilters.Add("e.employmentStatus IN @allowedEmploymentStatuses");
                        parameters.Add("@allowedEmploymentStatuses", statuses);
                    }

                    if (customFilters.Any())
                    {
                        query.Append(@" AND u.userCode IN (
                            SELECT employeeNo FROM e_basicinfo e WHERE " + string.Join(" OR ", customFilters) + ")");
                    }
                    break;

                case "ALL":
                    break;

                default:
                    query.Append(" AND u.userCode = @currentEmployeeNo");
                    parameters.Add("@currentEmployeeNo", EmployeeNo);
                    break;
            }
        }

        private void ApplyHiddenUsersFilter(StringBuilder query, DynamicParameters parameters)
        {
            var hiddenEmployees = _db.QueryFirstOrDefault<string>(@"
                SELECT hiddenEmployees 
                FROM s_role 
                WHERE roleCode = @roleCode AND isActive = 1
                LIMIT 1
            ", new { roleCode = RoleCode });

            if (!string.IsNullOrWhiteSpace(hiddenEmployees))
            {
                var hiddenList = hiddenEmployees.Split(',').Select(x => x.Trim()).ToArray();
                query.Append(" AND (u.userCode NOT IN @hiddenEmployees OR u.userCode = @currentUserCode)");
                parameters.Add("@hiddenEmployees", hiddenList);
                parameters.Add("@currentUserCode", EmployeeNo);
            }
        }

        [HttpGet]
        public JsonResult GetUserList()
        {
            try
            {
                var query = new StringBuilder(@"
                    SELECT 
                        u.id, 
                        u.userCode, 
                        u.lastName, 
                        u.firstName, 
                        u.middleName, 
                        u.roleCode,
                        e.employeeNo,
                        CONCAT(e.lastName, ', ', e.firstName) as employeeName
                    FROM s_user u
                    LEFT JOIN e_basicinfo e ON e.employeeNo = u.userCode
                    WHERE u.dtDeleted IS NULL");

                var parameters = new DynamicParameters();

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenUsersFilter(query, parameters);

                query.Append(" ORDER BY u.id DESC");

                var users = _db.Query<UserListModel>(query.ToString(), parameters).ToList();
                return Json(new { data = users });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetUserList: {ex.Message}");
                return Json(new { data = new List<object>(), error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedUserList()
        {
            try
            {
                var query = new StringBuilder(@"
                    SELECT 
                        u.id, 
                        u.userCode, 
                        u.lastName, 
                        u.firstName, 
                        u.middleName, 
                        u.roleCode
                    FROM s_user u
                    WHERE u.dtDeleted IS NOT NULL");

                var parameters = new DynamicParameters();

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenUsersFilter(query, parameters);

                query.Append(" ORDER BY u.id DESC");

                var users = _db.Query<UserListModel>(query.ToString(), parameters).ToList();
                return Json(new { data = users });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeletedUserList: {ex.Message}");
                return Json(new { data = new List<object>(), error = ex.Message });
            }
        }

        private bool CanViewUser(int userId)
        {
            var targetUserCode = _db.QueryFirstOrDefault<string>(
                "SELECT userCode FROM s_user WHERE id = @userId",
                new { userId });

            if (string.IsNullOrWhiteSpace(targetUserCode))
                return false;

            if (targetUserCode == EmployeeNo)
                return true;

            var hiddenEmployees = _db.QueryFirstOrDefault<string>(@"
                SELECT hiddenEmployees 
                FROM s_role 
                WHERE roleCode = @roleCode AND isActive = 1
                LIMIT 1
            ", new { roleCode = RoleCode });

            if (!string.IsNullOrWhiteSpace(hiddenEmployees))
            {
                var hiddenList = hiddenEmployees.Split(',').Select(x => x.Trim()).ToArray();
                if (hiddenList.Contains(targetUserCode))
                    return false;
            }

            var dataScope = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT 
                    scopeType, 
                    allowedRanks, 
                    allowedBranches, 
                    allowedDepartments,
                    allowedPositions,
                    allowedEmploymentStatuses
                FROM s_role
                WHERE roleCode = @roleCode AND isActive = 1
                LIMIT 1
            ", new { roleCode = RoleCode });

            var scopeType = dataScope?.scopeType ?? "OWN_ONLY";

            var targetEmployee = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT 
                    employeeNo,
                    branchCode,
                    departmentCode,
                    rankCode,
                    positionCode,
                    employmentStatus
                FROM e_basicinfo 
                WHERE employeeNo = @employeeNo
            ", new { employeeNo = targetUserCode });

            if (targetEmployee == null)
                return scopeType == "ALL";

            switch (scopeType)
            {
                case "OWN_ONLY":
                    return targetUserCode == EmployeeNo;

                case "DEPARTMENT":
                    var currentDept = _db.QueryFirstOrDefault<string>(
                        "SELECT departmentCode FROM e_basicinfo WHERE employeeNo = @employeeNo",
                        new { employeeNo = EmployeeNo }
                    );
                    string targetDept = targetEmployee.departmentCode;
                    return currentDept == targetDept;

                case "BRANCH":
                    string allowedBranchesStr = dataScope?.allowedBranches;
                    if (string.IsNullOrWhiteSpace(allowedBranchesStr)) return false;
                    var branches = allowedBranchesStr.Split(',');
                    string targetBranch = targetEmployee.branchCode;
                    return branches.Contains(targetBranch);

                case "RANK_FILTER":
                    string allowedRanksStr = dataScope?.allowedRanks;
                    if (string.IsNullOrWhiteSpace(allowedRanksStr)) return false;
                    var ranks = allowedRanksStr.Split(',');
                    string targetRank = targetEmployee.rankCode;
                    return ranks.Contains(targetRank);

                case "POSITION_FILTER":
                    string allowedPositionsStr = dataScope?.allowedPositions;
                    if (string.IsNullOrWhiteSpace(allowedPositionsStr)) return false;
                    var positions = allowedPositionsStr.Split(',');
                    string targetPosition = targetEmployee.positionCode;
                    return positions.Contains(targetPosition);

                case "EMPLOYMENT_STATUS":
                    string allowedEmploymentStatusesStr = dataScope?.allowedEmploymentStatuses;
                    if (string.IsNullOrWhiteSpace(allowedEmploymentStatusesStr)) return false;
                    var statuses = allowedEmploymentStatusesStr.Split(',');
                    string targetStatus = targetEmployee.employmentStatus;
                    return statuses.Contains(targetStatus);

                case "CUSTOM":
                    bool matchesAnyFilter = false;

                    string customRanksStr = dataScope?.allowedRanks;
                    if (!matchesAnyFilter && !string.IsNullOrWhiteSpace(customRanksStr))
                    {
                        var ranksCustom = customRanksStr.Split(',');
                        string targetRankCustom = targetEmployee.rankCode;
                        if (ranksCustom.Contains(targetRankCustom)) matchesAnyFilter = true;
                    }

                    string customBranchesStr = dataScope?.allowedBranches;
                    if (!matchesAnyFilter && !string.IsNullOrWhiteSpace(customBranchesStr))
                    {
                        var branchesCustom = customBranchesStr.Split(',');
                        string targetBranchCustom = targetEmployee.branchCode;
                        if (branchesCustom.Contains(targetBranchCustom)) matchesAnyFilter = true;
                    }

                    string customDepartmentsStr = dataScope?.allowedDepartments;
                    if (!matchesAnyFilter && !string.IsNullOrWhiteSpace(customDepartmentsStr))
                    {
                        var deptsCustom = customDepartmentsStr.Split(',');
                        string targetDeptCustom = targetEmployee.departmentCode;
                        if (deptsCustom.Contains(targetDeptCustom)) matchesAnyFilter = true;
                    }

                    string customPositionsStr = dataScope?.allowedPositions;
                    if (!matchesAnyFilter && !string.IsNullOrWhiteSpace(customPositionsStr))
                    {
                        var positionsCustom = customPositionsStr.Split(',');
                        string targetPositionCustom = targetEmployee.positionCode;
                        if (positionsCustom.Contains(targetPositionCustom)) matchesAnyFilter = true;
                    }

                    string customEmploymentStatusesStr = dataScope?.allowedEmploymentStatuses;
                    if (!matchesAnyFilter && !string.IsNullOrWhiteSpace(customEmploymentStatusesStr))
                    {
                        var statusesCustom = customEmploymentStatusesStr.Split(',');
                        string targetStatusCustom = targetEmployee.employmentStatus;
                        if (statusesCustom.Contains(targetStatusCustom)) matchesAnyFilter = true;
                    }

                    return matchesAnyFilter;

                case "ALL":
                    return true;

                default:
                    return targetUserCode == EmployeeNo;
            }
        }

        private bool CanEditRole()
        {
            return GetModuleAccessLevel("SuserM") == "FULL";
        }

        [HttpGet]
        public JsonResult GetUser(int id)
        {
            try
            {
                if (!CanViewUser(id))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to view this user." });
                }

                string sql = @"SELECT 
                          u.id, 
                          u.userCode, 
                          u.lastName, 
                          u.firstName, 
                          u.middleName, 
                          u.roleCode
                      FROM s_user u
                      WHERE u.id = @Id";

                var user = _db.QueryFirstOrDefault<UserListModel>(sql, new { Id = id });

                if (user != null)
                {
                    user.password = string.Empty;
                }

                return Json(new
                {
                    success = true,
                    data = user,
                    canEditRole = CanEditRole()
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading user: {ex.Message}" });
            }
        }

        [HttpGet]
        public JsonResult SearchEmployee(string employeeNo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(employeeNo))
                {
                    return Json(new { success = false, message = "Employee number is required" });
                }

                string checkUserSql = @"SELECT COUNT(*) 
                                       FROM s_user 
                                       WHERE userCode = @employeeNo 
                                       AND dtDeleted IS NULL";
                int userExists = _db.QuerySingle<int>(checkUserSql, new { employeeNo });

                if (userExists > 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "User account already exists for this employee number"
                    });
                }

                string sql = @"SELECT 
                                  e.employeeNo,
                                  e.lastName,
                                  e.firstName,
                                  e.middleName,
                                  e.rankCode,
                                  r.rankName,
                                  e.branchCode,
                                  b.branchName,
                                  e.departmentCode,
                                  d.departmentName,
                                  e.employmentStatus,
                                  es.employmentStatusName
                              FROM e_basicinfo e
                              LEFT JOIN s_rank r ON r.rankCode = e.rankCode
                              LEFT JOIN s_branch b ON b.branchCode = e.branchCode
                              LEFT JOIN s_department d ON d.departmentCode = e.departmentCode
                              LEFT JOIN s_employmentstatus es ON es.employmentStatusCode = e.employmentStatus
                              WHERE e.employeeNo = @employeeNo 
                              AND e.isActive = 1";

                var employee = _db.QueryFirstOrDefault<UserListModel>(sql, new { employeeNo });

                if (employee == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Employee not found or inactive"
                    });
                }

                return Json(new
                {
                    success = true,
                    data = employee
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error searching employee: {ex.Message}" });
            }
        }

        [HttpGet]
        public JsonResult GetActiveRoles()
        {
            string sql = @"SELECT 
                              id, 
                              roleCode, 
                              roleName 
                          FROM s_role 
                          WHERE isActive = 1 
                          AND dtDeleted IS NULL 
                          ORDER BY roleName";
            var roles = _db.Query<UserRoleListModel>(sql).ToList();
            return Json(new { data = roles });
        }

        [HttpPost]
        public JsonResult BulkCreateUserAccounts(string defaultPassword = "DefaultPass123!")
        {
            try
            {
                var accessLevel = GetModuleAccessLevel("SuserM");
                if (accessLevel != "READWRITE" && accessLevel != "FULL")
                {
                    return Json(new { success = false, message = "You don't have permission to create users." });
                }

                string sql = @"SELECT 
                                  e.employeeNo,
                                  e.lastName,
                                  e.firstName,
                                  e.middleName,
                                  e.rankCode
                              FROM e_basicinfo e
                              LEFT JOIN s_user u ON e.employeeNo = u.userCode AND u.dtDeleted IS NULL
                              WHERE e.isActive = 1 
                              AND u.id IS NULL";

                var employeesWithoutAccounts = _db.Query<dynamic>(sql).ToList();

                if (!employeesWithoutAccounts.Any())
                {
                    return Json(new { success = true, message = "All employees already have user accounts", count = 0 });
                }

                int successCount = 0;
                var errors = new List<string>();

                foreach (var employee in employeesWithoutAccounts)
                {
                    try
                    {
                        string defaultRoleCode = employee.rankCode == "RANK & FILE" ? "RL-000002" : "RL-000001";

                        string insertUserSql = @"INSERT INTO s_user 
                                                (userCode, username, password, lastName, firstName, 
                                                 middleName, positionName, roleCode, isActive, islock, 
                                                 attempt, isScheduleUploader, dtAdded, addedByUser) 
                                                VALUES 
                                                (@userCode, NULL, CAST(AES_ENCRYPT(@password, 'portal123') AS CHAR), 
                                                 @lastName, @firstName, @middleName, NULL, @roleCode, 
                                                 1, 0, 0, 0, NOW(), @addedByUser)";

                        _db.Execute(insertUserSql, new
                        {
                            userCode = employee.employeeNo,
                            password = defaultPassword,
                            lastName = employee.lastName,
                            firstName = employee.firstName,
                            middleName = employee.middleName,
                            roleCode = defaultRoleCode,
                            addedByUser = EmployeeNo
                        });

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to create account for {employee.employeeNo}: {ex.Message}");
                    }
                }

                _auditTrail.Log("s_user", 0, "BULK_CREATED",
                    $"Bulk created {successCount} user accounts out of {employeesWithoutAccounts.Count} employees");

                return Json(new
                {
                    success = true,
                    message = $"Created {successCount} user accounts successfully",
                    count = successCount,
                    errors = errors
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error in bulk creation: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult AddUser(UserListModel model)
        {
            try
            {
                var accessLevel = GetModuleAccessLevel("SuserM");
                if (accessLevel != "READWRITE" && accessLevel != "FULL")
                {
                    return Json(new { success = false, message = "You don't have permission to create users." });
                }

                string checkUserCodeSql = @"SELECT COUNT(*) 
                                           FROM s_user 
                                           WHERE userCode = @userCode 
                                           AND dtDeleted IS NULL";
                int userCodeExists = _db.QuerySingle<int>(checkUserCodeSql, new { userCode = model.userCode });

                if (userCodeExists > 0)
                {
                    return Json(new { success = false, message = "User code already exists!" });
                }

                string sql = @"INSERT INTO s_user 
                              (userCode, username, password, lastName, firstName, 
                               middleName, positionName, roleCode, isActive, islock, 
                               attempt, isScheduleUploader, dtAdded, addedByUser) 
                              VALUES 
                              (@userCode, NULL, CAST(AES_ENCRYPT(@password, 'portal123') AS CHAR), 
                               @lastName, @firstName, @middleName, NULL, @roleCode, 
                               1, 0, 0, 0, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    userCode = model.userCode,
                    password = model.password,
                    lastName = model.lastName,
                    firstName = model.firstName,
                    middleName = string.IsNullOrWhiteSpace(model.middleName) ? null : model.middleName,
                    roleCode = model.roleCode,
                    addedByUser = EmployeeNo
                });

                _auditTrail.Log("s_user", newId, "CREATED",
                    $"Added user: {model.userCode} - {model.firstName} {model.lastName}");

                return Json(new { success = true, message = "User added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding user: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateUser(UserListModel model)
        {
            try
            {
                var accessLevel = GetModuleAccessLevel("SuserM");
                if (accessLevel != "EDIT" && accessLevel != "READWRITE" && accessLevel != "FULL")
                {
                    return Json(new { success = false, message = "You don't have permission to edit users." });
                }

                if (!CanViewUser(model.id))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to edit this user." });
                }

                // Check if trying to change role without FULL access
                if (accessLevel != "FULL")
                {
                    var currentUser = _db.QueryFirstOrDefault<dynamic>(
                        "SELECT roleCode FROM s_user WHERE id = @id",
                        new { id = model.id }
                    );

                    if (currentUser != null && currentUser.roleCode != model.roleCode)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "You don't have permission to change the Role. Only administrators can modify this field."
                        });
                    }
                }

                string checkSql = @"SELECT COUNT(*) 
                                   FROM s_user 
                                   WHERE id = @id 
                                   AND dtDeleted IS NULL";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                {
                    return Json(new { success = false, message = "User record not found or has been deleted!" });
                }

                string duplicateUserCodeSql = @"SELECT COUNT(*) 
                                               FROM s_user 
                                               WHERE userCode = @userCode 
                                               AND id != @id 
                                               AND dtDeleted IS NULL";
                int duplicateUserCode = _db.QuerySingle<int>(duplicateUserCodeSql, new
                {
                    userCode = model.userCode,
                    id = model.id
                });

                if (duplicateUserCode > 0)
                {
                    return Json(new { success = false, message = "User code already exists!" });
                }

                string sql;
                object parameters;

                if (!string.IsNullOrWhiteSpace(model.password))
                {
                    sql = @"UPDATE s_user 
                           SET userCode = @userCode, 
                               username = NULL,
                               password = CAST(AES_ENCRYPT(@password, 'portal123') AS CHAR),
                               lastName = @lastName,
                               firstName = @firstName,
                               middleName = @middleName,
                               positionName = NULL,
                               roleCode = @roleCode,
                               dtLastModified = NOW(),
                               lastModifiedByUser = @lastModifiedByUser
                           WHERE id = @id";

                    parameters = new
                    {
                        id = model.id,
                        userCode = model.userCode,
                        password = model.password,
                        lastName = model.lastName,
                        firstName = model.firstName,
                        middleName = string.IsNullOrWhiteSpace(model.middleName) ? null : model.middleName,
                        roleCode = model.roleCode,
                        lastModifiedByUser = EmployeeNo
                    };
                }
                else
                {
                    sql = @"UPDATE s_user 
                           SET userCode = @userCode, 
                               username = NULL,
                               lastName = @lastName,
                               firstName = @firstName,
                               middleName = @middleName,
                               positionName = NULL,
                               roleCode = @roleCode,
                               dtLastModified = NOW(),
                               lastModifiedByUser = @lastModifiedByUser
                           WHERE id = @id";

                    parameters = new
                    {
                        id = model.id,
                        userCode = model.userCode,
                        lastName = model.lastName,
                        firstName = model.firstName,
                        middleName = string.IsNullOrWhiteSpace(model.middleName) ? null : model.middleName,
                        roleCode = model.roleCode,
                        lastModifiedByUser = EmployeeNo
                    };
                }

                _db.Execute(sql, parameters);

                _auditTrail.Log("s_user", model.id, "UPDATED",
                    $"Updated user: {model.userCode} - {model.firstName} {model.lastName}");

                return Json(new { success = true, message = "User updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating user: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteUser(int id, string reason = "")
        {
            try
            {
                var accessLevel = GetModuleAccessLevel("SuserM");
                if (accessLevel != "FULL")
                {
                    return Json(new { success = false, message = "You don't have permission to delete users." });
                }

                if (!CanViewUser(id))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to delete this user." });
                }

                var user = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT CONCAT(firstName, ' ', lastName) as userName FROM s_user WHERE id = @id",
                    new { id });

                if (user == null)
                    return Json(new { success = false, message = "User not found!" });

                string sql = @"UPDATE s_user 
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";
                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo
                });

                _auditTrail.Log("s_user", id, "DELETED",
                    $"User soft deleted: {user.userName}{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "User deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreUser(int id)
        {
            try
            {
                var accessLevel = GetModuleAccessLevel("SuserM");
                if (accessLevel != "FULL")
                {
                    return Json(new { success = false, message = "You don't have permission to restore users." });
                }

                var user = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT CONCAT(firstName, ' ', lastName) as userName FROM s_user WHERE id = @id",
                    new { id });

                if (user == null)
                    return Json(new { success = false, message = "User not found!" });

                string sql = @"UPDATE s_user 
                              SET dtDeleted = NULL, 
                                  isActive = 1,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @Id";
                _db.Execute(sql, new
                {
                    Id = id,
                    lastModifiedByUser = EmployeeNo
                });

                _auditTrail.Log("s_user", id, "RESTORED", $"User restored: {user.userName}");

                return Json(new { success = true, message = "User restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string GetModuleAccessLevel(string moduleCode)
        {
            if (RoleCode == "RL-000000")
                return "FULL";

            var json = HttpContext.Session.GetString("ROLE_ACCESS");
            if (string.IsNullOrEmpty(json))
                return "NO_ACCESS";

            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict != null && dict.ContainsKey(moduleCode) ? dict[moduleCode] : "NO_ACCESS";
        }
    }
}