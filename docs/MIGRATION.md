# Migration progress

## Objective

Upgrade OctoBackup to .NET 10 first, replace Blazor WebAssembly with React,
separate Backend and Frontend, preserve useful workflows, and harden the application.

## Work sequence

- [x] Upgrade .NET targets, Microsoft packages and build tooling to 10.
- [x] Move API/modules to Backend and retire the Blazor client.
- [x] Fix authentication, CSRF, resource authorization and realtime notifications.
- [x] Harden encryption, external processes, paths and configuration.
- [x] Implement React dashboard, servers/tunnels, schedules, history, account and administration.
- [x] Add regression tests and validate builds, dependencies and browser behavior.
- [x] Update architecture, security, operations and English/Polish README documentation.

## Baseline findings

The original modular monolith contains Auth, Administration, Backup, Crypto and
experimental BackupsTests modules. Identity and backup metadata use SQLite with EF
migrations. A hosted worker polls schedules every five minutes. MySQL/PostgreSQL
exports are compressed; SQL Server backups are created by the remote SQL server.
The client uses cookies plus a custom secondary token and SignalR notifications.

Verified issues: incomplete per-server authorization; token retrieval not bound to
the caller; predictable token generation; public Docker test endpoint; public hub
broadcast methods; client notifications implemented through leaked self-connections;
fake-success backup operations; string-built process arguments; unauthenticated AES
CBC; registration-based administrator assignment; permissive development CORS;
weak password requirements; ignored environment configuration precedence.

## Verification log

Validated on 2026-10-21:

| Check | Result |
| --- | --- |
| .NET 10 solution build | Passed |
| API/security/configuration tests | 33 passed, including authorization/session, runtime settings, dump compatibility and process lifecycle regressions |
| React unit/component tests | 4 passed |
| TypeScript and production frontend build | Passed |
| Desktop and mobile Chromium workflows | 2 passed |
| Release API publish | Passed; output in ignored `Backend/artifacts/publish` |
| npm audit | 0 vulnerabilities |
| NuGet direct/transitive audit | No vulnerable packages reported across 19 projects |
| UI mechanical detector | No findings |
| Desktop/mobile screenshot review | Completed; fixed sidebar overflow and CSP font loading |
| Git whitespace validation | Passed |
| Compose configuration validation | Passed without starting containers |

Security tests cover anonymous denial, login CSRF, removed dangerous endpoints,
cross-server backup/test/download denial, admin-only operations (including mixed-case and trailing-slash routes), schedule move
validation, password and blocked-account session invalidation, credential tamper
rejection, legacy CBC readability and path traversal rejection. Browser tests cover
sign-in, server create/edit with blank secret fields, schedule creation/listing,
empty history, administrator listing and sign-out.

At the initial migration pass the Docker engine was unavailable. Container build/run and real MySQL,
PostgreSQL/SQL Server export/restore validation were **not performed then**. The follow-up below supersedes this for PostgreSQL/MySQL; SQL Server remains a release gate. These are
release gates in ROADMAP.md, not passing tests. This is not a penetration test.

## Implementation notes for the next session

- .NET targets and Microsoft dependencies are on 10; global SDK starts at 10.0.302.
- Retained EF migrations plus a non-destructive .NET 10 Identity snapshot migration.
- Root Backend/Frontend are the only active application sources. Any ignored
  contents remaining under DbBackupService are local IDE/build caches, not source.
- React uses feature folders, self-hosted Manrope, responsive operations screens,
  real API data and scoped SignalR invalidation.
- Replaced secondary browser tokens with cookies and in-memory CSRF; removed
  public Docker execution and client broadcast methods.
- Added admin-only connection configuration, exact host allowlist, resource checks,
  authenticated encryption, TLS verification, SSH pins, safe processes and paths.
- Manual/scheduled jobs use a persistent delayed queue; successful replacement
  precedes retention. Accepted schedule occurrences advance atomically; failed
  requests require explicit retry. Unsupported restore-test/delete APIs return 501.
- Project Dockerfiles, root Compose files and CI build the split app. Production requires private key,
  TLS, trusted hosts and persistent data. See OPERATIONS.md.
- Legacy CBC reads, unencrypted backup files, unwired SMTP helpers, email proof/MFA,
  distributed jobs and restore verification remain explicit follow-up work.
- Vite cannot reliably parse `#` in this checkout path. Frontend npm scripts stage
  an isolated temp copy, preserving source and returning build/test artifacts.
- Initial UI seed service was unavailable; the user-approved sidebar/light-content/
  teal direction governed the implementation. No generated visual comps were used.

The working tree is intentionally uncommitted; user-provided skill/agent folders
were preserved. Update this file with new evidence when follow-up work changes it.


## Deployment and workspace cleanup follow-up

Moved technical documentation/Polish README into docs/, diagrams into docs/diagrams,
and the dotnet local-tool manifest into .config/. Root README and AGENTS.md remain
for discovery/tooling. Generated publish output moved to Backend/artifacts/publish.
Separate Backend/Dockerfile and Frontend/Dockerfile replace the combined root image.
compose.yaml runs the private API behind a TLS Nginx/React frontend; compose.dbs.yaml
runs independent database targets and can also merge with the app Compose file.
Added explicit trusted forwarded headers, API-only default hosting, configurable
Vite proxy, IConfiguration-backed crypto key and private console initialization/
launch scripts. See OPERATIONS.md for commands and deployment test evidence.


Follow-up verification completed:
- Separate API/frontend images built and ran healthy behind HTTPS Nginx.
- PostgreSQL 16 + MySQL 8.4 connected and exported through the API; downloaded ZIP
  dumps restored into independent disposable databases with matching sample rows.
- Secure browser login, CSRF and SignalR passed through the proxy.
- Console API/Vite login passed; application data stays under Backend/data.
- Corrected the reserved proxy IP/dynamic allocation range and MySQL TLS-opt-out
  caching_sha2 compatibility. Verified the local dump account's metadata grant.
- Compose app-only, databases-only (including optional SQL Server profile) and
  merged configurations parse successfully. SQL Server runtime remains untested.
- Removed only octobackup-check test resources; left private .env and generated
  publish/download files under ignored Backend/artifacts.

## Direct console startup fix

Reproduced plain `dotnet run` failing on missing CRYPTO_KEY: only the Node launcher
previously read `.env`. Added Development-only private local settings provisioning,
preserving existing encryption keys and bootstrap credentials. Base/development
appsettings now document storage, SSH pins, bootstrap and first-run settings.
Shared modules use the resolved configuration paths instead of separate environment
lookups. User-secrets/environment/CLI overrides and production secret requirements
are preserved. Existing registration/auto-confirmation settings were retained.

Verified plain `dotnet run` on port 5080, health, CSRF initialization and administrator
login without the launcher. Eleven backend tests pass, including five configuration
regressions for persistence, precedence, production isolation, existing-data key
protection and `.env` credential reuse.
Release publish passed with private local settings absent from output. Desktop
and mobile Chromium workflows both passed after the configuration changes.

## Compact dark frontend redesign

Replaced the previous light/green design with the user's requested compact dark
interface: ink-purple surfaces, violet gradients, lime actions, denser navigation,
forms and tables. Rebuilt the overview around real statistics, connection records
and enabled schedules; added manual refresh and actual SignalR connection status.
Updated sign-in, shared styles, engine accents and browser theme color. Transfer
motion is decorative, bounded and disabled with reduced-motion preferences.

Validated frontend TypeScript/production build, four unit tests and both real API
desktop/mobile workflow tests. Browser checks cover the configured dashboard,
refresh, live status, reduced motion, drawer Escape/focus behavior, horizontal
overflow and CSP errors. Reviewed desktop/mobile screenshots of dashboard,
connections, forms and sign-in. Mechanical design detector reported no findings.
Checked text/status/action/placeholder palette contrast (at least 6.65:1 across
the sampled pairs), and input boundary contrast (3.65:1 against the input surface).
No backend contracts, authorization or credential handling changed in this task.

## Runtime administrator settings

Added Administration > Application settings for exact database/SSH allowlists,
public registration and automatic confirmation, retention defaults for new server
configurations and worker polling. Settings use bounded validation, Admin-role
authorization, CSRF, atomic durable snapshots and revision-based concurrent edit
protection. Saved runtime values override deployment defaults and persist under
the application data volume. Deployment secrets, TLS/proxy trust and storage remain
outside the settings API. Public registration policy exposes only its two flags.

Host policies are checked during create/edit and before connection tests, manual
backups and scheduled jobs; removed hosts do not silently remain usable through
existing records. Default retention preserves existing server settings. Added an
explicit Confirm account action for administrator approval of pending operators.
The sign-in registration link follows the actual runtime policy.

Verification: 23 backend tests pass (including 12 new runtime-settings cases), four
frontend tests pass, TypeScript/production build passes, and both desktop/mobile
browser workflows pass. Browser flows approve a previously unlisted host, save and
reload settings, then create a connection using that host. Reviewed settings
screenshots at both sizes; mechanical design detector reported no findings.
Backend tests cover restart persistence, stale saves, validation, corrupt-file
handling, unauthorized/CSRF writes, SSH destinations, retention defaults, immediate
host revocation including scheduled work, registration and account confirmation.

Browser tests now run separate disposable API instances on ports 5087 and 5088;
the expanded shared-server test previously exhausted the legitimate per-IP budget.
Production rate limits were not relaxed. Application data used for all checks was
disposable. Per-existing-server retention editing remains in ROADMAP.md.

## MySQL export compatibility and diagnostics

Investigated the console host's MySQL Workbench mysqldump 8.0.43 and reproduced
exit code 2 against disposable MySQL 5.7 and 8.4 schemas containing a stored
procedure that the backup account could not inspect. Also reproduced exit code 2
on GTID-enabled MySQL 8.4: the original command requested FLUSH TABLES without a
global RELOAD/FLUSH_TABLES grant. The user's exact server version/grants were not
provided, so these are verified failure cases, not a claim about their server.

The provider now detects executable capabilities (including MariaDB aliases),
forces configured TCP host/port, disables supported column-statistics queries,
uses utf8mb4, and disables GTID_PURGED for per-database logical archives. The new
GTID options succeeded without expanding the test account's flush privileges.
TLS remains verified when requested. Routines/events/triggers remain included;
missing routine visibility produces an actionable permission message instead of
silently omitting schema objects. OPERATIONS.md documents version-specific grants.

DumpProcess concurrently drains stderr with an 8 KiB capture limit, classifies it
into fixed safe messages, terminates timed-out children and removes partial output
after failure/startup errors/timeouts. No raw provider text, query text, secret or
path is included in its diagnostic messages. Added portable process fixtures and
a console MySQL provider smoke harness to the solution; removed obsolete tool
availability probing.

Verification: 33 backend tests passed. Live C# provider exports on MySQL 5.7 and
8.4 restored table data and a stored routine, and both returned matching sample
rows. These are representative smoke checks, not full provider compatibility or
disaster-recovery certification. Production data and grants were not modified.

## Persistent backup queue (2026-10-22)

Replaced synchronous manual/scheduled exports with durable SQLite requests and a
single background executor. Added an additive EF migration, unique active-target
constraint, delayed eligibility, completion spacing, paginated scoped status,
owner/administrator cancellation and audit-preserving removal. Schedules use the
same queue and advance atomically on acceptance. Execution revalidates current
permissions/host policy/target, and successful metadata publication arbitrates
cancellation transactionally. Queued work survives restart; interrupted work is
visible for inspection instead of silently replayed.

The React shell now shows a real queue banner on every authenticated page, with
eligibility countdown and cancellation, plus a request history page. Connection
actions prevent duplicate requests; administrator settings expose both timing
controls. Dump processes, compression and database calls receive cancellation.
The Compose smoke script now polls the 202 response through completion.

Verification: 44 backend tests passed; seven frontend tests passed; TypeScript and
production build passed; desktop and mobile Chromium workflows both passed,
including enqueue, duplicate-disabled action, cancellation and removal. Backend
coverage includes active-target concurrency, row authorization/CSRF, execution-time
revocation, atomic publication/cancel races, process cancellation, schedule
coalescing/invalid configuration/stopping jobs, spacing and restart recovery.
All data used was disposable. Docker Desktop's Linux engine was unavailable in
this session, so a new live queued MySQL export was not executed. Earlier provider
export/restore smoke evidence above remains separate from these queue tests.

Reviewed queued-state screenshots at desktop/mobile sizes; the mechanical UI
detector reported no findings. npm audit reported zero vulnerabilities and the
direct/transitive NuGet audit reported no vulnerable packages across 21 projects.

Operational limits: one API process per data directory, no automatic failed-job
retry, local-time scheduling, possible hard-crash orphan files and best-effort
remote SQL Server cancellation/cleanup. Full provider restore drills and fault
injection remain release tasks; these checks are not a penetration test.
