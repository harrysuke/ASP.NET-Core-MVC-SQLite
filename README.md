# ASP.NET Core MVC — Books Management

A full-featured ASP.NET Core MVC application for managing a book catalog, built with
ASP.NET Core Identity, Entity Framework Core, **SQLite**, Bootstrap 5, and Chart.js.
Includes role-based authorization, audit logging, page-view analytics, an admin user
manager, a REST API, and a dark/light theme toggle.

## Highlights

- **Book CRUD** against a local `booksDb.sqlite` file through EF Core. SQLite has no
  stored procedures, so the data access that used to live in T-SQL procs now lives in
  `BookRepository`.
- **Identity & roles** — register/login/logout, password reset, profile, user
  management; `Admin` and `Member` roles with `MemberOnly` / `AdminOnly` policies.
- **Audit trail** — global `AuditActionFilter` + `PageViewFilter` record writes and page
  views with retention cleanup, exposed via a paged admin log + CSV export.
- **Analytics / KPI dashboard** — daily traffic and login charts (Chart.js, theme-aware)
  computed from local DB data (no third-party analytics).
- **REST API** — `/api/rest` JSON endpoints for books (MemberOnly).
- **IIS / Windows Server deployment ready** — framework-dependent publish, env-var
  configuration, automatic database creation + migration at startup.
- **Docker + CI/CD** — multi-stage `Dockerfile`, `docker-compose.yml` with the SQLite file on
  a named volume, and a GitHub Actions pipeline that builds, smoke-tests and publishes the
  image to Docker Hub.

## Full documentation

See [`docs/README.md`](docs/README.md) for the complete set of guides:

| Doc | Covers |
| --- | --- |
| [01 — Project Overview](docs/01-project-overview.md) | Features, tech stack, high-level flow |
| [02 — Architecture](docs/02-architecture.md) | Solution layout, startup pipeline, filters |
| [03 — Database](docs/03-database.md) | SQLite file, schema, migrations, seed |
| [04 — Security & Identity](docs/04-security-identity.md) | Auth, roles, policies, audit filters |
| [05 — REST API & Reports](docs/05-rest-api.md) | `/api/rest` endpoints and analytics reports |
| [06 — Deployment (IIS)](docs/06-deployment-iis.md) | Publishing, env overrides, file permissions |
| [07 — Frontend & Theming](docs/07-frontend-theming.md) | Layout, theme toggle, Chart.js |
| [08 — Troubleshooting](docs/08-troubleshooting.md) | Issues hit and their fixes |
| [09 — Docker & CI/CD](docs/09-docker-cicd.md) | Container image, compose, Docker Hub pipeline |

## Run with Docker

```bash
cp .env.example .env         # set DOCKERHUB_USERNAME, ADMIN_EMAIL, ADMIN_PASSWORD
docker compose up --build -d
```

Open <http://localhost:8080>. The database lives in the `books-data` volume, so
`docker compose down` (without `-v`) keeps your data.

Pull the published image:

```bash
docker run -d -p 8080:8080 -v books-data:/app/data \
  <dockerhub-user>/aspnet-core-mvc-sqlite:latest
```

See [docs/09 — Docker & CI/CD](docs/09-docker-cicd.md) for the image layout, configuration
and the Docker Hub workflow (secrets, tags, multi-arch).

## Quick start (local)

```bash
# restores dotnet-ef from .config/dotnet-tools.json (only needed for manual migrations)
dotnet tool restore --tool-manifest mvc/.config/dotnet-tools.json

# the app creates and migrates booksDb.sqlite itself on first run
dotnet run --project mvc
```

Optional: bootstrap an admin account on first run with
`Admin__Email` / `Admin__Password` environment variables.

Open `http://localhost:<port>`, register (or use the seeded admin), and manage books.
The database file lives next to the app (`mvc/booksDb.sqlite`) and is ignored by git.

## Tech stack

- .NET 10 · ASP.NET Core MVC + Razor
- ASP.NET Core Identity (`IdentityUser` / `IdentityRole`), cookie auth
- EF Core + SQLite (WAL mode, busy timeout), code-first migrations
- Bootstrap 5, jQuery validation, Chart.js
- Docker (multi-stage, non-root, healthcheck) · GitHub Actions → Docker Hub
- Hosted on IIS (Windows Server) or in a container