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
scripts/             Console launcher, smoke checks and image publishing
deploy/              Optional Compose overrides (merge after compose.yaml)
compose.yaml         API + frontend containers
compose.dbs.yaml     PostgreSQL/MySQL targets; optional SQL Server
OctoBackup.sln       Backend solution
```

Generated builds/reports are ignored. Local data and keys live under `Backend/data`;
smoke-test artifacts and tools can live under `Backend/artifacts`. Keep these private
files separate from source cleanup. Optional deployment examples are documented in
[operations](docs/OPERATIONS.md). The Windows image publishing helper is
`scripts/build-and-push.cmd`; both Dockerfiles use the repository root as context.

## Features and limits

Working workflows include connections/SSH, manual backups, schedules, authorized
history/downloads, user/access management and account settings. SQLite stores app
metadata; MySQL/MariaDB, PostgreSQL, SQL Server and mounted SQLite files are backup targets. New credentials use .NET Data Protection;
cookie sessions use CSRF, rate limits and server-side access checks.

Administrators can open **Application settings** to approve database/SSH hosts,
control public registration and confirmation, and change retention defaults and
schedule polling. If a server host is blocked, add its exact hostname/IP there and
save before creating the connection. Changes persist without an API restart.
MySQL exports detect client capabilities and report actionable failures. See
[MySQL backup troubleshooting](docs/OPERATIONS.md#mysql-dump-failures) for required
client tools, TLS trust and routine-visibility grants.

Restore tests run in disposable containers, with optional automatic testing after
each successful backup and manual **Test / preview** from Backup history. The saved
report shows sampled tables and rows. Enable the deployment runtime first, then
the administrator toggle; see [restore-test setup](docs/OPERATIONS.md#restore-testing).
The UI explains disabled deployments. Private runtime JSON settings reload for new
tests; preload the selected image and retry. Existing reports remain accessible.
Dump headers select compatible, deployment-approved engine versions. Preload the
required image; PostgreSQL 17 dumps need a 17+ test server. MySQL/MariaDB restores
inspect the archive's original database and preserve binary values.
Backup encryption is disabled by default. Administrators can enable global encryption
in Application settings by entering and confirming a custom password. Individual
server passwords work independently, including when global encryption is off, and
can be configured while creating/editing a server or from Servers > Backup encryption.
History offers encrypted downloads and password-confirmed decrypted downloads where
applicable. Existing encrypted backups retain their original keys when settings change.
See [encryption and engine compatibility](docs/BACKUP_ENCRYPTION.md), including the
offline decryption tool and SQLite mount configuration.
Email integration and MFA/recovery remain planned. SQL Server downloads require a
dedicated shared staging filesystem; configured local/remote roots can use different mount paths. Use one app replica per data
store. See [security](docs/SECURITY.md) for the controls and their limits.

See [connection troubleshooting and SQL Server setup](docs/DATABASE_CONNECTIONS.md)
for console dump clients, PostgreSQL password transport, private Docker networks
without published database ports, and the SQL Server staging Compose override.

Appsettings JSON runtime values reload automatically, including restore-image maps.
For Docker, use the watched directory override; see [configuration reload and image
preloading](docs/CONFIGURATION_RELOAD.md). Pull the exact configured image on the
restore daemon before retrying: `docker image pull mysql:8.4` is an example.
Environment changes, storage/key roots and HTTP/proxy setup remain startup settings.

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

[Architecture](docs/ARCHITECTURE.md) | [Roadmap](docs/ROADMAP.md) |
[Product](docs/PRODUCT.md) | [Design](docs/DESIGN.md) | [Agent guidance](AGENTS.md)

MIT licensed; see [LICENSE](LICENSE).

## Interface refresh (2026-10-24)

The workspace uses midnight and navy surfaces with deep blue/cobalt
gradients and white action labels, larger text and icons, and the supplied white octopus navigation logo.
Hover or focus the logo to reveal OctoBackup. Editing and sensitive operational
actions use focused dialogs with resource context, explicit submit/confirmation,
pending state and inline API errors. Application settings expose summary cards;
open **Edit workspace policies** or **Manage backup encryption** to edit values.
See [interface guidance](docs/DESIGN.md). Authentication and API authorization are unchanged.

The canvas is near-black (`#04070D`), with a 128px navigation logo and a 104px sign-in logo.

Backup history separates colored restore-verification labels from the Actions column.
Use **Test / preview** for the report and **Download** for encrypted/decrypted choices.
Decrypted downloads require the archive password; unencrypted archives offer their original file.
