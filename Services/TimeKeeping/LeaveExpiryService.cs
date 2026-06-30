using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data;

namespace KEISAN_HRIS_v2.Services.TimeKeeping
{
    public class LeaveExpiryService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LeaveExpiryService> _logger;

        // Run once a day
        private readonly TimeSpan _interval = TimeSpan.FromHours(24);

        public LeaveExpiryService(IServiceScopeFactory scopeFactory, ILogger<LeaveExpiryService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("LeaveExpiryService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Calculate delay until next midnight
                var now = DateTime.Now;
                var nextMidnight = now.Date.AddDays(1); // tomorrow 00:00:00
                var delay = nextMidnight - now;

                _logger.LogInformation("LeaveExpiryService: Next run at {nextMidnight}", nextMidnight);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                await RunExpiryCheck();
            }

            _logger.LogInformation("LeaveExpiryService stopped.");
        }

        private async Task RunExpiryCheck()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

                if (db.State != ConnectionState.Open)
                    db.Open();

                string sql = @"
                    UPDATE m_leave
                    SET 
                        isActive           = 0,
                        dtLastModified     = NOW(),
                        lastModifiedByUser = 'SYSTEM'
                    WHERE 
                        leaveCode    = 'CTO'
                        AND statusName   = 'OFFSET CREDIT EARNED'
                        AND isActive     = 1
                        AND dtDeleted IS NOT NULL
                        AND NOW() >= dtDeleted";

                int rows = await db.ExecuteAsync(sql);
                _logger.LogInformation("LeaveExpiryService: {rows} expired CTO record(s) set to inactive.", rows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LeaveExpiryService: Error during expiry check.");
            }
        }
    }
}