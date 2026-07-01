using Dapper;
using Google.Protobuf.WellKnownTypes;
using KEISAN_HRIS_v2.Models.Timekeeping;
using Microsoft.AspNetCore.Components.RenderTree;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KEISAN_HRIS_v2.Services.TimeKeeping
{
    public class ReviewDTRService
    {
        private readonly IDbConnection _db;

        public ReviewDTRService(IDbConnection db)
        {
            _db = db;
        }

        // ================= RAW QUERY (COMPLETE WITH ALL TIMEKEEPING REQUESTS =================
        private async Task<List<ReviewDTRModel>> QuerySqlAsync(
            DateTime dateFrom, DateTime dateTo, string branchCode, string employeeNo)
        {
            var sql = @"
                -- Temp biometrics (first in / last out)
                DROP TEMPORARY TABLE IF EXISTS tmp_Ubiometrics;

                CREATE TEMPORARY TABLE tmp_Ubiometrics
                AS
                SELECT
                    MIN(u.id)                AS id,
                    u.employeeNo,
                    u.biometricsDate,
                    MAX(u.biometricsDateOut) AS biometricsDateOut,
                    MIN(u.biometricsTimeIn)  AS biometricsTimeIn,
                    MAX(u.biometricsTimeOut) AS biometricsTimeOut,
                    -- Carry the device-log flag of the first (MIN id) row
                    -- so the main SELECT can detect 'modified' rows.
                    (
                        SELECT u2.biometricsDeviceLog
                        FROM u_biometrics u2
                        WHERE u2.employeeNo     = u.employeeNo
                          AND u2.biometricsDate = u.biometricsDate
                        ORDER BY u2.id ASC
                        LIMIT 1
                    ) AS biometricsDeviceLog
                FROM e_basicinfo eb
                JOIN u_biometrics u 
                  ON eb.employeeNo = u.employeeNo
                WHERE (@BranchCode = '' OR eb.branchCode = @BranchCode)
                  AND u.biometricsDate BETWEEN DATE(@DateFrom) AND DATE(@DateTo)
                GROUP BY
                    u.employeeNo,
                    u.biometricsDate;

                ALTER TABLE tmp_Ubiometrics
                ADD INDEX idx_ubio (employeeNo, biometricsDate);

                -- Temp dates
                DROP TEMPORARY TABLE IF EXISTS tmp_dates;

                CREATE TEMPORARY TABLE tmp_dates
                AS
                SELECT 
                    DATE_ADD(DATE(@DateFrom), INTERVAL n DAY) AS date_val,
                    DAYNAME(DATE_ADD(DATE(@DateFrom), INTERVAL n DAY)) AS weekDayName
                FROM (
                    SELECT a.N + b.N*10 + c.N*100 AS n
                    FROM 
                      (SELECT 0 N UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
                       UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) a,
                      (SELECT 0 N UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
                       UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) b,
                      (SELECT 0 N UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
                       UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) c
                ) numbers
                WHERE DATE_ADD(DATE(@DateFrom), INTERVAL n DAY) <= DATE(@DateTo);

                ALTER TABLE tmp_dates
                ADD INDEX idx_date (date_val);

                -- MAIN QUERY (WITH ALL TIMEKEEPING REQUESTS INCLUDING OFFSET)
                SELECT 
                    e.employeeNo,
                    d.date_val AS workDate,
                    d.weekDayName,
                    e.id,
                    e.firstName,
                    e.middleName,
                    e.lastName,
                    e.honorific,
                    e.suffix,
                    e.dateHired,
                    e.branchCode,
                    sb.branchName,
                    e.positionCode,
                    sp.positionName,
                    e.departmentCode,
                    sd.departmentName,
                    e.employmentStatus,
                    e.rankCode,

                    -- Rank information
                    sr.rankName,

                    -- Overtime
                    ro.id AS overtimeID,
                    ro.overTimeDateIN,
                    ro.overTimeDateOUT,
                    ro.overTimeIN,
                    ro.overTimeOUT,
                    ro.overTimeReason,

                    -- Leave
                    rl.id AS leaveID,
                    rl.leaveCode,
                    rl.leaveDateFrom,
                    rl.leaveDateTo,
                    rl.timeIN AS leaveTimeIn,
                    rl.timeOUT AS leaveTimeOut,
                    rl.leaveCountDays,
                    rl.leaveCountHours,
                    rl.leaveReason,
                    sl.leaveName,
                    rl.leaveType,

                    -- OB
                    rob.id AS OBID,
                    rob.obDateIn,
                    rob.obDateOut,
                    rob.obTimeIn,
                    rob.obTimeOut,
                    rob.obReason,

                    -- WFH
                    rw.id AS WFHID,
                    rw.wfhDateIn,
                    rw.wfhDateOut,
                    rw.wfhTimeIn,
                    rw.wfhTimeOut,
                    rw.wfhReason,

                    -- Change Schedule
                    rcs.id AS changeScheduleID,
                    rcs.timeIN AS changeScheduleTimeIn,
                    rcs.timeOUT AS changeScheduleTimeOut,
                    rcs.Reason AS changeScheduleReason,
                    rcs.scheduleTypeCode AS changeScheduleTypeCode,

                    -- Undertime
                    rut.id AS undertimeID,
                    rut.undertimeDateIN AS undertimeDateIN,
                    rut.undertimeTimeOUT AS undertimeTimeOUT,
                    rut.underTimeDateOUT AS underTimeDateOUT,
                    rut.undertimeReason,

                    -- ============================================
                    -- SCHEDULE TIME IN PRIORITY:
                    -- 1. Change Schedule
                    -- 2. WFH
                    -- 3. OB
                    -- 4. Half-Day Leave (first/second)
                    -- 5. Regular Schedule
                    -- ============================================
                    CASE
                        -- Change Schedule
                        WHEN rcs.id IS NOT NULL THEN TIMESTAMP(d.date_val, rcs.timeIN)
    
                        -- WFH
                        WHEN rw.id IS NOT NULL THEN TIMESTAMP(d.date_val, rw.wfhTimeIn)
    
                        -- OB
                        WHEN rob.id IS NOT NULL THEN TIMESTAMP(d.date_val, rob.obTimeIn)

                        -- ── HALF-DAY LEAVE: FIRST half taken → employee works SECOND half ──
                        -- totalRenderHour includes break (e.g. 9h = 8h net work + 1h break).
                        -- Net work hours = totalRenderHour - (totalBreaktimeMinute / 60)
                        -- Half of net work = net / 2  →  break START time
                        -- Second half start = timeIn + (net/2) + totalBreaktimeMinute
                        -- Example: 08:00 + 4h net/2 = 12:00 PM (break start)
                        --          12:00 PM + 60 min = 1:00 PM (second half start)  ✅
                        WHEN rl.id IS NOT NULL
                             AND rl.leaveType = 'first'
                             AND rl.leaveCountDays = 0.5
                             AND s.timeIn IS NOT NULL
                             AND s.totalRenderHour IS NOT NULL
                        THEN
                            CASE
                                WHEN ADDTIME(
                                         ADDTIME(s.timeIn,
                                             SEC_TO_TIME(
                                                 ((s.totalRenderHour - COALESCE(s.totalBreaktimeMinute, 0) / 60.0) * 3600) / 2
                                             )
                                         ),
                                         SEC_TO_TIME(COALESCE(s.totalBreaktimeMinute, 0) * 60)
                                     ) < s.timeIn
                                THEN TIMESTAMP(DATE_ADD(d.date_val, INTERVAL 1 DAY),
                                         ADDTIME(
                                             ADDTIME(s.timeIn,
                                                 SEC_TO_TIME(
                                                     ((s.totalRenderHour - COALESCE(s.totalBreaktimeMinute, 0) / 60.0) * 3600) / 2
                                                 )
                                             ),
                                             SEC_TO_TIME(COALESCE(s.totalBreaktimeMinute, 0) * 60)
                                         )
                                     )
                                ELSE TIMESTAMP(d.date_val,
                                         ADDTIME(
                                             ADDTIME(s.timeIn,
                                                 SEC_TO_TIME(
                                                     ((s.totalRenderHour - COALESCE(s.totalBreaktimeMinute, 0) / 60.0) * 3600) / 2
                                                 )
                                             ),
                                             SEC_TO_TIME(COALESCE(s.totalBreaktimeMinute, 0) * 60)
                                         )
                                     )
                            END

                        -- ── HALF-DAY LEAVE: SECOND half taken → employee works FIRST half ──
                        -- Schedule stays at original timeIn
                        WHEN rl.id IS NOT NULL
                             AND rl.leaveType = 'second'
                             AND rl.leaveCountDays = 0.5
                             AND s.timeIn IS NOT NULL
                        THEN TIMESTAMP(d.date_val, s.timeIn)

                        -- Rest Day or No Schedule
                        WHEN s.isRestDay = 1 THEN NULL
                        WHEN s.timeIn IS NULL THEN NULL
    
                        -- Regular Schedule
                        ELSE TIMESTAMP(d.date_val, s.timeIn)
                    END AS scheduleTimeIn,

                    -- ============================================
                    -- SCHEDULE TIME OUT PRIORITY:
                    -- 1. Approved Undertime
                    -- 2. Change Schedule      (overnight-aware)
                    -- 3. WFH                  (overnight-aware)
                    -- 4. OB                   (overnight-aware)
                    -- 5. Half-Day Leave (first/second)
                    -- 6. Regular Schedule     (overnight-aware)
                    -- ============================================
                    CASE
                        -- ── CHANGE SCHEDULE (overnight-aware) ──
                        -- If changeScheduleTimeOUT < changeScheduleTimeIN → next day
                        WHEN rcs.id IS NOT NULL THEN
                            CASE
                                WHEN rcs.timeOUT < rcs.timeIN
                                THEN TIMESTAMP(DATE_ADD(d.date_val, INTERVAL 1 DAY), rcs.timeOUT)
                                ELSE TIMESTAMP(d.date_val, rcs.timeOUT)
                            END

                        -- ── WFH (same day, overnight-aware) ──
                        WHEN rw.id IS NOT NULL AND rw.wfhDateOut = d.date_val THEN
                            CASE
                                WHEN rw.wfhTimeOut < rw.wfhTimeIn
                                THEN TIMESTAMP(DATE_ADD(d.date_val, INTERVAL 1 DAY), rw.wfhTimeOut)
                                ELSE TIMESTAMP(d.date_val, rw.wfhTimeOut)
                            END

                        -- ── WFH (overnight shift — wfhDateOut already next day) ──
                        WHEN rw.id IS NOT NULL AND rw.wfhDateOut > d.date_val THEN
                            TIMESTAMP(DATE_ADD(d.date_val, INTERVAL 1 DAY), rw.wfhTimeOut)

                        -- ── OB (same day, overnight-aware) ──
                        WHEN rob.id IS NOT NULL AND rob.obDateOut = d.date_val THEN
                            CASE
                                WHEN rob.obTimeOut < rob.obTimeIn
                                THEN TIMESTAMP(DATE_ADD(d.date_val, INTERVAL 1 DAY), rob.obTimeOut)
                                ELSE TIMESTAMP(d.date_val, rob.obTimeOut)
                            END

                        -- ── OB (overnight shift — obDateOut already next day) ──
                        WHEN rob.id IS NOT NULL AND rob.obDateOut > d.date_val THEN
                            TIMESTAMP(DATE_ADD(d.date_val, INTERVAL 1 DAY), rob.obTimeOut)

                        -- ── HALF-DAY LEAVE: FIRST half taken → employee works SECOND half ──
                        -- Schedule ends at original timeOut (overnight-aware)
                        WHEN rl.id IS NOT NULL
                             AND rl.leaveType = 'first'
                             AND rl.leaveCountDays = 0.5
                             AND s.timeOut IS NOT NULL
                        THEN
                            CASE
                                WHEN s.timeOut < s.timeIn
                                THEN TIMESTAMP(DATE_ADD(d.date_val, INTERVAL 1 DAY), s.timeOut)
                                ELSE TIMESTAMP(d.date_val, s.timeOut)
                            END

                        -- ── HALF-DAY LEAVE: SECOND half taken → employee works FIRST half ──
                        WHEN rl.id IS NOT NULL
                             AND rl.leaveType = 'second'
                             AND rl.leaveCountDays = 0.5
                             AND s.timeIn IS NOT NULL
                             AND s.totalRenderHour IS NOT NULL
                        THEN
                            TIMESTAMP(d.date_val,
                                ADDTIME(s.timeIn,
                                    SEC_TO_TIME(
                                        ((s.totalRenderHour - COALESCE(s.totalBreaktimeMinute, 0) / 60.0) * 3600) / 2
                                    )
                                )
                            )

                        -- Rest Day or No Schedule
                        WHEN s.isRestDay = 1 THEN NULL
                        WHEN s.timeOut IS NULL THEN NULL

                        -- ── REGULAR SCHEDULE (overnight-aware) ──
                        -- If timeOut < timeIn → graveyard/overnight shift → next day
                        ELSE
                            CASE
                                WHEN s.timeOut < s.timeIn
                                THEN TIMESTAMP(DATE_ADD(d.date_val, INTERVAL 1 DAY), s.timeOut)
                                ELSE TIMESTAMP(d.date_val, s.timeOut)
                            END
                    END AS scheduleTimeOut,

                    s.isRestDay,
                    s.totalRenderHour,
                    
                    -- Use change schedule type if available, otherwise use regular schedule type
                    COALESCE(rcs.scheduleTypeCode, s.scheduleTypeCode) AS scheduleTypeCode,
                    COALESCE(sst_change.scheduleTypeName, sst.scheduleTypeName) AS scheduleTypeName,

                    -- Biometrics

                    -- SMART biometricsDateIn
                    -- biometricsDateIn:
                    --   IF modified → use t_biometrics edited time
                    --   ELSE        → use original u_biometrics device time

                    CASE
                        WHEN u.biometricsDeviceLog = 'modified' AND tb.biometricsTimeIn IS NOT NULL THEN
                            TIMESTAMP(COALESCE(tb.biometricsDate, u.biometricsDate), tb.biometricsTimeIn)
                        WHEN u.biometricsTimeIn IS NULL THEN NULL
                        WHEN u.biometricsDate IS NOT NULL THEN
                            TIMESTAMP(u.biometricsDate, u.biometricsTimeIn)
                        ELSE
                            TIMESTAMP(d.date_val, u.biometricsTimeIn)
                    END AS biometricsDateIn,

                    -- SMART biometricsDateOut (handles overnight/graveyard)
                    --   IF modified → use t_biometrics edited time (with t_biometrics DateOut)
                    --   ELSE        → use original u_biometrics overnight/graveyard logic

                    CASE
                        -- Approved undertime overrides actual biometrics time out
                        WHEN rut.id IS NOT NULL
                             AND rut.underTimeDateOUT IS NOT NULL
                             AND rut.undertimeTimeOUT IS NOT NULL
                        THEN TIMESTAMP(rut.underTimeDateOUT, rut.undertimeTimeOUT)

                        WHEN u.biometricsDeviceLog = 'modified' AND tb.biometricsTimeOut IS NOT NULL THEN
                            TIMESTAMP(COALESCE(tb.DateOut, u.biometricsDate), tb.biometricsTimeOut)
                        WHEN u.biometricsTimeOut IS NULL THEN NULL
                        WHEN u.biometricsDateOut IS NOT NULL THEN
                            TIMESTAMP(u.biometricsDateOut, u.biometricsTimeOut)
                        WHEN u.biometricsDate IS NOT NULL THEN
                        CASE
                            WHEN u.biometricsTimeOut < COALESCE(tb.biometricsTimeIn, u.biometricsTimeIn) THEN
                                TIMESTAMP(DATE_ADD(u.biometricsDate, INTERVAL 1 DAY), u.biometricsTimeOut)
                            ELSE
                                TIMESTAMP(u.biometricsDate, u.biometricsTimeOut)
                        END
                    ELSE
                        CASE
                            WHEN u.biometricsTimeOut < COALESCE(tb.biometricsTimeIn, u.biometricsTimeIn) THEN
                                TIMESTAMP(DATE_ADD(d.date_val, INTERVAL 1 DAY), u.biometricsTimeOut)
                            ELSE
                                TIMESTAMP(d.date_val, u.biometricsTimeOut)
                        END
                    END AS biometricsDateOut,

                    -- t_biometrics
                    tb.u_biometricsID AS tid,
                    TIMESTAMP(tb.biometricsDate, u.biometricsTimeIn) AS tbiometricsDate,
                    tb.statusName AS tstatusName,
                    tb.remarks AS tremarks,
                    tb.biometricsTimeIn  AS tTimeIn,
                    tb.biometricsTimeOut AS tTimeOut,
                    tb.DateOut           AS tDateOut,

                    -- Payroll
                    ep.payrollType,
                    ep.payrollBasis,

                    -- Holiday
                    sh.holidayType,
                    sh.holidayName,

                    -- Manual edit flags

                    -- isTimeInManuallyEdited: true when t_biometrics has an admin-edited Time In
                    CASE
                        WHEN tb.biometricsTimeIn IS NOT NULL THEN 1
                        ELSE 0
                    END AS isTimeInManuallyEdited,

                    -- isTimeOutManuallyEdited: true when t_biometrics has an admin-edited Time Out
                    CASE
                        WHEN tb.biometricsTimeOut IS NOT NULL THEN 1
                        ELSE 0
                    END AS isTimeOutManuallyEdited

                FROM e_basicinfo e
                JOIN tmp_dates d

                LEFT JOIN e_payrolldetails ep 
                  ON ep.employeeNo = e.employeeNo
                LEFT JOIN s_branch sb
                  ON sb.branchCode = e.branchCode
                LEFT JOIN s_department sd
                  ON sd.departmentCode = e.departmentCode
                LEFT JOIN s_position sp
                  ON sp.positionCode = e.positionCode
                LEFT JOIN s_rank sr
                  ON sr.rankCode = e.rankCode
                LEFT JOIN e_schedule s
                  ON s.employeeNo = e.employeeNo
                 AND s.weekDayName = d.weekDayName
                 AND s.isActive = 1
                 AND s.effectivityDate = (
                     SELECT MAX(s2.effectivityDate)
                     FROM e_schedule s2
                     WHERE s2.employeeNo = e.employeeNo
                       AND s2.weekDayName = d.weekDayName
                       AND s2.isActive = 1
                       AND s2.effectivityDate <= d.date_val
                 )
                LEFT JOIN s_scheduletype sst
                  ON sst.scheduleTypeCode = s.scheduleTypeCode
                LEFT JOIN tmp_Ubiometrics u
                  ON u.employeeNo = e.employeeNo
                 AND u.biometricsDate = d.date_val
                LEFT JOIN rq_overtime ro 
                  ON ro.employeeNo = e.employeeNo
                 AND ro.statusLevel4  IN ('Approved', 'Processed')
                 AND ro.isActive = 1
                 AND ro.overTimeDateIN = d.date_val
                LEFT JOIN rq_leave rl 
                  ON rl.employeeNo = e.employeeNo
                 AND rl.statusLevel4  IN ('Approved', 'Processed')
                 AND rl.isActive = 1
                 AND d.date_val BETWEEN rl.leaveDateFrom AND rl.leaveDateTo
                LEFT JOIN rq_officialbusiness rob 
                  ON rob.employeeNo = e.employeeNo
                 AND rob.statusLevel4 IN ('Approved', 'Processed')
                 AND rob.isActive = 1
                 AND d.date_val BETWEEN rob.obDateIn AND rob.obDateOut
                LEFT JOIN rq_workfromhome rw 
                  ON rw.employeeNo = e.employeeNo
                 AND rw.statusLevel4  IN ('Approved', 'Processed')
                 AND rw.isActive = 1
                 AND d.date_val BETWEEN rw.wfhDateIn AND rw.wfhDateOut
                LEFT JOIN rq_changeschedule rcs
                  ON rcs.employeeNo = e.employeeNo
                 AND rcs.weekdayName = d.weekDayName
                 AND rcs.statusLevel4 IN ('Approved', 'Processed')
                 AND rcs.isActive = 1
                 AND d.date_val = rcs.effectivityDate
                LEFT JOIN rq_undertime rut
                  ON rut.employeeNo = e.employeeNo
                 AND rut.statusLevel4 IN ('Approved', 'Processed')
                 AND rut.isActive = 1
                 AND rut.undertimeDateIN = d.date_val
                LEFT JOIN s_scheduletype sst_change
                  ON sst_change.scheduleTypeCode = rcs.scheduleTypeCode
                LEFT JOIN t_biometrics tb
                  ON tb.id = (
                      SELECT tb2.id
                      FROM t_biometrics tb2
                      WHERE tb2.employeeNo     = e.employeeNo
                        AND tb2.u_biometricsID = u.id
                        AND tb2.isActive       = 1
                        AND tb2.tagStatus      = 'modified'
                      ORDER BY tb2.id DESC
                      LIMIT 1
                  )
                LEFT JOIN s_leave sl 
                  ON sl.leaveCode = rl.leaveCode
                LEFT JOIN s_holiday sh 
                  ON d.date_val = sh.holidayDate

                WHERE (@EmployeeNo = '' OR e.employeeNo = @EmployeeNo)
                  AND (@BranchCode = '' OR e.branchCode = @BranchCode)
                  AND e.isActive = '1'
                ORDER BY e.employeeNo, d.date_val
            ";

            return (await _db.QueryAsync<ReviewDTRModel>(sql, new
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                BranchCode = branchCode,
                EmployeeNo = employeeNo
            })).ToList();
        }

        // Check if DTR is posted
        public async Task<bool> IsDtrPostedAsync(
            int cutOffType,
            string dateMonth,
            int dateYear,
            string branchCode = ""
)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM p_biometricsline
                WHERE statusName = 'posted'
                  AND cutOffType = @cutOffType
                  AND dateMonth  = @dateMonth
                  AND dateYear   = @dateYear
                  AND (@branchCode = '' OR branchCode = @branchCode)";

            var count = await _db.ExecuteScalarAsync<int>(sql, new
            {
                cutOffType,
                dateMonth,
                dateYear,
                branchCode
            });

            return count > 0;
        }

        // ================= SUMMARY =================
        public async Task<List<ReviewDTREmployeeSummaryViewModel>> GetSummaryAsync(
            DateTime dateFrom,
            DateTime dateTo,
            string branchCode,
            string employeeNo)
        {
            // Fetch one extra day before the cutoff so the absent-before-holiday
            // rule can see the prior day even when a holiday lands on day 1
            // of the cutoff. The extra day is trimmed out before returning.
            var lookbackDateFrom = dateFrom.AddDays(-1);
            var raw = await QuerySqlAsync(lookbackDateFrom, dateTo, branchCode, employeeNo);
            var computed = ComputeAll(raw)
                .Where(x => x.Raw.workDate.HasValue && x.Raw.workDate.Value.Date >= dateFrom.Date)
                .ToList();

            return computed
                .GroupBy(x => x.Raw.employeeNo)
                .Select(g => new ReviewDTREmployeeSummaryViewModel
                {
                    EmployeeNo = g.Key,
                    FullName = $"{g.First().Raw.lastName}, {g.First().Raw.firstName}",
                    PayrollType = g.First().Raw.payrollType,

                    // NORMAL
                    NDHours = g.Sum(x => x.NDHours),
                    OTHours = g.Sum(x => x.OTHours),
                    OTNDHours = g.Sum(x => x.OTNDHours),

                    // REST DAY
                    RDHours = g.Sum(x => x.RDHours),
                    RDOTHours = g.Sum(x => x.RDOTHours),
                    RDNDHours = g.Sum(x => x.RDNDHours),
                    RDNDOTHours = g.Sum(x => x.RDNDOTHours),

                    // Special Holiday Rest Day
                    SPLHolidayRESTHours = g.Sum(x => x.SPLHolidayRESTHours),
                    SPLHolidayRESTOTHours = g.Sum(x => x.SPLHolidayRESTOTHours),
                    SPLHolidayRESTNDHours = g.Sum(x => x.SPLHolidayRESTNDHours),
                    SPLHolidayRESTNDOTHours = g.Sum(x => x.SPLHolidayRESTNDOTHours),

                    // Legal Holiday Rest Day
                    REGHolidayRESTNDHours = g.Sum(x => x.REGHolidayRESTNDHours),
                    REGHolidayRESTNDOTHours = g.Sum(x => x.REGHolidayRESTNDOTHours),
                    REGHolidayRESTHours = g.Sum(x => x.REGHolidayRESTHours),
                    REGHolidayRESTOTHours = g.Sum(x => x.REGHolidayRESTOTHours),

                    // SPECIAL HOLIDAY
                    SPLHolidayHours = g.Sum(x => x.SPLHolidayHours),
                    SPLHolidayOTHours = g.Sum(x => x.SPLHolidayOTHours),
                    SPLHolidayNDHours = g.Sum(x => x.SPLHolidayNDHours),
                    SPLHolidayNDOTHours = g.Sum(x => x.SPLHolidayNDOTHours),

                    // LEGAL HOLIDAY
                    REGHolidayHours = g.Sum(x => x.REGHolidayHours),
                    REGHolidayOTHours = g.Sum(x => x.REGHolidayOTHours),
                    REGHolidayNDHours = g.Sum(x => x.REGHolidayNDHours),
                    REGHolidayNDOTHours = g.Sum(x => x.REGHolidayNDOTHours),

                    TotalLateMinutes = g.Sum(x => x.LateMinutes),
                    TotalUndertimeMinutes = g.Sum(x => x.UnderTimeMinutes),
                    //TotalPresentDays = g.Count(x => x.IsPresent),
                    TotalPresentDays = g.Sum(x =>
                    {
                        if (!x.IsPresent) return 0;
                        // Half-day LWOP — employee worked half, so present = 0.5
                        if (x.Raw.leaveCountDays == 0.5 && x.Raw.leaveCode == "LWOP") return 0.5;
                        // Half-day paid leave — employee worked the other half
                        if (x.Raw.leaveCountDays == 0.5 && x.Raw.leaveType != null) return 0.5;
                        return 1.0;
                    }),
                    TotalAbsentDays = g.Sum(x => x.IsAbsent
                    ? x.Raw.leaveCode == "LWOP" && x.Raw.leaveCountDays == 0.5 ? 0.5 : 1
                    : 0)
                })
                .ToList();
        }

        // ================= DETAILS =================
        public async Task<List<ReviewDTRViewModel>> GetDailyRowsAsync(
            DateTime dateFrom, DateTime dateTo, string branchCode, string employeeNo)
        {
            // Same lookback fix as GetSummaryAsync — see comment there.
            var lookbackDateFrom = dateFrom.AddDays(-1);
            var raw = await QuerySqlAsync(lookbackDateFrom, dateTo, branchCode, employeeNo);
            var computed = ComputeAll(raw)
                .Where(x => x.Raw.workDate.HasValue && x.Raw.workDate.Value.Date >= dateFrom.Date)
                .ToList();

            return computed
                .OrderBy(x => x.Raw.workDate)
                .Select(x =>
                {
                    // Build tTimeIn: combine workDate + tTimeIn timespan → full DateTime
                    DateTime? tTimeIn = null;
                    if (x.Raw.tTimeIn.HasValue && x.Raw.workDate.HasValue)
                        tTimeIn = x.Raw.workDate.Value.Date.Add(x.Raw.tTimeIn.Value);

                    // Build tTimeOut: use tDateOut if present (overnight), else workDate
                    DateTime? tTimeOut = null;
                    if (x.Raw.tTimeOut.HasValue)
                    {
                        var outDate = x.Raw.tDateOut.HasValue
                            ? x.Raw.tDateOut.Value.Date
                            : x.Raw.workDate.HasValue ? x.Raw.workDate.Value.Date : DateTime.Today;
                        tTimeOut = outDate.Add(x.Raw.tTimeOut.Value);
                    }

                    return new ReviewDTRViewModel
                    {
                        employeeNo = x.Raw.employeeNo,
                        workDate = x.Raw.workDate?.ToString("yyyy-MM-dd"),
                        weekDayName = x.Raw.weekDayName,

                        branchCode = x.Raw.branchCode,
                        departmentCode = x.Raw.departmentCode,

                        RenderHours = x.RenderHours,

                        //scheduleTimeIn = x.Raw.scheduleTimeIn,
                        //scheduleTimeOut = x.Raw.scheduleTimeOut,
                        scheduleTimeIn = x.Remarks == "SUSPENDED" ? null : x.Raw.scheduleTimeIn,
                        scheduleTimeOut = x.Remarks == "SUSPENDED" ? null : x.Raw.scheduleTimeOut,
                        biometricsDateIn = x.Raw.biometricsDateIn,
                        biometricsDateOut = x.Raw.biometricsDateOut,

                        remarks = x.Remarks,

                        LateMinutes = x.LateMinutes,
                        UnderTimeMinutes = x.UnderTimeMinutes,

                        NDHours = x.NDHours,
                        OTNDHours = x.OTNDHours,
                        OTHours = x.OTHours,
                        RDHours = x.RDHours,
                        RDOTHours = x.RDOTHours,
                        RDNDHours = x.RDNDHours,
                        RDNDOTHours = x.RDNDOTHours,

                        // Special Holiday Rest Day
                        SPLHolidayRESTHours = x.SPLHolidayRESTHours,
                        SPLHolidayRESTOTHours = x.SPLHolidayRESTOTHours,
                        SPLHolidayRESTNDHours = x.SPLHolidayRESTNDHours,
                        SPLHolidayRESTNDOTHours = x.SPLHolidayRESTNDOTHours,

                        // Legal Holiday Rest Day
                        REGHolidayRESTNDHours = x.REGHolidayRESTNDHours,
                        REGHolidayRESTNDOTHours = x.REGHolidayRESTNDOTHours,
                        REGHolidayRESTHours = x.REGHolidayRESTHours,
                        REGHolidayRESTOTHours = x.REGHolidayRESTOTHours,

                        OvertimeDateTimeIn = x.Raw.overTimeDateIN.HasValue && x.Raw.overTimeIN.HasValue
                            ? x.Raw.overTimeDateIN.Value.Add(x.Raw.overTimeIN.Value)
                            : null,

                        OverTimeDateTimeOUT = x.Raw.overTimeDateOUT.HasValue && x.Raw.overTimeOUT.HasValue
                            ? x.Raw.overTimeDateOUT.Value.Add(x.Raw.overTimeOUT.Value)
                            : null,

                        OTReason = x.Raw.overTimeReason,

                        SPLHolidayHours = x.SPLHolidayHours,
                        SPLHolidayOTHours = x.SPLHolidayOTHours,
                        SPLHolidayNDHours = x.SPLHolidayNDHours,
                        SPLHolidayNDOTHours = x.SPLHolidayNDOTHours,

                        REGHolidayHours = x.REGHolidayHours,
                        REGHolidayOTHours = x.REGHolidayOTHours,
                        REGHolidayNDHours = x.REGHolidayNDHours,
                        REGHolidayNDOTHours = x.REGHolidayNDOTHours,

                        holidayType = x.Raw.holidayType,
                        holidayName = x.Raw.holidayName,

                        IsPresent = x.IsPresent,
                        IsAbsent = !x.IsPresent && !x.Raw.isRestDay && x.Raw.holidayType == null,

                        leaveName = x.Raw.leaveName,
                        leaveCountDays = x.Raw.leaveCountDays,
                        leaveReason = x.Raw.leaveReason,

                        obReason = x.Raw.obReason,
                        wfhReason = x.Raw.wfhReason,

                        isTimeInManuallyEdited = x.Raw.isTimeInManuallyEdited,
                        isTimeOutManuallyEdited = x.Raw.isTimeOutManuallyEdited,

                        // NEW: maps t_biometrics pending/approved tracking fields
                        // Fixes the pending state being lost on modal reopen
                        tid = x.Raw.tid,
                        tstatusName = x.Raw.tstatusName,
                        tTimeIn = tTimeIn,
                        tTimeOut = tTimeOut,
                    };
                })
                .ToList();
        }

        // ================= CORE COMPUTE (WITH ALL BUSINESS RULES INCLUDING OFFSET) =================
        private List<DTRComputed> ComputeAll(List<ReviewDTRModel> rows)
        {
            var list = new List<DTRComputed>();

            foreach (var r in rows)
            {

                // Determine employee rank type
                bool isFlexiTime = IsFlexiTime(r.scheduleTypeCode, r.rankCode);

                var item = new DTRComputed
                {
                    Raw = r,
                    Remarks = GetRemarks(r),
                    IsPresent = false, // Will be set below
                    IsAbsent = false,  // Will be set below

                    // Calculate late/undertime normally - scheduleTimeIn/Out now reflects all requests
                    LateMinutes = ComputeLate(r, isFlexiTime),
                    UnderTimeMinutes = ComputeUnderTime(r, isFlexiTime),

                    // Initialize all to 0
                    RenderHours = 0,
                    OTHours = 0,
                    NDHours = 0,
                    RDHours = 0,
                    RDOTHours = 0,
                    RDNDHours = 0,
                    SPLHolidayHours = 0,
                    SPLHolidayOTHours = 0,
                    SPLHolidayNDHours = 0,
                    REGHolidayHours = 0,
                    REGHolidayOTHours = 0,
                    REGHolidayNDHours = 0,

                    OTIn = r.overTimeDateIN.HasValue && r.overTimeIN.HasValue
                        ? r.overTimeDateIN.Value.Add(r.overTimeIN.Value)
                        : null,
                    OTOut = r.overTimeDateOUT.HasValue && r.overTimeOUT.HasValue
                        ? r.overTimeDateOUT.Value.Add(r.overTimeOUT.Value)
                        : null
                };

                // ================= ROUTING BASED ON DAY TYPE (EXISTING LOGIC) =================
                // ── LWOP — no pay, no credit, treat as absent ──────────────
                if (item.Remarks == "NO PAY LEAVE" || item.Remarks == "MATERNITY LEAVE" || item.Remarks == "PATERNITY LEAVE" || item.Remarks == "SUSPENDED")
                {
                    // Half day LWOP — still compute render hours for the half they worked
                    if (r.leaveCountDays == 0.5 && item.Remarks == "NO PAY LEAVE")
                    {
                        // Fall through to normal compute below
                    }
                    else
                    {
                        item.IsPresent = false;
                        item.IsAbsent = true;
                        list.Add(item);
                        continue;
                    }
                }

                // ── ABSENT-BEFORE-HOLIDAY RULE ──────────────────────────────
                // If the employee was ABSENT / NO TIMEOUT / NO SCHEDULE / on
                // full-day LWOP / SUSPENDED / MATERNITY / PATERNITY leave on
                // the day immediately before a holiday, the holiday itself is
                // not paid — tagged ABSENT instead of processed as a holiday.
                // Exceptions: REST DAY before holiday, and HALF-DAY LWOP
                // before holiday, are both still paid normally.
                if (r.holidayType != null)
                {
                    var prev = list.Count > 0 ? list[list.Count - 1] : null;

                    bool isConsecutiveDay = prev != null
                        && prev.Raw.employeeNo == r.employeeNo
                        && prev.Raw.workDate.HasValue
                        && r.workDate.HasValue
                        && prev.Raw.workDate.Value.Date == r.workDate.Value.Date.AddDays(-1);

                    bool prevIsHalfDayLWOP = prev != null
                        && prev.Remarks == "NO PAY LEAVE"
                        && prev.Raw.leaveCountDays == 0.5;

                    bool prevBlocksHolidayPay = prev != null && !prevIsHalfDayLWOP &&
                        (prev.Remarks == "ABSENT" ||
                         prev.Remarks == "NO TIMEOUT" ||
                         prev.Remarks == "NO SCHEDULE" ||
                         prev.Remarks == "NO PAY LEAVE" ||      // full-day LWOP
                         prev.Remarks == "SUSPENDED" ||         // SUS
                         prev.Remarks == "MATERNITY LEAVE" ||   // ML
                         prev.Remarks == "PATERNITY LEAVE");    // PL

                    if (isConsecutiveDay && prevBlocksHolidayPay)
                    {
                        item.Remarks = "ABSENT";
                        item.IsPresent = false;
                        item.IsAbsent = true;
                        list.Add(item);
                        continue;
                    }
                }

                if (r.holidayType == "Special Holiday" && r.isRestDay && !r.biometricsDateIn.HasValue)
                {
                    // Rest Day on Special Holiday → separate columns
                    var result = ComputeHolidayHours(r);
                    item.SPLHolidayRESTHours = result.regularHours;
                    item.SPLHolidayRESTOTHours = result.otHours;
                    item.SPLHolidayRESTNDHours = result.ndHours;
                    item.SPLHolidayRESTNDOTHours = result.ndOTHours;

                    if (result.regularHours > 0 || result.otHours > 0)
                        item.IsPresent = true;
                }
                else if (r.holidayType == "Special Holiday")
                {
                    // Regular Special Holiday
                    var result = ComputeHolidayHours(r);
                    item.SPLHolidayHours = result.regularHours;
                    item.SPLHolidayOTHours = result.otHours;
                    item.SPLHolidayNDHours = result.ndHours;
                    item.SPLHolidayNDOTHours = result.ndOTHours;

                    if (item.LateMinutes > 0 && (result.regularHours > 0 || result.otHours > 0))
                    {
                        double lateHours = item.LateMinutes / 60.0;
                        item.SPLHolidayHours = Math.Max(0, item.SPLHolidayHours - lateHours);
                    }

                    if (result.regularHours > 0 || result.otHours > 0)
                        item.IsPresent = true;
                }
                else if (r.holidayType == "Legal Holiday" && r.isRestDay && !r.biometricsDateIn.HasValue)
                {
                    // Rest Day on Legal Holiday → separate columns
                    var result = ComputeHolidayHours(r);
                    item.REGHolidayRESTHours = result.regularHours;
                    item.REGHolidayRESTOTHours = result.otHours;
                    item.REGHolidayRESTNDHours = result.ndHours;
                    item.REGHolidayRESTNDOTHours = result.ndOTHours;

                    if (result.regularHours > 0 || result.otHours > 0)
                        item.IsPresent = true;
                }
                else if (r.holidayType == "Legal Holiday")
                {
                    // Regular Legal Holiday
                    var result = ComputeHolidayHours(r);
                    item.REGHolidayHours = result.regularHours;
                    item.REGHolidayOTHours = result.otHours;
                    item.REGHolidayNDHours = result.ndHours;
                    item.REGHolidayNDOTHours = result.ndOTHours;

                    if (item.LateMinutes > 0 && (result.regularHours > 0 || result.otHours > 0))
                    {
                        double lateHours = item.LateMinutes / 60.0;
                        item.REGHolidayHours = Math.Max(0, item.REGHolidayHours - lateHours);
                    }

                    if (result.regularHours > 0 || result.otHours > 0)
                        item.IsPresent = true;
                }
                else if ((!string.IsNullOrEmpty(r.obReason) || !string.IsNullOrEmpty(r.wfhReason) ||
                    r.changeScheduleID.HasValue) &&
                    (!r.isRestDay || r.changeScheduleID.HasValue) &&
                    r.holidayType == null)
                {
                    // OB/WFH/Change Schedule → ComputeNormalDayHours
                    // Only fires when NOT a rest day and NOT a holiday
                    var result = ComputeNormalDayHours(r);

                    double maxRenderHours = GetMaxRenderHours(r);
                    item.RenderHours = Math.Min(result.regularHours + result.ndHours, maxRenderHours);

                    // Deduct late from render hours
                    if (item.LateMinutes > 0)
                    {
                        double lateHours = item.LateMinutes / 60.0;
                        item.RenderHours = Math.Max(0, item.RenderHours - lateHours);
                    }

                    item.OTHours = result.otHours;
                    item.NDHours = result.ndHours;
                    item.OTNDHours = result.otndHours;
                }
                else if (r.isRestDay || item.Remarks == "NO SCHEDULE")
                {
                    // ComputeApprovedRDOT
                    var result = ComputeApprovedRDOT(r);
                    item.RDHours = result.regularHours;
                    item.RDOTHours = result.otHours;
                    item.RDNDHours = result.ndHours;
                    item.RDNDOTHours = result.ndOTHours;

                    if (item.LateMinutes > 0 && (result.regularHours > 0 || result.otHours > 0))
                    {
                        double lateHours = item.LateMinutes / 60.0;
                        item.RDHours = Math.Max(0, item.RDHours - lateHours);
                    }

                    if (result.regularHours > 0 || result.otHours > 0)
                    {
                        item.IsPresent = true;
                    }
                }
                else
                {
                    // NORMAL DAY
                    var result = ComputeNormalDayHours(r);

                    double maxRenderHours = GetMaxRenderHours(r);
                    item.RenderHours = Math.Min(result.regularHours + result.ndHours, maxRenderHours);

                    // Deduct late from render hours
                    if (item.LateMinutes > 0)
                    {
                        double lateHours = item.LateMinutes / 60.0;
                        item.RenderHours = Math.Max(0, item.RenderHours - lateHours);
                    }

                    item.OTHours = result.otHours;
                    item.NDHours = result.ndHours;
                    item.OTNDHours = result.otndHours;
                }

                // Update IsPresent status
                item.IsPresent = item.Remarks != "NO TIMEOUT" &&
                     item.Remarks != "NO SCHEDULE" &&
                     item.Remarks != "ABSENT" &&
                     item.Remarks != "REST DAY" &&
                     item.Remarks != "LEGAL HOLIDAY" &&
                     item.Remarks != "SPECIAL HOLIDAY" &&
                     item.Remarks != "NO PAY LEAVE";

                item.IsAbsent = item.Remarks == "NO TIMEOUT" ||
                item.Remarks == "NO SCHEDULE" ||
                item.Remarks == "ABSENT" ||
                item.Remarks == "NO PAY LEAVE";

                // Half day LWOP — mark as present for the half worked
                if (item.Remarks == "NO PAY LEAVE" && r.leaveCountDays == 0.5)
                {
                    item.IsPresent = true;
                    item.IsAbsent = true; // still absent for 0.5, handled in summary Sum
                }

                list.Add(item);
            }

            return list;
        }

        // ================= HELPER METHODS =================

        private bool IsFlexiTime(string scheduleTypeCode, string rankCode)
        {
            // Priority 1: scheduleTypeCode
            if (!string.IsNullOrEmpty(scheduleTypeCode))
            {
                var code = scheduleTypeCode.ToUpper();
                if (code == "FLEXI1" || code == "FLEXI2")
                    return true;
            }

            return false;
        }

        private double GetMaxRenderHours(ReviewDTRModel r)
        {
            // Flexi employees now use the same schedule-based cap as everyone else
            if (r.totalRenderHour.HasValue && r.totalRenderHour.Value > 0)
            {
                double breakHours = (r.totalBreaktimeMinute ?? 0) / 60.0;
                double netHours = r.totalRenderHour.Value - breakHours;
                return netHours > 0 ? netHours : r.totalRenderHour.Value;
            }

            return 8;
        }

        // ================= TIME KEEPING RULES =================

        private string GetRemarks(ReviewDTRModel r)
        {
            // ===============================================
            // PRIORITY 1: Check for WORKING on special days
            // ===============================================

            // Working on Legal Holiday (Regular Holiday)
            if (r.holidayType == "Legal Holiday" &&
                (r.biometricsDateIn.HasValue ||
                 r.overTimeDateIN.HasValue && r.overTimeIN.HasValue))
            {
                return "WORKING LEGAL HOLIDAY";
            }

            // Working on Special Holiday
            if (r.holidayType == "Special Holiday" &&
                (r.biometricsDateIn.HasValue ||
                 r.overTimeDateIN.HasValue && r.overTimeIN.HasValue))
            {
                return "WORKING SPECIAL HOLIDAY";
            }

            // Change schedule overrides rest day — check FIRST
            if (r.changeScheduleID.HasValue && !string.IsNullOrEmpty(r.changeScheduleReason))
            {
                if (r.changeScheduleTypeCode == "REST")
                    return "REST DAY";

                // Require actual biometrics — no clock-in = absent
                if (!r.biometricsDateIn.HasValue)
                    return "ABSENT";

                return "CHANGE SCHEDULE";
            }

            // Working on Rest Day — only if no approved change schedule
            if (r.isRestDay &&
                r.holidayType == null &&
                !r.changeScheduleID.HasValue &&
                (r.biometricsDateIn.HasValue ||
                 r.overTimeDateIN.HasValue && r.overTimeIN.HasValue))
            {
                return "WORKING REST DAY";
            }

            // ===============================================
            // PRIORITY 2: Half-day leave remarks
            // Checked BEFORE the general leave/OB/WFH block so
            // that half-day leave gets its own specific remark.
            // leaveType = 'first'  → employee is on leave for the first half
            //                        → works the second half → FIRST HALF LEAVE
            // leaveType = 'second' → employee is on leave for the second half
            //                        → works the first half → SECOND HALF LEAVE
            // ===============================================
            if (r.leaveCountDays == 0.5 && r.leaveType == "first" && r.leaveCode != "LWOP" && r.leaveCode != "ML" && r.leaveCode != "PL")
                return "FIRST HALF LEAVE";

            if (r.leaveCountDays == 0.5 && r.leaveType == "second" && r.leaveCode != "LWOP" && r.leaveCode != "ML" && r.leaveCode != "PL")
                return "SECOND HALF LEAVE";

            // ===============================================
            // PRIORITY 3: Non-working special statuses
            // ===============================================

            if (r.leaveCode == "LWOP") return "NO PAY LEAVE";
            if (r.leaveCode == "ML") return "MATERNITY LEAVE";
            if (r.leaveCode == "PL") return "PATERNITY LEAVE";
            if (r.leaveCode == "SUS") return "SUSPENDED";
            if (r.leaveType != null) return "ON LEAVE";
            // OB approved — tagged Official Business and counted present,
            // even without a biometrics clock-in (e.g. employee was out
            // on official business for the whole day)
            if (!string.IsNullOrEmpty(r.obReason))
            {
                return "OFFICIAL BUSINESS";
            }

            // WFH requires biometrics — no clock-in = absent
            if (!string.IsNullOrEmpty(r.wfhReason))
            {
                if (!r.biometricsDateIn.HasValue)
                    return "ABSENT";
                return "WORK FROM HOME";
            }

            // ===============================================
            // PRIORITY 4: Regular day statuses
            // ===============================================

            if (r.isRestDay) return "REST DAY";
            if (r.holidayType == "Legal Holiday") return "LEGAL HOLIDAY";
            if (r.holidayType == "Special Holiday") return "SPECIAL HOLIDAY";

            if (!r.scheduleTimeIn.HasValue) return "NO SCHEDULE";
            if (r.biometricsDateIn.HasValue && !r.biometricsDateOut.HasValue) return "NO TIMEOUT";
            if (!r.biometricsDateIn.HasValue && r.holidayType == null && r.leaveType == null) return "ABSENT";

            return "PRESENT";
        }

        private double ComputeLate(ReviewDTRModel r, bool isFlexiTime)
        {
            // Don't compute late for rest days WITHOUT a schedule
            if (r.isRestDay && !r.scheduleTimeIn.HasValue)
                return 0;

            // Don't compute late for holidays WITHOUT a schedule
            if (r.holidayType != null && !r.scheduleTimeIn.HasValue)
                return 0;

            if (!r.scheduleTimeIn.HasValue || !r.biometricsDateIn.HasValue)
                return 0;

            var diff = (r.biometricsDateIn.Value - r.scheduleTimeIn.Value).TotalMinutes;

            if (diff <= 0) return 0;

            return Math.Round(diff, 3, MidpointRounding.AwayFromZero);
        }

        private double ComputeUnderTime(ReviewDTRModel r, bool isFlexiTime)
        {
            // Flexi employees now use the same undertime measurement as everyone else
            // (actual time-out vs. scheduled time-out) — only the "never late" perk
            // in ComputeLate() remains flexi-specific.

            // Full day leave = no undertime
            if (r.leaveCountDays >= 1)
                return 0;

            // Half day leave — compare biometricsDateOut vs shifted scheduleTimeOut
            if (r.leaveCountDays == 0.5)
            {
                if (!r.scheduleTimeOut.HasValue || !r.biometricsDateOut.HasValue)
                    return 0;

                return r.biometricsDateOut < r.scheduleTimeOut
                    ? Math.Round(
                        (r.scheduleTimeOut.Value - r.biometricsDateOut.Value).TotalMinutes,
                        3, MidpointRounding.AwayFromZero)
                    : 0;
            }

            // Normal undertime
            if (!r.scheduleTimeOut.HasValue || !r.biometricsDateOut.HasValue)
                return 0;

            return r.biometricsDateOut < r.scheduleTimeOut
                ? Math.Round(
                    (r.scheduleTimeOut.Value - r.biometricsDateOut.Value).TotalMinutes,
                    3, MidpointRounding.AwayFromZero)
                : 0;
        }

        /// <summary>
        /// Computes normal day hours with proper separation of Regular, OT, and ND
        /// Returns: (regularHours, otHours, ndHours)
        /// </summary>
        private (double regularHours, double otHours, double ndHours, double otndHours) ComputeNormalDayHours(
            ReviewDTRModel r)
        {
            // Must have biometrics
            if (!r.biometricsDateIn.HasValue || !r.biometricsDateOut.HasValue)
                return (0, 0, 0, 0);

            // Must have schedule
            if (!r.scheduleTimeOut.HasValue)
                return (0, 0, 0, 0);

            DateTime workStart = r.biometricsDateIn.Value;
            DateTime workEnd = r.biometricsDateOut.Value;
            DateTime scheduleEnd = r.scheduleTimeOut.Value;

            // ND period: 10 PM to 6 AM
            DateTime ndStart = workStart.Date.AddHours(22); // 10 PM
            DateTime ndEnd = ndStart.AddHours(8);          // 6 AM next day

            double regularHours = 0;
            double otHours = 0;
            double ndHours = 0;
            double otNDHours = 0; // OT hours that fall within 10PM-6AM tracked separately

            // STEP 1: Calculate REGULAR hours (up to schedule end, excluding ND)
            DateTime regularEnd = scheduleEnd < workEnd ? scheduleEnd : workEnd;

            if (workStart < regularEnd)
            {
                // For ND: use the later of actual clock-in vs schedule start
                // so early clock-in minutes before the shift are NOT counted as ND
                DateTime ndWorkStart = r.scheduleTimeIn.HasValue && r.scheduleTimeIn.Value > workStart
                    ? r.scheduleTimeIn.Value
                    : workStart;

                regularHours = (regularEnd - workStart).TotalHours;

                // Calculate ND over the full schedule window (not just up to regularEnd)
                // so overnight shifts hitting 10PM–6AM get the full ND credit.
                DateTime ndWindowEnd = r.scheduleTimeOut.HasValue && r.scheduleTimeOut.Value < workEnd
                    ? r.scheduleTimeOut.Value
                    : workEnd;

                var (_, ndHoursTotal) = SplitHoursByNDPeriod(ndWorkStart, ndWindowEnd, ndStart, ndEnd);
                ndHours += ndHoursTotal;
            }

            // STEP 2: Calculate OT hours (after schedule end)
            // Managers don't get OT
            if (r.overTimeDateIN.HasValue && r.overTimeDateOUT.HasValue)
            {
                var approvedOTStart = r.overTimeDateIN.Value.Add(r.overTimeIN ?? TimeSpan.Zero);
                var approvedOTEnd = r.overTimeDateOUT.Value.Add(r.overTimeOUT ?? TimeSpan.Zero);

                // OT can only start AFTER scheduled end
                var actualOTStart = scheduleEnd;
                var actualOTEnd = workEnd;

                // Find overlap between approved OT and actual work after schedule
                var otStart = approvedOTStart > actualOTStart ? approvedOTStart : actualOTStart;
                var otEnd = approvedOTEnd < actualOTEnd ? approvedOTEnd : actualOTEnd;

                if (otEnd > otStart)
                {
                    // Split OT hours into ND and non-ND
                    var (otNonND, otND) = SplitHoursByNDPeriod(otStart, otEnd, ndStart, ndEnd);

                    // Total OT hours including ND portion for minimum check
                    // Total OT hours including ND portion
                    double totalOTHours = otNonND + otND;

                    // No minimum-hour gate — any OT duration counted in full
                    otHours = totalOTHours; // full OT duration (non-ND + ND)
                    otNDHours = otND;       // ND overlap portion only
                }
            }

            return (
                Math.Round(regularHours, 3, MidpointRounding.AwayFromZero),
                Math.Round(otHours, 3, MidpointRounding.AwayFromZero),
                Math.Round(ndHours, 3, MidpointRounding.AwayFromZero),
                Math.Round(otNDHours, 3, MidpointRounding.AwayFromZero)
            );
        }

        /// <summary>
        /// Computes holiday hours using EITHER biometrics OR OT filing times
        /// PRIORITY: Biometrics (actual clock in/out) > OT Filing times (planned work)
        /// Used for both Special Holidays and Legal Holidays
        /// Returns: (regularHours, otHours, ndHours)
        /// </summary>
        private (double regularHours, double otHours, double ndHours, double ndOTHours) ComputeHolidayHours(
            ReviewDTRModel r)
        {
            DateTime? workStart = null;
            DateTime? workEnd = null;

            if (r.biometricsDateIn.HasValue && r.biometricsDateOut.HasValue)
            {
                // If employee clocked in early and schedule exists, start from schedule time
                workStart = r.scheduleTimeIn.HasValue && r.biometricsDateIn.Value < r.scheduleTimeIn.Value
                    ? r.scheduleTimeIn.Value
                    : r.biometricsDateIn.Value;
                workEnd = r.biometricsDateOut.Value;
            }
            else if (r.overTimeDateIN.HasValue && r.overTimeDateOUT.HasValue &&
                     r.overTimeIN.HasValue && r.overTimeOUT.HasValue)
            {
                workStart = r.overTimeDateIN.Value.Add(r.overTimeIN.Value);
                workEnd = r.overTimeDateOUT.Value.Add(r.overTimeOUT.Value);
            }

            if (!workStart.HasValue || !workEnd.HasValue)
                return (0, 0, 0, 0);

            DateTime actualWorkStart = workStart.Value;
            DateTime actualWorkEnd = workEnd.Value;

            if (actualWorkEnd <= actualWorkStart)
                return (0, 0, 0, 0);

            DateTime ndStart = actualWorkStart.Date.AddHours(22);
            DateTime ndEnd = ndStart.AddHours(8);

            double regularHours = 0;
            double otHours = 0;
            double ndHours = 0;
            double ndOTHours = 0;

            var totalHours = (actualWorkEnd - actualWorkStart).TotalHours;

            // First 8 hours — regular portion
            DateTime regularEnd = actualWorkStart.AddHours(Math.Min(8, totalHours));

            // Cap ND start to scheduleTimeIn if employee arrived early
            DateTime ndHolidayWorkStart = r.scheduleTimeIn.HasValue && r.scheduleTimeIn.Value > actualWorkStart
                ? r.scheduleTimeIn.Value
                : actualWorkStart;

            regularHours = (regularEnd - actualWorkStart).TotalHours;

            // Calculate ND over the full schedule window (not just the first 8 regular hours)
            // so overnight shifts hitting 10PM–6AM get the full ND credit.
            DateTime ndWindowEnd = r.scheduleTimeOut.HasValue && r.scheduleTimeOut.Value < actualWorkEnd
                ? r.scheduleTimeOut.Value
                : actualWorkEnd;

            var (_, ndHoursTotal) = SplitHoursByNDPeriod(ndHolidayWorkStart, ndWindowEnd, ndStart, ndEnd);
            ndHours = ndHoursTotal;

            // Beyond 8 hours — OT portion
            // Requires approved OT filing, same as normal day
            if (r.overTimeDateIN.HasValue && r.overTimeDateOUT.HasValue
                && r.overTimeIN.HasValue && r.overTimeOUT.HasValue)
            {
                var approvedOTStart = r.overTimeDateIN.Value.Add(r.overTimeIN.Value);
                var approvedOTEnd = r.overTimeDateOUT.Value.Add(r.overTimeOUT.Value);

                // OT can only start AFTER the first 8 regular hours
                var actualOTStart = regularEnd;
                var actualOTEnd = actualWorkEnd;

                // Overlap between approved OT window AND actual biometrics beyond 8hrs
                var otStart = approvedOTStart > actualOTStart ? approvedOTStart : actualOTStart;
                var otEnd = approvedOTEnd < actualOTEnd ? approvedOTEnd : actualOTEnd;

                if (otEnd > otStart)
                {
                    var (otNonND, otND) = SplitHoursByNDPeriod(otStart, otEnd, ndStart, ndEnd);
                    var totalOTHours = otNonND + otND;

                    // No minimum-hour gate — any OT duration counted in full
                    otHours = totalOTHours; // full OT duration (non-ND + ND)
                    ndOTHours = otND;       // ND overlap portion only
                }
            }

            return (
                Math.Round(regularHours, 3, MidpointRounding.AwayFromZero),
                Math.Round(otHours, 3, MidpointRounding.AwayFromZero),
                Math.Round(ndHours, 3, MidpointRounding.AwayFromZero),
                Math.Round(ndOTHours, 3, MidpointRounding.AwayFromZero)
            );
        }

        /// <summary>
        /// Computes rest day hours using EITHER biometrics OR OT filing times
        /// PRIORITY: Biometrics (actual clock in/out) > OT Filing times (planned work)
        /// Returns: (regularHours, otHours, ndHours)
        /// </summary>
        private (double regularHours, double otHours, double ndHours, double ndOTHours) ComputeApprovedRDOT(
            ReviewDTRModel r)
        {
            DateTime? workStart = null;
            DateTime? workEnd = null;

            if (r.biometricsDateIn.HasValue && r.biometricsDateOut.HasValue)
            {
                // If employee clocked in early and schedule exists, start from schedule time
                workStart = r.scheduleTimeIn.HasValue && r.biometricsDateIn.Value < r.scheduleTimeIn.Value
                    ? r.scheduleTimeIn.Value
                    : r.biometricsDateIn.Value;
                workEnd = r.biometricsDateOut.Value;
            }
            else if (r.overTimeDateIN.HasValue && r.overTimeDateOUT.HasValue &&
                     r.overTimeIN.HasValue && r.overTimeOUT.HasValue)
            {
                workStart = r.overTimeDateIN.Value.Add(r.overTimeIN.Value);
                workEnd = r.overTimeDateOUT.Value.Add(r.overTimeOUT.Value);
            }

            if (!workStart.HasValue || !workEnd.HasValue)
                return (0, 0, 0, 0);

            DateTime actualWorkStart = workStart.Value;
            DateTime actualWorkEnd = workEnd.Value;

            if (actualWorkEnd <= actualWorkStart)
                return (0, 0, 0, 0);

            DateTime ndStart = actualWorkStart.Date.AddHours(22);
            DateTime ndEnd = ndStart.AddHours(8);

            double regularHours = 0;
            double otHours = 0;
            double ndHours = 0;
            double ndOTHours = 0;

            var totalHours = (actualWorkEnd - actualWorkStart).TotalHours;

            // First 8 hours — regular RD portion
            DateTime regularEnd = actualWorkStart.AddHours(Math.Min(8, totalHours));

            // Cap ND start to scheduleTimeIn if employee arrived early
            DateTime ndRDWorkStart = r.scheduleTimeIn.HasValue && r.scheduleTimeIn.Value > actualWorkStart
                ? r.scheduleTimeIn.Value
                : actualWorkStart;

            regularHours = (regularEnd - actualWorkStart).TotalHours;

            // Calculate ND over the full schedule window (not just the first 8 regular hours)
            // so overnight shifts hitting 10PM–6AM get the full ND credit.
            DateTime ndWindowEnd = r.scheduleTimeOut.HasValue && r.scheduleTimeOut.Value < actualWorkEnd
                ? r.scheduleTimeOut.Value
                : actualWorkEnd;

            var (_, ndHoursTotal) = SplitHoursByNDPeriod(ndRDWorkStart, ndWindowEnd, ndStart, ndEnd);
            ndHours = ndHoursTotal;

            // Beyond 8 hours — RD OT portion
            // Requires approved OT filing, same as normal day
            if (r.overTimeDateIN.HasValue && r.overTimeDateOUT.HasValue
                && r.overTimeIN.HasValue && r.overTimeOUT.HasValue)
            {
                var approvedOTStart = r.overTimeDateIN.Value.Add(r.overTimeIN.Value);
                var approvedOTEnd = r.overTimeDateOUT.Value.Add(r.overTimeOUT.Value);

                // OT can only start AFTER the first 8 regular hours
                var actualOTStart = regularEnd;
                var actualOTEnd = actualWorkEnd;

                // Overlap between approved OT window AND actual biometrics beyond 8hrs
                var otStart = approvedOTStart > actualOTStart ? approvedOTStart : actualOTStart;
                var otEnd = approvedOTEnd < actualOTEnd ? approvedOTEnd : actualOTEnd;

                if (otEnd > otStart)
                {
                    var (otNonND, otND) = SplitHoursByNDPeriod(otStart, otEnd, ndStart, ndEnd);
                    var totalOTHours = otNonND + otND;

                    // No minimum-hour gate — any OT duration counted in full
                    otHours = totalOTHours; // full OT duration (non-ND + ND)
                    ndOTHours = otND;       // ND overlap portion only
                }
            }

            return (
                Math.Round(regularHours, 3, MidpointRounding.AwayFromZero),
                Math.Round(otHours, 3, MidpointRounding.AwayFromZero),
                Math.Round(ndHours, 3, MidpointRounding.AwayFromZero),
                Math.Round(ndOTHours, 3, MidpointRounding.AwayFromZero)
            );
        }

        private (double nonND, double nd) SplitHoursByNDPeriod(
            DateTime start, DateTime end, DateTime ndStart, DateTime ndEnd)
        {
            if (start >= end) return (0, 0);

            double totalHours = (end - start).TotalHours;
            double ndHours = 0;

            // ── Window 1: current night ──
            // 10 PM of start.Date → 6 AM next day (passed in as ndStart/ndEnd)
            var overlapStart1 = start > ndStart ? start : ndStart;
            var overlapEnd1 = end < ndEnd ? end : ndEnd;
            if (overlapEnd1 > overlapStart1)
                ndHours += (overlapEnd1 - overlapStart1).TotalHours;

            // ── Window 2: previous night ──
            // 10 PM of the day before start.Date → 6 AM of start.Date
            // Catches early-morning hours (e.g. 01:00 AM, 03:36 AM – 06:00 AM)
            // that belong to the tail end of the previous night's ND window.
            // start.Date.AddHours(-2) = 22:00 (10 PM) of the previous day
            var prevNdStart = start.Date.AddHours(-2);  // 10 PM previous day
            var prevNdEnd = start.Date.AddHours(6);   // 6 AM of start.Date

            var overlapStart2 = start > prevNdStart ? start : prevNdStart;
            var overlapEnd2 = end < prevNdEnd ? end : prevNdEnd;
            if (overlapEnd2 > overlapStart2)
                ndHours += (overlapEnd2 - overlapStart2).TotalHours;

            double nonNDHours = totalHours - ndHours;

            return (nonNDHours, ndHours);
        }

        // ================= PROCESS DTR =================

        public async Task<int> ProcessSingleEmployeeAsync(
            string employeeNo,
            DateTime dateFrom,
            DateTime dateTo,
            string branchCode,
            int cutOffType,
            string user,
            string dateMonth
        )
        {
            //var sql = @"DELETE FROM p_biometricsline
            //            WHERE employeeNo = @EmployeeNo
            //              AND dateFrom = @DateFrom
            //              AND dateTo = @DateTo
            //              AND cutOffType = @CutOffType;";

            var sql = @"DELETE FROM p_biometricsline
                        WHERE employeeNo = @EmployeeNo
                          AND cutOffType = @CutOffType
                          AND dateFrom <= @DateTo
                          AND dateTo >= @DateFrom;";

            await _db.ExecuteAsync(sql, new
            {
                EmployeeNo = employeeNo,
                DateFrom = dateFrom,
                DateTo = dateTo,
                CutOffType = cutOffType
            });

            var dailyRows = await GetDailyRowsAsync(dateFrom, dateTo, branchCode, employeeNo);

            if (!dailyRows.Any())
                return 0;

            if (_db is not MySqlConnection con)
                throw new InvalidOperationException("MySqlConnection required");

            if (con.State != ConnectionState.Open)
                await con.OpenAsync();

            using var tran = con.BeginTransaction();

            try
            {
                const string deleteSql = @"
                    DELETE FROM p_biometricsline
                    WHERE employeeNo = @employeeNo
                      AND cutOffType = @cutOffType
                      AND dateFrom  <= @dateTo
                      AND dateTo    >= @dateFrom;";

                await con.ExecuteAsync(deleteSql, new
                {
                    employeeNo,
                    dateFrom,
                    dateTo,
                    cutOffType
                }, transaction: tran);

                int inserted = await InsertBiometricsLinesBatchAsync(
                    dailyRows,
                    dateFrom,
                    dateTo,
                    cutOffType,
                    user,
                    dateMonth,
                    con,
                    tran
                );

                tran.Commit();
                return inserted;
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        // =====================================================
        // INSERT PROCESS (BATCH)
        // =====================================================

        private async Task<int> InsertBiometricsLinesBatchAsync(
            List<ReviewDTRViewModel> rows,
            DateTime dateFrom,
            DateTime dateTo,
            int cutOffType,
            string user,
            string dateMonth,
            MySqlConnection con,
            IDbTransaction tran
        )
        {
            var sql = new StringBuilder();

            sql.Append(@"
                INSERT INTO p_biometricsline
                (
                    methodType,
                    cutOffType,
                    dateMonth,
                    dateYear,
                    dateFrom,
                    dateTo,
                    date,
                    scheduleIn,
                    scheduleOut,
                    timeIn,
                    timeOut,
                    employeeNo,
                    branchCode,
                    departmentCode,
                    render,
                    renderOT,
                    renderNSD,
                    renderREST,
                    renderRESTOT,
                    renderNSDREST,
                    renderS,
                    renderOTS,
                    renderNSDS,
                    renderL,
                    renderOTL,
                    renderNSDL,
                    renderNSDOT,        
                    renderNSDRESTOT,   
                    renderNSDOTS,      
                    renderNSDOTL,   
                    renderRESTS,
                    renderRESTOTS,
                    renderNSDRESTS,
                    renderNSDRESTOTS,
                    renderNSDRESTL,
                    renderNSDRESTOTL,
                    renderRESTL,
                    renderRESTOTL, 
                    renderLate,
                    renderUndertime,
                    attendanceStatus,
                    holidayType,
                    presentCount,
                    absentCount,
                    scheduleStatus,
                    isActive,
                    dtAdded,
                    addedByUser,
                    statusName
                )
                VALUES
            ");

            var parameters = new DynamicParameters();
            var values = new List<string>();

            for (int i = 0; i < rows.Count; i++)
            {
                var d = rows[i];

                values.Add($@"
                    (
                        'DTR_PROCESS',
                        @CutOffType,
                        @DateMonth,
                        YEAR(@WorkDate{i}),
                        @DateFrom,
                        @DateTo,
                        @WorkDate{i},
                        @ScheduleIn{i},
                        @ScheduleOut{i},
                        @TimeIn{i},
                        @TimeOut{i},
                        @EmployeeNo{i},
                        @BranchCode{i},
                        @DepartmentCode{i},
                        @Render{i},
                        @RenderOT{i},
                        @RenderNSD{i},
                        @RenderREST{i},
                        @RenderRESTOT{i},
                        @RenderNSDREST{i},
                        @RenderS{i},
                        @RenderOTS{i},
                        @RenderNSDS{i},
                        @RenderL{i},
                        @RenderOTL{i},
                        @RenderNSDL{i},
                        @RenderNSDOT{i},       
                        @RenderNSDRESTOT{i},  
                        @RenderNSDOTS{i},     
                        @RenderNSDOTL{i},  
                        @RenderRESTS{i},
                        @RenderRESTOTS{i},
                        @RenderNSDRESTS{i},
                        @RenderNSDRESTOTS{i},
                        @RenderNSDRESTL{i},
                        @RenderNSDRESTOTL{i},
                        @RenderRESTL{i},
                        @RenderRESTOTL{i},
                        @LateMinutes{i},
                        @UnderTimeMinutes{i},
                        @AttendanceStatus{i},
                        @HolidayType{i},
                        @PresentCount{i},
                        @AbsentCount{i},
                        @ScheduleStatus{i},
                        1,
                        NOW(),
                        @User,
                        'Open'
                    )");

                parameters.Add($"WorkDate{i}", DateTime.Parse(d.workDate));
                parameters.Add($"ScheduleIn{i}", d.scheduleTimeIn);
                parameters.Add($"ScheduleOut{i}", d.scheduleTimeOut);
                parameters.Add($"TimeIn{i}", d.biometricsDateIn);
                parameters.Add($"TimeOut{i}", d.biometricsDateOut);
                parameters.Add($"EmployeeNo{i}", d.employeeNo);
                parameters.Add($"BranchCode{i}", d.branchCode);
                parameters.Add($"DepartmentCode{i}", d.departmentCode);
                parameters.Add($"Render{i}", d.RenderHours);
                parameters.Add($"RenderOT{i}", d.OTHours);
                parameters.Add($"RenderNSD{i}", d.NDHours);
                parameters.Add($"RenderREST{i}", d.RDHours);
                parameters.Add($"RenderRESTOT{i}", d.RDOTHours);
                parameters.Add($"RenderNSDREST{i}", d.RDNDHours);
                parameters.Add($"RenderS{i}", d.SPLHolidayHours);
                parameters.Add($"RenderOTS{i}", d.SPLHolidayOTHours);
                parameters.Add($"RenderNSDS{i}", d.SPLHolidayNDHours);
                parameters.Add($"RenderL{i}", d.REGHolidayHours);
                parameters.Add($"RenderOTL{i}", d.REGHolidayOTHours);
                parameters.Add($"RenderNSDL{i}", d.REGHolidayNDHours);
                parameters.Add($"RenderNSDOT{i}", d.OTNDHours);
                parameters.Add($"RenderNSDRESTOT{i}", d.RDNDOTHours);
                parameters.Add($"RenderNSDOTS{i}", d.SPLHolidayNDOTHours);
                parameters.Add($"RenderNSDOTL{i}", d.REGHolidayNDOTHours);
                parameters.Add($"RenderRESTS{i}", d.SPLHolidayRESTHours);
                parameters.Add($"RenderRESTOTS{i}", d.SPLHolidayRESTOTHours);
                parameters.Add($"RenderNSDRESTS{i}", d.SPLHolidayRESTNDHours);
                parameters.Add($"RenderNSDRESTOTS{i}", d.SPLHolidayRESTNDOTHours);
                parameters.Add($"RenderNSDRESTL{i}", d.REGHolidayRESTNDHours);
                parameters.Add($"RenderNSDRESTOTL{i}", d.REGHolidayRESTNDOTHours);
                parameters.Add($"RenderRESTL{i}", d.REGHolidayRESTHours);
                parameters.Add($"RenderRESTOTL{i}", d.REGHolidayRESTOTHours);
                parameters.Add($"LateMinutes{i}", d.LateMinutes);
                parameters.Add($"UnderTimeMinutes{i}", d.UnderTimeMinutes);
                parameters.Add($"AttendanceStatus{i}", d.remarks);
                parameters.Add($"HolidayType{i}", d.holidayType);
                double presentCount = 0;
                if (d.remarks == "NO PAY LEAVE" && d.leaveCountDays == 0.5)
                {
                    presentCount = 0.5;
                    Console.WriteLine($">>> SET TO 0.5 for {d.employeeNo} on {d.workDate}");
                }
                else if (d.IsPresent)
                {
                    presentCount = 1;
                    Console.WriteLine($">>> SET TO 1 for {d.employeeNo} on {d.workDate}");
                }
                Console.WriteLine($">>> FINAL presentCount = {presentCount} for {d.employeeNo}");
                parameters.Add($"PresentCount{i}", presentCount);

                double absentCount = 0;
                if (d.remarks == "ABSENT" || d.remarks == "NO TIMEOUT" ||
                    d.remarks == "MATERNITY LEAVE" || d.remarks == "PATERNITY LEAVE" ||
                    d.remarks == "NO SCHEDULE" || d.remarks == "SUSPENDED")
                {
                    absentCount = 1;
                }
                else if (d.remarks == "NO PAY LEAVE")
                {
                    absentCount = d.leaveCountDays == 0.5 ? 0.5 : 1;
                }
                parameters.Add($"AbsentCount{i}", absentCount);
                parameters.Add($"ScheduleStatus{i}", d.remarks);
            }

            sql.Append(string.Join(",", values));

            parameters.Add("CutOffType", cutOffType);
            parameters.Add("DateFrom", dateFrom);
            parameters.Add("DateTo", dateTo);
            parameters.Add("User", user);
            parameters.Add("DateMonth", dateMonth);

            return await con.ExecuteAsync(sql.ToString(), parameters, tran);
        }

        /// <summary>
        /// Returns true when any posted payroll cutoff period overlaps
        /// with [dateFrom, dateTo] in p_biometricsline.
        /// </summary>
        public async Task<bool> IsDateRangePostedAsync(DateTime dateFrom, DateTime dateTo, string branchCode = "")
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM p_biometricsline
                WHERE statusName = 'posted'
                  AND isActive   = 1
                  AND dateFrom  <= @dateTo
                  AND dateTo    >= @dateFrom
                  AND (@branchCode = '' OR branchCode = @branchCode)
                LIMIT 1";

            var count = await _db.ExecuteScalarAsync<int>(sql, new
            {
                dateFrom = dateFrom.Date,
                dateTo = dateTo.Date,
                branchCode
            });

            return count > 0;
        }
    }

    // ================= DTR COMPUTED MODEL =================
    internal class DTRComputed
    {
        public ReviewDTRModel Raw { get; set; }
        public string Remarks { get; set; }
        public bool IsPresent { get; set; }
        public bool IsAbsent { get; set; }
        public double RenderHours { get; set; }
        public double LateMinutes { get; set; }
        public double UnderTimeMinutes { get; set; }
        public double NDHours { get; set; }
        public double OTNDHours { get; set; }
        public double RDNDHours { get; set; }
        public double OTHours { get; set; }
        public double RDHours { get; set; }
        public double RDOTHours { get; set; }
        public double SPLHours { get; set; }
        public double REGHours { get; set; }
        public DateTime? OTIn { get; set; }
        public DateTime? OTOut { get; set; }
        public string OTReason { get; set; }
        public double SPLHolidayHours { get; set; }
        public double SPLHolidayOTHours { get; set; }
        public double SPLHolidayNDHours { get; set; }
        public double SPLHolidayNDOTHours { get; set; }
        public double REGHolidayHours { get; set; }
        public double REGHolidayOTHours { get; set; }
        public double REGHolidayNDHours { get; set; }
        public double REGHolidayNDOTHours { get; set; }
        public double RDNDOTHours { get; set; }
        // Special Holiday Rest Day
        public double SPLHolidayRESTHours { get; set; }
        public double SPLHolidayRESTOTHours { get; set; }
        public double SPLHolidayRESTNDHours { get; set; }
        public double SPLHolidayRESTNDOTHours { get; set; }

        // Legal Holiday Rest Day
        public double REGHolidayRESTNDHours { get; set; }
        public double REGHolidayRESTNDOTHours { get; set; }
        public double REGHolidayRESTHours { get; set; }
        public double REGHolidayRESTOTHours { get; set; }
    }
}