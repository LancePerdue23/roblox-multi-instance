# Performance optimizations

The desktop Performance page has three presets and an explicit Apply optimizations action. Selection edits a draft. Live policy covers only accounts launched by the saved-account provider with confirmed account identity, PID, and process creation time. Unknown and externally launched clients are left alone.

## Implemented live behavior

| Preset | Background scheduling | Window behavior |
| --- | --- | --- |
| Roblox defaults | Restore app-owned values | Restore app-owned placement |
| Balanced | Below normal priority, efficiency off | Preserve placement |
| Maximum instances | Below normal priority, efficiency on | 800 × 450 outer window, minimized when in the background |

Full-screen, maximized, ambiguous, and unavailable windows are preserved. Windows may enforce a larger minimum size; an unverified resize is reported as failed instead of claiming application. A window is changed once per application, so focusing/restoring it does not cause repeated minimization. User placement changes made afterwards survive reset. Minimized preview capture is already stopped by the host; Resume preview uses the existing validated focus/restore operation.

Focus boost releases app-owned priority and efficiency changes while focused. Scheduling resumes after one continuous second in the background. It does not change FPS, visuals, or placement. Repeated native failures and external scheduling changes are reported and require explicit Apply to retry, rather than fighting another manager.

Account details include Bypass optimizations. It persists independently of nickname, favorite, organization, and reconnect metadata. Bypass restores live settings and takes precedence over the global policy. Removing bypass inherits the active policy. Accounts cannot be restarted by this feature.

The native coordinator runs independently of page navigation and monitoring pause. Closing to the tray leaves it running. Full shutdown attempts to restore owned scheduling/placement. Ownership journals retain failures for retry. A private managed-session journal survives restart; reattachment requires the original process lifetime, trusted executable, Windows user/session, and matching immutable account identity from its owned log. Competing managers cannot own that journal simultaneously.

## FPS and visuals on the next account launch

Presets request 30 FPS / reduced textures in Balanced, and 10 FPS / minimum textures / no decorative grass / reduced geometry / gray sky in Maximum instances. Both request lowest graphics and minimum anti-aliasing. Custom requests support 10–240 FPS. Apply activates these settings for the next saved-account launch; running clients retain their existing FPS and graphics.

`PerformanceLaunchSettings` supplies a fixture-tested native transaction for Roblox XML properties and an explicit graphics-flag set. It preserves unrelated data, journals original/applied values before writes, serializes transactions using an OS file lease, restores only still-owned values, and recovers interrupted transactions before staging another launch. Invalid settings or a changed installation block mutation. No arbitrary asset replacement, game scripting, client memory modification, renderer selection, or free-form flags are exposed.

`PerformanceLaunchScope` serializes current app/helper launch paths across processes. Saved-account launches retain the lease until account/PID/window verification finishes, including cancellation and uncertain joins. Disabled policies and bypassed accounts do not stage settings. Interrupted settings are recovered before the next launch; original still-owned values restore on completion. Older installed helper binaries do not participate in this lease until updated; avoid overlapping browser launches with account batches when using an older helper.

Live debugging on November 5 confirmed a 10 FPS cap in the game (reported by the owner using Shift+F5), a completed four-account batch with unique verified lifetimes on the same server, and restoration of the original FPS/graphics property values after a batch. Account launching uses `--app` with an explicit authentication URL, ticket and join URL, avoiding protocol NoReload tray reuse and warm-session identity inheritance. Production-channel pinning and an exact signed-version preflight prevent experimental-channel installers from closing existing clients during a batch. The full acceptance matrix below remains incomplete: visual flag effects, shutdown writes, crash/update behavior, mixed bypass scenarios, and long-duration savings need separate validation. Do not interpret a staged flag as measured effectiveness.

Roblox's [local configuration allowlist](https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569/) can change. Verify each candidate flag and its meaning against the installed client before activation. FPS is staged through the settings XML, not a denied FPS FastFlag.

## Contracts and migration

- Profile format 2 adds enabled/focus-boost state, texture quality, minimum MSAA, grass, geometry, gray sky, and window behavior. Legacy format 1 remains readable and inactive; its older FPS range remains readable. Format 2 validates custom FPS at 10–240.
- `performance.activate` saves with an expected revision and activates live policy. `performance.optimizationState` returns per-instance requested/effective controls and applied/bypassed/unavailable/failed summaries.
- Existing save/import operations save inactive preferences and restore app-owned live controls. Import never activates an imported policy, even if the source was active. Reads and previews do not create profile/session storage.
- `accounts.optimizationBypass` takes an opaque account ID and an explicit Boolean. DTOs expose the persisted bypass and instance badges. No account sessions, credentials, tickets, paths, or native handles are exposed to the page.
- Manual controls remain available for unmanaged clients. Managed clients with an active global policy use the account bypass or global defaults for restoration. Explicit interrupted-setting recovery deactivates automatic policy before resetting native controls.

## Remaining live acceptance

Automated validation on November 5, 2026: root `Test.ps1` passed (including 69 browser workflows, with five optional workflows skipped); `Test-Shared.ps1` passed 133 checks. Final focused runs passed 187 backend checks and 46 performance/account/preview browser workflows. The default development desktop build succeeded. These checks use synthetic accounts, temporary settings files, and harmless native fixture processes; they do not establish live Roblox compatibility or memory savings.

Use owner-selected test accounts and a public test game; do not substitute fixtures for live compatibility or savings evidence.

1. Establish defaults for one account. Record actual FPS, screenshots of graphics settings/effects, CPU, working set, private commit, available system RAM, responsiveness, and disconnects after two minutes settling and five minutes observation.
2. Validate settings consumption using optimized alt → exempt main → optimized alt, with an already-running main. Repeat reversed, with a settings-menu change, on client shutdown, after app crash/recovery, and after a Roblox update. Prove that original preferences and exempt accounts are preserved throughout.
3. Demonstrate a reliable consumption signal and hold a shared launch transaction through it. Serialize all app/helper launch paths that could overlap staged settings; verify cancellation, handoff, and failed/unknown join outcomes. Do not use a fixed delay or the existence of a window as proof of settings consumption.
4. Check supported installation/configuration ownership and competing launchers before connecting the adapter. Refuse conflicting/unknown ownership and preserve external changes; a competing launcher's existing files must never be silently replaced.
5. Verify each visual request individually. Confirm 10 and 30 FPS caps using an independent frame measurement, not the saved file. Mark ignored/removed flags unavailable. Check gameplay visibility and rendering stability.
6. Only after steps 2–5 pass, connect the gated adapter to the verified launch pipeline and report launch requests as pending relaunch for already-running clients. Bypass restoration of launch-consumed settings must also report pending relaunch.
7. Repeat matched comparisons at 1, 5, and 10 accounts where hardware permits, with identical game/server/camera and account sets. Record launch success, disconnects, CPU, working set, private commit, available system RAM, and foreground responsiveness. Record unsupported counters as unavailable. Summed process working sets can double-count shared pages; do not call their sum unique system RAM.

No measured RAM-saving percentage, client-capacity increase, or live graphics/FPS acceptance is claimed. Automatic working-set trims, hard memory ceilings, CPU quotas/affinity, and arbitrary map removal are intentionally excluded.
