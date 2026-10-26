using Microsoft.EntityFrameworkCore;
using mvc.Data;
using mvc.Models;

namespace mvc.Services
{
    public interface IAnalyticsService
    {
        Task<AnalyticsDashboardData> GetDashboardAsync(int days);
    }

    public class AnalyticsService : IAnalyticsService
    {
        private readonly mvcContext _context;
        private readonly IBookRepository _bookRepository;

        public AnalyticsService(mvcContext context, IBookRepository bookRepository)
        {
            _context = context;
            _bookRepository = bookRepository;
        }

        public async Task<AnalyticsDashboardData> GetDashboardAsync(int days)
        {
            int normalizedDays = NormalizeDays(days);
            DateTime utcNow = DateTime.UtcNow;
            DateTime cutoff = utcNow.AddDays(-normalizedDays);
            bool allTime = normalizedDays == 0;

            // Aggregate in SQL so only summary rows cross the wire instead of entire tables.
            // Queries run one at a time because EF Core DbContext is not thread-safe;
            // they still aggregate in the database, so no full tables are fetched.

            int totalUsers = await _context.Users.CountAsync();
            int totalAudit = await _context.AuditLogs.AsNoTracking()
                .CountAsync(a => allTime || a.TimestampUtc >= cutoff);
            int totalPageViews = await _context.PageViews.AsNoTracking()
                .CountAsync(v => allTime || v.TimestampUtc >= cutoff);
            int successfulLogins = await _context.AuditLogs.AsNoTracking()
                .CountAsync(a => a.Controller == "Account" && a.Action == "Login" && a.Success && (allTime || a.TimestampUtc >= cutoff));
            int failedLogins = await _context.AuditLogs.AsNoTracking()
                .CountAsync(a => a.Controller == "Account" && a.Action == "Login" && !a.Success && (allTime || a.TimestampUtc >= cutoff));

            List<DailyPageView> dailyViews = await DailyPageViewsAsync(cutoff, allTime);
            List<DailyAuditCount> dailyLogins = await DailyAuditCountsAsync(cutoff, allTime, isLogin: true, success: true);
            List<DailyAuditCount> dailyFailedLogins = await DailyAuditCountsAsync(cutoff, allTime, isLogin: true, success: false);
            List<DailyAuditCount> dailyRegs = await DailyAuditCountsAsync(cutoff, allTime, isLogin: false, success: true);

            List<CountStat> topPages = await _context.PageViews.AsNoTracking()
                .Where(v => allTime || v.TimestampUtc >= cutoff)
                .GroupBy(v => v.Controller + "/" + v.Action)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new CountStat { Label = g.Key, Count = g.Count() })
                .ToListAsync();
            List<CountStat> failedLoginIps = await _context.AuditLogs.AsNoTracking()
                .Where(a => a.Controller == "Account" && a.Action == "Login" && !a.Success && a.IpAddress != null && (allTime || a.TimestampUtc >= cutoff))
                .GroupBy(a => a.IpAddress!)
                .OrderByDescending(g => g.Count())
                .Take(8)
                .Select(g => new CountStat { Label = g.Key, Count = g.Count() })
                .ToListAsync();
            List<CountStat> failedLoginEmails = await _context.AuditLogs.AsNoTracking()
                .Where(a => a.Controller == "Account" && a.Action == "Login" && !a.Success && a.UserName != null && (allTime || a.TimestampUtc >= cutoff))
                .GroupBy(a => a.UserName!)
                .OrderByDescending(g => g.Count())
                .Take(8)
                .Select(g => new CountStat { Label = g.Key, Count = g.Count() })
                .ToListAsync();
            List<CountStat> activeUsers = await _context.AuditLogs.AsNoTracking()
                .Where(a => a.UserName != null && (allTime || a.TimestampUtc >= cutoff))
                .GroupBy(a => a.UserName!)
                .OrderByDescending(g => g.Count())
                .Take(8)
                .Select(g => new CountStat { Label = g.Key, Count = g.Count() })
                .ToListAsync();
            List<AuditLog> recentActivity = await _context.AuditLogs.AsNoTracking()
                .Where(a => allTime || a.TimestampUtc >= cutoff)
                .OrderByDescending(a => a.TimestampUtc)
                .Take(12)
                .ToListAsync();

            List<BookViewModel> books = _bookRepository.GetAll();

            List<TrendPoint> trends = BuildTrends(
                dailyViews, dailyLogins, dailyFailedLogins, dailyRegs, cutoff, utcNow, allTime);

            return new AnalyticsDashboardData
            {
                Days = normalizedDays,
                Totals = BuildTotals(
                    totalUsers, totalAudit, totalPageViews, books, successfulLogins, failedLogins),
                Trends = trends,
                TopPages = topPages,
                BookAuthors = BuildAuthors(books),
                FailedLoginIps = failedLoginIps,
                FailedLoginEmails = failedLoginEmails,
                ActiveUsers = activeUsers,
                RecentActivity = recentActivity
            };
        }

        private Task<List<DailyPageView>> DailyPageViewsAsync(DateTime cutoff, bool allTime)
        {
            return _context.PageViews.AsNoTracking()
                .Where(v => allTime || v.TimestampUtc >= cutoff)
                .GroupBy(v => v.TimestampUtc.Date)
                .Select(g => new DailyPageView { Date = g.Key, Count = g.Count() })
                .ToListAsync();
        }

        private Task<List<DailyAuditCount>> DailyAuditCountsAsync(DateTime cutoff, bool allTime, bool isLogin, bool success)
        {
            IQueryable<AuditLog> query = _context.AuditLogs.AsNoTracking()
                .Where(a => allTime || a.TimestampUtc >= cutoff);

            if (isLogin)
            {
                query = query.Where(a => a.Controller == "Account" && a.Action == "Login" && a.Success == success);
            }
            else
            {
                query = query.Where(a => a.Controller == "Account" && a.Action == "Register" && a.Success);
            }

            return query
                .GroupBy(a => a.TimestampUtc.Date)
                .Select(g => new DailyAuditCount { Date = g.Key, Count = g.Count() })
                .ToListAsync();
        }

        private static int NormalizeDays(int days)
        {
            if (days <= 0)
            {
                return 0;
            }
            return days is >= 7 and <= 365 ? days : 14;
        }

        private static AnalyticsTotals BuildTotals(int totalUsers, int auditEvents, int pageViews, List<BookViewModel> books, int successfulLogins, int failedLogins)
        {
            int loginAttempts = successfulLogins + failedLogins;
            return new AnalyticsTotals
            {
                TotalUsers = totalUsers,
                TotalAuditEvents = auditEvents,
                TotalPageViews = pageViews,
                TotalBooks = books.Count,
                AvgPrice = books.Count > 0 ? Math.Round(books.Average(b => b.Price), 2) : 0,
                TotalValue = books.Sum(b => b.Price),
                SuccessfulLogins = successfulLogins,
                FailedLogins = failedLogins,
                LoginSuccessRate = loginAttempts > 0 ? Math.Round(successfulLogins * 100.0 / loginAttempts, 1) : 0
            };
        }

        private static List<TrendPoint> BuildTrends(List<DailyPageView> views, List<DailyAuditCount> logins,
            List<DailyAuditCount> failedLogins, List<DailyAuditCount> regs, DateTime cutoff, DateTime utcNow, bool allTime)
        {
            DateTime end = utcNow.Date;

            DateTime priorDataStart = end;
            foreach (DailyPageView v in views)
            {
                if (v.Date < priorDataStart) priorDataStart = v.Date;
            }
            foreach (DailyAuditCount v in logins)
            {
                if (v.Date < priorDataStart) priorDataStart = v.Date;
            }
            foreach (DailyAuditCount v in failedLogins)
            {
                if (v.Date < priorDataStart) priorDataStart = v.Date;
            }
            foreach (DailyAuditCount v in regs)
            {
                if (v.Date < priorDataStart) priorDataStart = v.Date;
            }

            DateTime start = priorDataStart;
            if (!allTime && start < cutoff.Date)
            {
                start = cutoff.Date;
            }
            if (allTime)
            {
                start = start.AddDays(-89);
            }
            if (start > end)
            {
                start = end;
            }

            Dictionary<DateTime, int> viewsByDay = views.ToDictionary(v => v.Date, v => v.Count);
            Dictionary<DateTime, int> loginsByDay = logins.ToDictionary(v => v.Date, v => v.Count);
            Dictionary<DateTime, int> failedByDay = failedLogins.ToDictionary(v => v.Date, v => v.Count);
            Dictionary<DateTime, int> regsByDay = regs.ToDictionary(v => v.Date, v => v.Count);

            List<TrendPoint> points = new List<TrendPoint>();
            for (DateTime day = start; day <= end; day = day.AddDays(1))
            {
                points.Add(new TrendPoint
                {
                    Date = day.ToString("yyyy-MM-dd"),
                    PageViews = CountAt(viewsByDay, day),
                    Logins = CountAt(loginsByDay, day),
                    FailedLogins = CountAt(failedByDay, day),
                    Registrations = CountAt(regsByDay, day)
                });
            }
            return points;
        }

        private static int CountAt(Dictionary<DateTime, int> counts, DateTime day)
        {
            return counts.TryGetValue(day, out int count) ? count : 0;
        }

        private static List<AuthorStat> BuildAuthors(List<BookViewModel> books)
        {
            return books
                .GroupBy(b => b.Author)
                .Select(g => new AuthorStat
                {
                    Author = string.IsNullOrWhiteSpace(g.Key) ? "Unknown" : g.Key,
                    BookCount = g.Count(),
                    AvgPrice = Math.Round(g.Average(b => b.Price), 2)
                })
                .OrderByDescending(a => a.BookCount)
                .Take(8)
                .ToList();
        }

        }

    public class DailyPageView
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }

    public class DailyAuditCount
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }
}