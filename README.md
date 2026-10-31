# Roblox Multi Instance

The upcoming 1.1.0 build adds a local health dashboard, recent launch results, and redacted troubleshooting exports. It retains the 1.0.1 fix that applies and verifies browser registration from Windows's normal user context, including when setup starts from a registry-isolated environment.

A Windows x64 launcher that closes Roblox's `ROBLOX_singletonEvent` Event handle after each launch, allowing separate clients to remain open. It uses the official Roblox player already installed on the PC. The executable remains named `RobloxLaunchHelper.exe` for compatibility with existing installations.

## Install and use

1. On first use, double-click `RobloxLaunchHelper.exe` to install and approve the Windows administrator prompt with your current account.
2. After installation, open the executable or **Roblox Multi Instance** desktop shortcut to open the Instances workspace, then select **Launch Roblox**. Roblox's normal browser Play buttons still launch their requested game immediately.
3. Use separate Roblox accounts/browser sessions as needed. This helper does not manage accounts or sign-ins.

Setup installs the executable under `%ProgramFiles%\RobloxLaunchHelper\<Windows SID>`, registers one on-demand elevated task for the current account, and wraps the per-user `roblox` and `roblox-player` launch links. No startup entry, scheduled timer, service, or permanent watcher is installed. The worker exits about two seconds after completing its work; each launch has a 60-second observation deadline after Roblox starts.

The desktop shortcut opens the Instances workspace without launching Roblox. **Launch Roblox** starts the Roblox app with its `-app` flag through the existing multi-instance flow. The dashboard stays responsive, prevents repeated clicks while that launch is running, and reloads recent results when it finishes. Closing the dashboard leaves the launch and Roblox running. Browser launches forward their original protocol link unchanged. Setup/repair use a temporary protected installer task to operate in the real Windows user context and temporary least-privilege checks for permissions, protocol registration, and shortcuts. These tasks are deleted on completion; only the on-demand handle task remains.

Roblox runs without elevation. Only installation, repair, removal, and the short-lived handle worker use administrator access. The executable is self-contained; a separate .NET installation is unnecessary. It is a local unsigned build, so Windows may identify its publisher as unknown.

For an explicit command-line app launch after setup, use `RobloxLaunchHelper.exe --launch-roblox`. Running without arguments opens Instances; `--diagnostics` opens System → Health.

## Repair and remove

The Start menu's **Roblox Launch Helper** folder contains **Repair**, **Uninstall**, and **Diagnostics** shortcuts. Equivalent commands:

```powershell
.\RobloxLaunchHelper.exe --setup
.\RobloxLaunchHelper.exe --repair
.\RobloxLaunchHelper.exe --recover
.\RobloxLaunchHelper.exe --disable-browser-integration
.\RobloxLaunchHelper.exe --enable-browser-integration
.\RobloxLaunchHelper.exe --uninstall
.\RobloxLaunchHelper.exe --diagnostics
```

Roblox updates may replace either launch registration or its version-specific executable. Open **Diagnostics**, select **Check again**, then **Recover/Repair** when needed. Recovery prefers a verified current official registration, then saved commands and backups. If those targets are gone, it checks immediate version directories under the standard per-user and Program Files Roblox folders. Only official signed `RobloxPlayerBeta.exe` files are accepted. If several players remain, you must select one; timestamps do not determine the choice. If none is trusted, reinstall official Roblox and retry. Normal launches never repair registrations automatically.

**Disable browser integration** restores official browser launching for protocols still owned by the helper, until you explicitly select **Enable browser integration**. External registrations are left alone. The desktop shortcut continues to use the helper. Repair, Recover and repeated Setup preserve the disabled preference; only Enable turns it back on. The toggle on **Instances** uses the installed, authenticated helper task without administrator prompts or completion dialogs; progress and failures appear inside the dashboard. After upgrading from an older helper, run **Recover/Repair** once to install support for silent toggling. Setup, Repair and the command-line installation actions still use same-account administrator approval. If a helper-owned protocol has no trusted official restoration target, disabling or uninstalling stops before changing registrations so you can reinstall official Roblox first.

The protected installation settings retain original registry snapshots, newer restoration snapshots, owned values and an interruption journal. Recovery, disable, rollback and uninstall change individual values only while they still match the expected helper-owned values. Other installers' values and nested keys are preserved. Interrupted operations are reconciled on the next explicit installation action. Detected concurrent changes stop the operation and appear as a conflict; Windows offers no atomic compare-and-write across arbitrary installers.

Uninstall removes the task, owned shortcuts, and installed program files. It restores owned protocol values with a trusted official launch command only while they still point at this helper, preserving registrations and unrelated values that another installer has since changed. Small launcher diagnostic logs under `%LOCALAPPDATA%\RobloxLaunchHelper\logs` are retained for troubleshooting.

Setup logs from the downloaded executable are retained under `%ProgramFiles%\RobloxLaunchHelper\setup-logs`. Repair invoked from the installed Start menu shortcut records its setup status in the installed `logs` folder.

## Helper updates

The first normal app or Roblox launch per Windows boot checks GitHub for a newer stable helper release. All launches for your Windows account share one automatic attempt; opening more clients or restarting the app does not repeat it. A temporary normal-user helper performs the network request without delaying Roblox startup. Offline networking, rate limits, and unavailable release metadata stay silent. **System → Updates** lets you check manually at any time.

A compact **Update available** card shows the installed and available versions without taking focus or playing sound. Ignore it, select **Later**, or close it to keep using the current version. **What’s new** displays release notes inside the app. **Download update** downloads directly from GitHub with progress and cancellation; a browser is optional. Downloads require a uniquely named executable asset with a published SHA-256 digest, and the helper verifies size, hash, and executable version before offering **Run setup**.

**Run setup** is a separate explicit action. It closes helper windows and tray mode gracefully, waits for file users, and uses the existing administrator approval flow. Roblox games remain open. Busy files produce a retry message; no processes are force-terminated. Canceled approval and failed setup retain the downloaded payload for retry. Successful setup confirms the installed version and offers **Open app**, which opens System → Health. Existing installation and user preferences are preserved. Nothing downloads or installs unattended, and no startup service or scheduled update timer is added.

Update state and isolated staging directories live under `%LOCALAPPDATA%\RobloxLaunchHelper\updates`, outside protected installation settings and troubleshooting exports. Unused known staging directories older than seven days are cleaned during later update activity. Publishing requirements and live acceptance checks are in [the update checker checklist](docs/update-checker-validation.md).

## Unified workspace and appearance

The app uses one main window with persistent Instances, Layouts, Activity, System, and Settings pages. Launch Roblox remains available throughout. Focus and Arrange act on the selected window; closing a window requires an in-window confirmation. Window titles never verify account identities.

Instances can show account usernames from each verified client's own open log. When Roblox logs only a numeric account ID in its game-join timing entry, the app resolves the username through Roblox's public profile API without cookies or sign-in credentials. Names and IDs stay in memory and are excluded from diagnostics. A newly opened client may show **Roblox** until it writes join metadata; unavailable logs or an offline profile lookup also leave the generic label and are retried automatically.

System contains Health, Updates, and Logs & reports. Update checks, release notes, verified downloads, cancellation, and the explicit Run setup action stay in that page. Switching pages keeps downloads running; closing the workspace cancels them safely. Automatic update notices direct you to the same page. Installation uses a separate setup handoff so the running executable can be replaced, and Windows still displays its administrator prompt.

Activity shows recent launches newest first and refreshes history when opened or when you select **Refresh**. **Needs attention** filters warnings, failures, and incomplete attempts. Selection survives refresh while the launch remains visible; otherwise the newest visible launch is selected. Details show local start/end times, duration, and a next step. **Clear history…** and a shortcut to **Logs & reports** sit beside the filter. Refreshing Activity reads history without running health checks or resetting a worker test.

Settings contains Appearance, Notifications, and Shortcuts & tray. Dark is the default; Light, Warm Neutral, and Follow Windows are also available. Appearance changes apply immediately and persist in `%LOCALAPPDATA%\RobloxLaunchHelper\appearance.json`, separately from existing preferences. Windows high contrast takes priority. Invalid appearance settings require an explicit reset before changing them. Appearance preferences are excluded from troubleshooting exports.

Dropdowns share the same surface, border, and arrow styling. Scrollbars use surface-colored tracks and slim rounded thumbs across text, lists, options, and grids; native scrolling and accessibility behavior are preserved. High contrast uses Windows scrollbar rendering.

Routine details, preset naming, and confirmations appear inside the workspace. File pickers, Windows administrator prompts, explicit Explorer/browser actions, and passive launch/update notices retain their normal behavior. An ellipsis on an action means additional input or confirmation is required; navigation actions do not use one.

## Diagnostics dashboard and reports

Open **Diagnostics** in the Start menu, or run `RobloxLaunchHelper.exe --diagnostics`, normally without administrator access. The local Windows dashboard checks installation settings, the saved Roblox player and publisher, both browser protocols, the on-demand task configuration, and effective permissions. Opening it or clicking **Check again** does not start a worker, launch Roblox, close handles, or repair the installation. Permission inspection is read-only; an inconclusive check is **Unknown**, not a pass. Registry results describe this process's view. Setup/Repair still verifies registration in Windows's normal user context separately.

**Test worker** explicitly starts or connects to the existing authenticated on-demand worker and sends only a probe. It does not scan Roblox handles; the worker exits normally when idle. A test has a 35-second total limit. Check again and installation actions invalidate its previous result. An idle worker is normal. **Recover/Repair** and **Setup** use the existing administrator approval flow only when selected. The browser toggle uses the authenticated helper task without popups. Buttons open the user, worker, and setup log folders separately.

Recent results distinguish a confirmed singleton release, Roblox starting without confirmed multi-instance readiness, a failed launch, and an interrupted launcher whose completion is unknown. Confirmation does not establish account sign-in or that either account remained in-game. Existing text logs are not reconstructed as launch outcomes, so history starts with this version. Successful launches remain silent by default.

**Settings → Notifications** offers separate **Show launch progress** and **Show confirmed helper completion** options, both off initially. Progress uses one nonmodal panel without taking foreground focus; quick attempts under 750 ms need not display it. Completion feedback means singleton release was confirmed, not that an account signed in or joined a game. Overlapping attempts share a temporary, normal-user notification host: terminal results are grouped for two seconds and popups are limited to one per ten seconds. Mixed results distinguish confirmed helper completion, unconfirmed completion, failure, and attempts still in progress. Dismiss hides progress for the current active group without canceling launches. Windows can suppress passive completion notifications; no repeated delivery is attempted.

Warnings and failures remain available independently of these options, grouped in one nonmodal warning panel when coordination succeeds. If the host is unavailable, busy beyond its bounded capacity, or cannot acknowledge a warning, the launcher retains its existing local warning dialog; simultaneous failures in that fallback mode can produce several dialogs. Notification delivery never gates player startup. The notification host exits after active attempts and pending summaries drain, with at most ten seconds of result-display grace; stale attempts expire as completion unknown. It does not enable tray mode, sign-in startup, a service, or worker activity. Preferences live in `%LOCALAPPDATA%\RobloxLaunchHelper\launch-notifications.json`, separately from installation and layout settings, and are excluded from diagnostic reports. Setup/Repair preserves them. Missing preferences disable optional feedback; invalid preferences disable it until explicitly reset in the settings dialog.

Structured history is stored under `%LOCALAPPDATA%\RobloxLaunchHelper\diagnostics`. It retains at most 100 attempts and 30 days, with a 512 KiB store limit and 4 KiB record limit. Pruning happens only while the launcher or dashboard runs; expired files can remain on disk while the helper is unused, but expired records are excluded when accessed. Launchers wait at most 100 ms for the shared history lock. Inaccessible, busy, or invalid storage cannot prevent Roblox launching. **Clear history** removes structured history, including recovery from a corrupt store; it does not remove text logs or reports already exported. Concurrent launches may create new records afterward.

**Export report** previews the included categories/counts and lets you choose a ZIP destination. The ZIP contains `summary.txt` and `diagnostics.json`: versions, current check results, the last explicitly requested worker test, UTC timestamps/durations, up to 100 recent attempts, and at most 200 approved log events. It reads at most 512 KiB per known log source pair and limits uncompressed report contents to 1 MiB. Missing or truncated sources are listed as omissions; export never silently starts a new worker test.

Reports exclude usernames, computer/domain names, SIDs, process/session IDs, persistent attempt IDs, full paths, launch URLs/tickets, cookies, account/game identifiers, raw commands, registry backups, stack traces, arbitrary exception text, and raw log attachments. Export uses approved fields and event codes rather than trying to scrub arbitrary log text. Versions that are not simple numeric versions are omitted as unknown. Local history uses a random attempt ID and transient launcher PID/creation time to distinguish interrupted attempts; those identifiers are not exported. Review reports before sharing: timestamps and launch activity can still be sensitive. Nothing is uploaded or copied to the clipboard. Delete exported ZIPs yourself; clearing history does not delete them. Runtime data and exports must remain outside commits.

## Diagnostics and behavior

**Instances**, the default workspace page, lists verified official Roblox players for your Windows user and interactive session, including players launched outside this helper. It shows window titles, PID, process start time in local time, CPU as a percentage of total logical CPU capacity across processor groups, and working set in MiB. Working set includes shared resident pages and is not private memory. The first CPU reading shows **Sampling…**; unavailable counters are labeled instead of reported as zero. A client can appear before it creates a window.

Select a client and, if it has several windows, select a window explicitly. **Focus** restores a minimized window and requests foreground activation; Windows may deny focus. **Close window…** asks for confirmation and sends a bounded, graceful close request. Roblox may prompt, ignore it, close only the selected window, or keep running; this window action does not force termination or automatically retry. Each action rechecks the process lifetime, owner/session, official publisher and window ownership. Windows has no atomic window-owner-check-and-send operation, so a small window-handle reuse race remains.

The panel counts clients with windows separately from background processes without visible windows. Roblox can remain running after its game window closes; those entries are live processes rather than cached rows. While the system app is running, including explicitly enabled tray mode, a normal-user monitor checks every five seconds without reading CPU or memory counters. A client observed without windows for a continuous minute becomes eligible for automatic force-stop. Windows returning, process replacement, failed checks, or observation gaps over 30 seconds restart the grace period. Only one app per Windows user/session runs this monitor; another open app can take over when it exits. Closing the whole system app stops monitoring; no service or sign-in startup is installed.

**Clean up background clients…** also offers confirmed manual cleanup of windowless processes older than one minute. Cleanup rechecks process creation time, owner/session, non-elevated status, official publisher and all visible top-level windows using the same process handle; changed or newly windowed clients are skipped. Each stop waits up to two seconds for exit confirmation. The manual action reports skipped, unavailable or unconfirmed results; automatic attempts that cannot finish wait through a new grace period before retrying. A slow startup that remains windowless for a minute can also be stopped. Windows cannot atomically check windows and terminate a process, so a window can still appear in the small interval after the final check. Background activity in a terminated process is lost. Closing the Active instances panel cancels its manual cleanup work; automatic cleanup continues while the system app remains running.

Refresh runs two seconds after each completed collection, only while the panel is visible and not minimized. Hiding, minimizing, closing, or confirming a close pauses collection; resuming resets CPU sampling. One native/signature call already in progress may finish after cancellation, but its late result is discarded and a replacement collector waits for it. Discovery never starts the worker or closes singleton handles. Panel data stays in memory and is excluded from logs, launch history and troubleshooting reports. **Window titles do not identify or verify Roblox accounts.** Live Roblox and normal-token acceptance checks are listed in [the implementation plan](docs/active-instance-manager-plan.md); synthetic tests do not establish account/gameplay behavior.

**Arrange** navigates to Layouts with an ordered, checked window list with per-window monitor assignments. Choose **Side-by-side** or **Grid**, select monitors, and use **Distribute** for round-robin assignment, or edit each row's monitor. **Move up/down** changes slot order. Initially eligible windows are checked; newly discovered windows are appended unchecked, and an exited window is never replaced automatically. Refresh windows explicitly to discover changes in the editor. **Preview** shows numbered slots; **Apply** confirms that preview. Resource sampling pauses throughout the editor.

Layouts use monitor working areas, an 8-logical-pixel margin/gap, and at least a **640×360 logical-pixel client area** plus window frames. Side-by-side divides a monitor into equal columns; grid uses the ceiling of the square root of its window count as its column count, filling rows left-to-right without stretching the final row. If the requested layout cannot fit, no windows move and the editor reports the fitting capacity. All rows remain selectable, including fullscreen windows. Preview freshly rechecks selected windows. Fullscreen clients show **Ready — will exit fullscreen** and remain part of the layout. Apply restores minimized clients, briefly focuses each fullscreen client and sends its real F11 shortcut through Windows input, verifies the transition for up to two seconds, then restores/resizes/places each window. It requests focus back to the arrangement panel after each conversion while that client still has focus. Release modifier keys during Apply; switching to another application interrupts fullscreen preparation. Already-windowed clients are never toggled. Windows must grant foreground activation and keyboard-input access; the helper does not bypass those restrictions or modify window styles. A client that refuses the request, exits, becomes busy, or changes identity gets an individual result while remaining windows continue; successful message delivery alone is not treated as a mode change. Fixed-size, busy or exited windows are still reported as unavailable. Saved-preset restore requires every slot to be ready rather than shifting assignments around skipped windows. Minimized/maximized windows restore to normal. Placement requests preserve Z order and do not activate windowed clients; fullscreen preparation briefly activates those clients. Clients can enforce their own sizes or ignore requests; results distinguish confirmed placement from an unconfirmed request. Exits or display changes after preflight can produce partial completion without rollback. An already queued native move may complete after cancellation. The native HWND ownership-check/use race also applies to positioning.

**Save current positions** stores named, ordered geometry slots. Choose **Restore preset** to map checked windows, in their current order, to those slots; counts must match. Presets support rename/delete, preserve deliberate overlap, and never identify accounts. Settings live in `%LOCALAPPDATA%\RobloxLaunchHelper\window-layouts.json`, with monitor interface paths, logical geometry and global-key preferences; no captions, process IDs, window handles or account data are saved or exported. Invalid/unsupported settings disable writes until **Reset saved settings** explicitly replaces them. Presets are not installation settings and require no migration.

The UI uses per-monitor DPI awareness. Saved positions follow monitor identities across coordinate changes, adjust for current scaling, and clamp inside the working area. Missing monitors fall back to a previewed grid on the primary monitor, requiring Apply and leaving the preset unchanged; fallback slots may overlap other saved slots. **Recover off-screen** previews relocation of checked windows whose title bars are entirely outside connected working areas, cascading on the primary monitor. Recovery is always explicit, even in tray mode. Too-small displays block recovery rather than violating the minimum.

Panel shortcuts are **Ctrl+Alt+1** (side-by-side), **Ctrl+Alt+2** (grid), **Ctrl+Alt+R** (restore selected preset), and **Ctrl+Alt+O** (off-screen recovery). Each opens a preview; Enter applies and Escape cancels. Layout keys are suppressed during text editing and modal dialogs. **Enable tray mode** explicitly keeps a normal-user process running after Diagnostics closes, enabling configurable global equivalents and **Ctrl+Alt+M** to open the manager. Global commands open the manager and preview; they do not silently move games. **Shortcut settings…** captures modified letters, digits or F1–F11; duplicates and reserved bindings are rejected, and registration conflicts are reported. Tray controls can disable global keys, disable tray mode, or exit. There is no sign-in startup, service, scheduled task, hidden sampler, or automatic activation of tray mode on subsequent launches.

- Normal success is silent unless optional launch feedback is enabled. If the worker fails, the helper leaves Roblox running and shows a grouped warning with Diagnostics access, or a local fallback dialog identifying the logs.
- Launcher logs: `%LOCALAPPDATA%\RobloxLaunchHelper\logs`.
- Elevated worker logs: the installed program's `logs` folder. Each log rotates at approximately 256 KiB with one previous file retained.
- Logs contain fixed status codes, timestamps, and process IDs. Roblox launch URLs and authentication tickets are never logged.
- The worker checks the process owner, interactive session, executable name, and Roblox Authenticode publisher. It checks the Event object type and exact final name, and rechecks the remote handle before closure. Similarly named events and mutexes are left alone.
- Remote handle closure has a small unavoidable race: Windows has no atomic compare-object-and-close-remote-handle operation. Tests use a private object-manager directory to avoid touching the real singleton.
- Setup checks task execution from an actual unelevated token, installed-file ownership/write protection, and task modification denial before changing launch links.
- This version targets an administrator account with UAC enabled. Approving setup with a different account is rejected. It does not install Bloxstrap.

## Build and tests

Use Windows x64 and a supported .NET 10 SDK. The SDK selection is defined in `global.json`. The source uses Windows Forms for warnings and Windows APIs for handle inspection and closure. Self-contained publishing downloads Microsoft runtime packages.

```powershell
./Test.ps1
./Build.ps1
```

The default suite needs no Roblox installation or administrator access. It covers argument forwarding, process identity checks, registry backup round trips, overlapping launch accounting, deadlines, and malformed pipe messages. It creates and removes a uniquely named temporary test registry subtree.

For the two additional integration checks, use an elevated PowerShell session with official Roblox installed:

```powershell
./Test.ps1 -Integration
```

These checks validate the Roblox signature and real handle closure in a controlled child process, preserving unrelated objects. The main launcher is `release/RobloxLaunchHelper.exe`; keep `release/` limited to this single executable so older builds cannot be mistaken for the current launcher. Put experimental builds, build caches and reports under `.build/`. Both directories are ignored by Git. Build scripts restore the caller's environment settings when they finish.

Browser/game behavior depends on the installed Roblox version. Process counts alone do not verify separate account sessions: manually confirm both accounts remain in-game and the helper exits. The helper does not alter Roblox binaries or guarantee compatibility with future client changes.

## Repository layout

- `src/`: launcher, installer, scheduled worker, and Windows API bindings.
- `tests/`: deterministic checks and opt-in integration tests.
- `Build.ps1` and `Test.ps1`: portable developer commands.
- `docs/windows-build.yml.example`: optional GitHub Actions workflow template.

See [CONTRIBUTING.md](CONTRIBUTING.md) for development and privacy expectations and [CHANGELOG.md](CHANGELOG.md) for project history. Runtime installation backups, diagnostics, executable binaries, and account data do not belong in Git.
