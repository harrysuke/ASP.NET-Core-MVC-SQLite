# 04 — Security & Identity

## Identity

- `AddDefaultIdentity<IdentityUser>` with roles (`IdentityRole`) and EF stores.
- Password policy: RequireDigit, RequiredLength ≥ 6, RequireNonAlphanumeric = **false**,
  RequireUppercase (set inline in `Program.cs`).
- Cookies via `ConfigureApplicationCookie`:
  - `LoginPath` = `/Account/Login` — anonymous user hits a protected page.
  - `LogoutPath` = `/Account/Logout`
  - `AccessDeniedPath` = `/Account/AccessDenied` — signed in, but the wrong role
    (e.g. a `Member` opening `/User`).
- Session cookie: 30-min idle timeout, `HttpOnly`, `IsEssential`.
- Identity email services are wired to `EmailSender` (`IEmailSender<IdentityUser>`)
  via SMTP settings in `EmailSettings` (enables confirmation/reset emails).

## Roles

Created in `SeedData` at startup and referenced by the authorization policies:

- `Admin` — full control (user management, audit log, analytics/secret areas).
- `Member` — normal logged-in members.

## Access levels

Every account is described by the roles it holds. The UI calls this the account's
**access level**, and there are exactly three effective states:

| Access level | Roles held | Can do |
| --- | --- | --- |
| Admin | `Admin` (+ `Member`) | Everything, including **user management** |
| Member | `Member` | Books, dashboard, REST API, analytics, audit log |
| No access | *(none)* | Signed in but blocked by every policy → `/Account/AccessDenied` |

`/User` is the only screen that changes other people's access, and it is gated at the
controller level (`[Authorize(Policy = "AdminOnly")]`) — the navbar/sidebar links are
merely hidden for non-admins, they are not the security boundary.

## User management (`/User`)

Admin-only CRUD over Identity users, backed by `UserManager` / `RoleManager`:

| Action | Route | Notes |
| --- | --- | --- |
| List + search | `GET /User?q=&role=` | Filter by email/username and by access level (`Admin`, `Member`, `None`) |
| Create | `GET/POST /User/Create` | Choose the access level at creation time (`Member` is the default) |
| Details | `GET /User/Details/{id}` | Roles, lockout state, confirmed email/phone, 2FA |
| Edit | `GET/POST /User/Edit` | Email/username + phone; rejects duplicates |
| Reset password | `GET/POST /User/ResetPassword` | Admin sets a new password directly (no email round-trip) |
| Grant/revoke role | `POST /User/ToggleRole` | Only `Admin` / `Member` accepted; anything else → 400 |
| Lock/unlock | `POST /User/ToggleLockout` | Sets `LockoutEnabled` + a 100-year `LockoutEnd` |
| Delete | `GET/POST /User/Delete` | Confirmation page, then `DeleteAsync` |

Guard rails, so an admin cannot lock themselves out of the system:

- **No self-destruction** — you cannot delete your own account, remove the `Admin` role
  from yourself, or lock yourself. Those menu items are also disabled in the UI.
- **No last-admin removal** — `IsLastAdmin()` blocks deleting, demoting, or locking the
  only remaining `Admin`.
- **Audit trail** — every mutation writes an `AuditLogs` row via
  `HttpContext.Items["AuditDetails"]`, e.g. *"Created user 'x@y' with role 'Admin'."*

A `Member` (or a role-less account) that opens `/User` is bounced to
`/Account/AccessDenied`, which shows the account's current roles and offers
"Back to home" / "Sign in as a different user".

`tests/user-management-smoke.py` exercises the whole flow against a running instance
(admin login → create member → search/filter → member blocked → guard rails →
lock/unlock → promote/demote → audit trail); it prints 47 pass/fail checks and exits
non-zero on any failure. See the header of that file for how to point it at a stack.

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