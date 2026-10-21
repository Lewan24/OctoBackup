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
| A04 Cryptographic failures | AES-GCM new secrets; random nonces; fail-closed tamper detection; private environment key; SSH SHA256 pins; verified database TLS defaults; persistent cookie keys | Legacy CBC reads remain for existing data. Backups and cookie key files rely on filesystem/volume encryption and permissions. |
| A05 Injection | EF queries, provider connection builders, argument-list process execution, environment-only dump passwords, escaped SQL Server identifier and parameterized file path, constrained database/path segments, React text escaping | Dump processes still require trusted binaries on PATH. Protect OS process environments from other users. |
| A06 Insecure design | Admin-managed connection configuration, explicit unsupported operations, write-only secrets, serialized backups, retention only after durable replacement | Single process deployment; no distributed job queue. Restore testing remains a release follow-up. |
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
persistence. Logs record actor/revision and policy values, never secrets. Startup
settings (keys, storage, TLS/proxies) are not writable through this API.

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
