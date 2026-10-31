# Simulated-user audit

The opt-in audit tests workflow segments and actual WinForms controls with synthetic data. It does not change product behavior. It assigns twelve personas across fifty journeys and runs 1,000 sessions (20 per journey), then renders focused page hosts plus all nine workspace destinations in three themes at three scale factors.

From the development checkout, establish the regression/build baseline:

```powershell
./Test.ps1 -Dotnet ./.build/sdk/dotnet.exe
./Build.ps1 -Dotnet ./.build/sdk/dotnet.exe -Output .build/user-simulation/baseline-release
```

Use a supported .NET 10 SDK instead of the local SDK path if necessary. Run the audit after building the tests:

```powershell
./.build/sdk/dotnet.exe tests/bin/Release/net10.0-windows/RobloxLaunchHelper.Tests.dll --user-simulation --seed 20261029
```

The seed is optional and defaults to `20261029`. Invalid arguments return 2. Failed invariants return 1 after all sessions finish; an otherwise completed run returns 0. Layout observations are evidence, not invariant failures. The ordinary regression suite remains independent.

Outputs remain ignored under `.build/user-simulation/`:

- `results.json`: seeds, journey/persona assignments, requested/effective fixture client counts, virtual timing, degradation, assertion results, measured technical durations, and render observations.
- `scenario-matrix.md`: fifty journey scripts and acceptance criteria, actual pass counts, and twelve persona goals.
- Fifteen focused-host PNGs plus 108 workspace PNGs under `workspace-renders/`: dashboard, active instances, arrangement, notification settings, and shortcut settings at 100%, 150%, and 200% layout scaling, using fresh forms for each render.
- `seed-<seed>/run-<random-id>/`: fresh isolated session preferences and history fixtures for every invocation. Repeating a seed overwrites the top-level evidence, but never reuses a prior run's mutable fixtures.
- `report.md`: the reviewed findings report delivered with the audit; this is an analyst artifact, not an automatically generated verdict.

Each session has a deterministic seed. Journey order guarantees coverage; a deterministic schedule varies requested counts 0/1/2/8/20/50, virtual timings 0/50/749/750/2000/60001 ms, and healthy/degraded state. Some actions require at least one client; `EffectiveFixtureClients` records that normalization. Dimensions are relevant only to the subsystem under test: a startup-router check does not exercise fifty windows merely because its session fixture has fifty clients.

The `Layer` field distinguishes workflow, controls, and presentation checks. Workflow checks call real coordinators/reducers against fixtures. Control checks drive actual buttons, selection, and editor ordering. They do not prove discoverability. `ExecutedActions` counts injected control inputs where measured, including disabled repeat-click attempts; `InvariantChecks` counts assertions separately. Backtracking and unexpected-dialog counts are null when unmeasured. Neither is inferred from a passing assertion. Durations are technical wall-clock times, not human task times.

Screenshots use `Control.Scale` to stress layout. They do not certify real Windows DPI transitions, physical keyboard navigation, assistive technology, or usability. Render metadata includes ancestor clipping, the WinForms traversal order of tab stops, and duplicate button mnemonics. Missing explicit accessible names are diagnostic metadata only: WinForms may derive an effective name from button text. Review images before making findings.

## Live checks

Run only when explicitly authorized to open and close real Roblox clients:

```powershell
./.build/sdk/dotnet.exe tests/bin/Release/net10.0-windows/RobloxLaunchHelper.Tests.dll --live-user-simulation
```

This mode requires a normal Windows token and the installed helper. It records twelve outcomes in `live-results.json`. It preserves the verified process lifetimes present at entry, launches up to two additional clients through the installed helper, focuses and arranges only newly discovered clients, restores their geometry, attempts fullscreen entry/exit, and gracefully closes test windows. It also tracks fresh process lifetimes created when test clients close. Eligible aged windowless test clients can be force-stopped using the production cleanup checks. Avoid launching additional clients concurrently: verified clients appearing after the baseline throughout the live run are treated as audit-owned.

The live tray check creates a real tray icon with isolated layout preferences and fixture hotkey registrations; it does not certify global shortcut delivery. Passive notification delivery is explicitly blocked in the current harness because it leaves user notification preferences unchanged and has no popup observer. Live launch success means a verified process was observed, not confirmed sign-in, separate accounts, in-game success, or authenticated worker completion. Blocked steps remain recorded and must not be counted as passes. A normal exit can still leave audit clients if graceful close/cleanup was blocked; inspect `live-results.json` and the active-instance panel.

Use a disposable Windows environment for real installer, uninstall, update-installation, interrupted recovery, and browser registration mutation. The synthetic audit covers routing, recovery choice/cancellation, and metadata validation; the regression suite supplies the deeper temporary-registry and update transaction tests. The fifty journeys are bounded workflow segments, not fifty full end-to-end account/gameplay tests.

Report artifacts contain synthetic captions and approved fields. Live outcomes persist counts and bounded explanations, not titles, account information, process identifiers, monitor identities, or raw launch input. Local product logs generated by live launches retain their existing behavior.

## Unified workspace checks

The final ten journeys drive the actual shared shell: navigation without extra windows, appearance persistence/corrupt-store recovery, embedded update checks and notes, offline retry, navigation during a download, cancellation/deduplication, integrity rejection, explicit setup, owned confirmation cancellation, selection preservation, and layout ordering/preview invalidation. All native actions, downloads, and setup in these journeys use injected fixtures. The remaining forty journeys retain their existing coverage.

For focused investigation after building tests, use `--workspace-tests` or `--workspace-render`. The rendering command writes all nine destinations in Dark, Light, and Warm Neutral at the minimum window size plus 100%, 150%, and 200% layout scaling. Inspect images; these scale-stress renders do not certify physical per-monitor DPI transitions or screen-reader behavior.

After rebuilding and installing, `./tests/InstalledWorkspaceSmoke.ps1` uses Windows UI Automation to navigate every main page at the minimum window size, verify appearance persistence across restart, restore the original color scheme, and verify that `--updates` routes into the existing window. It never selects Launch Roblox, Apply, cleanup, repair, or setup. Its results remain in `.build/user-simulation/installed-workspace-smoke.json`.
