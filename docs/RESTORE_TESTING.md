# Restore testing

Restore testing uses a durable background queue separate from exports. Manual
testing requires the same server access as backup/download; authorization and the
requesting account are checked again before execution. Tests never connect to the
source database or use its credentials.

Open **Backup history > Test / preview > Test backup**. The page shows Queued,
Running, Restore passed, Failed, Interrupted or Skipped, then a saved data preview.
Repeating a test makes the new result current; previous reports remain in SQLite.
Duplicates return 409; manual admission is capped at 100 active tests. Expired or
missing artifacts cannot be retested, but authorized users can read saved reports.

The administrator option **Automatically restore-test successful backups** defaults
off. Successful exports persist a test in the same transaction as backup metadata.
Turning it off skips waiting automatic tests; running tests finish and manual
tests remain available. Enabling does not backfill old backups. This global setting
supersedes the unused legacy per-server automatic-test configuration.

## Checks and saved preview

- Inspect at most 64 KiB of the single SQL entry before creating a container.
  Recognized format/metadata mismatches fail explicitly. PostgreSQL selects a
  configured image for the greater source-server/pg_dump major; MySQL/MariaDB select
  separate images and clients for the source release. Check the running server
  version before importing. Header text never supplies image names or commands.
- MySQL/PostgreSQL: stream the ZIP's single SQL entry to the engine client. Archive
  paths are never extracted onto the host. SQL Server: copy the native `.bak`, run
  VERIFYONLY and FILELISTONLY, then restore with all logical files moved into
  generated container paths. Ordinary D/L file layouts are supported.
- Require successful restore exit; PostgreSQL uses ON_ERROR_STOP, sqlcmd uses `-b`,
  and MySQL does not use `--force`. No user tables fails verification.
- Discover up to 1,000 user tables excluding system schemas; larger catalogs fail
  bounded inspection explicitly. Save the full discovered table-name inventory.
  Randomly choose up to 10 tables and read up to
  20 rows each, with at most 64 columns and 256 characters per cell. Row order is
  unspecified. Empty tables are explicit; an all-empty sample produces a warning.
  Schema-only backups can pass restoration without pretending data was found.
- Remove the container and volumes after inspection. The browser receives only a
  read-only snapshot: no database port/password or arbitrary SQL console is exposed.

MySQL `--databases` archives restore/inspect their recorded original database,
including CREATE DATABASE/USE and schema-qualified routines. Headerless logical
archives use the saved database name. System database names are rejected. MySQL
binary values are previewed as bounded hexadecimal, spatial values as WKT, and
text is converted to utf8mb4. NULL and JSON values remain explicit. Restore input
is byte-preserved, with binary mode enabled in MySQL/MariaDB clients. New exports
use `--hex-blob`; legacy binary exports remain supported without SQL rewriting.

Readable samples and successful restore exit do not prove source equality,
application validity, routine behavior or every constraint. Huge SQL files are
streamed rather than parsed into memory. Preview conversion of unusual
provider-specific types can fail; this is reported as an inspection
failure. No raw provider output or SQL is attached to public errors or logs.

Treat saved previews as sensitive backup data. Report responses use `no-store`.
Artifact retention currently does not purge preview/audit rows; include SQLite in
access reviews, storage sizing and recovery policy. Active tests protect artifacts
against export-worker retention.

## Runtime, isolation and compatibility

The standard API image has no Docker access. Explicitly enable the opt-in runtime
as described in [OPERATIONS.md](OPERATIONS.md#restore-testing). Image references
and resource limits are deployment-owned. Images must be preloaded (`--pull=never`);
prefer release-reviewed digests. No browser input selects images or commands.

Containers have no network, published ports or host mounts, a read-only root,
no-new-privileges, default seccomp, 256 PIDs, two CPUs and bounded memory/swap.
Database data lives on size-bounded tmpfs, alongside bounded `/tmp` and `/run`.
MySQL/PostgreSQL initialization receives only CHOWN, SETUID, SETGID, DAC_OVERRIDE
and FOWNER capabilities; SQL Server receives none. Container logs are disabled.
Disposable passwords are random and passed through process environments, not CLI
arguments. tmpfs data counts against container memory; size resources for the dump.

**Docker daemon access is privileged.** Mounting the socket, even read-only, grants
daemon control to the API process. Use a dedicated test daemon/VM, not a daemon
hosting sensitive unrelated workloads. The background worker currently lives in
the API process; a separately deployed worker remains a future hardening step.
Hostile dump/client features execute only inside the sandbox; patching the daemon
and host and scanning images remain necessary operational controls.

Use one API process per data directory. Container names/labels contain an
installation hash plus test UUID. Startup removes only matching labeled/generated
containers and marks previous Running tests Interrupted; Queued tests survive.
Cleanup has a separate 30-second deadline, even after restore cancellation. Cleanup
failure is recorded, and startup retries orphan removal. If recovery cannot access
Docker, queued reports explain that they are waiting for administrator attention.
Hard crashes or unavailable Docker can leave resources until recovery.

Versioned defaults map PostgreSQL 14/15/16/17/18, MySQL 5.7/8.0/8.4 and MariaDB
10.11/11.4 to explicit images (see appsettings.json). Headerless fallbacks remain
`PostgresImage`, `MySqlImage`, `MariaDbImage`. Unknown releases need an explicit
deployment mapping; missing images fail with a preload hint rather than silently
using another major or downloading images. SQL Server defaults to 2022 Developer.
Choose deployment mappings compatible with extensions, plugins, collations and
native formats. PostgreSQL uses explicit PGDATA and temporary mounts covering both
pre-18 and 18+ image layouts; MariaDB also gets its required socket directory.
No unsupported SET, routine, trigger, view or failing INSERT is dropped to make
verification pass. New PostgreSQL dumps omit
owners/ACLs, so production roles need not exist in the sandbox; those assignments
are outside the archive's scope. Old role-dependent dumps can fail. SQL Server
FILESTREAM, other non-D/L file layouts and encrypted backups requiring external
certificates need separate tooling. Old backups without engine snapshots use their
current connection's engine; new copies persist the engine at export time.

## API and reproducible live checks

- `POST /api/backups/TestBackup`, JSON backup GUID: 202 + report, requiring CSRF.
- `GET /api/backups/RestoreReport?id=<backup-guid>`: latest report or JSON null.
- `GET /api/backups/RestoreAvailability`: authenticated read-only deployment flag.
  The UI gates new tests and automatic-toggle activation on this flag; saved
  reports remain accessible. Missing reports always serialize as the literal `null`.
- Admin settings adds persisted `automaticRestoreTests`. Deployment
  `RestoreTests:Enabled` independently gates runtime availability.
- Numeric statuses preserve Running=0, Success=1, Failure=2; append Queued=3,
  Interrupted=4, Skipped=5. `TestedOn` is request time; `FinishedOn` is completion.

With a Linux engine running and selected images preloaded:

```powershell
dotnet run --project Backend/tests/RestoreSmoke -- postgres
dotnet run --project Backend/tests/RestoreSmoke -- mysql
dotnet run --project Backend/tests/RestoreSmoke -- sqlserver <disposable-native-bak>
```

Logical smoke checks generate disposable populated/empty tables, require a
`verified-row` sample, then verify corrupt SQL fails. The native fixture must contain
a `probe` table with `verified-row`. Each command cleans its test resources. Never
supply production artifacts to these fixture commands. API tests use a fixture
runner and browser preview tests use explicit route fixtures; neither substitutes
for this live engine matrix. Actual results are recorded in MIGRATION.md.

For real dump-tool compatibility checks (preload the matching image for each target):

```powershell
dotnet run --project Backend/tests/RestoreSmoke -- compatibility mysql57 mysql84 mariadb1011 postgres16 postgres17 postgres18
```

This seeds only disposable source containers, runs actual pg_dump/mysqldump/
mariadb-dump, restores the resulting ZIPs through the production runner, verifies
table inventories, empty tables, Unicode, NULLs, binary/JSON/spatial samples, and
requires corrupt SQL at the start of an 8 MiB stream to fail at the restore stage.
MySQL/MariaDB cover both legacy binary literals and new `--hex-blob` exports.
Views/routines are exported/restored without suppressing errors; this is not a
full semantic comparison of routine results or schema constraints. Every source
and target container is removed with independent cleanup deadlines.
