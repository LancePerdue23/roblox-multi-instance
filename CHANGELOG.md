# Changelog

## Unreleased

- Add optional launch progress and confirmed-helper completion feedback, both off by default, using the shared launch-status model without inferring account sign-in or game entry.
- Group overlapping launch feedback in a temporary, authenticated normal-user notification host with bounded delivery, deduplication, popup limits, and local warning fallback. Finalize launch history before warning presentation.
- Add a visible-only Active instances panel with window titles, local process start times, per-client CPU/working set, focus, and confirmed graceful close requests.
- Bind player sessions to process tokens, revalidate actions against PID/creation-time identities and window ownership, and keep transient instance data out of logs/history/reports.

- Add explicit update-aware player recovery and a persistent browser-integration toggle without background monitoring.
- Retain original protocol backups and use conditional value restoration with a persisted interruption journal to preserve other installers' changes.

- Add a local Diagnostics dashboard with explicit worker testing and existing Setup/Repair actions.
- Record bounded recent-launch outcomes and export allowlisted troubleshooting ZIPs without raw logs or identifying launch data.
- Preserve silent launches and the on-demand worker lifecycle; remove raw setup stack traces from future logs.
- Set the upcoming executable version to 1.1.0 so the dashboard can identify older installed helpers.
- Prepare the project for standalone Git development.
- Separate default tests from opt-in Windows/Roblox integration tests.
- Add portable build scripts and a Windows build workflow template.

## 1.0.1

- Apply and verify browser protocol registration from a clean Windows user context.
- Notify Windows when launch associations change.
- Record launch-request status without including authentication data.

## 1.0.0

- Add an on-demand singleton-event worker, launcher, setup, repair, and uninstall.
- Support browser Play links and a desktop shortcut without a startup watcher.
