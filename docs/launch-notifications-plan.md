> Historical feature validation notes. Current desktop testing uses root Test.ps1 and scripts/testing/Audit.ps1; shared/helper checks use Test-Shared.ps1 (including -Integration when explicitly required). Retired UI instructions below are historical. See [milestone audit](milestone-audit.md).

# Optional launch notifications plan

Status: implemented locally. Automated model, transport, history, preference, and UI checks cover the implementation; live Roblox/browser and Windows notification-suppression acceptance remains manual. Successful launches remain silent by default. See `docs/diagnostics-validation.md` for installed acceptance.

## Preferences and presentation

Add **Launch notifications…** to Diagnostics with two independent, unchecked options: **Show launch progress** and **Show confirmed helper completion**. Progress enables a temporary, nonmodal status panel; completion enables a passive completion notification. Neither option enables tray mode, global shortcuts, startup registration, or a permanent process. Existing warning/failure feedback remains available with both options off.

Store versioned preferences under the current user's LocalApplicationData folder, separately from protected installation settings and window-layout preferences. Missing preferences mean both options off. Invalid, unsupported, inaccessible, or busy storage also means optional notifications off; show a fixed settings error when the settings UI is opened and require an explicit reset before overwriting unsupported data. Setup/Repair preserves the preference. Notification errors must never prevent or delay Roblox launching or alter the launcher exit code.

Progress appears only after an attempt remains active for 750 ms, avoiding a flash for quick launches. It never takes foreground focus, moves Roblox windows, plays sound, or asks for UAC. Closing it dismisses presentation for the current active group; it does not cancel launches. New attempts arriving before that group drains remain dismissed. A later group may show progress again. Turning an option off dismisses its current presentation and drops its queued output.

## Shared status and truthful wording

Use `LaunchResult`, `LaunchOutcome`, `DiagnosticCode`, and `AttemptId` in `src/DiagnosticModels.cs` as the source of truth. Add a typed transient phase and immutable status snapshot alongside that model; do not create a notification-specific definition of launch success. Diagnostics and notifications should share a presenter for status meaning, with shorter notification text where needed.

| Evidence / shared state | Display wording | Meaning and limits |
| --- | --- | --- |
| Attempt begins, before player startup | “Starting Roblox…” | The launcher is preparing the request; no player startup is established. |
| `Process.Start` returns a process; `RobloxStarted = true`, still in progress | “Roblox player started; waiting for helper confirmation…” | A process was started. It may exit immediately; window creation, account sign-in, and game entry are unknown. |
| Terminal `Confirmed` / `LaunchConfirmed` | “Helper complete — singleton release confirmed.” | The existing worker protocol returned `OK` for this launch request. This does not establish continued player execution or worker-process exit. |
| Terminal `Warning`, player started, worker unavailable / timeout / unconfirmed | “Roblox player started; helper completion unconfirmed.” | Include the existing fixed diagnostic reason and next step. Never describe this as confirmed completion. |
| Terminal `Failed` | “Launch failed.” | Use the fixed diagnostic reason. Say the player could not start only when that is known; do not infer it from every failure. |
| Reconciled `Incomplete` / `Interrupted` | “Launcher interrupted; completion unknown.” | Do not infer whether Roblox started or stopped. Show on explicit history review; do not replay as a new notification. |

The progress panel and notification details state: **“Helper status does not verify account sign-in or joining a game.”** Never display “Account launched,” “Joined,” “In game,” or a game/account identity. Do not correlate attempts with manager rows, window titles, process-count increases, or nearby timestamps. Preserve the worker's existing overlapping-request accounting and its limits; notifications add no stronger association guarantee.

Refactor `LaunchAttempt` to expose a stable attempt ID and publish snapshots for begin, player-started, and terminal completion, with a strictly increasing per-attempt revision. Keep intermediate phases transient so the persisted history schema need not change. Continue using bounded, best-effort history storage; history failure must not disable live status or launching. Do not poll history as a progress stream: `Started()` currently changes only memory, and history can be cleared or unavailable.

`Complete()` currently sets provisional failure/warning values before risky operations. Separate those fallback values from terminal publication so signature checking and process startup do not emit false final notifications. Finalize exactly once, attach end time/duration, attempt the history write, and publish the same final snapshot without depending on that write succeeding. The warning path currently shows its modal dialog before disposal; move finalization before presentation so a dialog left open does not keep an attempt falsely active. Late or duplicate status cannot regress a terminal attempt.

## Overlapping launches and bounded lifetime

Use one temporary, unelevated notification host per Windows user and interactive session. Start it on demand when either optional preference is enabled, or when warning/failure feedback needs coordination. Reuse its one UI thread and one progress surface for all concurrent launcher processes. Keep it separate from `ManagerHost`: launch notifications must work when Diagnostics and explicit tray mode are closed, and closing notifications must not stop the manager. Do not add the presentation role to the elevated worker.

Use a session-local single-instance guard and a separate bounded IPC endpoint. Review and reuse the existing peer-validation principles, with endpoint ACLs and validation of the expected executable, user, session, and normal privilege context. Do not weaken or reuse the worker's command endpoint. Allow only typed, size-limited status messages and acknowledgments; no raw URIs, command lines, arbitrary text, file paths, or instructions to launch programs. Authenticate both ends. A status message is presentation data and never authorization to perform a privileged action.

Each launcher sends snapshots asynchronously with a small bounded queue, retaining its newest and terminal snapshot preferentially. After host startup/reconnection, send the current snapshot rather than replaying all phases. Use a 100 ms maximum enqueue/connect budget per publication; delivery failure falls back without retrying on the critical launch path. Settle the host-start race with the guard and bounded connection retry in the background, never by waiting for a UI acknowledgment before starting Roblox.

Keep at most 100 live/recent attempt entries and a bounded aggregate overflow count. Deduplicate by attempt ID/revision, reject regressions, and discard late progress after terminal completion. Clear the in-memory completed-entry cache after the host exits; it must not read/replay historical attempts on startup. Track launcher lifetime by PID plus creation time for active entries, without treating a dead IPC connection alone as an interruption. If an owner exits without a terminal snapshot, mark completion unknown rather than manufacturing success or failure. Use existing bounded launch deadlines plus a short delivery grace period to expire unverifiable/stale entries; expiration is unconfirmed, never successful. Bound and dispose all IPC tasks, timers, and UI resources.

When idle, close the host after any visible result and a short, bounded dismissal grace period (at most 10 seconds). Coalescing uses timers and never waits indefinitely for every launch in a group to finish. Preserve the elevated worker's normal idle exit and existing deadlines; host lifetime does not control worker lifetime. No polling or notification process remains after the grace period unless the user separately enabled the existing tray mode.

## Notification volume policy

- Show one progress panel with aggregate active counts, updated at most four times per second. Do not create a popup for each phase or attempt. Provide optional bounded details using local attempt labels, without account labels or persistent IDs.
- If confirmed-completion feedback is enabled, collect terminal events for a two-second window and show at most one completion popup per ten seconds across the session. Merge pending counts during the cooldown; after it expires, deliver one summary even if other launches are still active. A single attempt produces at most one terminal contribution. There is no backlog of individual popups.
- Report mixed results as separate counts, for example: “2 helper completions confirmed; 1 completion unconfirmed; 1 launch failed; 2 still in progress.” Count attempts, not players or accounts. Do not use a green all-success message when a batch has warnings or failures. With completion feedback off, omit routine confirmed counts from warning popups; progress may still show them in its aggregate view.
- Route existing launch warnings and failures through the same coordinator, independently of optional-success preferences. Present one nonmodal warning surface with merged counts and fixed remediation text, updated in place and opened at most once per ten seconds. Keep per-attempt outcomes available in recent history when storage works. Suppress the old per-launch warning dialog only after acknowledgment that the coordinator accepted responsibility for that warning; transport enqueue alone is insufficient.
- If the coordinator cannot accept a warning, preserve the current local warning/error fallback. This degraded case can still show multiple dialogs during simultaneous failures; successful launches remain silent. Never silently discard the only warning while trying to enforce the normal popup limit. Avoid a second `Program.Main` dialog for an error already presented by the coordinator.
- Use a passive Windows notification adapter for confirmed completion and a bounded nonmodal surface for actionable warnings. Verify delivery on supported Windows versions and report platform suppression honestly: notification display is best effort, with no repeat spam when Windows suppresses it. A click may open Diagnostics through a fixed internal action; it must never relaunch Roblox or replay a launch link.

## Implementation sequence

1. Extract shared status snapshots/presentation and refactor attempt finalization. Preserve outcome codes, protocol semantics, history compatibility, privacy allowlists, and exit codes. Add transient phase publication at existing launch milestones.
2. Add versioned user preferences and the Diagnostics settings dialog, with both options off. Introduce injectable clock, transport, and notification adapters.
3. Implement the temporary per-session host, authenticated IPC, bounded reducer, lifetime cleanup, aggregation, and passive presentation. Keep launch behavior independent of notification delivery.
4. Route warning/failure presentation through acknowledged coordinator delivery with existing local fallback. Review `Launch.RunCoreAsync`, `LaunchAttempt.Dispose`, and `Program.Main` together for exactly-once finalization and error presentation.
5. Validate before updating README/CHANGELOG with delivered behavior. Keep this plan marked proposed until implementation and acceptance are complete.

## Verification and acceptance

Use deterministic tests with fake clocks/transports to cover defaults, all preference combinations, every status row above, finalization exactly once, provisional outcomes, out-of-order and duplicate messages, disconnect/reconnect, stale owners, malformed IPC, queue/entry limits, and history unavailable/cleared during a launch. Verify privacy canaries for launch URLs/tickets, account data, titles, paths, and arbitrary exception text; new transient status must not enter report exports.

Test two overlapping attempts completing in either order, mixed confirmed/unconfirmed/failed outcomes, a continuous stream of launches, warning acknowledgments lost or delayed, host startup races, dismissal, option changes, notification suppression, and host crash. Assert the four-per-second progress and ten-second popup limits with exact counts, including a trailing summary when arrivals stop. Verify fallback errors remain visible and launches do not wait for delivery.

Run `Test.ps1` and `Build.ps1` with the supported .NET 10 SDK for implementation changes. Live Windows acceptance must cover desktop and both protocol schemes, normal-token operation, separate user/session isolation, Diagnostics/tray open and closed, denied focus, and worker/notification-host exit. With default settings, confirmed launches produce no progress panel, success popup, or notification host. With options enabled, manually check overlapping launches and truthful wording. Separately verify two accounts remain in-game inside Roblox; neither notification output nor synthetic tests establishes that behavior.
