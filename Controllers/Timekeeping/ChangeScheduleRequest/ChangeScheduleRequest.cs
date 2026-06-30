using Dapper;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.ChangeScheduleRequest
{
    [ModuleAuthorize("RchangeschedM")]
    public class ChangeScheduleRequestController : TimekeepingRequestBaseController
    {
        public ChangeScheduleRequestController(
            IDbConnection db,
            IAuditTrailService auditTrail,
            IEmailService emailService,
            IApproverService approverService)
            : base(db, auditTrail, "RchangeschedM")
        {
            _approverService = approverService;
            _emailService = (IEmailService)emailService;
        }

        public IActionResult Index()
        {
            return View("~/Views/Timekeeping/ChangeScheduleRequest.cshtml");
        }

        // Get all active Change Schedule Requests with filters AND DATA SCOPE
        [HttpGet]
        public async Task<JsonResult> GetChangeScheduleRequestList(string status, string branch, string department, string dateFrom, string dateTo)
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
                        DATE_FORMAT(rq.effectivityDate, '%m/%d/%Y') AS displayEffectivityDate,
                        TIME_FORMAT(rq.timeIN, '%H:%i') AS displayTimeIn,
                        TIME_FORMAT(rq.timeOUT, '%H:%i') AS displayTimeOut,
                        rq.Reason,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusLevel4 AS statusName,
                        rq.remarks,
                        rq.scheduleTypeCode,
                        ss.scheduleTypeName,
                        rq.dtAdded,
                        DATE_FORMAT(rq.dtAdded, '%m/%d/%Y') AS dateRequested,
                        CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                    FROM rq_changeschedule rq
                    INNER JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                    LEFT JOIN s_scheduleType ss ON rq.scheduleTypeCode = ss.scheduleTypeCode
                    LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                    WHERE rq.isActive = 1");

                ApplyDataScopeFilter(query, parameters);
            }
            else if (approverInfo.IsApprover)
            {
                query.Append(@"
                    SELECT 
                        rq.id,
                        rq.employeeNo,
                        CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,
                        DATE_FORMAT(rq.effectivityDate, '%m/%d/%Y') AS displayEffectivityDate,
                        TIME_FORMAT(rq.timeIN, '%H:%i') AS displayTimeIn,
                        TIME_FORMAT(rq.timeOUT, '%H:%i') AS displayTimeOut,
                        rq.Reason,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusLevel4 AS statusName,
                        rq.remarks,
                        rq.scheduleTypeCode,
                        ss.scheduleTypeName,
                        rq.dtAdded,
                        DATE_FORMAT(rq.dtAdded, '%m/%d/%Y') AS dateRequested,
                        CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                    FROM rq_changeschedule rq
                    INNER JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                    LEFT JOIN s_scheduleType ss ON rq.scheduleTypeCode = ss.scheduleTypeCode
                    LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                    WHERE rq.isActive = 1
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
                        DATE_FORMAT(rq.effectivityDate, '%m/%d/%Y') AS displayEffectivityDate,
                        TIME_FORMAT(rq.timeIN, '%H:%i') AS displayTimeIn,
                        TIME_FORMAT(rq.timeOUT, '%H:%i') AS displayTimeOut,
                        rq.Reason,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusLevel4 AS statusName,
                        rq.remarks,
                        rq.scheduleTypeCode,
                        ss.scheduleTypeName,
                        rq.dtAdded,
                        DATE_FORMAT(rq.dtAdded, '%m/%d/%Y') AS dateRequested,
                        CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                    FROM rq_changeschedule rq
                    INNER JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                    LEFT JOIN s_scheduleType ss ON rq.scheduleTypeCode = ss.scheduleTypeCode
                    LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                    WHERE rq.isActive = 1
                    AND rq.employeeNo = @currentEmployeeNo");

                parameters.Add("@currentEmployeeNo", EmployeeNo);
            }

            // Apply hidden employees filter
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
                    query.Append(" AND rq.effectivityDate BETWEEN @dateFrom AND @dateTo");
                    parameters.Add("@dateFrom", parsedFrom.ToString("yyyy-MM-dd"));
                    parameters.Add("@dateTo", parsedTo.ToString("yyyy-MM-dd"));
                }
            }

            query.Append(" ORDER BY rq.id DESC");

            await MarkRequestsAsProcessedAsync("rq_changeschedule", "effectivityDate");

            var requests = await _db.QueryAsync<dynamic>(query.ToString(), parameters);
            return Json(new { data = requests });
        }

        // Get single Change Schedule Request by ID WITH SECURITY CHECK
        [HttpGet]
        public async Task<JsonResult> GetChangeScheduleRequest(int id)
        {
            try
            {
                var sql = @"
                    SELECT 
                        rq.id,
                        rq.employeeNo,
                        DATE_FORMAT(rq.effectivityDate, '%m/%d/%Y') AS displayEffectivityDate,
                        TIME_FORMAT(rq.timeIN,  '%H:%i') AS displayTimeIn,
                        TIME_FORMAT(rq.timeOUT, '%H:%i') AS displayTimeOut,
                        rq.Reason,
                        rq.scheduleTypeCode,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusLevel4 AS statusName,
                        rq.remarks,
                        CONCAT(IFNULL(e.firstName,''),   ' ',
                               IFNULL(CONCAT(e.middleName,   ' '),''),
                               IFNULL(e.lastName,''))   AS employeeName,
                        CONCAT(IFNULL(req.firstName,''), ' ',
                               IFNULL(CONCAT(req.middleName, ' '),''),
                               IFNULL(req.lastName,'')) AS requestedByUser
                    FROM rq_changeschedule rq
                    LEFT JOIN e_basicinfo e   ON e.employeeNo   = rq.employeeNo
                    LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                    WHERE rq.id = @Id AND rq.isActive = 1";

                var request = _db.QueryFirstOrDefault<dynamic>(sql, new { Id = id });

                if (request == null) return Json(null);

                if (!CanViewEmployee((string)request.employeeNo))
                    return Json(new { error = "Access denied. You don't have permission to view this schedule change request." });

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

                        // Level 4 can always approve regardless of lower levels
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
                    employeeName = (string)request.employeeName,
                    requestedByUser = (string)request.requestedByUser,
                    displayEffectivityDate = (string)request.displayEffectivityDate,
                    displayTimeIn = (string)request.displayTimeIn,
                    displayTimeOut = (string)request.displayTimeOut,
                    reason = (string)request.Reason,
                    scheduleTypeCode = (string)request.scheduleTypeCode,
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
                Console.WriteLine($"Error in GetChangeScheduleRequest: {ex.Message}");
                return Json(null);
            }
        }

        // Get schedule types for dropdown
        [HttpGet]
        public JsonResult GetScheduleTypes()
        {
            try
            {
                var sql = @"
                    SELECT 
                        scheduleTypeCode, 
                        scheduleTypeName
                    FROM s_scheduleType 
                    WHERE isActive = 1 
                    ORDER BY scheduleTypeName";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetScheduleTypes: {ex.Message}");
                return Json(new List<object>());
            }
        }

        // Add new Change Schedule Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> AddChangeScheduleRequest(ChangeScheduleRequestModel model)
        {
            try
            {
                // Security check using base method
                if (!CanViewEmployee(model.employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to create a schedule change request for this employee." });
                }

                // Validate employee exists using base method
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Duplicate filing check
                var duplicate = _db.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) FROM rq_changeschedule
                    WHERE employeeNo = @employeeNo
                    AND effectivityDate = @effectivityDate
                    AND isActive = 1
                    AND statusLevel4 NOT IN ('Cancelled', 'Declined')",
                    new { model.employeeNo, model.effectivityDate });

                if (duplicate > 0)
                    return Json(new { success = false, message = "A change schedule request for this date already exists." });

                // Validate required fields
                if (model.effectivityDate == null)
                    return Json(new { success = false, message = "Effectivity Date is required!" });

                if (string.IsNullOrWhiteSpace(model.scheduleTypeCode))
                    return Json(new { success = false, message = "Schedule Type is required!" });

                if (string.IsNullOrWhiteSpace(model.Reason))
                    return Json(new { success = false, message = "Reason is required!" });

                // ── Check if the requestor is a Level 4 approver ──────────────────────
                // If yes, we will auto-approve immediately after insert.
                bool isLevel4Approver = _db.QuerySingle<int>(@"
                    SELECT COUNT(*)
                    FROM e_approver
                    WHERE approverNo    = @employeeNo
                    AND   approverLevel = 4
                    AND   isActive      = 1",
                    new { model.employeeNo }) > 0;

                // Calculate weekday name from effectivity date
                string weekdayName = model.effectivityDate?.DayOfWeek.ToString() ?? "";

                // ── Insert new Change Schedule Request ────────────────────────────────
                // Initial status is always Pending on insert; we update it right after
                // for Level 4 approvers so the row is never left in a dangling state.
                var sql = @"
                    INSERT INTO rq_changeschedule 
                    (employeeNo, weekdayName, effectivityDate, timeIN, timeOUT, Reason, 
                     scheduleTypeCode, statusLevel1, statusLevel2, statusLevel3, statusLevel4, remarks, 
                     isActive, dtAdded, addedByUser, requestedByUser, 
                     dtStatus, statusByUser, dtStatusLevel1, statusByLevel1,
                     dtStatusLevel2, statusByLevel2, 
                     dtStatusLevel3, statusByLevel3, dtStatusLevel4, statusByLevel4) 
                    VALUES 
                    (@employeeNo, @weekdayName, @effectivityDate, @timeIN, @timeOUT, @Reason, 
                     @scheduleTypeCode, 'Pending', 'Pending', 'Pending', 'Pending', @remarks, 
                     1, NOW(), @addedByUser, @requestedByUser, 
                     NOW(), @addedByUser, NOW(), @addedByUser,
                     NOW(), @addedByUser, 
                     NOW(), @addedByUser, NOW(), @addedByUser);
                    SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    model.employeeNo,
                    weekdayName,
                    model.effectivityDate,
                    timeIN = model.scheduleTypeCode == "REST" ? (TimeSpan?)null : (model.timeIN ?? TimeSpan.Parse("08:00:00")),
                    timeOUT = model.scheduleTypeCode == "REST" ? (TimeSpan?)null : (model.timeOUT ?? TimeSpan.Parse("17:00:00")),
                    Reason = model.Reason ?? "",
                    model.scheduleTypeCode,
                    remarks = model.remarks ?? "",
                    addedByUser = EmployeeNo ?? model.employeeNo,
                    requestedByUser = EmployeeNo ?? model.employeeNo
                });

                // ── Auto-approve for Level 4 approvers ───────────────────────────────
                if (isLevel4Approver)
                {
                    // Level 4 bypass: all status levels are set to Approved immediately.
                    _db.Execute(@"
                        UPDATE rq_changeschedule
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
                    NotifyRequestAction("changeSchedule", newId, model.employeeNo, "approved");

                    // Distinct audit message so it's clear this was not a manual approval
                    _auditTrail.Log("rq_changeschedule", newId, "AUTO-APPROVED",
                        $"Change schedule request auto-approved on creation: {model.employeeNo} is a Level 4 approver. " +
                        $"Effectivity date: {model.effectivityDate:yyyy-MM-dd}");
                }
                else
                {
                    // Standard flow — notify approvers that a request is pending
                    NotifyRequestAction("changeSchedule", newId, model.employeeNo, "pending");

                    _auditTrail.Log("rq_changeschedule", newId, "CREATED",
                        $"Added schedule change request for {model.employeeNo}: {model.effectivityDate:yyyy-MM-dd}");
                }

                var successMessage = isLevel4Approver
                    ? "Change Schedule Request filed and automatically approved."
                    : "Change Schedule Request added successfully!";

                string requestorName = _emailService.GetEmployeeNameAsync(model.employeeNo).ToString();
                string dateFrom = model.dateFrom?.ToString("yyyy-MM-dd") + " " + model.timeIN?.ToString(@"hh\:mm\:ss");
                string dateTo = model.dateTo?.ToString("yyyy-MM-dd") + " " + model.timeOUT?.ToString(@"hh\:mm\:ss");
                int? leastApproverLevel = await _emailService.GetLeastApproverLevelAsync(model.employeeNo);
                if (leastApproverLevel.HasValue)
                {
                    string approverEmail = await _emailService.GetApproverEmails(model.employeeNo, leastApproverLevel.Value);

                    if (!string.IsNullOrWhiteSpace(approverEmail))
                    {
                        _emailService.SendRequestEmailAsync("Change Schedule Request", requestorName, approverEmail, dateFrom, dateTo);
                    }
                }

                return Json(new { success = true, message = successMessage });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddChangeScheduleRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error adding Change Schedule Request: {ex.Message}" });
            }
        }

        // Update existing Change Schedule Request WITH SECURITY CHECK
        [HttpPost]
        public JsonResult UpdateChangeScheduleRequest(ChangeScheduleRequestModel model)
        {
            try
            {
                // Check if record exists and get employee info
                var existingRequest = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo, statusLevel4 FROM rq_changeschedule WHERE id = @id AND isActive = 1",
                    new { model.id });

                if (existingRequest == null)
                    return Json(new { success = false, message = "Change Schedule Request not found!" });

                // Security check using base method
                if (!CanViewEmployee(existingRequest.employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to update this schedule change request." });
                }

                string currentStatus = existingRequest.statusLevel4;

                // Only allow editing Pending or Declined requests
                if (currentStatus != "Pending" && currentStatus != "Declined")
                    return Json(new { success = false, message = "Only pending or declined requests can be edited!" });

                // Validate employee exists using base method
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Validate required fields
                if (model.effectivityDate == null)
                    return Json(new { success = false, message = "Effectivity Date is required!" });

                if (string.IsNullOrWhiteSpace(model.scheduleTypeCode))
                    return Json(new { success = false, message = "Schedule Type is required!" });

                if (string.IsNullOrWhiteSpace(model.Reason))
                    return Json(new { success = false, message = "Reason is required!" });

                // Calculate weekday name from effectivity date
                string weekdayName = model.effectivityDate?.DayOfWeek.ToString() ?? "";

                // If status was Declined, set back to Pending when edited
                var newStatus = currentStatus == "Declined" ? "Pending" : currentStatus;

                var sql = @"
                    UPDATE rq_changeschedule 
                    SET employeeNo = @employeeNo,
                        weekdayName = @weekdayName,
                        effectivityDate = @effectivityDate,
                        timeIN = @timeIN,
                        timeOUT = @timeOUT,
                        Reason = @Reason,
                        scheduleTypeCode = @scheduleTypeCode,
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
                    weekdayName,
                    model.effectivityDate,
                    timeIN = model.scheduleTypeCode == "REST" ? (TimeSpan?)null : (model.timeIN ?? TimeSpan.Parse("08:00:00")),
                    timeOUT = model.scheduleTypeCode == "REST" ? (TimeSpan?)null : (model.timeOUT ?? TimeSpan.Parse("17:00:00")),
                    Reason = model.Reason ?? "",
                    model.scheduleTypeCode,
                    remarks = model.remarks ?? "",
                    statusLevel = newStatus,
                    lastModifiedByUser = EmployeeNo ?? model.employeeNo
                });

                // Log to audit trail
                _auditTrail.Log("rq_changeschedule", model.id, "UPDATED",
                    $"Updated schedule change request for {model.employeeNo}: {model.effectivityDate:yyyy-MM-dd}");

                var message = currentStatus == "Declined"
                    ? "Change Schedule Request updated successfully and status set back to Pending!"
                    : "Change Schedule Request updated successfully!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateChangeScheduleRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error updating Change Schedule Request: {ex.Message}" });
            }
        }

        // Approve Change Schedule Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> ApproveChangeScheduleRequest(int id, string approvedByUser)
        {
            try
            {
                // ── 1. Load request ───────────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, statusLevel1, statusLevel2, statusLevel3, statusLevel4, dateFrom, timeIN, dateTo, timeOUT
                    FROM rq_changeschedule
                    WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Change Schedule Request not found!" });

                if (request.statusLevel4 != "Pending")
                    return Json(new
                    {
                        success = false,
                        message = "This request has already been finalised and cannot be approved again."
                    });

                string employeeNo = (string)request.employeeNo;
                string actingUser = approvedByUser ?? EmployeeNo;
                bool isFull = HasFullAccess();

                // ── 2. Determine acting approver level ────────────────────────────
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

                // ── 3. Required level chain ───────────────────────────────────────
                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                // valid approver levels are 1, 2, 3, 4
                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();

                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                // ── 4. Current approved levels ────────────────────────────────────
                var approvedLevels = new List<int>();
                if ((string)request.statusLevel1 == "Approved" && requiredLevels.Contains(1))
                    approvedLevels.Add(1);
                if ((string)request.statusLevel2 == "Approved" && requiredLevels.Contains(2))
                    approvedLevels.Add(2);
                if ((string)request.statusLevel3 == "Approved" && requiredLevels.Contains(3))
                    approvedLevels.Add(3);

                // ── 5. Resolve acting level ───────────────────────────────────────
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

                // ── 7. Guard: already approved at this level? ─────────────────────
                if (approvedLevels.Contains(actingLevel))
                    return Json(new
                    {
                        success = false,
                        message = $"Level {actingLevel} has already approved this request."
                    });

                // ── 8. Determine new overall state ────────────────────────────────
                // When Level 4 bypasses, ALL required levels are treated as approved.
                var newlyApproved = actingLevel == 4
                    ? new List<int>(requiredLevels)
                    : new List<int>(approvedLevels) { actingLevel };

                int highestRequired = requiredLevels.Max();
                bool isFullyApproved = requiredLevels.All(l => newlyApproved.Contains(l));

                // ── 9. Build UPDATE ───────────────────────────────────────────────
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

                // If fully approved and highest required < 4, cascade final gate
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
                    $"UPDATE rq_changeschedule SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // ── 10. Notify ────────────────────────────────────────────────────
                if (isFullyApproved)
                {
                    NotifyRequestAction("changeSchedule", id, employeeNo, "approved");
                }
                else
                {
                    int nextLevel = requiredLevels
                        .Where(l => !newlyApproved.Contains(l))
                        .OrderBy(l => l)
                        .First();

                    NotifyNextApprover("changeSchedule", id, employeeNo, nextLevel);
                }

                // ── 11. Audit ─────────────────────────────────────────────────────
                var auditMsg = isFullyApproved
                    ? $"Change schedule request fully approved at Level {actingLevel} by {actingUser}"
                    : $"Change schedule request partially approved at Level {actingLevel} by {actingUser}. Awaiting higher level approval.";

                string employeeEmail = _emailService.GetEmployeeEmail(request.employeeNo).ToString();
                string dateFrom = request.dateFrom?.ToString("yyyy-MM-dd") + " " + request.timeIN?.ToString(@"hh\:mm\:ss");
                string dateTo = request.dateTo?.ToString("yyyy-MM-dd") + " " + request.timeOUT?.ToString(@"hh\:mm\:ss");

                _emailService.SendRequestStatusEmailAsync("Change Request Status", employeeEmail, request.statusLevel1, request.statusLevel2,
                request.statusLevel3, request.statusLevel4, dateFrom, dateTo);

                _auditTrail.Log("rq_changeschedule", id, "APPROVED", auditMsg);

                var successMessage = isFullyApproved
                    ? "Change Schedule Request approved successfully!"
                    : $"Level {actingLevel} approval recorded. Request is now pending the next approver.";

                return Json(new { success = true, message = successMessage, isFullyApproved, actingLevel });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ApproveChangeScheduleRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while approving the request." });
            }
        }

        // Decline Change Schedule Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> DeclineChangeScheduleRequest(int id, string declinedByUser, string reason = "")
        {
            try
            {
                // ── 1. Load request ───────────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, statusLevel1, statusLevel2, statusLevel3, statusLevel4
                    FROM rq_changeschedule
                    WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Change Schedule Request not found!" });

                if (request.statusLevel4 == "Cancelled" || request.statusLevel4 == "Processed")
                    return Json(new { success = false, message = "Cancelled or processed requests cannot be declined!" });

                string employeeNo = (string)request.employeeNo;
                string actingUser = declinedByUser ?? EmployeeNo;
                bool isFull = HasFullAccess();

                // ── 2. Determine acting approver level ────────────────────────────
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

                // ── 3. Required levels ────────────────────────────────────────────
                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();

                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                // ── 4. FULL access: find next pending level ───────────────────────
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

                // ── 6. Build UPDATE ───────────────────────────────────────────────
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
                setParts.Add("dtStatus           = NOW()");
                setParts.Add("statusByUser       = @actingUser");
                setParts.Add("dtLastModified     = NOW()");
                setParts.Add("lastModifiedByUser = @actingUser");

                var updateSql =
                    $"UPDATE rq_changeschedule SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // ── 7. Notify ─────────────────────────────────────────────────────
                NotifyRequestAction("changeSchedule", id, employeeNo, "declined");

                // ── 8. Audit ──────────────────────────────────────────────────────
                _auditTrail.Log("rq_changeschedule", id, "DECLINED",
                    $"Declined change schedule request at Level {actingLevel} by {actingUser}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "Change Schedule Request declined successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeclineChangeScheduleRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while declining the request." });
            }
        }

        // Cancel Change Schedule Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> CancelChangeScheduleRequest(int id, string cancelledByUser, string reason = "")
        {
            try
            {
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT employeeNo, statusLevel4 FROM rq_changeschedule WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Change Schedule Request not found!" });

                // Only allow user to cancel their own request
                if (request.employeeNo != EmployeeNo && !HasFullAccess())
                    return Json(new { success = false, message = "Access denied. You can only cancel your own requests." });

                if (request.statusLevel4 == "Approved")
                    return Json(new { success = false, message = "Approved requests cannot be cancelled!" });

                if (request.statusLevel4 == "Cancelled")
                    return Json(new { success = false, message = "Request is already cancelled!" });

                var sql = @"
                    UPDATE rq_changeschedule 
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

                NotifyRequestAction("changeSchedule", id, request.employeeNo, "cancelled");

                _auditTrail.Log("rq_changeschedule", id, "CANCELLED",
                    $"Cancelled schedule change request by {cancelledByUser ?? EmployeeNo}{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Change Schedule Request cancelled successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CancelChangeScheduleRequest: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}