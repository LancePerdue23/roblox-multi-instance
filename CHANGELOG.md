# Changelog

## Unreleased

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
