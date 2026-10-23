# Backup compatibility and encryption

## Engine support

| Target | Export and retrieval | Test restore | Evidence and limits |
| --- | --- | --- | --- |
| MySQL | `mysqldump` SQL, ZIP, encrypted file | Compatible version in disposable Docker container | Earlier live real-export tests: 5.7 and 8.4. Grants must include exported routines/events/triggers. |
| MariaDB | Existing MySQL provider detects MariaDB dump clients; SQL, ZIP, encrypted file | MariaDB-specific client/image selected from the archive header | Earlier live real-export tests: 10.11. Choose **MySQL / MariaDB** in the connection form. |
| PostgreSQL | `pg_dump` SQL without owners/ACLs, ZIP, encrypted file | Compatible source/dump-client major in Docker | Earlier live real-export tests: 16, 17 and 18. Roles/permissions/extensions need separate compatibility assessment. |
| Microsoft SQL Server | Native `BACKUP DATABASE ... WITH COPY_ONLY, INIT, CHECKSUM`; API encrypts the resulting shared file | Native `.bak` in SQL Server Docker runner | Live SQL Server 2022 synthetic export/encryption/native restore passed. Requires shared staging storage and service-account permissions; a successful SQL connection is insufficient. |
| SQLite | Online backup API from a deployment-approved local source file; encrypted `.sqlite` snapshot | Reopen snapshot read-only, integrity check and bounded table preview | Supports committed WAL data; no arbitrary filesystem paths or SSH transport. This is a snapshot validation in the API process, not a container isolation test. |

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
