# Silent Roblox startup exits

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
  before launch. Two seconds without a replacement after starter exit produces a
  `Failed` / `PlayerExited` history entry and notification, rather than a 60-second
  timeout. Failed or partial inspection cannot establish an exit.
- Watches startup for at least eight seconds even after worker confirmation, and
  continues while waiting for the worker or an exit's handoff grace period. A
  surviving replacement prevents an early-exit failure; this is not proof of sign-in
  or game entry. Concurrent launches may conservatively mask an exit.
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
