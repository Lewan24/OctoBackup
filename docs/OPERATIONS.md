# Run OctoBackup

## First-time setup

Install .NET SDK 10.0.302+ (a .NET 10 feature band), Node.js 22.12+, and Docker
Desktop with its Linux engine if you want containers. From the repository root:

```powershell
node scripts/dev.mjs init
dotnet restore OctoBackup.sln
npm ci --prefix Frontend
```

`init` creates an ignored `.env` containing random encryption/database passwords.
It never overwrites an existing file. Read `ADMIN_EMAIL` and `ADMIN_PASSWORD` from
that file to sign in; passwords are not printed. Keep `CRYPTO_KEY` stable and backed
up. This file is local configuration, not a production secret-management system.

## Option A: API and React in Docker

```powershell
docker compose up --build -d --wait
docker compose logs -f api frontend
```

Open **https://localhost:8443**. The frontend generates a temporary self-signed
localhost certificate, so the browser initially asks you to trust it. For a trusted
certificate, put `server.crt` and `server.key` in the ignored `certs/` directory,
readable by container UID 101, and recreate the frontend. No private keys are built
into either image. `FRONTEND_PORT` changes the published port.

There are two separate project Dockerfiles, both built with repository-root context:

```powershell
docker build -f Backend/Dockerfile -t octobackup-api .
docker build -f Frontend/Dockerfile -t octobackup-frontend .
```

The non-root Nginx container serves React and proxies `/api` and `/backuphub` to
`api:8080`, including WebSocket upgrades and longer backup requests. The API is not
published to the host. TLS terminates at Nginx; the API trusts forwarded scheme/IP
only from the configured frontend IP. This is necessary for Secure cookies, HTTPS
redirects and correct rate limiting. No permissive CORS is required.

Both containers have health checks, read-only root filesystems, dropped
capabilities and temporary `/tmp`. Application metadata, cookie keys and backups
persist in the `octobackup-data` named volume. `docker compose down` keeps data;
`docker compose down -v` permanently deletes those volumes. Never use `-v` casually.

If subnet `172.30.85.0/24` conflicts with another Docker network, change
`WEB_SUBNET`, `WEB_DYNAMIC_RANGE` and `FRONTEND_IP` consistently in `.env`. The frontend IP must belong to the subnet and stay outside the dynamic allocation range. Change `ALLOWED_HOSTS` when accessing the app through a real hostname.
Default published ports bind to loopback. Internet deployment also requires a
trusted certificate, appropriate interface binding, firewall rules and backups.

## Option B: databases in Docker, applications in the console

```powershell
docker compose -f compose.dbs.yaml up -d --wait
node scripts/dev.mjs
```

Open **http://127.0.0.1:5173**. This starts the .NET API on port 5080 and React/Vite
on 5173 in the current terminal. Ctrl+C stops both. Existing environment values
have precedence over `.env`; the launcher explicitly selects Development mode,
local data under `Backend/data/`, and direct hosting without trusted proxy headers.
Do not expose these development servers to the internet.

To use separate terminals:

```powershell
node scripts/dev.mjs api
node scripts/dev.mjs frontend
```

`DEV_API_URL` changes the API address and Vite proxy target together. React always
calls relative `/api` URLs; secrets and backend URLs are not embedded in its bundle.

| Database | Console API connection | Database | User/password |
| --- | --- | --- | --- |
| PostgreSQL 16 | `127.0.0.1:55432` | `POSTGRES_DATABASE` | `POSTGRES_USER` / `POSTGRES_PASSWORD` |
| MySQL 8.4 | `127.0.0.1:53306` | `MYSQL_DATABASE` | `MYSQL_USER` / `MYSQL_PASSWORD` |
| SQL Server 2022 (optional) | `127.0.0.1:51433` | create a database first | `sa` / `MSSQL_SA_PASSWORD` |

The database names and credentials come from `.env`. Ports are configurable there.
PostgreSQL/MySQL are started by default. SQL Server needs extra memory and accepts
Microsoft's Developer edition license only for development/test use:

```powershell
docker compose -f compose.dbs.yaml --profile sqlserver up -d --wait
```

The local database images do not configure trusted TLS. Disable the connection's
TLS checkbox only for these local test targets; production connections should use
verified TLS. The MySQL `backup` account has database-scoped data grants plus SHOW_ROUTINE metadata access, initialized by Backend/docker/mysql-init.sh for new volumes. The dump omits tablespaces so PROCESS permission is unnecessary. With TLS explicitly disabled, local caching_sha2 authentication retrieves the server public key; use verified TLS in production.

These databases are **backup targets**. OctoBackup's own configuration/users remain
in SQLite. Database volumes persist across container restarts. Changing a password
in `.env` does not change the credentials of an already initialized database volume;
rotate credentials using the database's administration commands instead.

For real exports from a console API, install compatible `pg_dump` and
`mysqldump`/`mariadb-dump` on the host PATH. `pg_dump` 16+ supports the provided
PostgreSQL 16 target. The API Docker image includes clients, but host-run .NET
cannot invoke executables inside another container automatically.

### MySQL dump failures

The API detects the installed client's actual version/capabilities, including a
MariaDB executable named `mysqldump`. It forces TCP so the configured port is used,
disables column statistics when supported, and sets MySQL `--set-gtid-purged=OFF`
for per-database logical archives. These archives do not provision replication GTID
state. TLS verification remains enabled when requested; it is never downgraded
after a failed attempt. Client option files may supply a private trusted CA.

Exit code 2 is not a unique diagnosis. The application now returns a safe category
and recovery hint for permissions, authentication, TLS, connectivity and unsupported
client options. Raw stderr, SQL text and credentials are not exposed. Failed,
timed-out and unstartable exports remove their partial output; existing files are
not overwritten. The stderr pipe is drained concurrently with bounded capture.

If the message names stored-routine visibility, an authorized database administrator
must check grants for the actual account and allowed client host. Table-level or
database-level grants alone can leave routine definitions unreadable. Examples:

```sql
-- MySQL 8.0.20 and newer; replace both account placeholders.
GRANT SHOW_ROUTINE ON *.* TO 'backup_user'@'allowed_client_host';
-- MySQL 5.7 instead:
GRANT SELECT ON mysql.proc TO 'backup_user'@'allowed_client_host';
```

The first grant permits viewing routine definitions across the server; the second
permits reading the legacy routine metadata table. Review that scope for your
deployment. Also check database-scoped SELECT, SHOW VIEW, TRIGGER and EVENT grants.
The application does not grant itself privileges or silently omit routines/events
to report a successful incomplete backup. Do not replace the account host with
`%` unless the database administrator intentionally permits that access pattern.

Reference: [MySQL mysqldump options and privilege requirements](https://dev.mysql.com/doc/refman/8.0/en/mysqldump.html).

## Optional: applications and databases all in Docker

```powershell
docker compose -p octobackup -f compose.yaml -f compose.dbs.yaml up --build -d --wait
```

Use `postgres:5432` or `mysql:3306` as hosts/ports in the UI, rather than host
loopback ports. Both files merge onto the same project network. The API allowlist
includes these service names. When using separate Compose projects, their networks
are intentionally separate; use the console configuration above or explicitly
configure shared networking. `host.docker.internal` refers to the host from the API
container; `localhost` inside a container refers to that container itself.

SQL Server writes native `.bak` files on its own host. Downloads require a shared
backup filesystem visible at the same absolute path to SQL Server and API with
correct write/read permissions. The optional development database container does
not provision that shared path. A Windows console API and Linux SQL Server cannot
share arbitrary Windows paths; connection testing alone does not prove backups work.

## Direct dotnet console configuration (without launcher)

No launcher or manually exported variables are required for local development:

```powershell
dotnet run --project Backend/src/Server.Api
```

Inside `Backend/src/Server.Api`, simply run `dotnet run`. The `http` launch profile
selects Development and **http://127.0.0.1:5080**. Start React in another terminal
with `npm run dev --prefix Frontend`; open **http://127.0.0.1:5173**.

On first development startup, the API creates the ignored private file
`Backend/src/Server.Api/appsettings.Local.json`. It imports an existing repository
`.env` encryption key and bootstrap credentials, or generates random credentials
for a new installation. Read `Bootstrap:AdminEmail` and `Bootstrap:AdminPassword`
in the local JSON to sign in. Existing accounts are never replaced.

Keep this file stable: its key decrypts stored database/SSH credentials. If an
existing SQLite database is found without its key, startup refuses to generate a
replacement. Restore the original key instead. Production never generates or
loads this development file; it is excluded from publish output and Docker context.

Configuration precedence, from lowest to highest: checked-in appsettings files,
local JSON, Development user-secrets, environment variables, command-line options.
For example, explicitly manage secrets with:

```powershell
dotnet user-secrets set "CRYPTO_KEY" "<private-base64-32-byte-key>" --project Backend/src/Server.Api
dotnet user-secrets set "Bootstrap:AdminEmail" "admin@example.com" --project Backend/src/Server.Api
dotnet user-secrets set "Bootstrap:AdminPassword" "<private-password>" --project Backend/src/Server.Api
dotnet run --project Backend/src/Server.Api
```

Development appsettings select `Backend/data` for SQLite, backups and cookie keys.
Relative storage/SSH paths resolve against the API content root. Production falls
back to the OS local application-data OctoBackup folder unless configured; Docker
sets `/data`. The API logs its environment and storage location, never secrets.
Only the three secret/bootstrap values above are imported from `.env` during first
development initialization; general `.env` loading remains the launcher's job.

| Setting | Purpose |
| --- | --- |
| `CRYPTO_KEY` | Stable private AES key; generated privately on fresh Development startup; required explicitly in production |
| `Storage:DataDirectory` | Config, Backups and keys storage; legacy `OCTOBACKUP_DATA` takes precedence |
| `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword` | First administrator only when no users exist |
| `Development:AutoProvisionLocalSettings` | Enables first-run private settings creation in Development only |
| `AllowedHosts` | Semicolon-separated incoming hostnames, without ports |
| `Security__AllowedDatabaseHosts__0`, `__1`, ... | Initial exact database/SSH host allowlist; saved admin settings override it |
| `ReverseProxy__Enabled`, `ReverseProxy__KnownProxies__0` | Opt-in forwarding from explicit trusted IPs |
| `Hosting__ServeFrontend` | Default false: API-only. True permits serving a separately copied SPA for tests/embedded deployments |
| `Ssh:HostKeysFile` | JSON mapping hosts to verified SHA256 SSH fingerprints; legacy `OCTOBACKUP_SSH_HOST_KEYS` takes precedence |

Development appsettings allow localhost, 127.0.0.1 and host.docker.internal; base
production appsettings have an empty database-host allowlist. The current
`AuthSettings` seed registration and automatic confirmation; the admin panel can
disable both for administrator-managed accounts. Production cookies require HTTPS. `/api/health` permits private HTTP
health probes and becomes available after startup migrations/initialization.

## Administration settings

Sign in as an administrator, open **Application settings** under Administration,
and add the host that was rejected when creating your server. Add one exact DNS
hostname, IPv4 or IPv6 address per line (no URL, port, path or wildcard), then
select **Save settings**. Return to Servers and create the connection. For SSH,
allow the configured database host, SSH gateway and tunnel remote host separately.
Approving a host permits connection attempts; it does not prove connectivity or
replace database credentials, TLS verification or verified SSH host fingerprints.

| Editable setting | Effect |
| --- | --- |
| Allowed database hosts (up to 256) | Applies to connection create/edit, tests, manual and scheduled backups. Empty denies all. Removing a host blocks new operations on existing connections; active jobs are not interrupted. |
| Public account registration | Changes availability on sign-in and server-side registration immediately. Administrators can still create operators. |
| Automatic account confirmation | Applies to new public sign-ups. Existing pending accounts can be approved with **Confirm account** in Users & access. Does not verify mailbox ownership. |
| Default retention, 1–3650 days | Applies to new server backup configurations. Existing retention remains unchanged. Cleanup follows a successful replacement only. |
| Schedule polling, 30–3600 seconds | Changes the worker interval after the current wait/job completes; default 300 seconds. |

Settings persist in `Config/application-settings.json` under the data directory,
including the Docker `/data` volume. Include it in recovery copies. Until the first
save, deployment configuration supplies defaults (`Security`, `AuthSettings`,
`Backups:DefaultRetentionDays`, `Backups:SchedulePollSeconds`). After a save, this
file is authoritative for those runtime settings, even if appsettings/environment
defaults later change. The file contains no encryption key or provider passwords.
Changes are atomic, logged with administrator ID and tagged with a revision;
stale concurrent saves return HTTP 409 and require reloading/reapplying changes.
Invalid persisted configuration fails closed. Use one replica per data directory.

Encryption/bootstrap secrets, storage paths, incoming host filtering, proxy trust,
HTTPS and SSH fingerprints remain deployment-managed. Password/session/rate-limit
security rules remain fixed in the host. These values are never exposed by this API.
Network egress controls are still required for DNS-rebinding/port restrictions.

## Tests and generated output

```powershell
dotnet test Backend/tests/OctoBackup.Tests
npm test --prefix Frontend
npm run build --prefix Frontend
node Frontend/node_modules/@playwright/test/cli.js install chromium
npm run test:e2e --prefix Frontend
```

Run backend builds/tests before E2E: Windows locks running server binaries.
Browser tests use disposable data and explicitly enable embedded SPA hosting.
Desktop/mobile suites use separate API processes on 5087/5088 to isolate data and
per-IP rate-limit budgets. Both ports must be free.
`Backend/tests/DumpToolFixture` is a portable child-process fixture used by backend
tests; it exercises stderr backpressure, timeout termination and output cleanup.
`Backend/tests/MySqlDumpSmoke` runs the real MySQL provider against a disposable
loopback schema named `backup_probe`, with the test account password in MYSQL_PWD:

```powershell
dotnet run --project Backend/tests/MySqlDumpSmoke -- <disposable-mysql-port>
```

Seed a `probe` table containing `verified-row` and a `get_probe` routine first.
`--expect-denied` additionally verifies the missing-routine-visibility failure.
This harness is intended only for disposable local databases and writes test dumps
under `Backend/artifacts/mysql-dump-smoke`; it does not configure production grants.
`Frontend/dist`, `Frontend/test-results`, `Backend/bin/obj` outputs and
`Backend/artifacts/publish` are generated and ignored. `artifacts` means published
build output, not application source or live backup data. Recreate it with:

```powershell
dotnet publish Backend/src/Server.Api -c Release -o Backend/artifacts/publish
```

Paths containing `#` are handled by the frontend's temp-staging launcher. Source
remains in the checkout; output is copied back. Do not put secrets in frontend code.

## Upgrade and recovery

### Backup queue

After upgrading, restart the API once so startup applies the additive
`AddPersistentBackupQueue` migration. Rebuild the frontend (or both Docker images)
to show the queue banner, request list and settings. Existing backup/history tables
and their migrations are retained. No additional broker or database is required.

Click **Queue backup** to save a request; acceptance is not backup completion.
The global banner and **Backup queue** page show Queued, Running, Cancelling,
Succeeded, Failed, Cancelled or Interrupted states and safe failure hints. One
export runs at a time. A database already queued/running/stopping returns HTTP 409,
including equivalent connection records for the same canonical target.

Application settings controls:

| Setting | Default | Bounds |
| --- | --- | --- |
| Cancellation window | 20 seconds | 5–300 seconds |
| Completion spacing between jobs | 5 seconds | 1–300 seconds |
| Schedule polling | 300 seconds | 30–3,600 seconds |

Deployment defaults are `Backups:QueueDelaySeconds`, `Backups:QueueSpacingSeconds`
and `Backups:SchedulePollSeconds`. Persisted administrator settings take precedence.
The delay is saved with each new request; changing it does not shorten existing
grace periods. Eligibility countdown means ready to start, not a guaranteed start
time. Active job admission is capped at 1,000; pages contain at most 100 rows.

Cancel stops waiting work immediately. Running cancellation becomes Cancelling
until the provider/process and cleanup acknowledge it; a backup already committed
may win the race. Remove cancels/hides a waiting request or hides a terminal one.
Running requests must stop first. Hidden rows remain in SQLite for audit; removing
a request does not delete its completed backup. Administrators manage all requests;
operators manage their own manual requests and scheduled requests on assigned
servers. Other assigned operators can view but cannot cancel a peer's manual job.

Schedules enqueue through the same worker pipeline. Matching due occurrences on
one connection coalesce into an active job and advance when accepted. Cancel skips
that occurrence; failures do not automatically retry it. Future occurrences remain
scheduled. Invalid/unapproved schedules stay due for repair and are logged.
Queued requests survive restart. Running/stopping jobs become Interrupted; inspect
history/storage before retrying. Hard termination can leave orphan files, and SQL
Server remote-file cleanup is best effort. Use one API replica per data directory.

API clients must adapt to asynchronous acceptance:

- `POST /api/backups/PerformBackup`, body: server GUID → 202 + request DTO.
- `GET /api/backup-queue?offset=0&limit=25` → scoped status/page and worker summary.
- `POST /api/backup-queue/Cancel` or `/Delete`, body: request GUID → 204.
- Administrator `POST /api/backups/PerformSchedulesBackup` → 202 + queued count.

All mutations require the existing session and CSRF token. Poll status before
downloading a finished backup; `scripts/check-compose.mjs --backup` does so.

Stop the service and take a consistent offline copy of the full data directory,
SQLite WAL/SHM state where applicable, cookie keys and external CRYPTO_KEY. Use
one application replica per data directory. Existing migrations are retained.
Bootstrap does not overwrite users or create a second admin on an existing store.

Legacy deployments used a public development key/certificate. Rotate those secrets
with a reviewed credential migration; replacing CRYPTO_KEY alone makes old secrets
unreadable. Legacy CBC remains readable; new writes use GCM. ZIP files themselves
are not encrypted. Restrict storage permissions, encrypt volumes, monitor failures
and disk space, and verify restores independently. See SECURITY.md and ROADMAP.md.

Proxy guidance: [Microsoft forwarded headers documentation](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).


## Verified deployment checks (2026-10-21)

- Separate API and frontend Docker images built successfully.
- Combined app/database Compose stack: all four containers healthy; non-root,
  read-only app containers, TLS frontend, private API and forwarded-header handling.
- Browser sign-in through Nginx; Secure/HttpOnly session, CSRF rejection and
  authenticated SignalR negotiation/WebSocket traffic.
- PostgreSQL 16 and MySQL 8.4 connections, exports and ZIP downloads passed.
- Downloaded exports restored into separate disposable databases; representative
  rows matched. This is a smoke test, not a full schema/provider compatibility suite.
- Console launcher: API 5080 + Vite 5173; browser login/dashboard passed.
- Direct `dotnet run`: health, CSRF and administrator login passed without the
  launcher. Eleven backend tests and both browser workflows passed. Release
  publishing excludes the private development settings file.
- SQL Server profile parsed successfully; its large image and native backup/shared
  filesystem flow were not exercised in this pass.
- Temporary test containers/volumes and console processes were stopped afterward.

To reproduce the disposable Compose smoke test (requires installed frontend deps
and Playwright Chromium):

```powershell
docker compose -p octobackup-check -f compose.yaml -f compose.dbs.yaml up --build -d --wait
node scripts/check-compose.mjs --backup
docker compose -p octobackup-check -f compose.yaml -f compose.dbs.yaml down --volumes
```

The smoke script is reserved for this disposable project: it seeds test tables,
initializes development MySQL grants, and writes downloaded ZIPs beneath
Backend/artifacts/compose-check. Do not point it at production infrastructure.
