# Implemented behavior and follow-up work

## Current migration

.NET 10 host; root Backend/Frontend split; React dashboard, connections and SSH
forms, connection tests, manual backups, access grants/revocation, schedule CRUD,
backup history/downloads, profile changes and administrator user management.
Application settings manage database/SSH allowlists, public registration and
confirmation, new-server retention defaults and schedule polling without restart.
Manual and scheduled work use a persistent, delayed queue with scoped status,
duplicate prevention, cancellation/removal and administrator timing controls.
Authentication and security controls are detailed in SECURITY.md. Test evidence is
recorded in MIGRATION.md; distinguish code support from live-provider verification.

## Release validation still requiring an external environment

- Non-root API/frontend container build/run smoke test passed. Container image vulnerability scanning remains a release task.
- PostgreSQL/MySQL representative-row export/download/restore smoke tests passed. For full schemas and SQL Server: export,
  download, restore independently and compare contents, constraints and routines.
- Verify TLS CA trust, SSH host-key mismatch rejection and database versions.
- Exercise disk-full, backup timeout, retention and recovery using a copy of data.

## Next implementation priorities

1. Isolated restore-verification jobs with bounded resources, no Docker socket in
   the web host, persistent results and per-provider restore checks. Current UI
   truthfully reports unverified backups and unsupported APIs return 501.
2. Offline credential rekey utility and migration of all legacy CBC ciphertext.
   Add optional backup encryption, checksum/provenance and audited restore/download.
3. Verified email change, password recovery, MFA and account invitation lifecycle.
   Integrate the retained SMTP helpers into explicit runtime workflows and tests.
4. Server-side backup-history filtering/paging,
   per-existing-server retention editing and timezone-aware scheduling with DST tests.
5. Distributed worker coordination if multiple replicas are required, orphan-file
   reconciliation after hard crashes, and configurable retry policies;
   hub principal revalidation for long-lived connections.
6. Generate browser types/API reference from backend contracts, localize English/
   Polish UI, and add deeper accessibility automation and provider test CI.

Do not mark these complete without implementation and verification. Do not restore
the retired anonymous container prototype, fake-success endpoints or custom browser
token scheme to make a planned feature look finished.
