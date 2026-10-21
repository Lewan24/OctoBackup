# OctoBackup interface

Mode: Operate. A compact dark operations workspace for administrators, programmers
and technical operators. The latest user brief supersedes the original green/light
theme. Cyberpunk influence comes through ink-purple surfaces, electric lime actions,
violet gradients and precise motion; controls retain familiar web behavior.

## Tokens and hierarchy

Source of truth: `Frontend/src/styles.css`.

| Role | Value |
| --- | --- |
| Canvas | `#100e1b` |
| Navigation | `#151120` |
| Panel | `#1a1628` |
| Raised surface | `#221c33` |
| Primary text | `#f1eef9` |
| Secondary text | `#aaa3be` |
| Primary action / focus | `#d0f584` with `#202714` text |
| Secondary action / links | `#b5a0ff` |
| Divider | `#332b48` |
| Input/control boundary | `#76668d` |
| Error / destructive text | `#ffa5b9` |

Self-hosted Manrope remains the interface face. Body defaults to 13px; page titles
25px, panel titles 14px and table/form text 11-12px. Monospace is reserved for host
addresses, timestamps and other data. Numeric values use tabular figures. Main
panels have 12px corners; controls use 5-7px corners. Desktop controls are 36-38px
high; mobile inputs are 42px high with 16px text to avoid browser zoom.

## Layout and behavior

- Fixed 218px desktop navigation, 61px top bar, fluid content with 28px gutters.
- Authenticated shell: compact live queue banner above every page. Dashboard has inline real statistics, recent records and
  connections, then enabled schedules. No decorative chart substitutes for data.
- Forms, tables, server connections, settings and access management share dark
  surfaces, restrained dividers and explicit loading/error/empty states.
- Database engines use subtle distinct accents; enabled/paused/error states always
  have text. Enabled means configured as enabled, not a successful health probe.
- Realtime indicator reflects actual SignalR connection state. Refresh explicitly
  reloads overview data. Backup/schedule actions navigate to the working flows.
- Mobile uses a drawer with backdrop and Escape dismissal, stacked panels/forms
  and horizontally scrollable tables. Closing by Escape returns focus to the menu.
- Administration separates Users & access from Application settings. Settings use
  grouped network, registration and backup sections with one save/discard action,
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

Keep visible lime focus outlines, labeled forms, semantic tables, a skip link,
themed selection/caret/scrollbars and native dark browser controls. Gradients belong
to the action banner and sign-in surface; keep data panels quiet. Never fabricate
records, protection claims, health checks or restore verification. Database/SSH
secrets remain write-only.
