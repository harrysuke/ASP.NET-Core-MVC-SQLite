using System.Threading.Channels;
using mvc.Data;
using mvc.Models;

namespace mvc.Services
{
    // Consumes the AuditWriteQueue channels and persists entries asynchronously,
    // batching SaveChanges per flush so the request thread never waits on a write.
    public class AuditWriteService : BackgroundService
    {
        private readonly AuditWriteQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AuditWriteService> _logger;

        public AuditWriteService(AuditWriteQueue queue, IServiceScopeFactory scopeFactory, ILogger<AuditWriteService> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var auditConsumer = ConsumeAsync(_queue.AuditLogs, stoppingToken);
            var pageViewConsumer = ConsumeAsync(_queue.PageViews, stoppingToken);

            await Task.WhenAll(auditConsumer, pageViewConsumer);
        }

        private async Task ConsumeAsync(Channel<object> channel, CancellationToken stoppingToken)
        {
            await foreach (object entry in channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    mvcContext context = scope.ServiceProvider.GetRequiredService<mvcContext>();

                    if (entry is AuditLog audit)
                    {
                        context.AuditLogs.Add(audit);
                        await context.SaveChangesAsync(stoppingToken);
                    }
                    else if (entry is PageView pageView)
                    {
                        context.PageViews.Add(pageView);
                        await context.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist queued {Type} entry.", entry?.GetType().Name ?? "unknown");
                }
            }
        }
    }
}