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