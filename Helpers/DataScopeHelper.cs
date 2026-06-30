using Dapper;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Helpers
{
    /// <summary>
    /// Centralized helper for all data scope filtering logic.
    /// Handles OWN_ONLY, OWN_AND_ASSIGNED, DEPARTMENT, BRANCH, RANK_FILTER,
    /// POSITION_FILTER, EMPLOYMENT_STATUS, CUSTOM, and ALL scope types.
    /// 
    /// Usage: inject or call statically from any controller that needs row-level security.
    /// </summary>
    public static class DataScopeHelper
    {
        // ─────────────────────────────────────────────────────────────────────
        // CORE: Resolve all employee numbers this approver can see via e_approver
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all employeeNo values that the given approver (by their employeeNo)
        /// is configured to approve/see, based on e_approver typeList logic:
        ///   1 = employmentStatus match
        ///   2 = branchCode match
        ///   3 = departmentCode match
        ///   4 = positionCode match
        ///   5 = ALL employees
        ///   6 = direct employee assignment
        /// </summary>
        public static List<string> GetAssignedEmployeeNos(IDbConnection db, string approverEmployeeNo)
        {
            // Get all approver assignments for this person
            var assignments = db.Query<dynamic>(@"
                SELECT employeeNo, typeList
                FROM e_approver
                WHERE approverNo = @approverNo
                  AND isActive = 1
                  AND dtDeleted IS NULL
            ", new { approverNo = approverEmployeeNo }).ToList();

            if (!assignments.Any())
                return new List<string>();

            var result = new HashSet<string>();

            foreach (var assignment in assignments)
            {
                int typeList = (int)(assignment.typeList ?? 0);
                string val = assignment.employeeNo ?? "";

                switch (typeList)
                {
                    case 6:
                        // Direct employee-to-employee assignment
                        if (!string.IsNullOrWhiteSpace(val))
                            result.Add(val);
                        break;

                    case 1:
                        // val is an employmentStatus code
                        var byStatus = db.Query<string>(@"
                            SELECT employeeNo FROM e_basicinfo
                            WHERE employmentStatus = @val AND isActive = 1",
                            new { val }).ToList();
                        foreach (var e in byStatus) result.Add(e);
                        break;

                    case 2:
                        // val is a branchCode
                        var byBranch = db.Query<string>(@"
                            SELECT employeeNo FROM e_basicinfo
                            WHERE branchCode = @val AND isActive = 1",
                            new { val }).ToList();
                        foreach (var e in byBranch) result.Add(e);
                        break;

                    case 3:
                        // val is a departmentCode
                        var byDept = db.Query<string>(@"
                            SELECT employeeNo FROM e_basicinfo
                            WHERE departmentCode = @val AND isActive = 1",
                            new { val }).ToList();
                        foreach (var e in byDept) result.Add(e);
                        break;

                    case 4:
                        // val is a positionCode
                        var byPos = db.Query<string>(@"
                            SELECT employeeNo FROM e_basicinfo
                            WHERE positionCode = @val AND isActive = 1",
                            new { val }).ToList();
                        foreach (var e in byPos) result.Add(e);
                        break;

                    case 5:
                        // ALL employees — return immediately, no need to filter
                        var all = db.Query<string>(@"
                            SELECT employeeNo FROM e_basicinfo WHERE isActive = 1").ToList();
                        foreach (var e in all) result.Add(e);
                        return result.ToList(); // short-circuit
                }
            }

            return result.ToList();
        }

        // ─────────────────────────────────────────────────────────────────────
        // CanViewEmployee — used by single-record security checks
        // tableAlias is not needed here, this is record-level
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether currentEmployeeNo (with their roleCode) can view targetEmployeeNo.
        /// Handles all scopeTypes including OWN_AND_ASSIGNED.
        /// </summary>
        public static bool CanViewEmployee(
            IDbConnection db,
            string currentEmployeeNo,
            string roleCode,
            string targetEmployeeNo)
        {
            // Always allow own record
            if (targetEmployeeNo == currentEmployeeNo)
                return true;

            // Check hidden employees first
            var hiddenEmployees = db.QueryFirstOrDefault<string>(@"
                SELECT hiddenEmployees
                FROM s_role
                WHERE roleCode = @roleCode AND isActive = 1
                LIMIT 1", new { roleCode });

            if (!string.IsNullOrWhiteSpace(hiddenEmployees))
            {
                var hiddenList = hiddenEmployees.Split(',').Select(x => x.Trim()).ToArray();
                if (hiddenList.Contains(targetEmployeeNo))
                    return false;
            }

            // Load scope config
            var dataScope = db.QueryFirstOrDefault<dynamic>(@"
                SELECT scopeType, allowedRanks, allowedBranches, allowedDepartments,
                       allowedPositions, allowedEmploymentStatuses
                FROM s_role
                WHERE roleCode = @roleCode AND isActive = 1
                LIMIT 1", new { roleCode });

            var scopeType = (string)(dataScope?.scopeType) ?? "OWN_ONLY";

            // Load target employee details for filter comparisons
            var target = db.QueryFirstOrDefault<dynamic>(@"
                SELECT employeeNo, branchCode, departmentCode, rankCode,
                       positionCode, employmentStatus
                FROM e_basicinfo
                WHERE employeeNo = @employeeNo", new { employeeNo = targetEmployeeNo });

            if (target == null) return false;

            switch (scopeType)
            {
                case "OWN_ONLY":
                    return false; // already handled own-record above

                case "OWN_AND_ASSIGNED":
                    // Check if targetEmployeeNo is in the approver's assigned list
                    var assigned = GetAssignedEmployeeNos(db, currentEmployeeNo);
                    return assigned.Contains(targetEmployeeNo);

                case "DEPARTMENT":
                    var currentDept = db.QueryFirstOrDefault<string>(
                        "SELECT departmentCode FROM e_basicinfo WHERE employeeNo = @emp",
                        new { emp = currentEmployeeNo });
                    return currentDept == (string)target.departmentCode;

                case "BRANCH":
                    if (string.IsNullOrWhiteSpace((string)dataScope?.allowedBranches)) return false;
                    return dataScope.allowedBranches.Split(',').Contains((string)target.branchCode);

                case "RANK_FILTER":
                    if (string.IsNullOrWhiteSpace((string)dataScope?.allowedRanks)) return false;
                    return dataScope.allowedRanks.Split(',').Contains((string)target.rankCode);

                case "POSITION_FILTER":
                    if (string.IsNullOrWhiteSpace((string)dataScope?.allowedPositions)) return false;
                    return dataScope.allowedPositions.Split(',').Contains((string)target.positionCode);

                case "EMPLOYMENT_STATUS":
                    if (string.IsNullOrWhiteSpace((string)dataScope?.allowedEmploymentStatuses)) return false;
                    return dataScope.allowedEmploymentStatuses.Split(',').Contains((string)target.employmentStatus);

                case "CUSTOM":
                    return CheckCustomScope(dataScope, target);

                case "ALL":
                    return true;

                default:
                    return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ApplyDataScopeFilter — appends WHERE clause fragment to a StringBuilder
        // tableAlias: "e", "b", "a" depending on which controller calls it
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Appends a data scope WHERE clause fragment.
        /// tableAlias is the SQL alias for e_basicinfo in the query (e.g. "e", "b", "a").
        /// currentEmployeeNo is from the session.
        /// roleCode is from the session.
        /// </summary>
        public static void ApplyDataScopeFilter(
            IDbConnection db,
            StringBuilder query,
            DynamicParameters parameters,
            string currentEmployeeNo,
            string roleCode,
            string tableAlias = "e")
        {
            var dataScope = db.QueryFirstOrDefault<dynamic>(@"
                SELECT scopeType, allowedRanks, allowedBranches, allowedDepartments,
                       allowedPositions, allowedEmploymentStatuses
                FROM s_role
                WHERE roleCode = @roleCode AND isActive = 1
                LIMIT 1", new { roleCode });

            var scopeType = (string)(dataScope?.scopeType) ?? "OWN_ONLY";
            string a = tableAlias;

            switch (scopeType)
            {
                case "OWN_ONLY":
                    query.Append($" AND {a}.employeeNo = @currentEmployeeNo");
                    parameters.Add("@currentEmployeeNo", currentEmployeeNo);
                    break;

                case "OWN_AND_ASSIGNED":
                    // Own record + all employees derived from e_approver
                    var assignedNos = GetAssignedEmployeeNos(db, currentEmployeeNo);
                    if (assignedNos.Any())
                    {
                        // Include self + all assigned employees
                        var allVisible = new HashSet<string>(assignedNos) { currentEmployeeNo };
                        query.Append($" AND {a}.employeeNo IN @ownAndAssignedList");
                        parameters.Add("@ownAndAssignedList", allVisible.ToArray());
                    }
                    else
                    {
                        // No assignments, fall back to own only
                        query.Append($" AND {a}.employeeNo = @currentEmployeeNo");
                        parameters.Add("@currentEmployeeNo", currentEmployeeNo);
                    }
                    break;

                case "DEPARTMENT":
                    query.Append($@" AND {a}.departmentCode IN (
                        SELECT departmentCode FROM e_basicinfo WHERE employeeNo = @currentEmployeeNo
                    )");
                    parameters.Add("@currentEmployeeNo", currentEmployeeNo);
                    break;

                case "BRANCH":
                    string allowedBranchesStr = dataScope?.allowedBranches;
                    if (!string.IsNullOrWhiteSpace(allowedBranchesStr))
                    {
                        query.Append($" AND {a}.branchCode IN @allowedBranches");
                        parameters.Add("@allowedBranches", allowedBranchesStr.Split(','));
                    }
                    break;

                case "RANK_FILTER":
                    string allowedRanksStr = dataScope?.allowedRanks;
                    if (!string.IsNullOrWhiteSpace(allowedRanksStr))
                    {
                        query.Append($" AND {a}.rankCode IN @allowedRanks");
                        parameters.Add("@allowedRanks", allowedRanksStr.Split(','));
                    }
                    break;

                case "POSITION_FILTER":
                    string allowedPositionsStr = dataScope?.allowedPositions;
                    if (!string.IsNullOrWhiteSpace(allowedPositionsStr))
                    {
                        query.Append($" AND {a}.positionCode IN @allowedPositions");
                        parameters.Add("@allowedPositions", allowedPositionsStr.Split(','));
                    }
                    break;

                case "EMPLOYMENT_STATUS":
                    string allowedStatusesStr = dataScope?.allowedEmploymentStatuses;
                    if (!string.IsNullOrWhiteSpace(allowedStatusesStr))
                    {
                        query.Append($" AND {a}.employmentStatus IN @allowedEmploymentStatuses");
                        parameters.Add("@allowedEmploymentStatuses", allowedStatusesStr.Split(','));
                    }
                    break;

                case "CUSTOM":
                    AppendCustomFilters(query, parameters, dataScope, a);
                    break;

                case "ALL":
                    // No filter needed
                    break;

                default:
                    // Unknown scope — safe fallback to OWN_ONLY
                    query.Append($" AND {a}.employeeNo = @currentEmployeeNo");
                    parameters.Add("@currentEmployeeNo", currentEmployeeNo);
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ApplyHiddenEmployeesFilter — appends NOT IN clause for hidden employees
        // The OWN_AND_ASSIGNED scope still respects hiddenEmployees:
        //   hidden employees are excluded EXCEPT the current user's own record.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Appends a hidden employees exclusion filter.
        /// Always allows the current user to see their own record even if somehow hidden.
        /// </summary>
        public static void ApplyHiddenEmployeesFilter(
            IDbConnection db,
            StringBuilder query,
            DynamicParameters parameters,
            string currentEmployeeNo,
            string roleCode,
            string tableAlias = "e")
        {
            var hiddenEmployees = db.QueryFirstOrDefault<string>(@"
                SELECT hiddenEmployees
                FROM s_role
                WHERE roleCode = @roleCode AND isActive = 1
                LIMIT 1", new { roleCode });

            if (!string.IsNullOrWhiteSpace(hiddenEmployees))
            {
                var hiddenList = hiddenEmployees.Split(',').Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x)).ToArray();

                if (hiddenList.Any())
                {
                    string a = tableAlias;
                    // Always show current employee even if they're in hidden list
                    query.Append($" AND ({a}.employeeNo NOT IN @hiddenEmployees OR {a}.employeeNo = @selfEmployeeNo)");
                    parameters.Add("@hiddenEmployees", hiddenList);
                    parameters.Add("@selfEmployeeNo", currentEmployeeNo);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────

        private static bool CheckCustomScope(dynamic dataScope, dynamic target)
        {
            string customRanks = dataScope?.allowedRanks;
            if (!string.IsNullOrWhiteSpace(customRanks) &&
                customRanks.Split(',').Contains((string)target.rankCode))
                return true;

            string customBranches = dataScope?.allowedBranches;
            if (!string.IsNullOrWhiteSpace(customBranches) &&
                customBranches.Split(',').Contains((string)target.branchCode))
                return true;

            string customDepts = dataScope?.allowedDepartments;
            if (!string.IsNullOrWhiteSpace(customDepts) &&
                customDepts.Split(',').Contains((string)target.departmentCode))
                return true;

            string customPositions = dataScope?.allowedPositions;
            if (!string.IsNullOrWhiteSpace(customPositions) &&
                customPositions.Split(',').Contains((string)target.positionCode))
                return true;

            string customStatuses = dataScope?.allowedEmploymentStatuses;
            if (!string.IsNullOrWhiteSpace(customStatuses) &&
                customStatuses.Split(',').Contains((string)target.employmentStatus))
                return true;

            return false;
        }

        private static void AppendCustomFilters(
            StringBuilder query,
            DynamicParameters parameters,
            dynamic dataScope,
            string a)
        {
            var customFilters = new List<string>();

            string ranks = dataScope?.allowedRanks;
            if (!string.IsNullOrWhiteSpace(ranks))
            {
                customFilters.Add($"{a}.rankCode IN @allowedRanks");
                parameters.Add("@allowedRanks", ranks.Split(','));
            }

            string branches = dataScope?.allowedBranches;
            if (!string.IsNullOrWhiteSpace(branches))
            {
                customFilters.Add($"{a}.branchCode IN @allowedBranches");
                parameters.Add("@allowedBranches", branches.Split(','));
            }

            string depts = dataScope?.allowedDepartments;
            if (!string.IsNullOrWhiteSpace(depts))
            {
                customFilters.Add($"{a}.departmentCode IN @allowedDepartments");
                parameters.Add("@allowedDepartments", depts.Split(','));
            }

            string positions = dataScope?.allowedPositions;
            if (!string.IsNullOrWhiteSpace(positions))
            {
                customFilters.Add($"{a}.positionCode IN @allowedPositions");
                parameters.Add("@allowedPositions", positions.Split(','));
            }

            string statuses = dataScope?.allowedEmploymentStatuses;
            if (!string.IsNullOrWhiteSpace(statuses))
            {
                customFilters.Add($"{a}.employmentStatus IN @allowedEmploymentStatuses");
                parameters.Add("@allowedEmploymentStatuses", statuses.Split(','));
            }

            if (customFilters.Any())
                query.Append(" AND (" + string.Join(" OR ", customFilters) + ")");
        }
    }
}