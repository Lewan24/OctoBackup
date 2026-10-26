# Security model

Trust boundaries include untrusted browsers, operator accounts, administrator-owned
connection configuration, database hosts, the container runtime and host storage.
The controls below do not constitute a penetration test or security certification.

## Web transport and sessions

- The deployment edge terminates HTTPS, owns certificates and redirects HTTP. Both application containers use private HTTP without certificates or HTTPS middleware.
- Nginx overwrites forwarded scheme/address; the API accepts one forwarding hop from explicit trusted IPs. Do not enable unrestricted forwarded-header trust.
- Production session and CSRF cookies are Secure, HttpOnly and SameSite Strict by default. Local HTTP uses Development; the explicit startup option `Security:AllowInsecureHttp=true` removes Secure for isolated private HTTP testing. Keep it false for public deployments. Browser CSRF request tokens stay in memory; no second auth token or localStorage secrets exist.
- Every API mutation, including login, requires CSRF. Same-origin requests need no permissive CORS.
- CSP, content-type/frame/referrer/permissions headers and host filtering apply. The edge supplies HSTS.
- Identity uses a 12-character password policy, lockout and per-IP rate limits. Each authenticated request rechecks blocked/confirmed state and security stamp.
- In the default two-proxy chain, API rate limits group clients by edge address. Apply client-IP limits at the public edge; do not trust caller-supplied address headers.

## Authorization and network policy

Connection configuration and user/policy administration require the Admin role.
Backup/download/restore/schedule actions require current server access. Queue owners
and administrators manage manual requests; assigned peers cannot cancel another
operator's manual work. Scheduled requests follow assignment rules. Execution
rechecks account, assignments, unchanged target and approved destinations.

Exact database/SSH hostname/IP allowlists reject URLs, wildcards, paths and ports.
Empty means deny all. Removing a host blocks subsequent operations on existing
connections; active operations can finish. DNS approval does not prevent rebinding
or constrain ports/private-network destinations: use host/container egress controls.
Preserve database CA verification and verified SSH SHA256 pins. Web certificate
removal does not change either provider safeguard.

SignalR exposes authenticated payload-free invalidations scoped to affected users
and administrators. Clients cannot publish. Existing connections may receive
invalidations until disconnected after account changes; periodic principal
revalidation remains proposed work.

## Secrets and backup data

New database/SSH secrets and backup keys use purpose-isolated Data Protection.
Legacy GCM/CBC secrets remain readable with the external `CRYPTO_KEY`. Login
passwords are hashed. Secrets are write-only in forms; blank edit values preserve
existing credentials. API responses do not expose passwords or physical backup paths.

Global backup encryption starts disabled. Individual passwords apply independently.
Encrypted artifacts use authenticated chunked AES-256-GCM; historical key versions
remain for downloads/tests. Decrypted downloads also require the original artifact
password. Resetting a key affects future files. [Format and recovery](BACKUPS.md).

Preserve the entire data directory, key ring, external key and recovery passwords.
Linux filesystem Data Protection keys are not automatically encrypted at rest.
Restrict permissions and encrypt storage. Exports, SQL Server staging, decrypted
work files and saved preview samples can contain plaintext. Errors clean up partial
work; hard crashes can leave orphans. Preview/audit rows outlive artifact retention.

Never rotate `CRYPTO_KEY` by simply replacing its value on an existing store. Legacy
credentials require migration with the old key first. If an older deployment used
public development keys, rotate affected credentials through a reviewed migration.
Do not reuse repository history as a secret store.

## Input and execution boundaries

EF/provider parameterization, argument-list processes, escaped SQL Server database
identifiers and parameterized destinations constrain injection. Dump passwords are
passed through process environments rather than command-line arguments. Protect
service-account process access. Raw SQL, provider output and credentials never
enter public diagnostics; stderr capture is bounded and classified.

File destinations reject traversal and symlinks. SQLite sources are restricted to
a narrow deployment-owned mount outside app data; no SSH or arbitrary paths.
Read-only SQLite validation disables extensions/trusted schema and runs in-process.
SQL Server staging is a separate share; never expose metadata or keys to the source.

Docker restore testing is explicit opt-in. The standard API has no socket; the
runner target requires privileged daemon access. Use a dedicated daemon/VM.
Restore containers have no network/ports/host mounts, bounded resources, read-only
roots, explicit minimal capabilities and no-new-privileges. Images are deployment
owned and preloaded; dump metadata cannot select arbitrary commands/images.
[Isolation and compatibility limits](BACKUPS.md#restore-testing).

## Deployment and maintenance

Development can generate ignored private local settings only for a fresh store;
Production ignores/excludes them and requires supplied secrets. Runtime JSON reload
retains last-valid data, omits values from parser warnings and freezes startup key,
storage and web security settings until restart. Admin snapshots use bounded
validation, revisions and atomic persistence.

Use one API replica per store. Back up before migrations and practice recovery on a
copy. Container roots are read-only and non-root. Keep host, dump clients and engine
images patched; audit npm/NuGet dependencies and scan/pin release images. CI/API/
browser tests do not replace deployment fault injection or provider restore drills.

Review registration and automatic confirmation before exposing a deployment; the
base configuration enables both. Mailbox ownership, MFA, password recovery and
email delivery are not implemented. Keep sign-ups administrator controlled where
those guarantees are required. Bootstrap only creates an administrator on an empty
user store; matching a registration email never grants administrator privileges.

## Reporting vulnerabilities

Report privately to the repository maintainer through the hosting platform's private
security advisory facility where available. Do not post credentials or backup data
in public issues. No monitored incident-response mailbox is promised by this repository.
