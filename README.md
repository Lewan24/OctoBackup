# OctoBackup

[Po polsku](docs/README_PL.md)

Self-hosted database backup management with a **.NET 10 API** and **React +
TypeScript frontend**. Manage connections, server access, schedules and backup
history from a compact dark operations dashboard with violet gradients, lime
actions and responsive layouts for technical teams.

Manual and scheduled backups enter a persistent queue with live status, duplicate
prevention and cancel/remove actions. Requests wait 20 seconds by default; admins
can configure the cancellation window and spacing in Application settings.

## Get started

Requirements: Docker Desktop (Linux containers), or .NET SDK 10.0.302+ and Node.js
22.12+ for console development.

```powershell
node scripts/dev.mjs init
```

This creates a private, ignored `.env` with random keys and passwords. Existing
settings are preserved. Sign-in credentials are `ADMIN_EMAIL` / `ADMIN_PASSWORD`
in that file.

**Both applications in Docker:**

```powershell
docker compose up --build -d --wait
```

Open **https://localhost:8443**. The default local certificate is self-signed; mount
your trusted certificate/key in `certs/` to replace it. React/Nginx proxies the
private API and SignalR on the same browser origin.

**Only databases in Docker, applications in the console:**

```powershell
docker compose -f compose.dbs.yaml up -d --wait
dotnet restore OctoBackup.sln
npm ci --prefix Frontend
node scripts/dev.mjs
```

Open **http://127.0.0.1:5173**; the API runs on port 5080. Use `api` or `frontend`
after the launcher command to run either project in its own terminal. Host-run
exports require local dump tools. SQL Server is an optional Compose profile.

**Run the API directly:**

```powershell
dotnet run --project Backend/src/Server.Api
```

Or run `dotnet run` inside the API directory. It starts at **http://127.0.0.1:5080**,
creates private development settings in `Backend/src/Server.Api/appsettings.Local.json`
and uses `Backend/data` for storage. The private file contains initial sign-in
credentials; an existing `.env` key is preserved. Start React separately with
`npm run dev --prefix Frontend`.

See [operations and configuration](docs/OPERATIONS.md) for configuration precedence,
database ports, combined deployment, trusted proxy settings and recovery.

## Repository

```text
Backend/             .NET API, modules, tests, Dockerfile and generated artifacts
Frontend/            React app, Nginx proxy, tests and Dockerfile
docs/                Architecture, security, progress, roadmap and diagrams
scripts/dev.mjs      Private local configuration and console launcher
compose.yaml         API + frontend containers
compose.dbs.yaml     PostgreSQL/MySQL targets; optional SQL Server
OctoBackup.sln       Backend solution
```

`Backend/artifacts/publish` contains generated release binaries. It is ignored,
regenerable build output, not source or live backup data.

## Features and limits

Working workflows include connections/SSH, manual backups, schedules, authorized
history/downloads, user/access management and account settings. SQLite stores app
metadata; PostgreSQL/MySQL/SQL Server are backup targets. New credentials use AES-GCM;
cookie sessions use CSRF, rate limits and server-side access checks.

Administrators can open **Application settings** to approve database/SSH hosts,
control public registration and confirmation, and change retention defaults and
schedule polling. If a server host is blocked, add its exact hostname/IP there and
save before creating the connection. Changes persist without an API restart.
MySQL exports detect client capabilities and report actionable failures. See
[MySQL backup troubleshooting](docs/OPERATIONS.md#mysql-dump-failures) for required
client tools, TLS trust and routine-visibility grants.

Restore verification, email integration, MFA/recovery and backup-file encryption
remain planned. ZIP compression is not encryption. SQL Server downloads require a
shared backup filesystem at the same absolute path. Use one app replica per data
store. See [security](docs/SECURITY.md) for the controls and their limits.

## Verification

```powershell
dotnet test Backend/tests/OctoBackup.Tests
npm test --prefix Frontend
npm run build --prefix Frontend
node Frontend/node_modules/@playwright/test/cli.js install chromium
npm run test:e2e --prefix Frontend
```

Run backend builds before browser tests. Test results and remaining release checks
are in [migration progress](docs/MIGRATION.md).

[Architecture](docs/ARCHITECTURE.md) ? [Roadmap](docs/ROADMAP.md) |
[Product](docs/PRODUCT.md) ? [Design](docs/DESIGN.md) ? [Agent guidance](AGENTS.md)

MIT licensed; see [LICENSE](LICENSE).
