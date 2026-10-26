namespace mvc.Models
{
    public class AnalyticsDashboardData
    {
        public int Days { get; set; }
        public AnalyticsTotals Totals { get; set; } = new();
        public List<TrendPoint> Trends { get; set; } = new();
        public List<CountStat> TopPages { get; set; } = new();
        public List<AuthorStat> BookAuthors { get; set; } = new();
        public List<CountStat> FailedLoginIps { get; set; } = new();
        public List<CountStat> FailedLoginEmails { get; set; } = new();
        public List<CountStat> ActiveUsers { get; set; } = new();
        public List<AuditLog> RecentActivity { get; set; } = new();
    }

    public class AnalyticsTotals
    {
        public int TotalUsers { get; set; }
        public int TotalAuditEvents { get; set; }
        public int TotalPageViews { get; set; }
        public int TotalBooks { get; set; }
        public double AvgPrice { get; set; }
        public int TotalValue { get; set; }
        public int SuccessfulLogins { get; set; }
        public int FailedLogins { get; set; }
        public double LoginSuccessRate { get; set; }
    }

    public class TrendPoint
    {
        public string Date { get; set; } = string.Empty;
        public int PageViews { get; set; }
        public int Logins { get; set; }
        public int FailedLogins { get; set; }
        public int Registrations { get; set; }
    }

    public class CountStat
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class AuthorStat
    {
        public string Author { get; set; } = string.Empty;
        public int BookCount { get; set; }
        public double AvgPrice { get; set; }
    }
}