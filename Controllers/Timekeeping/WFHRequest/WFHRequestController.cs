using Dapper;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.WFHRequest
{
    [ModuleAuthorize("RworkOnOffM")]
    public class WFHRequestController : TimekeepingRequestBaseController
    {
        public WFHRequestController(
        IDbConnection db,
        IAuditTrailService auditTrail,
        IApproverService approverService)
        : base(db, auditTrail, "RworkOnOffM")
        {
            _approverService = approverService;
        }

        public IActionResult Index()
        {
            return View("~/Views/Timekeeping/WFHRequest.cshtml");
        }

        [HttpGet]
        public async Task<JsonResult> GetWFHRequestList(string status, string branch, string department, string dateFrom, string dateTo)
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
                    DATE_FORMAT(rq.wfhDateIn, '%m/%d/%Y') as displayDateIn,
                    TIME_FORMAT(rq.wfhTimeIn, '%H:%i') as displayTimeIn,
                    DATE_FORMAT(rq.wfhDateOut, '%m/%d/%Y') as displayDateOut,
                    TIME_FORMAT(rq.wfhTimeOut, '%H:%i') as displayTimeOut,
                    rq.wfhReason,
                    rq.statusLevel1,
                    rq.statusLevel2,
                    rq.statusLevel3,
                    rq.statusLevel4,
                    rq.statusLevel4 AS statusName,
                    rq.remarks,
                    rq.dtAdded,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser
                FROM rq_workfromhome rq
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
                    query.Append(" AND rq.wfhDateIn BETWEEN @dateFrom AND @dateTo");
                    parameters.Add("@dateFrom", parsedFrom.ToString("yyyy-MM-dd"));
                    parameters.Add("@dateTo", parsedTo.ToString("yyyy-MM-dd"));
                }
            }

            query.Append(" ORDER BY rq.id DESC");

            await MarkRequestsAsProcessedAsync("rq_workfromhome", "wfhDateIn");

            var requests = await _db.QueryAsync<dynamic>(query.ToString(), parameters);
            return Json(new { data = requests });
        }

        // Get single WFH Request by ID
        [HttpGet]
        public async Task<JsonResult> GetWFHRequest(int id)
        {
            try
            {
                var employeeNo = _db.QueryFirstOrDefault<string>(
                    "SELECT employeeNo FROM rq_workfromhome WHERE id = @id AND isActive = 1",
                    new { id });

                if (string.IsNullOrEmpty(employeeNo))
                    return Json(new { error = "WFH Request not found!" });

                if (!CanViewEmployee(employeeNo))
                    return Json(new { error = "Access denied. You don't have permission to view this employee's WFH request." });

                var sql = @"
                    SELECT 
                        rq.id,
                        rq.employeeNo,
                        DATE_FORMAT(rq.wfhDateIn, '%m/%d/%Y') as displayDateIn,
                        TIME_FORMAT(rq.wfhTimeIn, '%H:%i') as displayTimeIn,
                        DATE_FORMAT(rq.wfhDateOut, '%m/%d/%Y') as displayDateOut,
                        TIME_FORMAT(rq.wfhTimeOut, '%H:%i') as displayTimeOut,
                        rq.wfhReason,
                        rq.statusLevel1,
                        rq.statusLevel2,
                        rq.statusLevel3,
                        rq.statusLevel4,
                        rq.statusLevel4 AS statusName,
                        rq.remarks,
                        CONCAT(IFNULL(e.firstName, ''), ' ', IFNULL(CONCAT(e.middleName, ' '), ''), IFNULL(e.lastName, '')) as employeeName,
                        CONCAT(IFNULL(req.firstName, ''), ' ', IFNULL(CONCAT(req.middleName, ' '), ''), IFNULL(req.lastName, '')) as requestedByUser
                    FROM rq_workfromhome rq
                    LEFT JOIN e_basicinfo e ON rq.employeeNo = e.employeeNo
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
                    displayTimeIn = (string)request.displayTimeIn,
                    displayDateOut = (string)request.displayDateOut,
                    displayTimeOut = (string)request.displayTimeOut,
                    wfhReason = (string)request.wfhReason,
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
                Console.WriteLine($"Error in GetWFHRequest: {ex.Message}");
                return Json(null);
            }
        }

        // Add new WFH Request
        [HttpPost]
        public JsonResult AddWFHRequest(WorkFromHomeRequestModel model)
        {
            try
            {
                // Security check using base method
                if (!CanViewEmployee(model.employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to create WFH requests for this employee." });
                }

                // Validate employee exists using base method
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Validate dates
                if (model.wfhDateIn == null || model.wfhDateOut == null)
                    return Json(new { success = false, message = "Date In and Date Out are required!" });

                if (model.wfhDateOut < model.wfhDateIn)
                    return Json(new { success = false, message = "Date Out cannot be earlier than Date In!" });

                // Duplicate filing check — block overlapping WFH requests
                var duplicate = _db.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) FROM rq_workfromhome
                    WHERE employeeNo = @employeeNo
                    AND isActive = 1
                    AND statusLevel4 NOT IN ('Cancelled', 'Declined')
                    AND wfhDateIn  <= @wfhDateOut
                    AND wfhDateOut >= @wfhDateIn",
                    new { model.employeeNo, model.wfhDateIn, model.wfhDateOut });

                if (duplicate > 0)
                    return Json(new { success = false, message = "A work from home request already exists for the selected date range." });

                // Insert new WFH Request with Pending status across all levels
                var sql = @"
                        INSERT INTO rq_workfromhome 
                        (employeeNo, wfhDateIn, wfhTimeIn, wfhDateOut, wfhTimeOut, wfhReason, 
                         statusLevel1, statusLevel2, statusLevel3, statusLevel4, remarks, isActive, dtAdded, addedByUser, 
                         requestedByUser, dtStatus, statusByUser, dtStatusLevel1, statusByLevel1,
                         dtStatusLevel2, statusByLevel2, 
                         dtStatusLevel3, statusByLevel3, dtStatusLevel4, statusByLevel4) 
                        VALUES 
                        (@employeeNo, @wfhDateIn, @wfhTimeIn, @wfhDateOut, @wfhTimeOut, @wfhReason, 
                         'Pending', 'Pending', 'Pending', 'Pending', @remarks, 1, NOW(), @addedByUser, @requestedByUser, 
                         NOW(), @addedByUser, NOW(), @addedByUser,
                         NOW(), @addedByUser, 
                         NOW(), @addedByUser, NOW(), @addedByUser);
                    SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    model.employeeNo,
                    model.wfhDateIn,
                    wfhTimeIn = model.wfhTimeIn ?? TimeSpan.Parse("08:00:00"),
                    model.wfhDateOut,
                    wfhTimeOut = model.wfhTimeOut ?? TimeSpan.Parse("17:00:00"),
                    wfhReason = model.wfhReason ?? "",
                    remarks = model.remarks ?? "",
                    addedByUser = EmployeeNo,
                    requestedByUser = EmployeeNo
                });

                NotifyRequestAction("workFromHome", newId, model.employeeNo, "pending");

                // Log to audit trail
                _auditTrail.Log("rq_workfromhome", newId, "CREATED",
                    $"Added WFH request for {model.employeeNo}: {model.wfhDateIn:yyyy-MM-dd} to {model.wfhDateOut:yyyy-MM-dd}");

                return Json(new { success = true, message = "Work From Home Request added successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddWFHRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error adding WFH Request: {ex.Message}" });
            }
        }

        // Update existing WFH Request (Pending and Declined only)
        [HttpPost]
        public JsonResult UpdateWFHRequest(WorkFromHomeRequestModel model)
        {
            try
            {
                // Check if record exists and get current status
                var currentRecord = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT statusLevel4, employeeNo FROM rq_workfromhome WHERE id = @id AND isActive = 1",
                    new { model.id });

                if (currentRecord == null)
                    return Json(new { success = false, message = "WFH Request not found!" });

                string currentStatus = currentRecord.statusLevel4;
                string recordEmployeeNo = currentRecord.employeeNo;

                // Security check using base method
                if (!CanViewEmployee(recordEmployeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to modify this employee's WFH request." });
                }

                // Only allow editing Pending or Declined requests
                if (currentStatus != "Pending" && currentStatus != "Declined")
                    return Json(new { success = false, message = "Only pending or declined requests can be edited!" });

                // Validate employee exists using base method
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                // Validate dates
                if (model.wfhDateOut < model.wfhDateIn)
                    return Json(new { success = false, message = "Date Out cannot be earlier than Date In!" });

                // If status was Declined, set back to Pending when edited
                var newStatus = currentStatus == "Declined" ? "Pending" : currentStatus;

                var sql = @"
                    UPDATE rq_workfromhome 
                    SET employeeNo = @employeeNo,
                        wfhDateIn = @wfhDateIn,
                        wfhTimeIn = @wfhTimeIn,
                        wfhDateOut = @wfhDateOut,
                        wfhTimeOut = @wfhTimeOut,
                        wfhReason = @wfhReason,
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
                    model.wfhDateIn,
                    wfhTimeIn = model.wfhTimeIn ?? TimeSpan.Parse("08:00:00"),
                    model.wfhDateOut,
                    wfhTimeOut = model.wfhTimeOut ?? TimeSpan.Parse("17:00:00"),
                    wfhReason = model.wfhReason ?? "",
                    remarks = model.remarks ?? "",
                    statusLevel = newStatus,
                    lastModifiedByUser = EmployeeNo
                });

                // Log to audit trail
                _auditTrail.Log("rq_workfromhome", model.id, "UPDATED",
                    $"Updated WFH request for {model.employeeNo}: {model.wfhDateIn:yyyy-MM-dd} to {model.wfhDateOut:yyyy-MM-dd}");

                var message = currentStatus == "Declined"
                    ? "WFH Request updated successfully and status set back to Pending!"
                    : "Work From Home Request updated successfully!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateWFHRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error updating WFH Request: {ex.Message}" });
            }
        }

        // Approve WFH Request
        [HttpPost]
        public async Task<JsonResult> ApproveWFHRequest(int id, string approvedByUser)
        {
            try
            {
                // ── 1. Load the request ───────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, statusLevel1, statusLevel2, statusLevel3, statusLevel4
                    FROM rq_workfromhome
                    WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "WFH Request not found!" });

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

                // ── 3. Get required level chain ───────────────────────────────────
                var requiredLevels = await _approverService
                    .GetRequiredApprovalLevelsAsync(employeeNo);

                requiredLevels = requiredLevels.Where(l => l >= 1 && l <= 4).ToList();
                if (requiredLevels.Count == 0)
                    requiredLevels = new List<int> { 4 };

                // ── 4. Build current approved levels from DB ──────────────────────
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
                        setParts.Add("statusName     = 'Approved'");
                        setParts.Add("dtStatusLevel4 = NOW()");
                        setParts.Add("statusByLevel4 = @actingUser");
                        break;
                }

                // If fully approved and highest required < 4, cascade final gate
                if (isFullyApproved && highestRequired < 4 && actingLevel != 4)
                {
                    setParts.Add("statusLevel4   = 'Approved'");
                    setParts.Add("statusName     = 'Approved'");
                    setParts.Add("dtStatusLevel4 = NOW()");
                    setParts.Add("statusByLevel4 = @actingUser");
                }

                setParts.Add("dtStatus           = NOW()");
                setParts.Add("statusByUser       = @actingUser");
                setParts.Add("dtLastModified     = NOW()");
                setParts.Add("lastModifiedByUser = @actingUser");

                var updateSql =
                    $"UPDATE rq_workfromhome SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // ── 10. Notify ────────────────────────────────────────────────────
                if (isFullyApproved)
                {
                    NotifyRequestAction("workFromHome", id, employeeNo, "approved");
                }
                else
                {
                    int nextLevel = requiredLevels
                        .Where(l => !newlyApproved.Contains(l))
                        .OrderBy(l => l)
                        .First();

                    NotifyNextApprover("workFromHome", id, employeeNo, nextLevel);
                }

                // ── 11. Audit ─────────────────────────────────────────────────────
                var auditMsg = isFullyApproved
                    ? $"WFH request fully approved at Level {actingLevel} by {actingUser}"
                    : $"WFH request partially approved at Level {actingLevel} by {actingUser}. " +
                      $"Awaiting higher level approval.";

                _auditTrail.Log("rq_workfromhome", id, "APPROVED", auditMsg);

                var successMessage = isFullyApproved
                    ? "WFH Request approved successfully!"
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
                Console.WriteLine($"Error in ApproveWFHRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while approving the request." });
            }
        }

        // Decline WFH Request
        [HttpPost]
        public async Task<JsonResult> DeclineWFHRequest(int id, string declinedByUser, string reason = "")
        {
            try
            {
                // ── 1. Load request ───────────────────────────────────────────────
                var request = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT employeeNo, statusLevel1, statusLevel2, statusLevel3, statusLevel4
                    FROM rq_workfromhome
                    WHERE id = @id AND isActive = 1",
                    new { id });

                if (request == null)
                    return Json(new { success = false, message = "WFH Request not found!" });

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

                // ── 3. Get required levels ────────────────────────────────────────
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
                setParts.Add("statusName     = 'Declined'");
                setParts.Add("dtStatusLevel4 = NOW()");
                setParts.Add("statusByLevel4 = @actingUser");
                setParts.Add("dtStatus           = NOW()");
                setParts.Add("statusByUser       = @actingUser");
                setParts.Add("dtLastModified     = NOW()");
                setParts.Add("lastModifiedByUser = @actingUser");

                var updateSql =
                    $"UPDATE rq_workfromhome SET {string.Join(", ", setParts)} WHERE id = @id";

                await _db.ExecuteAsync(updateSql, new { id, actingUser });

                // ── 7. Notify ─────────────────────────────────────────────────────
                NotifyRequestAction("workFromHome", id, employeeNo, "declined");

                // ── 8. Audit ──────────────────────────────────────────────────────
                _auditTrail.Log("rq_workfromhome", id, "DECLINED",
                    $"Declined WFH request at Level {actingLevel} by {actingUser}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "WFH Request declined successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeclineWFHRequest: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while declining the request." });
            }
        }

        // Cancel WFH Request
        [HttpPost]
        public JsonResult CancelWFHRequest(int id, string cancelledByUser, string reason = "")
        {
            try
            {
                // Get the employeeNo for this request
                var record = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT statusLevel4, employeeNo FROM rq_workfromhome WHERE id = @id AND isActive = 1",
                    new { id });

                if (record == null)
                    return Json(new { success = false, message = "WFH Request not found!" });

                string currentStatus = record.statusLevel4;
                string employeeNo = record.employeeNo;

                // Security check using base method
                if (!CanViewEmployee(employeeNo))
                {
                    return Json(new { success = false, message = "Access denied. You don't have permission to cancel this employee's WFH request." });
                }

                // Only allow cancelling Pending or Declined requests (NOT Approved)
                if (currentStatus == "Approved")
                    return Json(new { success = false, message = "Approved requests cannot be cancelled!" });

                if (currentStatus == "Cancelled")
                    return Json(new { success = false, message = "Request is already cancelled!" });

                var sql = @"
                    UPDATE rq_workfromhome 
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

                _db.Execute(sql, new { id, cancelledByUser = EmployeeNo });

                NotifyRequestAction("workFromHome", id, employeeNo, "cancelled");

                // Log to audit trail
                _auditTrail.Log("rq_workfromhome", id, "CANCELLED",
                    $"Cancelled WFH request by {EmployeeNo}{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "WFH Request cancelled successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CancelWFHRequest: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}