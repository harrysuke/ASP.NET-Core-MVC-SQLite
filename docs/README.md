# mvc — Books Management Application

Documentation for the `mvc` solution.

## Contents

| Doc | Covers |
| --- | --- |
| [01 Project Overview](01-project-overview.md) | What the app is, features, tech stack |
| [02 Architecture](02-architecture.md) | Solution layout, layers, request pipeline |
| [03 Database](03-database.md) | SQLite file, schema, migrations, seed data |
| [04 Security & Identity](04-security-identity.md) | Identity setup, roles, policies, audit + analytics filters |
| [05 REST API & Reports](05-rest-api.md) | `RestController` endpoints and report routes |
| [06 Deployment (IIS)](06-deployment-iis.md) | Publishing to Windows Server / IIS, SQL auth, env config |
| [07 Frontend & Theming](07-frontend-theming.md) | Layout, dark/light theme toggle, Chart.js |
| [08 Troubleshooting](08-troubleshooting.md) | Issues hit during development/deployment and their fixes |
| [09 Docker & CI/CD](09-docker-cicd.md) | Container image, compose deployment, Docker Hub pipeline |

## Quick facts

- Target framework: `net10.0` (Framework-dependent), ASP.NET Core MVC
- Identity: ASP.NET Core Identity (`IdentityUser`) with **default auth** scaffold
- Roles: `Admin`, `Member` (see `SeedData.AdminRole` / `SeedData.MemberRole`)
- Database: **SQLite** (`booksDb.sqlite`), code-first EF Core, `DefaultConnection`
- Data access: EF Core in `BookRepository` (SQLite has no stored procedures)
- Deployment: **IIS** at `C:\inetpub\wwwroot\mvc`; the app pool needs write access to the
  folder holding the database file
- Container: `Dockerfile` + `docker-compose.yml`, image pushed to Docker Hub by
  `.github/workflows/docker-publish.yml` (SQLite file on the `books-data` volume)
- Dev keyboard shortcut (site.js): `Ctrl+Shift+T` toggles theme + dumps state to console