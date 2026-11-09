# Working on OctoBackup

Read docs/ARCHITECTURE.md, docs/SECURITY.md and docs/OPERATIONS.md before changes.
docs/BACKUPS.md documents recovery/testing; README.md separates proposed work from facts.

- Backend is a .NET 10 modular monolith under Backend/src. Preserve namespaces,
  Identity/SQLite compatibility and existing EF migrations. Use the local EF tool.
- Frontend is React/TypeScript/Vite under Frontend. Keep features self-contained;
  app composes features; shared code must not import features. Follow docs/DESIGN.md.
- Browser requests use same-origin cookies and in-memory CSRF via shared/api.ts.
  Never store secrets in localStorage or introduce a second browser auth token.
- Enforce resource and admin authorization on the API; UI visibility is no boundary.
  Do not expose connection secrets, physical backup paths, public restore execution
  or client-callable SignalR broadcasts.
- Treat database/SSH hosts, SQL identifiers, external command arguments and file
  paths as hostile input. Retain allowlists, certificate verification and SSH pins.
- Preserve truthful empty/error/pending states. Do not substitute example data or
  success responses for missing backend behavior.
- Run backend build/tests before E2E tests; Windows locks running server binaries.
  Frontend scripts handle # in filesystem paths through temp staging.
- Validate with dotnet test, npm test/build and Playwright as appropriate. Check
  dependency audits. Use disposable data, never a developer's real backup database.
- Update relevant reference docs and both READMEs when behavior or setup changes.
  Report actual results and remaining limitations in the change summary; do not claim
  a penetration test or live-provider restore verification from unit/API/browser checks alone.

- Build Dockerfiles with repository-root context. The deployment reverse proxy
  terminates TLS; frontend Nginx proxies the private HTTP API. Never trust arbitrary
  forwarded headers.
- Console helper: node scripts/dev.mjs init, then node scripts/dev.mjs. Never print .env secrets.
