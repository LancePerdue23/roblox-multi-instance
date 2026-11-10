# Feature parity and implementation plan

Prepared November 3, 2026, for Roblox Multi Instance. Status: **implementation in progress**. See [implementation progress](feature-parity-progress.md) for completed slices, validation and remaining scope.

For the delivery order rebased on the current implementation, see [the November 4 next gap-closure plan](next-gap-closure-plan.md). This document retains the complete capability roadmap; its original sequencing and historical gates should be read alongside that update.

This plan closes the gaps identified in [the competitive review](competitive-review-2026-11-03.md), preserves the existing system's strengths, and adds saved workspaces and measurable performance outcomes beyond basic parity. The review's G01–G56 IDs are mapped individually below. Planning used three medium-effort subagents for account/launch, performance, and overall coverage review.

**Outcome and scope**

The finished product should let a user organize accounts, choose games and servers, launch a verified batch, apply account-specific performance and layouts, monitor sessions, recover from failures, and restore a saved workspace. Advanced account tools, streaming views, automation, and integrations follow without complicating ordinary play.

Every reviewed capability has a destination. “Optional” means an implemented feature that users can leave disabled; it does not mean omitted from the roadmap. A compatibility-gated item must finish with working acceptance evidence or a recorded blocker and accurately labeled alternative. A substitute does not count as exact feature parity.

Preserve React/TypeScript, WPF/WebView2, the C# shared core, current packaging/edition model, CLI, on-demand browser helper, private storage, typed bridge, and layout planner. No framework rewrite is planned. Existing uncommitted work must be preserved. No staging, commits, publication, installation, credential use, or real Roblox operations are authorized merely by this planning document.

**Delivery order**

| Milestone | Result | Dependencies | Relative size |
| --- | --- | --- | --- |
| M0 | Baseline, contracts, capability states, feature boundaries | None | M |
| M1 | Verified selected-account launch, sessions, and batches | M0; live acceptance inputs | XL / largest uncertainty |
| M2 | Performance page, metrics, profiles, and explicit native controls | M0; independent of completed M1 | L |
| M3 | Organized accounts, session checks, genuine vault/backup | M0 and M1's storage decision; metadata UI can proceed immediately | XL |
| M4 | Games, links, servers, favorites, and friend targets | M0 for read-only UI; M1 for launch execution | L |
| M5 | Launch-time FPS/graphics and adaptive performance | M1, M2, relevant M3 lock integration | XL |
| M6 | Bounded rejoin/restart, timers, emergency stop | M1; M4 target handling; M5 for resource-driven restart | L |
| M7 | Window tools, previews, audio, saved workspaces | M1–M5 for complete workspace execution; existing layout work can proceed earlier | L |
| M8 | Customization, streamer mode, localization/RTL, network policy | M0; integrate each new screen as it lands | L |
| M9 | Local notifications, Discord alerts and presence | M1/M6 event model; M3 secret storage | M |
| M10 | Account utilities, outfits, signup, Roblox/build management | M1, M3, M4; capability research for individual actions | XL |
| M11 | CLI expansion, manager workflows, recordings, trusted extensions/API | Shared services as they land; M1/M3/M6 for execution | XL |
| M12 | Signed updates, startup recovery, full release acceptance | Signature/startup work starts after M0; final acceptance spans all milestones | L |

Sizes express scope, not elapsed-time promises. M1 feasibility, third-party compatibility, migration, and live validation prevent credible calendar estimates until the first investigation is complete.

The first useful delivery is **M0 + M2 monitoring/profiles**, while M1 investigates launch and storage. Do not stall the requested Performance page behind account launch. Native manual controls can follow after their fixture checks, even before saved-account launch is enabled. M12 update-signature work also starts early rather than waiting until the end.

A practical sequence is:

1. Foundation and early value: M0; M1 feasibility; M2 monitoring/profiles; M3 account organization/session checks; M12 signature/startup foundations.
2. Core parity: finish M1, then M4, genuine M3 vault/backup, M2 native controls, and M5.
3. Session reliability and differentiation: M6, M7, M8, M9, with shared CLI coverage accompanying each service.
4. Broad power-user parity: M10 and M11, remaining compatibility-gated controls, then final M12 acceptance.

**M0 — Establish boundaries and a measurable baseline**

Deliver:

- Record current dirty-tree state and relevant baseline results without resetting or absorbing unrelated work. Read active AGENTS.md and existing feasibility/distribution/audit documents at implementation time.
- Refactor only the portions of main.tsx needed for new screens into feature modules/hooks. Keep existing behavior intact; avoid a preliminary application-wide rewrite.
- Define common capability states: Available, Requires setup, Requires next launch, Unsupported, Unverified, Failed. Keep historical identity checks distinct from current session validity.
- Define sanitized operation codes, cancellable progress, and conflict categories for new mutations. Update native authorization, operation coordination, and frontend command classification together; a new mutation must never accidentally default to a read-only category.
- Introduce versioned stores with bounded data, migration rules, corruption preservation, atomic commits, and cross-instance coordination.
- Establish translation keys, RTL-compatible primitives and a shared privacy-display policy before new views/alerts are added; reviewed language packs and full streamer-mode acceptance follow in M8.
- Establish a single session identity contract: opaque local account ID, launch-attempt ID, PID plus creation time, trusted window association, state, and observation source. Native secrets and Roblox numeric identity remain native.
- Define performance measurement fixtures, graph-history bounds, and benchmark fields. Record baseline overhead before setting quantitative budgets.

Touchpoints: src/desktop/web/src/main.tsx and bridge.ts; src/desktop/backend/Contracts.cs and DesktopBackend.cs; src/desktop/host/BridgeProtocol.cs; src/core/OperationCoordinator.cs; src/desktop/Contracts.ps1.

Acceptance: existing journeys remain functional; contract generation is deterministic; late/canceled responses cannot change newer state; migration preserves originals; unknown operations remain visible without automatic replay. Exit from an unrelated app must not suddenly apply a queued mutation to a stale target.

**M1 — Finish account launching and authoritative session tracking**

M1a: resolve storage and authentication feasibility.

- Document whether the acceptable current launch flow needs browser-bound state or can use native vault-held session material. Investigate current endpoints, challenge/CSRF requirements, session rotation, launch authorization lifetime, signed player/bootstrapper handoff, and failure modes.
- Treat competitor code as research leads, not a compatibility guarantee or source to copy. Revalidate third-party behavior during implementation.
- Separate native identity/session validation from the sign-in window. Return Valid, Expired, Challenged, RateLimited, Unavailable, and WrongIdentity outcomes with timestamps.
- Decide the M3 migration format before duplicating authenticated sessions. Preserve unsupported sign-in methods as explicit capability limits rather than bypassing challenges.

M1b: implement the provider behind an acceptance gate.

- Implement IAccountSessionProvider using native-only session material and bounded requests. UI receives progress and sanitized outcomes, never launch tickets or reusable authentication.
- Build a session tracker with Starting, IdentityVerified, Joining, InGame, Disconnected, Exited, and Unknown states. A process existing is not proof of joining the correct account.
- Bind identity through verified immutable account evidence and process-owned observations across handoff. Existing username enrichment is display evidence, not sufficient launch authorization.
- Preserve user/session/signature checks and exact process creation identity. Unrelated browser launches, stale logs, reused PIDs, and ambiguous windows must not be adopted silently.
- Complete ordered batches: freeze order/target/preset, bounded stagger, per-account progress, explicit failure policy, and retry only selected confirmed failures.
- Coordinate launches across desktop instances and background recovery. Storage leases should not block unrelated reads for an entire long batch.
- Cancellation stops pending work and leaves started games alone. An uncertain start is reconciled rather than replayed. Retrying failed slots does not turn a partial/canceled batch into a complete verified batch; use manual Layouts preview/apply for the combined session set until a separate composite-placement design is accepted.

Touchpoints: src/desktop/backend/Accounts.cs and FeatureBackend.cs; host AccountSignInWindow/Policy/Cleanup; src/core/Launch.cs, LaunchStartup.cs, ActiveInstances.cs, InstanceAccounts.cs and AccountEnrichment.cs; account, backend, and bridge tests.

Acceptance:

- Fixtures cover expired/mismatched identity, challenge, throttling, redirects, oversized/malformed response, timeout, cancel, concurrent external launch, handoff, stale log, PID reuse, duplicate binding and partial batches.
- Separately controlled live acceptance proves the selected account joins even when the ordinary browser/player account differs; two distinct accounts remain in game; each account maps to its correct process/window.
- Slots stay stable after failures. Automatic layout retains the existing requirement for a complete, uncanceled, uniquely verified batch; partial placement remains manual unless separately designed later.
- LaunchAvailable stays false until required evidence passes. If no acceptable mechanism is found, record the blocker, retain browser-assisted launching as an explicitly narrower alternative, and continue independent milestones. Do not call account-launch parity complete.

**M2 — Deliver the requested Performance page early**

M2a: monitoring and profiles.

- Add a dedicated Performance destination with overview, per-client table/history, presets, account roles, and advanced controls.
- Collect CPU, working set, private working set where available, private committed bytes, uptime, and process I/O rates. Add GPU counters only after capability detection.
- Aggregate once per PID plus creation time: current UI window rows must not cause a multi-window process to be counted twice.
- Use bounded history, nonoverlapping collectors, generation-aware results, adjustable polling and a table-only mode. Pause hidden graph rendering; allow background monitoring only when a policy actually needs it.
- Label unavailable counters, first samples, stale observations, and totals clearly. Do not label aggregate I/O as network traffic or a configured FPS cap as measured FPS.
- Add versioned Full quality, Balanced and Many accounts profiles. Defaults should preserve current quality until the user chooses a profile; tune suggested values from evidence.
- Resolve precedence: global defaults → selected account profile/role → explicit session override. Safety constraints such as loading grace and protected main account remain visible.
- Report requested, effective, and actually applied settings separately, with source and failure reason.
- Import/export bounded performance-only profiles with validation and a diff preview. Exclude accounts, credentials, launch arguments, hotkeys and arbitrary paths.

M2b: explicit native process controls.

- Implement priority/EcoQoS, then CPU Job budgets and advanced affinity. Reuse current trust checks before each mutation; request narrow rights and show access refusal.
- Allow manual selection of a verified client without assuming its account identity. Automatic per-account policies require M1 binding or explicit supported adoption.
- Spike inherited/nested Windows Job behavior using harmless fixture processes. Do not replace another owner's Job or claim a process can simply be detached.
- Never enable kill-on-job-close behavior for Roblox. Reset can lift an owned CPU restriction while Job membership may persist.
- Account for processor groups/topology before exposing affinity on larger machines.
- Elect one authoritative policy owner per Windows user/session through a cross-process lease covering desktop instances and CLI. Refuse automatic takeover when uncertain; reconcile owner failure so one manager never captures another manager's applied value as the original.
- Journal owned changes with original/applied values and process lifetime. Restore cleanly on reset/exit where possible, preserve external changes, and reconcile interrupted work on restart. Do not promise immediate cleanup after a manager crash when no recovery owner is running.
- Start memory tooling with commit/pressure warnings. Add an explicit rate-limited trim with honest explanation; introduce automatic trimming only after responsiveness and paging measurements.

Touchpoints: new focused src/core performance/metrics/policy services beside ActiveInstances.cs and ActiveInstanceNative.cs; desktop facade/DTOs; host allowlist; dedicated web feature module; shared/backend/Playwright tests.

Acceptance: no inflated totals, inherited history after PID reuse, overlapping collectors or false success. Test denied access, existing Jobs, process exit during application, partial native failure, external setting changes, repeated reset, competing managers, owner crash, stale journals and manager exit. Profile override origins are understandable. Monitoring and save-only profiles work with saved-account launch disabled.

**M3 — Complete account management, vault semantics and portable backup**

Deliver:

- Search/filter, groups, ordering, aliases, notes, colors, favorites, essential avatars/presence/account summaries, bounded refresh and last-checked status.
- Separate presence, reusable-session validity, launch access and local running state. Background API calls must use caching/rate limits and remain cancellable.
- Individual/bulk session check, expected-identity reconnect, duplicate prevention and account removal/reference repair.
- A genuine optional master-password session vault, Windows-user protection by default, explicit lock, idle/suspend locking, key lifecycle, sensitive-action confirmation and recovery-code design.
- Explicit migration from persistent authenticated WebView2 profiles to disposable sign-in profiles plus native vault storage **only if the selected launch mechanism supports it**. Journal migration, verify the new representation before retiring the old, and verify cleanup of app-managed authenticated profiles and temporary copies after WebView2 has shut down.
- Verify recovery against encrypted test ciphertext: an independent recovery key can unlock/re-wrap the vault, whereas a destructive reset loses saved sessions. Password changes do not invalidate old exported backups. Remembered unlock material/recovery codes need explicit protection semantics.
- Cleanup is bounded to app-managed locations; ordinary deletion does not prove erasure from recovery tools, snapshots, backups or earlier copies. Interrupted cleanup stays pending and prevents a full password-vault migration claim. Provider-side rotation/revocation and re-enrollment need separate investigation if historical sessions must be invalidated.
- An incremental application lock may ship earlier, but must say it is an application lock. It must not imply that existing browser profiles received master-password encryption.
- Lock blocks native credential-dependent operations, including launch, validation, sensitive export and new sign-in. A shared authorization generation/barrier covers every entrypoint and is checked at secret read, authorization issuance and process-start commit. Lock invalidates queued/in-flight grants across desktop instances, CLI, recovery, workflows and API. It cannot undo a ticket already issued or a process already started; report that boundary. Unlock uses an interactive native flow; unattended callers cannot silently unlock. Pause pending recovery; leave already-running games alone. Define behavior of password change/removal, lost password, canceled sign-in and interrupted cleanup.
- Password-encrypted portable backup with versioned format, bounded KDF/input work, authenticated encryption, integrity validation, restore preview, duplicate/merge policy and rollback. Never ship a device-derived fallback that opens a leaked backup.
- Back up native session material through the provider/vault contract, not by blindly copying live browser profiles. If sessions cannot be exported safely, offer metadata/workspace export plus reconnect and label that limitation.
- Add native-only cookie/bulk-cookie and legacy-manager import adapters after format validation, with preview, duplicate handling and private temporary storage. No password-list collection or import. Keep sensitive entry/reveal/copy outside the ordinary renderer; copy, if offered, expires and avoids history where supported.
- Document the limit of Windows-user and unlocked-process protection; do not claim protection against malware already running as the same user.

Touchpoints: Accounts.cs, AccountStorageSecurity.cs, sign-in host, new vault/migration/import services, desktop account view and generated contracts.

Acceptance: restore under another Windows account works with the independent backup password; wrong passwords/tampering do not mutate state; interrupted restore and migration recover; corrupt originals survive. Test lock/recovery across idle, suspend, restart, competing instances and active batches. Secret canaries remain absent from bridge, logs, reports, updates, telemetry, clipboard history and temporary files where the product promises exclusion. An accessible app-managed authenticated legacy profile prevents claiming full password-vault migration complete; historical copies outside app control are a separately disclosed limitation.

**M4 — Add a coherent Play and server workflow**

Deliver:

- A shared launch context across Accounts, Play, Instances, Performance and Layouts; selected accounts and game target do not disappear on navigation.
- Typed targets for place, public job, private server/share links and player/friend targets. Parse exact allowed origins/schemes locally; perform bounded, separately validated native resolution.
- Keep sensitive private codes in native storage; ordinary UI receives labels/opaque references. Never forward an arbitrary executable, URI or redirect.
- Game discovery/details, favorites, recent games and account/server history; several private-server references per favorite where supported.
- Public and owned private-server browsing with incremental pagination, capacity/sorting, cancellation, caching, retry-after/rate-limit handling and stale-data indications.
- Same-server group launch: choose a server with group capacity, or follow the first account's positively observed server. Capacity is not a reservation. Show per-account access failures and never silently fall back into different servers.
- Friends/other-account/player-follow targets only when current visibility and Roblox permissions allow them.
- Region/access checks as explicit bounded actions with a cache and query-cost indication. Treat geography as inferred; disclose any third-party lookup and do not equate region to measured ping.

Touchpoints: replace/extend LaunchTargetResolver and target DTOs deliberately; new shared target/server/history services; Play UI; existing batch service; native secret storage.

Acceptance: malformed/host-confusable URLs, redirects, stale/full/vanished servers, inaccessible/private targets, expired links and throttling produce safe outcomes. Changing target cancels stale queries. First-account failure does not accidentally send the rest elsewhere. Private-link canaries never enter general reports. Navigation preserves the selected launch context without preserving secrets in localStorage.

**M5 — Add launch-time FPS/graphics and adaptive policies**

Deliver:

- Default/account/session FPS cap and graphics quality; supported allowlisted controls for antialiasing, textures and rendering/visual options.
- Version/capability checks and bootstrapper detection. Unsupported or unconfirmed settings remain explicit; no arbitrary historical FastFlag editor.
- A cross-process launch-settings lease: journal original/owned values, merge bounded keys, launch, establish settings consumption, restore promptly, and reconcile interruption.
- Make desktop, CLI, recovery and owned helper launch paths share the lease. Uncontrolled browser/bootstrapper launches cannot be guaranteed isolated from shared settings; detect/refuse conflicts where possible or report overrides as unconfirmed.
- Handle concurrent differently configured launches, game exit rewriting settings, user edits, timeouts, cancellation and manager crashes. Preserve unrelated keys and external/user changes.
- Skip or delegate settings managed by an external bootstrapper through a supported adapter; do not fight its writes.
- Foreground/background adaptation with debounce, loading grace, main-account protection and visible override provenance.
- Configurable sustained memory-pressure policy. Optional CPU throttling must not be described as freeing committed memory; any high-memory restart uses M6 and explicitly disconnects the client.

Acceptance: coordinated profile launches cannot consume one another's settings; an external-launch-during-lease fixture exercises the documented refusal/unconfirmed path. Partial writes and every lease interruption point recover; user edits survive. A timeout cannot be reported as applied. Measured responsiveness and disconnect behavior determine default limits. Settings return to the user's chosen state without waiting for all clients to exit.

**M6 — Long-session recovery, scheduling, and emergency control**

Deliver:

- Opt-in per-account disconnect rejoin, crash/freeze restart, and sustained high-memory restart.
- Distinguish intentional leave/close, disconnect, crash, loading, network outage and unknown. Unknown evidence must not trigger an uncontrolled launch.
- Reacquire authorization before retrying; use grace periods, exponential backoff, attempt/time budgets, jitter, circuit breakers and per-account opt-out. Queues share M1's launch coordinator.
- Persist policy/configuration and safe observation checkpoints, not replayable launch tickets. Reconcile after restart before acting.
- Unified emergency stop immediately prevents new scheduling and requests cancellation of queued launches, recovery, timers, recordings and controlled automation. In-flight native/external effects may complete and need reconciliation; unrestricted trusted code has only best-effort stop semantics unless containment is proven. Default process closure is limited to positively identified app-owned/explicitly selected clients in this Windows session; force-close and broader selection are separate explicit actions.
- Optional anti-AFK with per-account exclusion, bounded input, validated foreground/target ownership, user-activity interruption and a quick stop. Validate actual efficacy; do not claim posted background messages prevent idle kicks without evidence.
- Inspectable/cancellable session-stop and rejoin schedules, launch-minimize behavior, and bounded log cleanup that only deletes owned eligible files.
- Optional PC shutdown requires an explicit choice distinct from closing clients, a visible countdown and cancellation. Define sleep/resume, clock changes and overdue schedules; do not unexpectedly execute stale actions on startup.
- Close-to-tray versus Exit has clear policy: tray may continue enabled desktop automation; Exit stops it, drains work and restores owned policies while leaving games alone unless the user selected closure. Do not turn the on-demand browser helper into an unannounced persistent service.

Touchpoints: new session/recovery policy services, OperationCoordinator, launch/history services, presentation notification/hotkey modules, host lifetime and current cleanup policy.

Acceptance: fake-clock/event fixtures cover outage storms, repeated crashes, intentional close, lock, late completion, simultaneous batch and rejoin, and process reuse. No duplicate client is launched after an uncertain outcome. Panic prevents pending work restarting itself. Live recovery is separately validated with owner-controlled sessions.

**M7 — Extend window control and build saved workspaces**

Deliver:

- Minimize/restore selected or all scoped clients, focus, compact/tiny modes and reversible borderless options. Keep existing multi-monitor preview, capacity checks, minimum-size policy and recovery.
- Tiny/free-resize mode is an explicit advanced capability with tested limits; do not weaken the normal planner's capacity rules to make more windows appear to fit. Avoid global hooks unless a scoped, documented need is proven.
- Optional in-app live client preview grid; see the [G18 implementation plan](live-client-preview-grid-plan.md) for capture, transport, resource limits, lifecycle and acceptance gates. Offer capture-rate limits, stale/minimized states, privacy controls and a table-only mode. Do not invisibly restore minimized clients merely to refresh previews. A separate clean presentation/streaming view remains later work.
- Per-client/background audio through validated Windows audio-session association, with original/applied ownership tracking and preservation of later user changes.
- Versioned saved workspaces combining account order, per-account targets, performance roles, launch options, monitor assignments and named layout.
- A review/preflight shows expired/missing accounts, unavailable targets, profile changes and monitor remapping. Preserve stable slots and the existing complete-batch automatic placement gate.
- Repair references after account/preset/profile deletion or renaming. Export/import workspace definitions without sessions; private targets use separately protected references or explicit encrypted export.
- Add session before/after resource comparisons with named metrics and a manager-overhead view. Any client-capacity suggestion is an estimate with observed inputs, not a guaranteed maximum.

Touchpoints: existing WindowLayouts.cs, LayoutPreferences.cs and native placement, new workspace/preview/audio services, Layouts workspace tab and Instances view.

Acceptance: original geometry/style/audio restores only while current state is still owned and the exact process/window/audio-session association remains valid. Skip disappeared/reused objects, preserve later user changes, and never replay stale restoration after a crash. Monitor removal and mixed DPI require remapping/preview rather than surprise movement. Capture errors cannot block window actions. Workspace retry only launches confirmed failed slots and does not duplicate already running verified slots; the combined partial/retried set uses manual Layouts preview/apply under the M1 placement rule. Compare table versus preview overhead on the same workload.

**M8 — Coherent UI, privacy, customization, and languages**

Deliver:

- Keep primary navigation coherent: Instances, Accounts, Play, Performance, Layouts, Activity, System, Settings; initially place saved workspaces inside Layouts rather than multiplying top-level destinations.
- Contextual primary actions: selected-account Play must not be confused with browser/player launch. Persistent account selection, search and a compact launch summary connect workflows.
- Keep routine selection/filter/preferences efficient; use confirmations for destructive or high-impact actions. Present actionable errors instead of dumping technical state.
- Streamer mode covers account labels, titles, alerts, tray menus, preview labels and share views. Explicitly distinguish display hiding from secret encryption; do not rewrite account data.
- Expand themes with sensible accent/font/background choices, live preview, reset and bounded theme import/export. Use bundled/local font choices by default; remote fetches require an explicit user choice.
- Internationalize strings, pluralization, formatting and layout early. Deliver reviewed English/Portuguese/Spanish first, then the full reviewed competitor language set: Arabic RTL, Bosnian, Simplified Chinese, Filipino, French, German, Indonesian, Japanese, Korean, Polish, Russian, Swedish, Thai, Turkish and Vietnamese. Missing/unreviewed strings fall back explicitly; a selectable label alone does not count as translated support.
- Optional public-network policy pauses new launches/recovery and can lock a password vault. Define unavailable/stale network status and explain that Windows network classification is a policy input, not a security guarantee.

Acceptance: keyboard-only navigation, focus restoration, named controls, screen-reader journeys, high contrast, long translations, RTL and 100/125/150/200% scaling across new screens. Streamer-mode fixtures include notifications and native surfaces, not only account rows. No sensitive labels appear in a stream view the product claims to hide.

**M9 — Notifications, Discord alerts, and presence**

Deliver:

- Unified sanitized session events for launch progress, join, disconnect, crash, restart, batch completion and exhausted retry budget.
- User-selected local notifications and Discord webhook events, per-account mute, deduplication, rate limits and bounded retry.
- Native protected webhook storage with redaction; imports/exports exclude it unless explicitly part of an encrypted secret backup.
- Rich presence as a separate opt-in choice with game/account-count sharing explained. Streamer mode controls what may be displayed/shared.
- Useful tray favorites, scoped session actions and conflict-aware shortcuts using the existing ownership model.

Acceptance: retries do not spam during a global outage; notification failure does not block launches; locked-vault behavior is explicit; no cookies, tickets/private links enter messages. Test offline delivery, webhook errors, deduplication, mute and privacy interactions with fixtures; real external sends require explicit test configuration.

**M10 — Broad account utilities and compatibility tools**

Deliver as separately tested capability slices:

- Account details/security summaries; supported display-name/privacy changes, Quick Login and account/session utilities. Password/email/PIN/security changes route through official authenticated flows where required; do not collect secrets into manager forms just for parity.
- Previewed friend operations, including connecting selected accounts, with bounded request counts and throttling. Bulk sign-out or other account-wide effects need an explicit scope preview.
- Free catalog search, saved outfits and batch dressing with price revalidation before mutation. Never make a purchase or accept a paid item because a cached catalog entry once said free.
- Official free signup assistance with a human-operated challenge step; no challenge bypass, purchased-account provider or account-farm acquisition integration.
- Roblox version/bootstrapper discovery and compatibility reporting. Add signed official install/update and user-selected side-by-side build management only where the provider supports it; do not promise arbitrary old builds can still join games.
- Supported bootstrapper adapters respect that product's ownership of settings. Unknown bootstrappers get a useful detection result rather than overwritten files.
- Optional upload QoS limit with explicit Apply/elevation, exact application/policy ownership, journaled cleanup and accurate scope. Do not present it as a download limiter or per-account cap if the OS policy applies to all matching clients.
- Safe data migration/export for preferences, themes and workspaces. Private account storage keeps validated local ownership/ACLs; portability uses encrypted export and re-enrollment where necessary, rather than arbitrary network/reparse destinations.

Acceptance: each action is checked against current provider behavior. Challenges/access denial stop automation. Free-item price changes refuse purchase. Version installation validates source, signature and intended path; cancellation preserves working installation/settings. QoS cleanup never removes another tool's policy. Unsupported direct account operations remain visibly narrower official-page alternatives and stay marked partial in the parity ledger.

**M11 — CLI parity, workflows, recordings, and integrations**

Deliver:

- Extend rmi alongside each nonvisual service: accounts/validation, targets, batches, sessions, profiles, workspaces and recovery policies, retaining shared typed results, JSON, exit codes and cancellation.
- A declarative manager-workflow engine first: approved actions, triggers/schedules, dry-run preview, target revalidation, bounded execution and stop. Never persist raw process handles or stale executable commands for replay.
- Record/play allowlisted manager actions by default. Any optional input recording uses a fixed non-text action/key allowlist on verified allowed client targets: no raw text, clipboard, global keystroke stream or arbitrary desktop capture. Stop on focus loss, sign-in/payment surfaces, vault lock or uncertain ownership. Do not claim arbitrary gameplay input can automatically be classified as free of sensitive content.
- Advanced user-authored JS/Python as explicitly trusted extensions in separate processes with visible execution, manager-API grants, limits and revocation. Grants/revocation constrain manager access and tracked execution, not arbitrary same-user file/network/native access, and cannot undo prior external effects. Helper API guards alone are not a secure sandbox for arbitrary Python.
- Before advertising isolation, prove the chosen process/container boundary enforces the promised file/network/native access limits. Otherwise clearly label scripts as trusted local code and do not accept automatic execution of downloaded/imported scripts.
- An explicitly enabled authenticated local API using loopback or a current-user pipe, strong revocable tokens in headers, request limits and origin protections. No credentials in URLs, secret-retrieval endpoints or broad native invocation.
- A documented compatibility adapter for useful Nexus-style external manager actions. No Roblox code injection or exploit-runtime requirement. Unsupported game-side behaviors are documented exclusions, not silently claimed as equivalent.

Touchpoints: Terminal.cs, shared operations/policies, new workflow/extension transport services, native private storage, dedicated advanced UI.

Acceptance: desktop and CLI enforce the same permissions and outcomes; redirected CLI input cannot hang on confirmations. Test token revocation, malicious origins, oversized payloads, forbidden actions, canceled schedules and stale process identities. Scripts cannot silently escalate grants through settings. Emergency stop targets positively owned automation processes. Claim whole-tree stopping only after containment is proven; unrestricted code can spawn detached work. Never close an extension Job containing Roblox; containment must prevent or safely exclude game children before tree termination is enabled.

**M12 — Update authenticity, startup resilience, and release acceptance**

Start M12a/M12b early; complete M12c after feature integration.

M12a: independently authenticated updates.

- Sign a canonical release manifest covering exact artifact hashes/sizes, edition, version and protocol; verify through an embedded publisher trust anchor before execution.
- Choose a supported signing implementation during technical design, keep signing secrets outside the repository, define key IDs, rotation/revocation and rollback protection.
- Retain current host/digest/size/version/edition/protocol checks and graceful handoff/revalidation. Recheck locked/staged bytes at the execution boundary.
- Distinguish release-signature verification from Windows Authenticode/SmartScreen identity. Evaluate OS code signing separately; neither a checksum nor a UI badge substitutes for publisher authentication.
- Preserve browser-helper operation and running games during manager updates. Publication remains a separately requested action.

M12b: blank-WebView recovery.

- Host-owned readiness handshake and bounded startup timeout, native error/retry surface, explicit software-rendering safe mode and a startup override.
- Stop retry loops; preserve account profiles/settings; keep CSP and bridge restrictions. A real initialization error must stay actionable rather than being repeatedly hidden by restarts.

M12c: integrated acceptance.

- Complete the documented Windows installation/repair/recovery/uninstall/update, protocol ownership, retained-data migration, canceled elevation, busy-file and interruption matrix.
- Verify helper-only/browser-first and desktop workflows under the existing edition model.
- Complete controlled two-account coexistence, true account/window correlation, performance/recovery, bootstrapper compatibility, monitor/DPI and screen-reader checks.
- Record build hashes, dirty state, versions, environment, pass/fail/blocked/skipped/manual evidence and actual soak duration.
- Release notes distinguish implemented, production-enabled and live-verified functionality. Do not promote blocked/skipped tests to passes.

**Coverage ledger for the original comparison**

All entries begin **Planned**, with existing implementations preserved where stated. “Compatibility gate” is an engineering prerequisite, not a completed item.

| Gap ID | Capability/disposition | Destination |
| --- | --- | --- |
| G01 | Preserve multi-instance and prove real two-account coexistence | M1, M12 |
| G02 | Complete saved sign-in and cleanup/migration | M1, M3 |
| G03 | Implement verified selected-account launch | M1 |
| G04 | Complete ordered cancellable batches | M1 |
| G05 | Implement groups/search/notes/order/favorites | M3 |
| G06 | Implement avatars and account summaries | M3 |
| G07 | Implement bounded check/reconnect/session state | M1, M3 |
| G08 | Cookie/legacy-manager migration; no password-list import | M3 |
| G09 | Genuine vault/lock/recovery; storage compatibility gate | M1a, M3 |
| G10 | Password-encrypted portable backup/restore | M3 |
| G11 | Implement typed private/share/deep links | M4 |
| G12 | Implement group same-server selection/follow-first | M4 |
| G13 | Implement public/private server browser | M4 |
| G14 | Implement bounded inferred region/access checks; provider gate | M4 |
| G15 | Implement favorites/recents/friend targets | M4 |
| G16 | Persistent launch context and per-account options | M1, M4, M7 |
| G17 | Rich authoritative session dashboard | M1, M2 |
| G18 | Optional bounded previews and clean streaming view | M7 |
| G19 | Preserve trusted focus/close | M0, M7, M12 |
| G20 | Minimize/restore, compact/borderless/tiny; compatibility gate | M7 |
| G21 | Preserve/extend saved monitor layout editor | M7 |
| G22 | Preserve DPI/capacity/recovery and verify hardware | M7, M12 |
| G23 | Implement accurate totals/history/GPU where available | M2 |
| G24 | Implement dedicated Performance page/presets | M2 |
| G25 | Implement account/launch FPS and graphics overrides | M5 |
| G26 | Implement bounded supported graphics controls | M5 |
| G27 | Implement CPU/priority/EcoQoS/affinity; native capability gates | M2 |
| G28 | Memory observability, optional trim/throttle, bounded restart | M2, M5, M6 |
| G29 | Main/foreground exemptions and adaptive policy | M2, M5 |
| G30 | Implement optional process-correlated audio policy | M7 |
| G31 | Implement opt-in disconnect rejoin | M6 |
| G32 | Implement bounded crash/freeze/memory restart | M6 |
| G33 | Implement optional validated anti-AFK; efficacy gate | M6 |
| G34 | Timers/log maintenance; separate explicit PC shutdown | M6 |
| G35 | Implement scoped emergency stop | M6, M11 |
| G36 | Local/Discord alerts; separate opt-in presence | M9 |
| G37 | Preserve on-demand browser/helper advantage | M0, M12 |
| G38 | Preserve/enrich health/repair/ownership checks | M0, M12 |
| G39 | Extend shared CLI with every nonvisual service | M11, alongside M1–M10 |
| G40 | Add independent publisher signature verification | M12a |
| G41 | Preserve redacted reporting and enrich session reasons | M1, M6, M9, M12 |
| G42 | Extend themes/accent/font/background and export | M8 |
| G43 | Implement cross-surface streamer mode | M8 |
| G44 | Implement localization/RTL and reviewed language coverage | M8 |
| G45 | Supported account utilities; official-flow alternatives | M10 |
| G46 | Implement free outfits/catalog/batch dressing | M10 |
| G47 | Official human-operated signup; exclude paid account acquisition | M10 |
| G48 | Manager workflows/recordings plus explicitly trusted extensions | M11 |
| G49 | Authenticated local API/Nexus-style manager adapter; no injection | M11 |
| G50 | Discovery, signed updates and supported build management | M10 |
| G51 | Implement optional owned upload QoS policy | M10 |
| G52 | Implement optional public-network policy | M8 |
| G53 | Implement bounded safe-video startup recovery | M12b |
| G54 | Safe preference/workspace migration and encrypted account export | M3, M10 |
| G55 | Preserve/extend tray and conflict-aware shortcuts | M6, M9 |
| G56 | Complete controlled distribution/live acceptance | M12c |

Retained design exclusions: machine/MAC identity spoofing, password-list collection, challenge bypass, arbitrary game injection, developer-side session storage, unreviewed automatic script execution, and disabling the main CSP. These are deliberate departures from risky implementation choices, not hidden completed parity claims.

**Implementation structure and agent usage**

Use incremental, reviewable work packages. Existing roots remain authoritative:

| Layer | Ownership |
| --- | --- |
| src/core | Canonical process identity, launch/session, policy, metrics, settings leases, targets, recovery, workflow and CLI services |
| src/desktop/backend | UI facade, account/vault ownership and sanitized DTOs; resolve shared account-service boundaries as CLI features arrive |
| src/desktop/host | Native sign-in/dialogs, secret entry, bridge origin/command checks, startup recovery, lifetime |
| src/desktop/web | Feature-oriented UI, accessible controls, current context and effective-state display |
| src/helper, src/cli, src/setup | Thin entrypoints/integration with the same canonical policies |
| Existing tests and new focused fixtures | Shared/native, backend, bridge and user workflow evidence |

Proposed service/type names are design aids, not a mandate to create empty abstractions. Introduce a module only when a milestone needs it.

Agent policy:

- Coordinator owns scope, architecture, integration, shared-contract review and final verification.
- Default subagent effort is **medium** for substantive service/UI implementation or review. Use **low/light effort** for bounded inventories, documentation updates or simple isolated changes.
- Keep at most three contributors plus the coordinator. Start with a core/native task, UI task, and independent review/test task when their file ownership is disjoint.
- Separate chats and agents share this checkout. Do not switch branches, reset changes or let agents edit the same files concurrently. No additional worktree is assumed under the active-checkout instruction.
- A single owner integrates Contracts.cs, generated TypeScript, BridgeProtocol.cs, operation classification and shared entrypoints. Other agents submit interface requirements before those edits.
- Serialize shared builds/tests and integration mutations. An agent can work on a dedicated module or read-only review while integration runs.
- Each handoff reports files touched, behavior, focused validation, known limitations and remaining live gates. An independent review checks secrets, identity, cancellation, settings restoration and scope before milestone completion.

**Validation and completion rules**

During development, run the focused existing suite covering the changed behavior. At milestone close, run the appropriate current checks from the active checkout:

- ./Test.ps1 for desktop contracts, backend/host boundaries, frontend build/unit/workflow checks and existing feature/service checks.
- ./Test-Shared.ps1 whenever canonical shared/core or installed-helper behavior changes.
- ./Build.ps1 to verify the production desktop after integration.
- ./Package.ps1 and ./scripts/testing/Test-Distribution.ps1 when packaging/updater/setup changes require artifact verification; these are separate from installation.
- Occasional ./scripts/testing/Audit.ps1 milestone persona/stress runs; preserve the documented full soak requirement for an acceptance run.
- Do not use retired UI tests for current desktop work. Keep existing archive preservation requirements.

Add tests for actual failure boundaries and user behavior, not a second copy of implementation logic. Use fake HTTP, fake clocks, isolated storage, controlled native fixture processes/windows, malicious/corrupt inputs and secret canaries. Fixture tests must not launch/stop Roblox, install software, change real protocols or operate on existing games.

Live checks need owner-controlled accounts, an explicit ready test session and the existing audit checkpoint procedure. Never ask for passwords/cookies in chat. The current live harness limits additional clients; larger performance runs require a separately agreed protocol and explicit client-count/cleanup scope. Higher-scale tests must not silently expand that harness.

The performance protocol should compare the same machine, experience, workload and duration at 1/5/10/20 clients **where explicitly authorized and feasible**, capture the entire manager process tree (including WebView2), CPU, resident/private/committed memory, GPU where available, responsiveness, disconnects and recovery. Use warm-up and repeated baseline/profile runs with controlled ordering; fix foreground/loading/minimized/preview states and record client/server versions, thermal/power conditions, system commit pressure and sampling interval. Separate startup/loading from steady state and apply consistent metric definitions/process deduplication. Establish numeric budgets from M0 measurements; do not invent savings or use a working-set drop alone as proof of improvement.

Each feature records three separate statuses: **Implemented**, **Enabled in production**, **Live verified**. Record blocked/skipped/unsupported cases separately. A milestone is complete only when its scoped behavior, relevant fixtures and required live evidence pass; a finished prototype behind a disabled capability remains partial.

Before each milestone, refresh the current checkout and dependency status. After each milestone, update this ledger with actual evidence, migrate only owned data, and keep a rollback/recovery path. No commit, release, publication or external notification is implied by completing a work package.

**First implementation package**

The initial package is deliberately concrete:

1. Capture M0 baseline/limits and agree the native session/performance DTO boundaries in code.
2. Extract a dedicated Performance view and add accurate CPU/working-set/commit/I/O snapshots with bounded history, unavailable states and deduplicated totals.
3. Add save-only performance profiles, precedence/effective-state display and a validated import preview; clearly mark launch-only controls.
4. Run focused shared/backend/bridge/UI fixtures and the required current integration checks.
5. In an independently scoped M1 investigation, document the launch/storage mechanism and the exact live proof needed before enabling it.
6. Continue account organization and signed-update foundations while M1 waits for live inputs.

This delivers the user's explicitly requested performance direction early, while keeping account launch, real credential protection and automatic recovery tied to evidence.


