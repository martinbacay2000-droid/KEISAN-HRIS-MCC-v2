using Dapper;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.UndertimeRequest
{
    [ModuleAuthorize("RundertimeM")]
    public class UndertimeRequestController : TimekeepingRequestBaseController
    {
        public UndertimeRequestController(
            IDbConnection db,
            IAuditTrailService auditTrail,
            IApproverService approverService)
            : base(db, auditTrail, "RundertimeM")
        {
            _approverService = approverService;
        }

        public IActionResult Index()
        {
            return View("~/Views/Timekeeping/UndertimeRequest.cshtml");
        }

        // Get all active Undertime Requests with filters AND DATA SCOPE
        [HttpGet]
        public async Task<JsonResult> GetUndertimeRequestList(string status, string branch, string department, string dateFrom, string dateTo)
        {
            var approverInfo = await GetApproverInfoCachedAsync();
            var hasFullAccess = HasFullAccess();
            var hasBroadScope = HasBroadDataScope();

            var query = new StringBuilder();
            var parameters = new DynamicParameters();

            const string selectBlock = @"
                SELECT 
                    rq.id, rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS employeeName,
                    DATE_FORMAT(rq.undertimeDateIN, '%m/%d/%Y') as displayDateIn,
                    DATE_FORMAT(rq.undertimeDateOUT, '%m/%d/%Y') as displayDateOut,
                    TIME_FORMAT(rq.undertimeTimeOUT, '%H:%i') as displayTimeOut,
                    rq.undertimeReason,
                    rq.statusLevel1,
                    rq.statusLevel2,
                    rq.statusLevel3,
                    rq.statusLevel4,
                    rq.statusName,
                    rq.remarks,
                    rq.dtAdded,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                FROM rq_undertime rq
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
                query.Append(" AND rq.statusName = @status");
                parameters.Add("@status", "Pending");
            }
            else if (!status.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                query.Append(" AND rq.statusName = @status");
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
                    query.Append(" AND rq.undertimeDateIN BETWEEN @dateFrom AND @dateTo");
                    parameters.Add("@dateFrom", parsedFrom.ToString("yyyy-MM-dd"));
                    parameters.Add("@dateTo", parsedTo.ToString("yyyy-MM-dd"));
                }
            }

            query.Append(" ORDER BY rq.id DESC");

            await MarkRequestsAsProcessedAsync("rq_undertime", "undertimeDateIN", "statusName", alsoUpdateStatusName: false);

            var requests = await _db.QueryAsync<dynamic>(query.ToString(), parameters);
            return Json(new { data = requests });
        }

        // Get single Undertime Request by ID
        [HttpGet]
        public async Task<JsonResult> GetUndertimeRequest(int id)
        {
            try
            {
                var employeeNo = _db.QueryFirstOrDefault<string>(
                    "SELECT employeeNo FROM rq_undertime WHERE id = @id AND isActive = 1",
                    new { id });

                if (string.IsNullOrEmpty(employeeNo))
                    return Json(new { error = "Undertime Request not found!" });

                if (!CanViewEmployee(employeeNo))
                    return Json(new
                    {
                        error = "Access denied. You don't have permission to view this employee's undertime request."
                    });

                var sql = @"
                    SELECT
                        rq.id,
                        rq.employeeNo,
                        DATE_FORMAT(rq.undertimeDateIN,  '%m/%d/%Y') AS displayDateIn,
                        DATE_FORMAT(rq.undertimeDateOUT, '%m/%d/%Y') AS displayDateOut,
                        TIME_FORMAT(rq.undertimeTimeOUT, '%H:%i')    AS displayTimeOut,
                        rq.undertimeReason,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusName,
                        rq.remarks,
                        CONCAT(IFNULL(e.firstName,''),   ' ',
                               IFNULL(CONCAT(e.middleName,   ' '),''),
                               IFNULL(e.lastName,''))   AS employeeName,
                        CONCAT(IFNULL(req.firstName,''), ' ',
                               IFNULL(CONCAT(req.middleName, ' '),''),
                               IFNULL(req.lastName,'')) AS requestedByUser
                    FROM rq_undertime rq
                    LEFT JOIN e_basicinfo e   ON e.employeeNo   = rq.employeeNo
                    LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                    WHERE rq.id = @Id AND rq.isActive = 1";

                var request = _db.QueryFirstOrDefault<dynamic>(sql, new { Id = id });

                if (request == null) return Json(null);

                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();
                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                bool canCurrentUserApprove = false;
                int? currentUserLevel = null;

                string statusLevel1 = (string)request.statusLevel1 ?? "Pending";
                string statusLevel2 = (string)request.statusLevel2;
                string statusLevel3 = (string)request.statusLevel3;
                string statusLevel4 = (string)request.statusLevel4 ?? "Pending";
                string statusName = (string)request.statusName;

                bool isFull = HasFullAccess();

                if (isFull)
                {
                    canCurrentUserApprove = statusName == "Pending";
                }
                else
                {
                    currentUserLevel = await _approverService
                        .GetApproverLevelForEmployeeAsync(EmployeeNo, employeeNo);

                    if (currentUserLevel.HasValue && statusName == "Pending")
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
                                4 => statusName == "Approved",
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
                    displayTimeOut = (string)request.displayTimeOut,
                    undertimeReason = (string)request.undertimeReason,
                    remarks = (string)request.remarks,
                    statusLevel1,
                    statusLevel2,
                    statusLevel3,
                    statusLevel4,
                    statusName,
                    requiredLevels,
                    canCurrentUserApprove,
                    currentUserLevel
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetUndertimeRequest: {ex.Message}");
                return Json(null);
            }
        }

        // Add new Undertime Request
        [HttpPost]
        public JsonResult AddUndertimeRequest(UndertimeRequestModel model)
        {
            try
            {
                // Security check using base method
                if (!CanViewEmployee(model.employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to create undertime requests for this employee." });
                }

                // Validate employee exists using base method
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Validate dates are provided
                if (model.undertimeDateIN == null || model.undertimeDateOUT == null)
                    return Json(new { success = false, message = "Date In and Date Out are required!" });

                // Validate date logic
                if (model.undertimeDateOUT < model.undertimeDateIN)
                    return Json(new { success = false, message = "Date Out cannot be earlier than Date In!" });

                // Duplicate filing check — block overlapping Undertime requests
                var duplicate = _db.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) FROM rq_undertime
                    WHERE employeeNo = @employeeNo
                    AND isActive = 1
                    AND statusName NOT IN ('Cancelled', 'Declined')
                    AND undertimeDateIN  <= @undertimeDateOUT
                    AND undertimeDateOUT >= @undertimeDateIN",
                    new { model.employeeNo, model.undertimeDateIN, model.undertimeDateOUT });

                if (duplicate > 0)
                    return Json(new { success = false, message = "An undertime request already exists for the selected date range." });

                // Validate time out
                if (model.undertimeTimeOUT == null)
                    return Json(new { success = false, message = "Time Out is required!" });

                // Validate reason
                if (string.IsNullOrWhiteSpace(model.undertimeReason))
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

                // ── Insert new Undertime Request ──────────────────────────────────────
                // Initial status is always Pending on insert; we update it right after
                // for Level 4 approvers so the row is never left in a dangling state.
                var sql = @"
                    INSERT INTO rq_undertime 
                    (employeeNo, undertimeDateIN, undertimeDateOUT, undertimeTimeOUT, undertimeReason, 
                     statusName, statusLevel1, statusLevel2, statusLevel3, statusLevel4, remarks, isActive, dtAdded, addedByUser, 
                     requestedByUser, dtStatus, statusByUser, dtStatusLevel1, statusByLevel1,
                     dtStatusLevel2, statusByLevel2, 
                     dtStatusLevel3, statusByLevel3, dtStatusLevel4, statusByLevel4) 
                    VALUES 
                    (@employeeNo, @undertimeDateIN, @undertimeDateOUT, @undertimeTimeOUT, @undertimeReason,
                     'Pending', 'Pending', 'Pending', 'Pending', 'Pending', @remarks, 1, NOW(), @addedByUser, @requestedByUser, 
                     NOW(), @addedByUser, NOW(), @addedByUser,
                     NOW(), @addedByUser, 
                     NOW(), @addedByUser, NOW(), @addedByUser);
                    SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    model.employeeNo,
                    model.undertimeDateIN,
                    model.undertimeDateOUT,
                    model.undertimeTimeOUT,
                    undertimeReason = model.undertimeReason?.Trim() ?? "",
                    remarks = model.remarks?.Trim() ?? "",
                    addedByUser = EmployeeNo,
                    requestedByUser = EmployeeNo
                });

                // ── Auto-approve for Level 4 approvers ───────────────────────────────
                if (isLevel4Approver)
                {
                    // Level 4 bypass: all status levels are set to Approved immediately.
                    // NOTE: rq_undertime uses statusName (not statusLevel4) as the final gate,
                    //       and dtLastModifiedByUser (not dtLastModified) for the audit column.
                    _db.Execute(@"
                        UPDATE rq_undertime
                        SET statusLevel1         = 'Approved',
                            dtStatusLevel1       = NOW(),
                            statusByLevel1       = @approvedBy,
                            statusLevel2         = 'Approved',
                            dtStatusLevel2       = NOW(),
                            statusByLevel2       = @approvedBy,
                            statusLevel3         = 'Approved',
                            dtStatusLevel3       = NOW(),
                            statusByLevel3       = @approvedBy,
                            statusLevel4         = 'Approved',
                            dtStatusLevel4       = NOW(),
                            statusByLevel4       = @approvedBy,
                            statusName           = 'Approved',
                            dtStatus             = NOW(),
                            statusByUser         = @approvedBy,
                            dtLastModifiedByUser = NOW(),
                            lastModifiedByUser   = @approvedBy
                        WHERE id = @id",
                        new { id = newId, approvedBy = model.employeeNo });

                    // Notify the requestor that their request was auto-approved
                    NotifyRequestAction("undertime", newId, model.employeeNo, "approved");

                    // Distinct audit message so it's clear this was not a manual approval
                    _auditTrail.Log("rq_undertime", newId, "AUTO-APPROVED",
                        $"Undertime request auto-approved on creation: {model.employeeNo} is a Level 4 approver. " +
                        $"Period: {model.undertimeDateIN:yyyy-MM-dd} to {model.undertimeDateOUT:yyyy-MM-dd}");
                }
                else
                {
                    // Standard flow — notify approvers that a request is pending
                    NotifyRequestAction("undertime", newId, model.employeeNo, "pending");

                    _auditTrail.Log("rq_undertime", newId, "CREATED",
                        $"Added undertime request for {model.employeeNo}: {model.undertimeDateIN:yyyy-MM-dd} to {model.undertimeDateOUT:yyyy-MM-dd}");
                }

                var successMessage = isLevel4Approver
                    ? "Undertime Request filed and automatically approved."
                    : "Undertime Request added successfully!";

                return Json(new { success = true, message = successMessage });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddUndertimeRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error adding Undertime Request: {ex.Message}" });
            }
        }

        // Update existing Undertime Request (Pending and Declined only)
        [HttpPost]
        public JsonResult UpdateUndertimeRequest(UndertimeRequestModel model)
        {
            try
            {
                // Check if record exists and get current status
                var currentRecord = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT statusName, employeeNo FROM rq_undertime WHERE id = @id AND isActive = 1",
                    new { model.id });

                if (currentRecord == null)
                    return Json(new { success = false, message = "Undertime Request not found!" });

                string currentStatus = currentRecord.statusName;
                string recordEmployeeNo = currentRecord.employeeNo;

                // Security check using base method
                if (!CanViewEmployee(recordEmployeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to modify this employee's undertime request." });
                }

                // Only allow editing Pending or Declined requests
                if (currentStatus != "Pending" && currentStatus != "Declined")
                    return Json(new { success = false, message = "Only pending or declined requests can be edited!" });

                // Validate employee exists using base method
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Validate date logic
                if (model.undertimeDateOUT < model.undertimeDateIN)
                    return Json(new { success = false, message = "Date Out cannot be earlier than Date In!" });

                // Validate time out
                if (model.undertimeTimeOUT == null)
                    return Json(new { success = false, message = "Time Out is required!" });

                // Validate reason
                if (string.IsNullOrWhiteSpace(model.undertimeReason))
                    return Json(new { success = false, message = "Reason is required!" });

                // If status was Declined, set back to Pending when edited
                var newStatus = currentStatus == "Declined" ? "Pending" : currentStatus;

                // Update undertime request with all status levels
                var sql = @"
                    UPDATE rq_undertime 
                    SET employeeNo = @employeeNo, undertimeDateIN = @undertimeDateIN,
                        undertimeDateOUT = @undertimeDateOUT, undertimeTimeOUT = @undertimeTimeOUT,
                        undertimeReason = @undertimeReason, remarks = @remarks,
                        statusName = @statusName, statusLevel1 = @statusName, statusLevel2 = @statusName, 
                        statusLevel3 = @statusName, statusLevel4 = @statusName,
                        dtLastModifiedByUser = NOW(), lastModifiedByUser = @lastModifiedByUser,
                        dtStatus = NOW(), statusByUser = @lastModifiedByUser,
                        dtStatusLevel1 = NOW(), statusByLevel1 = @lastModifiedByUser,
                        dtStatusLevel2 = NOW(), statusByLevel2 = @lastModifiedByUser,
                        dtStatusLevel3 = NOW(), statusByLevel3 = @lastModifiedByUser,
                        dtStatusLevel4 = NOW(), statusByLevel4 = @lastModifiedByUser
                    WHERE id = @id";

                _db.Execute(sql, new
                {
                    model.id,
                    model.employeeNo,
                    model.undertimeDateIN,
                    model.undertimeDateOUT,
                    model.undertimeTimeOUT,
                    undertimeReason = model.undertimeReason?.Trim() ?? "",
                    remarks = model.remarks?.Trim() ?? "",
                    statusName = newStatus,
                    lastModifiedByUser = EmployeeNo
                });

                // Log to audit trail
                _auditTrail.Log("rq_undertime", model.id, "UPDATED",
                    $"Updated undertime request for {model.employeeNo}: {model.undertimeDateIN:yyyy-MM-dd} to {model.undertimeDateOUT:yyyy-MM-dd}");

                // Return appropriate success message
                var message = currentStatus == "Declined"
                    ? "Undertime Request updated successfully and status set back to Pending!"
                    : "Undertime Request updated successfully!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateUndertimeRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error updating Undertime Request: {ex.Message}" });
            }
        }

        // Approve Undertime Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> ApproveUndertimeRequest(int id, string approvedByUser)
        {
            try
            {
                // ── 1. Load the request ──────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, statusLevel1, statusLevel2, statusLevel3, statusLevel4, statusName
                    FROM rq_undertime
                    WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Undertime Request not found!" });

                if (request.statusName != "Pending")
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
                if ((string)request.statusLevel4 == "Approved" && requiredLevels.Contains(4))
                    approvedLevels.Add(4);

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
                // rq_undertime column mapping:
                //   Level 2 → statusLevel2
                //   Level 3 → statusLevel3
                //   Level 4 → statusName  (final gate on rq_undertime)
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
                        setParts.Add("statusName     = 'Approved'");
                        setParts.Add("dtStatus       = NOW()");
                        setParts.Add("statusByUser   = @actingUser");
                        break;
                }

                // If fully approved and highest required < 4, cascade final gate
                if (isFullyApproved && highestRequired < 4 && actingLevel != 4)
                {
                    setParts.Add("statusLevel4 = 'Approved'");
                    setParts.Add("dtStatusLevel4 = NOW()");
                    setParts.Add("statusByLevel4 = @actingUser");
                    setParts.Add("statusName   = 'Approved'");
                    setParts.Add("dtStatus     = NOW()");
                    setParts.Add("statusByUser = @actingUser");
                }

                setParts.Add("dtLastModifiedByUser = NOW()");
                setParts.Add("lastModifiedByUser   = @actingUser");

                var updateSql =
                    $"UPDATE rq_undertime SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // ── 10. Notify ───────────────────────────────────────────────────
                if (isFullyApproved)
                {
                    NotifyRequestAction("undertime", id, employeeNo, "approved");
                }
                else
                {
                    int nextLevel = requiredLevels
                        .Where(l => !newlyApproved.Contains(l))
                        .OrderBy(l => l)
                        .First();

                    NotifyNextApprover("undertime", id, employeeNo, nextLevel);
                }

                // ── 11. Audit ────────────────────────────────────────────────────
                var auditMsg = isFullyApproved
                    ? $"Undertime request fully approved at Level {actingLevel} by {actingUser}"
                    : $"Undertime request partially approved at Level {actingLevel} by {actingUser}. " +
                      $"Awaiting higher level approval.";

                _auditTrail.Log("rq_undertime", id, "APPROVED", auditMsg);

                var successMessage = isFullyApproved
                    ? "Undertime Request approved successfully!"
                    : $"Level {actingLevel} approval recorded. Request is now pending the next approver.";

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
                Console.WriteLine($"Error in ApproveUndertimeRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while approving the request." });
            }
        }

        // Decline Undertime Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> DeclineUndertimeRequest(int id, string declinedByUser, string reason = "")
        {
            try
            {
                // ── 1. Load request ──────────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, statusLevel1, statusLevel2, statusLevel3, statusLevel4, statusName
                    FROM rq_undertime
                    WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Undertime Request not found!" });

                if (request.statusName == "Cancelled" || request.statusName == "Processed")
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
                    // statusName (final gate) is set below
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

                // Final gate (statusName) always reflects the decline
                setParts.Add("statusLevel4         = 'Declined'");
                setParts.Add("dtStatusLevel4       = NOW()");
                setParts.Add("statusByLevel4       = @actingUser");
                setParts.Add("statusName           = 'Declined'");
                setParts.Add("dtStatus             = NOW()");
                setParts.Add("statusByUser         = @actingUser");
                setParts.Add("dtLastModifiedByUser = NOW()");
                setParts.Add("lastModifiedByUser   = @actingUser");

                var updateSql =
                    $"UPDATE rq_undertime SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // ── 7. Notify ────────────────────────────────────────────────────
                NotifyRequestAction("undertime", id, employeeNo, "declined");

                // ── 8. Audit ─────────────────────────────────────────────────────
                _auditTrail.Log("rq_undertime", id, "DECLINED",
                    $"Declined undertime request at Level {actingLevel} by {actingUser}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "Undertime Request declined successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeclineUndertimeRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while declining the request." });
            }
        }

        // Cancel Undertime Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> CancelUndertimeRequest(int id, string cancelledByUser, string reason = "")
        {
            try
            {
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT employeeNo, statusName FROM rq_undertime WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "Undertime Request not found!" });

                // Only allow user to cancel their own request (or full access)
                if (request.employeeNo != EmployeeNo && !HasFullAccess())
                    return Json(new { success = false, message = "Access denied. You can only cancel your own requests." });

                if (request.statusName == "Approved")
                    return Json(new { success = false, message = "Approved requests cannot be cancelled!" });

                if (request.statusName == "Cancelled")
                    return Json(new { success = false, message = "Request is already cancelled!" });

                var sql = @"
                    UPDATE rq_undertime 
                    SET statusName = 'Cancelled',
                        statusLevel1 = 'Cancelled',
                        statusLevel2 = 'Cancelled',
                        statusLevel3 = 'Cancelled',
                        statusLevel4 = 'Cancelled',
                        dtStatus = NOW(), statusByUser = @cancelledByUser,
                        dtStatusLevel1 = NOW(), statusByLevel1 = @cancelledByUser,
                        dtStatusLevel2 = NOW(), statusByLevel2 = @cancelledByUser,
                        dtStatusLevel3 = NOW(), statusByLevel3 = @cancelledByUser,
                        dtStatusLevel4 = NOW(), statusByLevel4 = @cancelledByUser,
                        dtLastModifiedByUser = NOW(), lastModifiedByUser = @cancelledByUser
                    WHERE id = @id";

                await _db.ExecuteAsync(sql, new { id, cancelledByUser = cancelledByUser ?? EmployeeNo });

                NotifyRequestAction("undertime", id, request.employeeNo, "cancelled");

                _auditTrail.Log("rq_undertime", id, "CANCELLED",
                    $"Cancelled undertime request by {cancelledByUser ?? EmployeeNo}{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Undertime Request cancelled successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CancelUndertimeRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while cancelling the request." });
            }
        }
    }
}