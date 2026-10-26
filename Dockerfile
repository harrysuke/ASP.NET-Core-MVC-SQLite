# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# ASP.NET Core MVC + SQLite
#
# The database is a single file, so there is no database server to compose with.
# The SQLite file lives on a volume mounted at /app/data, which keeps the
# container itself stateless and disposable.
# ---------------------------------------------------------------------------

# ---- build stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on its own layer: editing source code then reuses the restored
# packages instead of downloading them again.
COPY mvc/mvc.csproj mvc/
RUN dotnet restore mvc/mvc.csproj

# Then the rest of the project and a release publish.
COPY mvc/ mvc/
RUN dotnet publish mvc/mvc.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        /p:UseAppHost=false

# ---- runtime stage --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl exists only for the HEALTHCHECK below; the ASP.NET runtime image has no
# HTTP client of its own. Removed package lists keep the layer small.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Writable data directory for booksDb.sqlite (+ its -wal / -shm companions).
# Ownership is set here so a freshly created named volume inherits it.
RUN mkdir -p /app/data \
    && chown -R $APP_UID:$APP_UID /app/data
VOLUME ["/app/data"]

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__DefaultConnection="Data Source=/app/data/booksDb.sqlite;Default Timeout=30"

# .NET 8+ images ship a non-root "app" user ($APP_UID, uid 1654).
USER $APP_UID

EXPOSE 8080

# The landing page is [AllowAnonymous], so it is a valid unauthenticated probe.
HEALTHCHECK --interval=30s --timeout=5s --start-period=25s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "mvc.dll"]
