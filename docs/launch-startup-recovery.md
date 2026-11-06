# Silent Roblox startup exits

November 5 account-batch debugging also found `NoReload Detector: WebPlay NoReload with Tray only!`: the protocol starter exits after handing the request to an existing tray process. A new-process singleton confirmation cannot confirm that reused lifetime. Saved-account launches now use native `--app`, explicit `-a` authentication, `-t` ticket and `-j` join URL as separate arguments. Explicit authentication prevents warm launches from inheriting the shared desktop session. Account identity and process creation time still require an owned join log before success. Browser launches continue to use their original protocol contract.

The same live investigation found experimental-channel switching launching RobloxPlayerInstaller, which enumerated and closed all six existing clients. Saved accounts now pass `-channel production` and preflight the public production version against exact installed, signed player paths. Missing production builds fail before starting a player or installer. No arbitrary installed version is selected by timestamp. A final live four-account batch completed with four unique verified lifetimes and the same observed server; each log reported production and no required update. The owner separately confirmed the staged 10 FPS cap using Shift+F5.

Observed failure: browser Play starts Roblox, but the new player immediately exits
with `SingleInstanceGuard.cpp` / `Invalid window handle`. The helper previously
waited for a 60-second singleton timeout. Fully closing games can leave a new
windowless tray client, so closing game windows alone does not guarantee recovery.

The shared launch service now:

- Inspects background clients before launch. Only clients at least one minute old,
  present in two snapshots 500 ms apart, with no game or other visible windows,
  qualify for cleanup. Any visible or uninspectable client disables this cleanup.
  The existing cleanup service rechecks process creation time, owner, session,
  elevation, signature and visibility on the same process handle before stopping it.
- Observes starter exit and trusted replacement players, excluding processes present
  before launch. A two-second empty interval after starter exit can establish exit
  only after the minimum eight-second observation and the worker's bounded result.
  A process gap during bootstrap must not cancel a pending singleton scan. Failed
  or partial inspection cannot establish an exit.
- Finishes a healthy observation when the worker confirms cleanup, without an
  unconditional eight-second success delay. Suspected exits retain the eight-second
  minimum and handoff grace, and cannot cancel a pending worker scan. A surviving
  replacement prevents an early-exit failure; confirmation is not proof of sign-in,
  game entry or survival after observation. Concurrent launches may mask an exit.
- Records warnings/failures even when progress/completion preferences are disabled.
  Desktop-edition launches can present them through the separate desktop notification
  host. Helper-only browser launches remain windowless; inspect `rmi status`,
  diagnostics, and launch history for outcomes.

Running games are never closed for launch recovery. No launch URI is retried (its
authentication ticket may be single-use). When the remaining background state
cannot be cleared safely, the notice asks the user to save progress, close Roblox
games, exit Roblox in the Windows system tray, and try Play again. This addresses
safe stale-client cleanup and feedback; it does not guarantee prevention of Roblox's
internal single-instance failure or detect crashes after the startup observation.

`Test-Shared.ps1` covers stale-client selection, game/unknown-window exclusions,
process lifetime changes, handoffs, existing-game masking, early worker completion,
deadline grace, cancellation, and failure notification with both preferences off.
These are fixtures; no real Roblox processes are started/stopped by the tests.

Browser Play uses the installed helper, so rebuilding only the desktop does not
activate this change. Use the matching edition's installer to update that protected
installation; validate live browser launches separately from fixtures.

November 4 investigation: the available helper history ends on November 3. Its last
two attempts include early startup-exit/cancellation outcomes, and the worker log
records singleton closure followed by a disconnected client. The observer now waits
for the worker result through transient process gaps. November 4 Roblox player logs
exist without corresponding helper log entries. A user-triggered reproduction was
traced without recording command lines: Chrome (PID 3388) directly started the
player (PID 42816), bypassing the helper, despite Windows' current association query
resolving both Roblox schemes to the helper. The installed helper's read-only probe
succeeded. Cache refresh and browser restart did not restore routing. A subsequent
cross-check through Windows' `root/default:StdRegProv` registry service revealed the
live user Classes hive actually pointed both schemes directly at Roblox. The agent
shell's registry view differed from that live view; its successful registration
queries were not evidence of the browser's actual registration.

The live commands were restored to the existing installed helper through the local
registry service, after validating the installation owner, enabled integration
preference and exact original official commands against installation.json. Only
the default command value for each scheme was changed; other values/subkeys and
the installation's original backups were preserved. The pre-repair live commands
were saved locally in `.build/browser-protocol-before-repair.json`. Readback through
the same service verified both repairs. No installation or Roblox restart was
performed. User-triggered browser retests subsequently reached the helper. Worker
logs recorded singleton closure for new players 33636 at 14:11:08, 20088 at
14:11:10, and 21224 at 14:12:17 (local time, November 4). This verifies restored
browser routing and actual closure in those attempts, not universal launch success;
another intervening attempt had no matching closure at the time of inspection.

The apparent roughly threefold successful launch duration is consistent with the
unconditional eight-second observation introduced previously: older confirmed
attempts took approximately 3.4 seconds, while a later attempt took approximately
9 seconds. Healthy cleanup now returns when the worker completes; a deterministic
fixture confirms a worker result at 3 seconds completes observation at 3 seconds.
This is an observation-duration correction, not a measured Roblox loading speedup.
