# Roblox Multi Instance

The upcoming 1.1.0 build adds a local health dashboard, recent launch results, and redacted troubleshooting exports. It retains the 1.0.1 fix that applies and verifies browser registration from Windows's normal user context, including when setup starts from a registry-isolated environment.

A Windows x64 launcher that closes Roblox's `ROBLOX_singletonEvent` Event handle after each launch, allowing separate clients to remain open. It uses the official Roblox player already installed on the PC. The executable remains named `RobloxLaunchHelper.exe` for compatibility with existing installations.

## Install and use

1. Double-click `RobloxLaunchHelper.exe` and approve the Windows administrator prompt with your current account.
2. Use Roblox's normal browser Play buttons or the new **Roblox Multi Instance** desktop shortcut.
3. Use separate Roblox accounts/browser sessions as needed. This helper does not manage accounts or sign-ins.

Setup installs the executable under `%ProgramFiles%\RobloxLaunchHelper\<Windows SID>`, registers one on-demand elevated task for the current account, and wraps the per-user `roblox` and `roblox-player` launch links. No startup entry, scheduled timer, service, or permanent watcher is installed. The worker exits about two seconds after completing its work; each launch has a 60-second observation deadline after Roblox starts.

The desktop shortcut supplies Roblox's `-app` flag. Browser launches forward their original protocol link unchanged. Setup/repair use a temporary protected installer task to operate in the real Windows user context and temporary least-privilege checks for permissions, protocol registration, and shortcuts. These tasks are deleted on completion; only the on-demand handle task remains.

Roblox runs without elevation. Only installation, repair, removal, and the short-lived handle worker use administrator access. The executable is self-contained; a separate .NET installation is unnecessary. It is a local unsigned build, so Windows may identify its publisher as unknown.

## Repair and remove

The Start menu's **Roblox Launch Helper** folder contains **Repair**, **Uninstall**, and **Diagnostics** shortcuts. Equivalent commands:

```powershell
.\RobloxLaunchHelper.exe --setup
.\RobloxLaunchHelper.exe --repair
.\RobloxLaunchHelper.exe --uninstall
.\RobloxLaunchHelper.exe --diagnostics
```

Roblox updates may replace either launch registration or its version-specific executable. If launches stop using the helper, launch/reinstall official Roblox normally and then run **Repair**. Repair saves the current verified Roblox launch command and restores the wrapper. There is no background updater.

Uninstall removes the task, owned shortcuts, and installed program files. It restores backed-up protocol keys only when they still point at this helper, preserving registrations that another installer has since changed. Small launcher diagnostic logs under `%LOCALAPPDATA%\RobloxLaunchHelper\logs` are retained for troubleshooting.

Setup logs from the downloaded executable are retained under `%ProgramFiles%\RobloxLaunchHelper\setup-logs`. Repair invoked from the installed Start menu shortcut records its setup status in the installed `logs` folder.

## Diagnostics dashboard and reports

Open **Diagnostics** in the Start menu, or run `RobloxLaunchHelper.exe --diagnostics`, normally without administrator access. The local Windows dashboard checks installation settings, the saved Roblox player and publisher, both browser protocols, the on-demand task configuration, and effective permissions. Opening it or clicking **Refresh** does not start a worker, launch Roblox, close handles, or repair the installation. Permission inspection is read-only; an inconclusive check is **Unknown**, not a pass. Registry results describe this process's view. Setup/Repair still verifies registration in Windows's normal user context separately.

**Test worker** explicitly starts or connects to the existing authenticated on-demand worker and sends only a probe. It does not scan Roblox handles; the worker exits normally when idle. A test has a 35-second total limit. Refresh and Setup/Repair invalidate its previous result. An idle worker is normal. **Repair** and **Setup** use the existing administrator approval flow only when selected. Buttons open the user, worker, and setup log folders separately.

Recent results distinguish a confirmed singleton release, Roblox starting without confirmed multi-instance readiness, a failed launch, and an interrupted launcher whose completion is unknown. Confirmation does not establish account sign-in or that either account remained in-game. Existing text logs are not reconstructed as launch outcomes, so history starts with this version. Successful launches remain silent.

Structured history is stored under `%LOCALAPPDATA%\RobloxLaunchHelper\diagnostics`. It retains at most 100 attempts and 30 days, with a 512 KiB store limit and 4 KiB record limit. Pruning happens only while the launcher or dashboard runs; expired files can remain on disk while the helper is unused, but expired records are excluded when accessed. Launchers wait at most 100 ms for the shared history lock. Inaccessible, busy, or invalid storage cannot prevent Roblox launching. **Clear history** removes structured history, including recovery from a corrupt store; it does not remove text logs or reports already exported. Concurrent launches may create new records afterward.

**Export report** previews the included categories/counts and lets you choose a ZIP destination. The ZIP contains `summary.txt` and `diagnostics.json`: versions, current check results, the last explicitly requested worker test, UTC timestamps/durations, up to 100 recent attempts, and at most 200 approved log events. It reads at most 512 KiB per known log source pair and limits uncompressed report contents to 1 MiB. Missing or truncated sources are listed as omissions; export never silently starts a new worker test.

Reports exclude usernames, computer/domain names, SIDs, process/session IDs, persistent attempt IDs, full paths, launch URLs/tickets, cookies, account/game identifiers, raw commands, registry backups, stack traces, arbitrary exception text, and raw log attachments. Export uses approved fields and event codes rather than trying to scrub arbitrary log text. Versions that are not simple numeric versions are omitted as unknown. Local history uses a random attempt ID and transient launcher PID/creation time to distinguish interrupted attempts; those identifiers are not exported. Review reports before sharing: timestamps and launch activity can still be sensitive. Nothing is uploaded or copied to the clipboard. Delete exported ZIPs yourself; clearing history does not delete them. Runtime data and exports must remain outside commits.

## Diagnostics and behavior

- Normal success is silent. If the worker fails, Roblox remains running and a warning identifies the logs.
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

These checks validate the Roblox signature and real handle closure in a controlled child process, preserving unrelated objects. The built executable is written to `release/`; build caches and reports are written under `.build/`. Both directories are ignored by Git. Build scripts restore the caller's environment settings when they finish.

Browser/game behavior depends on the installed Roblox version. Process counts alone do not verify separate account sessions: manually confirm both accounts remain in-game and the helper exits. The helper does not alter Roblox binaries or guarantee compatibility with future client changes.

## Repository layout

- `src/`: launcher, installer, scheduled worker, and Windows API bindings.
- `tests/`: deterministic checks and opt-in integration tests.
- `Build.ps1` and `Test.ps1`: portable developer commands.
- `docs/windows-build.yml.example`: optional GitHub Actions workflow template.

See [CONTRIBUTING.md](CONTRIBUTING.md) for development and privacy expectations and [CHANGELOG.md](CHANGELOG.md) for project history. Runtime installation backups, diagnostics, executable binaries, and account data do not belong in Git.
