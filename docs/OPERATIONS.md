# Operations

[Quick start and roadmap](../README.md) · [Database setup](DATABASE_CONNECTIONS.md) · [Backups and recovery](BACKUPS.md)

## Deployment

From the repository root:

```sh
node scripts/dev.mjs init
# Add your public hostname to ALLOWED_HOSTS; keep localhost;127.0.0.1.
docker compose pull
docker compose up -d --wait
docker compose logs -f api frontend
```

Compose uses `lewan24/octobackup-api:latest` and `lewan24/octobackup-web:latest`.
To build locally, use the two root-context build commands in the README, then run
`docker compose up -d --wait`. Ordinary Compose services have no `build` section.
For publishing on Linux or Windows, authenticate with `docker login`, then run
`node scripts/build-and-push.mjs`. It builds both images before pushing and stops
at the first failure. It resolves the repository root from its own location,
including when called from another directory. `--build-only` skips pushes and
`--dry-run` prints commands without contacting Docker. On Linux/WSL/Bash use
`./buildAndPushDockerImages.sh`; on Windows PowerShell use
`.\buildAndPushDockerImages.bat`. Both delegate to the same Node.js script and pass
through its options. A `.bat` file cannot run directly in Bash: errors about
`@echo`, `rem` or `call` indicate the Windows launcher was used in a Unix shell.

Both images use Linux containers. Docker Engine on Linux and Docker Desktop's
Linux-container mode on Windows use the same Dockerfiles and Compose configuration.
The helper checks the selected engine and rejects native Windows-container mode.
CPU architecture follows that engine; the helper publishes a single architecture,
not a combined amd64/arm64 manifest. A deployment needs a matching architecture
or configured emulation. See [Docker's Windows setup](https://docs.docker.com/desktop/setup/install/windows-install/).

Both application containers run as non-root, with read-only roots, dropped
capabilities, no-new-privileges and temporary `/tmp`. Health checks use private HTTP.
The API is not published. `/data` holds metadata, backups, runtime policies and keys
on the `octobackup-data` named volume. `docker compose down` preserves it;
`docker compose down --volumes` deletes it.

Optional overrides live in `deploy/`. Pass root `compose.yaml` first so paths,
build contexts and `.env` resolution remain rooted in the repository:

```sh
docker compose -f compose.yaml -f deploy/compose.private-network.yaml up -d
```

## Reverse proxy

Public TLS terminates at **your reverse proxy**. Application containers neither
load nor generate certificates. Nginx serves the SPA and forwards API/WebSocket
traffic over HTTP to `api:8080`. The API has no HTTPS redirect or HSTS middleware;
configure HTTP-to-HTTPS redirection and HSTS at the public edge.

For a proxy running on the host, use `http://127.0.0.1:8080` as upstream.
`FRONTEND_PORT` changes that host port. Existing `.env` files retain their saved
port; update it if desired. Add your public hostname to `ALLOWED_HOSTS` without
scheme or port. Keep `localhost;127.0.0.1` in that list: the API image probes
`http://localhost:8080/api/health`, and host filtering also applies to health probes.
A host Nginx example, inside the `http` configuration:

```nginx
map $http_upgrade $octobackup_connection {
    default upgrade;
    '' close;
}
server {
    listen 80;
    server_name backups.example.com;
    return 301 https://backups.example.com$request_uri;
}
server {
    listen 443 ssl;
    server_name backups.example.com;
    ssl_certificate /etc/letsencrypt/live/backups.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/backups.example.com/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    add_header Strict-Transport-Security "max-age=31536000" always;
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $octobackup_connection;
        proxy_read_timeout 3600s;
    }
}
```

For a containerized edge proxy, attach it to the Compose `web` network and point it
at `http://frontend:8080`. Host loopback inside a proxy container is that proxy's
own container. If attaching via a separate Compose project, declare the existing
network as external. The default network is `octobackup_web`.

The frontend sets a fixed `X-Forwarded-Proto: https` because its public deployment
contract is HTTPS. It overwrites caller-supplied forwarding headers. Production
session/CSRF cookies stay Secure; browser sign-in requires the public HTTPS URL.
HTTP access to the upstream is useful for health checks. For an isolated private
HTTP test deployment, set `ALLOW_INSECURE_HTTP=true` in `.env` (API setting
`Security:AllowInsecureHttp`), then recreate the API. Both cookies explicitly omit
Secure in that mode; CSRF, HttpOnly, SameSite and authorization remain enforced.
Keep this false behind public HTTPS. The flag is startup-only. For source development
use the Development launcher instead.

The API accepts forwarding only from `ReverseProxy:KnownProxies` and one hop.
Compose trusts the frontend's reserved address (`FRONTEND_IP`). Change `WEB_SUBNET`,
`WEB_DYNAMIC_RANGE` and `FRONTEND_IP` together when the default subnet conflicts;
the reserved address must be inside the subnet and outside the dynamic range.
Never enable trust for arbitrary sources. If bypassing the frontend and proxying
directly to the API, configure the actual edge IP and have it overwrite both
`X-Forwarded-Proto` and `X-Forwarded-For`; route the SPA and hub on the same origin.

The standard two-proxy chain reports the edge proxy's address to the API. Its
per-IP rate limits are shared by those users. Apply client-IP rate limits at the
edge; do not pass unvalidated browser-supplied address headers through Nginx.

SSH host fingerprints are unrelated to web TLS. For SSH connections mount a verified
pin file narrowly into the API and set `Ssh__HostKeysFile` to its container path.
The standard stack needs no `certs/` directory. Database certificate verification
and SSH fingerprint checks remain enabled when configured.

## Development and database targets

Requirements: .NET 10 SDK selected by `global.json`, Node.js 22.12+; Docker is optional.

```sh
node scripts/dev.mjs init
dotnet restore OctoBackup.sln
npm ci --prefix Frontend
node scripts/dev.mjs
```

The launcher runs API `127.0.0.1:5080` and Vite `127.0.0.1:5173`; Ctrl+C stops both.
Arguments `api` / `frontend` select one process. `DEV_API_URL` changes the API and
Vite proxy target together. Development servers are for local access.

Direct `dotnet run --project Backend/src/Server.Api` also works. First Development
startup creates ignored `appsettings.Local.json`, reusing `.env` key/bootstrap values
or generating them for a fresh store. Initial sign-in credentials are in that file.
Existing data without its original key prevents automatic replacement. Production
never creates/loads this file; publishing excludes it. Development data lives in
`Backend/data`; preserve it during cleanup.

```sh
docker compose -f compose.dbs.yaml up -d --wait
# Optional SQL Server Developer edition for disposable development/test data:
docker compose -f compose.dbs.yaml --profile sqlserver up -d --wait
```

| Target | From host API | From API on same Compose network |
| --- | --- | --- |
| PostgreSQL | `127.0.0.1:55432` | `postgres:5432` |
| MySQL | `127.0.0.1:53306` | `mysql:3306` |
| SQL Server (optional) | `127.0.0.1:51433` | `sqlserver:1433` |

Database/user/password/host ports come from `.env`. These fixtures do not configure
trusted TLS. Disable database TLS only for isolated fixtures. Changing `.env` does
not rotate credentials in initialized database volumes. To combine app and targets:

```sh
docker compose -f compose.yaml -f compose.dbs.yaml up -d --wait
```

Host-run exports require local dump tools; the API image includes MySQL client and
pg_dump 18. See [connection tools, grants and storage](DATABASE_CONNECTIONS.md).

## Configuration

| Setting | Purpose |
| --- | --- |
| `CRYPTO_KEY` | Stable private base64 32-byte legacy credential key; required in production |
| `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword` | Initial administrator on an empty user store only |
| `Storage__DataDirectory` | Storage root; legacy `OCTOBACKUP_DATA` wins; Compose uses `/data` |
| `DataProtection__KeysDirectory` | Persistent key ring; defaults to `<data>/keys` |
| `AllowedHosts` | Semicolon-separated incoming hostnames, without ports |
| `Security__AllowedDatabaseHosts__0`, `__1`, … | Initial exact database/SSH host allowlist |
| `ReverseProxy__Enabled`, `ReverseProxy__KnownProxies__0` | Explicit forwarding trust |
| `Ssh__HostKeysFile` | Verified SSH SHA256 fingerprint JSON |
| `Hosting__ServeFrontend` | Optional embedded SPA; default false |

Environment variables use `__` for nested keys. Secrets never belong in frontend
configuration. Configuration order is base JSON, environment JSON, Development
Local JSON, optional runtime JSON, user-secrets, environment, command line.

Administrators manage exact database/SSH hosts, registration/confirmation,
new-server retention, queue timing and automatic restore testing in **Application
settings**. Snapshots persist atomically in `/data/Config/application-settings.json`
and supersede deployment defaults for those values. Stale revisions return 409.
The file contains no encryption key or provider password. Allow database host,
SSH gateway and tunnel remote host separately. Empty allowlists deny all.
Global encryption is a separate administrator-managed database policy.

| Policy | Default | Bounds / effect |
| --- | --- | --- |
| New-server retention | 4 days | 1–3650 days; existing settings preserved |
| Schedule polling | 300 seconds | 30–3600 seconds |
| Queue cancellation window | 20 seconds | 5–300 seconds; captured on admission |
| Completion spacing | 5 seconds | 1–300 seconds |

### Configuration reload

The API watches base/environment JSON, Development Local JSON and optional
`appsettings.Runtime.json` (`Configuration:ReloadFile` selects another startup path).
Valid saves reload runtime values. Missing/malformed files retain their last valid
snapshot; logs omit values/parser details. To remove overrides, save valid `{}`
rather than deleting the watched file.

Reloadable sections include restore images/limits/enablement, dump executable paths,
SQL Server staging paths, SQLite source/test settings, logging and deployment
application defaults until an admin snapshot is saved. Active restore jobs retain
their captured settings through cleanup. Runtime changes cannot create mounts,
install tools or pull images. Failed tests require explicit retry.

Storage, key/bootstrap values, HTTP bindings, host filtering, proxy trust and session
security require restart. Environment/.env changes require container recreation.
For editable Docker configuration, mount the directory so atomic editor saves work:

```sh
mkdir -p config
cp Backend/docker/appsettings.Runtime.example.json config/appsettings.Runtime.json
docker compose -f compose.yaml -f deploy/compose.config-reload.yaml up -d
```

The override selects `/config/appsettings.Runtime.json` and enables polling.
The private config directory is ignored and excluded from builds. Later JSON edits
need no rebuild. Preserve persisted admin policies and the key ring.

## Restore testing

The standard Compose API disables Docker restore execution and has no daemon socket.
SQLite snapshot validation has its separate `Sqlite:RestoreTestsEnabled` setting.
To enable Docker tests, set `RESTORE_DOCKER_GID` in `.env` to the socket group ID
(`stat -c %g /var/run/docker.sock` on Linux), then:

```sh
docker compose -f compose.yaml -f deploy/compose.restore-tests.yaml up --build -d --wait
```

This builds the `restore-runner` API target and delegates Docker daemon control.
Use a dedicated test daemon/VM. Console deployments need Docker CLI and
`RestoreTests__Enabled=true`; remote daemons require secure Docker CLI configuration.
Never expose an unauthenticated Docker endpoint.

Preload the **exact configured image/tag/digest on the daemon used by the API**:

```sh
docker image pull mysql:8.4
docker image pull postgres:18-alpine
docker image inspect postgres:18-alpine
# For a separately configured test context:
docker --context restore-tests image pull mysql:8.4
```

Versioned image maps are `RestoreTests:PostgresImages:<major>`,
`MySqlImages:<major.minor>` and `MariaDbImages:<major.minor>`. Recognized headers
select a compatible configured version; unknown versions require an explicit map.
Execution uses `--pull=never`. Pulling a compatible image needs no API restart;
retry failed tests manually. Do not silently switch engine versions to hide errors.
[Restore isolation, resource settings, reports and smoke commands](BACKUPS.md#restore-testing).

## Validation

```sh
dotnet build OctoBackup.sln
dotnet test Backend/tests/OctoBackup.Tests
npm test --prefix Frontend
npm run build --prefix Frontend
node Frontend/node_modules/@playwright/test/cli.js install chromium
npm run test:e2e --prefix Frontend
npm audit --prefix Frontend
dotnet list OctoBackup.sln package --vulnerable --include-transitive
```

Run backend checks before E2E; Windows locks running server binaries. Browser tests
use disposable stores on ports 5087/5088, which must be free. Frontend scripts handle
`#` in paths through temporary staging. `dist`, `bin`, `obj`, test reports and
`Backend/artifacts` are generated output; private stores/keys are not disposable.

For a disposable Docker deployment, build both standard images, prepare a separate
private environment file (never reuse real backup data), and start project
`octobackup-check` with root app/database Compose files. Override frontend port,
proxy IP and subnet if the main stack exists. Place your HTTPS proxy in front of
that frontend, then run:

```sh
node scripts/check-compose.mjs --env-file /path/to/disposable.env --base-url https://localhost:18443 --ignore-https-errors --backup
```

This script is restricted to the disposable project name. It seeds fixture tables,
checks browser login, Secure/HttpOnly cookies, CSRF, SignalR WebSocket/negotiation, database
connections, queued exports and ZIP downloads. The environment file must match the
one supplied to `docker compose --env-file`. Use `--ignore-https-errors` only with a
self-signed local test proxy. Remove only this test project's containers/volumes
when finished. Export/download checks do not verify a complete provider restore;
separate provider fixtures are described in [database setup](DATABASE_CONNECTIONS.md).

## Login and runtime troubleshooting

- **Portainer: dependency failed to start / API unhealthy:** keep `localhost` in
  API `AllowedHosts` along with the public hostname (also include `127.0.0.1`).
  The image health check uses `http://localhost:8080/api/health`; excluding that
  host returns HTTP 400 even when the API process is running. Compose then refuses
  to start the frontend because it depends on a healthy API. If the allowlist is
  correct, inspect the API logs and its health-check output for the actual startup
  failure; do not remove the dependency or disable the health check.

- **Missing security cookie / invalid token on login:** check the browser URL, not
  the upstream protocol. With the default production policy, the browser must use
  HTTPS even though the edge connects to an HTTP container. A non-loopback HTTP IP
  cannot return Secure cookies. Use public HTTPS or explicitly enable the private
  HTTP test setting above; a port named `10443` does not itself enable TLS.
- **`POST /api/auth/Login` returns 401:** CSRF passed, but Identity rejected the
  credentials/account. This is distinct from an expired session on a protected API.
  The browser shows a uniform sign-in error; API logs include `Sign-in rejected`, a
  safe reason and matching trace ID without the email, password or cookie.
  `invalid_credentials` means check the password; `account_not_found` means check
  the account and mounted store; `account_blocked` / `account_unconfirmed` require
  another administrator to review the account. `account_locked_out` requires the
  configured lockout period to expire (15 minutes after five failed attempts).
  Avoid repeated retries. Changing Bootstrap values never resets existing users;
  another administrator can reset a password through Users & access. Preserve the
  existing data/key volume when investigating; deleting it loses stored data.
- **Behind HTTPS but still failing:** preserve Host and cookies at the edge, disable
  caching of `/api/`, use one stable data/key volume and one API replica. Confirm
  `ReverseProxy:KnownProxies` matches the frontend IP. Clear site cookies once when
  switching transport/key rings, then reload. Never delete the key ring to fix login.
- **Antiforgery cache-header warning:** token responses must use `no-cache, no-store`
  and `Pragma: no-cache`. The API sets those headers consistently. This warning in
  older images is not evidence that token validation failed.
- **Restore worker unavailable:** the standard API image has no Docker CLI/socket.
  Leave `RestoreTests:Enabled=false` unless using the `restore-runner` target and
  configured dedicated daemon access. Setting it true alone cannot enable execution.
  The warning includes a fixed safe reason, logs once per failure change and retries
  durable work every 30 seconds. A recovery message confirms resumed processing.

The repository's private `testcompose.yml`, when present, targets an HTTPS edge
proxy with Secure cookies and Docker restores disabled. It includes loopback health
probe hosts as well as the configured public hostname. Do not publish its private
configuration. Rebuild/redeploy updated images to apply code changes; the publishing
helper must be run on your registry account if other hosts pull those tags.

## Upgrade and recovery

Stop the API and take a consistent offline copy of the full data directory,
including metadata, backups, key ring, admin settings and SQLite WAL/SHM where
applicable. Keep external `CRYPTO_KEY` and recovery passwords separately. Startup
applies retained additive EF migrations; bootstrap never overwrites existing users.
Use one replica per data directory.

Queued work survives restart; running/stopping jobs become Interrupted and require
inspection before retry. Hard crashes can leave orphan exports or SQL Server staging
files. Retention deletes older artifacts only after a durable successful replacement.
Preview/audit rows outlive artifact retention. Check disk space and practice recovery.

Moving from the previous TLS frontend: point the edge at HTTP container port 8080,
remove web certificate mounts, update host upstream port as needed and recreate both
containers. The old `certs/` directory may contain SSH pins; preserve and explicitly
mount those pins if used. Do not rotate `CRYPTO_KEY` or delete `/data/keys` as part
of this deployment change. [Encryption and offline recovery](BACKUPS.md).
