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