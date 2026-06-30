using Dapper;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.LeaveRequest
{
    [ModuleAuthorize("RleaveM")]
    public class LeaveRequestController : TimekeepingRequestBaseController
    {
        public LeaveRequestController(
        IDbConnection db,
        IAuditTrailService auditTrail,
        IEmailService emailService,
        IApproverService approverService)
        : base(db, auditTrail, "RleaveM")
        {
            _approverService = approverService;
            _emailService = (IEmailService)emailService;
        }

        public IActionResult Index()
        {
            return View("~/Views/Timekeeping/LeaveRequest.cshtml");
        }

        // Get employee leave types WITH SECURITY CHECK
        [HttpGet]
        public JsonResult GetEmployeeLeaveTypes(string employeeNo)
        {
            try
            {
                // Security check using base method
                if (!CanViewEmployee(employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to view this employee's leave types." });
                }

                // Use m_leave for accurate balance
                // Only show leave types the employee is already entitled to as of today
                var query = @"
                    SELECT 
                        e.leaveCode,
                        s.leaveName,
                        e.leaveDays,
                        m.usedCredits AS usedLeaveDays,
                        m.availableBalance AS remainingLeaveDays
                    FROM e_leave e
                    LEFT JOIN s_leave s ON s.leaveCode = e.leaveCode
                    LEFT JOIN (
                        SELECT ml.*
                        FROM m_leave ml
                        INNER JOIN (
                            SELECT employeeNo, leaveCode, MAX(id) AS maxId
                            FROM m_leave
                            GROUP BY employeeNo, leaveCode
                        ) x
                          ON x.employeeNo = ml.employeeNo
                         AND x.leaveCode = ml.leaveCode
                         AND x.maxId = ml.id
                    ) m
                      ON m.employeeNo = e.employeeNo
                     AND m.leaveCode = e.leaveCode
                    WHERE e.employeeNo = @employeeNo
                        AND e.isActive = 1
                        AND e.isLeave = 1
                        AND e.leaveCode != 'CTO'
                        AND (
                            -- Standard leave: dateEntitled must be today or earlier
                            (
                                e.dateEntitled IS NOT NULL
                                AND e.dateEntitled != '0000-00-00'
                                AND e.dateEntitled <= CURDATE()
                            )
                            OR
                            -- Maternity/Paternity: dateFrom must be today or earlier
                            (
                                e.dateFrom IS NOT NULL
                                AND e.dateFrom != '0000-00-00'
                                AND e.dateFrom <= CURDATE()
                            )
                            OR
                            -- No entitlement dates set at all (e.g. LWOP) — always show
                            (
                                (e.dateEntitled IS NULL OR e.dateEntitled = '0000-00-00')
                                AND (e.dateFrom IS NULL OR e.dateFrom = '0000-00-00')
                            )
                        )
                    ORDER BY s.leaveName ASC, e.leaveDays DESC";

                var leaveList = _db.Query<EmployeeLeaveModel>(query, new { employeeNo }).ToList();
                return Json(new { data = leaveList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeLeaveTypes: {ex.Message}");
                return Json(new { data = new List<EmployeeLeaveModel>() });
            }
        }

        // Get leave credits WITH SECURITY CHECK
        [HttpGet]
        public JsonResult GetLeaveCredits(string employeeNo, string leaveCode)
        {
            try
            {
                // Security check using base method
                if (!CanViewEmployee(employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to view this employee's leave credits." });
                }

                var query = @"
                    SELECT COALESCE(SUM(rq.leaveCountDays), 0) AS usedCredits
                    FROM rq_leave rq
                    WHERE rq.isActive = 1
                        AND rq.leaveCode = @leaveCode
                        AND rq.statusLevel4 = 'Approved'
                        AND rq.employeeNo = @employeeNo
                        AND YEAR(rq.leaveDateFrom) = YEAR(NOW())";

                var usedCredits = _db.QueryFirstOrDefault<decimal>(query, new { employeeNo, leaveCode });
                return Json(new { usedCredits });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveCredits: {ex.Message}");
                return Json(new { usedCredits = 0 });
            }
        }

        // Get leave request list WITH DATA SCOPE
        [HttpGet]
        public async Task<JsonResult> GetLeaveRequestList(string status, string branch, string department, string dateFrom, string dateTo)
        {
            try
            {
                var approverInfo = await GetApproverInfoCachedAsync();
                var hasFullAccess = HasFullAccess();
                var hasBroadScope = HasBroadDataScope();

                var query = new StringBuilder();
                var parameters = new DynamicParameters();

                if (hasFullAccess || hasBroadScope)
                {
                    query.Append(@"
                        SELECT 
                            rq.id,
                            rq.employeeNo,
                            CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,
                            rq.leaveType,
                            rq.leaveCode,
                            s.leaveName,
                            DATE_FORMAT(rq.leaveDateFrom, '%m/%d/%Y') AS displayDateFrom,
                            DATE_FORMAT(rq.leaveDateTo, '%m/%d/%Y') AS displayDateTo,
                            rq.leaveCountDays,
                            rq.leaveReason,
                            rq.dtAdded AS dateRequested,
                            rq.statusLevel1,
                            rq.statusLevel2,
                            rq.statusLevel3,
                            rq.statusLevel4,
                            rq.statusLevel4 AS statusName,
                            rq.remarks,
                            CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                        FROM rq_leave rq
                        LEFT JOIN s_leave s ON rq.leaveCode = s.leaveCode
                        JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                        LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                        WHERE rq.isActive = 1 
                            AND rq.leaveCode != 'CTO'");

                    ApplyDataScopeFilter(query, parameters);
                }
                else if (approverInfo.IsApprover)
                {
                    query.Append(@"
                        SELECT 
                            rq.id,
                            rq.employeeNo,
                            CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,
                            rq.leaveType,
                            rq.leaveCode,
                            s.leaveName,
                            DATE_FORMAT(rq.leaveDateFrom, '%m/%d/%Y') AS displayDateFrom,
                            DATE_FORMAT(rq.leaveDateTo, '%m/%d/%Y') AS displayDateTo,
                            rq.leaveCountDays,
                            rq.leaveReason,
                            rq.dtAdded AS dateRequested,
                            rq.statusLevel1,
                            rq.statusLevel2,
                            rq.statusLevel3,
                            rq.statusLevel4,
                            rq.statusLevel4 AS statusName,
                            rq.remarks,
                            CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                        FROM rq_leave rq
                        LEFT JOIN s_leave s ON rq.leaveCode = s.leaveCode
                        INNER JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                        LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                        WHERE rq.isActive = 1 
                            AND rq.leaveCode != 'CTO'
                            AND (
                                rq.employeeNo = @currentEmployeeNo
                                OR rq.employeeNo IN (
                                    SELECT DISTINCT b2.employeeNo
                                    FROM e_approver a
                                    INNER JOIN e_basicinfo b2 ON (
                                        CASE a.typeList
                                            WHEN 1 THEN b2.employmentStatus = a.employeeNo
                                            WHEN 2 THEN b2.branchCode = a.employeeNo
                                            WHEN 3 THEN b2.departmentCode = a.employeeNo
                                            WHEN 4 THEN b2.positionCode = a.employeeNo
                                            WHEN 5 THEN a.employeeNo = 'ALL'
                                            WHEN 6 THEN b2.employeeNo = a.employeeNo
                                            ELSE FALSE
                                        END
                                    )
                                    WHERE a.approverNo = @currentEmployeeNo
                                    AND a.isActive = 1
                                    AND b2.isActive = 1
                                )
                            )");

                    parameters.Add("@currentEmployeeNo", EmployeeNo);
                }
                else
                {
                    query.Append(@"
                        SELECT 
                            rq.id,
                            rq.employeeNo,
                            CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,
                            rq.leaveType,
                            rq.leaveCode,
                            s.leaveName,
                            DATE_FORMAT(rq.leaveDateFrom, '%m/%d/%Y') AS displayDateFrom,
                            DATE_FORMAT(rq.leaveDateTo, '%m/%d/%Y') AS displayDateTo,
                            rq.leaveCountDays,
                            rq.leaveReason,
                            rq.dtAdded AS dateRequested,
                            rq.statusLevel1,
                            rq.statusLevel2,
                            rq.statusLevel3,
                            rq.statusLevel4,
                            rq.statusLevel4 AS statusName,
                            rq.remarks,
                            CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                        FROM rq_leave rq
                        LEFT JOIN s_leave s ON rq.leaveCode = s.leaveCode
                        INNER JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                        LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                        WHERE rq.isActive = 1 
                            AND rq.leaveCode != 'CTO'
                            AND rq.employeeNo = @currentEmployeeNo");

                    parameters.Add("@currentEmployeeNo", EmployeeNo);
                }

                // Apply hidden employees filter (but don't hide own record)
                var hiddenEmployees = _db.QueryFirstOrDefault<string>(@"
                    SELECT hiddenEmployees 
                    FROM s_role 
                    WHERE roleCode = @roleCode AND isActive = 1
                    LIMIT 1
                ", new { roleCode = RoleCode });

                if (!string.IsNullOrWhiteSpace(hiddenEmployees))
                {
                    var hiddenList = hiddenEmployees.Split(',').Select(x => x.Trim()).ToArray();
                    query.Append(" AND (b.employeeNo NOT IN @hiddenEmployees OR b.employeeNo = @currentEmployeeNoHidden)");
                    parameters.Add("@hiddenEmployees", hiddenList);
                    parameters.Add("@currentEmployeeNoHidden", EmployeeNo);
                }

                // Apply status filter - default to Pending
                if (string.IsNullOrWhiteSpace(status) || status.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND rq.statusLevel4 = @status");
                    parameters.Add("@status", "Pending");
                }
                else if (!status.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND rq.statusLevel4 = @status");
                    parameters.Add("@status", status);
                }

                // Apply branch filter
                if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND b.branchCode = @branch");
                    parameters.Add("@branch", branch);
                }

                // Apply department filter
                if (!string.IsNullOrWhiteSpace(department) && !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND b.departmentCode = @department");
                    parameters.Add("@department", department);
                }

                // Apply date range filter
                if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
                {
                    if (DateTime.TryParseExact(dateFrom, "MM/dd/yyyy", null, System.Globalization.DateTimeStyles.None, out var parsedFrom) &&
                        DateTime.TryParseExact(dateTo, "MM/dd/yyyy", null, System.Globalization.DateTimeStyles.None, out var parsedTo))
                    {
                        query.Append(" AND rq.leaveDateFrom BETWEEN @dateFrom AND @dateTo");
                        parameters.Add("@dateFrom", parsedFrom.ToString("yyyy-MM-dd"));
                        parameters.Add("@dateTo", parsedTo.ToString("yyyy-MM-dd"));
                    }
                }

                query.Append(" ORDER BY rq.id DESC");

                await MarkRequestsAsProcessedAsync("rq_leave", "leaveDateFrom");

                var requests = await _db.QueryAsync<dynamic>(query.ToString(), parameters);
                return Json(new { data = requests });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveRequestList: {ex.Message}");
                return Json(new { data = new List<object>() });
            }
        }

        // Get leave request WITH SECURITY CHECK
        [HttpGet]
        public async Task<JsonResult> GetLeaveRequest(int id)
        {
            try
            {
                var query = @"
                    SELECT
                        rq.id,
                        rq.employeeNo,
                        rq.leaveCode,
                        rq.leaveType,
                        DATE_FORMAT(rq.leaveDateFrom, '%m/%d/%Y') AS displayDateFrom,
                        DATE_FORMAT(rq.leaveDateTo,   '%m/%d/%Y') AS displayDateTo,
                        rq.leaveCountDays,
                        rq.leaveCountHours,
                        rq.leaveReason,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusLevel4 AS statusName,
                        rq.remarks,
                        rq.creditDeductionOnly,
                        CONCAT(b.lastName, ', ', b.firstName, ' ',
                               LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName
                    FROM rq_leave rq
                    LEFT JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                    WHERE rq.id = @id AND rq.isActive = 1";

                var request = _db.QueryFirstOrDefault<dynamic>(query, new { id });

                if (request == null) return Json(null);

                if (!CanViewEmployee((string)request.employeeNo))
                    return Json(new { error = "Access denied. You don't have permission to view this leave request." });

                string employeeNo = (string)request.employeeNo;
                string statusLevel1 = (string)request.statusLevel1 ?? "Pending";
                string statusLevel2 = (string)request.statusLevel2;
                string statusLevel3 = (string)request.statusLevel3;
                string statusLevel4 = (string)request.statusLevel4;

                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();
                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                bool canCurrentUserApprove = false;
                int? currentUserLevel = null;

                bool isFull = HasFullAccess();

                if (isFull)
                {
                    canCurrentUserApprove = statusLevel4 == "Pending";
                }
                else
                {
                    currentUserLevel = await _approverService
                        .GetApproverLevelForEmployeeAsync(EmployeeNo, employeeNo);

                    if (currentUserLevel.HasValue && statusLevel4 == "Pending")
                    {
                        int level = currentUserLevel.Value;

                        if (level == 4)
                        {
                            canCurrentUserApprove = true;
                        }
                        else
                        {
                            bool lowerLevelsApproved = requiredLevels
                                .Where(l => l < level)
                                .All(l => l switch
                                {
                                    1 => statusLevel1 == "Approved",
                                    2 => statusLevel2 == "Approved",
                                    3 => statusLevel3 == "Approved",
                                    _ => false
                                });

                            bool thisLevelAlreadyApproved = level switch
                            {
                                1 => statusLevel1 == "Approved",
                                2 => statusLevel2 == "Approved",
                                3 => statusLevel3 == "Approved",
                                4 => statusLevel4 == "Approved",
                                _ => false
                            };

                            canCurrentUserApprove = lowerLevelsApproved && !thisLevelAlreadyApproved;
                        }
                    }
                }

                return Json(new
                {
                    id = (int)request.id,
                    employeeNo,
                    fullName = (string)request.fullName,
                    leaveCode = (string)request.leaveCode,
                    leaveType = (string)request.leaveType,
                    displayDateFrom = (string)request.displayDateFrom,
                    displayDateTo = (string)request.displayDateTo,
                    leaveCountDays = (object)request.leaveCountDays,
                    leaveCountHours = (object)request.leaveCountHours,
                    leaveReason = (string)request.leaveReason,
                    remarks = (string)request.remarks,
                    creditDeductionOnly = (object)request.creditDeductionOnly,
                    statusLevel1,
                    statusLevel2,
                    statusLevel3,
                    statusLevel4,
                    statusName = statusLevel4,
                    requiredLevels,
                    canCurrentUserApprove,
                    currentUserLevel
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveRequest: {ex.Message}");
                return Json(null);
            }
        }

        // Calculate leave days (no security check needed - calculation only)
        [HttpGet]
        public JsonResult CalculateLeaveDays(DateTime dateFrom, DateTime dateTo, string leaveType)
        {
            try
            {
                // Calculate total days including both start and end dates
                int totalDays = (int)(dateTo - dateFrom).TotalDays + 1;
                if (totalDays < 0) totalDays = 0;

                // Apply leave type multiplier: whole = 1, first/second half = 0.5
                decimal multiplier = leaveType == "whole" ? 1m : 0.5m;
                decimal leaveCount = totalDays * multiplier;

                return Json(new { leaveCount });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CalculateLeaveDays: {ex.Message}");
                return Json(new { leaveCount = 0 });
            }
        }

        // Add leave request - Only validate, NO m_leave recording
        [HttpPost]
        public async Task<JsonResult> AddLeaveRequest(LeaveRequestModel model, IFormFile attachment)
        {
            try
            {
                if (!CanViewEmployee(model.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to create a leave request for this employee." });

                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                if (model.leaveDateFrom == null || model.leaveDateTo == null)
                    return Json(new { success = false, message = "Leave dates are required!" });

                // Duplicate filing check — block overlapping leave requests
                var duplicate = _db.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) FROM rq_leave
                    WHERE employeeNo = @employeeNo
                    AND isActive = 1
                    AND statusLevel4 NOT IN ('Cancelled', 'Declined')
                    AND leaveDateFrom <= @leaveDateTo
                    AND leaveDateTo   >= @leaveDateFrom",
                    new { model.employeeNo, model.leaveDateFrom, model.leaveDateTo });

                if (duplicate > 0)
                    return Json(new { success = false, message = "A leave request already exists for the selected date range." });

                if (model.leaveDateTo < model.leaveDateFrom)
                    return Json(new { success = false, message = "End date cannot be earlier than start date!" });

                if (string.IsNullOrWhiteSpace(model.leaveCode))
                    return Json(new { success = false, message = "Leave type is required!" });

                // ── Entitlement validation ────────────────────────────────────────────
                var (isEntitled, entitlementMessage) = ValidateLeaveEntitlement(
                    model.employeeNo, model.leaveCode, model.leaveDateFrom);

                if (!isEntitled)
                    return Json(new { success = false, message = entitlementMessage });

                // ── Balance validation — runs for ALL employees including Level 4 ─────
                var existingBalance = _db.QueryFirstOrDefault<dynamic>(
                        @"SELECT availableBalance FROM m_leave 
                  WHERE employeeNo = @EmployeeNo AND leaveCode = @LeaveCode 
                  ORDER BY id DESC LIMIT 1",
                    new { EmployeeNo = model.employeeNo, LeaveCode = model.leaveCode });

                if (existingBalance != null && model.leaveCode != "LWOP" && model.leaveCode != "SUS")
                {
                    double availableBalance = Convert.ToDouble(existingBalance.availableBalance);
                    double requestedDays = Convert.ToDouble(model.leaveCountDays ?? 0);

                    if (requestedDays > availableBalance)
                        return Json(new { success = false, message = $"Insufficient leave balance. Available: {availableBalance:F2} days, Requested: {requestedDays:F2} days." });
                }

                // ── Check if the requestor is a Level 4 approver ──────────────────────
                // If yes, we will auto-approve immediately after insert.
                bool isLevel4Approver = _db.QuerySingle<int>(@"
                    SELECT COUNT(*)
                    FROM e_approver
                    WHERE approverNo    = @employeeNo
                    AND   approverLevel = 4
                    AND   isActive      = 1",
                    new { model.employeeNo }) > 0;

                // ── Insert new Leave Request ──────────────────────────────────────────
                // Initial status is always Pending on insert; we update it right after
                // for Level 4 approvers so the row is never left in a dangling state.
                var sql = @"
                    INSERT INTO rq_leave 
                    (employeeNo, leaveCode, leaveDateFrom, leaveDateTo, leaveCountDays, leaveCountHours, 
                     leaveReason, leaveType, statusLevel1, statusLevel2, statusLevel3, statusLevel4, remarks, 
                     creditDeductionOnly, isActive, dtAdded, addedByUser, requestedByUser,
                     dtStatus, statusByUser, dtStatusLevel1, statusByLevel1,
                     dtStatusLevel2, statusByLevel2, 
                     dtStatusLevel3, statusByLevel3, dtStatusLevel4, statusByLevel4) 
                    VALUES 
                    (@employeeNo, @leaveCode, @leaveDateFrom, @leaveDateTo, @leaveCountDays, @leaveCountHours, 
                     @leaveReason, @leaveType, 'Pending', 'Pending', 'Pending', 'Pending', @remarks, 
                     @creditDeductionOnly, 1, NOW(), @addedByUser, @requestedByUser,
                     NOW(), @addedByUser, NOW(), @addedByUser,
                     NOW(), @addedByUser, 
                     NOW(), @addedByUser, NOW(), @addedByUser);
                    SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    model.employeeNo,
                    model.leaveCode,
                    model.leaveDateFrom,
                    model.leaveDateTo,
                    leaveCountDays = model.leaveCountDays ?? 0,
                    leaveCountHours = model.leaveCountHours ?? 0,
                    leaveReason = model.leaveReason ?? "",
                    leaveType = model.leaveType ?? "whole",
                    remarks = model.remarks ?? "",
                    model.creditDeductionOnly,
                    addedByUser = EmployeeNo ?? model.employeeNo,
                    requestedByUser = EmployeeNo ?? model.employeeNo
                });

                // ── Record to m_leave immediately on submission ───────────────────────
                // This runs for ALL employees regardless of Level 4 status.
                // Credits are deducted on submission (existing behaviour — unchanged).
                // Capture leaveCode in a local variable to prevent any accidental
                // model mutation during async operations (fixes wrong leaveCode bug)
                string leaveCodeToRecord = model.leaveCode;

                var balanceRow = _db.QueryFirstOrDefault<dynamic>(
                        @"SELECT availableBalance, accrual, usedCredits FROM m_leave 
                          WHERE employeeNo = @EmployeeNo AND leaveCode = @LeaveCode 
                          ORDER BY id DESC LIMIT 1",
                    new { EmployeeNo = model.employeeNo, LeaveCode = leaveCodeToRecord });

                // No-credit leave types — record with all zeros for tracking purposes
                var noCreditLeaveCodes = new[] { "LWOP", "SUS", "ML", "PL" };
                bool isNoCreditLeave = noCreditLeaveCodes.Contains(leaveCodeToRecord);

                if (isNoCreditLeave)
                {
                    // Record to ledger with all zeros — just for tracking purposes
                    _db.Execute(@"
                        INSERT INTO m_leave (
                            employeeNo, rq_leaveID, leaveCode, statusName,
                            beginningBalance, accrual, usedCredits, availableBalance,
                            isActive, dtAdded, addedByUser
                        ) VALUES (
                            @EmployeeNo, @RqLeaveID, @LeaveCode, 'APPROVED LEAVE REQUEST',
                            0, 0, 0, 0,
                            1, NOW(), @UserCode
                        )", new
                    {
                        EmployeeNo = model.employeeNo,
                        RqLeaveID = newId,
                        LeaveCode = leaveCodeToRecord,  // ← fixed: use local variable
                        UserCode = EmployeeNo ?? model.employeeNo
                    });
                }
                else if (balanceRow != null)
                {
                    double leaveDays = Convert.ToDouble(model.leaveCountDays ?? 0);
                    double currentAvailable = Convert.ToDouble(balanceRow.availableBalance);
                    double newAvailable = currentAvailable - leaveDays;
                    double newUsedCredits = leaveDays;

                    _db.Execute(@"
                        INSERT INTO m_leave (
                            employeeNo, rq_leaveID, leaveCode, statusName,
                            beginningBalance, accrual, usedCredits, availableBalance,
                            isActive, dtAdded, addedByUser
                        ) VALUES (
                            @EmployeeNo, @RqLeaveID, @LeaveCode, 'APPROVED LEAVE REQUEST',
                            @BeginningBalance, 0, @UsedCredits, @AvailableBalance,
                            1, NOW(), @UserCode
                        )", new
                    {
                        EmployeeNo = model.employeeNo,
                        RqLeaveID = newId,
                        LeaveCode = leaveCodeToRecord,
                        BeginningBalance = currentAvailable,
                        UsedCredits = newUsedCredits,
                        AvailableBalance = newAvailable,
                        UserCode = EmployeeNo ?? model.employeeNo
                    });
                }

                // ── Auto-approve for Level 4 approvers ───────────────────────────────
                if (isLevel4Approver)
                {
                    // Level 4 bypass: all status levels are set to Approved immediately.
                    _db.Execute(@"
                        UPDATE rq_leave
                        SET statusLevel1       = 'Approved',
                            dtStatusLevel1     = NOW(),
                            statusByLevel1     = @approvedBy,
                            statusLevel2       = 'Approved',
                            dtStatusLevel2     = NOW(),
                            statusByLevel2     = @approvedBy,
                            statusLevel3       = 'Approved',
                            dtStatusLevel3     = NOW(),
                            statusByLevel3     = @approvedBy,
                            statusLevel4       = 'Approved',
                            dtStatusLevel4     = NOW(),
                            statusByLevel4     = @approvedBy,
                            dtStatus           = NOW(),
                            statusByUser       = @approvedBy,
                            dtLastModified     = NOW(),
                            lastModifiedByUser = @approvedBy
                        WHERE id = @id",
                        new { id = newId, approvedBy = model.employeeNo });

                    // Notify the requestor that their request was auto-approved
                    NotifyRequestAction("leave", newId, model.employeeNo, "approved");

                    // Distinct audit message so it's clear this was not a manual approval
                    _auditTrail.Log("rq_leave", newId, "AUTO-APPROVED",
                        $"Leave request auto-approved on creation: {model.employeeNo} is a Level 4 approver. " +
                        $"Period: {model.leaveDateFrom:yyyy-MM-dd} to {model.leaveDateTo:yyyy-MM-dd} " +
                        $"({model.leaveCode}, {model.leaveCountDays:F2} days). " +
                        $"Leave credits deducted immediately as per standard flow.");
                }
                else
                {
                    // Standard flow — notify approvers that a request is pending
                    NotifyRequestAction("leave", newId, model.employeeNo, "pending");

                    _auditTrail.Log("rq_leave", newId, "CREATED",
                        $"Added leave request for {model.employeeNo}: {model.leaveDateFrom:yyyy-MM-dd} to {model.leaveDateTo:yyyy-MM-dd} ({model.leaveCode})");
                }

                // ── Handle attachment upload if provided ──────────────────────────────
                // Runs in both branches — attachment handling is independent of approval status.
                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Leave request saved but attachment failed: {uploadResult.message}" });
                }

                var successMessage = isLevel4Approver
                    ? "Leave request filed and automatically approved."
                    : "Leave request added successfully!";

                string requestorName = _emailService.GetEmployeeNameAsync(model.employeeNo).ToString();

                string dateFrom = model.leaveDateFrom?.ToString("yyyy-MM-dd") + " " + model.timeIN?.ToString(@"hh\:mm\:ss");
                string dateTo = model.leaveDateTo?.ToString("yyyy-MM-dd") + " " + model.timeOUT?.ToString(@"hh\:mm\:ss");
                int? leastApproverLevel = await _emailService.GetLeastApproverLevelAsync(model.employeeNo);
                if (leastApproverLevel.HasValue)
                {
                    string approverEmail = await _emailService.GetApproverEmails(model.employeeNo, leastApproverLevel.Value);

                    if (!string.IsNullOrWhiteSpace(approverEmail))
                    {
                        _emailService.SendRequestEmailAsync("Leave Request", requestorName, approverEmail, dateFrom, dateTo);
                    }
                }

                return Json(new { success = true, message = successMessage });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddLeaveRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error adding leave request: {ex.Message}" });
            }
        }

        // Update leave request - Only validate, NO m_leave recording
        [HttpPost]
        public JsonResult UpdateLeaveRequest(LeaveRequestModel model, IFormFile attachment)
        {
            try
            {
                // Check if record exists and get employee info
                var existingRequest = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo, statusLevel4, leaveCode, leaveCountDays FROM rq_leave WHERE id = @id AND isActive = 1",
                    new { model.id });

                if (existingRequest == null)
                    return Json(new { success = false, message = "Leave request not found!" });

                // Security check using base method
                if (!CanViewEmployee(existingRequest.employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to update this leave request." });
                }

                string currentStatus = existingRequest.statusLevel4;

                // Only allow editing Pending or Declined requests
                if (currentStatus != "Pending" && currentStatus != "Declined")
                    return Json(new { success = false, message = "Only pending or declined requests can be edited!" });

                // Validate employee exists using base method
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Validate dates
                if (model.leaveDateTo < model.leaveDateFrom)
                    return Json(new { success = false, message = "End date cannot be earlier than start date!" });

                // VALIDATION: Check if employee has enough leave balance
                var existingBalance = _db.QueryFirstOrDefault<dynamic>(
                    @"SELECT availableBalance FROM m_leave 
                      WHERE employeeNo = @EmployeeNo AND leaveCode = @LeaveCode 
                      ORDER BY id DESC LIMIT 1",
                    new { EmployeeNo = model.employeeNo, LeaveCode = model.leaveCode });

                if (existingBalance != null && model.leaveCode != "LWOP" && model.leaveCode != "SUS")
                {
                    double availableBalance = Convert.ToDouble(existingBalance.availableBalance);
                    double requestedDays = Convert.ToDouble(model.leaveCountDays ?? 0);

                    if (requestedDays > availableBalance)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Insufficient leave balance. Available: {availableBalance:F2} days, Requested: {requestedDays:F2} days."
                        });
                    }
                }

                // ── Entitlement validation ────────────────────────────────────────────
                var (isEntitled, entitlementMessage) = ValidateLeaveEntitlement(
                    model.employeeNo, model.leaveCode, model.leaveDateFrom);

                if (!isEntitled)
                    return Json(new { success = false, message = entitlementMessage });

                // If status was Declined, set back to Pending when edited
                var newStatus = currentStatus == "Declined" ? "Pending" : currentStatus;

                var sql = @"
                    UPDATE rq_leave 
                    SET employeeNo = @employeeNo,
                        leaveCode = @leaveCode,
                        leaveDateFrom = @leaveDateFrom,
                        leaveDateTo = @leaveDateTo,
                        leaveCountDays = @leaveCountDays,
                        leaveCountHours = @leaveCountHours,
                        leaveReason = @leaveReason,
                        leaveType = @leaveType,
                        remarks = @remarks,
                        creditDeductionOnly = @creditDeductionOnly,
                        statusLevel1 = @statusLevel,
                        statusLevel2 = @statusLevel,
                        statusLevel3 = @statusLevel,
                        statusLevel4 = @statusLevel,
                        dtLastModified = NOW(),
                        lastModifiedByUser = @lastModifiedByUser,
                        dtStatusLevel1 = NOW(),
                        statusByLevel1 = @lastModifiedByUser,
                        dtStatusLevel2 = NOW(),
                        statusByLevel2 = @lastModifiedByUser,
                        dtStatusLevel3 = NOW(),
                        statusByLevel3 = @lastModifiedByUser,
                        dtStatusLevel4 = NOW(),
                        statusByLevel4 = @lastModifiedByUser
                    WHERE id = @id";

                _db.Execute(sql, new
                {
                    model.id,
                    model.employeeNo,
                    model.leaveCode,
                    model.leaveDateFrom,
                    model.leaveDateTo,
                    leaveCountDays = model.leaveCountDays ?? 0,
                    leaveCountHours = model.leaveCountHours ?? 0,
                    leaveReason = model.leaveReason ?? "",
                    leaveType = model.leaveType ?? "whole",
                    remarks = model.remarks ?? "",
                    model.creditDeductionOnly,
                    statusLevel = newStatus,
                    lastModifiedByUser = EmployeeNo ?? model.employeeNo
                });

                // ── Update m_leave balance on edit ───────────────────────────
                // Get the old leave days from the existing request
                double oldLeaveDays = Convert.ToDouble(existingRequest.leaveCountDays);
                double newLeaveDays = Convert.ToDouble(model.leaveCountDays ?? 0);
                double difference = oldLeaveDays - newLeaveDays; // positive = refund, negative = extra deduction

                // Only update if days actually changed
                if (difference != 0)
                {
                    var latestBalance = _db.QueryFirstOrDefault<dynamic>(
                        @"SELECT availableBalance FROM m_leave
                          WHERE employeeNo = @EmployeeNo AND leaveCode = @LeaveCode
                          ORDER BY id DESC LIMIT 1",
                        new { EmployeeNo = model.employeeNo, LeaveCode = model.leaveCode });

                    if (latestBalance != null)
                    {
                        double currentAvail = Convert.ToDouble(latestBalance.availableBalance);
                        double newAvailable = currentAvail + difference;

                        // Positive difference = employee reduced days = earn back credits (accrual)
                        // Negative difference = employee increased days = use more credits (usedCredits)
                        bool isRefund = difference > 0;

                        _db.Execute(@"
                            INSERT INTO m_leave (
                                employeeNo, rq_leaveID, leaveCode, statusName,
                                beginningBalance, accrual, usedCredits, availableBalance,
                                isActive, dtAdded, addedByUser
                            ) VALUES (
                                @EmployeeNo, @RqLeaveID, @LeaveCode, 'UPDATED LEAVE REQUEST',
                                @BeginningBalance, @Accrual, @UsedCredits, @AvailableBalance,
                                1, NOW(), @UserCode
                            )",
                            new
                            {
                                EmployeeNo = model.employeeNo,
                                RqLeaveID = model.id,
                                LeaveCode = model.leaveCode,
                                BeginningBalance = currentAvail,
                                Accrual = isRefund ? difference : 0,
                                UsedCredits = isRefund ? 0 : Math.Abs(difference),
                                AvailableBalance = newAvailable,
                                UserCode = EmployeeNo ?? model.employeeNo
                            });
                    }
                }

                // Log to audit trail
                _auditTrail.Log("rq_leave", model.id, "UPDATED",
                    $"Updated leave request for {model.employeeNo}: {model.leaveDateFrom:yyyy-MM-dd} to {model.leaveDateTo:yyyy-MM-dd}");

                // Handle attachment upload if provided
                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Leave request updated but attachment failed: {uploadResult.message}" });
                }

                var message = currentStatus == "Declined"
                    ? "Leave request updated successfully and status set back to Pending!"
                    : "Leave request updated successfully!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateLeaveRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error updating leave request: {ex.Message}" });
            }
        }

        // Approve leave request - Record to m_leave ONLY when approved
        [HttpPost]
        public async Task<JsonResult> ApproveLeaveRequest(int id, string approvedByUser)
        {
            try
            {
                // ── 1. Load the request ──────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, leaveCode, leaveCountDays,
                           statusLevel1, statusLevel2, statusLevel3, statusLevel4, leaveDateFrom, leaveDateTo, timeIN, timeOUT
                    FROM rq_leave
                    WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Leave Request not found!" });

                if (request.statusLevel4 != "Pending")
                    return Json(new
                    {
                        success = false,
                        message = "This request has already been finalised and cannot be approved again."
                    });

                string employeeNo = (string)request.employeeNo;
                string actingUser = approvedByUser ?? EmployeeNo;
                bool isFull = HasFullAccess();

                // ── 2. Determine acting approver level ───────────────────────────
                int? approverLevel = null;

                if (!isFull)
                {
                    approverLevel = await _approverService
                        .GetApproverLevelForEmployeeAsync(EmployeeNo, employeeNo);

                    if (approverLevel == null)
                        return Json(new
                        {
                            success = false,
                            message = "Access denied. You are not an authorised approver for this employee."
                        });
                }

                // ── 3. Get required level chain ──────────────────────────────────
                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();
                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                // ── 4. Build current approved levels from DB ─────────────────────
                var approvedLevels = new List<int>();
                if ((string)request.statusLevel1 == "Approved" && requiredLevels.Contains(1))
                    approvedLevels.Add(1);
                if ((string)request.statusLevel2 == "Approved" && requiredLevels.Contains(2))
                    approvedLevels.Add(2);
                if ((string)request.statusLevel3 == "Approved" && requiredLevels.Contains(3))
                    approvedLevels.Add(3);

                // ── 5. Resolve acting level ──────────────────────────────────────
                int actingLevel;

                if (isFull)
                {
                    actingLevel = requiredLevels
                        .Where(l => !approvedLevels.Contains(l))
                        .OrderBy(l => l)
                        .FirstOrDefault();

                    if (actingLevel == 0)
                        return Json(new
                        {
                            success = false,
                            message = "All approval levels have already been satisfied."
                        });
                }
                else
                {
                    actingLevel = approverLevel!.Value;
                }

                // ── 6. Sequential guard — SKIPPED for Level 4 (Level 4 can bypass) ─
                // Level 4 has the authority to approve regardless of whether
                // Level 2 and/or Level 3 have acted yet.
                if (actingLevel != 4)
                {
                    var lowerPending = requiredLevels
                        .Where(l => l < actingLevel && !approvedLevels.Contains(l))
                        .OrderBy(l => l)
                        .ToList();

                    if (lowerPending.Any())
                        return Json(new
                        {
                            success = false,
                            message = $"This request must first be approved by Level {lowerPending.First()} " +
                                      $"before Level {actingLevel} can act."
                        });
                }

                // ── 7. Guard: already approved at this level? ────────────────────
                if (approvedLevels.Contains(actingLevel))
                    return Json(new
                    {
                        success = false,
                        message = $"Level {actingLevel} has already approved this request."
                    });

                // ── 8. Determine new overall state ───────────────────────────────
                // When Level 4 bypasses, ALL required levels are treated as approved.
                var newlyApproved = actingLevel == 4
                    ? new List<int>(requiredLevels)
                    : new List<int>(approvedLevels) { actingLevel };

                int highestRequired = requiredLevels.Max();
                bool isFullyApproved = requiredLevels.All(l => newlyApproved.Contains(l));

                // ── 9. Build UPDATE ──────────────────────────────────────────────
                var setParts = new List<string>();

                // Level 4 bypass: auto-approve any pending lower required levels
                if (actingLevel == 4)
                {
                    if (requiredLevels.Contains(1) && (string)request.statusLevel1 != "Approved")
                    {
                        setParts.Add("statusLevel1   = 'Approved'");
                        setParts.Add("dtStatusLevel1 = NOW()");
                        setParts.Add("statusByLevel1 = @actingUser");
                    }
                    if (requiredLevels.Contains(2) && (string)request.statusLevel2 != "Approved")
                    {
                        setParts.Add("statusLevel2   = 'Approved'");
                        setParts.Add("dtStatusLevel2 = NOW()");
                        setParts.Add("statusByLevel2 = @actingUser");
                    }
                    if (requiredLevels.Contains(3) && (string)request.statusLevel3 != "Approved")
                    {
                        setParts.Add("statusLevel3   = 'Approved'");
                        setParts.Add("dtStatusLevel3 = NOW()");
                        setParts.Add("statusByLevel3 = @actingUser");
                    }
                }

                switch (actingLevel)
                {
                    case 1:
                        setParts.Add("statusLevel1   = 'Approved'");
                        setParts.Add("dtStatusLevel1 = NOW()");
                        setParts.Add("statusByLevel1 = @actingUser");
                        break;
                    case 2:
                        setParts.Add("statusLevel2   = 'Approved'");
                        setParts.Add("dtStatusLevel2 = NOW()");
                        setParts.Add("statusByLevel2 = @actingUser");
                        break;
                    case 3:
                        setParts.Add("statusLevel3   = 'Approved'");
                        setParts.Add("dtStatusLevel3 = NOW()");
                        setParts.Add("statusByLevel3 = @actingUser");
                        break;
                    case 4:
                        setParts.Add("statusLevel4   = 'Approved'");
                        setParts.Add("dtStatusLevel4 = NOW()");
                        setParts.Add("statusByLevel4 = @actingUser");
                        break;
                }

                // Cascade to final gate when fully approved via a lower level
                if (isFullyApproved && highestRequired < 4 && actingLevel != 4)
                {
                    setParts.Add("statusLevel4   = 'Approved'");
                    setParts.Add("dtStatusLevel4 = NOW()");
                    setParts.Add("statusByLevel4 = @actingUser");
                }

                setParts.Add("dtStatus           = NOW()");
                setParts.Add("statusByUser       = @actingUser");
                setParts.Add("dtLastModified     = NOW()");
                setParts.Add("lastModifiedByUser = @actingUser");

                var updateSql =
                    $"UPDATE rq_leave SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // ── 10. Notify ───────────────────────────────────────────────────
                if (isFullyApproved)
                {
                    NotifyRequestAction("leave", id, employeeNo, "approved");
                }
                else
                {
                    int nextLevel = requiredLevels
                        .Where(l => !newlyApproved.Contains(l))
                        .OrderBy(l => l)
                        .First();

                    NotifyNextApprover("leave", id, employeeNo, nextLevel);
                }

                // ── 11. Audit ────────────────────────────────────────────────────
                var auditMsg = isFullyApproved
                    ? $"Leave request fully approved at Level {actingLevel} by {actingUser}"
                    : $"Leave request partially approved at Level {actingLevel} by {actingUser}. " +
                      $"Awaiting higher level approval.";

                _auditTrail.Log("rq_leave", id, "APPROVED", auditMsg);

                var successMessage = isFullyApproved
                    ? "Leave Request approved successfully!"
                    : $"Level {actingLevel} approval recorded. Request is now pending the next approver.";

                string employeeEmail = _emailService.GetEmployeeEmail(request.employeeNo).ToString();
                string dateFrom = request.leaveDateFrom?.ToString("yyyy-MM-dd") + " " + request.timeIN?.ToString(@"hh\:mm\:ss");
                string dateTo = request.leaveDateTo?.ToString("yyyy-MM-dd") + " " + request.timeOUT?.ToString(@"hh\:mm\:ss");

                _emailService.SendRequestStatusEmailAsync("Leave Request Status", employeeEmail, request.statusLevel1, request.statusLevel2,
                request.statusLevel3, request.statusLevel4, dateFrom, dateTo);

                return Json(new
                {
                    success = true,
                    message = successMessage,
                    isFullyApproved,
                    actingLevel,
                    nextLevel = isFullyApproved
                        ? (int?)null
                        : requiredLevels
                            .Where(l => !newlyApproved.Contains(l))
                            .OrderBy(l => l)
                            .Cast<int?>()
                            .FirstOrDefault()
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ApproveLeaveRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while approving the request." });
            }
        }

        // Decline leave request - NO ledger entry needed (never recorded in pending state)
        [HttpPost]
        public async Task<JsonResult> DeclineLeaveRequest(int id, string declinedByUser, string reason = "")
        {
            try
            {
                // ── 1. Load request ──────────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, leaveCode, leaveCountDays,
                           statusLevel1, statusLevel2, statusLevel3, statusLevel4
                    FROM rq_leave
                    WHERE id = @id AND isActive = 1",
                            new { id });

                if (request == null)
                    return Json(new { success = false, message = "Leave Request not found!" });

                if (request.statusLevel4 == "Cancelled" || request.statusLevel4 == "Processed")
                    return Json(new { success = false, message = "Cancelled or processed requests cannot be declined!" });

                string employeeNo = (string)request.employeeNo;
                string actingUser = declinedByUser ?? EmployeeNo;
                bool isFull = HasFullAccess();

                // ── 2. Determine acting approver level ───────────────────────────
                int? approverLevel = null;

                if (!isFull)
                {
                    approverLevel = await _approverService
                        .GetApproverLevelForEmployeeAsync(EmployeeNo, employeeNo);

                    if (approverLevel == null)
                        return Json(new
                        {
                            success = false,
                            message = "Access denied. You are not an authorised approver for this employee."
                        });
                }

                // ── 3. Get required levels ───────────────────────────────────────
                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();
                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                // ── 4. FULL access: find next pending level ──────────────────────
                if (isFull)
                {
                    string sl1 = (string)request.statusLevel1 ?? "Pending";
                    string sl2 = (string)request.statusLevel2;
                    string sl3 = (string)request.statusLevel3;

                    approverLevel = requiredLevels.FirstOrDefault(l => l switch
                    {
                        1 => sl1 != "Approved",
                        2 => sl2 != "Approved",
                        3 => sl3 != "Approved",
                        _ => true
                    });

                    if (approverLevel == 0) approverLevel = requiredLevels.Max();
                }

                int actingLevel = approverLevel!.Value;

                // ── 5. Sequential guard — SKIPPED for Level 4 (Level 4 can bypass) ─
                // Level 4 has the authority to decline regardless of whether
                // Level 2 and/or Level 3 have acted yet.
                if (actingLevel != 4)
                {
                    string statusLevel1 = (string)request.statusLevel1 ?? "Pending";
                    string statusLevel2 = (string)request.statusLevel2;
                    string statusLevel3 = (string)request.statusLevel3;

                    var lowerPending = requiredLevels
                        .Where(l => l < actingLevel)
                        .Where(l => l switch
                        {
                            1 => statusLevel1 != "Approved",
                            2 => statusLevel2 != "Approved",
                            3 => statusLevel3 != "Approved",
                            _ => false
                        })
                        .OrderBy(l => l)
                        .ToList();

                    if (lowerPending.Any())
                        return Json(new
                        {
                            success = false,
                            message = $"This request must first be approved by Level {lowerPending.First()} " +
                                      $"before Level {actingLevel} can act."
                        });
                }

                // ── 6. Build UPDATE ──────────────────────────────────────────────
                var setParts = new List<string>();

                if (actingLevel == 4)
                {
                    // Level 4 bypass: auto-decline any pending lower required levels
                    if (requiredLevels.Contains(1) && (string)request.statusLevel1 != "Approved")
                    {
                        setParts.Add("statusLevel1   = 'Declined'");
                        setParts.Add("dtStatusLevel1 = NOW()");
                        setParts.Add("statusByLevel1 = @actingUser");
                    }
                    if (requiredLevels.Contains(2) && (string)request.statusLevel2 != "Approved")
                    {
                        setParts.Add("statusLevel2   = 'Declined'");
                        setParts.Add("dtStatusLevel2 = NOW()");
                        setParts.Add("statusByLevel2 = @actingUser");
                    }
                    if (requiredLevels.Contains(3) && (string)request.statusLevel3 != "Approved")
                    {
                        setParts.Add("statusLevel3   = 'Declined'");
                        setParts.Add("dtStatusLevel3 = NOW()");
                        setParts.Add("statusByLevel3 = @actingUser");
                    }
                    // statusLevel4 is set below as the final gate
                }
                else
                {
                    switch (actingLevel)
                    {
                        case 1:
                            setParts.Add("statusLevel1   = 'Declined'");
                            setParts.Add("dtStatusLevel1 = NOW()");
                            setParts.Add("statusByLevel1 = @actingUser");
                            if (requiredLevels.Contains(2))
                            {
                                setParts.Add("statusLevel2   = 'Declined'");
                                setParts.Add("dtStatusLevel2 = NOW()");
                                setParts.Add("statusByLevel2 = @actingUser");
                            }
                            if (requiredLevels.Contains(3))
                            {
                                setParts.Add("statusLevel3   = 'Declined'");
                                setParts.Add("dtStatusLevel3 = NOW()");
                                setParts.Add("statusByLevel3 = @actingUser");
                            }
                            break;

                        case 2:
                            setParts.Add("statusLevel2   = 'Declined'");
                            setParts.Add("dtStatusLevel2 = NOW()");
                            setParts.Add("statusByLevel2 = @actingUser");
                            if (requiredLevels.Contains(3))
                            {
                                setParts.Add("statusLevel3   = 'Declined'");
                                setParts.Add("dtStatusLevel3 = NOW()");
                                setParts.Add("statusByLevel3 = @actingUser");
                            }
                            break;

                        case 3:
                            setParts.Add("statusLevel3   = 'Declined'");
                            setParts.Add("dtStatusLevel3 = NOW()");
                            setParts.Add("statusByLevel3 = @actingUser");
                            break;
                    }
                }

                // Final gate always reflects the decline
                setParts.Add("statusLevel4   = 'Declined'");
                setParts.Add("dtStatusLevel4 = NOW()");
                setParts.Add("statusByLevel4 = @actingUser");
                if (!string.IsNullOrWhiteSpace(reason))
                    setParts.Add("remarks = @reason");
                setParts.Add("dtStatus           = NOW()");
                setParts.Add("statusByUser       = @actingUser");
                setParts.Add("dtLastModified     = NOW()");
                setParts.Add("lastModifiedByUser = @actingUser");

                var updateSql =
                    $"UPDATE rq_leave SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser, reason = reason ?? "" });

                // ── 7. Reverse leave credit deduction ───────────────────────────
                var leaveInfo = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT leaveCode, leaveCountDays FROM rq_leave WHERE id = @id",
                    new { id });

                if (leaveInfo != null)
                {
                    double leaveDays = Convert.ToDouble(leaveInfo.leaveCountDays);
                    string leaveCode = (string)leaveInfo.leaveCode;

                    var latestBalance = _db.QueryFirstOrDefault<dynamic>(
                        @"SELECT availableBalance, usedCredits FROM m_leave
                              WHERE employeeNo = @employeeNo AND leaveCode = @leaveCode
                              ORDER BY id DESC LIMIT 1",
                        new { employeeNo, leaveCode });

                    var noCreditLeaveCodes = new[] { "LWOP", "SUS", "ML", "PL" };
                    bool isNoCreditLeave = noCreditLeaveCodes.Contains(leaveCode);

                    if (isNoCreditLeave)
                    {
                        // Record decline with all zeros — just for tracking
                        _db.Execute(@"
                            INSERT INTO m_leave (
                                employeeNo, rq_leaveID, leaveCode, statusName,
                                beginningBalance, accrual, usedCredits, availableBalance,
                                isActive, dtAdded, addedByUser
                            ) VALUES (
                                @EmployeeNo, @RqLeaveID, @LeaveCode, 'DECLINED LEAVE REQUEST',
                                0, 0, 0, 0,
                                1, NOW(), @UserCode
                            )",
                            new
                            {
                                EmployeeNo = employeeNo,
                                RqLeaveID = id,
                                LeaveCode = leaveCode,
                                UserCode = actingUser
                            });
                    }
                    else if (latestBalance != null && leaveDays > 0)
                    {
                        double currentAvail = Convert.ToDouble(latestBalance.availableBalance);
                        double currentUsed = Convert.ToDouble(latestBalance.usedCredits);

                        _db.Execute(@"
                        INSERT INTO m_leave (
                            employeeNo, rq_leaveID, leaveCode, statusName,
                            beginningBalance, accrual, usedCredits, availableBalance,
                            isActive, dtAdded, addedByUser
                        ) VALUES (
                            @EmployeeNo, @RqLeaveID, @LeaveCode, 'DECLINED LEAVE REQUEST',
                            @BeginningBalance, @Accrual, 0, @AvailableBalance,
                            1, NOW(), @UserCode
                        )",
                        new
                        {
                            EmployeeNo = employeeNo,
                            RqLeaveID = id,
                            LeaveCode = leaveCode,
                            BeginningBalance = currentAvail,
                            Accrual = leaveDays,
                            AvailableBalance = currentAvail + leaveDays,
                            UserCode = actingUser
                        });
                    }
                }

                // ── 8. Notify ────────────────────────────────────────────────────
                NotifyRequestAction("leave", id, employeeNo, "declined");

                // ── 9. Audit ─────────────────────────────────────────────────────
                _auditTrail.Log("rq_leave", id, "DECLINED",
                    $"Declined leave request at Level {actingLevel} by {actingUser}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "Leave Request declined successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeclineLeaveRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while declining the request." });
            }
        }


        // Cancel leave request - NO ledger entry needed (never recorded in pending state)
        [HttpPost]
        public async Task<JsonResult> CancelLeaveRequest(int id, string cancelledByUser, string reason = "")
        {
            try
            {
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT employeeNo, leaveCode, leaveCountDays, statusLevel4 FROM rq_leave WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Leave request not found!" });

                // Only allow user to cancel their own request
                if (request.employeeNo != EmployeeNo && !HasFullAccess())
                    return Json(new { success = false, message = "Access denied. You can only cancel your own requests." });

                if (request.statusLevel4 == "Approved")
                    return Json(new { success = false, message = "Approved requests cannot be cancelled!" });

                if (request.statusLevel4 == "Cancelled")
                    return Json(new { success = false, message = "Request is already cancelled!" });

                // Prevent cancelling an already declined request (credits already restored on decline)
                if (request.statusLevel4 == "Declined")
                    return Json(new { success = false, message = "Declined requests cannot be cancelled. Credits have already been restored." });

                var sql = @"
                    UPDATE rq_leave 
                    SET statusLevel1 = 'Cancelled',
                        statusLevel2 = 'Cancelled',
                        statusLevel3 = 'Cancelled',
                        statusLevel4 = 'Cancelled',
                        dtStatusLevel1 = NOW(),
                        statusByLevel1 = @cancelledByUser,
                        dtStatusLevel2 = NOW(),
                        statusByLevel2 = @cancelledByUser,
                        dtStatusLevel3 = NOW(),
                        statusByLevel3 = @cancelledByUser,
                        dtStatusLevel4 = NOW(),
                        statusByLevel4 = @cancelledByUser
                    WHERE id = @id";

                await _db.ExecuteAsync(sql, new { id, cancelledByUser = cancelledByUser ?? EmployeeNo });

                // Reverse the credit deduction that was applied on submission
                var latestBalance = _db.QueryFirstOrDefault<dynamic>(
                                @"SELECT availableBalance, usedCredits FROM m_leave
                  WHERE employeeNo = @employeeNo AND leaveCode = @leaveCode
                  ORDER BY id DESC LIMIT 1",
                    new { employeeNo = (string)request.employeeNo, leaveCode = (string)request.leaveCode });

                var noCreditLeaveCodes = new[] { "LWOP", "SUS", "ML", "PL" };
                bool isNoCreditLeave = noCreditLeaveCodes.Contains((string)request.leaveCode);

                if (isNoCreditLeave)
                {
                    // Record cancel with all zeros — just for tracking
                    _db.Execute(@"
                        INSERT INTO m_leave (
                            employeeNo, rq_leaveID, leaveCode, statusName,
                            beginningBalance, accrual, usedCredits, availableBalance,
                            isActive, dtAdded, addedByUser
                        ) VALUES (
                            @EmployeeNo, @RqLeaveID, @LeaveCode, 'CANCELLED LEAVE REQUEST',
                            0, 0, 0, 0,
                            1, NOW(), @UserCode
                        )",
                        new
                        {
                            EmployeeNo = (string)request.employeeNo,
                            RqLeaveID = id,
                            LeaveCode = (string)request.leaveCode,
                            UserCode = cancelledByUser ?? EmployeeNo
                        });
                }
                else if (latestBalance != null)
                {
                    double leaveDays = Convert.ToDouble(request.leaveCountDays);
                    double currentAvail = Convert.ToDouble(latestBalance.availableBalance);
                    double currentUsed = Convert.ToDouble(latestBalance.usedCredits);
                    double newAvailable = currentAvail + leaveDays;
                    //double newUsedCredits = currentUsed - leaveDays;
                    double newUsedCredits = leaveDays;

                    _db.Execute(@"
                    INSERT INTO m_leave (
                        employeeNo, rq_leaveID, leaveCode, statusName,
                        beginningBalance, accrual, usedCredits, availableBalance,
                        isActive, dtAdded, addedByUser
                    ) VALUES (
                        @EmployeeNo, @RqLeaveID, @LeaveCode, 'CANCELLED LEAVE REQUEST',
                        @BeginningBalance, @Accrual, 0, @AvailableBalance,
                        1, NOW(), @UserCode
                    )",
                    new
                    {
                        EmployeeNo = (string)request.employeeNo,
                        RqLeaveID = id,
                        LeaveCode = (string)request.leaveCode,
                        BeginningBalance = currentAvail,
                        Accrual = leaveDays,
                        AvailableBalance = newAvailable,
                        UserCode = cancelledByUser ?? EmployeeNo
                    });
                }

                NotifyRequestAction("leave", id, request.employeeNo, "cancelled");

                // NOTE: NO m_leave entry needed - credits were never deducted in pending state

                _auditTrail.Log("rq_leave", id, "CANCELLED",
                    $"Cancelled leave request by {cancelledByUser ?? EmployeeNo}{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Leave request cancelled successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CancelLeaveRequest: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Get leave types list (no security check needed - static data)
        [HttpGet]
        public JsonResult GetLeaveTypesList()
        {
            try
            {
                var sql = @"
                    SELECT 
                        leaveCode, 
                        leaveName,
                        leaveCredits
                    FROM s_leave 
                    WHERE isActive = 1 
                        AND dtDeleted IS NULL
                        AND leaveCode != 'CTO'
                    ORDER BY leaveName";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveTypesList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        // Get leave attachments WITH SECURITY CHECK
        [HttpGet]
        public JsonResult GetLeaveAttachments(string employeeNo)
        {
            try
            {
                // Security check using base method
                if (!CanViewEmployee(employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to view this employee's attachments." });
                }

                var sql = @"
                    SELECT id, attachmentPath, dtAdded 
                    FROM e_attachment 
                    WHERE employeeNo = @employeeNo 
                    AND attachmentTypeCode = 'LEAVE' 
                    AND isActive = 1
                    ORDER BY dtAdded DESC";

                return Json(_db.Query(sql, new { employeeNo }).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLeaveAttachments: {ex.Message}");
                return Json(new List<object>());
            }
        }

        // Helper method for validating leave entitlement date
        private (bool isValid, string message) ValidateLeaveEntitlement(
            string employeeNo, string leaveCode, DateTime? leaveDateFrom)
        {
            var leaveSetup = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT
                    el.dateEntitled,
                    el.dateFrom,
                    sl.leaveName
                FROM e_leave el
                LEFT JOIN s_leave sl ON sl.leaveCode = el.leaveCode
                WHERE el.employeeNo = @employeeNo
                  AND el.leaveCode  = @leaveCode
                  AND el.isActive   = 1
                  AND (el.dtDeleted IS NULL OR el.dtDeleted = '0000-00-00 00:00:00')
                LIMIT 1",
                new { employeeNo, leaveCode });

            // Block if no setup record exists for this employee + leave type
            if (leaveSetup == null)
            {
                if (leaveCode == "LWOP" || leaveCode == "SUS")
                    return (true, string.Empty);

                return (false, "No leave setup found for this leave type. Please contact HR.");
            }

            string leaveName = (string)leaveSetup.leaveName ?? leaveCode;
            DateTime today = DateTime.Today;
            bool isMatPat = leaveName.ToLower().Contains("maternity") ||
                               leaveName.ToLower().Contains("paternity");

            if (isMatPat)
            {
                // Maternity/Paternity uses dateFrom instead of dateEntitled
                if (leaveSetup.dateFrom == null)
                    return (false, $"No start date configured for {leaveName}. Please contact HR.");

                DateTime dateFrom = Convert.ToDateTime(leaveSetup.dateFrom);

                if (today < dateFrom)
                    return (false,
                        $"You are not yet entitled to {leaveName}. " +
                        $"Entitlement starts on {dateFrom:MMMM dd, yyyy}.");

                if (leaveDateFrom.HasValue && leaveDateFrom.Value.Date < dateFrom)
                    return (false,
                        $"Leave date cannot be before the {leaveName} start date " +
                        $"of {dateFrom:MMMM dd, yyyy}.");
            }
            else
            {
                // Standard leave uses dateEntitled
                if (leaveSetup.dateEntitled == null)
                    return (false, $"No entitlement date configured for {leaveName}. Please contact HR.");

                DateTime dateEntitled = Convert.ToDateTime(leaveSetup.dateEntitled);

                if (today < dateEntitled)
                    return (false,
                        $"You are not yet entitled to {leaveName}. " +
                        $"Entitlement date is {dateEntitled:MMMM dd, yyyy}.");

                if (leaveDateFrom.HasValue && leaveDateFrom.Value.Date < dateEntitled)
                    return (false,
                        $"Leave date cannot be before your entitlement date " +
                        $"of {dateEntitled:MMMM dd, yyyy} for {leaveName}.");
            }

            return (true, string.Empty);
        }

        // Helper method for saving attachments
        private (bool success, string message) SaveAttachment(string employeeNo, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return (false, "No file provided");

                // Validate file size (5MB limit)
                if (file.Length > 5 * 1024 * 1024)
                    return (false, "File size exceeds 5MB limit");

                // Validate file extension
                var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" };
                var fileExtension = Path.GetExtension(file.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                    return (false, "Invalid file format. Allowed: PDF, JPG, PNG, DOC, DOCX");

                // Create uploads directory if it doesn't exist
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "leave");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                // Generate unique filename
                var fileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                var filePath = Path.Combine(uploadsFolder, fileName);

                // Save file to disk
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                // Save attachment record to database
                var sql = @"
                    INSERT INTO e_attachment (employeeNo, attachmentDescription, attachmentTypeCode, attachmentPath, isActive, dtAdded) 
                    VALUES (@employeeNo, 'Leave Request', 'LEAVE', @attachmentPath, 1, NOW())";

                _db.Execute(sql, new
                {
                    employeeNo,
                    attachmentPath = $"/uploads/leave/{fileName}"
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