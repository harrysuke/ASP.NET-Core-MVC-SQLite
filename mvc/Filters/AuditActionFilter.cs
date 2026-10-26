using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Filters;
using mvc.Models;
using mvc.Services;

namespace mvc.Filters
{
    public class AuditActionFilter : IAsyncActionFilter
    {
        private readonly AuditWriteQueue _queue;
        private readonly ILogger<AuditActionFilter> _logger;

        readonly HashSet<string> _mutations = new(StringComparer.OrdinalIgnoreCase)
        {
            "POST", "PUT", "PATCH", "DELETE"
        };

        public AuditActionFilter(AuditWriteQueue queue, ILogger<AuditActionFilter> logger)
        {
            _queue = queue;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            ActionExecutedContext executed = await next();
            if (executed.Exception != null && !executed.ExceptionHandled)
            {
                return;
            }

            HttpContext http = context.HttpContext;
            string method = http.Request.Method;
            if (!_mutations.Contains(method))
            {
                return;
            }

            // Ignore the audit log's own CSV export action.
            string? controller = context.RouteData.Values["controller"]?.ToString();
            if (string.Equals(controller, "AuditLog", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                AuditLog entry = new AuditLog
                {
                    TimestampUtc = DateTime.UtcNow,
                    UserId = http.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    UserName = http.User.Identity?.Name,
                    Controller = controller ?? string.Empty,
                    Action = context.RouteData.Values["action"]?.ToString() ?? string.Empty,
                    HttpMethod = method,
                    RouteValues = SerializeRouteValues(context),
                    IpAddress = http.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = http.Request.Headers.UserAgent.ToString(),
                    Success = http.Items.TryGetValue("AuditSuccess", out object? auditSuccess) && auditSuccess is bool successFlag
                        ? successFlag
                        : executed.Exception == null,
                    Details = http.Items.TryGetValue("AuditDetails", out object? details) ? details?.ToString() : null,
                    CorrelationId = http.TraceIdentifier
                };

                _queue.TryWriteAudit(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to queue audit log for {Controller}/{Action}.", controller, context.RouteData.Values["action"]);
            }
        }

        private static string? SerializeRouteValues(ActionExecutingContext context)
        {
            // Log only route parameters (e.g. {id}), never query strings or bodies.
            var routeValues = context.RouteData.Values
                .Where(kvp => kvp.Key is not "controller" and not "action" and not "page")
                .ToDictionary(k => k.Key, v => v.Value?.ToString());
            if (routeValues.Count == 0)
            {
                return null;
            }
            return JsonSerializer.Serialize(routeValues);
        }
    }
}