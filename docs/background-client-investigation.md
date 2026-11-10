# Background Roblox client investigation — November 9, 2026

Status: Roblox tray behavior established for the available logs; the first-use-after-reboot sequence is not yet reproduced. No unintended app launch was found, so there is no speculative launch suppression or cleanup timing change.

## Captured evidence

Times below are America/New_York (EST). The read-only CIM query reports the last boot as November 8 at 12:29:31.500 PM. The current development manager is PID 34416, created November 9 at 11:30:46.303793 AM, parent PID 31372 (`explorer.exe`), command line pointing to `.build/desktop/RobloxMultiInstance.exe` with no arguments. No Roblox player was running at the initial query. Studio appeared later during investigation; it is a separate product, not a background player launched by these checks.

Local source logs under `%LOCALAPPDATA%/Roblox/logs`:

The read-only registry check also found `HKCU/Software/Microsoft/Windows/CurrentVersion/Run`, value `RobloxPlayerBeta`, pointing to `C:\Users\Lance\AppData\Local\Roblox\Versions\version-f0ea5da9412f4e35\RobloxPlayerBeta.exe` with `--launch-to-tray`. This is a direct Roblox startup registration, independent of manager startup. Its presence explains a possible sign-in tray launch but is not historical proof that Windows executed it on the last boot. No registration was changed.

| File | Evidence |
| --- | --- |
| `0.746.0.7461267_20261108T173714Z_Player_8ABC2_last.log` | `AppState/TrayMode` at November 8, 12:37:14.245 PM, about eight minutes after boot. This identifies tray mode, but does not establish its original parent or whether an earlier manager session/game launch preceded it. |
| `0.746.0.7461267_20261109T122142Z_Player_B2FBC_last.log` | Tray startup at 7:21:42.246 AM; `Starting background update timers`; at 11:21:42.383 AM, `appRestartTimer Fired`, followed at 11:21:42.416 AM by `Successfully launched new process in systray mode`. |
| `0.746.0.7461267_20261109T162145Z_Player_2B6E4_last.log` | Replacement starts in `AppState/TrayMode` at 11:21:45.246 AM, about nine minutes before the current manager opens. |

The helper log at `%LOCALAPPDATA%/RobloxLaunchHelper/logs/helper.log` has no matching launch entries for the morning tray restarts; its last `roblox-launched` entry before them is November 9 at 12:27:07.4525556 AM, PID 60308. Absence of a rotated/missing log is not proof of absence of an app action.

The ignored `.build/startup-evidence/*-ManagerOpen.jsonl` capture contains boot time, current process creation times, parent metadata/command lines and relevant log extracts. Process-start event subscription was unavailable (`Call cancelled`), recorded explicitly; snapshots still worked. Historic tray processes had exited, so their CIM creation times, PIDs and command lines cannot be recovered from this snapshot. Log timestamps are application activity times, not OS process creation times. The current evidence proves a tray process before **this** manager opening, not before every manager opening since boot.

## Code audit and regression coverage

- `src/desktop/host/Entry.cs` opens the manager; `MainWindow.cs` initializes UI/backend services. There is no player start in this startup route.
- `DesktopBackend.ReadinessAsync` inspects installation/health and evaluates the cached probe result; it does not launch a player or run a fresh worker probe. For explicit worker tests, `Launch.VerifyAsync` sends `PROBE`; `Worker.cs` replies `OK` and returns. Neither path starts Roblox.
- `Launch.RunCoreAsync` contains the explicit player `Process.Start`. It runs for desktop launch commands, account launches, or helper URI/explicit launch requests. `ProductionPlayer.ResolveAsync` only fetches metadata and selects an installed signed player; it does not bootstrap a second player. Account launch calls this shared launch once per account.
- Startup logging now records `desktop-entry` before backend construction. `launch-baseline` records existing player identities/creation times and visibility before prelaunch cleanup. `roblox-launch-origin` records the requesting PID, executable, player PID and launch mode. Authentication arguments/URIs and window titles are omitted.
- The UI regression covers startup, refresh and reload with saved launchable accounts and verifies no launch command is sent. Existing shared startup-routing tests distinguish opening the app from explicit player launch. A new evidence-format test verifies identity, visibility, uncertainty and omission of private titles. These fixtures do not establish Roblox's native tray behavior.

## Finish the cold-boot investigation

Validation: `Test-Shared.ps1` passed 143/143; frontend production build and desktop host compilation passed; the focused startup UI test passed. `Test.ps1` passed native/backend/bridge checks and 126 UI cases (10 skipped), then stopped on an unrelated supporter test's missing trace file during browser-context teardown. That test passed when rerun with isolated output. `scripts/testing/Test-Features.ps1` subsequently passed separately. The capture script was exercised read-only against live process metadata/logs and the Roblox Run entry. These checks do not replace the reboot capture below. Changes remain uncommitted; the running development executable was not replaced.

After the next reboot, before opening the manager or Roblox, run from the active checkout:

```powershell
./scripts/Capture-RobloxStartup.ps1 -Phase BeforeManager -WatchSeconds 600
```

Leave that observer running while opening the manager and performing exactly one intended launch. In a second PowerShell window, use these snapshots to mark the boundaries:

```powershell
./scripts/Capture-RobloxStartup.ps1 -Phase ManagerOpen
# Perform one launch in the manager, then:
./scripts/Capture-RobloxStartup.ps1 -Phase AfterFirstLaunch
```

The collector starts/stops no application and changes no startup settings. It records boot/phase times, OS process creation times, PID/parent PID, available paths and redacted command lines, parent snapshots, and safe tray/helper log markers. Run in normal PowerShell; if CIM access is denied, use an administrator PowerShell for observation only. Output defaults to `.build/startup-evidence`. Keep continuous capture running until the extra client appears, up to 30 minutes if necessary. Short-lived processes can be missed if event subscription fails; exited/inaccessible parents and PID reuse must not be treated as proven ancestry. Parent creation later than the child disproves that parent identity.

Classify the first appearance against the phase markers and `desktop-entry`/`launch-baseline`/`roblox-launch-origin`: already present before opening; created while opening without a launch request; or created after the first explicit player launch. If Roblox logs report a tray spawn and ancestry agrees, attribute that child to Roblox. If an extra direct manager/helper start is captured, fix the request/start path producing it and add a regression for that trigger. Do not solve attribution by shortening cleanup delays. A reboot, real-game launch, or tray-setting change was not performed during this investigation.
