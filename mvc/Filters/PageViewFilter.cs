using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using mvc.Models;
using mvc.Services;

namespace mvc.Filters
{
    public class PageViewFilter : IAsyncActionFilter
    {
        private readonly AuditWriteQueue _queue;
        private readonly ILogger<PageViewFilter> _logger;

        public PageViewFilter(AuditWriteQueue queue, ILogger<PageViewFilter> logger)
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
            if (!string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                PageView entry = new PageView
                {
                    TimestampUtc = DateTime.UtcNow,
                    UserId = http.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    UserName = http.User.Identity?.Name,
                    Controller = context.RouteData.Values["controller"]?.ToString() ?? string.Empty,
                    Action = context.RouteData.Values["action"]?.ToString() ?? string.Empty,
                    IpAddress = http.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = http.Request.Headers.UserAgent.ToString(),
                    CorrelationId = http.TraceIdentifier
                };

                _queue.TryWritePageView(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to queue page view for {Controller}/{Action}.", context.RouteData.Values["controller"], context.RouteData.Values["action"]);
            }
        }
    }
}