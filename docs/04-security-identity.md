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