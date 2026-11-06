# Next gap-closure delivery plan

Prepared November 4, 2026. Status: planned; no implementation or live testing is implied.

This is the execution order for the remaining work after the six integrated assignments in [feature-parity-progress.md](feature-parity-progress.md). The [original roadmap](feature-parity-implementation-plan.md) retains the complete G01–G56 scope and detailed requirements. The [competitive review](competitive-review-2026-11-03.md) remains a dated comparison of pinned competitor snapshots, rather than a description of today's checkout.

**Target outcome**

A user can organize accounts, select a game/server, launch the intended accounts, understand each session's state, apply effective performance settings, restore a workspace, and recover from failures. Prioritize this complete daily workflow before expanding into scripts, outfits, or broad account utilities. Preserve the layout planner, on-demand helper, process identity checks, CLI, and existing architecture.

**Starting point**

| Capability | Current evidence | Remaining work |
| --- | --- | --- |
| Saved-account launch and batches | Native provider connected to desktop; explicit development-test confirmation; fixtures | Real authentication, selected-account joins, two-account coexistence and process/log correlation |
| Account organization | Search, groups, notes, colors, favorites, persistent ordering | Ongoing validation, summaries, secure migration/backup |
| Performance | Dedicated page, bounded history, CPU/memory/I/O, saved profiles, manual priority/EcoQoS | Applied profile semantics, account roles, launch controls, further native capabilities and measurements |
| Startup resilience | Native readiness/failure UI, bounded retries, explicit safe-rendering restart | Controlled Windows/GPU acceptance |
| Distribution | Packaging and private validation release workflow documented | Publisher-authenticated updates and complete Windows acceptance |

Existing November 3 fixture/build results are baseline evidence, not a new verification of subsequent edits. Recheck the dirty checkout before implementation. Record Implemented, Enabled in production, and Live verified independently for each delivered slice; do not reduce them to one completion checkbox.

**Delivery sequence**

| Phase | Result | Original scope | Depends on |
| --- | --- | --- | --- |
| 0 | Accurate capability ledger and actionable acceptance packet | M0, M1, M12 | Current source inventory |
| 1 | Trusted launch/session foundation | M1, G01–G04, G07, G17 | Phase 0; owner-controlled live session |
| 2 | Useful game and server selection | M4, G11–G16 | Read-only work can start after 0; execution requires 1 |
| 3 | Effective performance profiles | M2/M5, G23–G29 | Existing monitoring; verified binding for account policies |
| 4 | Genuine session protection and backup | M3, G08–G10, G54 | Authentication/storage feasibility decision |
| 5 | Bounded recovery and saved workspaces | M6/M7, G18–G22, G30–G35 | 1–3; lock integration from 4 |
| 6 | Privacy, accessibility and useful notifications | M8/M9, G36, G42–G44, G52/G55 | Shared policies start at 0; integrate through all phases |
| Release track | Authenticated updates and integrated acceptance | M12, G40/G53/G56 | Signature work starts after 0; final gate spans shipped phases |

Phases are delivery priorities, not a requirement to block independent work behind live acceptance. If launch verification is blocked, continue read-only game selection, signature work, storage research, and manual process controls. Never enable account automation using uncertain identity as a substitute. No calendar estimates until phase 0 identifies compatibility and live-test dependencies.

**Phase 0 — Reconcile status and prepare the next package**

- Reconcile the old adapter-unavailable text in account-launch-feasibility.md with its newer implementation update. Preserve historical context explicitly.
- Update the original roadmap's launch gate: the current development path is enabled with a testing confirmation; general production-readiness claims still require acceptance. Do not silently remove the development path or call it live verified.
- Create a capability ledger keyed by G IDs with implementation, enablement, live evidence, limits, and next action. Include build hash/dirty state with acceptance evidence.
- Finish shared capability vocabulary and sanitized session/event contracts only where needed by the next slice. Extract account hooks/components as they change; avoid an application-wide refactor.
- Prepare an owner-run one-account/two-account acceptance checklist, including existing clients, selected public Place ID, expected identities, cancellation, expiry, Unknown outcomes, and cleanup scope. Never collect credentials in chat.

Exit: source and documentation agree on what is available, and the exact next live test and its expected outcomes are reviewable. Planning does not authorize running it.

**Phase 1 — Prove launch and build durable session state**

1. Validate the existing native launch flow before expanding it. Prove the selected account joins while the ordinary browser account differs, then prove two selected accounts coexist and map to the correct process/window.
2. If current player logs cannot establish the expected correlation, revise the evidence model and fixtures; retain Unknown until an authoritative association is available. Never retry an uncertain handoff automatically.
3. Implement bounded session observation with Starting, Joining, InGame, Disconnected, Exited and Unknown states, timestamps and evidence sources. Account identity and game state are separate claims.
4. Add explicit session validation with Valid, Expired, Challenged, RateLimited, Unavailable and WrongIdentity results. Bulk validation uses bounded concurrency and cancellation; network errors do not mark accounts expired.
5. Show actionable per-slot batch outcomes and retry only confirmed failures. Preserve ordered selections, started games on cancellation, and the complete/uncanceled/verified automatic-layout rule.

Exit: fixtures cover identity mismatch, stale logs, PID reuse, concurrent launches, cancellation and partial batches; controlled live evidence proves the normal path. Any unresolved live failure keeps the affected capability partial and blocks automatic recovery for it.

**Phase 2 — Make Play useful**

- First slice: versioned typed targets, game favorites and recents, persistent selected accounts/target, and validated import/export of nonsecret workspace metadata.
- Second slice: bounded public-server browser with occupancy, sorting, pagination, unavailable states and a group-capacity check. Recheck before launch; capacity remains a snapshot, not a reservation.
- Third slice: supported private/share/deep links, same-server groups and follow-first behavior. Keep private link material native and redact it from reports and notifications. Resolve links through explicitly validated provider behavior.
- Later slices: friend targets, access checks and inferred region with clear provenance/query cost. Do not block the basic server workflow on these.

Exit: malformed links, redirects, stale capacity, throttling and cancellation are fixture-covered; actual server joining is separately verified. Favorites distinguish public metadata from secret-bearing targets.

**Phase 3 — Turn saved profiles into effective controls**

- Keep requested, observed and applied settings distinct. Current saved profiles stay visibly save-only until their specific controls work.
- First deliver supported account/launch FPS and graphics overrides through a researched settings mechanism. If settings are installation-wide, serialize ownership and disclose that scope rather than claiming independent per-account values. Journal and restore only owned changes; respect bootstrapper ownership.
- Add global → account role/profile → session override precedence, loading grace, main-account exemption and explicit Apply/Reset behavior. Automatic foreground policy requires trusted binding and a visible opt-in.
- Add CPU Job budgets and affinity as separate native capability slices. Establish safe Job membership/lifetime behavior before assignment. Keep existing priority/EcoQoS restoration guarantees.
- Improve memory-pressure visibility first. Add trim or resource-driven restart only with honest working-set versus commit reporting and measured usefulness. GPU/private-working-set counters are capability-detected additions.
- Benchmark the whole manager process tree and matched Roblox workloads, controlling foreground, loading, minimized and preview state. Record responsiveness and failures alongside resource consumption.

Exit: settings isolation/restoration, interruption, other-tool conflicts and stale process identities pass fixtures. Live measurements demonstrate actual behavior; no savings claim is based only on a working-set drop. Higher client counts require separately agreed scope; the current live harness permits at most two additional clients.

**Phase 4 — Finish account protection**

1. Decide whether supported launch/sign-in can use vault-held sessions or requires persistent browser profiles. Do not duplicate secrets while that decision is unresolved.
2. Define the threat model, password/key handling, idle lock, recovery and behavior of already-running games. An app lock alone does not encrypt persistent WebView2 sessions or protect against arbitrary same-user code.
3. Implement preservation-first migration and genuine at-rest protection where feasible. If browser sessions cannot meet the model, retain an explicitly narrower design and document the blocker.
4. Add password-encrypted portable backup/restore with bounded parsing, authenticated encryption, duplicate handling, preview and atomic commit. Preserve originals after wrong-password, corrupt or interrupted restore.
5. Add essential avatars/summaries and supported migration imports as independent slices. Never collect password lists; challenge/reconnect requirements remain explicit.

Exit: migration, wrong-password/corruption, lock races, cross-instance access, exports and secret-canary fixtures pass. Lock/recovery claims match actual storage. Backup restoration is verified in isolated storage before owner-controlled real-session acceptance.

**Phase 5 — Reliable long sessions and workspaces**

- Deliver session-stop timer and scoped emergency stop first. Preview affected sessions and distinguish stopping pending automation from closing games.
- Add opt-in rejoin/crash recovery only for positively bound sessions. Use intentional-leave suppression, retry budgets, backoff, circuit breaker, lock/cancellation checks and a reconcile-before-retry rule. Unknown state does not trigger a restart.
- Add saved workspaces containing accounts, validated targets, profiles and named layouts. Preview missing accounts, unsupported controls, changed monitors and capacity before execution; do not persist stale process IDs or launch tickets.
- Extend minimize/restore and compact/borderless tools with geometry/style restoration. Add bounded previews and per-client audio only after native compatibility checks; both remain optional.
- Evaluate anti-AFK after core recovery, as an explicit opt-in with verified targets and bounded input behavior.

Exit: fixture clocks prove timers/retry bounds; shutdown/cancel/lock cannot leave new launches queued. Real intentional leave, disconnect and crash cases are separately verified. Workspace restoration passes changed-monitor/DPI and partial-launch scenarios without altering unrelated games.

**Phase 6 — Polish the complete workflow**

- Apply one privacy-display policy across account rows, native dialogs, previews, history, notifications, tray and external alerts. Private targets and secrets remain redacted independently of streamer mode.
- Establish translation keys, RTL-compatible layouts and accessible controls incrementally; then validate reviewed language coverage, keyboard journeys, focus restoration, screen reader, high contrast and scaling.
- Add local session notifications from sanitized shared events. Discord webhooks use protected storage, deduplication, bounded retries and explicit opt-in. Rich presence has its own consent and privacy settings.
- Extend themes and tray shortcuts around the actual daily workflow. Public-network restrictions remain an optional policy with explicit unavailable/stale states.

Exit: privacy canaries cover all claimed surfaces; notification failures never block launches; keyboard/screen-reader and real DPI acceptance are recorded separately from browser fixtures.

**Release track — Start authenticity early, finish acceptance last**

- Add a signed canonical release manifest and embedded publisher trust anchor, including key rotation/revocation and rollback policy. Preserve current artifact size/hash/edition/version/protocol validation and execution-boundary revalidation.
- Keep publisher-manifest verification distinct from Windows Authenticode/SmartScreen identity. Signing secrets stay outside the repository.
- Verify startup retries and explicit safe rendering on controlled Windows hardware; fixtures alone do not prove GPU recovery.
- Complete the existing distribution matrix: both editions, installation/upgrades, ownership conflicts, repair/recovery, canceled elevation, interrupted/busy updates, retained-data reinstall and uninstall. Preserve running games and browser-first operation.
- Promote only the capabilities actually accepted. Unfinished optional capabilities remain explicitly unavailable or development-only; broad roadmap completion is not a prerequisite for an honestly scoped release.

**Deferred breadth**

Keep M10/M11 in the original roadmap: account utilities, outfits, signup assistance, build management, upload QoS, workflows, recordings, trusted scripts and authenticated local integrations. Reassess them after the core workflow is accepted. Extend CLI support alongside each nonvisual service rather than postponing all CLI work to M11. Remote APIs and script runtimes need a demonstrated user workflow before taking priority over core reliability.

**Verification and working rules**

- Work only in the active checkout; preserve unrelated edits and archive requirements. No staging, commits, publication or installation is implied by this plan.
- Run focused fixtures for each behavior; at integration boundaries run Test.ps1 and Build.ps1, plus Test-Shared.ps1 for core/helper changes. Packaging changes require Package.ps1 and Test-Distribution.ps1; these do not install software.
- Use occasional full milestone Audit.ps1 runs with the documented 15-minute synthetic soak. Short harness runs never substitute for acceptance.
- Use the checkpoint procedure in milestone-audit.md for owner-controlled live work. Keep fixture testing isolated from real games, accounts and protocol ownership.
- Before each package, record dependencies, exact scope, changed contracts, fixture acceptance, live gates and rollback behavior. End with evidence and unresolved limitations, not a blanket parity claim.

**Next implementation package**

Deliver phase 0 plus a read-only typed target/favorites slice from phase 2. Prepare phase 1's live acceptance packet in the same package, without launching games. This supplies useful Play improvements while the owner-controlled launch proof is pending. Start release-manifest design after those boundaries are clear. Do not repeat the already delivered performance page, organization or startup implementation.
