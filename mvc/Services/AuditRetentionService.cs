using Microsoft.EntityFrameworkCore;
using mvc.Data;

namespace mvc.Services
{
    public class AuditRetentionService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuditRetentionService> _logger;

        public AuditRetentionService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AuditRetentionService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PurgeAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Audit retention cleanup failed.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task PurgeAsync(CancellationToken stoppingToken)
        {
            int retentionDays = _configuration.GetValue("Audit:RetentionDays", 180);
            if (retentionDays <= 0)
            {
                return; // retention disabled
            }

            DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            using IServiceScope scope = _scopeFactory.CreateScope();
            mvcContext context = scope.ServiceProvider.GetRequiredService<mvcContext>();

            int removedAudits = await context.AuditLogs
                .Where(a => a.TimestampUtc < cutoff)
                .ExecuteDeleteAsync(stoppingToken);

            if (removedAudits > 0)
            {
                _logger.LogInformation("Purged {Count} audit log entries older than {RetentionDays} days.", removedAudits, retentionDays);
            }

            int removedPageViews = await context.PageViews
                .Where(p => p.TimestampUtc < cutoff)
                .ExecuteDeleteAsync(stoppingToken);

            if (removedPageViews > 0)
            {
                _logger.LogInformation("Purged {Count} page view entries older than {RetentionDays} days.", removedPageViews, retentionDays);
            }
        }
    }
}