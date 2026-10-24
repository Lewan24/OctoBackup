# OctoBackup

<!-- impeccable:product-schema 1 -->

## Platform

web

## Product Purpose

Manage database connections, scheduled backups, backup history and user access from
one self-hosted application. Support MySQL/MariaDB, PostgreSQL, SQL Server native shared-staging backups and mounted SQLite snapshots. Offer optional administrator-enabled global encryption and independent individual server passwords; preserve historical backup keys.

## Users

Repository evidence: administrators manage users and server access; operators work
with assigned servers, backups and schedules.

## Capabilities and Constraints

User-requested stack: .NET 10 backend and React frontend in root Backend/Frontend
directories. Retain SQLite metadata and existing migrations. Do not fabricate
backup success or restore-verification results. Credentials are write-only in UI.

## Brand Commitments

OctoBackup name. Latest user direction replaces the previous turquoise interface:
readable, modern dark UI for administrators, programmers and technical
operators, with a near-black canvas and midnight/navy surfaces and deep blue/cobalt gradients, larger
typography and focused action dialogs. Keep standard navigation, readable data and real operations foremost.

## Product Principles

- Make backup actions and their results explicit.
- Enforce access on the server for every resource.
- Distinguish working functionality from planned restore verification.
- Keep deployment and future development reproducible and documented.
