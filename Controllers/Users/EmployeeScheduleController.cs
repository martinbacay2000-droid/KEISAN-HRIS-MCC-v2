using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FscheduleM")]
    public class EmployeeScheduleController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public EmployeeScheduleController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Users/EmployeeSchedule.cshtml");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Data scope + hidden employees filters — delegated to DataScopeHelper
        // Table alias "a" matches this controller's e_basicinfo join alias
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyDataScopeFilter(StringBuilder query, DynamicParameters parameters)
        {
            DataScopeHelper.ApplyDataScopeFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "a");
        }

        private void ApplyHiddenEmployeesFilter(StringBuilder query, DynamicParameters parameters)
        {
            DataScopeHelper.ApplyHiddenEmployeesFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "a");
        }

        private bool CanViewEmployee(string employeeNo)
        {
            return DataScopeHelper.CanViewEmployee(_db, EmployeeNo, RoleCode, employeeNo);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Queries
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public JsonResult GetScheduleList(string branch, string department, string dateFrom, string dateTo, string employeeNo)
        {
            var query = new StringBuilder(@"
            SELECT
                MIN(e.id) as id,
                e.employeeNo,
                CONCAT(MAX(a.lastName), ', ', MAX(a.firstName)) AS fullname,
                GROUP_CONCAT(DISTINCT DATE_FORMAT(e.effectivityDate, '%Y-%m-%d') ORDER BY e.effectivityDate DESC SEPARATOR ', ') AS effectivityDate,
                e.scheduleTypeCode,
                MAX(s.scheduleTypeName) as scheduleTypeName,
                GROUP_CONCAT(DISTINCT e.weekdayName ORDER BY
                    FIELD(e.weekdayName, 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday')
                    SEPARATOR ', ') as weekdays,
                COUNT(DISTINCT e.weekdayName) as dayCount,
                COUNT(DISTINCT DATE(e.effectivityDate)) as dateCount
            FROM e_schedule e
            LEFT JOIN e_basicinfo a ON a.employeeNo = e.employeeNo
            LEFT JOIN s_scheduleType s ON s.scheduleTypeCode = e.scheduleTypeCode
            WHERE e.isActive = 1 AND a.isActive = 1");

            var parameters = new DynamicParameters();

            ApplyDataScopeFilter(query, parameters);
            ApplyHiddenEmployeesFilter(query, parameters);

            if (!string.IsNullOrWhiteSpace(employeeNo))
            {
                query.Append(" AND e.employeeNo = @employeeNo");
                parameters.Add("@employeeNo", employeeNo);
            }

            if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                query.Append(" AND a.branchCode = @branch");
                parameters.Add("@branch", branch);
            }

            if (!string.IsNullOrWhiteSpace(department) && !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                query.Append(" AND a.departmentCode = @department");
                parameters.Add("@department", department);
            }

            if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
            {
                // Normalize both mm/dd/yyyy and yyyy-mm-dd formats to DateTime
                if (!DateTime.TryParse(dateFrom, out DateTime parsedFrom))
                    parsedFrom = DateTime.MinValue;
                if (!DateTime.TryParse(dateTo, out DateTime parsedTo))
                    parsedTo = DateTime.MaxValue;

                Console.WriteLine($"[GetScheduleList] dateFrom raw: {dateFrom} → parsed: {parsedFrom:yyyy-MM-dd}");
                Console.WriteLine($"[GetScheduleList] dateTo raw: {dateTo} → parsed: {parsedTo:yyyy-MM-dd}");

                query.Append(" AND DATE(e.effectivityDate) BETWEEN @dateFrom AND @dateTo");
                parameters.Add("@dateFrom", parsedFrom.ToString("yyyy-MM-dd"));
                parameters.Add("@dateTo", parsedTo.ToString("yyyy-MM-dd"));
            }

            query.Append(" GROUP BY e.employeeNo, e.scheduleTypeCode");
            query.Append(" ORDER BY fullname");

            var scheduleList = _db.Query<dynamic>(query.ToString(), parameters).ToList();
            return Json(new { data = scheduleList });
        }

        [HttpGet]
        public JsonResult GetWeeklySchedule(string employeeNo, string effectivityDate)
        {
            try
            {
                if (!CanViewEmployee(employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to view this employee's schedule." });

                var sql = @"
                    SELECT
                        e.id,
                        e.employeeNo,
                        e.weekdayName,
                        e.timeIn,
                        e.timeOut,
                        e.totalRenderHour,
                        e.totalBreaktimeMinute,
                        e.scheduleTypeCode,
                        s.scheduleTypeName,
                        DATE_FORMAT(e.effectivityDate, '%Y-%m-%d') AS effectivityDate,
                        CONCAT(a.lastName, ', ', a.firstName) AS fullname
                    FROM e_schedule e
                    LEFT JOIN e_basicinfo a ON a.employeeNo = e.employeeNo
                    LEFT JOIN s_scheduleType s ON s.scheduleTypeCode = e.scheduleTypeCode
                    WHERE e.employeeNo = @employeeNo
                    AND DATE(e.effectivityDate) = DATE(@effectivityDate)
                    AND e.isActive = 1
                    ORDER BY FIELD(e.weekdayName, 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday')";

                var schedules = _db.Query<dynamic>(sql, new { employeeNo, effectivityDate }).ToList();
                return Json(new { success = true, data = schedules });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetWeeklySchedule: {ex.Message}");
                return Json(new { success = false, message = "Error loading weekly schedule" });
            }
        }

        [HttpGet]
        public JsonResult GetSchedule(int id)
        {
            try
            {
                var sql = @"
                    SELECT
                        e.id,
                        e.employeeNo,
                        e.schedCode,
                        DATE_FORMAT(e.effectivityDate, '%Y-%m-%d') AS effectivityDate,
                        e.weekdayName,
                        e.timeIn,
                        e.timeOut,
                        e.totalRenderHour,
                        e.totalBreaktimeMinute,
                        e.scheduleTypeCode,
                        s.scheduleTypeName,
                        CONCAT(a.lastName, ', ', a.firstName) AS fullname
                    FROM e_schedule e
                    LEFT JOIN e_basicinfo a ON a.employeeNo = e.employeeNo
                    LEFT JOIN s_scheduleType s ON s.scheduleTypeCode = e.scheduleTypeCode
                    WHERE e.id = @Id AND e.isActive = 1";

                var schedule = _db.QueryFirstOrDefault<dynamic>(sql, new { Id = id });
                if (schedule == null) return Json(null);

                if (!CanViewEmployee(schedule.employeeNo))
                    return Json(new { error = "Access denied. You don't have permission to view this employee's schedule." });

                return Json(schedule);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetSchedule: {ex.Message}");
                return Json(null);
            }
        }

        [HttpGet]
        public JsonResult GetEmployeeAllSchedules(string employeeNo, string scheduleTypeCode)
        {
            try
            {
                if (!CanViewEmployee(employeeNo))
                    return Json(new { success = false, message = "Access denied." });

                var sql = @"
                    SELECT
                        e.id,
                        e.employeeNo,
                        e.weekdayName,
                        e.timeIn,
                        e.timeOut,
                        e.totalRenderHour,
                        e.totalBreaktimeMinute,
                        e.scheduleTypeCode,
                        s.scheduleTypeName,
                        DATE_FORMAT(e.effectivityDate, '%Y-%m-%d') AS effectivityDate,
                        CONCAT(a.lastName, ', ', a.firstName) AS fullname
                    FROM e_schedule e
                    LEFT JOIN e_basicinfo a ON a.employeeNo = e.employeeNo
                    LEFT JOIN s_scheduleType s ON s.scheduleTypeCode = e.scheduleTypeCode
                    WHERE e.employeeNo = @employeeNo
                    AND e.isActive = 1";

                if (!string.IsNullOrWhiteSpace(scheduleTypeCode))
                    sql += " AND e.scheduleTypeCode = @scheduleTypeCode";

                sql += @" ORDER BY e.effectivityDate ASC,
                 FIELD(e.weekdayName, 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday')";

                var schedules = _db.Query<dynamic>(sql, new { employeeNo, scheduleTypeCode }).ToList();
                return Json(new { success = true, data = schedules });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading schedules: {ex.Message}" });
            }
        }

        [HttpGet]
        public JsonResult GetEmployeeList()
        {
            try
            {
                var query = new StringBuilder(@"
                    SELECT
                        a.employeeNo,
                        CONCAT(a.lastName, ', ', a.firstName) AS employeeName
                    FROM e_basicinfo a
                    WHERE a.isActive = 1");

                var parameters = new DynamicParameters();

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenEmployeesFilter(query, parameters);

                query.Append(" ORDER BY a.lastName, a.firstName");

                return Json(_db.Query(query.ToString(), parameters).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public JsonResult GetScheduleTypes()
        {
            try
            {
                var sql = @"
                    SELECT scheduleTypeCode AS value, scheduleTypeName AS text
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

        [HttpPost]
        public JsonResult AddSchedule(userSchedule model)
        {
            try
            {
                if (!CanViewEmployee(model.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to manage this employee's schedule." });

                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                if (!string.IsNullOrWhiteSpace(model.scheduleTypeCode) &&
                    !RecordExists("s_scheduleType", "scheduleTypeCode", model.scheduleTypeCode))
                    return Json(new { success = false, message = "Schedule type not found!" });

                if (string.IsNullOrWhiteSpace(model.effectivityDate))
                    return Json(new { success = false, message = "Effectivity date is required!" });

                if (model.weekdays == null || !model.weekdays.Any())
                    return Json(new { success = false, message = "Please select at least one weekday!" });

                var isRestDay = false;
                if (!string.IsNullOrWhiteSpace(model.scheduleTypeCode))
                {
                    var scheduleType = _db.QueryFirstOrDefault<dynamic>(
                        "SELECT scheduleTypeName FROM s_scheduleType WHERE scheduleTypeCode = @code AND isActive = 1",
                        new { code = model.scheduleTypeCode });

                    if (scheduleType?.scheduleTypeName != null)
                        isRestDay = scheduleType.scheduleTypeName.ToString().ToUpper().Contains("REST DAY");
                }

                if (!isRestDay)
                {
                    if (string.IsNullOrWhiteSpace(model.timeIn))
                        return Json(new { success = false, message = "Time In is required for working schedules!" });
                    if (string.IsNullOrWhiteSpace(model.timeOut))
                        return Json(new { success = false, message = "Time Out is required for working schedules!" });
                }

                // ── AUTO-COMPUTE effectivityDateTo ──────────────────────────────
                // For overnight/graveyard shifts (timeOut < timeIn), the schedule
                // spans into the next calendar day. Store that next date as effectivityDateTo.
                // For regular (same-day) shifts, effectivityDateTo = effectivityDate.
                string effectivityDateTo = ComputeEffectivityDateTo(
                    model.effectivityDate,
                    model.timeIn,
                    model.timeOut,
                    isRestDay);

                var sql = @"
                    INSERT INTO e_schedule
                    (employeeNo, weekdayName, effectivityDate, effectivityDateTo, scheduleTypeCode,
                     timeIn, timeOut, totalRenderHour, totalBreaktimeMinute,
                     isRestDay, dtAdded, addedByUser, isActive)
                    VALUES
                    (@employeeNo, @weekdayName, @effectivityDate, @effectivityDateTo, @scheduleTypeCode,
                     @timeIn, @timeOut, @totalRenderHour, @totalBreaktimeMinute,
                     @isRestDay, NOW(), @addedByUser, 1)";

                int insertedCount = 0;
                var skippedDays = new List<string>();

                foreach (var day in model.weekdays)
                {
                    var exists = _db.ExecuteScalar<int>(@"
                        SELECT COUNT(*) FROM e_schedule
                        WHERE employeeNo = @employeeNo AND weekdayName = @weekdayName
                        AND effectivityDate = @effectivityDate AND isActive = 1",
                        new { model.employeeNo, weekdayName = day, model.effectivityDate });

                    if (exists > 0) { skippedDays.Add(day); continue; }

                    _db.Execute(sql, new
                    {
                        model.employeeNo,
                        weekdayName = day,
                        model.effectivityDate,
                        effectivityDateTo,
                        model.scheduleTypeCode,
                        timeIn = isRestDay ? null : model.timeIn,
                        timeOut = isRestDay ? null : model.timeOut,
                        totalRenderHour = isRestDay ? 0 : model.totalRenderHour,
                        totalBreaktimeMinute = isRestDay ? 0 : model.totalBreaktimeMinute,
                        isRestDay = isRestDay ? 1 : (int?)null,
                        addedByUser = EmployeeNo
                    });
                    insertedCount++;
                }

                if (insertedCount == 0)
                    return Json(new { success = false, message = "Schedule(s) already exist for selected day(s)!" });

                _auditTrail.Log("e_schedule", 0, "CREATED",
                    $"Added {insertedCount} schedule(s) for {model.employeeNo}: {string.Join(", ", model.weekdays.Except(skippedDays))} - Effectivity: {model.effectivityDate}");

                var message = skippedDays.Any()
                    ? $"{insertedCount} schedule(s) added successfully! Skipped existing: {string.Join(", ", skippedDays)}"
                    : "Schedule(s) added successfully!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddSchedule: {ex.Message}");
                return Json(new { success = false, message = $"Error adding schedule: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateSchedule(userSchedule model)
        {
            try
            {
                var existingSchedule = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo FROM e_schedule WHERE id = @id AND isActive = 1", new { model.Id });

                if (existingSchedule == null)
                    return Json(new { success = false, message = "Schedule not found!" });

                if (!CanViewEmployee(existingSchedule.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to update this employee's schedule." });

                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                if (!string.IsNullOrWhiteSpace(model.scheduleTypeCode) &&
                    !RecordExists("s_scheduleType", "scheduleTypeCode", model.scheduleTypeCode))
                    return Json(new { success = false, message = "Schedule type not found!" });

                var isRestDay = false;
                if (!string.IsNullOrWhiteSpace(model.scheduleTypeCode))
                {
                    var scheduleType = _db.QueryFirstOrDefault<dynamic>(
                        "SELECT scheduleTypeName FROM s_scheduleType WHERE scheduleTypeCode = @code AND isActive = 1",
                        new { code = model.scheduleTypeCode });

                    if (scheduleType?.scheduleTypeName != null)
                        isRestDay = scheduleType.scheduleTypeName.ToString().ToUpper().Contains("REST DAY");
                }

                if (!isRestDay)
                {
                    if (string.IsNullOrWhiteSpace(model.timeIn))
                        return Json(new { success = false, message = "Time In is required for working schedules!" });
                    if (string.IsNullOrWhiteSpace(model.timeOut))
                        return Json(new { success = false, message = "Time Out is required for working schedules!" });
                }

                var duplicate = _db.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM e_schedule
                    WHERE employeeNo = @employeeNo AND weekdayName = @weekdayName
                    AND effectivityDate = @effectivityDate AND id != @Id AND isActive = 1",
                    new { model.employeeNo, model.weekdayName, model.effectivityDate, model.Id });

                if (duplicate > 0)
                    return Json(new { success = false, message = "A schedule already exists for this employee on this day and effectivity date!" });

                // ── AUTO-COMPUTE effectivityDateTo ──────────────────────────────
                string effectivityDateTo = ComputeEffectivityDateTo(
                    model.effectivityDate,
                    model.timeIn,
                    model.timeOut,
                    isRestDay);

                _db.Execute(@"
                    UPDATE e_schedule
                    SET weekdayName = @weekdayName,
                        effectivityDate = @effectivityDate,
                        effectivityDateTo = @effectivityDateTo,
                        scheduleTypeCode = @scheduleTypeCode,
                        timeIn = @timeIn,
                        timeOut = @timeOut,
                        totalRenderHour = @totalRenderHour,
                        totalBreaktimeMinute = @totalBreaktimeMinute,
                        isRestDay = @isRestDay,
                        dtLastModified = NOW(),
                        lastModifiedByUser = @lastModifiedByUser
                    WHERE id = @Id",
                    new
                    {
                        model.Id,
                        model.weekdayName,
                        model.effectivityDate,
                        effectivityDateTo,
                        model.scheduleTypeCode,
                        timeIn = isRestDay ? null : model.timeIn,
                        timeOut = isRestDay ? null : model.timeOut,
                        totalRenderHour = isRestDay ? 0 : model.totalRenderHour,
                        totalBreaktimeMinute = isRestDay ? 0 : model.totalBreaktimeMinute,
                        isRestDay = isRestDay ? 1 : (int?)null,
                        lastModifiedByUser = EmployeeNo
                    });

                _auditTrail.Log("e_schedule", model.Id, "UPDATED",
                    $"Updated schedule for {model.employeeNo}: {model.weekdayName} - Effectivity: {model.effectivityDate}");

                return Json(new { success = true, message = "Schedule updated successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateSchedule: {ex.Message}");
                return Json(new { success = false, message = $"Error updating schedule: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteScheduleGroup(string employeeNo, string effectivityDate, string reason, string deletedByUser)
        {
            try
            {
                if (!CanViewEmployee(employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to delete this employee's schedules." });

                var count = _db.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM e_schedule
                    WHERE employeeNo = @employeeNo AND DATE(effectivityDate) = DATE(@effectivityDate) AND isActive = 1",
                    new { employeeNo, effectivityDate });

                if (count == 0)
                    return Json(new { success = false, message = "No schedules found to delete!" });

                if (string.IsNullOrWhiteSpace(reason))
                    return Json(new { success = false, message = "Reason for deletion is required!" });

                _db.Execute(@"
                    UPDATE e_schedule
                    SET isActive = 0, dtLastModified = NOW(), lastModifiedByUser = @deletedByUser
                    WHERE employeeNo = @employeeNo AND DATE(effectivityDate) = DATE(@effectivityDate) AND isActive = 1",
                    new { employeeNo, effectivityDate, deletedByUser = EmployeeNo });

                _auditTrail.Log("e_schedule", 0, "DELETED",
                    $"Deleted {count} schedule(s) for {employeeNo} - Effectivity: {effectivityDate}. Reason: {reason}");

                return Json(new { success = true, message = $"{count} schedule(s) deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteScheduleGroup: {ex.Message}");
                return Json(new { success = false, message = $"Error deleting schedules: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult InactiveSchedule(int id, string reason, string deletedByUser)
        {
            try
            {
                var existingSchedule = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo FROM e_schedule WHERE id = @id AND isActive = 1", new { id });

                if (existingSchedule == null)
                    return Json(new { success = false, message = "Schedule not found or already inactive!" });

                if (!CanViewEmployee(existingSchedule.employeeNo))
                    return Json(new { success = false, message = "Access denied. You don't have permission to delete this employee's schedule." });

                if (string.IsNullOrWhiteSpace(reason))
                    return Json(new { success = false, message = "Reason for deletion is required!" });

                _db.Execute(@"
                    UPDATE e_schedule
                    SET isActive = 0, dtLastModified = NOW(), lastModifiedByUser = @deletedByUser
                    WHERE id = @Id",
                    new { Id = id, deletedByUser = EmployeeNo });

                _auditTrail.Log("e_schedule", id, "DELETED",
                    $"Marked schedule as inactive by {EmployeeNo}. Reason: {reason}");

                return Json(new { success = true, message = "Schedule deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InactiveSchedule: {ex.Message}");
                return Json(new { success = false, message = $"Error deleting schedule: {ex.Message}" });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper: compute effectivityDateTo based on timeIn vs timeOut
        // If timeOut < timeIn (overnight/graveyard), effectivityDateTo = effectivityDate + 1 day
        // Otherwise effectivityDateTo = effectivityDate (same day)
        // ─────────────────────────────────────────────────────────────────────
        private string ComputeEffectivityDateTo(string effectivityDate, string timeIn, string timeOut, bool isRestDay)
        {
            // Rest days have no time, so effectivityDateTo = effectivityDate
            if (isRestDay || string.IsNullOrWhiteSpace(timeIn) || string.IsNullOrWhiteSpace(timeOut))
                return effectivityDate;

            // Parse timeIn and timeOut as TimeSpan (HH:mm format from <input type="time">)
            if (TimeSpan.TryParse(timeIn, out var tsIn) && TimeSpan.TryParse(timeOut, out var tsOut))
            {
                // Overnight shift detected: timeOut is earlier than timeIn
                if (tsOut < tsIn)
                {
                    if (DateTime.TryParse(effectivityDate, out var effDate))
                        return effDate.AddDays(1).ToString("yyyy-MM-dd");
                }
            }

            return effectivityDate;
        }

        private bool RecordExists(string table, string column, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var sql = $"SELECT COUNT(*) FROM {table} WHERE {column} = @value AND isActive = 1";
            return _db.QuerySingle<int>(sql, new { value }) > 0;
        }
    }
}