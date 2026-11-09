# Changelog

## 1.1.1 — private validation prereleases

- Record setup attempt/version, phases, fixed failure reasons, code locations and native error codes without private exception text or paths. Name the setup log folder in failure messages.
- Add native GitHub update credential storage, authenticated private release/asset requests, explicit validation-prerelease selection and access-error guidance. Preserve all package verification and explicit download/install actions.
- Ship new recognition artwork, animations, badge collections and sidebar controls; retain signed account eligibility. Keep new supporter designs and cumulative contributions unpublished.
- Older versions require one manual update to use private update access. Builds remain unsigned validation prereleases; clean-machine Windows acceptance and the original tester setup failure remain open.

## 1.1.0 — private validation prereleases

- Replace the global app-only launch action with Launch games: repeat the last setup, edit or create a batch, review its accounts and layout, and follow progress or cancel pending launches. Preserve Open Roblox app as a secondary action.
- Support Grid and Columns alongside saved launch layouts, remember ordered account setups, and preserve the Play page's draft while using the quick launcher.
- Add confirmed Stop all for listed Roblox clients, including background clients, with process-lifetime validation and per-client results.
- Add guided account creation through Roblox's official signup page, explicit autofill/refill controls, protected supplied-credential storage and interrupted-signup recovery.
- Add signed account-bound cosmetic recognition, badge-file activation and per-badge appearance controls. Packages include the public verification key only.
- Offer explicit repository-trusted updates in unsigned builds; pinned builds continue to require their configured publisher signature. Private prereleases remain manual downloads.
- Harden account-creation cancellation, password controls, launch focus and automatic-layout coordination. Add native, backend, browser and shared-service regression coverage.
- Exclude owner-local files and preview tools from Git/release inputs, and omit distribution debug symbols and developer source paths. Validate release versions against the shared product version.
- Focused live checks were completed by the owner. These unsigned private tester builds do not claim completion of the broader isolated Windows install/update/accessibility matrix. Roblox's close-to-tray interaction is documented; this version does not replace Roblox's window-close behavior.

## 1.0.1 — validation prereleases

- Keep Activity's launch-history table independent of the Instances preview switch.
- Preserve pending singleton cleanup through temporary bootstrap process gaps instead of canceling the worker after an early startup-exit inference.
- Remove the unconditional eight-second wait after healthy singleton cleanup; suspected startup failures still receive bounded observation and handoff grace.

- Add an experimental opt-in live client preview grid in Instances, with native window capture, bounded visible subscriptions, pause/privacy controls and validated Focus. Live Roblox and hardware/performance acceptance remain pending.

- Gracefully close a locally replaced game window after a successful launch only when fresh process-owned logs verify a same-account duplicate-login disconnect; preserve unrelated accounts and active/rejoined clients.

- Add default-off installer consent, Privacy settings, bounded technical error summaries, desktop uploads, CLI controls, and a free-tier Worker/D1 ingestion service.
- Add owner-side GitHub release download counts without app tracking.
- Add Stripe-hosted one-time contribution service, verified offline cosmetic receipts, and supporter badge controls; service configuration and payment onboarding remain pending.
- Add isolated local Roblox sign-in, protected session groundwork, game/server target validation, sequential batch coordination, and verified-process layout integration.
- Keep production account launching explicitly unavailable until real-account authentication and window-correlation acceptance succeeds. No deployment, real payment, or gameplay acceptance is implied.
- Add native privacy/session/payment fixtures, browser feature journeys, and local Worker tests.

## 1.0.0 — validation prereleases

- First planned GitHub release: Windows x64 desktop app and on-demand helper editions, both including rmi.exe.
- Add matching graphical installers, portable desktop packaging, edition manifests, SHA-256 verification, and explicit setup/update handoff.
- Separate shared services from desktop presentation and strengthen launch observation, readiness, operation coordination, and account lookup reliability.
- Normalize unpublished development version 1.1.0 to release version 1.0.0; retain numeric downgrade and edition-switch protections, including legacy installations.
- Inspect setup edition, protocol and embedded payload hashes during update verification before handoff; verify every portable member and publish checksums for all packages.
- Live Windows installation, upgrade, uninstall, gameplay, DPI, theme and accessibility acceptance remain pending. See docs/releases/v1.0.0.md.
- Publish separate private helper-only and full app + helper validation prereleases; verify downloaded assets and isolated startup for both editions. Stable production acceptance remains pending.

## Prior development notes (historical)

- Promote the web desktop source and executable identity; preserve a verified local WinForms archive.
- Consolidate duplicated compatibility services and migrate valid Preview preferences without overwriting existing primary settings.
- Earlier distribution preparation was superseded by the two installer editions and rmi commands described above.

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

## Historical local build 1.0.1 (unpublished)

- Apply and verify browser protocol registration from a clean Windows user context.
- Notify Windows when launch associations change.
- Record launch-request status without including authentication data.

## Historical local build 1.0.0 (unpublished)

- Add an on-demand singleton-event worker, launcher, setup, repair, and uninstall.
- Support browser Play links and a desktop shortcut without a startup watcher.

