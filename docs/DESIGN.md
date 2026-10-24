# OctoBackup interface

The current user brief defines a readable midnight-blue operations workspace.
This replaces the previous turquoise palette. The custom visual direction follows
the user-provided palette.

## Tokens and hierarchy

Source of truth: `Frontend/src/styles.css`.

| Role | Value |
| --- | --- |
| Near-black canvas | `#04070D` |
| Deep navy panel/dialog | `#02203C` |
| Raised blue surface | `#032C4F` |
| Cobalt gradient / primary action | `#0F427E` |
| Primary text | `#F5F8FF` |
| Secondary text | `#B0C2DD` |
| Focus / small highlights | `#91BFFF` |
| Secondary links | `#C1D9FF` |
| Divider | `#26476E` |
| Input boundary | `#6383AA` |

Self-hosted Manrope remains the interface face. Body text is 16px; forms use 16px
inputs, supporting text generally 13–15px, and page titles 29–32px. Primary controls use
44px minimum heights, navigation icons 22px, and action icons 20px. Spacious panels,
subtle borders and navy-to-cobalt gradients establish hierarchy without decorative data.
The supplied white octopus logo is 128px in navigation and 104px on sign-in. It is centered in the sidebar; OctoBackup appears on
hover and keyboard focus. Its navigation link always has an accessible name.

## Focused actions

Connection create/edit, server encryption/access, schedule create/edit, user
create/edit, account email/password, decrypted download, global encryption and
workspace policies use native modal dialogs. Destructive server/schedule actions,
server backup/test/availability, user block/confirmation, queue cancel/remove and
restore-test requests identify the resource and ask for explicit confirmation.
Forms keep validation and server errors inside the dialog, disable duplicate
submission while pending, and prevent dismissal during submission. Native dialogs
contain keyboard focus, support Escape dismissal when idle, dim the workspace and
restore focus on close. Navigation/filtering/refresh and direct file downloads
remain immediate. Workspace policy editing retains revision checks and dirty state.

## Layout and behavior

- 264px desktop navigation, an 80px top bar, and fluid content with 32px gutters.
- Authenticated shell: compact live queue banner above every page. Dashboard has inline real statistics, recent records and
  connections, then enabled schedules. No decorative chart substitutes for data.
- Forms, tables, server connections, settings and access management share dark
  surfaces, restrained dividers and explicit loading/error/empty states.
- Backup history links to a restore report with manual test/retry, explicit queue
  and failure states, a native sampled-table selector and a horizontally scrollable
  read-only snapshot. Empty tables, NULLs and truncation are labeled. Samples use
  existing panel/table tokens and do not imply complete source verification.
- Database engines use subtle distinct accents; enabled/paused/error states always
  have text. Enabled means configured as enabled, not a successful health probe.
- Realtime indicator reflects actual SignalR connection state. Refresh explicitly
  reloads overview data. Backup/schedule actions navigate to the working flows.
- Mobile uses a drawer with backdrop and Escape dismissal, stacked panels/forms
  and horizontally scrollable tables. Closing by Escape returns focus to the menu.
- Administration separates Users & access from Application settings. Settings use
  summary cards and a focused policy dialog with grouped network, registration and backup sections and one save/discard action,
  a visible dirty state and explicit reload for stale edits. A quiet side panel
  explains deployment-managed values and persistence. Background backup events do
  not refresh or replace the settings editor.

## Motion and accessibility

The queue banner reports persisted waiting/running/stopping state, a cancellation
countdown, actual scoped counts and a link to paginated request history. Eligibility
is not a promise of execution time while another job uses the worker. Pending and
failed status loads remain explicit. Running animation reflects worker state. Buttons have
short hover/press feedback; navigation transitions use transform. No animation
library or continuously running decorative effect is required. All motion is
disabled under `prefers-reduced-motion`.

Keep visible blue focus outlines, labeled forms, semantic tables, a skip link,
themed selection/caret/scrollbars and native dark browser controls. Gradients belong
to the action banner and sign-in surface; keep data panels quiet. Never fabricate
records, protection claims, health checks or restore verification. Database/SSH
secrets remain write-only.

Use white labels on dark primary action gradients. Pale blue is reserved for focus,
links and small highlights; large surfaces and primary buttons remain dark blue.

## Backup history actions

Restore verification is a standalone icon-and-text label: green for a saved passed
restore, rose for a failed restore, blue for running/queued tests, and neutral for
not tested, interrupted, skipped or unknown reports. Status never implies that an
untested backup is valid. The Actions column groups Test / preview and Download
with explicit spacing. Download opens a native top-layer menu with encrypted and
decrypted options for encrypted artifacts; decrypted download opens the existing
password dialog and preserves inline API failures. Unencrypted artifacts only
expose their original download, and expired artifacts expose no download actions.
Menus support outside-click/Escape dismissal and arrow-key navigation.
