# How OctoBackup works

## Restore verification

`RestoreTestWorker` consumes persisted `BackupTest` rows independently of exports.
Successful backup publication atomically adds an automatic test when the runtime
administrator option is enabled. Manual requests use the existing authenticated,
CSRF-protected backup API and require server access at admission and execution.
`DockerRestoreTestRunner` streams logical ZIP dumps or restores native SQL Server
files in networkless, resource-bounded containers, saves bounded random-table
samples in SQLite, then removes its container. No provider ports or interactive SQL
are exposed. The frontend polls the latest authorized saved report. Additive EF
columns preserve legacy data/status values and record engine snapshots for new
artifacts. Deployment opt-in is separate from the administrator automatic toggle.
See [restore testing](RESTORE_TESTING.md) for isolation, compatibility and limits.
`RestoreArchiveProfile` reads a bounded SQL header and resolves only deployment-owned
version mappings. PostgreSQL uses at least the source/dump-client major, MySQL and
MariaDB use source release/family-specific clients and the archive's database name.
The runner checks actual versions before importing, streams all SQL unchanged and
identifies the failing stage without logging provider output or SQL text.

## Repository map

| Path | Responsibility |
| --- | --- |
| `Backend/src/Server.Api` | .NET 10 HTTP host, middleware, startup migrations, initial administrator |
| `Backend/src/Modules/AuthModule` | ASP.NET Identity accounts, login, registration policy, profile changes |
| `Backend/src/Modules/AdministrationModule` | User listing, explicit confirmation/editing and blocking |
| `Backend/src/Server.Api/Configuration` | Development secrets and durable runtime administrator settings/endpoints |
| `Backend/src/Modules/BackupModule` | Connections, SSH tunnels, access assignments, schedules, backup execution, history and realtime invalidation |
| `Backend/src/Modules/CryptoModule` | Data Protection credential writes; legacy GCM/CBC reads |
| `Backend/src/Modules/Shared` | Data directory and endpoint validation/administrator filters |
| `Backend/tests/OctoBackup.Tests` | API security, encryption and filesystem regression tests |
| `Frontend/src/app` | Routing, authenticated shell, session and SignalR lifecycle |
| `Frontend/src/features` | Auth, dashboard, servers/tunnels, schedules, history, account, administration |
| `Frontend/src/shared` | HTTP/CSRF transport, API types and shared form/status components |
| `Frontend/e2e` | Real API/browser workflow tests on desktop and mobile |
| `deploy` | Optional Compose overrides, merged after root `compose.yaml` |
| `scripts` | Development launcher, deployment checks and Windows image publishing |

The app layer composes queue state into the Servers page so features do not import
one another. Backend backup endpoint filenames match their endpoint classes.

The backend remains a modular monolith. Core contains entities/provider adapters;
Application coordinates use cases; Infrastructure owns EF contexts; Api maps HTTP
endpoints; Shared holds contracts. Module/project names remain stable to preserve
migrations and limit unnecessary namespace churn. There is no active Blazor host.

## Request and data flow

1. React uses same-origin `/api` calls. The production Nginx frontend serves the built SPA and proxies the API;
   Vite proxies API and SignalR requests during development. The API is standalone by default; Hosting:ServeFrontend enables optional embedded SPA hosting.
2. Identity issues an HttpOnly session cookie after password/lockout checks.
   React obtains a CSRF request token in memory for state-changing requests.
3. Authorization runs before business operations. Administrators configure
   connections and assign users. Operators can list assigned enabled servers,
   run/test backups and maintain schedules on those servers.
4. Identity and backup metadata use two EF contexts in the existing
   `Config/OctoBackup.db` SQLite file. Existing migrations are retained. The .NET 10
   Identity migration updates the snapshot without destructive schema changes.
5. Manual actions persist a `BackupRequest` in SQLite and return HTTP 202.
   The worker polls eligible requests each second and runs one export at a time.
   A separate schedule loop scans immediately at startup, then at the configured
   interval (default five minutes). Due occurrences enter the same durable queue;
   occurrence advancement and enqueueing commit together. Duplicate active targets
   are blocked by a unique nullable key. Run only one application replica.
6. MySQL/MariaDB and PostgreSQL dump tools write unique files; ZIP compression
   follows. SQL Server uses an escaped database identifier and parameterized backup
   destination to create a native `.bak` on the database host. A shared filesystem
   with an optional deployment-owned remote-root mapping is required for SQL Server
   downloads. Dedicated staging mode copies a generated native file from a narrowly
   shared staging volume into private API storage, then removes staging plaintext.
   SQLite uses its online snapshot API from an approved local source
   mount. Publication encrypts artifacts only when an individual key or enabled global policy applies.
   MySQL client capabilities determine compatible flags; TCP, disabled histogram
   queries and logical per-database GTID handling avoid client/server mismatches.
   Bounded stderr capture becomes fixed safe failure hints, never raw provider text.
7. A successful replacement file and metadata precede retention cleanup. Server
   configuration defaults to four days; administrators can change the default for
   new configurations. Existing server retention is preserved.
8. Server-side SignalR publishes payload-free invalidation events to affected
   Identity user IDs and administrators. React invalidates queries; reconnecting
   refetches data. Clients cannot call broadcast methods.

## Storage and secrets

`Storage:DataDirectory` configures storage; legacy `OCTOBACKUP_DATA` takes precedence.
Development uses `Backend/data`; production defaults to the platform's local
application-data `OctoBackup` directory. It contains `Config/OctoBackup.db`, `Backups/` and `keys/` (Identity cookie
data-protection keys). Keep the entire directory and the external `CRYPTO_KEY` in
your disaster recovery plan. Neither the key nor database credentials enter React.

Development startup provisions an ignored `appsettings.Local.json`, reusing the
repository `.env` key/bootstrap credentials when present. The file overrides base
JSON but remains below user-secrets, environment and CLI configuration. Production
neither loads nor generates it, and publish excludes it. Existing SQLite data
without its key prevents automatic key generation. Shared storage and SSH pin
configuration are initialized before module services and EF contexts are created.

Database and SSH secrets are encrypted before SQLite persistence. Blank secrets on
edit preserve existing values. New credential ciphertext uses purpose-isolated
.NET Data Protection. Legacy
GCM/CBC remains readable with CRYPTO_KEY; editing upgrades the secret. Backup
artifacts use portable chunked AES-256-GCM with versioned global/connection keys
protected in SQLite by Data Protection. A persisted disabled-by-default policy gates
global encryption; custom individual keys work independently. Key changes preserve older versions.
See [backup encryption](BACKUP_ENCRYPTION.md) for the format, offline recovery,
download confirmation, SQLite snapshots and transient plaintext storage limits.

## Baseline analysis and intentional changes

The original project mixed a Blazor WASM client into the server host. It also used
cookies plus a homegrown secondary token, created SignalR client connections inside
notification services, and exposed a development-only Docker restore prototype.
The React migration replaces client page/form/service logic, removes that token
transport and prototype, and keeps the useful database engine and metadata model.

Original placeholder delete/test actions reported success; delete remains HTTP 501.
Restore tests now enqueue durable work and expose authenticated saved reports.
History paging returns records rather than an empty success.
Creating/editing connections is now administrator-only; this prevents ordinary
operators from redirecting the backend to arbitrary network destinations. Allowed
database/SSH hosts must also be configured by the deployment administrator.

Administrators can manage exact database/SSH hosts, registration/auto-confirmation,
new-server retention defaults and worker polling in Application settings. A shared
read-only settings interface supplies snapshots to Auth/Backup modules. The host
persists validated revisions atomically to `Config/application-settings.json`; saved
values supersede deployment defaults for these runtime settings. The protected
`GET/POST /api/administration/settings` routes enforce the Admin role and mutations
also use global CSRF protection. A public registration-policy route exposes only
the two registration flags. Key material and deployment paths are never returned.
Backup/test execution rechecks approved destinations, including scheduled work.

Unused legacy SMTP/configuration helpers were removed during repository cleanup.
Email delivery remains unimplemented; email workflows and further restore
validation are tracked in ROADMAP.md.

## API contracts

Existing `/api/servers`, `/api/schedules`, `/api/backups`, `/api/statistics`,
`/api/auth` and `/api/administration` route names remain for migration continuity.
Exceptions: secondary token endpoints and `/api/BackupsTests/RunTest` were removed.
`GET /api/auth/csrf` initializes CSRF protection; `GET /api/health` is a minimal
liveness check. Unknown API routes return 404 rather than SPA HTML. OpenAPI/Scalar
dependencies were removed; adding generated API documentation is a follow-up item.

See `Frontend/src/shared/types.ts` for the browser contracts and backend `Shared`
projects for their server equivalents. UI role checks are presentation only; the
backend always enforces permissions.

## Persistent backup queue

`BackupQueueService` handles admission, scoped listing, cancellation/removal and
schedule acceptance. `BackupQueueProcessor` claims eligible rows, revalidates current
account/access/host/target policy, monitors cancellation and publishes backup metadata
with the successful terminal state in one transaction. `BackupExecutor` owns provider
export/compression. Retention follows a durable replacement. Target identity includes
provider, canonical hostname, port, database and SSH destination; credentials and
display names do not bypass deduplication. Different DNS aliases are not resolved
into one network identity.

Requests wait 20 seconds by default and executions have a five-second completion
spacing. Both are administrator settings. Cancellation of a waiting job releases
its active key immediately; a running job keeps it until the worker acknowledges
stopping. Remove hides terminal rows while preserving an audit record; removing a
waiting job cancels it too. Completion versus cancellation uses a conditional
transactional state transition. Due schedules on the same connection coalesce into
one active request; stopping jobs and alias connections do not consume occurrences.
Cancellation skips an accepted occurrence, and failures require explicit retry.

Queued rows survive restarts. Previously running/stopping rows become Interrupted
and are not automatically replayed. Hard crashes can leave orphan files requiring
inspection. Gates and startup recovery assume one API process per data directory;
this is not distributed worker coordination.

Provider connection checks share the same SSH tunnel helper as exports and return
fixed safe error categories. PostgreSQL password connections and pg_dump explicitly
disable GSS negotiation while preserving selected TLS verification. Dump executable
paths are deployment-owned DatabaseTools settings; console clients and container
clients are separate installations. See DATABASE_CONNECTIONS.md.

## Configuration reload

The API watches base/environment/Development Local JSON plus an optional private
runtime file. Each provider atomically retains its last valid data on incomplete
saves or missing files. Only runtime sections reload; startup storage/key/HTTP/proxy
values stay frozen and log a restart notice. Explicit user-secrets/environment/CLI
precedence is preserved. Deployment application defaults reload until an admin
snapshot is saved; that snapshot continues to win. Restore runner scopes capture
image/resource settings so active jobs and cleanup survive later configuration
changes. Enabling the runtime triggers orphan recovery. See CONFIGURATION_RELOAD.md.
