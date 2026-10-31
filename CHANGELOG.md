# Changelog

## Unreleased

- Use a fully client-painted Layouts scrollbar to prevent native white/black tracks and flashes during activation and options expansion; preserve wheel, drag, keyboard, and focus scrolling.
- Resolve instance account names from ID-only Roblox game-join logs through public profiles; retry unavailable lookups and keep reopened clients separate by process lifetime.
- Replace separate management windows with an Instances-first workspace: embedded Layouts, Activity, System health/updates/reports, and Settings pages.
- Add Dark, Light, Warm Neutral, and Follow Windows appearance choices with immediate switching and high-contrast support.
- Consolidate update checks, release notes, download progress/cancellation, verification, and explicit setup into System → Updates; passive notices navigate to that page.
- Add consistent line navigation icons, readable themed controls, icon-free summaries, and in-window confirmations and preset naming.
- Extend the simulated-user audit to 50 journeys and 1,000 sessions, with embedded navigation/update controls and a three-theme screenshot matrix covering three scales and the minimum window size.

- Count windowed clients separately from background Roblox processes; automatically stop clients observed without windows for one minute while the system app or tray runs, with identity and visible-window rechecks and a confirmed manual cleanup action.
- Open Diagnostics by default from the installed executable and desktop shortcut; add an explicit Launch Roblox button while preserving immediate browser Play launches.
- Check GitHub Releases once per Windows boot with a passive update card, in-app release notes, and manual update checks in Diagnostics.
- Add explicit direct downloads with size/SHA-256/version verification and a separate graceful setup handoff, preserving Roblox games and existing preferences.
- Add optional launch progress and confirmed-helper completion feedback, both off by default, using the shared launch-status model without inferring account sign-in or game entry.
- Group overlapping launch feedback in a temporary, authenticated normal-user notification host with bounded delivery, deduplication, popup limits, and local warning fallback. Finalize launch history before warning presentation.
- Wait through Windows' temporary null foreground handle during activation/fullscreen transitions, support an already-focused client, and hold F11 for 150 ms so frame-based input sampling can observe the press.
- Use a focus-checked real F11 shortcut for fullscreen conversion; background key messages were ignored by live Roblox clients. Restore manager focus when appropriate and report denied focus, held keys, or interruptions.
- Automatically request and verify windowed mode for selected fullscreen Roblox clients before arranging, with bounded per-client results and continuation when a client does not respond.
- Keep arrangement checkboxes selectable in fullscreen and refresh selected window states on Preview, so switching to windowed mode does not require reopening Arrange.
- Add previewed side-by-side/grid window arrangements, per-monitor assignments, ordered saved-position presets, and explicit off-screen recovery with a 640×360 logical-pixel client minimum.
- Support mixed-DPI placement and monitor identity matching, with missing-monitor fallback and revalidated process/window targets.
- Add panel layout shortcuts and explicitly enabled tray/global shortcuts without sign-in startup or hidden resource sampling.
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
