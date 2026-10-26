using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mvc.Models;
using mvc.Services;

namespace mvc.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class AnalyticsController : Controller
    {
        private readonly IAnalyticsService _analyticsService;

        public AnalyticsController(IAnalyticsService analyticsService)
        {
            _analyticsService = analyticsService;
        }

        public async Task<IActionResult> Index(int? days)
        {
            AnalyticsDashboardData data = await _analyticsService.GetDashboardAsync(days ?? 14);
            return View(data);
        }
    }
}