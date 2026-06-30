using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping
{
    /// <summary>
    /// Base controller for all timekeeping request controllers.
    /// Provides centralized security checks, data scope filtering, and notification management.
    /// Uses DataScopeHelper for all row-level security — supports OWN_AND_ASSIGNED scope.
    /// </summary>
    public abstract class TimekeepingRequestBaseController : BaseController
    {
        protected readonly IDbConnection _db;
        protected readonly IAuditTrailService _auditTrail;
        protected readonly string _moduleCode;
        protected IApproverService _approverService;
        protected IEmailService _emailService;

        protected TimekeepingRequestBaseController(
            IDbConnection db,
            IAuditTrailService auditTrail,
            string moduleCode)
        {
            _db = db;
            _auditTrail = auditTrail;
            _moduleCode = moduleCode;
        }

        #region Data Scope Filtering

        /// <summary>
        /// Applies dynamic data scope filter using the centralized DataScopeHelper.
        /// Table alias "b" is used for e_basicinfo in timekeeping queries.
        /// </summary>
        protected void ApplyDataScopeFilter(StringBuilder query, DynamicParameters parameters)
        {
            DataScopeHelper.ApplyDataScopeFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "b");
        }

        /// <summary>
        /// Applies hidden employees filter using the centralized DataScopeHelper.
        /// Always allows the current user to see their own record.
        /// </summary>
        protected void ApplyHiddenEmployeesFilter(StringBuilder query, DynamicParameters parameters)
        {
            DataScopeHelper.ApplyHiddenEmployeesFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "b");
        }

        #endregion

        #region Security Checks

        /// <summary>
        /// Checks if the current user can view/edit a specific employee based on data scope
        /// and hidden employees configuration. Supports all scope types including OWN_AND_ASSIGNED.
        /// </summary>
        protected bool CanViewEmployee(string employeeNo)
        {
            return DataScopeHelper.CanViewEmployee(_db, EmployeeNo, RoleCode, employeeNo);
        }

        /// <summary>
        /// Checks if the current user has FULL access level to the module.
        /// </summary>
        protected bool HasFullAccess()
        {
            var roleAccess = _db.QueryFirstOrDefault<string>(@"
                SELECT accessLevel
                FROM s_roleaccess
                WHERE roleCode = @roleCode AND moduleCode = @moduleCode
                LIMIT 1", new { roleCode = RoleCode, moduleCode = _moduleCode });

            return roleAccess == "FULL" || RoleCode == "RL-000000";
        }

        /// <summary>
        /// Gets current employee information including access permissions.
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> GetCurrentEmployee()
        {
            try
            {
                var employee = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT
                        employeeNo,
                        CONCAT(IFNULL(firstName, ''), ' ', IFNULL(CONCAT(middleName, ' '), ''), IFNULL(lastName, '')) as employeeName
                    FROM e_basicinfo
                    WHERE employeeNo = @employeeNo AND isActive = 1",
                    new { employeeNo = EmployeeNo });

                var approverInfo = await GetApproverInfoCachedAsync();

                return Json(new
                {
                    canEditEmployee = HasFullAccess(),
                    currentEmployee = employee,
                    isApprover = approverInfo.IsApprover
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCurrentEmployee: {ex.Message}");
                return Json(new
                {
                    canEditEmployee = false,
                    currentEmployee = (object)null,
                    isApprover = false
                });
            }
        }

        /// <summary>
        /// Gets list of employees based on current user's data scope.
        /// Supports OWN_AND_ASSIGNED: approvers see their own record + their assigned employees.
        /// </summary>
        [HttpGet]
        public virtual JsonResult GetEmployeeList()
        {
            try
            {
                var query = new StringBuilder(@"
                    SELECT
                        b.employeeNo,
                        CONCAT(IFNULL(b.firstName, ''), ' ', IFNULL(CONCAT(b.middleName, ' '), ''), IFNULL(b.lastName, '')) as employeeName
                    FROM e_basicinfo b
                    WHERE b.isActive = 1");

                var parameters = new DynamicParameters();

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenEmployeesFilter(query, parameters);

                query.Append(" ORDER BY b.firstName, b.lastName");

                return Json(_db.Query(query.ToString(), parameters).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        #endregion

        #region Notification Methods

        /// <summary>
        /// Centralized notification method for all timekeeping requests.
        /// Automatically determines recipients based on request status.
        /// </summary>
        protected void NotifyRequestAction(
            string requestType,
            int requestId,
            string requestorEmployeeNo,
            string newStatus)
        {
            try
            {
                if (_configuration == null)
                {
                    Console.WriteLine("Configuration not available for notifications");
                    return;
                }

                var actionType = newStatus.ToLower();
                var recipients = new List<string>();

                if (actionType == "pending")
                {
                    recipients = GetApproversForEmployee(requestorEmployeeNo);
                }
                else if (actionType == "approved" || actionType == "declined" || actionType == "cancelled")
                {
                    recipients.Add(requestorEmployeeNo);
                }

                foreach (var recipientEmployeeNo in recipients)
                {
                    CreateNotification(recipientEmployeeNo, requestType, requestId, requestorEmployeeNo, actionType);
                }

                Console.WriteLine($"Notifications created: Type={requestType}, Action={actionType}, Recipients={recipients.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in NotifyRequestAction: {ex.Message}");
            }
        }

        private List<string> GetApproversForEmployee(string employeeNo)
        {
            try
            {
                var employeeDetails = _db.QueryFirstOrDefault<dynamic>(@"
                    SELECT employmentStatus, branchCode, departmentCode, positionCode
                    FROM e_basicinfo
                    WHERE employeeNo = @employeeNo", new { employeeNo });

                if (employeeDetails == null) return new List<string>();

                // Only notify actual configured approvers from e_approver
                var approvers = _db.Query<string>(@"
                    SELECT DISTINCT a.approverNo
                    FROM e_approver a
                    WHERE a.isActive = 1
                    AND a.approverNo != @employeeNo
                    AND (
                        (a.typeList = 1 AND a.employeeNo = @employmentStatus) OR
                        (a.typeList = 2 AND a.employeeNo = @branchCode) OR
                        (a.typeList = 3 AND a.employeeNo = @departmentCode) OR
                        (a.typeList = 4 AND a.employeeNo = @positionCode) OR
                        (a.typeList = 5 AND a.employeeNo = 'ALL') OR
                        (a.typeList = 6 AND a.employeeNo = @employeeNo)
                    )",
                    new
                    {
                        employeeNo,
                        employmentStatus = (string)employeeDetails.employmentStatus,
                        branchCode = (string)employeeDetails.branchCode,
                        departmentCode = (string)employeeDetails.departmentCode,
                        positionCode = (string)employeeDetails.positionCode
                    }).ToList();

                return approvers;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting approvers: {ex.Message}");
                return new List<string>();
            }
        }

        //private List<string> GetApproversForEmployee(string employeeNo)
        //{
        //    try
        //    {
        //        var admins = _db.Query<string>(@"
        //            SELECT DISTINCT u.userCode
        //            FROM s_user u
        //            WHERE u.roleCode = 'RL-000000'
        //            AND u.isActive = 1
        //            AND u.userCode != @employeeNo",
        //            new { employeeNo }).ToList();

        //        var employeeDetails = _db.QueryFirstOrDefault<dynamic>(@"
        //            SELECT employmentStatus, branchCode, departmentCode, positionCode
        //            FROM e_basicinfo
        //            WHERE employeeNo = @employeeNo", new { employeeNo });

        //        if (employeeDetails == null)
        //            return admins;

        //        var approvers = _db.Query<string>(@"
        //            SELECT DISTINCT a.approverNo
        //            FROM e_approver a
        //            WHERE a.isActive = 1
        //            AND a.approverNo != @employeeNo
        //            AND (
        //                (a.typeList = 1 AND a.employeeNo = @employmentStatus) OR
        //                (a.typeList = 2 AND a.employeeNo = @branchCode) OR
        //                (a.typeList = 3 AND a.employeeNo = @departmentCode) OR
        //                (a.typeList = 4 AND a.employeeNo = @positionCode) OR
        //                (a.typeList = 5 AND a.employeeNo = 'ALL') OR
        //                (a.typeList = 6 AND a.employeeNo = @employeeNo)
        //            )",
        //            new
        //            {
        //                employeeNo,
        //                employmentStatus = employeeDetails.employmentStatus,
        //                branchCode = employeeDetails.branchCode,
        //                departmentCode = employeeDetails.departmentCode,
        //                positionCode = employeeDetails.positionCode
        //            }).ToList();

        //        return admins.Concat(approvers).Distinct().ToList();
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Error getting approvers: {ex.Message}");
        //        return _db.Query<string>(@"
        //            SELECT DISTINCT u.userCode
        //            FROM s_user u
        //            WHERE u.roleCode = 'RL-000000' AND u.isActive = 1
        //            AND u.userCode != @employeeNo",
        //            new { employeeNo }).ToList();
        //    }
        //}

        private void CreateNotification(
            string recipientEmployeeNo,
            string requestType,
            int requestId,
            string requestorEmployeeNo,
            string actionType)
        {
            try
            {
                var message = GenerateNotificationMessage(requestType, actionType, requestorEmployeeNo);
                var notificationCode = $"NOTIF-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 8)}";

                _db.Execute(@"
                    INSERT INTO s_notification
                    (notificationCode, recipientEmployeeNo, requestType, requestId,
                     requestorEmployeeNo, actionType, message, isRead, dtCreated, isActive)
                    VALUES
                    (@notificationCode, @recipientEmployeeNo, @requestType, @requestId,
                     @requestorEmployeeNo, @actionType, @message, 0, NOW(), 1)",
                    new { notificationCode, recipientEmployeeNo, requestType, requestId, requestorEmployeeNo, actionType, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating notification: {ex.Message}");
            }
        }

        private string GenerateNotificationMessage(string requestType, string actionType, string requestorEmployeeNo)
        {
            var requestorName = _db.QueryFirstOrDefault<string>(
                "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                new { employeeNo = requestorEmployeeNo }) ?? "An employee";

            var requestTypeDisplay = GetRequestTypeDisplay(requestType);

            return actionType switch
            {
                "pending" => $"{requestorName} submitted a {requestTypeDisplay} request that requires your approval.",
                "approved" => $"Your {requestTypeDisplay} request has been approved.",
                "declined" => $"Your {requestTypeDisplay} request has been declined.",
                "cancelled" => $"Your {requestTypeDisplay} request has been cancelled.",
                _ => $"Status update for your {requestTypeDisplay} request."
            };
        }

        private string GetRequestTypeDisplay(string requestType)
        {
            return requestType switch
            {
                "leave" => "Leave",
                "changeSchedule" => "Change Schedule",
                "officialBusiness" => "Official Business",
                "cto" => "CTO",
                "offsetCredit" => "Offset Credit",
                "overtime" => "Overtime",
                "undertime" => "Undertime",
                "workFromHome" => "Work From Home",
                _ => "Request"
            };
        }

        protected void NotifyNextApprover(
            string requestType,
            int requestId,
            string requestorEmployeeNo,
            int nextLevel)
        {
            try
            {
                var employeeDetails = _db.QueryFirstOrDefault<dynamic>(@"
                    SELECT employmentStatus, branchCode, departmentCode, positionCode
                    FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { employeeNo = requestorEmployeeNo });

                if (employeeDetails == null) return;

                var nextApprovers = _db.Query<string>(@"
                    SELECT DISTINCT a.approverNo
                    FROM e_approver a
                    WHERE a.isActive      = 1
                    AND   a.approverLevel = @nextLevel
                    AND (
                        (a.typeList = 1 AND a.employeeNo = @employmentStatus) OR
                        (a.typeList = 2 AND a.employeeNo = @branchCode)       OR
                        (a.typeList = 3 AND a.employeeNo = @departmentCode)   OR
                        (a.typeList = 4 AND a.employeeNo = @positionCode)     OR
                        (a.typeList = 5 AND a.employeeNo = 'ALL')             OR
                        (a.typeList = 6 AND a.employeeNo = @employeeNo)
                    )",
                    new
                    {
                        nextLevel,
                        employeeNo = requestorEmployeeNo,
                        employmentStatus = employeeDetails.employmentStatus,
                        branchCode = employeeDetails.branchCode,
                        departmentCode = employeeDetails.departmentCode,
                        positionCode = employeeDetails.positionCode
                    }).ToList();

                foreach (var approverNo in nextApprovers)
                    CreateNotification(approverNo, requestType, requestId, requestorEmployeeNo, "pending");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in NotifyNextApprover: {ex.Message}");
            }
        }

        protected bool HasBroadDataScope()
        {
            var scopeType = _db.QueryFirstOrDefault<string>(@"
                SELECT scopeType
                FROM s_role
                WHERE roleCode = @roleCode AND isActive = 1
                LIMIT 1", new { roleCode = RoleCode });

            return scopeType is "ALL" or "BRANCH" or "DEPARTMENT" or "RANK_FILTER"
                              or "POSITION_FILTER" or "EMPLOYMENT_STATUS" or "CUSTOM";
        }

        #endregion

        #region Helper Methods

        protected bool RecordExists(string table, string column, string value)
        {
            var sql = $"SELECT COUNT(*) FROM {table} WHERE {column} = @value AND isActive = 1";
            return _db.QuerySingle<int>(sql, new { value }) > 0;
        }

        protected async Task<ApproverInfo> GetApproverInfoCachedAsync()
        {
            var cacheKey = $"ApproverInfo_{EmployeeNo}";

            if (HttpContext.Items.TryGetValue(cacheKey, out var cached))
                return (ApproverInfo)cached;

            var info = await _approverService.GetApproverInfoAsync(EmployeeNo);
            HttpContext.Items[cacheKey] = info;
            return info;
        }

        /// <summary>
        /// Marks Approved timekeeping requests as Processed when their
        /// relevant date falls within a posted payroll cutoff period.
        ///
        /// Parameters:
        ///   tableName      — e.g. "rq_changeschedule"
        ///   dateColumn     — the date field to match against p_biometricsline
        ///                    e.g. "effectivityDate", "leaveDateFrom", "obDateIn"
        ///   finalGateColumn— the column that holds the overall final status.
        ///                    Most tables: "statusLevel4"
        ///                    rq_undertime / rq_overtime / rq_cto: "statusName"
        ///   alsoUpdateStatusName
        ///                 — true when the table has a separate "statusName"
        ///                    column that must be kept in sync with statusLevel4
        ///                    (rq_overtime, rq_cto have both; rq_undertime uses
        ///                    statusName as the sole gate so pass false there).
        /// </summary>
        protected async Task MarkRequestsAsProcessedAsync(
            string tableName,
            string dateColumn,
            string finalGateColumn = "statusLevel4",
            bool alsoUpdateStatusName = false)
        {
            try
            {
                // Find all posted cutoff periods
                var postedPeriods = await _db.QueryAsync<dynamic>(@"
                    SELECT DISTINCT dateFrom, dateTo
                    FROM p_biometricsline
                    WHERE statusName = 'posted'
                      AND isActive   = 1");

                if (!postedPeriods.Any()) return;

                foreach (var period in postedPeriods)
                {
                    DateTime dateFrom = (DateTime)period.dateFrom;
                    DateTime dateTo = (DateTime)period.dateTo;

                    // Build SET clause
                    var setClauses = new List<string>
                    {
                        $"{finalGateColumn}   = 'Processed'",
                        "dtLastModified     = NOW()",
                        "lastModifiedByUser = 'SYSTEM'"
                    };

                    // Also update statusLevel1-3 to Processed for consistency
                    setClauses.Add("statusLevel1 = 'Processed'");
                    setClauses.Add("statusLevel2 = 'Processed'");
                    setClauses.Add("statusLevel3 = 'Processed'");

                    // If statusLevel4 is the gate, also set it explicitly
                    if (finalGateColumn != "statusLevel4")
                    {
                        setClauses.Add("statusLevel4 = 'Processed'");
                    }

                    // If the table has a separate statusName column to keep in sync
                    if (alsoUpdateStatusName && finalGateColumn != "statusName")
                    {
                        setClauses.Add("statusName = 'Processed'");
                    }

                    // rq_undertime uses statusName as the final gate AND has a separate
                    // dtLastModifiedByUser column (not dtLastModified). Patch the audit column.
                    if (finalGateColumn == "statusName")
                    {
                        setClauses.Add("dtLastModifiedByUser = NOW()");
                    }

                    var sql = $@"
                        UPDATE {tableName}
                        SET    {string.Join(", ", setClauses)}
                        WHERE  {finalGateColumn} = 'Approved'
                          AND  isActive          = 1
                          AND  {dateColumn} BETWEEN @dateFrom AND @dateTo";

                    await _db.ExecuteAsync(sql, new
                    {
                        dateFrom = dateFrom.Date,
                        dateTo = dateTo.Date
                    });
                }
            }
            catch (Exception ex)
            {
                // Non-fatal — log and continue so the list still loads
                Console.WriteLine($"MarkRequestsAsProcessedAsync [{tableName}]: {ex.Message}");
            }
        }

        /// <summary>
        /// Triggers a Processed-status sync for all timekeeping request
        /// tables. Safe to call multiple times — it is idempotent.
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> TriggerProcessedSync()
        {
            try
            {
                await MarkRequestsAsProcessedAsync("rq_changeschedule", "effectivityDate");
                await MarkRequestsAsProcessedAsync("rq_leave", "leaveDateFrom");
                await MarkRequestsAsProcessedAsync("rq_officialbusiness", "obDateIn");
                await MarkRequestsAsProcessedAsync("rq_workfromhome", "wfhDateIn");
                await MarkRequestsAsProcessedAsync("rq_overtime", "overTimeDateIN", "statusLevel4", alsoUpdateStatusName: true);
                //await MarkRequestsAsProcessedAsync("rq_cto", "overTimeDateIN", "statusLevel4", alsoUpdateStatusName: true);
                await MarkRequestsAsProcessedAsync("rq_undertime", "undertimeDateIN", "statusName", alsoUpdateStatusName: false);
                await MarkRequestsAsProcessedAsync("rq_cto", "overTimeDateIN", "statusLevel4", alsoUpdateStatusName: true);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        #endregion
    }
}