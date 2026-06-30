using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace KEISAN_HRIS_v2.Services.TimeKeeping
{
    public class AutoPunchBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AutoPunchBackgroundService> _logger;

        // Fixed render employees
        private static readonly string[] FixedRenderEmployees = { "R00006", "R00002" };

        public AutoPunchBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<AutoPunchBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // =====================================================
        // BACKGROUND SERVICE ENTRY POINT
        // Runs backfill first on startup, then loops daily
        // =====================================================

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AutoPunchBackgroundService started.");

            // ── BACKFILL on startup ──────────────────────────────
            // Fills any missing auto-punch records from the past.
            // Safe to run every startup — skips existing records.
            // Starts from Jan 1 of the current year up to yesterday.
            var backfillFrom = new DateTime(DateTime.Today.Year, 1, 1);
            var backfillTo = DateTime.Today.AddDays(-1);

            if (backfillTo >= backfillFrom)
            {
                _logger.LogInformation(
                    "AutoPunch Backfill starting: {From} → {To}",
                    backfillFrom.ToString("yyyy-MM-dd"),
                    backfillTo.ToString("yyyy-MM-dd"));

                var (inserted, skipped, _) = await BackfillAsync(backfillFrom, backfillTo, stoppingToken);

                _logger.LogInformation(
                    "AutoPunch Backfill complete. Inserted: {Inserted} | Skipped: {Skipped}",
                    inserted, skipped);
            }

            // ── DAILY LOOP ───────────────────────────────────────
            // Runs every midnight to punch yesterday's date
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var nextRun = now.Date.AddDays(1); // midnight tonight
                var delay = nextRun - now;

                _logger.LogInformation("AutoPunch next run at: {NextRun}", nextRun);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                await RunAutoPunchAsync(DateTime.Today.AddDays(-1));
            }

            _logger.LogInformation("AutoPunchBackgroundService stopped.");
        }

        // =====================================================
        // BACKFILL — iterates date range, processes each day
        // =====================================================

        private async Task<(int inserted, int skipped, List<string> log)> BackfillAsync(
            DateTime dateFrom, DateTime dateTo, CancellationToken stoppingToken = default)
        {
            int inserted = 0;
            int skipped = 0;
            var log = new List<string>();

            for (var date = dateFrom.Date; date <= dateTo.Date; date = date.AddDays(1))
            {
                if (stoppingToken.IsCancellationRequested) break;

                var (dateInserted, dateSkipped, dateLog) = await RunAutoPunchWithResultAsync(date);
                inserted += dateInserted;
                skipped += dateSkipped;
                log.AddRange(dateLog);
            }

            return (inserted, skipped, log);
        }

        // =====================================================
        // DAILY TRIGGER — wrapper for single date
        // =====================================================

        private async Task RunAutoPunchAsync(DateTime date)
        {
            await RunAutoPunchWithResultAsync(date);
        }

        // =====================================================
        // CORE — processes a single date for all fixed employees
        // =====================================================

        private async Task<(int inserted, int skipped, List<string> log)> RunAutoPunchWithResultAsync(
            DateTime date)
        {
            int inserted = 0;
            int skipped = 0;
            var log = new List<string>();

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

                string weekDayName = date.DayOfWeek.ToString(); // e.g. "Monday"

                foreach (var employeeNo in FixedRenderEmployees)
                {
                    try
                    {
                        // Check if biometrics already exists for this date
                        var exists = await db.ExecuteScalarAsync<int>(@"
                            SELECT COUNT(1)
                            FROM u_biometrics
                            WHERE employeeNo     = @EmployeeNo
                              AND biometricsDate = @Date
                              AND isActive       = 1",
                            new { EmployeeNo = employeeNo, Date = date });

                        if (exists > 0)
                        {
                            var msg = $"SKIP | {employeeNo} | {date:yyyy-MM-dd} | already has biometrics";
                            log.Add(msg);
                            _logger.LogInformation("AutoPunch: {Msg}", msg);
                            skipped++;
                            continue;
                        }

                        // Get schedule for this day (latest effectivity date, non-destructive)
                        var schedule = await db.QueryFirstOrDefaultAsync(@"
                            SELECT timeIn, timeOut, isRestDay
                            FROM e_schedule
                            WHERE employeeNo      = @EmployeeNo
                              AND weekDayName     = @WeekDayName
                              AND isActive        = 1
                              AND effectivityDate = (
                                  SELECT MAX(s2.effectivityDate)
                                  FROM e_schedule s2
                                  WHERE s2.employeeNo      = @EmployeeNo
                                    AND s2.weekDayName     = @WeekDayName
                                    AND s2.isActive        = 1
                                    AND s2.effectivityDate <= @Date
                              )",
                            new
                            {
                                EmployeeNo = employeeNo,
                                WeekDayName = weekDayName,
                                Date = date
                            });

                        // Skip if no schedule found
                        if (schedule == null)
                        {
                            var msg = $"SKIP | {employeeNo} | {date:yyyy-MM-dd} | no schedule found";
                            log.Add(msg);
                            _logger.LogInformation("AutoPunch: {Msg}", msg);
                            skipped++;
                            continue;
                        }

                        // Skip if rest day
                        if (schedule.isRestDay != null && schedule.isRestDay == 1)
                        {
                            var msg = $"SKIP | {employeeNo} | {date:yyyy-MM-dd} | rest day";
                            log.Add(msg);
                            _logger.LogInformation("AutoPunch: {Msg}", msg);
                            skipped++;
                            continue;
                        }

                        // Skip if Legal Holiday or Special Holiday ──────────────
                        var holiday = await db.QueryFirstOrDefaultAsync(@"
                            SELECT holidayType
                            FROM s_holiday
                            WHERE holidayDate = @Date
                            LIMIT 1",
                            new { Date = date });

                        if (holiday != null)
                        {
                            var msg = $"SKIP | {employeeNo} | {date:yyyy-MM-dd} | holiday ({holiday.holidayType})";
                            log.Add(msg);
                            _logger.LogInformation("AutoPunch: {Msg}", msg);
                            skipped++;
                            continue;
                        }

                        // Skip if timeIn or timeOut is null
                        if (schedule.timeIn == null || schedule.timeOut == null)
                        {
                            var msg = $"SKIP | {employeeNo} | {date:yyyy-MM-dd} | timeIn or timeOut is NULL";
                            log.Add(msg);
                            _logger.LogInformation("AutoPunch: {Msg}", msg);
                            skipped++;
                            continue;
                        }

                        // Safe cast — MySQL returns time columns as TimeSpan via Dapper
                        TimeSpan timeIn = schedule.timeIn is TimeSpan ti ? ti : TimeSpan.Parse(schedule.timeIn.ToString());
                        TimeSpan timeOut = schedule.timeOut is TimeSpan to ? to : TimeSpan.Parse(schedule.timeOut.ToString());
                        DateTime dateOut = timeOut < timeIn
                            ? date.AddDays(1)
                            : date;

                        // Insert auto-punch record
                        await db.ExecuteAsync(@"
                            INSERT INTO u_biometrics
                            (
                                employeeNo,
                                biometricsDate,
                                biometricsDateOut,
                                biometricsTimeIn,
                                biometricsTimeOut,
                                biometricsDeviceLog,
                                isActive,
                                statusName,
                                methodType,
                                addedByUser,
                                dtAdded
                            )
                            VALUES
                            (
                                @EmployeeNo,
                                @BiometricsDate,
                                @BiometricsDateOut,
                                @BiometricsTimeIn,
                                @BiometricsTimeOut,
                                'auto-punch',
                                1,
                                'auto-punch',
                                'AUTO_PUNCH',
                                'SYSTEM',
                                NOW()
                            )",
                            new
                            {
                                EmployeeNo = employeeNo,
                                BiometricsDate = date,
                                BiometricsDateOut = dateOut,
                                BiometricsTimeIn = timeIn,
                                BiometricsTimeOut = timeOut
                            });

                        var insertMsg = $"INSERT | {employeeNo} | {date:yyyy-MM-dd} | {timeIn} → {timeOut} (out: {dateOut:yyyy-MM-dd})";
                        log.Add(insertMsg);
                        _logger.LogInformation("AutoPunch: {Msg}", insertMsg);
                        inserted++;
                    }
                    catch (Exception ex)
                    {
                        // Per-employee error — don't stop the entire job
                        var errMsg = $"ERROR | {employeeNo} | {date:yyyy-MM-dd} | {ex.Message}";
                        log.Add(errMsg);
                        _logger.LogError(ex, "AutoPunch: {Msg}", errMsg);
                    }
                }
            }
            catch (Exception ex)
            {
                var errMsg = $"FATAL | {date:yyyy-MM-dd} | {ex.Message}";
                log.Add(errMsg);
                _logger.LogError(ex, "AutoPunch: {Msg}", errMsg);
            }

            return (inserted, skipped, log);
        }
    }
}