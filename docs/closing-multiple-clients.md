# Closing multiple Roblox clients

Closing one client is not designed to close the others. The desktop's Close window command sends WM_CLOSE to one verified process-owned window. Background cleanup excludes visible clients. Replaced-session cleanup requires the same numeric Roblox account and replacement disconnect evidence; different accounts do not qualify.

## Observed tray replacement interaction

The November 7, 2026 local reproduction used different accounts. At 9:40:58 PM Eastern, the first Roblox player logged `Successfully launched new process in systray mode` while closing. Roughly 540 ms later, the other player's WndProcessCheck logged a successful mutex wait, a failure waiting on its named single-instance event, and `Received close main window request`. Both clients disconnected with client-initiated reason 285. A new tray-mode process appeared. The launch helper had already exited at 9:40:50 PM, before either shutdown.

This evidence points to Roblox's close-to-tray replacement interacting with its single-instance checks, rather than an account-conflict kick or the desktop issuing a close to every window. It does not establish that every window close, client version, or tray configuration has this behavior.

In Roblox's own Settings → Device preferences, turn off **Minimize to tray on close** (wording may vary), then retest with different accounts. This is Roblox's setting, separate from this application's tray mode. That live retest remains necessary to verify the workaround. Do not change Roblox flags, remote handles, or local preferences automatically on this evidence alone.

## Stop all

Instances → Active instances has a **Stop all…** button beside Close window. It works without selecting rows. Its confirmation states the number of distinct listed clients and warns that games are forcibly ended and unsaved progress may be lost. Background clients are included; clients first discovered after opening confirmation are excluded.

The backend requires confirmation and resolves only the frozen opaque instance IDs. It deduplicates process lifetimes and rechecks process creation time, executable name, Windows owner/session, elevation, and Roblox publisher before terminating through the same held process handle. It does not send WM_CLOSE or terminate a process tree. Stale IDs require refreshing and reviewing again. Results report stopped, skipped, failed, and unconfirmed counts; a termination request alone is not reported as a confirmed exit.

Fixture tests exercise this without launching or stopping real Roblox. Normal Roblox-window X behavior remains controlled by Roblox.
