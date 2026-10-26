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
- **User management (Admin)** — `UserController` lists users with roles, create / edit /
  delete, and reset passwords.
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

---

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

---

# 03 — Database

## Engine & file

- Engine: **SQLite** (embedded, single file — no server, no service, no login).
- File: `booksDb.sqlite`, created in the app's working directory (the project folder when
  you run `dotnet run --project mvc`, the publish folder under IIS).
- Connection string (`appsettings.json`):

  ```
  Data Source=booksDb.sqlite;Default Timeout=30
  ```

  `Default Timeout` is the busy timeout in seconds: it makes a writer wait for a lock
  instead of failing immediately with "database is locked".
- The file, plus its `-wal` / `-shm` side files, is ignored by `.gitignore`.

## Connection-level pragmas

`Data/DatabaseInitializer` applies these once per process at startup:

| Pragma | Why |
| --- | --- |
| `journal_mode=WAL` | lets reads run while `AuditWriteService` commits audit / page-view rows |
| `synchronous=NORMAL` | safe with WAL and much cheaper than `FULL` for every write |

## Tables

| Table | Purpose | Managed by |
| --- | --- | --- |
| `books` (`id`, `title`, `author`, `price`) | Book catalog | `BookRepository` (EF Core) |
| `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens` | ASP.NET Identity tables | EF Core migrations |
| `AuditLogs` | Audit trail rows (see `Models/AuditLog`) | `AuditActionFilter` + `AuditRetentionService` |
| `PageViews` | Page view hits (see `Models/PageView`) | `PageViewFilter` |

`BookViewModel` is mapped in `mvcContext.OnModelCreating` to the lowercase `books` table so
the SQLite schema stays compatible with the original catalog layout. The `searchString`
property on the model is a UI-only field and is `Ignore`d.

## Migrations (EF Core, code-first)

| Migration | Adds |
| --- | --- |
| `20261026004959_InitialSqlite` | All tables: Identity, `books`, `AuditLogs`, `PageViews` |

Current model is captured in `mvcContextModelSnapshot.cs`. The migrations were regenerated
for SQLite — the old SQL Server ones (`nvarchar`/`IDENTITY`/`datetime2`) cannot be applied
to a SQLite file.

Migrations run automatically at startup (`DatabaseInitializer`), so a fresh clone only
needs `dotnet run`. To do it by hand:

```bash
cd mvc
dotnet tool restore                 # restores dotnet-ef 10.0.9 from .config/dotnet-tools.json
dotnet ef database update           # creates booksDb.sqlite and applies migrations
dotnet ef migrations add <Name>     # after changing the model
```

## Data access

SQLite has no stored procedures, so the original `BookViewAll` / `BookViewById` /
`BookAdd` / `BookEdit` / `BookAddOrEdit` / `BookDeleteById` / `BookSearchByTitle` procs are
gone. `BookRepository` keeps the same interface and issues plain EF Core queries:

| Repository method | Emitted SQL shape |
| --- | --- |
| `GetAll()` | `SELECT … FROM books ORDER BY id` (cached 5 min in `IMemoryCache`) |
| `GetById(id)` | `SELECT … FROM books WHERE id = @id LIMIT 1` |
| `Add(book)` | `INSERT INTO books (title, author, price) VALUES (…) RETURNING id` |
| `Update(id, book)` | `UPDATE books SET title=…, author=…, price=… WHERE id=@id` |
| `Delete(id)` | `DELETE FROM books WHERE id=@id` |
| `Search(term)` | `WHERE instr(lower(title), @t) > 0 OR instr(lower(author), @t) > 0` |

`Search` uses `lower()` + `instr()` rather than `LIKE` so the match is case-insensitive for
non-ASCII text and `%` / `_` in the search term are treated as literal characters.

## Additional files

- `SQL/books.sql` — standalone SQLite DDL for `books` plus the six demo rows. The app
  creates the same table through migrations; the script is a reference / manual-rebuild
  helper:

  ```bash
  sqlite3 booksDb.sqlite < mvc/SQL/books.sql
  ```

## Seeding at startup (`Data/DatabaseInitializer.cs` + `Data/SeedData.cs`)

Runs every startup inside `Program.cs`:

- `DatabaseInitializer`: applies migrations, sets WAL, then inserts six demo books **only
  when `books` is empty**.
- `SeedData`: creates roles if missing — **Admin**, **Member** (`SeedData.AdminRole`,
  `SeedData.MemberRole`).
- If `Admin:Email` + `Admin:Password` are configured (see `appsettings.json` /
  IIS env vars), creates that user, sets `EmailConfirmed = true`, assigns `Admin`.
- Failure paths are logged and non-fatal: if the file is locked or unreadable the site
  still starts.
- Existing users are never re-created; upgrades to role assignment are skipped if the
  user already exists.

## Backups

A SQLite database is a single file, so a backup is a file copy. Use the SQLite backup API
or copy the file while the app is stopped (with WAL, also copy `-wal` and `-shm`, or run
`VACUUM INTO 'backup.sqlite'` from `sqlite3` to get a consistent snapshot).

---

# 04 — Security & Identity

## Identity

- `AddDefaultIdentity<IdentityUser>` with roles (`IdentityRole`) and EF stores.
- Password policy: RequireDigit, RequiredLength ≥ 6, RequireNonAlphanumeric = **false**,
  RequireUppercase (set inline in `Program.cs`).
- Cookies via `ConfigureApplicationCookie`:
  - `LoginPath` = `/Account/Login`
  - `LogoutPath` = `/Account/Logout`
- Session cookie: 30-min idle timeout, `HttpOnly`, `IsEssential`.
- Identity email services are wired to `EmailSender` (`IEmailSender<IdentityUser>`)
  via SMTP settings in `EmailSettings` (enables confirmation/reset emails).

## Roles

Created in `SeedData` at startup and referenced by the authorization policies:

- `Admin` — full control (user management, audit log, analytics/secret areas).
- `Member` — normal logged-in members.

## Authorization

Defined in `Program.cs`:

| Policy | Requirement |
| --- | --- |
| Fallback (default) | `RequireAuthenticatedUser()` — applied to **all routes** unless `[AllowAnonymous]` |
| `MemberOnly` | authenticated + role `Admin` **or** `Member` |
| `AdminOnly` | authenticated + role `Admin` |

Usage highlights:

- Global fallback means anonymous users are redirected to `/Account/Login`.
- Controllers publicly allowed, e.g. `[AllowAnonymous]` on `Home/Index` and Account
  login/register/forgot pages.
- `UserController`, `AuditLogController`, `AnalyticsController` → `[Authorize(Policy =
  "AdminOnly")]` (admin only).
- `DashboardController` → `[Authorize(Policy = "MemberOnly")]`.
- `RestController` (`/api/rest`) → `[Authorize(Policy = "MemberOnly")]`.

## Global MVC filters (`Filters/`)

Both registered in `Program.cs` via
`options.Filters.Add<...>()`, so they run on every controller action:

### `AuditActionFilter`

- `IAsyncActionFilter` / `IAsyncResultFilter`.
- For **write** requests (POST/PUT/DELETE), writes an `AuditLogs` row after the action
  completes:
  - captures `Controller`, `Action`, `HttpMethod`, user (id + name or anonymous),
    `IpAddress`, `UserAgent`, `TimestampUtc`, `CorrelationId`.
  - `Success = true/false` based on whether the action threw / result status.
  - `Details` captures action-name + route data when no explicit `AuditDetails` item was
    set by the action (the REST controller sets
    `HttpContext.Items["AuditDetails"]` explicitly).
- Requires the `AuditLogs` table (migration `AddAuditLogs`).

### `PageViewFilter`

- Logs each GET page view as a `PageViews` row (Controller, Action, User, IP, UA,
  correlation id).
- Maintains a **`visitorCount` cookie**; increments per visitor and stores the count in
  `HttpContext.Items["VisitorCount"]` for analytics to read. This is the basis of the
  “visitors / unique visitors” number.

## Antiforgery / validation

- `[ValidateAntiForgeryToken]` on all state-changing controller actions
  (`ExportCsv`, Create/Edit/Delete, account actions).
- MVC `[ApiController]` on `RestController` automatically enforces model binding +
  `[FromBody]`.
- Views use `jquery-validation` + `jquery-validation-unobtrusive` client-side rules.

## Data protection / secrets

- Connection string, email SMTP credentials, and admin bootstrap credentials should not
  be committed to source (watch `appsettings.json` / IIS env vars). Use
  `.gitignore` for secrets; keep the local trusted-connection string in appsettings and
  override secrets with env overrides on the server.

---

# 05 — REST API & Reports

## REST API

`RestController` (route `api/rest`, attribute routing via
`[Route("api/[controller]")]`). `[ApiController]` = automatic 400 on invalid model.
Auth: `[Authorize(Policy = "MemberOnly")]`.

| Method | Route    | Body / Param        | Behavior                                              |
| ------ | -------- | ------------------- | ----------------------------------------------------- |
| GET    | `/api/rest` | — | `GetAll()` from repository, `Ok(books)` |
| POST   | `/api/rest` | `BookViewModel` JSON | `Add` then sets `HttpContext.Items["AuditDetails"]` |
| PUT    | `/api/rest/{id}` | `BookViewModel` JSON | `Update(id, book)` |
| DELETE | `/api/rest/{id}` | — | `Delete(id)` |

After each write the controller stores a human-readable `AuditDetails` string so the
`AuditActionFilter` can log "API: created book 'X'." etc.

## MVC routes (controllers)

| Controller | Route                  | Access     | Notes |
| --- | --- | --- | --- |
| `HomeController` | `/` , `/Home/{action}` | Anonymous | Landing page; `/Home/Api` shows API usage |
| `AccountController` | `/Account/{action}` | Anonymous for Login/Register/ForgotPassword; auth for Profile | default identity pages + Profile |
| `BookController` | `/Book/{action}` | Anonymous (or MemberOnly) | list/details/create/edit/delete + Search |
| `DashboardController` | `/Dashboard` | `MemberOnly` | KPI overview |
| `UserController` | `/User` | AdminOnly | user CRUD + ResetPassword |
| `AuditLogController` | `/AuditLog` | AdminOnly | paged log + CSV export |
| `AnalyticsController` | `/Analytics` | AdminOnly | daily traffic & login charts (Chart.js) |

> The exact access level per controller should be verified against attributes in each
> controller file (home page used to be anonymous; e.g. `HomeController` has
> `[AllowAnonymous]`).

### Analytics / reports

`AnalyticsController.Index` renders `Views/Analytics/Index.cshtml`:

- Computes per-day aggregates using `AnalyticsService` (counts page views, logins
  success/failure, registrations, book stats) grouped by UTC day.
- Two Chart.js canvases (`trafficChart` line, `loginChart` bar) painted client-side,
  re-rendering on `themechange` events (see `js/site.js`).
- No third-party analytics (no Google Analytics tag) — data comes straight from the local
  SQLite tables (`AuditLogs`, `PageViews`, `books`).

### Audit log export

`AuditLogController.ExportCsv` (`POST`, antiforgery) returns `text/csv` with the same
filters as the Index view. Used by the AuditLog page "Export CSV" button.

---

# 06 — Deployment (IIS / Windows Server)

## Publish method

The app is published to a **webroot folder** served by IIS (hosted. Use either the
`mvc.WebDeploy` / web deploy profile in Visual Studio or CLI, both target a folder that
IIS maps at `http://<host>/`).

Current deployed location: `C:\inetpub\wwwroot\mvc`

## Publish command (CI / CLI equivalent)

```
dotnet publish mvc/mvc.csproj -c Release -o C:\inetpub\wwwroot\mvc
```

Then create/recycle the IIS site (or application pool restart) so Kestrel starts. The
published folder contains `web.config` (aspNetCore handler) plus the app binaries.

## Required environment overrides on the server

`appsettings.json` already contains a working SQLite connection string
(`Data Source=booksDb.sqlite`), so a plain publish runs without any override. Override
these only when the defaults don't fit (either in web.config `<environmentVariables>` or
machine/user env vars):

| Env var | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Absolute SQLite path when the working directory is not writable, e.g. `Data Source=C:\ProgramData\mvc\booksDb.sqlite;Default Timeout=30` |
| `EmailSettings__Host` / `Port` / `UseSsl` / `Username` / `Password` / `FromEmail` / `FromName` | SMTP for Identity emails |
| `Admin__Email` / `Admin__Password` | Bootstrap admin account created by `SeedData` |
| `Audit__RetentionDays` | Audit log retention (0 = disabled) — see AuditRetentionService |

## Database file permissions

There is no database login any more, but the app pool identity still needs **modify**
rights on the folder that holds the file (the app creates `booksDb.sqlite` and its `-wal` /
`-shm` companions on first run):

```
icacls "C:\inetpub\wwwroot\mvc" /grant "IIS AppPool\mvc":(OI)(CI)M
```

For a site that should not be able to overwrite its own binaries, point the connection
string at a data folder instead, e.g. `C:\ProgramData\mvc\booksDb.sqlite`, and grant the
app pool rights on that folder only.

Double colon syntax (`ConnectionStrings__DefaultConnection`) works with the config
binder and maps to `ConnectionStrings:DefaultConnection`.

## Hosting model

IIS → ASP.NET Core hosting module (`web.config` `aspNetCoreModule`) → Kestrel
(`mvc.dll`). Ensure:

- App pool .NET CLR version = **No Managed Code**.
- IIS site binds at `C:\inetpub\wwwroot\mvc`.
- The app pool user has **modify** access to the folder holding `booksDb.sqlite` (the file
  is created on first start, so a read-only folder breaks startup seeding).

## HTTPS

`app.UseHttpsRedirection()` is on. Terminate TLS at the IIS binding; Kestrel behind the
reverse proxy handles the redirect (the app checks `HTTPS` env / forwarded headers as
configured).

## Startup seeding behavior

On first start the app creates `booksDb.sqlite`, applies the migrations, seeds demo books
(only when `books` is empty), then seeds Admin/Member roles + an admin from `Admin__Email` /
`Admin__Password`. If either credential is unset or the file cannot be opened, `Program.cs`
catches the exception, logs a warning, and still serves the site.

## Deploy checklist

1. Publish release build into `wwwroot\mvc`.
2. Grant the app pool **modify** on the folder holding `booksDb.sqlite` (or point
   `ConnectionStrings__DefaultConnection` at a writable data folder).
3. Set env overrides as needed (admin bootstrap, email settings, retention).
4. First start creates and migrates the SQLite file — no separate DB setup step.
5. Recycle the app pool, browse site, log in as admin.
6. Back up by copying `booksDb.sqlite` (stop the site first, or use `VACUUM INTO`).

---

# 07 — Frontend & Theming

## Layout & assets

- `Views/Shared/_Layout.cshtml` — Bootstrap 5 navbar + themed shell. Inline script in
  `<head>` (before CSS paint) sets `data-bs-theme` from `localStorage` or system
  preference, avoiding a flash of the wrong mode. `meta[name="theme-color"]` carries
  `data-theme-color-light/dark` so the browser tab bar also switches.
- Toggle buttons (`data-theme-toggle`) appear in the navbar (desktop + mobile).
- `wwwroot/css/site.css` — Bootstrap 5 CSS variables overridden for dark mode:
  `[data-bs-theme="dark"]` (background/text/accent overrides).
- `wwwroot/js/site.js` — theme runtime:

  - `STORAGE_KEY = 'theme'`, persisted in `localStorage`.
  - Applies `data-bs-theme` on `documentElement`, writes localStorage, syncs
    `aria-pressed`/`aria-label`/`title` on every toggle for accessibility, updates
    `theme-color` meta, dispatches a `themechange` CustomEvent with `{ theme }`.
  - Uses one delegated click handler on `[data-theme-toggle]` (works for dynamically
    re-rendered toggles).
  - A `MutationObserver` re-syncs toggle state if the attribute changes from any source.
  - **Debug hotkey: `Ctrl+Shift+T`** toggles theme and dumps current state to the
    browser console (stored value, computed CSS vars, toggle aria state) — handy for
    verifying the theme pipeline.

## Analytics charts (Chart.js)

`Views/Analytics/Index.cshtml`:

- Loads `~/lib/chart.js/dist/chart.umd.min.js`.
- Renders `trafficChart` (line: page views, logins, registrations per day) and
  `loginChart` (bar: successful vs failed logins).
- Chart colors adapt to light/dark via `themeColors()` reading CSS custom properties;
  charts re-render whenever the page receives a `themechange` event so they always match
  the active theme.

## Client libraries (`wwwroot/lib`)

Bootstrap 5, jQuery (+ validation, unobtrusive), Chart.js, and icons (Bootstrap Icons)
shipped as static libs (no CDN).

## Accessibility notes

- Toggle buttons set `aria-pressed`, `aria-label`, `title` so screen readers can
  announce state and action.
- Chart canvases carry `role="img"` + descriptive `aria-label`.
- `theme-color` meta updated per theme for mobile browsers.

---

# 08 — Troubleshooting

Common issues hit while building/deploying and their fixes.

## 1. `.NET Core` handler failure (HTTP 500.30 startup) on IIS

**Symptom** — `HTTP 500.30` / ASP.NET Core Module startup failure after publish.

**Fixes applied:**
- Use **Framework-dependent** publish so the server's installed .NET runtime serves it
  (no `win-x64` self-contained mismatch).
- Ensure the app pool is **No Managed Code**.
- Check `Windows Event Viewer → Application` for the crashing startup exception (often
  `InvalidOperationException: Connection string 'DefaultConnection' not found`).

## 2. Startup exception: connection string not found

**Symptom:** app boots, then on first request:
`InvalidOperationException: Connection string 'DefaultConnection' not found.`

**Cause:** published `appsettings.json` didn't ship or `DefaultConnection` removed.

**Fix:** set `ConnectionStrings__DefaultConnection` via IIS env overrides (see
deployment doc). Remember the double-underscore naming resolves to
`ConnectionStrings:DefaultConnection`.

## 3. `database is locked` / SQLiteException on writes

**Symptom:** `SqliteException: database is locked` during a book save or while the audit
writer flushes.

**Cause:** SQLite allows one writer at a time. The audit/page-view writer commits off the
request path, so a long-running write can hold the lock.

**Fixes applied:**
- `journal_mode=WAL` at startup (readers no longer block the writer).
- `Default Timeout=30` in the connection string (busy timeout instead of instant failure).
- Audit rows are queued in `AuditWriteQueue` and written one at a time, so a burst of
  requests doesn't open many concurrent writers.

If it still happens, check for a second process (e.g. a DB browser) holding the file.

## 4. Porting notes: stored procedures are gone

**Cause:** SQLite has no stored procedures, no `nvarchar`, no `IDENTITY`, no `GO`.

**What changed:**
- Book CRUD moved into `BookRepository` as EF Core queries.
- Migrations were regenerated for SQLite (`InitialSqlite`); the SQL Server migrations
  (`nvarchar` / `IDENTITY` / `datetime2`) cannot be applied to a SQLite file.
- `AuditLog` paging now uses `LIMIT/OFFSET` in the database instead of paging in memory.

## 5. Password policy surprises

**Symptom:** strong test password like `Password123!` fails; or simple passwords fail.

**Explanation:** policy = digit + length ≥ 6 + uppercase, **non-alphanumeric NOT
required**. Nothing to fix — that's intended for this app.

## 6. Book search returns empty

**Symptom:** searching for part of a title or author returns nothing.

**Cause:** the match is `instr(lower(title), @term) > 0 OR instr(lower(author), @term) > 0`
— a substring match on the whole term, not a multi-word search. `%` and `_` are literal
characters here (they are *not* LIKE wildcards any more).

**Fix:** search for a contiguous substring ("clean", "martin"). For multi-word or fuzzy
search, extend `BookRepository.Search`.

## 7. First deploy shows no books / startup seeding warning

1. Confirm the working directory (or `ConnectionStrings__DefaultConnection`) points at a
   folder the app pool can write to — the file is created on first start.
2. Check the log line `SQLite database ready at '<path>'` to see which file was opened.
3. If `books` is empty the initializer inserts the six demo rows; if you deleted them,
   re-apply `mvc/SQL/books.sql` or add books through the UI.
4. A `Startup database initialization failed` warning means the file could not be opened —
   fix permissions rather than hunting for a server.

## 8. Theme toggle appears not to stick

- Check `localStorage` in devtools: key `theme`.
- Toggle uses `data-bs-theme` attribute; the MutationObserver re-syncs only the toggle
  buttons' accessibility state, **not** the theme, so an external theme change may not
  re-paint charts unless `themechange` fires (it does on every `apply()`).
- Use **Ctrl+Shift+T** to dump the full state to console and manually verify CSS vars.

## 9. Email not sending (registration confirmation, forgot password)

**Cause:** `EmailSettings.*` unset in `appsettings.json` (empty host/user/pass) and no
IIS env override.

**Fix:** set `EmailSettings__*` env vars to working SMTP (e.g. Host=
`smtp.example.com`, Port 587, UseSsl, Username, Password, FromEmail, FromName). Until
then Identity flows that require email delivery will fail/incomplete.

## 10. Smart App Control / WDAC blocks the SQLite assemblies

**Symptom:** the site starts but every DB call fails with

```
System.IO.FileLoadException: Could not load file or assembly
'…\SQLitePCLRaw.provider.e_sqlite3.dll'. An Application Control policy has blocked this file. (0x800711C7)
```

and the startup log shows `Startup database initialization failed`.

**Cause:** the SQLite interop assemblies (`SQLitePCLRaw.core`, `.batteries_v2`,
`.provider.e_sqlite3`) are **unsigned**. Windows Smart App Control (or a WDAC policy with
an "enterprise signing" requirement) only lets signed binaries load, so the app cannot
open any SQLite file. The matching event log entry is
`Microsoft-Windows-CodeIntegrity/Operational` event **3077**.

**Fixes:**
- Deploy to a server without Smart App Control (the normal case for IIS), or
- Turn Smart App Control off (Windows Security → App & browser control → Smart App
  Control). Note it cannot be re-enabled without resetting Windows, or
- On a managed fleet, add a WDAC supplemental policy that trusts the NuGet-restored
  assemblies.

Nothing in the application code can work around this — the block happens before any
managed code in the package runs.

## 11. Database file backup / exposure

`booksDb.sqlite` holds the Identity password hashes and the audit trail, so treat it like
a credential store: keep it out of source control (already in `.gitignore`), don't put it
under `wwwroot`, and copy it (or `VACUUM INTO`) for backups rather than shipping it around.

---

# 09 — Docker & CI/CD (Docker Hub)

## Why there is no database container

The app stores everything in a single SQLite file, so there is no `db` service to run.
The file lives on a volume at `/app/data`, which means the container stays stateless:
destroy and recreate it freely, the catalog, users and audit log survive.

```
┌─────────────────────────┐        ┌──────────────────────┐
│  web (mvc.dll, :8080)   │───────▶│  volume: books-data  │
│  ASP.NET Core, non-root │        │  /app/data/booksDb…  │
└─────────────────────────┘        └──────────────────────┘
```

## Files

| File | Purpose |
| --- | --- |
| `Dockerfile` | Two-stage build: `sdk:10.0` compiles, `aspnet:10.0` runs |
| `.dockerignore` | Keeps `bin/`, `obj/`, docs and any local `.sqlite` file out of the context |
| `docker-compose.yml` | Single-host deployment: port, volume, env vars, healthcheck |
| `.env.example` | Template for the compose variables (`cp .env.example .env`) |
| `.github/workflows/docker-publish.yml` | Build → smoke test → push to Docker Hub |

## Run it locally

```bash
cp .env.example .env          # set DOCKERHUB_USERNAME / ADMIN_EMAIL / ADMIN_PASSWORD
docker compose up --build -d
docker compose logs -f web
```

Then open <http://localhost:8080> and log in with the admin account from `.env`
(it is created on first start by `SeedData`).

```bash
docker compose ps             # STATUS should become "healthy"
docker compose down           # keeps the volume
docker compose down -v        # deletes the database too
```

Or without compose:

```bash
docker build -t books:local .
docker run --rm -p 8080:8080 \
  -v books-data:/app/data \
  -e Admin__Email=admin@example.com \
  -e Admin__Password=ChangeMe123 \
  books:local
```

## Image details

- **Build stage** restores `mvc/mvc.csproj` on its own layer before copying the source, so
  a code change does not re-download NuGet packages. It publishes with
  `--configuration Release /p:UseAppHost=false` — the container runs `dotnet mvc.dll`.
- **Runtime stage** is `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian slim). `curl` is
  installed only for the `HEALTHCHECK`.
- **Non-root**: the app runs as the image's built-in `app` user (`$APP_UID`, 1654).
  `/app/data` is created and `chown`ed in the image so a fresh named volume inherits the
  ownership.
- **Port 8080** (`ASPNETCORE_HTTP_PORTS=8080`). Plain HTTP inside the container — TLS is
  expected to terminate at your reverse proxy.
- **Healthcheck** probes `GET /`, which is `[AllowAnonymous]`, every 30 s after a 25 s
  start period.
- **`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`** is set in compose so client IPs and
  scheme are taken from `X-Forwarded-*` when running behind nginx / Traefik / IIS ARR.

Publish output is currently ~130 MB because the project still references build-time
packages it does not use at runtime (`Microsoft.Build`, `NuGet.*`, `Microsoft.CodeAnalysis`,
`Azure.Identity`). Dropping those from `mvc.csproj` would shrink both the publish output and
the image considerably — worth doing if image size matters.

## Configuration

Everything is standard ASP.NET Core configuration, so `Section__Key` environment variables
override `appsettings.json`.

| Variable | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | `Data Source=/app/data/booksDb.sqlite;Default Timeout=30` | Point elsewhere to move the database |
| `ASPNETCORE_ENVIRONMENT` | `Production` | |
| `Admin__Email` / `Admin__Password` | unset | Bootstrap admin; created once, then ignored |
| `Audit__RetentionDays` | `180` | `0` disables the purge |
| `EmailSettings__*` | unset | SMTP for Identity mail; set the whole block or none of it |

> Setting `EmailSettings__Host` without `EmailSettings__Port` makes the options binder fail
> on `int`, so keep the SMTP block complete.

## Backups

```bash
# consistent snapshot without stopping the app (SQLite VACUUM INTO)
docker run --rm -v books-data:/data -v "$PWD":/backup alpine \
  sh -c "apk add --no-cache sqlite && sqlite3 /data/booksDb.sqlite \"VACUUM INTO '/backup/booksDb-backup.sqlite'\""
```

Or stop the app and copy the file itself (`booksDb.sqlite` plus `-wal` / `-shm`) out of the
volume with `docker compose cp`.

## CI/CD — GitHub Actions

`.github/workflows/docker-publish.yml` assumes **this folder is the repository root**, so
`.github/workflows/` sits next to `mvc/`.

### One-time setup

1. Create the Docker Hub repository `aspnet-core-mvc-sqlite` (or change `IMAGE_NAME` in the
   workflow).
2. Add two repository secrets (Settings → Secrets and variables → Actions):

   | Secret | Value |
   | --- | --- |
   | `DOCKERHUB_USERNAME` | Docker Hub account name |
   | `DOCKERHUB_TOKEN` | Access token from Docker Hub → Account settings → Security |

   Use a token, not your password — tokens can be scoped and revoked.

### Pipeline

| Job | What it does |
| --- | --- |
| `verify` | `dotnet restore` → `dotnet build -c Release` → `dotnet publish` (the exact command the Dockerfile runs) and asserts `mvc.dll` + `appsettings.json` are in the output |
| `docker` | Builds the image into the local store, **runs it**, polls `http://localhost:8080/` for up to 60 s, checks `/app/data/booksDb.sqlite` was created on the volume, dumps logs, then builds and pushes |

The smoke test runs before any push, so a broken image never reaches Docker Hub. Both
builds share the GitHub Actions layer cache (`type=gha`), so the second one is fast.

### Triggers and tags

| Event | Result |
| --- | --- |
| Push to `main` / `master` | pushes `latest`, `<branch>`, `sha-<short>` |
| Tag `v1.4.0` | pushes `1.4.0`, `1.4`, `sha-<short>` |
| Pull request | builds + smoke tests only, **no push** (fork PRs have no secrets) |
| `workflow_dispatch` | manual run from the Actions tab |
| Docs-only push (`docs/**`, `*.md`) | skipped by `paths-ignore` |

`docker/metadata-action` generates the tag list and OCI labels (source, revision, version);
`concurrency` cancels superseded runs on the same ref.

### Multi-arch

The workflow builds `linux/amd64` only — arm64 would run the whole .NET build under QEMU
and roughly triple the runtime. To enable it:

```yaml
      - name: Set up QEMU
        uses: docker/setup-qemu-action@v3
      # ...
          platforms: linux/amd64,linux/arm64
```

The publish step is RID-agnostic, so the same image content works on both architectures
(the `runtimes/` folder carries `libe_sqlite3.so` per RID).

## Known caveats

- **Cookie key ring is not persisted.** ASP.NET Core Data Protection stores its keys under
  `$HOME/.aspnet/DataProtection-Keys` inside the container, so recreating the container
  logs everybody out. To keep sessions across redeploys you need two changes:
  1. In the `Dockerfile`, create the folder next to `/app/data`:
     `mkdir -p /home/app/.aspnet/DataProtection-Keys` and `chown` it to `$APP_UID` (an
     empty named volume mounted over a root-owned path is not writable by the app user).
  2. In `docker-compose.yml`, add the volume:

     ```yaml
     services:
       web:
         volumes:
           - books-data:/app/data
           - dp-keys:/home/app/.aspnet/DataProtection-Keys

     volumes:
       books-data:
       dp-keys:
     ```
- **PDF export may not work on Linux.** `/Book/PdfReport` uses iTextSharp 5.5.13.5, which
  only ships a `net461` build (no .NET Standard target). Excel export (`ClosedXML`) and the
  rest of the app are fine.
- **TLS terminates outside the container.** `UseHttpsRedirection()` has no HTTPS port to
  redirect to, so it logs a warning and passes requests through — that is expected. Publish
  the container behind a proxy that handles TLS.
- **Image size** — see the note above about unused build-time package references.

## Windows-specific caveat

SQLite needs the native `SQLitePCLRaw` assemblies, which are unsigned. On a Windows host
with **Smart App Control / WDAC** enforced, `dotnet run` fails with
`An Application Control policy has blocked this file (0x800711C7)` — see
[troubleshooting item 10](08-troubleshooting.md#10-smart-app-control--wdac-blocks-the-sqlite-assemblies).

Containers are **not** affected: a Linux container runs inside the WSL2 / Hyper-V VM, where
the Windows user-mode code-integrity policy does not apply. Docker is therefore a practical
way to run this app on such a machine.
