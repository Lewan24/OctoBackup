# Implemented behavior and follow-up work

## Current migration

.NET 10 host; root Backend/Frontend split; React dashboard, connections and SSH
forms, connection tests, manual backups, access grants/revocation, schedule CRUD,
backup history/downloads, profile changes and administrator user management.
Application settings manage database/SSH allowlists, public registration and
confirmation, new-server retention defaults and schedule polling without restart.
Manual and scheduled work use a persistent, delayed queue with scoped status,
duplicate prevention, cancellation/removal and administrator timing controls.
Durable restore tests support automatic opt-in and manual saved data previews in
isolated, bounded Docker containers. Live real-export/restore checks passed for
MySQL 5.7/8.4, MariaDB 10.11 and PostgreSQL 16/17/18, including populated/empty tables,
Unicode, binary samples, hex exports and early invalid SQL rejection. Native
SQL Server 2022 now has a synthetic native export/encryption/restore check; broader version/schema matrices still require live validation.
Authentication and security controls are detailed in SECURITY.md. Test evidence is
recorded in MIGRATION.md; distinguish code support from live-provider verification.

## Release validation still requiring an external environment

- Non-root API/frontend container build/run smoke test passed. Container image vulnerability scanning remains a release task.
- PostgreSQL/MySQL representative-row export/download/restore smoke tests passed. For full schemas and SQL Server: export,
  download, restore independently and compare contents, constraints and routines.
- Verify TLS CA trust, SSH host-key mismatch rejection and database versions.
- Exercise disk-full, backup timeout, retention and recovery using a copy of data.

## Next implementation priorities

1. Move restore execution into a separately deployed worker with no daemon access
   in the web host. The current opt-in runner requires trusted dedicated Docker
   access from the API process. Validate real engine/version/extension matrices,
   additional native SQL Server versions/layouts and large schemas; add source manifests/checksums.
2. Offline credential rekey utility and migration of all legacy CBC ciphertext.
   Add checksum/provenance, historical backup re-encryption/revocation and external key wrapping. Optional global/individual backup encryption and versioned key changes are implemented; see BACKUP_ENCRYPTION.md.
3. Verified email change, password recovery, MFA and account invitation lifecycle.
   Implement email delivery with explicit runtime workflows and tests.
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

Opt-in global encryption, independent individual passwords in server create/edit,
versioned keys, password-confirmed downloads,
administrator resets and mounted SQLite online snapshots are implemented. Further
work: remote SQLite snapshot transport, SQL Server SFTP/object-storage retrieval,
large-download streaming in browsers, separate-process SQLite validation, and
broader native SQL Server versions, layouts and production-scale restore verification.

## Interface refresh — 2026-10-24

Implemented the user-directed carbon/ocean palette, larger typography and controls,
white octopus brand mark, summary cards and modal editing/confirmation flows.
Dialog regressions cover failed-action retry and prevention of pending dismissal.
Validation: 101 backend tests, 16 frontend tests, production build and 4 desktop/mobile
browser checks passed; npm and NuGet audits reported no vulnerabilities. Results
and limitations are recorded in docs/UI_REFRESH.md. No provider restore or
penetration-test claims are made by this visual refresh.

### Dark blue palette follow-up

Replaced turquoise surfaces/accents with the requested midnight/navy/cobalt palette
and gradients; retained larger typography, white logo and dialog workflows.
See UI_REFRESH.md for validation results.

Dark background follow-up: near-black canvas, darker navigation/sign-in backgrounds,
and larger logos (128px navigation, 104px sign-in). See UI_REFRESH.md for checks.

Backup history follow-up: separate restore status labels and Actions, with native
download menus and password-gated decryption. Validation is in UI_REFRESH.md.
