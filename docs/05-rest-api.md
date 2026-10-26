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