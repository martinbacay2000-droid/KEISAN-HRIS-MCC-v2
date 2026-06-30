using Dapper;
using System.Data;

namespace KEISAN_HRIS_v2.Services.OtherServices 
{
    public class NotificationCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NotificationCleanupService> _logger;

        private readonly TimeSpan _interval = TimeSpan.FromHours(24);

        public NotificationCleanupService(
            IServiceScopeFactory scopeFactory,
            ILogger<NotificationCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Notification Cleanup Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Notification cleanup running at: {Time}", DateTime.Now);

                try
                {
                    await CleanupOldNotificationsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during notification cleanup.");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Notification Cleanup Service stopped.");
        }

        private async Task CleanupOldNotificationsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

            var deleted = await db.ExecuteAsync(@"
                DELETE FROM s_notification
                WHERE dtCreated < DATE_SUB(NOW(), INTERVAL 20 DAY)
                  AND isActive = 1");

            if (deleted > 0)
                _logger.LogInformation(
                    "Notification cleanup: {Count} old notification(s) deleted at {Time}.",
                    deleted, DateTime.Now);
            else
                _logger.LogInformation(
                    "Notification cleanup: No old notifications to delete at {Time}.", DateTime.Now);
        }
    }
}