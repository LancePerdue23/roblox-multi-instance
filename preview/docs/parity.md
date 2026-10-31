# Feature parity and acceptance checklist

Implementation is present for the behaviors below. Acceptance remains open where live testing is required. Synthetic tests do not establish real Roblox or installer acceptance.

| Area | Required behaviors | Acceptance |
| --- | --- | --- |
| Instances | refresh, metrics, unavailable/empty states, selected process and window, focus, confirmed close, background-client status | Implemented; live acceptance pending |
| Launch | persistent launch action, repeated-click protection, installed normal-user helper, observation cancellation without killing launch | Implemented; live acceptance pending |
| Browser integration | shared state and warning, enable/disable, recoverable failures | Implemented; live acceptance pending |
| Layout editor | Grid/SideBySide/Saved/Recover, window selection/order, monitor selection/assignment, live preview invalidation | Implemented; live acceptance pending |
| Placement | minimum size/capacity, physical pixel DPI, full-screen preparation, process identity revalidation, partial failure/recovery, cancel | Implemented; live acceptance pending |
| Presets | save, rename, delete, reset, corrupt-data recovery, isolated persistence | Implemented; live acceptance pending |
| Activity | All/Needs attention, stable selected details, empty/unavailable state, shared history clear confirmation | Implemented; live acceptance pending |
| Health | read-only refresh, per-check detail, explicit worker test, setup and Recover/Repair | Implemented; live acceptance pending |
| Reports | allowlisted redacted ZIP, export confirmation, user save path, no uploads or raw identifiers | Implemented; live acceptance pending |
| Logs | existing user/worker/setup folders, safe missing-folder errors | Implemented; live acceptance pending |
| Updates | installed vs preview versions, check, notes, availability, verified download, progress/cancel/retry, explicit setup and postcheck | Implemented; live acceptance pending |
| Appearance | dark/light/warm/system, high contrast priority, reset, isolated storage | Implemented; live acceptance pending |
| Notifications | shared progress and confirmed-completion settings, corrupt-store reset | Implemented; live acceptance pending |
| Shortcuts | private validated bindings, conflict handling, reset, one owner per session | Implemented; live acceptance pending |
| Tray/lifetime | enable/disable, reopen, shortcuts, graceful exit/drain, no forced termination | Implemented; live acceptance pending |
| Cleanup | existing grace/trust/identity safeguards, mutex ownership, stopped on exit | Implemented; live acceptance pending |
| Import | explicit validated one-time preference copy, never write source | Implemented; live acceptance pending |
| Bridge | bounded typed allowlist, invalid origin/version/payload, duplicate IDs, cancellation and late responses | 16 host boundary checks + 4 frontend bridge checks; host smoke passed |
| Visual/accessibility | resize, scrolling, keyboard/focus, long content, 100/125/150/200% scaling and monitor changes | Default real WebView screenshot inspected; multi-DPI/manual accessibility pending |
| Preservation | archived hashes match originals, release unchanged, old build/tests still work, no installation on preview startup | 80 source hashes + release match; original build and 122 tests passed |
| Live integration | two separate accounts remain in-game, installed worker, maintenance, canceled UAC, update busy files | Implemented; live acceptance pending |

## Automated evidence
26 backend checks cover confirm guards, private writes, corrupt-settings recovery, cancellation, stale targets/plans, skipped layout slots and F11 notices. Generated TypeScript contracts are checked against C# DTOs. Three offline Playwright workflow tests and four Vitest bridge checks pass; see migration ledger for scenarios.

## Release boundary
This is a portable comparison build, not a production replacement. No two-account game sessions, real placement/closure, administrator approval, repair/uninstall, or update installation was exercised by implementation checks. Those live rows remain open until evaluated in the controlled acceptance environment. Automatic background cleanup is opt-in in the preview during comparison, so merely opening it does not begin stopping existing clients.


