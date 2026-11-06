# Feature parity implementation progress

Updated November 3, 2026. **All six scoped implementation assignments are complete and integrated, with fixture/build verification. The full 13-milestone roadmap remains in progress.**

Scope and dependencies remain in [the implementation plan](feature-parity-implementation-plan.md). Completion here refers to the six assignments below, not all G01–G56 capabilities.

November 4 addition: G18 now has an experimental opt-in Instances preview grid with native capture, private in-memory image delivery, bounded subscriptions and lifecycle tests. Native WPF/WebView2 fixtures passed; live Roblox, hardware matrix and resource-soak acceptance remain open. See [G18 status and acceptance](live-client-preview-grid-plan.md).

| Assignment | Result | Status |
| --- | --- | --- |
| 1. Performance services | Trusted per-process CPU, working set, private commit, uptime and I/O sampling; deduplicated totals; serialized collection; bounded profiles/import validation, atomic storage and stale-revision rejection | Implemented and verified |
| 2. Performance page | Dedicated navigation/view, overview/table/history, polling controls, hidden/unmount pause, honest unavailable/stale states and save-only profile import/export | Implemented and verified |
| 3. Account organization and native validation | Search, groups, notes, colors, favorites, persistent order; preservation-first v1-to-v2 metadata migration; native typed session validator used by sign-in | Implemented and verified |
| 4. Native performance controls | Explicit priority/EcoQoS with trusted lifetime checks, cross-process ownership, durable restoration journal, readback, reset and interrupted-owner recovery; reset required before replacing an owned setting | Implemented and verified |
| 5. Native-control UI | Frozen confirmation targets/values, current versus owned settings, explicit Apply/Reset/Recover, denied/partial outcomes and stale-profile recovery | Implemented and verified |
| 6. Desktop startup resilience | Bounded initialization/navigation/renderer readiness, native failure/retry controls, three-attempt limit, stale-attempt rejection and explicit software-rendering restart preserving account profiles | Implemented and verified |

**Final integrated evidence**

- Root Test.ps1: passed after all product and fixture corrections. Includes 59 host/bridge/startup checks, 116 backend checks, 79 account checks, 14 frontend unit tests, 33 browser workflows, plus existing support/service fixtures.
- Root Test-Shared.ps1: 128/128 passed.
- Root Build.ps1: passed; current development output is .build/desktop.
- Performance browser coverage includes 11 workflows. The process-replacement confirmation regression additionally passed 10 repeated runs after deterministic scroll setup.
- Real native policy fixtures used harmless test processes, including a separate policy-owner process exiting abruptly; no Roblox process was launched or modified.
- Performance screenshots were inspected for layout and readability.

Evidence logs (ignored local build output):

- .build/feature-parity/closure-desktop.log
- .build/feature-parity/shared-telemetry-retry.log
- .build/feature-parity/closure-build.log
- .build/shared-test-results.json
- .build/performance-ui-final-20261103/
- .build/performance-disappearance-repeat-20261103/

**Corrections required during integration**

- Native fixture dispatch now supports both apphost and dotnet-host execution, refuses misplaced fixture flags before running suites, bounds child lifetime and reaps owned children. The earlier recursive test dispatch is resolved.
- Confirmations freeze process identity/settings and have a process-replacement regression; the parent also remounts the control panel when process lifetime changes.
- Corrected UTF-8 control labels and deterministic dropdown-scroll setup in browser fixtures.
- Startup keeps the HWND-backed WebView hidden until readiness, rather than relying on a WPF overlay; normalized unintended whitespace and guarded stale smoke callbacks.
- A shared telemetry test exposed transient access denial at atomic File.Move replacement. Only replacement of the same prepared file retries, bounded to four attempts/70ms. Counter increments, consent transactions and HTTP uploads are not replayed. Deterministic injected-denial coverage and the original daily-budget test pass.

**Roadmap status and remaining limits**

M0 is partial: operation/contracts integration and baseline evidence exist; original pre-edit full baseline was interrupted and is not claimed passed. Current integrated checks pass.

M1 is partial: the desktop now enables a native saved-account launch provider for explicit development testing, with session rotation, bounded authorization, ordered batches and conservative process/log binding. Controlled live acceptance of Roblox authentication, correct-account joins and process correlation remains pending. See account-launch-feasibility.md for the current implementation and acceptance steps.

M2 is partial against the complete roadmap: monitoring, profiles and explicit priority/EcoQoS are delivered. Saved FPS/graphics/profile requests do not automatically change clients. CPU Job budgets, affinity, automatic account roles, memory trimming, GPU/private-working-set counters and launch-time graphics remain unavailable or unverified.

M3 is partial: organization and metadata migration are delivered; genuine master-password session protection, migration away from persistent browser sessions and portable encrypted backup remain pending. An app lock must not be represented as encrypting existing WebView2 profiles.

M4–M11 remain broader pending work. Typed game targets/favorites and signed-update design were discussed but were held outside these six closure assignments; they are not counted as implemented.

M12 is partial: startup resilience is delivered; independent release signatures and live distribution/Windows/GPU acceptance remain pending.

No staging, commits, publication, installation, external alert sends or live Roblox acceptance were performed. Fixture/build verification does not establish real game compatibility, resource savings, screen-reader acceptance or actual GPU-failure recovery.

