# Active-instance manager implementation plan

Status: implemented in the local application. Automated safety checks and synthetic rendering cover the manager; live normal-token Roblox/gameplay acceptance remains a separate manual check. The design and acceptance criteria below describe the implementation and its platform limits.

## User experience and scope

Add an **Active instances** button to Diagnostics opening one owned, modeless Windows Forms panel. Keep Diagnostics checks and launch history unchanged. Opening the panel starts local discovery immediately; closing it stops discovery. Normal launches remain silent and install no watcher, service, startup trigger, or scheduled polling task.

Show one parent row per verified official Roblox player belonging to the current Windows user and interactive session, including clients launched outside this helper. Columns: window title(s), PID, process started (local date/time), CPU (% of total logical CPU capacity), working set (MiB), and state. Use creation time for the start column, not discovery time or a recent launch attempt. Store creation time in UTC and format locally. Do not infer a browser source or launch-history association from timing.

Provide **Focus** and **Close window…** for a selected window. If a process owns multiple eligible windows, show child rows or an explicit window picker; never guess which window to close. Resource counters belong to the process parent and are not duplicated or summed per window. Keep windowless clients visible as **No window yet**, with actions disabled. Empty titles display **Untitled window**. Titles may change or be identical across clients. Preserve selection by process key and window handle, never row index or title; a disappearing selection disables actions rather than moving them to the next row.

Display a short note: “Window titles are supplied by Roblox. They do not identify or verify accounts.” Do not parse usernames, sign-in state, game identity, or account relationships from captions, process counts, or helper launch outcomes. Do not add account management.

## Existing identity checks reviewed

| Existing code | Reuse and limits |
| --- | --- |
| `src/NativeHandles.cs`: `ProcessKey(int Id, long Created)` | Reuse as the lifetime key everywhere: rows, trust, CPU baselines, selections, and action requests. `Created` is the raw Windows creation FILETIME from `GetProcessTimes`. PID alone is insufficient. |
| `Native.GetIdentity(SafeProcessHandle, int)` and PID overload | Reuse handle-based creation time, image-path, and token-owner queries. Most fields are handle-bound, but session currently comes from `ProcessIdToSessionId(pid)`; add a handle-token `TokenSessionId` query for the manager so a recycled PID cannot supply a different process's session. Check token buffers and failures. Keep existing callers compatible and regression-test any shared refactor. |
| `src/Worker.cs`: `ScanService.Scan` | Reuse the policy of matching owner SID/current session and caching Roblox trust per process lifetime. Do not call this scan from the manager: it opens duplicate-handle rights and closes singleton events. Factor a small identity policy only if it avoids duplicating rules without coupling discovery to handle closure. |
| `ScanService.CurrentProcesses` | Not an authorization filter: it collects candidate PID/creation pairs without owner/session/publisher filtering. Do not use its results as trusted rows or action targets. |
| `RobloxTrust.IsSignedRoblox` | Reuse exact `RobloxPlayerBeta.exe` basename plus Authenticode/Roblox Corporation publisher validation. Enumeration name is only a prefilter; the queried image path must pass validation. This checks the file at that path, not an attestation of all loaded process code. Do not weaken its checks for speed. |
| `src/DiagnosticHistory.cs`: `IsAlive` | Reuse the principle of matching PID and start time, not its timestamp representation. History stores `DateTime.Ticks`, while `ProcessKey.Created` stores FILETIME. Do not compare those longs directly or join active rows to history owners (which identify launchers). |
| `Protocol.IsPeer`, `VerifyClient`, `VerifyServer` | Keep unchanged: pipe authentication has a different role from player authorization. The panel requires no worker connection or elevated endpoint. |

## Discovery and process lifetime

Create `ActiveInstances.cs` for typed snapshots, a collector interface, lifecycle controller, and sampling rules; add narrowly scoped process/window bindings in `ActiveInstanceNative.cs`. Keep the form in `ActiveInstancesForm.cs`. Use injectable collectors, clocks, and action adapters for deterministic tests.

1. Enumerate player-name candidates off the UI thread. Open a `SafeProcessHandle` with only the query rights needed for identity and metrics. Bind every read to that handle; avoid later PID-based `Process` properties that might open a replacement process. Add synchronization rights only if the chosen exit check requires them; never request debug privilege, duplicate-handle rights, or termination rights.
2. Read owner, token session, image, and creation time from that handle. Require current SID/session and the exact official player basename. Verify the publisher on first discovery of each `ProcessKey`; retain trust only while that lifetime remains observed. Cache results in memory only, and remove entries on exit, loss of verification, or panel shutdown. Retry transient validation errors on later refreshes without treating them as trusted. Actions perform a fresh trust check.
3. Read cumulative user/kernel CPU time and working set using the same handle. Recheck liveness before publishing the snapshot. A process can exit at any stage: omit exited rows, discard their baselines and selections, and continue collecting other clients.
4. Never authorize an unverified process based on its name or caption. Report a generic count/status for inaccessible candidates without exposing their titles or enabling actions. Distinguish enumeration failure (**Refresh unavailable**, old rows stale and disabled) from successful enumeration with zero clients (**No active instances**). If identity cannot be refreshed for an existing key, disable it and discard its counters until verification succeeds again; a failed sample must not masquerade as a current zero.
5. Enumerate eligible top-level, visible, unowned windows; include minimized windows and exclude helper/tool windows. Associate each HWND using `GetWindowThreadProcessId`, matching a currently verified process key and live handle. Validate ownership again after caption collection. Use bounded caption lengths, render plain text, and handle disappearing windows independently. Refresh associations each cycle; window handles can be recycled too.

Dispose every enumeration wrapper, native handle, token buffer, and canceled snapshot's resources on all paths. Prefer value-only snapshots with no native handles crossing to the UI.

## CPU, memory, and refresh lifecycle

Use `GetProcessTimes` (kernel + user) for cumulative CPU time and a monotonic clock for elapsed sample time:

`CPU % = 100 * delta(process CPU time) / (delta(monotonic time) * logical processor count)`

Convert both deltas to the same units. Define logical CPU capacity consistently across processor groups and document/test the chosen Windows API. Clamp small rounding excursions to 0–100. Show **Sampling…** for the first sample, after reopening, after identity changes, or after invalid/negative deltas. Never inherit a baseline across PID reuse. Use **Unavailable** for failed counters and compute the next value only from valid samples of the same lifetime. Label memory **Working set (MiB)** using `GetProcessMemoryInfo`; it is resident memory, includes shared pages, and is not private allocation or total system usage. Counter failure may leave an otherwise verified row actionable.

Use a panel-scoped asynchronous loop with a two-second delay after each completed collection; never overlap refreshes or queue missed ticks. Update controls only on the UI thread using immutable snapshots. An optional Refresh button coalesces with an in-flight collection. Preserve sorting/selection by key. Keep signature checks off the UI thread and check cancellation between candidates and expensive calls.

Collect only while the panel is open and visible. Pause when hidden/minimized or while the close-confirmation dialog is active. On pause/close, cancel the current generation, stop scheduling, clear baselines/trust as appropriate, and discard late completions using a generation ID plus disposal check. On resume, collect immediately with fresh CPU baselines. Closing Diagnostics closes its owned panel. Repeated opens must not create multiple collectors. Do not refresh configuration diagnostics or launch history on the sampling interval.

Cancellation of a wait does not stop a synchronous native/signature call already executing. Keep that work owned by a single controller, prevent a second collector until it drains, and suppress its UI publication. Dispose results in its finally path. No further enumeration or metric sampling is allowed once the panel closes; an already executing call may finish. Bound window messages separately so hung clients cannot hold the UI open.

## Focus and close actions

At click time capture the selected `ProcessKey` and chosen HWND. Reopen a query handle, require matching creation time, owner/session/image and a fresh signature check, confirm the process is still live, and re-enumerate/revalidate that HWND's ownership and eligibility. Keep the handle alive through the action. If any check fails, cancel with **Instance exited or changed; refresh and select it again**. Never resolve a stale selection to a new process sharing its PID, or automatically pick a replacement window.

For **Focus**, restore a minimized window with a nonblocking window API, validate again, then call `SetForegroundWindow`. Windows can deny foreground activation; show **Windows did not allow focus; select the window from the taskbar**. Do not claim success solely because discovery found an HWND, simulate keystrokes, or attach thread input queues to bypass Windows focus policy.

For **Close window…**, show a single-target confirmation with the current caption, PID and start time, warning that gameplay may be interrupted. After confirmation, repeat identity/window validation; the dialog creates a significant delay. Send only `WM_CLOSE` to that window using `SendMessageTimeout` with an abort-if-hung policy and a short finite timeout (for example 500 ms), off the UI thread. A successful delivery means **Close requested**, not **Process exited**. The client may prompt, ignore the request, close only that window, or remain running. Let later discovery establish exit. Timeout/access denial produces a clear result and no automatic retry, elevation, force kill, tree kill, broadcast, or worker fallback.

Holding a process handle and revalidating prevents retargeting through PID lookup, but HWND APIs offer no atomic compare-process-lifetime-and-send operation. A window can disappear/recycle between the last ownership check and the call. Minimize this interval, never queue an HWND action across refreshes, document the residual race, and never claim atomic safety. Limit close to graceful window requests; force termination is outside this plan.

## Privacy and operational boundaries

Keep window captions, active PIDs/start identities, counters, and selection state in memory only. Do not add them to launch history, text logs, diagnostic JSON, ZIP reports, clipboard operations, or telemetry. Preserve current report allowlists; privacy tests must explicitly prove captions and new snapshot fields cannot leak. UI errors use fixed messages/status codes rather than arbitrary exception text. Opening/refreshing/focusing/closing the panel must not launch Roblox, start the scheduled worker, alter registrations, close singleton handles, or request UAC.

## Implementation sequence and acceptance

1. Add handle-bound identity/session and counter helpers with named access constants. Extract a shared player identity policy if needed; retain worker and pipe behavior. Validate FILETIME conversion separately from diagnostic-history ticks.
2. Implement snapshots, discovery, per-key trust and CPU sampling, and lifecycle cancellation with injected dependencies. Prove failures invalidate stale data and PID reuse resets all state before adding UI actions.
3. Add the owned panel, multiple-window selection, visible-only lifecycle, and accessible labels. Add separately revalidated focus and confirmed graceful-close actions. Keep action targeting independent of the latest row index.
4. Add `tests/ActiveInstancesTests.cs` to the existing test runner. Cover wrong owner/session/name/publisher, validation failure, no-window/duplicate-title/multi-window clients, window replacement, exits during every collection/action stage, same PID with a new creation time, stale selection after sorting, confirmation-time exit/reuse, hung/denied close, foreground rejection, and delivery without exit. Test CPU normalization/units, invalid deltas, first samples, counter failures, pruning, and FILETIME formatting.
5. Test controller timing deterministically: slow collectors never overlap; hidden/closed panels do not reschedule; late results are discarded; reopen waits for canceled work to drain; baselines reset; resources dispose even on error. Extend synthetic form rendering and report privacy canaries with fake captions. Default tests use adapters/controlled fixtures and must not focus or close real Roblox windows.
6. Run `./Test.ps1` and `./Build.ps1` with a supported .NET 10 SDK. Run existing elevated integration tests separately when available. Under a normal token, manually check multiple official clients, one minimized client, changed/empty titles, windowless startup, independent exits, denied/hung actions, two-second resource updates, and repeated panel open/close. Confirm Task Scheduler/worker inactivity and no sampling while hidden/closed. Verify display scaling/keyboard selection. Use controlled fixtures for reproducible PID/window replacement tests; do not rely on the OS recycling a live Roblox PID on demand.
7. Manually confirm distinct account sessions inside Roblox only if testing multi-account gameplay; captions and process counts are insufficient evidence. Recheck silent normal launches and worker idle exit. Update README/CHANGELOG with actual delivered behavior and remaining platform limits only once implementation passes these checks.

## Windows API references

These references constrain the implementation, rather than promising stronger guarantees than Windows provides:

- [GetProcessTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes): creation FILETIME and cumulative kernel/user time.
- [GetProcessMemoryInfo](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo): process memory counters and access requirements.
- [GetWindowThreadProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid): HWND owner lookup.
- [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow): foreground activation restrictions and result.
- [WM_CLOSE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-close): a close request that the application can handle or defer.
- [SendMessageTimeout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw): bounded message delivery and hung-window behavior.

## Validation of this planning change

The documentation branch passed all 35 existing default tests and `Build.ps1` using the ignored repository-local .NET 10 SDK. The first sandboxed test run could not access temporary registry/filesystem fixtures; rerunning the default suite with those operations permitted passed. No runtime code changed. These checks establish the existing baseline only; the manager-specific automated and live acceptance checks above remain future implementation work. Elevated integration and live Roblox gameplay were not run for this documentation change.

## Implementation validation

The implementation adds 15 default test groups covering authorization, trust invalidation, CPU normalization and lifetime resets, counter failures, process/window exits and replacement, cancellation boundaries, action outcomes, serialized refresh generations and delayed ticks, native handle-bound identity/counters, controlled visible/minimized/hidden/child windows, selection retention, and report exclusion. Synthetic panel previews at full width, minimum width and 150% scale are generated under ignored `.build/` output and inspected visually. The full 50-test default suite and release build use the repository-local .NET 10 SDK; registry/filesystem fixtures require permission outside the restricted sandbox.

The UI uses one process row plus an explicit window picker, with stable PID/creation-time and HWND selections. Shared identity reading now uses `TokenSessionId`; the worker's scanning and pipe authentication remain separate. CPU capacity uses `GetActiveProcessorCount(ALL_PROCESSOR_GROUPS)`. Close delivery uses `WM_CLOSE` with `SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT` and a 500 ms timeout. Successful delivery is not an exit assertion. Native HWND validation includes top-level/visible/unowned/non-tool eligibility, but the documented check/send race remains.

No installed helper upgrade, live Roblox window action, distinct-account gameplay, normal-token installed acceptance, or elevated singleton-event integration run is claimed by these automated checks. Follow the manual acceptance steps above on the test installation. An in-progress signature/native call is allowed to drain after cancellation; the old controller is also awaited before a closed panel is recreated.
