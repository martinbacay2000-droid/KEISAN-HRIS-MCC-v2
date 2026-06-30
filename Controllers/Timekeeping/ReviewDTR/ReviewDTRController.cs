using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using KEISAN_HRIS_v2.Services.TimeKeeping;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping
{
    [ModuleAuthorize("TreviewDTRM")]
    public class ReviewDTRController : BaseController
    {
        private const string MODULE = "TreviewDTRM";

        private readonly ReviewDTRService _service;
        private readonly IDbConnection _db;

        public ReviewDTRController(ReviewDTRService service, IDbConnection db)
        {
            _service = service;
            _db = db;
        }

        // ─────────────────────────────────────────────────────────────
        // INDEX
        // ─────────────────────────────────────────────────────────────
        public IActionResult Index()
        {
            return View("~/Views/Timekeeping/ReviewDTR.cshtml");
        }

        // ─────────────────────────────────────────────────────────────
        // EXPOSE ACCESS LEVEL TO FRONT-END
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult GetCurrentAccess()
        {
            var level = AccessHelper.GetAccess(HttpContext, MODULE) ?? "NO_ACCESS";
            return Json(new { accessLevel = level, roleCode = RoleCode ?? "" });
        }

        /// <summary>
        /// Returns whether ANY date in [dateFrom, dateTo] falls inside
        /// a posted payroll cutoff period.
        /// The front-end calls this when the employee detail modal opens
        /// to decide whether to render cells as locked.
        /// </summary>
        //[HttpGet]
        //public async Task<IActionResult> IsDateRangePosted(DateTime dateFrom, DateTime dateTo)
        //{
        //    bool posted = await _service.IsDateRangePostedAsync(dateFrom, dateTo);
        //    return Json(new { posted });
        //}
        [HttpGet]
        public async Task<IActionResult> IsDateRangePosted(DateTime dateFrom, DateTime dateTo, string branchCode = "")
        {
            branchCode = (branchCode == "ALL" || branchCode == null) ? "" : branchCode;
            bool posted = await _service.IsDateRangePostedAsync(dateFrom, dateTo, branchCode);
            return Json(new { posted });
        }

        // ─────────────────────────────────────────────────────────────
        // SUMMARY DATA  — enforces data scope
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetSummaryData(
            DateTime dateFrom,
            DateTime dateTo,
            string branchCode)
        {
            // FIX: restore original null guard (was: if (branchCode == "ALL"))
            branchCode = (branchCode == "ALL" || branchCode == null) ? "" : branchCode;

            var (allowedEmployeeNos, effectiveBranch) = ResolveScope(branchCode);

            // Empty allowed set = no employees in scope
            if (allowedEmployeeNos != null && !allowedEmployeeNos.Any())
                return Json(new { data = new List<object>() });

            var data = await _service.GetSummaryAsync(dateFrom, dateTo, effectiveBranch, "");

            // Post-filter to allowed employees when scope is employee-based
            if (allowedEmployeeNos != null)
                data = data.Where(d => allowedEmployeeNos.Contains(d.EmployeeNo)).ToList();

            return Json(new { data });
        }

        // ─────────────────────────────────────────────────────────────
        // MODAL PARTIAL
        // ─────────────────────────────────────────────────────────────
        public IActionResult EmployeeDetails()
        {
            return PartialView("~/Views/Timekeeping/Partials/_EmployeeDailyDetails.cshtml");
        }

        // ─────────────────────────────────────────────────────────────
        // EMPLOYEE DAILY DATA  — enforces data scope
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetEmployeeDailyData(
            string employeeNo, DateTime dateFrom, DateTime dateTo, string branchCode)
        {
            // FIX: restore original null guard
            branchCode = (branchCode == "ALL" || branchCode == null) ? "" : branchCode;

            if (!CanViewEmployee(employeeNo))
                return Json(new { data = new List<object>() });

            var (_, effectiveBranch) = ResolveScope(branchCode);
            var rows = await _service.GetDailyRowsAsync(dateFrom, dateTo, effectiveBranch, employeeNo);
            return Json(new { data = rows });
        }

        // ─────────────────────────────────────────────────────────────
        // PROCESS DTR  (READWRITE / FULL only)
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ProcessEmployeeDTR(
            string employeeNo,
            DateTime dateFrom,
            DateTime dateTo,
            string branchCode,
            int cutOffType,
            string dateMonth)
        {
            if (!AccessHelper.CanCreate(HttpContext, MODULE))
                return Json(new { success = false, message = "Access denied. You do not have permission to process DTR." });

            // HR CASUAL is not allowed to process DTR — they use Lock DTR instead
            if (RoleCode == "HR CASUAL")
                return Json(new { success = false, message = "Access denied. HR Casual uses Lock DTR instead." });

            if (!CanViewEmployee(employeeNo))
                return Json(new { success = false, message = "Access denied. This employee is outside your data scope." });

            // Resolve the effective branch for this employee so we check only their branch
            var empBranch = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT branchCode FROM e_basicinfo WHERE employeeNo = @employeeNo AND isActive = 1 LIMIT 1",
                new { employeeNo });

            bool alreadyPosted = await _service.IsDtrPostedAsync(cutOffType, dateMonth, dateFrom.Year, empBranch ?? "");
            if (alreadyPosted)
                return Json(new { success = false, message = "DTR is already POSTED for this cutoff." });

            // FIX: restore original null guard
            branchCode = (branchCode == "ALL" || branchCode == null) ? "" : branchCode;
            var (_, effectiveBranch) = ResolveScope(branchCode);

            int rowsInserted = await _service.ProcessSingleEmployeeAsync(
                employeeNo, dateFrom, dateTo, effectiveBranch, cutOffType, EmployeeNo ?? "SYSTEM", dateMonth);

            return Json(new { success = true, employeeNo, rowsInserted });
        }

        // ─────────────────────────────────────────────────────────────
        // SAVE TIME EDIT (FULL access only)
        // Saves both Time In and Time Out into a SINGLE t_biometrics row.
        // Only the sides that actually changed are written — the other side
        // stays NULL in t_biometrics so the SQL CASE falls back to u_biometrics.
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SaveBiometricsEdit(
            string employeeNo,
            DateTime workDate,
            string? newTimeIn,       // HH:mm:ss or empty if unchanged
            string? newTimeOut,      // HH:mm:ss or empty if unchanged
            string? newTimeOutDate)  // yyyy-MM-dd or empty
        {
            if (!Helpers.AccessHelper.CanDelete(HttpContext, MODULE))
                return Json(new { success = false, message = "Access denied. Full access required." });

            if (!CanViewEmployee(employeeNo))
                return Json(new { success = false, message = "Access denied for this employee." });

            try
            {
                TimeSpan? timeInTs = null;
                TimeSpan? timeOutTs = null;
                DateTime? dateOutDt = null;

                TimeSpan pTI = default;
                TimeSpan pTO = default;

                bool hasTI = !string.IsNullOrWhiteSpace(newTimeIn) && TimeSpan.TryParse(newTimeIn, out pTI);
                bool hasTO = !string.IsNullOrWhiteSpace(newTimeOut) && TimeSpan.TryParse(newTimeOut, out pTO);

                if (hasTI) timeInTs = pTI;
                if (hasTO) timeOutTs = pTO;
                if (!string.IsNullOrWhiteSpace(newTimeOutDate) && DateTime.TryParse(newTimeOutDate, out var pDate))
                    dateOutDt = pDate.Date;

                // ── Step 1: Find or create ONE u_biometrics row ───────────────
                var existingUBio = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT id, biometricsDeviceLog, biometricsTimeIn, biometricsTimeOut
                    FROM u_biometrics
                    WHERE employeeNo     = @employeeNo
                      AND biometricsDate = @workDate
                    ORDER BY
                        CASE WHEN biometricsDeviceLog = 'modified' THEN 1 ELSE 0 END ASC,
                        id ASC
                    LIMIT 1",
                    new { employeeNo, workDate = workDate.Date });

                int uBiometricsId;

                if (existingUBio != null)
                {
                    await _db.ExecuteAsync(@"
                        UPDATE u_biometrics
                        SET biometricsDeviceLog = 'modified', isActive = 1
                        WHERE id = @id",
                        new { id = (int)existingUBio.id });
                    uBiometricsId = (int)existingUBio.id;
                }
                else
                {
                    await _db.ExecuteAsync(@"
                        INSERT INTO u_biometrics
                            (employeeNo, biometricsDate, biometricsTimeIn, biometricsDateOut,
                             biometricsTimeOut, biometricsDeviceLog, isActive)
                        VALUES
                            (@employeeNo, @workDate, NULL, NULL, NULL, 'modified', 1)",
                        new { employeeNo, workDate = workDate.Date });
                    uBiometricsId = (int)(await _db.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()"));
                }

                // ── Step 2: Upsert ONE t_biometrics row (same pattern as cancel) ─
                var existingTBio = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT id
                    FROM t_biometrics
                    WHERE employeeNo     = @employeeNo
                      AND biometricsDate = @workDate
                      AND u_biometricsID = @uBiometricsId
                      AND isActive       = 1
                    LIMIT 1",
                    new { employeeNo, workDate = workDate.Date, uBiometricsId });

                if (existingTBio != null)
                {
                    // Update only the sides that changed — leave the other side as-is
                    string setClauses = "";
                    if (hasTI) setClauses += " biometricsTimeIn  = @timeIn,";
                    if (hasTO) setClauses += " biometricsTimeOut = @timeOut, DateOut = @dateOut,";
                    setClauses += " tagStatus = 'modified', statusName = 'modified'," +
                                  " dtLastModified = NOW(), lastModifiedByUser = @modifiedBy, isActive = 1";

                    await _db.ExecuteAsync($@"
                        UPDATE t_biometrics
                        SET {setClauses}
                        WHERE id = @id",
                        new
                        {
                            timeIn = timeInTs,
                            timeOut = timeOutTs,
                            dateOut = dateOutDt ?? workDate.Date,
                            modifiedBy = EmployeeNo ?? "SYSTEM",
                            id = (int)existingTBio.id
                        });
                }
                else
                {
                    await _db.ExecuteAsync(@"
                        INSERT INTO t_biometrics
                            (employeeNo, u_biometricsID, biometricsDate, DateOut,
                             biometricsTimeIn, biometricsTimeOut,
                             tagStatus, statusName,
                             isActive, dtAdded, addedByUser)
                        VALUES
                            (@employeeNo, @uBiometricsId, @workDate, @dateOut,
                             @timeIn, @timeOut,
                             'modified', 'modified',
                             1, NOW(), @addedBy)",
                        new
                        {
                            employeeNo,
                            uBiometricsId,
                            workDate = workDate.Date,
                            dateOut = dateOutDt ?? workDate.Date,
                            timeIn = timeInTs,   // NULL if not changed
                            timeOut = timeOutTs,  // NULL if not changed
                            addedBy = EmployeeNo ?? "SYSTEM"
                        });
                }

                return Json(new { success = true, uBiometricsId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // UNDO BIOMETRICS EDIT  (FULL access only)
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> UndoBiometricsEdit(
            string employeeNo,
            DateTime workDate,
            int? uBiometricsId)
        {
            if (!Helpers.AccessHelper.CanDelete(HttpContext, MODULE))
                return Json(new { success = false, message = "Access denied. Full access required." });

            if (!CanViewEmployee(employeeNo))
                return Json(new { success = false, message = "Access denied for this employee." });

            try
            {
                // Delete the single t_biometrics edited record
                await _db.ExecuteAsync(@"
                    DELETE FROM t_biometrics
                    WHERE employeeNo     = @employeeNo
                      AND biometricsDate = @workDate
                      AND u_biometricsID = @uBiometricsId
                      AND statusName     = 'modified'
                      AND isActive       = 1",
                    new { employeeNo, workDate = workDate.Date, uBiometricsId });

                // Check if any t_biometrics rows remain for this u_biometrics
                var remaining = await _db.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1) FROM t_biometrics
                    WHERE u_biometricsID = @uBiometricsId AND isActive = 1",
                    new { uBiometricsId });

                if (remaining == 0 && uBiometricsId.HasValue)
                {
                    var uBioRow = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                        SELECT id, biometricsTimeIn, biometricsTimeOut
                        FROM u_biometrics WHERE id = @id LIMIT 1",
                        new { id = uBiometricsId.Value });

                    if (uBioRow != null)
                    {
                        if (uBioRow.biometricsTimeIn == null && uBioRow.biometricsTimeOut == null)
                            await _db.ExecuteAsync("DELETE FROM u_biometrics WHERE id = @id",
                                new { id = (int)uBioRow.id });
                        else
                            await _db.ExecuteAsync("UPDATE u_biometrics SET biometricsDeviceLog = NULL WHERE id = @id",
                                new { id = (int)uBioRow.id });
                    }
                }

                // Return original device times for UI restore
                var orig = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT biometricsDate, biometricsTimeIn, biometricsDateOut, biometricsTimeOut
                    FROM u_biometrics
                    WHERE employeeNo = @employeeNo AND biometricsDate = @workDate AND isActive = 1
                    ORDER BY id ASC LIMIT 1",
                    new { employeeNo, workDate = workDate.Date });

                string? origTI = null, origTO = null;
                if (orig?.biometricsTimeIn != null)
                    origTI = ((DateTime)orig.biometricsDate).Add((TimeSpan)orig.biometricsTimeIn)
                             .ToString("yyyy-MM-ddTHH:mm:ss");
                if (orig?.biometricsTimeOut != null)
                {
                    var d = orig.biometricsDateOut != null ? (DateTime)orig.biometricsDateOut : (DateTime)orig.biometricsDate;
                    origTO = d.Add((TimeSpan)orig.biometricsTimeOut).ToString("yyyy-MM-ddTHH:mm:ss");
                }

                return Json(new { success = true, originalTimeIn = origTI, originalTimeOut = origTO });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // TIME IN  (EDIT / READWRITE / FULL only)
        //   u_biometrics = Original device records.
        //                  We NEVER overwrite biometricsTimeIn / biometricsTimeOut.
        //                  We only set biometricsDeviceLog = 'modified' as a tag.
        //   t_biometrics = stores the admin-edited time in/out pair
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> InsertOrUpdateBiometricsTimeIn(
            string employeeNo,
            DateTime workDate,
            string timeIn,
            string? existingTimeOut,
            string? existingTimeOutDate)
        {
            if (!AccessHelper.CanEdit(HttpContext, MODULE))
                return Json(new { success = false, message = "Access denied. You do not have permission to edit Time In." });

            if (!CanViewEmployee(employeeNo))
                return Json(new { success = false, message = "Access denied. This employee is outside your data scope." });

            try
            {
                TimeSpan timeInTs = TimeSpan.Parse(timeIn);
                TimeSpan? timeOutTs = null;
                DateTime? timeOutDate = null;

                if (!string.IsNullOrWhiteSpace(existingTimeOut) && TimeSpan.TryParse(existingTimeOut, out var parsedTimeOut))
                    timeOutTs = parsedTimeOut;
                if (!string.IsNullOrWhiteSpace(existingTimeOutDate) && DateTime.TryParse(existingTimeOutDate, out var parsedTimeOutDate))
                    timeOutDate = parsedTimeOutDate.Date;

                // ── STEP 1: Find existing u_biometrics row for this date ──────────
                // Prefer the original device row over any previously inserted 'modified' skeleton.
                var existingUBio = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT id, biometricsDeviceLog
                    FROM u_biometrics
                    WHERE employeeNo     = @employeeNo
                      AND biometricsDate = @workDate
                    ORDER BY
                        CASE WHEN biometricsDeviceLog = 'modified' THEN 1 ELSE 0 END ASC,
                        id ASC
                    LIMIT 1",
                    new { employeeNo, workDate = workDate.Date });

                int uBiometricsId;

                if (existingUBio != null)
                {
                    // Row exists — tag it as modified, leave ALL time columns completely alone
                    await _db.ExecuteAsync(@"
                        UPDATE u_biometrics
                        SET biometricsDeviceLog = 'modified',
                            isActive            = 1
                        WHERE id = @id",
                        new { id = (int)existingUBio.id });

                    uBiometricsId = (int)existingUBio.id;
                }
                else
                {
                    // No device row at all — insert a skeleton row (NULLs for all time fields).
                    // The actual edited time is saved in t_biometrics below.
                    await _db.ExecuteAsync(@"
                        INSERT INTO u_biometrics
                            (employeeNo, biometricsDate, biometricsTimeIn, biometricsDateOut,
                             biometricsTimeOut, biometricsDeviceLog, isActive)
                        VALUES
                            (@employeeNo, @workDate, NULL, NULL, NULL, 'modified', 1)",
                        new { employeeNo, workDate = workDate.Date });

                    uBiometricsId = (int)(await _db.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()"));
                }

                // ── STEP 2: Upsert into t_biometrics ─────────────────────────────
                var existingTBio = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT id
                    FROM t_biometrics
                    WHERE employeeNo      = @employeeNo
                      AND biometricsDate  = @workDate
                      AND u_biometricsID  = @uBiometricsId
                    LIMIT 1",
                    new { employeeNo, workDate = workDate.Date, uBiometricsId });

                if (existingTBio != null)
                {
                    // UPDATE — only overwrite Time In, preserve existing Time Out
                    await _db.ExecuteAsync(@"
                        UPDATE t_biometrics
                        SET biometricsTimeIn   = @timeIn,
                            tagStatus          = 'modified',
                            statusName         = 'modified',
                            dtLastModified     = NOW(),
                            lastModifiedByUser = @modifiedBy,
                            isActive           = 1
                        WHERE id = @id",
                        new
                        {
                            timeIn = timeInTs,
                            modifiedBy = EmployeeNo ?? "SYSTEM",
                            id = (int)existingTBio.id
                        });
                }
                else
                {
                    // INSERT fresh t_biometrics row
                    await _db.ExecuteAsync(@"
                        INSERT INTO t_biometrics
                            (employeeNo, u_biometricsID, biometricsDate, DateOut,
                             biometricsTimeIn, biometricsTimeOut,
                             tagStatus, statusName,
                             isActive, dtAdded, addedByUser)
                        VALUES
                            (@employeeNo, @uBiometricsId, @workDate, @dateOut,
                             @timeIn, @timeOut,
                             'modified', 'modified',
                             1, NOW(), @addedBy)",
                        new
                        {
                            employeeNo,
                            uBiometricsId,
                            workDate = workDate.Date,
                            dateOut = timeOutDate ?? workDate.Date,
                            timeIn = timeInTs,
                            timeOut = timeOutTs,   // may be NULL — perfectly fine
                            addedBy = EmployeeNo ?? "SYSTEM"
                        });
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // TIME OUT  (EDIT / READWRITE / FULL only)
        // Same design as TIME IN — mirror logic for the out side.
        // u_biometrics is only tagged. t_biometrics holds the edit.
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> InsertOrUpdateBiometricsTimeOut(
            string employeeNo,
            DateTime workDate,
            DateTime? timeOutDate,
            string timeOut,
            string? existingTimeIn)
        {
            if (!AccessHelper.CanEdit(HttpContext, MODULE))
                return Json(new { success = false, message = "Access denied. You do not have permission to edit Time Out." });

            if (!CanViewEmployee(employeeNo))
                return Json(new { success = false, message = "Access denied. This employee is outside your data scope." });

            try
            {
                DateTime actualOutDate = timeOutDate ?? workDate;
                TimeSpan timeOutTs = TimeSpan.Parse(timeOut);
                TimeSpan? timeInTs = null;

                if (!string.IsNullOrWhiteSpace(existingTimeIn) && TimeSpan.TryParse(existingTimeIn, out var parsedTimeIn))
                    timeInTs = parsedTimeIn;

                // ── STEP 1: Find / tag u_biometrics row ──────────────────────────
                var existingUBio = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT id, biometricsDeviceLog
                    FROM u_biometrics
                    WHERE employeeNo     = @employeeNo
                      AND biometricsDate = @workDate
                    ORDER BY
                        CASE WHEN biometricsDeviceLog = 'modified' THEN 1 ELSE 0 END ASC,
                        id ASC
                    LIMIT 1",
                    new { employeeNo, workDate = workDate.Date });

                int uBiometricsId;

                if (existingUBio != null)
                {
                    // Tag as modified — do NOT touch biometricsTimeIn / biometricsTimeOut
                    await _db.ExecuteAsync(@"
                        UPDATE u_biometrics
                        SET biometricsDeviceLog = 'modified',
                            isActive            = 1
                        WHERE id = @id",
                        new { id = (int)existingUBio.id });

                    uBiometricsId = (int)existingUBio.id;
                }
                else
                {
                    // No device row — insert skeleton with NULLs for all time fields
                    await _db.ExecuteAsync(@"
                        INSERT INTO u_biometrics
                            (employeeNo, biometricsDate, biometricsTimeIn, biometricsDateOut,
                             biometricsTimeOut, biometricsDeviceLog, isActive)
                        VALUES
                            (@employeeNo, @workDate, NULL, NULL, NULL, 'modified', 1)",
                        new { employeeNo, workDate = workDate.Date });

                    uBiometricsId = (int)(await _db.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()"));
                }

                // ── STEP 2: Upsert into t_biometrics ─────────────────────────────
                var existingTBio = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT id
                    FROM t_biometrics
                    WHERE employeeNo     = @employeeNo
                      AND biometricsDate = @workDate
                      AND u_biometricsID = @uBiometricsId
                    LIMIT 1",
                    new { employeeNo, workDate = workDate.Date, uBiometricsId });

                if (existingTBio != null)
                {
                    // UPDATE — only overwrite Time Out, preserve existing Time In
                    await _db.ExecuteAsync(@"
                        UPDATE t_biometrics
                        SET biometricsTimeOut  = @timeOut,
                            DateOut            = @dateOut,
                            tagStatus          = 'modified',
                            statusName         = 'modified',
                            dtLastModified     = NOW(),
                            lastModifiedByUser = @modifiedBy,
                            isActive           = 1
                        WHERE id = @id",
                        new
                        {
                            timeOut = timeOutTs,
                            dateOut = actualOutDate.Date,
                            modifiedBy = EmployeeNo ?? "SYSTEM",
                            id = (int)existingTBio.id
                        });
                }
                else
                {
                    // INSERT fresh t_biometrics row
                    await _db.ExecuteAsync(@"
                        INSERT INTO t_biometrics
                            (employeeNo, u_biometricsID, biometricsDate, DateOut,
                             biometricsTimeIn, biometricsTimeOut,
                             tagStatus, statusName,
                             isActive, dtAdded, addedByUser)
                        VALUES
                            (@employeeNo, @uBiometricsId, @workDate, @dateOut,
                             @timeIn, @timeOut,
                             'modified', 'modified',
                             1, NOW(), @addedBy)",
                        new
                        {
                            employeeNo,
                            uBiometricsId,
                            workDate = workDate.Date,
                            dateOut = actualOutDate.Date,
                            timeIn = timeInTs,   // may be NULL — perfectly fine
                            timeOut = timeOutTs,
                            addedBy = EmployeeNo ?? "SYSTEM"
                        });
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // SUBMIT BIOMETRICS REQUEST  (EDIT / READWRITE only)
        // Creates/updates a t_biometrics row with statusName = 'pending'
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SubmitBiometricsRequest(
            string employeeNo,
            DateTime workDate,
            string requestType,
            string? timeIn,
            string? timeOut,
            string? timeOutDate,
            string? existingTimeOut,
            string? existingTimeOutDate,
            string? existingTimeIn,
            string reason)
        {
            // Only EDIT or READWRITE may submit requests (FULL saves directly)
            var accessLevel = Helpers.AccessHelper.GetAccess(HttpContext, MODULE);
            if (accessLevel == "FULL")
                return Json(new { success = false, message = "Full access users save directly." });

            if (!Helpers.AccessHelper.CanEdit(HttpContext, MODULE))
                return Json(new { success = false, message = "Access denied." });

            if (!CanViewEmployee(employeeNo))
                return Json(new { success = false, message = "Access denied. This employee is outside your data scope." });

            if (string.IsNullOrWhiteSpace(reason))
                return Json(new { success = false, message = "Reason is required." });

            try
            {
                // ── STEP 1: Find or create u_biometrics skeleton ──
                var existingUBio = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT id, biometricsDeviceLog
                    FROM u_biometrics
                    WHERE employeeNo     = @employeeNo
                      AND biometricsDate = @workDate
                    ORDER BY
                        CASE WHEN biometricsDeviceLog = 'modified' THEN 1 ELSE 0 END ASC,
                        id ASC
                    LIMIT 1",
                    new { employeeNo, workDate = workDate.Date });

                int uBiometricsId;

                if (existingUBio != null)
                {
                    await _db.ExecuteAsync(@"
                        UPDATE u_biometrics
                        SET biometricsDeviceLog = 'modified',
                            isActive            = 1
                        WHERE id = @id",
                        new { id = (int)existingUBio.id });

                    uBiometricsId = (int)existingUBio.id;
                }
                else
                {
                    await _db.ExecuteAsync(@"
                        INSERT INTO u_biometrics
                            (employeeNo, biometricsDate, biometricsTimeIn, biometricsDateOut,
                             biometricsTimeOut, biometricsDeviceLog, isActive)
                        VALUES
                            (@employeeNo, @workDate, NULL, NULL, NULL, 'modified', 1)",
                        new { employeeNo, workDate = workDate.Date });

                    uBiometricsId = (int)(await _db.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()"));
                }

                // ── STEP 2: Parse time values ──
                TimeSpan? timeInTs = null;
                TimeSpan? timeOutTs = null;
                DateTime? dateOutDt = null;

                TimeSpan parsedTI = default;
                TimeSpan parsedTO = default;

                bool hasNewTI = !string.IsNullOrWhiteSpace(timeIn)
                                && TimeSpan.TryParse(timeIn, out parsedTI);
                if (hasNewTI) timeInTs = parsedTI;

                bool hasNewTO = !string.IsNullOrWhiteSpace(timeOut)
                                && TimeSpan.TryParse(timeOut, out parsedTO);
                if (hasNewTO) timeOutTs = parsedTO;

                if (!string.IsNullOrWhiteSpace(timeOutDate)
                    && DateTime.TryParse(timeOutDate, out var parsedTODate))
                    dateOutDt = parsedTODate.Date;

                // ── STEP 3: Check if t_biometrics row already exists ──
                var existingTBio = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT id
                    FROM t_biometrics
                    WHERE employeeNo     = @employeeNo
                      AND biometricsDate = @workDate
                      AND u_biometricsID = @uBiometricsId
                    LIMIT 1",
                    new { employeeNo, workDate = workDate.Date, uBiometricsId });

                if (existingTBio != null)
                {
                    // Only save the side the user actually changed — NULL the other side
                    var saveTimeIn = hasNewTI ? timeInTs : (TimeSpan?)null;
                    var saveTimeOut = hasNewTO ? timeOutTs : (TimeSpan?)null;
                    var saveDateOut = hasNewTO ? (dateOutDt ?? workDate.Date) : workDate.Date;

                    await _db.ExecuteAsync(@"
                        UPDATE t_biometrics
                        SET biometricsTimeIn   = @timeIn,
                            biometricsTimeOut  = @timeOut,
                            DateOut            = @dateOut,
                            tagStatus          = 'modified',
                            statusName         = 'pending',
                            remarks            = @reason,
                            dtLastModified     = NOW(),
                            lastModifiedByUser = @modifiedBy,
                            isActive           = 1
                        WHERE id = @id",
                        new
                        {
                            timeIn = saveTimeIn,
                            timeOut = saveTimeOut,
                            dateOut = saveDateOut,
                            reason,
                            modifiedBy = EmployeeNo ?? "SYSTEM",
                            id = (int)existingTBio.id
                        });
                }
                else
                {
                    // Only populate the side that was actually submitted — NULL the other side
                    await _db.ExecuteAsync(@"
                        INSERT INTO t_biometrics
                            (employeeNo, u_biometricsID, biometricsDate, DateOut,
                             biometricsTimeIn, biometricsTimeOut,
                             tagStatus, statusName, remarks,
                             isActive, dtAdded, addedByUser)
                        VALUES
                            (@employeeNo, @uBiometricsId, @workDate, @dateOut,
                             @timeIn, @timeOut,
                             'modified', 'pending', @reason,
                             1, NOW(), @addedBy)",
                        new
                        {
                            employeeNo,
                            uBiometricsId,
                            workDate = workDate.Date,
                            dateOut = hasNewTO ? (dateOutDt ?? workDate.Date) : workDate.Date,
                            timeIn = hasNewTI ? timeInTs : (TimeSpan?)null,
                            timeOut = hasNewTO ? timeOutTs : (TimeSpan?)null,
                            reason,
                            addedBy = EmployeeNo ?? "SYSTEM"
                        });
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // CANCEL BIOMETRICS REQUEST  (requestor only)
        // Removes the pending t_biometrics row and cleans up skeleton
        // u_biometrics if it was created solely for this request
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> CancelBiometricsRequest(
            string employeeNo,
            DateTime workDate,
            int? uBiometricsId)
        {
            if (!CanViewEmployee(employeeNo))
                return Json(new { success = false, message = "Access denied." });

            try
            {
                // Only the requestor or a FULL access user may cancel
                var accessLevel = Helpers.AccessHelper.GetAccess(HttpContext, MODULE);
                bool isFullAccess = accessLevel == "FULL";

                // Non-full users can only cancel their own pending requests
                if (!isFullAccess && employeeNo != EmployeeNo)
                    return Json(new { success = false, message = "You can only cancel your own requests." });

                // ── Find the pending t_biometrics row ─────────────────────────────
                string tBioQuery = uBiometricsId.HasValue
                    ? @"SELECT id FROM t_biometrics
                WHERE employeeNo     = @employeeNo
                  AND biometricsDate = @workDate
                  AND u_biometricsID = @uBiometricsId
                  AND statusName     = 'pending'
                LIMIT 1"
                    : @"SELECT id FROM t_biometrics
                WHERE employeeNo     = @employeeNo
                  AND biometricsDate = @workDate
                  AND statusName     = 'pending'
                LIMIT 1";

                var tBioRow = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    tBioQuery,
                    new { employeeNo, workDate = workDate.Date, uBiometricsId });

                if (tBioRow != null)
                {
                    await _db.ExecuteAsync(
                        "DELETE FROM t_biometrics WHERE id = @id",
                        new { id = (int)tBioRow.id });
                }

                // ── If the u_biometrics row was a skeleton (NULL times, 'modified'), clean it up ──
                if (uBiometricsId.HasValue)
                {
                    var uBioRow = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT id, biometricsTimeIn, biometricsTimeOut, biometricsDeviceLog
                FROM u_biometrics
                WHERE id = @id LIMIT 1",
                        new { id = uBiometricsId.Value });

                    if (uBioRow != null
                        && (string)uBioRow.biometricsDeviceLog == "modified"
                        && uBioRow.biometricsTimeIn == null
                        && uBioRow.biometricsTimeOut == null)
                    {
                        // Skeleton row with no device data — safe to delete
                        await _db.ExecuteAsync(
                            "DELETE FROM u_biometrics WHERE id = @id",
                            new { id = (int)uBioRow.id });
                    }
                }

                // ── Return original times so the UI can restore the cells ─────────
                var originalUBio = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT biometricsDate, biometricsTimeIn, biometricsDateOut, biometricsTimeOut
            FROM u_biometrics
            WHERE employeeNo     = @employeeNo
              AND biometricsDate = @workDate
              AND isActive       = 1
            ORDER BY id ASC
            LIMIT 1",
                    new { employeeNo, workDate = workDate.Date });

                string? originalTimeIn = null;
                string? originalTimeOut = null;

                if (originalUBio != null)
                {
                    if (originalUBio.biometricsTimeIn != null)
                    {
                        var tiDate = (DateTime)originalUBio.biometricsDate;
                        var tiSpan = (TimeSpan)originalUBio.biometricsTimeIn;
                        originalTimeIn = tiDate.Add(tiSpan).ToString("yyyy-MM-ddTHH:mm:ss");
                    }
                    if (originalUBio.biometricsTimeOut != null)
                    {
                        var toDate = originalUBio.biometricsDateOut != null
                            ? (DateTime)originalUBio.biometricsDateOut
                            : (DateTime)originalUBio.biometricsDate;
                        var toSpan = (TimeSpan)originalUBio.biometricsTimeOut;
                        originalTimeOut = toDate.Add(toSpan).ToString("yyyy-MM-ddTHH:mm:ss");
                    }
                }

                return Json(new
                {
                    success = true,
                    originalTimeIn = originalTimeIn,
                    originalTimeOut = originalTimeOut
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // GET PENDING BIOMETRICS REQUESTS  (FULL access only)
        // Returns all pending t_biometrics rows within the user's scope
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetPendingBiometricsRequests(
            string? branchCode,
            DateTime? dateFrom,
            DateTime? dateTo)
        {
            if (!Helpers.AccessHelper.CanDelete(HttpContext, MODULE))
                return Json(new { success = false, message = "Access denied. Full access required." });

            // Resolve scope to know which employees this FULL user can approve
            var resolvedBranch = string.IsNullOrEmpty(branchCode) || branchCode == "ALL" ? "" : branchCode;
            var (allowedEmployeeNos, effectiveBranch) = ResolveScope(resolvedBranch);

            try
            {
                var sql = @"
            SELECT
                tb.id,
                tb.employeeNo,
                CONCAT(eb.lastName, ', ', eb.firstName) AS fullName,
                eb.branchCode,
                sb.branchName,
                tb.biometricsDate  AS workDate,
                tb.biometricsTimeIn,
                tb.biometricsTimeOut,
                tb.DateOut,
                tb.remarks,
                tb.dtAdded,
                tb.addedByUser,
                -- original u_biometrics times
                ub.biometricsTimeIn  AS originalTimeIn,
                ub.biometricsTimeOut AS originalTimeOut,
                ub.biometricsDate    AS originalDate,
                ub.biometricsDateOut AS originalDateOut,
                tb.u_biometricsID    AS uBiometricsId
            FROM t_biometrics tb
            JOIN e_basicinfo eb
              ON eb.employeeNo = tb.employeeNo AND eb.isActive = 1
            LEFT JOIN s_branch sb
              ON sb.branchCode = eb.branchCode
            LEFT JOIN u_biometrics ub
              ON ub.id = tb.u_biometricsID
            WHERE tb.statusName = 'pending'
              AND tb.isActive   = 1
              AND (@BranchCode = '' OR eb.branchCode = @BranchCode)
              AND (@DateFrom IS NULL OR tb.biometricsDate >= @DateFrom)
              AND (@DateTo   IS NULL OR tb.biometricsDate <= @DateTo)
            ORDER BY tb.dtAdded DESC";

                var rows = (await _db.QueryAsync<dynamic>(sql, new
                {
                    BranchCode = effectiveBranch,
                    DateFrom = dateFrom?.Date,
                    DateTo = dateTo?.Date
                })).ToList();

                // Post-filter to scope if employee-based
                if (allowedEmployeeNos != null)
                    rows = rows.Where(r => allowedEmployeeNos.Contains((string)r.employeeNo)).ToList();

                // Format for the DataTable
                var result = rows.Select(r =>
                {
                    // Build requested time in display
                    string? reqTimeIn = null;
                    if (r.biometricsTimeIn != null)
                    {
                        var d = (DateTime)r.workDate;
                        var ts = (TimeSpan)r.biometricsTimeIn;
                        reqTimeIn = d.Add(ts).ToString("yyyy-MM-dd hh:mm tt");
                    }

                    // Build requested time out display
                    string? reqTimeOut = null;
                    if (r.biometricsTimeOut != null)
                    {
                        var d = r.DateOut != null ? (DateTime)r.DateOut : (DateTime)r.workDate;
                        var ts = (TimeSpan)r.biometricsTimeOut;
                        reqTimeOut = d.Add(ts).ToString("yyyy-MM-dd hh:mm tt");
                    }

                    // Build original time in display
                    string? origTimeIn = null;
                    if (r.originalTimeIn != null && r.originalDate != null)
                    {
                        var d = (DateTime)r.originalDate;
                        var ts = (TimeSpan)r.originalTimeIn;
                        origTimeIn = d.Add(ts).ToString("yyyy-MM-dd hh:mm tt");
                    }

                    // Build original time out display
                    string? origTimeOut = null;
                    if (r.originalTimeOut != null)
                    {
                        var d = r.originalDateOut != null ? (DateTime)r.originalDateOut : (DateTime)r.originalDate!;
                        var ts = (TimeSpan)r.originalTimeOut;
                        origTimeOut = d.Add(ts).ToString("yyyy-MM-dd hh:mm tt");
                    }

                    return new
                    {
                        id = (int)r.id,
                        employeeNo = (string)r.employeeNo,
                        fullName = (string)r.fullName,
                        branchName = (string?)r.branchName,
                        workDate = ((DateTime)r.workDate).ToString("yyyy-MM-dd"),
                        requestedTimeIn = reqTimeIn,
                        requestedTimeOut = reqTimeOut,
                        originalTimeIn = origTimeIn,
                        originalTimeOut = origTimeOut,
                        reason = (string?)r.remarks,
                        submittedBy = (string?)r.addedByUser,
                        submittedAt = ((DateTime)r.dtAdded).ToString("yyyy-MM-dd HH:mm"),
                        uBiometricsId = (int?)r.uBiometricsId
                    };
                }).ToList();

                return Json(new { data = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // APPROVE BIOMETRICS REQUEST  (FULL access only)
        // Changes statusName from 'pending' to 'modified'
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ApproveBiometricsRequest(int id)
        {
            if (!Helpers.AccessHelper.CanDelete(HttpContext, MODULE))
                return Json(new { success = false, message = "Access denied. Full access required." });

            try
            {
                var row = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT id, employeeNo FROM t_biometrics WHERE id = @id AND statusName = 'pending' LIMIT 1",
                    new { id });

                if (row == null)
                    return Json(new { success = false, message = "Pending request not found." });

                if (!CanViewEmployee((string)row.employeeNo))
                    return Json(new { success = false, message = "Access denied for this employee." });

                await _db.ExecuteAsync(@"
            UPDATE t_biometrics
            SET statusName         = 'modified',
                dtLastModified     = NOW(),
                lastModifiedByUser = @approvedBy
            WHERE id = @id",
                    new { id, approvedBy = EmployeeNo ?? "SYSTEM" });

                return Json(new { success = true, message = "Request approved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // DECLINE BIOMETRICS REQUEST  (FULL access only)
        // Case 1 (had original device record): deletes t_biometrics row,
        //         u_biometrics stays (original device times preserved)
        // Case 2 (no original device record): deletes t_biometrics AND
        //         the skeleton u_biometrics row
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> DeclineBiometricsRequest(int id, int? uBiometricsId)
        {
            if (!Helpers.AccessHelper.CanDelete(HttpContext, MODULE))
                return Json(new { success = false, message = "Access denied. Full access required." });

            try
            {
                var row = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT id, employeeNo FROM t_biometrics WHERE id = @id AND statusName = 'pending' LIMIT 1",
                    new { id });

                if (row == null)
                    return Json(new { success = false, message = "Pending request not found." });

                if (!CanViewEmployee((string)row.employeeNo))
                    return Json(new { success = false, message = "Access denied for this employee." });

                // Delete the t_biometrics request row
                await _db.ExecuteAsync("DELETE FROM t_biometrics WHERE id = @id", new { id });

                // Check if u_biometrics is a skeleton (no device times) and clean it up
                if (uBiometricsId.HasValue)
                {
                    var uBioRow = await _db.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT id, biometricsTimeIn, biometricsTimeOut, biometricsDeviceLog
                FROM u_biometrics
                WHERE id = @id LIMIT 1",
                        new { id = uBiometricsId.Value });

                    if (uBioRow != null
                        && (string)uBioRow.biometricsDeviceLog == "modified"
                        && uBioRow.biometricsTimeIn == null
                        && uBioRow.biometricsTimeOut == null)
                    {
                        // No real device record — delete skeleton
                        await _db.ExecuteAsync(
                            "DELETE FROM u_biometrics WHERE id = @id",
                            new { id = (int)uBioRow.id });
                    }
                    // Else: real device record exists — leave u_biometrics as-is
                }

                return Json(new { success = true, message = "Request declined." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // LOCK DTR  (HR CASUAL only — locks branchCode = Casual)
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> LockDTR(
            DateTime dateFrom,
            DateTime dateTo,
            int cutOffType,
            string dateMonth)
        {
            // Server-side guard: only HR CASUAL or Super Admin may lock
            if (RoleCode != "HR CASUAL" && RoleCode != "RL-000000")
                return Json(new { success = false, message = "Access denied. Only HR Casual or Admin may lock DTR." });

            // Always enforce branchCode = Casual regardless of what the client sent
            const string casualBranch = "Casual";

            // Prevent double-locking
            bool alreadyLocked = await _service.IsDateRangePostedAsync(dateFrom, dateTo, casualBranch);
            if (alreadyLocked)
                return Json(new { success = false, message = "DTR is already locked for this cutoff and branch." });

            try
            {
                int rows = await _db.ExecuteAsync(@"
                    UPDATE p_biometricsline
                    SET    statusName = 'posted'
                    WHERE  branchCode  = @branchCode
                      AND  cutOffType  = @cutOffType
                      AND  dateMonth   = @dateMonth
                      AND  dateFrom   >= @dateFrom
                      AND  dateTo     <= @dateTo
                      AND  isActive    = 1
                      AND  statusName != 'posted'",
                    new
                    {
                        branchCode = casualBranch,
                        cutOffType,
                        dateMonth,
                        dateFrom = dateFrom.Date,
                        dateTo = dateTo.Date
                    });

                return Json(new { success = true, rowsUpdated = rows });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // UNLOCK DTR  (RL-000000 only — unlocks branchCode = Casual)
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> UnlockDTR(
            DateTime dateFrom,
            DateTime dateTo,
            int cutOffType,
            string dateMonth)
        {
            // Server-side guard: only super admin may unlock
            if (RoleCode != "RL-000000")
                return Json(new { success = false, message = "Access denied. Only Super Admin may unlock DTR." });

            // Always enforce branchCode = Casual
            const string casualBranch = "Casual";

            // Must be locked first
            bool isLocked = await _service.IsDateRangePostedAsync(dateFrom, dateTo, casualBranch);
            if (!isLocked)
                return Json(new { success = false, message = "DTR is not locked for this cutoff and branch." });

            try
            {
                int rows = await _db.ExecuteAsync(@"
                    UPDATE p_biometricsline
                    SET    statusName = 'Open'
                    WHERE  branchCode  = @branchCode
                      AND  cutOffType  = @cutOffType
                      AND  dateMonth   = @dateMonth
                      AND  dateFrom   >= @dateFrom
                      AND  dateTo     <= @dateTo
                      AND  isActive    = 1
                      AND  statusName  = 'posted'",
                    new
                    {
                        branchCode = casualBranch,
                        cutOffType,
                        dateMonth,
                        dateFrom = dateFrom.Date,
                        dateTo = dateTo.Date
                    });

                return Json(new { success = true, rowsUpdated = rows });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // IS DTR POSTED CHECK
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> IsDtrPosted(int cutOffType, string dateMonth, int dateYear, string branchCode = "")
        {
            if (!int.TryParse(dateMonth, out int monthNumber) || monthNumber < 1 || monthNumber > 12)
                return Json(new { posted = false });

            // Never let Casual lock block other branches in the Process DTR check
            // If no branchCode passed, exclude Casual from the check entirely
            string effectiveBranch = string.IsNullOrEmpty(branchCode) ? "" : branchCode;

            string monthName = new DateTime(dateYear, monthNumber, 1).ToString("MMMM");
            bool posted = await _service.IsDtrPostedAsync(cutOffType, monthName, dateYear, effectiveBranch);
            return Json(new { posted });
        }

        // ═════════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolves which employees this user may see, based on s_role.scopeType.
        ///
        /// Returns:
        ///   allowedEmployeeNos — null  = no employee-level restriction
        ///                                (e.g. ALL or BRANCH — service handles it via branchCode)
        ///                        set   = post-filter the service result to only these employees
        ///                        empty = user sees nobody
        ///   effectiveBranch    — branch string to pass into GetSummaryAsync / GetDailyRowsAsync
        /// </summary>
        private (HashSet<string>? allowedEmployeeNos, string effectiveBranch) ResolveScope(string requestedBranchCode)
        {
            // Admin always bypasses all scope rules
            if (RoleCode == "RL-000000")
                return (null, requestedBranchCode ?? "");

            var role = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT scopeType,
                       allowedRanks,
                       allowedBranches,
                       allowedDepartments,
                       allowedPositions,
                       allowedEmploymentStatuses
                FROM s_role
                WHERE roleCode = @roleCode
                  AND isActive = 1
                LIMIT 1",
                new { roleCode = RoleCode });

            // No role row found → safest fallback is own record only
            if (role == null)
                return (new HashSet<string> { EmployeeNo }, "");

            string scopeType = (string)(role.scopeType ?? "OWN_ONLY");

            switch (scopeType)
            {
                // ── All employees, no restriction ──────────────────────
                case "ALL":
                    return (null, requestedBranchCode ?? "");

                // ── Own record only ────────────────────────────────────
                case "OWN_ONLY":
                    return (new HashSet<string> { EmployeeNo }, "");

                // ── Specific branch(es) ────────────────────────────────
                case "BRANCH":
                    {
                        string raw = (string)(role.allowedBranches ?? "");
                        var allowed = raw.Split(',')
                                         .Select(b => b.Trim())
                                         .Where(b => !string.IsNullOrEmpty(b))
                                         .ToList();

                        if (!allowed.Any())
                            return (new HashSet<string>(), "");

                        string effectiveBranch = (!string.IsNullOrEmpty(requestedBranchCode)
                                                  && allowed.Contains(requestedBranchCode))
                            ? requestedBranchCode
                            : allowed.First();

                        return (null, effectiveBranch);
                    }

                // ── Same department as the logged-in user ──────────────
                case "DEPARTMENT":
                    {
                        var myDept = _db.QueryFirstOrDefault<string>(
                            "SELECT departmentCode FROM e_basicinfo WHERE employeeNo = @e AND isActive = 1",
                            new { e = EmployeeNo });

                        if (string.IsNullOrEmpty(myDept))
                            return (new HashSet<string> { EmployeeNo }, "");

                        var empNos = _db.Query<string>(
                            "SELECT employeeNo FROM e_basicinfo WHERE departmentCode = @d AND isActive = 1",
                            new { d = myDept });

                        return (new HashSet<string>(empNos), requestedBranchCode ?? "");
                    }

                // ── Specific rank(s) ───────────────────────────────────
                case "RANK_FILTER":
                    {
                        string raw = (string)(role.allowedRanks ?? "");
                        if (string.IsNullOrWhiteSpace(raw))
                            return (new HashSet<string> { EmployeeNo }, "");

                        var ranks = raw.Split(',').Select(r => r.Trim())
                                       .Where(r => !string.IsNullOrEmpty(r)).ToArray();
                        var empNos = _db.Query<string>(
                            "SELECT employeeNo FROM e_basicinfo WHERE rankCode IN @ranks AND isActive = 1",
                            new { ranks });

                        return (new HashSet<string>(empNos), requestedBranchCode ?? "");
                    }

                // ── Specific position(s) ───────────────────────────────
                case "POSITION_FILTER":
                    {
                        string raw = (string)(role.allowedPositions ?? "");
                        if (string.IsNullOrWhiteSpace(raw))
                            return (new HashSet<string> { EmployeeNo }, "");

                        var positions = raw.Split(',').Select(p => p.Trim())
                                           .Where(p => !string.IsNullOrEmpty(p)).ToArray();
                        var empNos = _db.Query<string>(
                            "SELECT employeeNo FROM e_basicinfo WHERE positionCode IN @positions AND isActive = 1",
                            new { positions });

                        return (new HashSet<string>(empNos), requestedBranchCode ?? "");
                    }

                // ── Specific employment status(es) ─────────────────────
                case "EMPLOYMENT_STATUS":
                    {
                        string raw = (string)(role.allowedEmploymentStatuses ?? "");
                        if (string.IsNullOrWhiteSpace(raw))
                            return (new HashSet<string> { EmployeeNo }, "");

                        var statuses = raw.Split(',').Select(s => s.Trim())
                                          .Where(s => !string.IsNullOrEmpty(s)).ToArray();
                        var empNos = _db.Query<string>(
                            "SELECT employeeNo FROM e_basicinfo WHERE employmentStatus IN @statuses AND isActive = 1",
                            new { statuses });

                        return (new HashSet<string>(empNos), requestedBranchCode ?? "");
                    }

                // ── Custom: OR across all configured filters ───────────
                case "CUSTOM":
                    {
                        var conditions = new List<string>();
                        var p = new DynamicParameters();

                        string ranksRaw = (string)(role.allowedRanks ?? "");
                        if (!string.IsNullOrWhiteSpace(ranksRaw))
                        {
                            var arr = ranksRaw.Split(',').Select(r => r.Trim())
                                              .Where(r => !string.IsNullOrEmpty(r)).ToArray();
                            conditions.Add("rankCode IN @ranks");
                            p.Add("@ranks", arr);
                        }

                        string branchesRaw = (string)(role.allowedBranches ?? "");
                        if (!string.IsNullOrWhiteSpace(branchesRaw))
                        {
                            var arr = branchesRaw.Split(',').Select(b => b.Trim())
                                                 .Where(b => !string.IsNullOrEmpty(b)).ToArray();
                            conditions.Add("branchCode IN @branches");
                            p.Add("@branches", arr);
                        }

                        string deptsRaw = (string)(role.allowedDepartments ?? "");
                        if (!string.IsNullOrWhiteSpace(deptsRaw))
                        {
                            var arr = deptsRaw.Split(',').Select(d => d.Trim())
                                              .Where(d => !string.IsNullOrEmpty(d)).ToArray();
                            conditions.Add("departmentCode IN @depts");
                            p.Add("@depts", arr);
                        }

                        string posRaw = (string)(role.allowedPositions ?? "");
                        if (!string.IsNullOrWhiteSpace(posRaw))
                        {
                            var arr = posRaw.Split(',').Select(pos => pos.Trim())
                                            .Where(pos => !string.IsNullOrEmpty(pos)).ToArray();
                            conditions.Add("positionCode IN @positions");
                            p.Add("@positions", arr);
                        }

                        string statusRaw = (string)(role.allowedEmploymentStatuses ?? "");
                        if (!string.IsNullOrWhiteSpace(statusRaw))
                        {
                            var arr = statusRaw.Split(',').Select(s => s.Trim())
                                               .Where(s => !string.IsNullOrEmpty(s)).ToArray();
                            conditions.Add("employmentStatus IN @statuses");
                            p.Add("@statuses", arr);
                        }

                        if (!conditions.Any())
                            return (new HashSet<string> { EmployeeNo }, "");

                        string sql = $@"SELECT employeeNo FROM e_basicinfo
                                    WHERE isActive = 1
                                      AND ({string.Join(" OR ", conditions)})";
                        var empNos = _db.Query<string>(sql, p);

                        return (new HashSet<string>(empNos), requestedBranchCode ?? "");
                    }

                // ── Safest default ─────────────────────────────────────
                default:
                    return (new HashSet<string> { EmployeeNo }, "");
            }
        }

        /// <summary>
        /// Quick check: can the current user view a specific employee?
        /// Used by detail, edit, and process endpoints.
        /// </summary>
        private bool CanViewEmployee(string employeeNo)
        {
            if (RoleCode == "RL-000000") return true;
            if (employeeNo == EmployeeNo) return true;   // always own record

            var (allowedSet, _) = ResolveScope("");
            if (allowedSet == null) return true;           // null = unrestricted (ALL / BRANCH)
            return allowedSet.Contains(employeeNo);
        }
    }
}