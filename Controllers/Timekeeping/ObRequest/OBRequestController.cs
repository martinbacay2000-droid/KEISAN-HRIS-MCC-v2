using Dapper;
using KEISAN_HRIS_v2.Models.Timekeeping;
using Microsoft.AspNetCore.Mvc;
using KEISAN_HRIS_v2.Security;
using System.Data;
using System.Text;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using KEISAN_HRIS_v2.Services.OtherServices;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.ObRequest
{
    [ModuleAuthorize("RofficialbusinessM")]
    public class OBRequestController : TimekeepingRequestBaseController
    {
        public OBRequestController(
            IDbConnection db,
            IAuditTrailService auditTrail,
            IEmailService emailService,
            IApproverService approverService)
            : base(db, auditTrail, "RofficialbusinessM")
        {
            _approverService = approverService;
            _emailService = (IEmailService)emailService;
        }

        public IActionResult Index(string status = null)
        {
            ViewBag.FilterStatus = status;
            return View("~/Views/Timekeeping/OBRequest.cshtml");
        }

        [HttpGet]
        public async Task<JsonResult> GetOBRequestList(
            string status, string branch, string department,
            string dateFrom, string dateTo)
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
                    CONCAT(b.lastName, ', ', b.firstName, ' ',
                           LEFT(IFNULL(b.middleName,''), 1), '.') AS employeeName,
                    DATE_FORMAT(rq.obDateIn,  '%m/%d/%Y') AS displayDateIn,
                    TIME_FORMAT(rq.obTimeIn,  '%H:%i')    AS displayTimeIn,
                    DATE_FORMAT(rq.obDateOut, '%m/%d/%Y') AS displayDateOut,
                    TIME_FORMAT(rq.obTimeOut, '%H:%i')    AS displayTimeOut,
                    rq.obReason,
                    rq.statusLevel1,
                    rq.statusLevel2,
                    rq.statusLevel3,
                    rq.statusLevel4,
                    rq.statusLevel4 AS statusName,
                    rq.remarks,
                    rq.dtAdded,
                    CONCAT(req.lastName, ', ', req.firstName, ' ',
                           LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                FROM rq_officialbusiness rq
                INNER JOIN e_basicinfo b   ON b.employeeNo   = rq.employeeNo
                LEFT  JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser";

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
                                    WHEN 2 THEN b2.branchCode       = a.employeeNo
                                    WHEN 3 THEN b2.departmentCode   = a.employeeNo
                                    WHEN 4 THEN b2.positionCode     = a.employeeNo
                                    WHEN 5 THEN a.employeeNo        = 'ALL'
                                    WHEN 6 THEN b2.employeeNo       = a.employeeNo
                                    ELSE FALSE
                                END
                            )
                            WHERE a.approverNo = @currentEmployeeNo
                            AND   a.isActive   = 1
                            AND   b2.isActive  = 1
                        )
                    )");
                parameters.Add("@currentEmployeeNo", EmployeeNo);
            }
            else
            {
                query.Append(selectBlock);
                query.Append(@"
                    WHERE rq.isActive   = 1
                    AND   rq.employeeNo = @currentEmployeeNo");
                parameters.Add("@currentEmployeeNo", EmployeeNo);
            }

            var hiddenEmployees = _db.QueryFirstOrDefault<string>(@"
                SELECT hiddenEmployees FROM s_role
                WHERE roleCode = @roleCode AND isActive = 1 LIMIT 1",
                        new { roleCode = RoleCode });

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
                    query.Append(" AND rq.obDateIn BETWEEN @dateFrom AND @dateTo");
                    parameters.Add("@dateFrom", parsedFrom.ToString("yyyy-MM-dd"));
                    parameters.Add("@dateTo", parsedTo.ToString("yyyy-MM-dd"));
                }
            }

            query.Append(" ORDER BY rq.id DESC");

            await MarkRequestsAsProcessedAsync("rq_officialbusiness", "obDateIn");

            var requests = await _db.QueryAsync<dynamic>(query.ToString(), parameters);
            return Json(new { data = requests });
        }

        // Get single OB Request by ID WITH SECURITY CHECK
        [HttpGet]
        public async Task<JsonResult> GetOBRequest(int id)
        {
            try
            {
                var sql = @"
                    SELECT 
                        rq.id,
                        rq.employeeNo,
                        DATE_FORMAT(rq.obDateIn,  '%m/%d/%Y') AS displayDateIn,
                        TIME_FORMAT(rq.obTimeIn,  '%H:%i')    AS displayTimeIn,
                        DATE_FORMAT(rq.obDateOut, '%m/%d/%Y') AS displayDateOut,
                        TIME_FORMAT(rq.obTimeOut, '%H:%i')    AS displayTimeOut,
                        rq.obReason,
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
                    FROM rq_officialbusiness rq
                    LEFT JOIN e_basicinfo e   ON e.employeeNo   = rq.employeeNo
                    LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                    WHERE rq.id = @Id AND rq.isActive = 1";

                var request = _db.QueryFirstOrDefault<dynamic>(sql, new { Id = id });

                if (request == null) return Json(null);

                string employeeNo = (string)request.employeeNo;

                if (!CanViewEmployee(employeeNo))
                    return Json(new
                    {
                        error = "Access denied. You don't have permission to view this OB request."
                    });

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
                string statusLevel4 = (string)request.statusLevel4;

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
                                .All(l =>
                                {
                                    return l switch
                                    {
                                        1 => statusLevel1 == "Approved",
                                        2 => statusLevel2 == "Approved",
                                        3 => statusLevel3 == "Approved",
                                        _ => false
                                    };
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
                    displayTimeIn = (string)request.displayTimeIn,
                    displayDateOut = (string)request.displayDateOut,
                    displayTimeOut = (string)request.displayTimeOut,
                    obReason = (string)request.obReason,
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
                Console.WriteLine($"Error in GetOBRequest: {ex.Message}");
                return Json(null);
            }
        }

        // Add new OB Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> AddOBRequest(OBRequestModel model, IFormFile attachment)
        {
            try
            {
                // Security check using base method
                if (!CanViewEmployee(model.employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to create an OB request for this employee." });
                }

                // Validate employee exists using base method
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Validate dates
                if (model.obDateIn == null || model.obDateOut == null)
                    return Json(new { success = false, message = "Date In and Date Out are required!" });

                if (model.obDateOut < model.obDateIn)
                    return Json(new { success = false, message = "Date Out cannot be earlier than Date In!" });

                // Duplicate filing check — block overlapping OB requests
                var duplicate = _db.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) FROM rq_officialbusiness
                    WHERE employeeNo = @employeeNo
                    AND isActive = 1
                    AND statusLevel4 NOT IN ('Cancelled', 'Declined')
                    AND obDateIn  <= @obDateOut
                    AND obDateOut >= @obDateIn",
                    new { model.employeeNo, model.obDateIn, model.obDateOut });

                if (duplicate > 0)
                    return Json(new { success = false, message = "An official business request already exists for the selected date range." });

                // ── Check if the requestor is a Level 4 approver ──────────────────────
                // If yes, we will auto-approve immediately after insert.
                bool isLevel4Approver = _db.QuerySingle<int>(@"
                    SELECT COUNT(*)
                    FROM e_approver
                    WHERE approverNo  = @employeeNo
                    AND   approverLevel = 4
                    AND   isActive    = 1",
                    new { model.employeeNo }) > 0;

                // ── Insert new OB Request ─────────────────────────────────────────────
                // Initial status is always Pending on insert; we update it right after
                // for Level 4 approvers so the row is never left in a dangling state.
                var sql = @"
                    INSERT INTO rq_officialbusiness 
                    (employeeNo, obDateIn, obTimeIn, obDateOut, obTimeOut, obReason, 
                     statusLevel1, statusLevel2, statusLevel3, statusLevel4, remarks, isActive, dtAdded, addedByUser, 
                     requestedByUser, dtStatus, statusByUser, dtStatusLevel1, statusByLevel1,
                     dtStatusLevel2, statusByLevel2, 
                     dtStatusLevel3, statusByLevel3, dtStatusLevel4, statusByLevel4) 
                    VALUES 
                    (@employeeNo, @obDateIn, @obTimeIn, @obDateOut, @obTimeOut, @obReason, 
                     'Pending', 'Pending', 'Pending', 'Pending', @remarks, 1, NOW(), @addedByUser, @requestedByUser, 
                     NOW(), @addedByUser, NOW(), @addedByUser,
                     NOW(), @addedByUser, 
                     NOW(), @addedByUser, NOW(), @addedByUser);
                    SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    model.employeeNo,
                    model.obDateIn,
                    obTimeIn = model.obTimeIn ?? TimeSpan.Parse("08:00:00"),
                    model.obDateOut,
                    obTimeOut = model.obTimeOut ?? TimeSpan.Parse("17:00:00"),
                    obReason = model.obReason ?? "",
                    remarks = model.remarks ?? "",
                    addedByUser = EmployeeNo ?? model.employeeNo,
                    requestedByUser = EmployeeNo ?? model.employeeNo
                });

                // ── Auto-approve for Level 4 approvers ───────────────────────────────
                if (isLevel4Approver)
                {
                    // Level 4 bypass: all status levels are set to Approved immediately.
                    _db.Execute(@"
                        UPDATE rq_officialbusiness
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
                    NotifyRequestAction("officialBusiness", newId, model.employeeNo, "approved");

                    // Distinct audit message so it's clear this was not a manual approval
                    _auditTrail.Log("rq_officialbusiness", newId, "AUTO-APPROVED",
                        $"OB request auto-approved on creation: {model.employeeNo} is a Level 4 approver. " +
                        $"Period: {model.obDateIn:yyyy-MM-dd} to {model.obDateOut:yyyy-MM-dd}");
                }
                else
                {
                    // Standard flow — notify approvers that a request is pending
                    NotifyRequestAction("officialBusiness", newId, model.employeeNo, "pending");

                    _auditTrail.Log("rq_officialbusiness", newId, "CREATED",
                        $"Added OB request for {model.employeeNo}: {model.obDateIn:yyyy-MM-dd} to {model.obDateOut:yyyy-MM-dd}");
                }

                // ── Handle attachment upload if provided ──────────────────────────────
                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"OB Request saved but attachment failed: {uploadResult.message}" });
                }

                var successMessage = isLevel4Approver
                    ? "Official Business Request filed and automatically approved."
                    : "Official Business Request added successfully!";

                string requestorName = _emailService.GetEmployeeNameAsync(model.employeeNo).ToString();
                string dateFrom = model.obDateIn?.ToString("yyyy-MM-dd") + " " + model.obTimeIn?.ToString(@"hh\:mm\:ss");
                string dateTo = model.obDateOut?.ToString("yyyy-MM-dd") + " " + model.obTimeOut?.ToString(@"hh\:mm\:ss");
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
                Console.WriteLine($"Error in AddOBRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error adding OB Request: {ex.Message}" });
            }
        }

        // Update existing OB Request WITH SECURITY CHECK
        [HttpPost]
        public JsonResult UpdateOBRequest(OBRequestModel model, IFormFile attachment)
        {
            try
            {
                // Check if record exists and get employee info
                var existingRequest = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo, statusLevel4 FROM rq_officialbusiness WHERE id = @id AND isActive = 1",
                    new { model.id });

                if (existingRequest == null)
                    return Json(new { success = false, message = "OB Request not found!" });

                // Security check using base method
                if (!CanViewEmployee(existingRequest.employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to update this OB request." });
                }

                string currentStatus = existingRequest.statusLevel4;

                // Only allow editing Pending or Declined requests
                if (currentStatus != "Pending" && currentStatus != "Declined")
                    return Json(new { success = false, message = "Only pending or declined requests can be edited!" });

                // Validate employee exists using base method
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Validate dates
                if (model.obDateOut < model.obDateIn)
                    return Json(new { success = false, message = "Date Out cannot be earlier than Date In!" });

                // If status was Declined, set back to Pending when edited
                var newStatus = currentStatus == "Declined" ? "Pending" : currentStatus;

                var sql = @"
                    UPDATE rq_officialbusiness 
                    SET employeeNo = @employeeNo,
                        obDateIn = @obDateIn,
                        obTimeIn = @obTimeIn,
                        obDateOut = @obDateOut,
                        obTimeOut = @obTimeOut,
                        obReason = @obReason,
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
                    model.obDateIn,
                    obTimeIn = model.obTimeIn ?? TimeSpan.Parse("08:00:00"),
                    model.obDateOut,
                    obTimeOut = model.obTimeOut ?? TimeSpan.Parse("17:00:00"),
                    obReason = model.obReason ?? "",
                    remarks = model.remarks ?? "",
                    statusLevel = newStatus,
                    lastModifiedByUser = EmployeeNo ?? model.employeeNo
                });

                // Log to audit trail
                _auditTrail.Log("rq_officialbusiness", model.id, "UPDATED",
                    $"Updated OB request for {model.employeeNo}: {model.obDateIn:yyyy-MM-dd} to {model.obDateOut:yyyy-MM-dd}");

                // Handle attachment upload if provided
                if (attachment != null && attachment.Length > 0)
                {
                    var uploadResult = SaveAttachment(model.employeeNo, attachment);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"OB Request updated but attachment failed: {uploadResult.message}" });
                }

                var message = currentStatus == "Declined"
                    ? "OB Request updated successfully and status set back to Pending!"
                    : "Official Business Request updated successfully!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateOBRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error updating OB Request: {ex.Message}" });
            }
        }

        /// Returns whether the currently logged-in user is an approver
        /// at the given level for the given employee.
        /// Used by the frontend to decide whether to show the Approve button.
        [HttpGet]
        public async Task<JsonResult> CanApproveAtLevel(string employeeNo, int level)
        {
            try
            {
                var sql = @"
                    SELECT COUNT(*)
                    FROM e_approver a
                    INNER JOIN e_basicinfo b ON (
                        CASE a.typeList
                            WHEN 1 THEN b.employmentStatus = a.employeeNo
                            WHEN 2 THEN b.branchCode       = a.employeeNo
                            WHEN 3 THEN b.departmentCode   = a.employeeNo
                            WHEN 4 THEN b.positionCode     = a.employeeNo
                            WHEN 5 THEN a.employeeNo       = 'ALL'
                            WHEN 6 THEN b.employeeNo       = a.employeeNo
                            ELSE FALSE
                        END
                    )
                    WHERE a.approverNo    = @currentUser
                    AND   b.employeeNo   = @employeeNo
                    AND   a.approverLevel = @level
                    AND   a.isActive      = 1";

                var count = await _db.ExecuteScalarAsync<int>(
                    sql,
                    new { currentUser = EmployeeNo, employeeNo, level });

                return Json(new { canApprove = count > 0 });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CanApproveAtLevel: {ex.Message}");
                return Json(new { canApprove = false });
            }
        }

        // Approve OB Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> ApproveOBRequest(int id, string approvedByUser)
        {
            try
            {
                // ── 1. Load the request ────────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    rq.employeeNo,
                    rq.statusLevel1,
                    rq.statusLevel2,
                    rq.statusLevel3,
                    rq.statusLevel4,
                    rq.obDateIn,
                    rq.obTimeIn,
                    rq.obDateOut,
                    rq.obTimeOut
                FROM rq_officialbusiness rq
                WHERE rq.id = @id AND rq.isActive = 1",
                new { id });

                if (request == null)
                    return Json(new { success = false, message = "OB Request not found!" });

                if (request.statusLevel4 != "Pending" &&
                    request.statusLevel4 != "Partially Approved")
                    return Json(new
                    {
                        success = false,
                        message = "This request has already been finalised and cannot be approved again."
                    });

                string employeeNo = request.employeeNo;
                string actingUser = approvedByUser ?? EmployeeNo;

                // ── 2. Determine the approver's level for this specific employee ───
                int? approverLevel = null;
                bool isFull = HasFullAccess();

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

                // ── 3. Get the full required-level chain for this employee ─────────
                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();
                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                // ── 4. Build current approval state from DB columns ───────────────
                var approvedLevels = new List<int>();
                if ((string)request.statusLevel1 == "Approved" && requiredLevels.Contains(1))
                    approvedLevels.Add(1);
                if ((string)request.statusLevel2 == "Approved" && requiredLevels.Contains(2))
                    approvedLevels.Add(2);
                if ((string)request.statusLevel3 == "Approved" && requiredLevels.Contains(3))
                    approvedLevels.Add(3);

                // ── 5. Which level is this approver acting as? ────────────────────
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
                    var lowerPendingLevels = requiredLevels
                        .Where(l => l < actingLevel && !approvedLevels.Contains(l))
                        .OrderBy(l => l)
                        .ToList();

                    if (lowerPendingLevels.Any())
                        return Json(new
                        {
                            success = false,
                            message = $"This request must first be approved by Level {lowerPendingLevels.First()} " +
                                      $"before Level {actingLevel} can act."
                        });
                }

                // ── 7. Guard: has this level already approved? ─────────────────────
                if (approvedLevels.Contains(actingLevel))
                    return Json(new
                    {
                        success = false,
                        message = $"Level {actingLevel} has already approved this request."
                    });

                // ── 8. Compute the new overall status after this approval ──────────
                // When Level 4 bypasses, ALL required levels are treated as approved.
                var newlyApproved = actingLevel == 4
                    ? new List<int>(requiredLevels)
                    : new List<int>(approvedLevels) { actingLevel };

                int highestRequired = requiredLevels.Max();
                bool isFullyApproved = requiredLevels.All(l => newlyApproved.Contains(l));

                // ── 9. Build the UPDATE statement dynamically ──────────────────────
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

                // Update the column for the acting level
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

                // If fully approved and the highest required level is NOT 4,
                // still push statusLevel4 to "Approved" as the final gate.
                if (isFullyApproved && highestRequired < 4 && actingLevel != 4)
                {
                    setParts.Add("statusLevel4   = 'Approved'");
                    setParts.Add("dtStatusLevel4 = NOW()");
                    setParts.Add("statusByLevel4 = @actingUser");
                }

                // Always update audit trail columns
                setParts.Add("dtStatus           = NOW()");
                setParts.Add("statusByUser       = @actingUser");
                setParts.Add("dtLastModified     = NOW()");
                setParts.Add("lastModifiedByUser = @actingUser");

                var updateSql =
                    $"UPDATE rq_officialbusiness SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // ── 10. Notify ─────────────────────────────────────────────────────
                if (isFullyApproved)
                {
                    NotifyRequestAction("officialBusiness", id, employeeNo, "approved");
                }
                else
                {
                    int nextLevel = requiredLevels
                        .Where(l => !newlyApproved.Contains(l))
                        .OrderBy(l => l)
                        .First();

                    NotifyNextApprover("officialBusiness", id, employeeNo, nextLevel);
                }

                // ── 11. Audit ──────────────────────────────────────────────────────
                var auditMsg = isFullyApproved
                    ? $"OB request fully approved at Level {actingLevel} by {actingUser}"
                    : $"OB request partially approved at Level {actingLevel} by {actingUser}. Awaiting higher level approval.";

                _auditTrail.Log("rq_officialbusiness", id, "APPROVED", auditMsg);

                var successMessage = isFullyApproved
                    ? "OB Request approved successfully!"
                    : $"Level {actingLevel} approval recorded. Request is now pending the next approver.";

                string employeeEmail = _emailService.GetEmployeeEmail(request.employeeNo).ToString();
                string dateFrom = request.obDateIn?.ToString("yyyy-MM-dd") + " " + request.obTimeIn?.ToString(@"hh\:mm\:ss");
                string dateTo = request.obDateOut?.ToString("yyyy-MM-dd") + " " + request.obTimeOut?.ToString(@"hh\:mm\:ss");

                _emailService.SendRequestStatusEmailAsync("Official Business Request Status", employeeEmail, request.statusLevel1, request.statusLevel2,
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
                Console.WriteLine($"Error in ApproveOBRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while approving the request." });
            }
        }

        // Decline OB Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> DeclineOBRequest(int id, string declinedByUser, string reason = "")
        {
            try
            {
                // ── 1. Load the request with current level statuses ───────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    rq.employeeNo,
                    rq.statusLevel1,
                    rq.statusLevel2,
                    rq.statusLevel3,
                    rq.statusLevel4
                FROM rq_officialbusiness rq
                INNER JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                WHERE rq.id = @id AND rq.isActive = 1",
                new { id });

                if (request == null)
                    return Json(new { success = false, message = "OB Request not found!" });

                if (request.statusLevel4 == "Cancelled" || request.statusLevel4 == "Processed")
                    return Json(new { success = false, message = "Cancelled or processed requests cannot be declined!" });

                string employeeNo = (string)request.employeeNo;
                string actingUser = declinedByUser ?? EmployeeNo;
                bool isFull = HasFullAccess();

                // ── 2. Determine the acting user's approver level ─────────────────
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

                // ── 3. Get required levels to know the chain ──────────────────────
                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();
                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                // ── 4. FULL access declines at the next pending level ─────────────
                if (isFull)
                {
                    string sl1 = (string)request.statusLevel1 ?? "Pending";
                    string sl2 = (string)request.statusLevel2;
                    string sl3 = (string)request.statusLevel3;

                    approverLevel = requiredLevels.FirstOrDefault(l =>
                    {
                        return l switch
                        {
                            1 => sl1 != "Approved",
                            2 => sl2 != "Approved",
                            3 => sl3 != "Approved",
                            _ => true
                        };
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

                    var lowerPendingLevels = requiredLevels
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

                    if (lowerPendingLevels.Any())
                        return Json(new
                        {
                            success = false,
                            message = $"This request must first be approved by Level " +
                                      $"{lowerPendingLevels.First()} before Level {actingLevel} can act."
                        });
                }

                // ── 6. Build UPDATE ────────────────────────────────────────────────
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

                // Audit trail columns
                setParts.Add("dtStatus           = NOW()");
                setParts.Add("statusByUser       = @actingUser");
                setParts.Add("dtLastModified     = NOW()");
                setParts.Add("lastModifiedByUser = @actingUser");

                var updateSql =
                    $"UPDATE rq_officialbusiness SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // ── 7. Notify requestor ───────────────────────────────────────────
                NotifyRequestAction("officialBusiness", id, employeeNo, "declined");

                // ── 8. Audit ──────────────────────────────────────────────────────
                _auditTrail.Log("rq_officialbusiness", id, "DECLINED",
                    $"Declined OB request at Level {actingLevel} by {actingUser}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "OB Request declined successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeclineOBRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while declining the request." });
            }
        }

        // Cancel OB Request WITH SECURITY CHECK
        [HttpPost]
        public async Task<JsonResult> CancelOBRequest(int id, string cancelledByUser, string reason = "")
        {
            try
            {
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT employeeNo, statusLevel4 FROM rq_officialbusiness WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "OB Request not found!" });

                // Only allow user to cancel their own request
                if (request.employeeNo != EmployeeNo && !HasFullAccess())
                    return Json(new { success = false, message = "Access denied. You can only cancel your own requests." });

                if (request.statusLevel4 == "Approved")
                    return Json(new { success = false, message = "Approved requests cannot be cancelled!" });

                if (request.statusLevel4 == "Cancelled")
                    return Json(new { success = false, message = "Request is already cancelled!" });

                var sql = @"
                    UPDATE rq_officialbusiness 
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

                NotifyRequestAction("officialBusiness", id, request.employeeNo, "cancelled");

                _auditTrail.Log("rq_officialbusiness", id, "CANCELLED",
                    $"Cancelled OB request by {cancelledByUser ?? EmployeeNo}{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "OB Request cancelled successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CancelOBRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while cancelling the request." });
            }
        }

        // Get attachments for OB Request WITH SECURITY CHECK
        [HttpGet]
        public JsonResult GetOBAttachments(string employeeNo)
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
                    AND attachmentTypeCode = 'OFFICIALBUSINESS' 
                    AND isActive = 1
                    ORDER BY dtAdded DESC";

                return Json(_db.Query(sql, new { employeeNo }).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetOBAttachments: {ex.Message}");
                return Json(new List<object>());
            }
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
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "officialbusiness");
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
                    VALUES (@employeeNo, 'Official Business', 'OFFICIALBUSINESS', @attachmentPath, 1, NOW())";

                _db.Execute(sql, new
                {
                    employeeNo,
                    attachmentPath = $"/uploads/officialbusiness/{fileName}"
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