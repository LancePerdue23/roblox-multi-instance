# Replaced Roblox sessions

The existing background cleanup is for windowless clients only. Its optional desktop monitor requires a full minute of continuous windowless observations, and launch startup also checks old windowless clients. Neither removes a visible disconnected game window.

After a helper-confirmed launch, the shared launch path now checks for an older visible client displaced by a newly started local client. It requires process-owned local Roblox logs to establish the same numeric account ID, an explicit Network disconnect reason 264 or 273 in the old process, and no disconnect in the replacement. The disconnect must follow the replacement process's creation and be recent. Ambiguous/missing logs, incomplete process discovery, unrelated accounts, ordinary disconnects, and already-existing replacement processes are skipped.

Immediately before closing, it revalidates both process lifetimes, Windows owner/session, non-elevated state, Roblox signatures, account/disconnect evidence, and the old window's ownership. It sends one graceful close request to exactly one old window. It never force-terminates a visible client, closes a working client just because it shares an account, or follows an account's game on another device. If the old client has rejoined or the replacement exited/disconnected, it leaves everything alone.

Account IDs and log contents stay in memory and are not sent to telemetry or written into diagnostics. Cleanup has a six-second deadline, introduces no resident service, and cannot change a successful launch into a failure. Changed Roblox log formats fail safely by skipping cleanup. This currently runs after verified startup, so a replacement that has not produced sufficient log evidence by then may be left for manual closure.

Fixture tests cover strict log markers, rejoining, unrelated accounts, stale evidence, PID reuse, changed Windows ownership/elevation, replacement exit, cancellation, and graceful close targeting. Real two-account gameplay acceptance remains manual; fixture tests never launch or stop Roblox.

Desktop launches use the rebuilt shared library immediately. Browser Play launches use the installed helper, which needs the rebuilt installer before this change affects browser launches. Building does not install or replace that helper.
