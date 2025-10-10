# ============= BUILD STAGE =============
FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY . .

# restore solution
RUN dotnet restore "OctoBackup.sln"

WORKDIR /src/Src/Bootstraper/Server/Server.Api

RUN dotnet publish "Server.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false


# ============= RUNTIME STAGE =============
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime

WORKDIR /app

# install tools (mysqldump, pg_dump, sqlcmd)
# for smaller image remove this (application will use libraries instead of these tools automatically)
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        mariadb-client \
        postgresql-client \
        curl gnupg2 apt-transport-https ca-certificates && \
    curl -sSL https://packages.microsoft.com/keys/microsoft.asc \
        | gpg --dearmor \
        | tee /usr/share/keyrings/microsoft-prod.gpg > /dev/null && \
    echo "deb [arch=amd64,arm64,armhf signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/debian/12/prod bookworm main" \
        | tee /etc/apt/sources.list.d/mssql-release.list && \
    apt-get update && \
    ACCEPT_EULA=Y apt-get install -y --no-install-recommends \
        msodbcsql18 mssql-tools18 && \
    echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' >> /etc/bash.bashrc && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN find . -name "*.pdb" -delete && \
    find . -name "*.xml" -delete

EXPOSE 80
EXPOSE 443

ENTRYPOINT ["dotnet", "Server.Api.dll"]
