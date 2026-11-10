# Account/session security review — November 9, 2026

Reviewed the current, uncommitted checkout. Automated checks use synthetic sessions and processes; no Roblox client was launched or stopped and no installation was performed.

## Findings fixed

- Pipelined account batches forwarded provider status, code, progress stage and observed-server text directly to page events/results. The serialized provider path checked status/code syntax, which still admitted arbitrary uppercase secret text. Both paths now use a fixed status/code allowlist; pipeline progress uses fixed stages and observed server IDs must be nonempty UUIDs. Canary tests cover each field, including a syntactically valid uppercase ticket canary, and repeated explicit batches call the provider exactly once per selected slot.
- A pipeline `Finish` exception could skip releasing the batch semaphore, blocking subsequent launches indefinitely. Semaphore release now runs in its own `finally`. A fixture verifies the next explicit batch completes after a cleanup failure.

## Storage and disclosure boundaries

- Account profiles are isolated by opaque IDs, with private Windows ACLs and local-path/reparse-point validation. Credentials and optional vault entries use CurrentUser DPAPI; temporary cleartext byte arrays are cleared. The browser retains the session cookie; account launches do not make an app-managed cookie copy. This protects local storage within the Windows-user boundary, not against code already running as that user or an administrator.
- Signup password viewing, editing, copying and unfinished-signup recovery run in native dialogs. The account DTO exposes credential availability, not passwords. Native reveal/hide and disabled-state checks pass. Clipboard clearing waits 30 seconds and preserves clipboard text changed by the user.
- Supporter recovery codes are intentionally returned to the page for explicit reveal/restore and held in component state. They are not account passwords. No frontend console logging or persistent browser storage of these codes was found. They are sent to the configured support endpoint for restoration as intended; diagnostic telemetry never receives them.
- Native authorization keeps cookies, assertion/CSRF values and tickets out of DTOs and exception messages. Ticket serialization and diagnostic strings are redacted. Launch logs contain fixed stages/timing and process identity, not player arguments or authentication URIs.
- The bridge validates local origin, message size/depth/envelope, command allowlist and request replay IDs. Page navigation, popups, downloads, permissions and host objects are constrained. Generic native exception details are replaced before page errors. Account sign-in uses a separate Roblox-only top-level navigation policy with no workspace bridge.
- External actions use validated destinations: fixed Roblox game-pass URLs, Stripe checkout host validation, and repository-bound release-page validation. Game launch inputs accept positive Place IDs or constrained HTTPS game URLs; arbitrary schemes, private/share/query-bearing links and shell command input are rejected.
- Diagnostic ZIPs rebuild bounded, allowlisted fields and event codes rather than attaching raw logs, browser profiles or native exception details. Telemetry accepts fixed codes and numeric errors, uses explicit consent and a strict bounded schema, and disables redirects/cookies. Existing report/telemetry canary tests pass.
- Packaging uses fresh publish/staging directories and payload manifests. Existing Desktop/Helper installer hashes, embedded payload allowlists and portable contents passed `scripts/testing/Test-Distribution.ps1`. These existing artifacts predate this review's source fixes; no new package was built or published.
- Account removal deletes the selected profile, vault entry and linked/pending credentials before removing metadata. Locked/redirected data fails without pretending removal succeeded. Other saved accounts and unrelated filesystem targets are preserved in fixtures. Removal does not close running games or revoke a Roblox session remotely.

## Launch and cleanup verification

Expired/revoked sessions, wrong identity, challenges, cookie rotation failure, reconnect identity mismatch, canceled sign-in/authorization, uncertain handoff, partial batches and duplicate process correlation are covered. Unknown results do not permit layout application or trigger automatic replay. Frontend tests cover double-click submission, pending-operation navigation, cancellation, reconnect and retained batch results. Separate explicit batches remain intentional launches; they are not globally deduplicated.

Background cleanup rechecks process creation time, Windows user/session, exact player identity, signature, elevation, liveness and visibility against the same handle before stopping a client. Tests preserve visible windows, windows appearing during verification, unrelated users/sessions/executables, PID-reused processes and unavailable observations. Prelaunch cleanup also refuses all candidates when another client is visible or inspection is incomplete. Replaced-session cleanup is separate: it may gracefully close a visible old disconnected client only when matching account/replacement evidence qualifies; different accounts do not qualify.

## Roblox close-to-tray

[Closing multiple clients](closing-multiple-clients.md) still accurately distinguishes Roblox's own tray setting from the manager's tray mode. Its recorded November 7 sequence describes a Roblox systray replacement followed by another client's single-instance close request, with the helper already exited. The source logs for that date are no longer available locally, so this review cannot independently reconfirm the historical sequence. No live setting change or close/reopen experiment was performed. Disabling Roblox's **Minimize to tray on close** and retesting different accounts remains a manual acceptance item, not a verified workaround. See also [background startup investigation](background-client-investigation.md).

## Validation

- Root `Test.ps1`: native host/shortcut/preview checks, 259 backend checks, frontend build/unit checks, and 140 browser tests passed (12 mode-dependent skips); feature/service checks passed.
- `Test-Shared.ps1`: 147/147 passed; live/integration checks omitted.
- `scripts/testing/Test-Features.ps1`: account, support, telemetry-worker and support-worker checks passed. Final focused account rerun after the last canaries: 171 checks passed.
- `scripts/testing/Test-Distribution.ps1`: existing Desktop/Helper and portable artifact checks passed.

The initial sandbox run rejected redirected temporary profile paths; native/ACL fixture suites passed outside the sandbox. Automated results do not establish live Roblox shutdown behavior, cold-boot attribution or long-running gameplay compatibility.
