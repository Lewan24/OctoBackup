FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY Backend/ Backend/
RUN dotnet publish Backend/src/Server.Api/Server.Api.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends mysql-client postgresql-client ca-certificates curl \
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
