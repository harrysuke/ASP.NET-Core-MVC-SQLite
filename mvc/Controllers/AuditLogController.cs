using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using mvc.Data;
using mvc.Models;

namespace mvc.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class AuditLogController : Controller
    {
        private const int PageSize = 25;
        private readonly mvcContext _context;

        public AuditLogController(mvcContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? user, string? actionName, DateTime? from, DateTime? to, bool? success, int? page)
        {
            IQueryable<AuditLog> query = ApplyFilters(user, actionName, from, to, success);
            int total = await query.CountAsync();
            int rawPage = page ?? 1;
            int maxPage = Math.Max(0, (total - 1) / PageSize);
            int currentPage = Math.Clamp(rawPage, 1, maxPage + 1);
            int currentPageZero = currentPage - 1;

            // SQLite supports LIMIT/OFFSET, so the page is sliced by the database
            // instead of loading the whole filtered log into memory.
            List<AuditLog> entries = await query
                .OrderByDescending(a => a.TimestampUtc)
                .Skip(currentPageZero * PageSize)
                .Take(PageSize)
                .ToListAsync();

            ViewData["SearchUser"] = user;
            ViewData["SearchAction"] = actionName;
            ViewData["SearchFrom"] = from;
            ViewData["SearchTo"] = to;
            ViewData["SearchSuccess"] = success;
            ViewData["CurrentPage"] = currentPage;
            ViewData["TotalPages"] = maxPage + 1;
            ViewData["Total"] = total;

            return View(entries);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportCsv(string? user, string? actionName, DateTime? from, DateTime? to, bool? success)
        {
            List<AuditLog> entries = await ApplyFilters(user, actionName, from, to, success)
                .OrderByDescending(a => a.TimestampUtc)
                .ToListAsync();

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("TimestampUtc,UserEmail,Controller,Action,HttpMethod,Success,IpAddress,Details,CorrelationId");
            foreach (AuditLog entry in entries)
            {
                csv.AppendLine(string.Join(",",
                    Quote(entry.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss")),
                    Quote(entry.UserName),
                    Quote(entry.Controller),
                    Quote(entry.Action),
                    Quote(entry.HttpMethod),
                    entry.Success ? "1" : "0",
                    Quote(entry.IpAddress),
                    Quote(entry.Details),
                    Quote(entry.CorrelationId)));
            }

            byte[] bytes = Encoding.UTF8.GetBytes(csv.ToString());
            string filename = $"auditlog_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            return File(bytes, "text/csv", filename);
        }

        private IQueryable<AuditLog> ApplyFilters(string? user, string? actionName, DateTime? from, DateTime? to, bool? success)
        {
            IQueryable<AuditLog> query = _context.AuditLogs.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(user))
            {
                string searchUser = user.Trim();
                query = query.Where(a => a.UserName != null && a.UserName.Contains(searchUser));
            }
            if (!string.IsNullOrWhiteSpace(actionName))
            {
                string searchAction = actionName.Trim();
                query = query.Where(a => a.Action.Contains(searchAction));
            }
            if (from.HasValue)
            {
                DateTime fromUtc = FromDateTime(from.Value);
                query = query.Where(a => a.TimestampUtc >= fromUtc);
            }
            if (to.HasValue)
            {
                DateTime toUtc = NextDayMidnightUtc(to.Value);
                query = query.Where(a => a.TimestampUtc < toUtc);
            }
            if (success.HasValue)
            {
                query = query.Where(a => a.Success == success.Value);
            }

            return query;
        }

        private static DateTime FromDateTime(DateTime local)
        {
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        }

        private static DateTime NextDayMidnightUtc(DateTime local)
        {
            DateTime nextDayLocal = DateTime.SpecifyKind(local, DateTimeKind.Unspecified).Date.AddDays(1);
            return TimeZoneInfo.ConvertTimeToUtc(nextDayLocal, TimeZoneInfo.Local);
        }

        private static string Quote(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}