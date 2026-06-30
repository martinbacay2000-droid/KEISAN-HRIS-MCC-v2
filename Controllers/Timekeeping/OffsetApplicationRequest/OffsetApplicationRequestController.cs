using Dapper;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.OffsetApplicationRequest
{
    [ModuleAuthorize("RoffsetApplicationM")]
    public class OffsetApplicationRequestController : TimekeepingRequestBaseController
    {
        public OffsetApplicationRequestController(
        IDbConnection db,
        IAuditTrailService auditTrail,
        IEmailService emailService,
        IApproverService approverService)
        : base(db, auditTrail, "RoffsetApplicationM")
        {
            _approverService = approverService;
            _emailService = (IEmailService)emailService;
        }

        public IActionResult Index()
        {
            return View("~/Views/Timekeeping/OffsetApplicationRequest.cshtml");
        }

        [HttpGet]
        public async Task<JsonResult> GetOffsetApplicationRequestList(string status, string branch, string department, string dateFrom, string dateTo)
        {
            try
            {
                var approverInfo = await GetApproverInfoCachedAsync();
                var hasFullAccess = HasFullAccess();
                var hasBroadScope = HasBroadDataScope();

                var query = new StringBuilder();
                var parameters = new DynamicParameters();

                const string selectBlock = @"
                    SELECT 
                        rq.id,
                        rq.employeeNo,
                        CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,
                        rq.leaveCode,
                        s.leaveName,
                        DATE_FORMAT(rq.leaveDateFrom, '%m/%d/%Y') AS displayDateFrom,
                        DATE_FORMAT(rq.leaveDateTo, '%m/%d/%Y') AS displayDateTo,
                        rq.leaveCountDays,
                        rq.leaveType,
                        rq.leaveReason,
                        rq.dtAdded AS dateRequested,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusLevel4 AS statusName,
                        CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                    FROM rq_leave rq
                    INNER JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                    LEFT JOIN s_leave s ON rq.leaveCode = s.leaveCode
                    LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser";

                if (hasFullAccess || hasBroadScope)
                {
                    query.Append(selectBlock);
                    query.Append(" WHERE rq.isActive = 1 AND rq.leaveCode = 'CTO'");
                    ApplyDataScopeFilter(query, parameters);
                }
                else if (approverInfo.IsApprover)
                {
                    query.Append(selectBlock);
                    query.Append(@"
                        WHERE rq.isActive = 1
                        AND rq.leaveCode = 'CTO'
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
                    query.Append(selectBlock);
                    query.Append(@"
                        WHERE rq.isActive = 1
                        AND rq.leaveCode = 'CTO'
                        AND rq.employeeNo = @currentEmployeeNo");
                    parameters.Add("@currentEmployeeNo", EmployeeNo);
                }

                var hiddenEmployees = _db.QueryFirstOrDefault<string>(@"
                    SELECT hiddenEmployees 
                    FROM s_role 
                    WHERE roleCode = @roleCode AND isActive = 1
                    LIMIT 1", new { roleCode = RoleCode });

                if (!string.IsNullOrWhiteSpace(hiddenEmployees))
                {
                    var hiddenList = hiddenEmployees.Split(',').Select(x => x.Trim()).ToArray();
                    query.Append(" AND (b.employeeNo NOT IN @hiddenEmployees OR b.employeeNo = @currentEmployeeNoHidden)");
                    parameters.Add("@hiddenEmployees", hiddenList);
                    parameters.Add("@currentEmployeeNoHidden", EmployeeNo);
                }

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

                if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND b.branchCode = @branch");
                    parameters.Add("@branch", branch);
                }

                if (!string.IsNullOrWhiteSpace(department) && !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    query.Append(" AND b.departmentCode = @department");
                    parameters.Add("@department", department);
                }

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
                Console.WriteLine($"Error in GetOffsetApplicationRequestList: {ex.Message}");
                return Json(new { data = new List<object>() });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetOffsetApplicationRequest(int id)
        {
            try
            {
                var employeeNo = _db.QueryFirstOrDefault<string>(
                    "SELECT employeeNo FROM rq_leave WHERE id = @id AND isActive = 1 AND leaveCode = 'CTO'",
                    new { id });

                if (string.IsNullOrEmpty(employeeNo))
                    return Json(new { error = "Offset application request not found!" });

                if (!CanViewEmployee(employeeNo))
                    return Json(new { error = "Access denied. You don't have permission to view this employee's offset application request." });

                var query = @"
                    SELECT
                        rq.id,
                        rq.employeeNo,
                        rq.leaveCode,
                        DATE_FORMAT(rq.leaveDateFrom, '%m/%d/%Y') AS displayDateFrom,
                        DATE_FORMAT(rq.leaveDateTo,   '%m/%d/%Y') AS displayDateTo,
                        rq.leaveCountDays,
                        rq.leaveType,
                        rq.leaveReason,
                        rq.remarks,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusLevel4 AS statusName,
                        CONCAT(b.lastName, ', ', b.firstName, ' ',
                               LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName
                    FROM rq_leave rq
                    LEFT JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                    WHERE rq.id = @id AND rq.isActive = 1 AND rq.leaveCode = 'CTO'";

                var request = _db.QueryFirstOrDefault<dynamic>(query, new { id });

                if (request == null) return Json(null);

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
                    displayDateFrom = (string)request.displayDateFrom,
                    displayDateTo = (string)request.displayDateTo,
                    leaveCountDays = (object)request.leaveCountDays,
                    leaveType = (string)request.leaveType,
                    leaveReason = (string)request.leaveReason,
                    remarks = (string)request.remarks,
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
                Console.WriteLine($"Error in GetOffsetApplicationRequest: {ex.Message}");
                return Json(null);
            }
        }

        [HttpGet]
        public JsonResult CalculateLeaveDays(DateTime dateFrom, DateTime dateTo, string leaveType)
        {
            try
            {
                int totalDays = (int)(dateTo - dateFrom).TotalDays + 1;
                if (totalDays < 0) totalDays = 0;

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

        [HttpGet]
        public JsonResult GetEmployeeCTOTypes(string employeeNo)
        {
            try
            {
                if (!CanViewEmployee(employeeNo))
                    return Json(new { error = "Access denied." });

                var query = @"
                    SELECT 
                        'CTO'  AS leaveCode,
                        s.leaveName,
                        0      AS leaveDays,
                        COALESCE(
                            (SELECT availableBalance
                             FROM   m_leave
                             WHERE  employeeNo = @employeeNo
                               AND  leaveCode  = 'CTO'
                               AND  isActive   = 1
                             ORDER  BY id DESC
                             LIMIT  1), 0
                        ) AS remainingLeaveDays,
                        COALESCE(
                            (SELECT availableBalance
                             FROM   m_leave
                             WHERE  employeeNo = @employeeNo
                               AND  leaveCode  = 'CTO'
                               AND  isActive   = 1
                             ORDER  BY id DESC
                             LIMIT  1), 0
                        ) AS availableBalance
                    FROM s_leave s
                    WHERE s.leaveCode = 'CTO'
                    LIMIT 1";

                var ctoList = _db.Query<dynamic>(query, new { employeeNo }).ToList();
                return Json(new { data = ctoList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeCTOTypes: {ex.Message}");
                return Json(new { data = new List<object>() });
            }
        }

        [HttpPost]
        public JsonResult AddOffsetApplicationRequest(OffsetApplicationRequestModel model, IFormFile attachment)
        {
            try
            {
                if (!CanViewEmployee(model.employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to create offset application requests for this employee." });
                }

                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                if (model.leaveDateFrom == null || model.leaveDateTo == null)
                    return Json(new { success = false, message = "Date fields are required!" });

                if (model.leaveDateTo < model.leaveDateFrom)
                    return Json(new { success = false, message = "End date must be after start date!" });

                if (string.IsNullOrWhiteSpace(model.leaveReason))
                    return Json(new { success = false, message = "Reason is required!" });

                // Always use CTO for offset applications
                model.leaveCode = "CTO";

                // Duplicate filing check — block overlapping CTO/offset application requests
                var duplicate = _db.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) FROM rq_leave
                    WHERE employeeNo = @employeeNo
                    AND leaveCode = 'CTO'
                    AND isActive = 1
                    AND statusLevel4 NOT IN ('Cancelled', 'Declined')
                    AND leaveDateFrom <= @leaveDateTo
                    AND leaveDateTo   >= @leaveDateFrom",
                    new { model.employeeNo, model.leaveDateFrom, model.leaveDateTo });

                if (duplicate > 0)
                    return Json(new { success = false, message = "An offset application request already exists for the selected date range." });

                double requestedDays = model.leaveCountDays ?? 0;

                // ── Balance validation — runs for ALL employees including Level 4 ─────
                var availableCredits = _db.QueryFirstOrDefault<double?>(
                        @"SELECT COALESCE(
                    (SELECT availableBalance
                     FROM   m_leave
                     WHERE  employeeNo = @EmployeeNo
                       AND  leaveCode  = 'CTO'
                       AND  isActive   = 1
                     ORDER  BY id DESC
                     LIMIT  1), 0)",
                    new { EmployeeNo = model.employeeNo });

                double availableBalance = availableCredits ?? 0;

                if (requestedDays > availableBalance)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Insufficient CTO balance. Available: {availableBalance:F2} days, Requested: {requestedDays:F2} days."
                    });
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

                // ── Insert new Offset Application Request ─────────────────────────────
                // Initial status is always Pending on insert; we update it right after
                // for Level 4 approvers so the row is never left in a dangling state.
                var sql = @"
                    INSERT INTO rq_leave 
                    (employeeNo, leaveCode, leaveDateFrom, leaveDateTo, leaveCountDays, leaveCountHours,
                     leaveType, leaveReason, remarks, creditDeductionOnly,
                     statusLevel1, statusLevel2, statusLevel3, statusLevel4, 
                     isActive, dtAdded, addedByUser, requestedByUser,
                     dtStatus, statusByUser, dtStatusLevel1, statusByLevel1,
                     dtStatusLevel2, statusByLevel2, 
                     dtStatusLevel3, statusByLevel3, dtStatusLevel4, statusByLevel4) 
                    VALUES 
                    (@employeeNo, @leaveCode, @leaveDateFrom, @leaveDateTo, @leaveCountDays, @leaveCountHours,
                     @leaveType, @leaveReason, @remarks, @creditDeductionOnly,
                     'Pending', 'Pending', 'Pending', 'Pending', 
                     1, NOW(), @addedByUser, @requestedByUser,
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
                    leaveType = model.leaveType ?? "whole",
                    leaveReason = model.leaveReason ?? "",
                    remarks = model.remarks ?? "",
                    creditDeductionOnly = false,
                    addedByUser = EmployeeNo,
                    requestedByUser = EmployeeNo
                });

                // ── Record to m_leave immediately on submission ───────────────────────
                // This runs for ALL employees regardless of Level 4 status.
                // CTO credits are deducted on submission (existing behaviour — unchanged).
                var existingBalance = _db.QueryFirstOrDefault<dynamic>(
                        @"SELECT availableBalance, accrual, usedCredits FROM m_leave 
                  WHERE employeeNo = @EmployeeNo AND leaveCode = 'CTO' 
                  ORDER BY id DESC LIMIT 1",
                    new { EmployeeNo = model.employeeNo });

                if (existingBalance != null && requestedDays > 0)
                {
                    double currentAvailable = Convert.ToDouble(existingBalance.availableBalance);
                    double newAvailable = currentAvailable - requestedDays;

                    _db.Execute(@"
                        INSERT INTO m_leave (
                            employeeNo, rq_leaveID, leaveCode, statusName,
                            beginningBalance, accrual, usedCredits, availableBalance,
                            isActive, dtAdded, addedByUser
                        ) VALUES (
                            @EmployeeNo, @RqLeaveID, 'CTO', 'APPROVED OFFSET APPLICATION',
                            @BeginningBalance, 0, @UsedCredits, @AvailableBalance,
                            1, NOW(), @UserCode
                        )", new
                    {
                        EmployeeNo = model.employeeNo,
                        RqLeaveID = newId,
                        BeginningBalance = currentAvailable,
                        UsedCredits = -requestedDays,
                        AvailableBalance = newAvailable,
                        UserCode = EmployeeNo
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
                    NotifyRequestAction("cto", newId, model.employeeNo, "approved");

                    // Distinct audit message so it's clear this was not a manual approval
                    _auditTrail.Log("rq_leave", newId, "AUTO-APPROVED",
                        $"Offset application request auto-approved on creation: {model.employeeNo} is a Level 4 approver. " +
                        $"Period: {model.leaveDateFrom:yyyy-MM-dd} to {model.leaveDateTo:yyyy-MM-dd} " +
                        $"(CTO, {requestedDays:F2} days). CTO credits deducted immediately as per standard flow.");
                }
                else
                {
                    // Standard flow — notify approvers that a request is pending
                    NotifyRequestAction("cto", newId, model.employeeNo, "pending");

                    _auditTrail.Log("rq_leave", newId, "CREATED",
                        $"Added offset application for {model.employeeNo}: {model.leaveDateFrom:yyyy-MM-dd} to {model.leaveDateTo:yyyy-MM-dd} (CTO)");
                }

                // ── Handle attachment upload if provided ──────────────────────────────
                // Runs in both branches — attachment handling is independent of approval status.
                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Offset application saved but attachment failed: {uploadResult.message}" });
                }

                var successMessage = isLevel4Approver
                    ? "Offset application request filed and automatically approved."
                    : "Offset application request added successfully!";

                string requestorName = _emailService.GetEmployeeNameAsync(model.employeeNo).ToString();
                string approverEmail = _emailService.GetApproverEmails(model.employeeNo, 2).ToString();
                string dateFrom = model.leaveDateFrom?.ToString("yyyy-MM-dd") + " " + model.timeIN?.ToString(@"hh\:mm\:ss");
                string dateTo = model.leaveDateTo?.ToString("yyyy-MM-dd") + " " + model.timeOUT?.ToString(@"hh\:mm\:ss");

                _emailService.SendRequestEmailAsync("Offset Application Request", requestorName, approverEmail, dateFrom, dateTo);

                return Json(new { success = true, message = successMessage });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddOffsetApplicationRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error adding offset application request: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateOffsetApplicationRequest(OffsetApplicationRequestModel model, IFormFile attachment)
        {
            try
            {
                var currentRecord = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT statusLevel4, employeeNo FROM rq_leave WHERE id = @id AND isActive = 1 AND leaveCode = 'CTO'",
                    new { model.id });

                if (currentRecord == null)
                    return Json(new { success = false, message = "Offset application request not found!" });

                string currentStatus = currentRecord.statusLevel4;
                string recordEmployeeNo = currentRecord.employeeNo;

                if (!CanViewEmployee(recordEmployeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to modify this employee's offset application request." });
                }

                if (currentStatus != "Pending" && currentStatus != "Declined")
                    return Json(new { success = false, message = "Only pending or declined requests can be edited!" });

                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                if (model.leaveDateTo < model.leaveDateFrom)
                    return Json(new { success = false, message = "End date must be after start date!" });

                // Always use CTO for offset applications
                model.leaveCode = "CTO";

                double requestedDays = model.leaveCountDays ?? 0;

                var availableCredits = _db.QueryFirstOrDefault<double?>(
                    @"SELECT COALESCE(
                        (SELECT availableBalance
                         FROM   m_leave
                         WHERE  employeeNo = @EmployeeNo
                           AND  leaveCode  = 'CTO'
                           AND  isActive   = 1
                         ORDER  BY id DESC
                         LIMIT  1), 0)",
                    new { EmployeeNo = model.employeeNo });

                double availableBalance = availableCredits ?? 0;

                if (requestedDays > availableBalance)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Insufficient CTO balance. Available: {availableBalance:F2} days, Requested: {requestedDays:F2} days."
                    });
                }

                var newStatus = currentStatus == "Declined" ? "Pending" : currentStatus;

                var sql = @"
                    UPDATE rq_leave 
                    SET employeeNo = @employeeNo,
                        leaveCode = @leaveCode,
                        leaveDateFrom = @leaveDateFrom,
                        leaveDateTo = @leaveDateTo,
                        leaveCountDays = @leaveCountDays,
                        leaveCountHours = @leaveCountHours,
                        leaveType = @leaveType,
                        leaveReason = @leaveReason,
                        remarks = @remarks,
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
                    leaveType = model.leaveType ?? "whole",
                    leaveReason = model.leaveReason ?? "",
                    remarks = model.remarks ?? "",
                    statusLevel = newStatus,
                    lastModifiedByUser = EmployeeNo
                });

                _auditTrail.Log("rq_leave", model.id, "UPDATED",
                    $"Updated offset application for {model.employeeNo}");

                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Offset application updated but attachment failed: {uploadResult.message}" });
                }

                var message = currentStatus == "Declined"
                    ? "Offset application request updated successfully and status set back to Pending!"
                    : "Offset application request updated successfully!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateOffsetApplicationRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error updating offset application request: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<JsonResult> ApproveOffsetApplicationRequest(int id, string approvedByUser)
        {
            try
            {
                // ── 1. Load the request ──────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, leaveCode, leaveCountDays,
                           statusLevel1, statusLevel2, statusLevel3, statusLevel4, leaveDateFrom, leaveDateTo, timeIN, timeOUT
                    FROM rq_leave
                    WHERE id = @id AND isActive = 1 AND leaveCode = 'CTO'",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Offset application request not found!" });

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
                    NotifyRequestAction("cto", id, employeeNo, "approved");
                }
                else
                {
                    int nextLevel = requiredLevels
                        .Where(l => !newlyApproved.Contains(l))
                        .OrderBy(l => l)
                        .First();

                    NotifyNextApprover("cto", id, employeeNo, nextLevel);
                }

                // ── 11. Audit ────────────────────────────────────────────────────
                var auditMsg = isFullyApproved
                    ? $"Offset application fully approved at Level {actingLevel} by {actingUser}"
                    : $"Offset application partially approved at Level {actingLevel} by {actingUser}. " +
                      $"Awaiting higher level approval.";

                _auditTrail.Log("rq_leave", id, "APPROVED", auditMsg);

                var successMessage = isFullyApproved
                    ? "Offset application request approved successfully!"
                    : $"Level {actingLevel} approval recorded. Request is now pending the next approver.";

                string employeeEmail = _emailService.GetEmployeeEmail(request.employeeNo).ToString();
                string dateFrom = request.leaveDateFrom?.ToString("yyyy-MM-dd") + " " + request.timeIN?.ToString(@"hh\:mm\:ss");
                string dateTo = request.leaveDateTo?.ToString("yyyy-MM-dd") + " " + request.timeOUT?.ToString(@"hh\:mm\:ss");

                _emailService.SendRequestStatusEmailAsync("Offset Application Request Status", employeeEmail, request.statusLevel1, request.statusLevel2,
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
                Console.WriteLine($"Error in ApproveOffsetApplicationRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while approving the request." });
            }
        }

        [HttpPost]
        public async Task<JsonResult> DeclineOffsetApplicationRequest(int id, string declinedByUser, string reason = "")
        {
            try
            {
                // ── 1. Load request ──────────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, statusLevel1, statusLevel2, statusLevel3, statusLevel4
                    FROM rq_leave
                    WHERE id = @id AND isActive = 1 AND leaveCode = 'CTO'",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Offset application request not found!" });

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

                // ── 7. Notify ────────────────────────────────────────────────────
                NotifyRequestAction("cto", id, employeeNo, "declined");

                // ── 8. Audit ─────────────────────────────────────────────────────
                _auditTrail.Log("rq_leave", id, "DECLINED",
                    $"Declined offset application request at Level {actingLevel} by {actingUser}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "Offset application request declined successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeclineOffsetApplicationRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while declining the request." });
            }
        }

        [HttpPost]
        public async Task<JsonResult> CancelOffsetApplicationRequest(int id, string cancelledByUser, string reason = "")
        {
            try
            {
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT employeeNo, leaveCode, leaveCountDays, statusLevel4 FROM rq_leave WHERE id = @id AND isActive = 1 AND leaveCode = 'CTO'",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Offset application request not found!" });

                if (request.employeeNo != EmployeeNo && !HasFullAccess())
                    return Json(new { success = false, message = "Access denied. You can only cancel your own requests." });

                if (request.statusLevel4 == "Cancelled")
                    return Json(new { success = false, message = "Request is already cancelled!" });

                if (request.statusLevel4 == "Approved")
                    return Json(new { success = false, message = "Approved requests cannot be cancelled!" });

                // ── Always reverse the credit deduction applied on submission ─────────
                // Credits are deducted on submission for ALL statuses (Pending or Declined),
                // so cancellation must always restore them — mirrors CancelLeaveRequest logic.
                double ctoDays = Convert.ToDouble(request.leaveCountDays);

                var latestBalance = _db.QueryFirstOrDefault<dynamic>(
                    @"SELECT availableBalance, usedCredits FROM m_leave 
                      WHERE employeeNo = @employeeNo AND leaveCode = 'CTO'
                      ORDER BY id DESC LIMIT 1",
                    new { employeeNo = (string)request.employeeNo });

                if (latestBalance != null && ctoDays > 0)
                {
                    double currentAvail = Convert.ToDouble(latestBalance.availableBalance);
                    double currentUsed = Convert.ToDouble(latestBalance.usedCredits);
                    double newAvailable = currentAvail + ctoDays;
                    double newUsedCredits = currentUsed - ctoDays;

                    _db.Execute(@"
                        INSERT INTO m_leave (
                            employeeNo, rq_leaveID, leaveCode, statusName,
                            beginningBalance, accrual, usedCredits, availableBalance,
                            isActive, dtAdded, addedByUser
                        ) VALUES (
                            @EmployeeNo, @RqLeaveID, 'CTO', 'CANCELLED OFFSET APPLICATION',
                            @BeginningBalance, 0, @UsedCredits, @AvailableBalance,
                            1, NOW(), @UserCode
                        )",
                        new
                        {
                            EmployeeNo = (string)request.employeeNo,
                            RqLeaveID = id,
                            BeginningBalance = currentAvail,
                            UsedCredits = newUsedCredits,
                            AvailableBalance = newAvailable,
                            UserCode = cancelledByUser ?? EmployeeNo
                        });
                }

                // ── Update request status to Cancelled ────────────────────────────────
                var sql = @"
                    UPDATE rq_leave 
                    SET statusLevel1       = 'Cancelled',
                        statusLevel2       = 'Cancelled',
                        statusLevel3       = 'Cancelled',
                        statusLevel4       = 'Cancelled',
                        dtStatusLevel1     = NOW(),
                        statusByLevel1     = @cancelledByUser,
                        dtStatusLevel2     = NOW(),
                        statusByLevel2     = @cancelledByUser,
                        dtStatusLevel3     = NOW(),
                        statusByLevel3     = @cancelledByUser,
                        dtStatusLevel4     = NOW(),
                        statusByLevel4     = @cancelledByUser,
                        dtLastModified     = NOW(),
                        lastModifiedByUser = @cancelledByUser
                    WHERE id = @id";

                await _db.ExecuteAsync(sql, new { id, cancelledByUser = cancelledByUser ?? EmployeeNo });

                NotifyRequestAction("cto", id, request.employeeNo, "cancelled");

                _auditTrail.Log("rq_leave", id, "CANCELLED",
                    $"Cancelled offset application request by {cancelledByUser ?? EmployeeNo}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "Offset application request cancelled successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CancelOffsetApplicationRequest: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetCTOAttachments(string employeeNo)
        {
            try
            {
                if (!CanViewEmployee(employeeNo))
                {
                    return Json(new { error = "Access denied. You don't have permission to view this employee's attachments." });
                }

                var sql = @"
                    SELECT id, attachmentPath, dtAdded 
                    FROM e_attachment 
                    WHERE employeeNo = @employeeNo 
                    AND attachmentTypeCode = 'CTO' 
                    AND isActive = 1
                    ORDER BY dtAdded DESC";

                return Json(_db.Query(sql, new { employeeNo }).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCTOAttachments: {ex.Message}");
                return Json(new List<object>());
            }
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

                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "cto");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                var sql = @"
                    INSERT INTO e_attachment (employeeNo, attachmentDescription, attachmentTypeCode, attachmentPath, isActive, dtAdded) 
                    VALUES (@employeeNo, 'Offset Application (CTO)', 'CTO', @attachmentPath, 1, NOW())";

                _db.Execute(sql, new
                {
                    employeeNo,
                    attachmentPath = $"/uploads/cto/{fileName}"
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