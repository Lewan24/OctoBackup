# Security model and verification

This is an OWASP Top 10 hardening implementation, not a certification or a claim
that every possible vulnerability has been eliminated. Threat model: untrusted
browser input, cross-site requests, compromised operator accounts and database
connections configured by trusted deployment/application administrators. Host OS,
container runtime and external databases remain separate trust boundaries.

The mapping follows [OWASP Top 10:2025](https://owasp.org/Top10/2025/).

| Category | Implemented controls | Verification / operational limits |
| --- | --- | --- |
| A01 Broken access control | Authenticated route groups; explicit role policies (case-insensitive routes included); admin filters; server ownership checks for backup/test/download; source and destination checks for schedule moves; approved hostnames; no public container execution | API cross-user/admin tests. Hostname allowlists need network egress restrictions to mitigate DNS rebinding and constrain ports/private networks. |
| A02 Security misconfiguration | Same-origin frontend proxy, explicit trusted proxy IPs, no permissive CORS, strict HttpOnly cookies, HTTPS/HSTS in production, CSP and security headers, host filtering, no bundled certificate/private key | Production requires TLS configuration and explicit hosts. Development HTTP is loopback-only by run configuration. |
| A03 Software supply chain failures | Lockfile-backed npm installs, NuGet vulnerability warnings fail restore/build, patched SSH/SQLite/crypto/email dependencies, CI audits | Re-run audits regularly; new advisories can appear after this change. Container images must also be scanned and pinned by release automation. |
| A04 Cryptographic failures | Data Protection new secrets; AES-256-GCM backup files; random nonces; fail-closed tamper detection; private environment key; SSH SHA256 pins; verified database TLS defaults; persistent cookie keys | Legacy CBC reads remain for existing data. Legacy plaintext backups, transient staging files and the Data Protection ring still require filesystem/volume encryption and permissions. |
| A05 Injection | EF queries, provider connection builders, argument-list process execution, environment-only dump passwords, escaped SQL Server identifier and parameterized file path, constrained database/path segments, React text escaping | Dump processes still require trusted binaries on PATH. Protect OS process environments from other users. |
| A06 Insecure design | Admin-managed connection configuration, explicit unsupported operations, write-only secrets, serialized backups, retention only after durable replacement; opt-in isolated restore tests with scoped saved samples and deployment-owned version mappings | Single process deployment; no distributed job queue. Restore runtime requires dedicated trusted Docker access; a synthetic SQL Server 2022 native flow passed; broader schema/version validation remains pending. See RESTORE_TESTING.md. |
| A07 Authentication failures | Identity password hashing, 12-character password policy, login lockout, per-IP rate limiting, uniform login failures, admin-managed registration/confirmation, bootstrap admin only on an empty user store, password reauthentication for email changes | Initial appsettings enable registration/auto-confirmation; saved admin settings take precedence. MFA, recovery and proof of mailbox ownership are not implemented. |
| A08 Software/data integrity failures | CSRF on all API mutations including login; authenticated SignalR with no client publish methods; payload-free scoped events; filesystem root/symlink checks | OS write access to backup storage is trusted. Add backup checksums and signed provenance in a later phase. |
| A09 Security logging/alerting failures | Login and admin/service events; structured exception logs with trace IDs; error responses hide stack traces and provider secrets | External collection, retention, alert rules and delivery are deployment responsibilities; no claim of a built-in SOC/alert service. |
| A10 Mishandling exceptional conditions | Problem responses, bounded request bodies and dump timeouts, concurrent stderr drain, durable failed-job status and explicit retry, partial dump cleanup, loading/error/pending UI states | Provider outage, disk-full and complete restore drills still require operational testing. |

## Session and browser rules

Queue mutations require authentication and CSRF. Administrators manage every job;
operators manage their own requests and scheduled requests on assigned servers.
Assigned peers may view but cannot cancel another operator's manual job. Execution
rechecks current account status, assignments, approved hosts and unchanged target.
Listings are paginated and scoped; unauthorized request details, credentials and
physical artifact paths are never returned. Admission caps active jobs at 1,000.
Database uniqueness prevents simultaneous requests for the same target. Running
cancellation propagates to dump processes, compression and provider calls; synchronous
SSH setup can delay acknowledgement until its bounded timeout. Remote SQL Server
cleanup and hard-crash orphan cleanup require operational inspection. Remove keeps
hidden audit rows rather than deleting evidence or completed backup files.

- Browser credentials live only in HttpOnly cookies. No tokens/passwords are stored
  in localStorage. CSRF tokens stay in memory and reset when identity changes.
- Every authenticated HTTP request rechecks blocked/confirmed state and security
  stamp. Password/admin account changes invalidate old cookies.
- Existing SignalR connections cannot execute commands or receive secrets, but a
  previously established connection may receive invalidations until disconnected;
  follow-up work should add periodic hub identity revalidation.
- Administrator privileges are never granted by matching a registration email.
  Bootstrap credentials are only consumed when there are no users.
- Cookie data-protection keys are persisted under the data directory. Restrict
  directory permissions and encrypt volumes. Do not share keys with other apps.

## Upgrading an existing installation

Runtime administration settings are restricted to authenticated Admin-role users
and CSRF-protected writes. Values have bounded ranges and exact host validation;
wildcards, URLs, paths and ports are rejected. Host removal is enforced before new
manual/scheduled backup and connection-test execution, including existing records.
Running jobs retain their original operation. DNS/IP approval does not replace
network egress controls or SSH pins. Concurrent edits use revisions and atomic
persistence. Logs record actor/revision and policy values, never secrets. Deployment
settings (key-ring storage, source mounts, TLS/proxies) are not writable through this API. Backup encryption keys have separate authorized endpoints.

Dump-tool errors are classified into fixed actionable messages; raw stderr is
discarded after bounded capture and never attached to logs/API errors. Database
passwords remain environment-only. Failed exports remove partial output, including
process-start failures and timeouts. MySQL backup options retain routines/events,
avoid granting global flush privileges for per-database archives, and never weaken
requested TLS verification or retry by dropping schema objects.

Local Development creates a private ignored `appsettings.Local.json` with a random
key/bootstrap password, importing an existing repository `.env` key when available.
It never replaces an existing local file and refuses to invent a new key for an
existing SQLite store. User-secrets, environment and CLI values retain precedence.
This file is excluded from publishing and Docker builds; Production ignores it
and requires explicitly supplied secrets. Protect local file permissions and keep
the key with your recovery material.

Back up the SQLite file, backups directory, current encryption key and cookie keys
before deployment. Test the upgrade on a copy. The old repository included a
development AES key and TLS certificate: treat any installation using them as
compromised secrets. Generate a private TLS certificate and rotate database/SSH
credentials. To change `CRYPTO_KEY`, re-encrypt existing stored secrets with the old
key before switching; simply replacing the variable makes old secrets unreadable.
A dedicated offline rekey utility is still planned. Do not reuse repository history
as a secret store.

The application authenticates administrators but does not validate ownership of a
new email mailbox. Keep accounts under administrative control until verified email
flows and recovery are implemented. Do not enable public auto-confirm registration.

## Reporting a vulnerability

Report privately to the repository maintainer through the hosting platform's private
security advisory facility where available. Avoid posting credentials or backup
contents in public issues. Do not assume this repository provides a monitored
incident-response mailbox.

## Backup encryption and key versions

See [backup encryption](BACKUP_ENCRYPTION.md). New files are encrypted before publication when global encryption is enabled or
the connection has an individual key. Global encryption defaults off; only an
administrator can enable it with a confirmed custom password. Individual keys are
available independently and in atomic connection create/edit forms. Decrypted downloads require resource access plus the original backup
password; administrator reset only changes future backups. Protected historical
keys remain for automated restores. Identity passwords remain hashed. Data Protection
protects new connection secrets and backup keys; legacy credential reads remain.
SQLite sources are restricted to a deployment-owned mount outside app data, with
no symlinks/SSH/arbitrary paths. Snapshot validation is read-only in-process with
extensions/trusted schema disabled; it is not Docker isolation. Export staging and
saved restore previews contain plaintext; retain filesystem/volume protection.

Source Docker databases need only a shared network with the API, not a daemon socket
or published port. SQL Server's optional staging share is separate from metadata/
keys and final encrypted storage. Provider diagnostics expose only fixed error
categories. The isolated SQL restore allows only NET_BIND_SERVICE (required by the
image's executable file capability) after dropping all capabilities; non-root,
no-new-privileges, networkless and read-only-root restrictions remain. Native
restore input is bounded and streamed into tmpfs without root exec.

Reloadable JSON never logs values or parser exceptions. Storage roots, legacy crypto
keys, Data Protection locations and host security setup remain frozen until restart;
malformed/missing files retain their last valid snapshot. Runtime executable and
image paths remain deployment-owned. Image execution still uses --pull=never on the
selected daemon; image downloads remain an explicit administrator action. The new
configuration-directory Docker mount is read-only and exposes no Docker control API.
