# Interface refresh — 2026-10-24

## Delivered

- Custom midnight/navy surfaces and deep blue/cobalt gradients built from
  `#081635`, `#02203c`, `#032c4f` and `#0f427e`; the turquoise direction is superseded.
- Larger text, navigation/action icons, inputs, buttons, cards and table spacing.
- Supplied octopus logo centered in navigation, with the application name revealed
  on hover and keyboard focus and a permanent accessible navigation label.
- Native modal editors for connections, schedules, users, account credentials,
  server access/encryption, decrypted downloads, workspace policies and encryption
  settings. Settings pages show readable summaries before opening an editor.
- Resource-specific confirmations for backup queueing, connection tests/availability,
  connection/schedule deletion, user blocking/confirmation, access revocation,
  queue cancel/remove/retry, and restore-test requests.
- Pending submissions prevent duplicate sends and dismissal; API errors remain
  visible in the dialog. Idle dialogs support Escape and return focus on close.
- Existing API authorization, cookies/CSRF, write-only credentials, saved settings
  revisions, and truthful loading/error/empty states are preserved.

## Validation

- Backend: `dotnet test Backend/tests/OctoBackup.Tests` — 101 passed.
- Frontend: `npm test --prefix Frontend` — 16 passed, including dialog failure/retry
  and pending-dismissal regression checks.
- Frontend: `npm run build --prefix Frontend` — passed.
- Audits: npm audit — zero vulnerabilities; NuGet transitive vulnerability check
  for the backend test graph — no vulnerable packages reported.
- Desktop and mobile browser workflows passed: settings/encryption, server create
  and edit, queue backup/cancel/remove, schedule creation, restore sample rendering,
  navigation and sign-out. Screenshots inspected for desktop sign-in, dashboard,
  connection dialog and mobile dashboard.
- Final `npm run test:e2e --prefix Frontend` — all 4 tests passed across desktop
  and mobile: real API workflows plus isolated UI fixtures for logo hover/focus,
  account action separation, Tab wrapping, Escape dismissal and focus restoration.
- Fixed mobile drawer dismissal when selecting the already-current route. The
  final browser run verified this behavior. Inspected the mobile account dialog.
- `git diff --check` — passed.

## Limits

Browser checks use disposable API data. Restore-preview browser data is explicitly
mocked in the test fixture; this redesign does not verify live provider restores
or constitute a penetration test. Navigation, filtering, refresh and direct file
downloads stay immediate. The logo asset itself contains lettering; the separate
navigation label is what is hidden until hover/focus. Reduced motion remains
supported. Existing SignalR dependency build annotation warnings remain non-fatal.

## Dark blue palette follow-up

Recolored navigation, panels, dialogs, form controls, statuses and sign-in surfaces.
Primary actions use dark blue gradients with white labels; pale blue highlights
keep focus and links readable. Existing layouts and modal workflows are preserved.
Palette validation: production build passed; backend 101/101; frontend 16/16;
desktop/mobile Playwright 4/4; npm audit zero vulnerabilities; diff whitespace
check passed. An initial frontend run under concurrent build/test load timed out
waiting for the restore fixture; the subsequent run passed all tests. Reviewed
new desktop dashboard/sign-in and mobile dashboard screenshots. White action text
on the lightest primary gradient endpoint has a 9.4:1 computed contrast ratio;
secondary text on the navy panel has 9.1:1. These spot checks are not a complete
accessibility audit.

## Darker background and larger logos

Canvas changed to `#04070D`; navigation/top bar and sign-in backgrounds are darker
while panels retain blue depth. Navigation logo increased from 88px to 128px and
sign-in logo from 56px to 104px. Production build and all four desktop/mobile
browser checks passed. Reviewed desktop login/dashboard screenshots and retained
logo hover/focus behavior. `git diff --check` passed. This follow-up changes CSS
only; backend/unit and dependency audit results above are from the preceding
palette validation.

## Backup history labels and actions

Moved Test / preview and download controls into Actions; restore verification uses
colored labels with explicit saved/pending/not-tested states. Native popovers avoid
table overflow clipping and offer encrypted/decrypted choices only when encryption
exists. Decryption reuses the password dialog and CSRF-protected endpoint. Expired
files have no download actions. Added desktop/mobile browser fixtures covering
status separation, keyboard behavior, a decrypted fixture browser download event,
encrypted endpoint link/attachment attributes, rejected-password retry, plain
archives and expired artifacts. Native encrypted attachment content is not claimed
as verified by these UI fixtures; the existing endpoint remains unchanged.

Validation: production build passed; 101 backend tests and 16 frontend tests passed;
all six desktop/mobile browser checks passed. npm audit reported zero vulnerabilities;
`git diff --check` passed. Reviewed history/menu screenshots on desktop and mobile.
The browser checks caught and fixed an asynchronous menu-reopening keyboard race.
Fixture documents are served locally to avoid consuming the real API's per-IP
budget; production rate limits were not changed. The long existing workflow now
has a 60-second timeout to accommodate its expanded dialog steps.
