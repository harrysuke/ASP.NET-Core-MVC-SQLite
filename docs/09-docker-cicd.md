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
