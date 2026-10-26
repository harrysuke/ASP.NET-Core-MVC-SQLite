# 02 — Architecture

## Solution layout

```
mvc.sln
├── mvc/            Main web application (hosted in IIS)
│   ├── Controllers/        Account, Analytics, AuditLog, Book, Dashboard, Home, Rest, User
│   ├── Data/               mvcContext (EF Core DbContext), DatabaseInitializer, SeedData
│   ├── Filters/            AuditActionFilter, PageViewFilter (global MVC filters)
│   ├── Models/             Book, AuditLog, view models (Login, Book, Analytics…)
│   ├── Services/           IBookRepository/BookRepository,
│   │                       IAnalyticsService/AnalyticsService,
│   │                       EmailSender, AuditRetentionService
│   ├── Views/              Razor views per controller + Shared layout
│   ├── wwwroot/            css/site.css, js/site.js, lib/ (Bootstrap, jQuery, Chart.js…)
│   ├── SQL/                books.sql — SQLite DDL + demo rows (reference copy)
│   ├── appsettings.json    ConnectionStrings, EmailSettings, Audit
│   ├── Program.cs          DI, Identity, auth policies, filters, pipeline
│   └── mvc.csproj          Package refs, publish profile (webroot)
└── mvctest/                Starter scaffold (default Home only) — not the active app
    └── wwwroot/…           Minimal default template
```

## Startup pipeline (`Program.cs`)

1. Read `ConnectionStrings:DefaultConnection` (throws if missing). The value is a SQLite
   connection string: `Data Source=booksDb.sqlite;Default Timeout=30`.
2. `AddDbContext<mvcContext>` with `UseSqlite`. SQLite is an embedded single-file
   database, so there is no `EnableRetryOnFailure` execution strategy (that is SQL Server
   specific); concurrency is handled by WAL mode plus the busy timeout.
3. Register scoped `IBookRepository`, transient `IEmailSender<IdentityUser>`,
   hosted `AuditRetentionService`, scoped `IAnalyticsService`.
4. `AddDefaultIdentity<IdentityUser>` (password policy: digit, ≥6 length,
   uppercase; no non-alphanumeric requirement) + `.AddRoles<IdentityRole>()`
   + EF stores + default token providers.
5. Authorization:
   - Fallback policy → `RequireAuthenticatedUser()` (login required everywhere
     unless `[AllowAnonymous]`).
   - `"MemberOnly"` → authenticated + `Admin` or `Member`.
   - `"AdminOnly"` → authenticated + `Admin`.
6. Cookie config: `LoginPath=/Account/Login`, `LogoutPath=/Account/Logout`.
7. Session (`IDistributedMemoryCache` + 30-min idle timeout, HttpOnly & essential cookie).
8. Global MVC filters: `AuditActionFilter`, `PageViewFilter`.
9. Startup database work inside a `CreateScope()`:
   - `DatabaseInitializer.InitializeAsync` — applies EF migrations (creating
     `booksDb.sqlite` on first run), enables WAL, seeds demo books if the table is empty.
   - `SeedData.InitializeAsync` — Admin/Member roles + optional bootstrap admin.
   Exceptions are logged as a warning and ignored so the site still boots.
10. Pipeline: `UseExceptionHandler` (non-dev), `UseHttpsRedirection`, `UseStaticFiles`,
    `UseRouting`, `UseSession`, `UseAuthentication`, `UseAuthorization`,
    `MapControllerRoute("{controller=Home}/{action=Index}/{id?}")`.

## Request lifecycle

```
Request → IIS
        → UseStaticFiles / UseRouting
        → UseSession (page visitorCount cookie read/write)
        → UseAuthentication (Identity cookie)
        → UseAuthorization (fallback policy + action attributes)
        → Global filters:
             PageViewFilter     — logs page views + bumps visitorCount
             AuditActionFilter  — logs controller/action params for writes
        → Controller action → service/repository → EF Core → booksDb.sqlite
        → Razor view
```

## Filter notes

- `AuditActionFilter` — global; records parameter-safe audit rows for POSTs (writes),
  keyed to the logged-in user, with timestamp + user IP/user agent if captured.
- `PageViewFilter` — global; logs each page view, updates `visitorCount` cookie and stores
  count on `HttpContext.Items` for the dashboard/analytics to read.
- `AuditRetentionService.StopAsync` deletes `AuditLogs` older than `Audit:RetentionDays`
  (best-effort, `AuditLogDbRepository`/context direct).

## Key dependencies (project `mvc`)

- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.Sqlite`
- `Microsoft.EntityFrameworkCore.Tools` (for `dotnet ef` migrations)
- `SQLitePCLRaw.bundle_e_sqlite3` (pinned above the version EF Core pulls in, to get the
  patched native SQLite build)
- `MailKit` (EmailSender SMTP) — verify in csproj

## REST API controller

`RestController` (`/api/rest`) — JSON API behind `[Authorize(Policy = "MemberOnly")]`:

| Method & route     | Action               | Notes |
| ------------------ | -------------------- | ----- |
| `GET /api/rest`    | `GetBooks`           | Full book list |
| `POST /api/rest`   | `AddBook(body BookViewModel)` | Sets `HttpContext.Items["AuditDetails"]` |
| `PUT /api/rest/{id}` | `UpdateBook`       | ditto |
| `DELETE /api/rest/{id}` | `DeleteBook`     | ditto |

Routes map to `BookRepository`; the POST body binds a `BookViewModel`, so `Id` in the body
is ignored and the SQLite `AUTOINCREMENT` key is assigned by the database.

## Audit log controller

`AuditLogController` is `[Authorize(Policy = "AdminOnly")]`:

- `GET /AuditLog/Index` — paged log with filters (user, action, date range, success).
  Paging is done by the database (`ORDER BY … LIMIT 25 OFFSET n`), which SQLite supports.
- `POST /AuditLog/ExportCsv` — `[ValidateAntiForgeryToken]`; same filters, returns
  `text/csv` download.