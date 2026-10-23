FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY Backend/ Backend/
RUN dotnet publish Backend/src/Server.Api/Server.Api.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
# PGDG supplies pg_dump 18, which can export supported older PostgreSQL servers
# as well as 17/18. Noble's default client (16) cannot export a 17/18 source.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && mkdir -p /usr/share/postgresql-common/pgdg \
    && curl --fail --silent --show-error https://www.postgresql.org/media/keys/ACCC4CF8.asc -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
    && echo 'deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt noble-pgdg main' > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends mysql-client postgresql-client-18 \
    && mysqldump --version && pg_dump --version \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/Config /data/Backups /data/keys \
    && chown -R app:app /data
WORKDIR /app
COPY --from=build --chown=app:app /out/ ./
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=8080 OCTOBACKUP_DATA=/data
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
    CMD curl --fail --silent http://localhost:8080/api/health || exit 1
USER app
ENTRYPOINT ["dotnet", "Server.Api.dll"]

# Explicit opt-in build for deployments that delegate a dedicated Docker daemon.
FROM runtime AS restore-runner
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends docker.io \
    && rm -rf /var/lib/apt/lists/*
USER app

FROM runtime AS final
