using Dapper;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.OffsetCreditRequest
{
    [ModuleAuthorize("RoffsetCreditM")]
    public class OffsetCreditRequestController : TimekeepingRequestBaseController
    {

        public OffsetCreditRequestController(
            IDbConnection db,
            IAuditTrailService auditTrail,
            IEmailService emailService,
            IApproverService approverService)
            : base(db, auditTrail, "RoffsetCreditM")
        {
            _approverService = approverService;
            _emailService = (IEmailService)emailService;
        }

        public IActionResult Index()
        {
            return View("~/Views/Timekeeping/OffsetCreditRequest.cshtml");
        }

        [HttpGet]
        public async Task<JsonResult> GetOffsetCreditRequestList(string status, string branch, string department, string dateFrom, string dateTo)
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
                    CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS employeeName,
                    DATE_FORMAT(rq.overTimeDateIN, '%m/%d/%Y') as displayDateIn,
                    DATE_FORMAT(rq.overTimeDateOUT, '%m/%d/%Y') as displayDateOut,
                    rq.approvedRenderOT,
                    rq.overTimeReason,
                    rq.statusLevel1,
                    rq.statusLevel2,
                    rq.statusLevel3,
                    rq.statusLevel4,
                    rq.statusLevel4 AS statusName,
                    rq.remarks,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y') as dateRequested,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                FROM rq_cto rq
                INNER JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser";

            if (hasFullAccess || hasBroadScope)
            {
                query.Append(selectBlock);
                query.Append(" WHERE rq.isActive = 1");
                ApplyDataScopeFilter(query, parameters);
            }
            else if (approverInfo.IsApprover)
            {
                query.Append(selectBlock);
                query.Append(@"
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
                query.Append(selectBlock);
                query.Append(" WHERE rq.isActive = 1 AND rq.employeeNo = @currentEmployeeNo");
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
                    query.Append(" AND rq.overTimeDateIN BETWEEN @dateFrom AND @dateTo");
                    parameters.Add("@dateFrom", parsedFrom.ToString("yyyy-MM-dd"));
                    parameters.Add("@dateTo", parsedTo.ToString("yyyy-MM-dd"));
                }
            }

            query.Append(" ORDER BY rq.id DESC");

            await MarkRequestsAsProcessedAsync("rq_cto", "overTimeDateIN", "statusLevel4", alsoUpdateStatusName: true);

            var requests = await _db.QueryAsync<dynamic>(query.ToString(), parameters);
            return Json(new { data = requests });
        }

        // Get single Offset Credit Request by ID
        [HttpGet]
        public async Task<JsonResult> GetOffsetCreditRequest(int id)
        {
            try
            {
                var employeeNo = _db.QueryFirstOrDefault<string>(
                    "SELECT employeeNo FROM rq_cto WHERE id = @id AND isActive = 1",
                    new { id });

                if (string.IsNullOrEmpty(employeeNo))
                    return Json(new { error = "Offset Credit Request not found!" });

                if (!CanViewEmployee(employeeNo))
                    return Json(new { error = "Access denied. You don't have permission to view this employee's offset credit request." });

                var sql = @"
                    SELECT 
                        rq.id,
                        rq.employeeNo,
                        DATE_FORMAT(rq.overTimeDateIN,  '%m/%d/%Y') AS displayDateIn,
                        DATE_FORMAT(rq.overTimeDateOUT, '%m/%d/%Y') AS displayDateOut,
                        rq.approvedRenderOT,
                        rq.overTimeReason,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusLevel4 AS statusName,
                        rq.remarks,
                        DATE_FORMAT(rq.dtAdded, '%m/%d/%Y') AS dateRequested,
                        CONCAT(IFNULL(e.firstName,''),   ' ',
                               IFNULL(CONCAT(e.middleName,   ' '),''),
                               IFNULL(e.lastName,''))   AS employeeName,
                        CONCAT(IFNULL(req.firstName,''), ' ',
                               IFNULL(CONCAT(req.middleName, ' '),''),
                               IFNULL(req.lastName,'')) AS requestedByUser
                    FROM rq_cto rq
                    LEFT JOIN e_basicinfo e   ON e.employeeNo   = rq.employeeNo
                    LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                    WHERE rq.id = @Id AND rq.isActive = 1";

                var request = _db.QueryFirstOrDefault<dynamic>(sql, new { Id = id });
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
                    employeeName = (string)request.employeeName,
                    requestedByUser = (string)request.requestedByUser,
                    displayDateIn = (string)request.displayDateIn,
                    displayDateOut = (string)request.displayDateOut,
                    approvedRenderOT = (object)request.approvedRenderOT,
                    overTimeReason = (string)request.overTimeReason,
                    remarks = (string)request.remarks,
                    dateRequested = (string)request.dateRequested,
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
                Console.WriteLine($"Error in GetOffsetCreditRequest: {ex.Message}");
                return Json(null);
            }
        }

        [HttpGet]
        public JsonResult GetCTOBalance(string employeeNo)
        {
            try
            {
                if (!CanViewEmployee(employeeNo))
                    return Json(new { success = false, error = "Access denied." });

                var balanceSql = @"
                    SELECT 
                        COALESCE(SUM(accrual), 0) AS totalEarned,
                        COALESCE(SUM(usedCredits), 0) AS totalUsed,
                        (SELECT availableBalance 
                         FROM m_leave 
                         WHERE employeeNo = @EmployeeNo AND leaveCode = 'CTO' AND isActive = 1
                         ORDER BY id DESC LIMIT 1) AS availableBalance
                    FROM m_leave
                    WHERE employeeNo = @EmployeeNo AND leaveCode = 'CTO' AND isActive = 1";

                var result = _db.QueryFirstOrDefault<dynamic>(balanceSql, new { EmployeeNo = employeeNo });

                double totalEarned = result != null ? Convert.ToDouble(result.totalEarned) : 0;
                double totalUsed = result != null ? Math.Abs(Convert.ToDouble(result.totalUsed)) : 0;
                double availableBalance = result != null && result.availableBalance != null
                    ? Convert.ToDouble(result.availableBalance) : 0;

                return Json(new
                {
                    success = true,
                    totalEarned,
                    totalUsed,
                    availableBalance
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCTOBalance: {ex.Message}");
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── HELPER: Compute offset days from date range ───────────────────────────
        // Formula: days = (dateOut - dateIn).Days + 1
        // approvedRenderOT = days * 9  (so existing formula: 9hrs - 1 lunch = 8hrs / 8 = 1 day)
        private static (double approvedRenderOT, double offsetDays) ComputeOffsetFromDates(
            DateTime dateIn, DateTime dateOut)
        {
            int days = (dateOut.Date - dateIn.Date).Days + 1;
            if (days < 1) days = 1;
            double approvedRenderOT = days * 9.0;           // 9 hrs per day stored
            double effectiveHours = approvedRenderOT - days; // subtract 1 lunch per day
            double offsetDays = effectiveHours / 8.0;   // = days exactly
            return (approvedRenderOT, offsetDays);
        }

        // Add new Offset Credit Request
        [HttpPost]
        public async Task<JsonResult> AddOffsetCreditRequest(OffsetCreditRequestModel model)
        {
            try
            {
                if (!CanViewEmployee(model.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to create offset credit requests for this employee." });

                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                if (model.overTimeDateIN == null || model.overTimeDateOUT == null)
                    return Json(new { success = false, message = "Offset Date In and Date Out are required!" });

                if (model.overTimeDateOUT < model.overTimeDateIN)
                    return Json(new { success = false, message = "Offset Date Out cannot be earlier than Offset Date In!" });

                // Duplicate filing check — block overlapping Offset Credit requests
                var duplicate = _db.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) FROM rq_cto
                    WHERE employeeNo = @employeeNo
                    AND isActive = 1
                    AND statusLevel4 NOT IN ('Cancelled', 'Declined')
                    AND overTimeDateIN  <= @overTimeDateOUT
                    AND overTimeDateOUT >= @overTimeDateIN",
                    new { model.employeeNo, model.overTimeDateIN, model.overTimeDateOUT });

                if (duplicate > 0)
                    return Json(new { success = false, message = "An offset credit request already exists for the selected date range." });

                // ── Compute hours/days from dates only ───────────────────────────
                var (approvedRenderOT, offsetDays) = ComputeOffsetFromDates(
                    model.overTimeDateIN.Value, model.overTimeDateOUT.Value);

                model.approvedRenderOT = approvedRenderOT;

                // ── Check if the requestor is a Level 4 approver ─────────────────
                bool isLevel4Approver = _db.QuerySingle<int>(@"
                    SELECT COUNT(*)
                    FROM e_approver
                    WHERE approverNo    = @employeeNo
                    AND   approverLevel = 4
                    AND   isActive      = 1",
                    new { model.employeeNo }) > 0;

                // ── Insert new Offset Credit Request ─────────────────────────────
                // Store default 08:00 / 17:00 in the time columns (kept for DB compatibility)
                var sql = @"
                    INSERT INTO rq_cto 
                    (employeeNo, overTimeDateIN, overTimeIN, overTimeDateOUT, overTimeOUT, 
                     approvedRenderOT, overTimeReason, statusName, statusLevel1, statusLevel2, statusLevel3, statusLevel4,
                     remarks, isActive, dtAdded, addedByUser, requestedByUser, 
                     dtStatus, statusByUser, dtStatusLevel1, statusByLevel1,
                     dtStatusLevel2, statusByLevel2, 
                     dtStatusLevel3, statusByLevel3, dtStatusLevel4, statusByLevel4) 
                    VALUES 
                    (@employeeNo, @overTimeDateIN, '08:00:00', @overTimeDateOUT, '17:00:00', 
                     @approvedRenderOT, @overTimeReason, 'Pending', 'Pending', 'Pending', 'Pending', 'Pending',
                     @remarks, 1, NOW(), @addedByUser, @requestedByUser, 
                     NOW(), @addedByUser, NOW(), @addedByUser,
                     NOW(), @addedByUser, 
                     NOW(), @addedByUser, NOW(), @addedByUser);
                    SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    model.employeeNo,
                    model.overTimeDateIN,
                    model.overTimeDateOUT,
                    model.approvedRenderOT,
                    overTimeReason = model.overTimeReason ?? "",
                    remarks = model.remarks ?? "",
                    addedByUser = EmployeeNo,
                    requestedByUser = EmployeeNo
                });

                // ── Auto-approve for Level 4 approvers ──────────────────────────
                if (isLevel4Approver)
                {
                    _db.Execute(@"
                        UPDATE rq_cto
                        SET statusName         = 'Approved',
                            statusLevel1       = 'Approved',
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

                    NotifyRequestAction("offsetCredit", newId, model.employeeNo, "approved");
                    RecordToLeaveLedger(model.employeeNo, newId, offsetDays, "OFFSET CREDIT EARNED");

                    _auditTrail.Log("rq_cto", newId, "AUTO-APPROVED",
                        $"Offset Credit request auto-approved on creation: {model.employeeNo} is a Level 4 approver. " +
                        $"Period: {model.overTimeDateIN:yyyy-MM-dd} to {model.overTimeDateOUT:yyyy-MM-dd} " +
                        $"({offsetDays:F2} days). Recorded {offsetDays:F2} days to CTO balance.");
                }
                else
                {
                    NotifyRequestAction("offsetCredit", newId, model.employeeNo, "pending");
                    RecordToLeaveLedger(model.employeeNo, newId, offsetDays, "OFFSET CREDIT EARNED");

                    _auditTrail.Log("rq_cto", newId, "CREATED",
                        $"Added Offset Credit request for {model.employeeNo}: {model.overTimeDateIN:yyyy-MM-dd} to {model.overTimeDateOUT:yyyy-MM-dd} " +
                        $"({offsetDays:F2} days). Recorded {offsetDays:F2} days to CTO balance.");
                }

                var successMessage = isLevel4Approver
                    ? "Offset Credit Request filed and automatically approved. CTO credits earned!"
                    : "Offset Credit Request added successfully and CTO credits earned!";

                string requestorName = _emailService.GetEmployeeNameAsync(model.employeeNo).ToString();
                string dateFrom = model.overTimeDateIN?.ToString("yyyy-MM-dd") + " " + model.overTimeIN?.ToString(@"hh\:mm\:ss");
                string dateTo = model.overTimeDateOUT?.ToString("yyyy-MM-dd") + " " + model.overTimeOUT?.ToString(@"hh\:mm\:ss");
                int? leastApproverLevel = await _emailService.GetLeastApproverLevelAsync(model.employeeNo);
                if (leastApproverLevel.HasValue)
                {
                    string approverEmail = await _emailService.GetApproverEmails(model.employeeNo, leastApproverLevel.Value);

                    if (!string.IsNullOrWhiteSpace(approverEmail))
                    {
                        await _emailService.SendRequestEmailAsync("Offset Credit Request", requestorName, approverEmail, dateFrom, dateTo);
                    }
                }


                return Json(new { success = true, message = successMessage });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddOffsetCreditRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error adding Offset Credit Request: {ex.Message}" });
            }
        }

        // Update existing Offset Credit Request (Pending and Declined only)
        [HttpPost]
        public JsonResult UpdateOffsetCreditRequest(OffsetCreditRequestModel model)
        {
            try
            {
                var currentRecord = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT statusLevel4, employeeNo, approvedRenderOT FROM rq_cto WHERE id = @id AND isActive = 1",
                    new { model.id });

                if (currentRecord == null)
                    return Json(new { success = false, message = "Offset Credit Request not found!" });

                string currentStatus = currentRecord.statusLevel4;
                string recordEmployeeNo = currentRecord.employeeNo;
                double oldApprovedRenderOT = Convert.ToDouble(currentRecord.approvedRenderOT);

                if (!CanViewEmployee(recordEmployeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to modify this employee's offset credit request." });

                if (currentStatus != "Pending" && currentStatus != "Declined")
                    return Json(new { success = false, message = "Only pending or declined requests can be edited!" });

                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                if (model.overTimeDateIN == null || model.overTimeDateOUT == null)
                    return Json(new { success = false, message = "Offset Date In and Date Out are required!" });

                if (model.overTimeDateOUT < model.overTimeDateIN)
                    return Json(new { success = false, message = "Offset Date Out cannot be earlier than Offset Date In!" });

                // ── Compute from dates only ──────────────────────────────────────
                var (newApprovedRenderOT, newOffsetDays) = ComputeOffsetFromDates(
                    model.overTimeDateIN.Value, model.overTimeDateOUT.Value);

                model.approvedRenderOT = newApprovedRenderOT;

                // Old offset days (re-derive from stored approvedRenderOT using same formula)
                // approvedRenderOT was stored as days*9, so days = approvedRenderOT/9
                double oldDays = oldApprovedRenderOT / 9.0;

                var newStatus = currentStatus == "Declined" ? "Pending" : currentStatus;

                var sql = @"
                    UPDATE rq_cto 
                    SET employeeNo        = @employeeNo,
                        overTimeDateIN    = @overTimeDateIN,
                        overTimeIN        = '08:00:00',
                        overTimeDateOUT   = @overTimeDateOUT,
                        overTimeOUT       = '17:00:00',
                        approvedRenderOT  = @approvedRenderOT,
                        overTimeReason    = @overTimeReason,
                        remarks           = @remarks,
                        statusName        = @statusLevel,
                        statusLevel1      = @statusLevel,
                        statusLevel2      = @statusLevel,
                        statusLevel3      = @statusLevel,
                        statusLevel4      = @statusLevel,
                        dtLastModified    = NOW(),
                        lastModifiedByUser = @lastModifiedByUser,
                        dtStatus          = NOW(),
                        statusByUser      = @lastModifiedByUser,
                        dtStatusLevel1    = NOW(),
                        statusByLevel1    = @lastModifiedByUser,
                        dtStatusLevel2    = NOW(),
                        statusByLevel2    = @lastModifiedByUser,
                        dtStatusLevel3    = NOW(),
                        statusByLevel3    = @lastModifiedByUser,
                        dtStatusLevel4    = NOW(),
                        statusByLevel4    = @lastModifiedByUser
                    WHERE id = @id";

                _db.Execute(sql, new
                {
                    model.id,
                    model.employeeNo,
                    model.overTimeDateIN,
                    model.overTimeDateOUT,
                    model.approvedRenderOT,
                    overTimeReason = model.overTimeReason ?? "",
                    remarks = model.remarks ?? "",
                    statusLevel = newStatus,
                    lastModifiedByUser = EmployeeNo
                });

                // ── Reversal logic if days changed ───────────────────────────────
                if (Math.Abs(oldDays - newOffsetDays) > 0.001)
                {
                    RecordToLeaveLedger(model.employeeNo, model.id, -oldDays, "OFFSET CREDIT REVERSED - EDITED");
                    RecordToLeaveLedger(model.employeeNo, model.id, newOffsetDays, "OFFSET CREDIT EARNED");
                }

                _auditTrail.Log("rq_cto", model.id, "UPDATED",
                    $"Updated Offset Credit request for {model.employeeNo}: {model.overTimeDateIN:yyyy-MM-dd} to {model.overTimeDateOUT:yyyy-MM-dd} ({newOffsetDays:F2} days). Old: {oldDays:F2} days, New: {newOffsetDays:F2} days");

                var message = currentStatus == "Declined"
                    ? "Offset Credit Request updated successfully and status set back to Pending!"
                    : "Offset Credit Request updated successfully!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateOffsetCreditRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error updating Offset Credit Request: {ex.Message}" });
            }
        }

        // Approve Offset Credit Request - NO m_leave CHANGES (already recorded on creation)
        [HttpPost]
        public async Task<JsonResult> ApproveOffsetCreditRequest(int id, string approvedByUser)
        {
            try
            {
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, approvedRenderOT, statusLevel1, statusLevel2, statusLevel3, statusLevel4
                    FROM rq_cto
                    WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Offset Credit Request not found!" });

                if (request.statusLevel4 != "Pending")
                    return Json(new { success = false, message = "This request has already been finalised and cannot be approved again." });

                string employeeNo = (string)request.employeeNo;
                string actingUser = approvedByUser ?? EmployeeNo;
                bool isFull = HasFullAccess();

                int? approverLevel = null;

                if (!isFull)
                {
                    approverLevel = await _approverService
                        .GetApproverLevelForEmployeeAsync(EmployeeNo, employeeNo);

                    if (approverLevel == null)
                        return Json(new { success = false, message = "Access denied. You are not an authorised approver for this employee." });
                }

                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();
                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                var approvedLevels = new List<int>();
                if ((string)request.statusLevel1 == "Approved" && requiredLevels.Contains(1))
                    approvedLevels.Add(1);
                if ((string)request.statusLevel2 == "Approved" && requiredLevels.Contains(2))
                    approvedLevels.Add(2);
                if ((string)request.statusLevel3 == "Approved" && requiredLevels.Contains(3))
                    approvedLevels.Add(3);

                int actingLevel;

                if (isFull)
                {
                    actingLevel = requiredLevels
                        .Where(l => !approvedLevels.Contains(l))
                        .OrderBy(l => l)
                        .FirstOrDefault();

                    if (actingLevel == 0)
                        return Json(new { success = false, message = "All approval levels have already been satisfied." });
                }
                else
                {
                    actingLevel = approverLevel!.Value;
                }

                if (actingLevel != 4)
                {
                    var lowerPending = requiredLevels
                        .Where(l => l < actingLevel && !approvedLevels.Contains(l))
                        .OrderBy(l => l)
                        .ToList();

                    if (lowerPending.Any())
                        return Json(new { success = false, message = $"This request must first be approved by Level {lowerPending.First()} before Level {actingLevel} can act." });
                }

                if (approvedLevels.Contains(actingLevel))
                    return Json(new { success = false, message = $"Level {actingLevel} has already approved this request." });

                var newlyApproved = actingLevel == 4
                    ? new List<int>(requiredLevels)
                    : new List<int>(approvedLevels) { actingLevel };

                int highestRequired = requiredLevels.Max();
                bool isFullyApproved = requiredLevels.All(l => newlyApproved.Contains(l));

                var setParts = new List<string>();

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

                if (isFullyApproved && highestRequired < 4 && actingLevel != 4)
                {
                    setParts.Add("statusLevel4   = 'Approved'");
                    setParts.Add("dtStatusLevel4 = NOW()");
                    setParts.Add("statusByLevel4 = @actingUser");
                }

                setParts.Add("statusName         = CASE WHEN statusLevel4 = 'Approved' THEN 'Approved' ELSE statusName END");
                setParts.Add("dtStatus           = NOW()");
                setParts.Add("statusByUser       = @actingUser");
                setParts.Add("dtLastModified     = NOW()");
                setParts.Add("lastModifiedByUser = @actingUser");

                // Check if employee rendered on the requested date
                //var renderData = await _db.QueryFirstOrDefaultAsync(@"
                //    SELECT 
                //        r.id,
                //        r.employeeNo,
                //        r.overTimeDateIN,
                //        r.overTimeIN,
                //        r.overTimeDateOUT,
                //        r.overTimeOUT,
                //        r.approvedRenderOT,

                //        s.timeIn AS schedTimeIn,
                //        s.timeOut AS schedTimeOut,

                //        b.biometricsTimeIn,
                //        b.biometricsTimeOut,
                //        b.biometricsDate,
                //        b.biometricsDateOut,

                //        TIMESTAMP(r.overTimeDateIN, r.overTimeIN) AS requestDateTimeIn,
                //        TIMESTAMP(r.overTimeDateOUT, r.overTimeOUT) AS requestDateTimeOut,
                //        TIMESTAMP(b.biometricsDate, b.biometricsTimeIn) AS actualDateTimeIn,
                //        TIMESTAMP(IFNULL(b.biometricsDateOut, b.biometricsDate), b.biometricsTimeOut) AS actualDateTimeOut
                //    FROM rq_cto r
                //    LEFT JOIN e_schedule s
                //        ON s.employeeNo = r.employeeNo
                //        AND r.overTimeDateIN BETWEEN s.effectivityDate 
                //                                AND IFNULL(s.effectivityDateTo, '2999-12-31')
                //        AND s.weekdayName = DAYNAME(r.overTimeDateIN)
                //    LEFT JOIN u_biometrics b
                //        ON b.employeeNo = r.employeeNo
                //        AND b.biometricsDate = r.overTimeDateIN
                //    WHERE r.id = @id
                //", new { id });

                //if (renderData != null && renderData.actualDateTimeIn != null && renderData.actualDateTimeOut != null)
                //{
                //    DateTime requestIn = renderData.requestDateTimeIn;
                //    DateTime requestOut = renderData.requestDateTimeOut;
                //    DateTime actualIn = renderData.actualDateTimeIn;
                //    DateTime actualOut = renderData.actualDateTimeOut;

                //    if (actualOut <= actualIn)
                //        actualOut = actualOut.AddDays(1);

                //    if (requestOut <= requestIn)
                //        requestOut = requestOut.AddDays(1);

                //    if (actualIn > requestIn || actualOut < requestOut)
                //    {
                //        string requested = $"{requestIn:MM/dd/yyyy hh:mm tt} - {requestOut:MM/dd/yyyy hh:mm tt}";
                //        string rendered = $"{actualIn:MM/dd/yyyy hh:mm tt} - {actualOut:MM/dd/yyyy hh:mm tt}";

                //        return Json(new
                //        {
                //            success = false,
                //            message = $"Request cannot be approved.\nRequested: {requested}\nRendered: {rendered}",
                //            isFullyApproved,
                //            actingLevel
                //        });
                //    }
                //}

                var updateSql = $"UPDATE rq_cto SET {string.Join(", ", setParts)} WHERE id = @id";
                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                if (isFullyApproved)
                    NotifyRequestAction("offsetCredit", id, employeeNo, "approved");
                else
                {
                    int nextLevel = requiredLevels
                        .Where(l => !newlyApproved.Contains(l))
                        .OrderBy(l => l)
                        .First();
                    NotifyNextApprover("offsetCredit", id, employeeNo, nextLevel);
                }

                double approvedHours = Convert.ToDouble(request.approvedRenderOT);
                double ctoDays = approvedHours / 9.0; // days = approvedRenderOT / 9

                var auditMsg = isFullyApproved
                    ? $"Offset Credit request fully approved at Level {actingLevel} by {actingUser} for {ctoDays:F2} days. Note: CTO credits were already recorded when request was created."
                    : $"Offset Credit request partially approved at Level {actingLevel} by {actingUser}. Awaiting higher level approval.";

                _auditTrail.Log("rq_cto", id, "APPROVED", auditMsg);

                var successMessage = isFullyApproved
                    ? "Offset Credit Request approved successfully!"
                    : $"Level {actingLevel} approval recorded. Request is now pending the next approver.";

                string employeeEmail = _emailService.GetEmployeeEmail(request.employeeNo).ToString();
                string dateFrom = request.overTimeDateIN?.ToString("yyyy-MM-dd") + " " + request.overTimeIN?.ToString(@"hh\:mm\:ss");
                string dateTo = request.overTimeDateOUT?.ToString("yyyy-MM-dd") + " " + request.overTimeOUT?.ToString(@"hh\:mm\:ss");

                _emailService.SendRequestStatusEmailAsync("Offset Credit Request Status", employeeEmail, request.statusLevel1, request.statusLevel2,
                    request.statusLevel3, request.statusLevel4, dateFrom, dateTo);

                return Json(new { success = true, message = successMessage, isFullyApproved, actingLevel });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ApproveOffsetCreditRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while approving the request." });
            }
        }

        // Decline Offset Credit Request - REVERSES the m_leave entry
        [HttpPost]
        public async Task<JsonResult> DeclineOffsetCreditRequest(int id, string declinedByUser, string reason = "")
        {
            try
            {
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, approvedRenderOT, statusLevel1, statusLevel2, statusLevel3, statusLevel4
                    FROM rq_cto
                    WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Offset Credit Request not found!" });

                if (request.statusLevel4 == "Cancelled" || request.statusLevel4 == "Processed")
                    return Json(new { success = false, message = "Cancelled or processed requests cannot be declined!" });

                string employeeNo = (string)request.employeeNo;
                string actingUser = declinedByUser ?? EmployeeNo;
                bool isFull = HasFullAccess();

                int? approverLevel = null;

                if (!isFull)
                {
                    approverLevel = await _approverService
                        .GetApproverLevelForEmployeeAsync(EmployeeNo, employeeNo);

                    if (approverLevel == null)
                        return Json(new { success = false, message = "Access denied. You are not an authorised approver for this employee." });
                }

                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();
                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

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
                        return Json(new { success = false, message = $"This request must first be approved by Level {lowerPending.First()} before Level {actingLevel} can act." });
                }

                var setParts = new List<string>();

                if (actingLevel == 4)
                {
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

                setParts.Add("statusLevel4       = 'Declined'");
                setParts.Add("dtStatusLevel4     = NOW()");
                setParts.Add("statusByLevel4     = @actingUser");
                setParts.Add("statusName         = 'Declined'");
                setParts.Add("dtStatus           = NOW()");
                setParts.Add("statusByUser       = @actingUser");
                setParts.Add("dtLastModified     = NOW()");
                setParts.Add("lastModifiedByUser = @actingUser");

                var updateSql = $"UPDATE rq_cto SET {string.Join(", ", setParts)} WHERE id = @id";
                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // Reverse CTO ledger
                double rawHours = Convert.ToDouble(request.approvedRenderOT);
                double offsetDays = rawHours / 9.0; // days = approvedRenderOT / 9

                RecordToLeaveLedger(employeeNo, id, -offsetDays, "OFFSET CREDIT REVERSED - DECLINED");
                NotifyRequestAction("offsetCredit", id, employeeNo, "declined");

                _auditTrail.Log("rq_cto", id, "DECLINED",
                    $"Declined Offset Credit request at Level {actingLevel} by {actingUser}. " +
                    $"Reversed {offsetDays:F2} days from CTO balance." +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}"));

                return Json(new { success = true, message = "Offset Credit Request declined successfully and CTO credits reversed!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeclineOffsetCreditRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while declining the request." });
            }
        }

        // Cancel Offset Credit Request - REVERSES the m_leave entry
        [HttpPost]
        public async Task<JsonResult> CancelOffsetCreditRequest(int id, string cancelledByUser, string reason = "")
        {
            try
            {
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT employeeNo, approvedRenderOT, statusLevel4 FROM rq_cto WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Offset Credit Request not found!" });

                if (request.employeeNo != EmployeeNo && !HasFullAccess())
                    return Json(new { success = false, message = "Access denied. You can only cancel your own requests." });

                if (request.statusLevel4 == "Approved")
                    return Json(new { success = false, message = "Approved requests cannot be cancelled!" });

                if (request.statusLevel4 == "Cancelled")
                    return Json(new { success = false, message = "Request is already cancelled!" });

                double rawHours = Convert.ToDouble(request.approvedRenderOT);
                double offsetDays = rawHours / 9.0; // days = approvedRenderOT / 9

                var sql = @"
                    UPDATE rq_cto 
                    SET statusName         = 'Cancelled',
                        statusLevel1       = 'Cancelled',
                        statusLevel2       = 'Cancelled',
                        statusLevel3       = 'Cancelled',
                        statusLevel4       = 'Cancelled',
                        dtStatus           = NOW(),
                        statusByUser       = @cancelledByUser,
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

                RecordToLeaveLedger(request.employeeNo, id, -offsetDays, "OFFSET CREDIT REVERSED - CANCELLED");
                NotifyRequestAction("offsetCredit", id, request.employeeNo, "cancelled");

                _auditTrail.Log("rq_cto", id, "CANCELLED",
                    $"Cancelled Offset Credit request by {cancelledByUser ?? EmployeeNo}. Reversed {offsetDays:F2} days from CTO balance.{(string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}")}");

                return Json(new { success = true, message = "Offset Credit Request cancelled successfully and CTO credits reversed!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CancelOffsetCreditRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while cancelling the request." });
            }
        }

        // HELPER METHOD: Record to m_leave ledger
        private void RecordToLeaveLedger(string employeeNo, int requestId, double accrualDays, string statusDescription)
        {
            try
            {
                var latestRow = _db.QueryFirstOrDefault<dynamic>(
                    @"SELECT availableBalance, accrual, usedCredits
                      FROM m_leave 
                      WHERE employeeNo = @EmployeeNo AND leaveCode = 'CTO' AND isActive = 1
                      ORDER BY id DESC LIMIT 1",
                    new { EmployeeNo = employeeNo });

                double beginningBalance = latestRow != null ? Convert.ToDouble(latestRow.availableBalance) : 0;
                double newAvailableBalance = beginningBalance + accrualDays;

                // ── Only set dtDeleted for OFFSET CREDIT EARNED (expires in 3 months) ──
                bool isEarned = statusDescription == "OFFSET CREDIT EARNED";

                var insertSql = @"
                    INSERT INTO m_leave (
                        employeeNo, rq_leaveID, leaveCode, statusName,
                        beginningBalance, accrual, usedCredits, availableBalance,
                        isActive, dtAdded, addedByUser, dtDeleted
                    )
                    VALUES (
                        @EmployeeNo, @RqCtoID, 'CTO', @StatusName,
                        @BeginningBalance, @Accrual, 0, @AvailableBalance,
                        1, NOW(), @UserCode,
                        @DtDeleted
                    )";

                _db.Execute(insertSql, new
                {
                    EmployeeNo = employeeNo,
                    RqCtoID = requestId,
                    StatusName = statusDescription,
                    BeginningBalance = beginningBalance,
                    Accrual = accrualDays,
                    AvailableBalance = newAvailableBalance,
                    UserCode = EmployeeNo,
                    // If OFFSET CREDIT EARNED → set expiry 3 months from now, else NULL
                    DtDeleted = isEarned ? DateTime.Now.AddMonths(3) : (DateTime?)null
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RecordToLeaveLedger: {ex.Message}");
                throw;
            }
        }
    }
}