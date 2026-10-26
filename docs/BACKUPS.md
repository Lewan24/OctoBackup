# Backups, encryption and restore testing

## Engine support

| Target | Export and retrieval | Test restore | Compatibility limits |
| --- | --- | --- | --- |
| MySQL | `mysqldump` SQL, ZIP, optional encryption | Compatible version in disposable Docker container | Grants must include exported routines/events/triggers. |
| MariaDB | Existing MySQL provider detects MariaDB dump clients; SQL, ZIP, optional encryption | MariaDB-specific client/image selected from the archive header | Choose **MySQL / MariaDB** in the connection form. |
| PostgreSQL | `pg_dump` SQL without owners/ACLs, ZIP, encrypted file | Compatible source/dump-client major in Docker | Roles/permissions/extensions need separate compatibility assessment. |
| Microsoft SQL Server | Native `BACKUP DATABASE ... WITH COPY_ONLY, INIT, CHECKSUM`; API optionally encrypts the resulting shared file | Native `.bak` in SQL Server Docker runner | Requires shared staging storage and service-account permissions; a successful SQL connection is insufficient. |
| SQLite | Online backup API from a deployment-approved local source file; optionally encrypted `.sqlite` snapshot | Reopen snapshot read-only, integrity check and bounded table preview | Supports committed WAL data; no arbitrary filesystem paths or SSH transport. This is a snapshot validation in the API process, not a container isolation test. |

These are database backups, not arbitrary filesystem backups. SQLite's database
file and WAL/SHM directory must be accessible to the API, with the filesystem locking
semantics SQLite requires. A bare remote copy of a live `.sqlite` file is unsafe.
Encrypted SQLCipher databases and custom SQLite extensions are not supported.
Microsoft SQL Server native backups cannot be streamed back through a normal SQL
query. Shared storage is the implemented retrieval method. A future SFTP/object-store
adapter would need deployment-owned paths, existing SSH pins, atomic retrieval,
verification and cleanup; no `xp_cmdshell` or SQL service privilege expansion is needed.

## First run and key management

Global backup encryption starts **disabled** on fresh installations and upgrades.
Startup creates a persisted disabled policy; it does not generate a backup password.
Only administrators can change **Application settings > Global backup encryption**.
Enabling requires a matching custom password and confirmation (16–512 characters).
Every enable, including re-enabling, requires entering a custom password. Disabling
requires administrator access, but does not require the old password or erase keys.
Global key rotation while enabled requires the old password unless an administrator
explicitly selects reset. Revision IDs reject stale changes. No deployment setting
or server connection form can turn on the global switch.

A server follows the global policy unless it has an individual password. Global off
means unencrypted new backups for inherited connections; global on means they use
the custom global key. An individual password always encrypts that server's new
backups, independent of the global switch. Operators with server access can manage
individual keys from **Servers > Backup encryption**. Connection administrators can
choose the encryption mode and password during server creation/editing. Edits keep
existing keys by default. Replacing/removing an individual password requires its
current password; administrator reset remains available in Application settings.
Connection/tunnel/key changes commit atomically, so invalid password confirmation
cannot leave a partially updated connection.

Data Protection protects versioned backup keys and new database/SSH credentials in
SQLite. Custom backup passwords are not stored or revealed. Legacy GCM/CBC secrets
remain readable with CRYPTO_KEY; Identity login passwords remain one-way hashed.
Preserve the Data Protection ring. Historical generated keys from earlier releases
remain available to administrators as **Reveal previous generated key**, exclusively
for recovering old backups; they do not enable global encryption.

All changes apply to **future backups only**. Existing encrypted files retain their
version IDs and keys for downloads/automated tests, even with global encryption off.
A new password cannot decrypt an older downloaded file. Save previous passwords
until their backups expire. Existing plaintext files are never relabeled encrypted.
Both new/old plaintext files are labeled unencrypted in history.

## Downloads and disaster recovery

Authorized users can download encrypted artifacts without knowing their password.
**Download decrypted** additionally requires the password/key for that artifact's
version, even for administrators. The password is sent in a CSRF-protected POST
body, never a URL or localStorage. The complete file is authenticated before the
response starts. Automated restore tests use protected stored keys internally.
The current browser download buffers the response as a Blob; very large decrypted
downloads need sufficient browser memory. Use encrypted download plus the offline
tool for large files.

```sh
dotnet run --project Backend/tools/DecryptBackup -- saved.zip.enc recovered.zip
dotnet run --project Backend/tools/DecryptBackup -- saved.bak.enc recovered.bak
dotnet run --project Backend/tools/DecryptBackup -- saved.sqlite.enc recovered.sqlite
```

The tool prompts without echo, authenticates all records, refuses to overwrite an
existing destination and removes partial output on failure. It needs only the
original encryption password and encrypted file, not an API session/database/key
ring. Protect its plaintext output appropriately.

`DataProtection:KeysDirectory` selects the key-ring directory (absolute or relative
to the API content root); null preserves `<Storage:DataDirectory>/keys`. Compose
sets `/data/keys` inside the existing persistent `octobackup-data:/data` volume,
so upgrades preserve the existing ring. Do not delete retired Data Protection keys.
Back up the whole metadata database, WAL/SHM when applicable, key ring, legacy
`CRYPTO_KEY`, and externally saved backup passwords. Losing the ring makes stored
keys/new connection credentials unreadable; startup refuses to replace existing
backup keys it cannot unprotect. Offline passwords can still recover downloaded
artifacts. Filesystem Data Protection keys are not automatically encrypted at rest
on Linux: restrict permissions and use volume encryption or an external key wrapper.

## Source storage configuration

SQLite is disabled as a source until `Sqlite:SourceDirectory` is configured to an
absolute directory outside application data. Mount only the intended SQLite source
directory into the API, for example `/sqlite-sources`, then set
`Sqlite__SourceDirectory=/sqlite-sources`. Enter only a basename such as `app.sqlite`
in the connection form. Directory traversal, symbolic links, application storage
and SSH mode are rejected. SQLite source mounts are deployment configuration,
not runtime administrator host allowlists. Do not broadly mount `/` or home folders.
`Sqlite:RestoreTestsEnabled` defaults true and can disable snapshot validation;
it does not require `RestoreTests:Enabled` or Docker when the Docker runtime is off.
The current SQLite backup API is synchronous; cancellation is checked before and
after copying, so an active snapshot can delay cancellation acknowledgement.

The recommended dedicated staging mode uses `SqlServer:LocalStagingDirectory` and
`SqlServer:RemoteBackupRoot`; see [connection/storage setup](DATABASE_CONNECTIONS.md).
With staging unset, SQL Server's `SqlServer:RemoteBackupRoot` optionally maps the API's backup root to
its path on the database host. For example, API `/data/Backups` and SQL Server
`D:\OctoBackup` must refer to the **same shared directory**; the provider appends
the same generated relative subdirectories. Set the option to `D:\OctoBackup`.
Null retains identical absolute paths. Provision the share, subdirectory visibility,
and SQL service-account write permission first. This does not retrieve arbitrary
remote files or provision mounts. The optional SQL Server Compose profile does not
set up this share automatically.

## File protection and limits

The v1 `OCTOENC1` format uses PBKDF2-HMAC-SHA256 (600,000 iterations, 16-byte key
salt), per-file HKDF-SHA256 keys, AES-256-GCM, and 1 MiB records. Its 64-byte header
contains magic (8), .NET Guid bytes (16), PBKDF2 salt (16), HKDF salt (16), and nonce
prefix (8). Each record is a big-endian length (4), ciphertext, and GCM tag (16).
Nonce = prefix plus big-endian record index (4). AAD = header plus record length.
A mandatory authenticated zero-length final record detects truncation; trailing,
reordered, damaged and substituted-key records fail. File size remains visible.

Exports/compression and native SQL Server output temporarily contain plaintext
before encryption; decrypted downloads/tests use private DeleteOnClose work files.
Normal errors/cancellation remove partial output and disposal removes decrypted
files. Startup removes recognized leftover decrypted work files. Dump/ZIP/SQLite
work files created by the API use owner-only Unix file permissions. Protect SQL
Server's service-created staging files with share ACLs. Hard crashes/disk failures
can still leave export/orphan files; filesystem encryption, restricted storage and
operational inspection remain necessary. Saved restore previews contain sampled
plaintext in metadata and follow the database's access/retention policy.

References: [SQLite online backup API](https://www.sqlite.org/backup.html),
[.NET Data Protection overview](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction).
The Data Protection ring is an application key wrapper, not the portable backup
format; retain it indefinitely while protected keys are needed.

## Restore testing

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

### Checks and saved preview

- Inspect at most 64 KiB of the single SQL entry before creating a container.
  Recognized format/metadata mismatches fail explicitly. PostgreSQL selects a
  configured image for the greater source-server/pg_dump major; MySQL/MariaDB select
  separate images and clients for the source release. Check the running server
  version before importing. Header text never supplies image names or commands.
- MySQL/PostgreSQL: stream the ZIP's single SQL entry to the engine client. Archive
  paths are never extracted onto the host. SQL Server: stream the native `.bak` into private tmpfs as the non-root SQL user, run
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

### Runtime, isolation and compatibility

The standard API image has no Docker access. Explicitly enable the opt-in runtime
as described in [OPERATIONS.md](OPERATIONS.md#restore-testing). Image references
and resource limits are deployment-owned. Images must be preloaded (`--pull=never`);
prefer release-reviewed digests. No browser input selects images or commands.

Runtime resource settings under `RestoreTests`:

| Setting | Default | Bounds |
| --- | --- | --- |
| `TimeoutSeconds` | 1800 | 30–7200 |
| `MemoryMiB` | 4096 | 2048–65536 |
| `DataGiB` | 4 | 1–64 |
| `MaxArchiveGiB` | 10 | 1–100 |

Values are clamped; tmpfs counts against memory. `Automatic` only seeds the admin
policy before a snapshot is saved. PostgreSQL requires at least the source and
dump-client major; never map an incompatible older version to make a test start.

Containers have no network, published ports or host mounts, a read-only root,
no-new-privileges, default seccomp, 256 PIDs, two CPUs and bounded memory/swap.
Database data lives on size-bounded tmpfs, alongside bounded `/tmp` and `/run`.
MySQL/PostgreSQL initialization receives only CHOWN, SETUID, SETGID, DAC_OVERRIDE
and FOWNER capabilities; SQL Server receives only NET_BIND_SERVICE because the
official sqlservr binary declares that file capability and otherwise fails exec.
All capabilities are dropped before these explicit additions; network isolation
and no-new-privileges remain enabled. Container logs are disabled.
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

### API and reproducible checks

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
for a live engine compatibility matrix.

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

### Encrypted backups and SQLite snapshots

New artifacts are decrypted internally with their saved key version before running
the existing restore adapters. Complete authentication precedes the test; private
work files are removed on disposal and recognized leftovers on startup. Password
rotation does not break old tests. Legacy plaintext archives remain readable.
SQLite uses a read-only integrity check and bounded table preview of the snapshot,
without Docker when its runtime is disabled; `Sqlite:RestoreTestsEnabled` can turn
this off. It is an in-process validation, not a container restore. See
[backup encryption and provider limits](BACKUPS.md).

Encrypted-format smoke check with a locally preloaded compatible image:

```sh
OCTOBACKUP_SMOKE_POSTGRES_IMAGE=postgres:17-alpine dotnet run --project Backend/tests/RestoreSmoke -- postgres --encrypted
```

Native SQL Server readiness uses explicit `tcp:127.0.0.1,1433` to avoid DNS in the
networkless runtime. The ODBC sqlcmd uses `-y 0` without incompatible `-h`/`-W`
options. Native files are streamed with a size bound into writable tmpfs, avoiding
Docker daemon refusal to `cp` into read-only containers. The fixed transfer command
contains no archive-supplied path or shell text and creates mode-0600 files as the
ordinary SQL user; no root exec or host mount is required.

Restore image maps, limits and enablement reload from watched appsettings JSON.
Each scoped runner retains its settings for an active test, and cleanup does not
skip a created container when enablement changes. Pull the exact mapped image/tag
on the runtime's daemon before retrying; adding a mapping does not download it.
See [configuration reload and image preloading](OPERATIONS.md#configuration-reload).
