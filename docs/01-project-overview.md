# 01 — Project Overview

ASP.NET Core MVC application for managing a book catalog, with authentication,
audit logging, analytics, and role-based authorization.

## Features

- **Book CRUD** against the `books` table in a local SQLite file, through
  `BookRepository` (EF Core). There are no stored procedures — SQLite does not have them.
- **Search** — `BookController.Search` calls `BookRepository.Search`, a case-insensitive
  `lower()/instr()` match on title and author.
- **Identity & Accounts** — register, login, logout, password reset, account profile
  (`AccountController`). Extended to allow resetting arbitrary users' passwords via
  `User/ResetPassword`.
- **User management (Admin)** — `UserController` (AdminOnly) lists users with their
  access level, searchable/filterable, create with a chosen role, edit, reset passwords,
  grant/revoke `Admin`/`Member`, lock/unlock, delete. Guard rails block self-demotion,
  self-deletion, self-lock and removing the last admin; non-admins get
  `/Account/AccessDenied`.
- **Role-based authorization** —
  - Fallback policy: authenticated required on everything.
  - `MemberOnly` = `Admin` or `Member`.
  - `AdminOnly` = `Admin` only.
- **Audit log** — `mvc.Filters.AuditActionFilter` records writes (add/edit/delete) per
  user with IP, DTO snapshot; `mvc.Filters.PageViewFilter` logs page visits plus a
  `visitorCount` via a session cookie. Both are registered as **global MVC filters** in
  `Program.cs`.
- **Audit retention** — `mvc.Services.AuditRetentionService` (hosted service) deletes
  audit rows older than `Audit:RetentionDays` (0 = disabled).
- **Analytics / KPI** — `AnalyticsController` + `AnalyticsService` produce daily traffic
  (page views, logins, registrations), login success/failure, and book stats, rendered
  with Chart.js. No Google Analytics scripts — analytics are computed from local DB data.
- **Dashboard** — `DashboardController` aggregates KPIs.
- **REST API** — `RestController` exposes JSON endpoints for books, users, audit logs,
  roles, and analytics.
- **Email** — `EmailSender` implements `IEmailSender<IdentityUser>` over SMTP using the
  `EmailSettings` section (used for confirmation / password reset).
- **Dark/Light theme toggle** — persisted in `localStorage`, no-flash inline script.

## Tech stack

- .NET 10 (SDK `10.0.402` installed locally)
- ASP.NET Core MVC + Razor
- ASP.NET Core Identity (`IdentityUser`/`IdentityRole`), cookies
- EF Core with the **SQLite** provider (`Microsoft.EntityFrameworkCore.Sqlite`)
- SQLite — a single embedded file (`booksDb.sqlite`), no server to install
- Bootstrap 5 + Chart.js from `wwwroot/lib`
- Hosted on IIS (Windows Server) via webroot publish, env override `ASPNETCORE_IIS_...`

## High-level flow

```
Browser ──► IIS (webroot wwwroot\mvc) ──► Kestrel (mvc.dll)
                │
                ▼
         Controllers ──► Services / Repositories ──► booksDb.sqlite (EF Core)
                │
                ▼
         Razor Views (Shared\_Layout, Bootstrap 5, site.css/site.js)
```

At startup `Program.cs` runs `DatabaseInitializer` (creates/migrates `booksDb.sqlite`,
switches it to WAL, seeds demo books when the table is empty) and then `SeedData`
(Admin/Member roles + default admin). Both are best-effort: failures are logged as a
warning and the site still starts.