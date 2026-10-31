# Migration ledger

## Fixed decisions
- React + TypeScript + CSS in bundled WebView2, minimal WPF host.
- All new implementation lives under preview/. Original sources, tests, scripts and release output remain unchanged.
- Portable preview with separate executable and LocalAppData root. Existing installed helper performs launches and maintenance.
- History, launch notification preferences, browser integration and helper maintenance are intentionally shared and labeled.
- Appearance, layouts, shortcuts, browser profile and preview logs are private. Import is explicit and copy-only.
- Original named mutexes are retained only for exclusive ownership of background cleanup and global shortcuts.
- No commits, staging, pushes, installation, or promotion.

## Baseline
Immutable archive: .build/migration-baseline/20261030-211138 (source.zip, source-hashes.json, git-status.txt, release executable and hash).
Includes current modified and untracked nonignored project source. Excludes caches, runtime data and credential patterns.
Original release exists locally. Source identifies as upcoming 1.1.0; archived binary is not assumed identical to source.
Node 24.20.0; .build/sdk/dotnet.exe 10.0.403 available. System dotnet selects unsupported SDK6.

## Work ownership
- Primary: baseline, migration docs, verification and integration review.
- backend agent: preview/backend only; reusable backend copy + public PreviewBackend facade.
- host agent: preview/host and preview build/test/packaging scripts; WPF lifecycle and bridge.
- frontend agent: preview/web only; React UI and browser tests.

## Resume protocol
Read this ledger and parity.md before continuing. Inspect git status. Preserve baseline and original files. Run preview build/tests and original tests with local SDK. Record actual results and unresolved gaps below; do not mark fixture checks as live acceptance. Contracts must remain coordinated across backend/host/web. Do not modify another agent's files until ownership is released.

## Progress
- Baseline archived before implementation.
- Original regression suite started with Test.ps1 -Dotnet .build/sdk/dotnet.exe.
- Implementation agents active; feature completion and live acceptance not yet claimed.

## Baseline verification results
- Original Test.ps1 using local SDK: 122/122 tests passed outside sandbox. Initial sandbox run 104/122 due registry/filesystem access restrictions; rerun resolved these without source changes.
- Existing --workspace-render completed; 111 representative PNGs under .build/user-simulation/workspace-renders (includes prior artifacts; screenshots are fixture-based).
- VerifyPreservation.ps1: 80 original project files and original release executable match immutable baseline.
- Preview implementation still being integrated; live Roblox/installer acceptance not performed.

## Original-build verification
`./Build.ps1 -Dotnet ./.build/sdk/dotnet.exe -Output ./.build/original-preservation-check` passed. The output override was deliberate: release/ remained untouched. Original regression suite: 122/122 passed. Fixture render completed and representative original Instances screenshot inspected. Post-build preservation check still passes all 80 source files and release executable.

## Preview integration evidence (in progress)
Backend and WPF host compile. Real WebView2 smoke captured `.build/preview/host-smoke.png` and exited successfully without requesting helper/maintenance actions. Primary visually inspected the empty Instances page: local frontend rendered, navigation and shared-action labels visible, no clipping at default desktop size. Additional browser fixture tests and final build are being completed.

## Final automated results and handoff
- Original preservation: 80 source-file SHA-256 hashes and original release executable match baseline.
- Original regression suite: 122/122 pass; original Build.ps1 passed with isolated output override.
- Preview host boundary checks: 16 pass.
- Preview backend regression checks: 26 pass.
- Generated C# -> TypeScript DTO drift check: pass; frontend imports generated DTOs.
- Frontend TypeScript/Vite production build: pass.
- Frontend bridge Vitest checks: 4 pass.
- Offline Playwright workflow tests: 3 pass (all pages/layout apply confirmation, appearance/shared notification confirmation, narrow viewport/manual cleanup confirmation).
- Real WPF/WebView2 host startup smoke: exit 0, local navigation and screenshot capture passed, no helper/maintenance action requested.
- Primary visually reviewed real host screenshot plus fixture Settings and narrow Instances screenshots. Multi-DPI hardware acceptance remains open.

## Current completion boundary
Portable comparison implementation is present and packaged. This is NOT a claim of production/live parity acceptance. See parity.md for remaining live two-account, real placement, display topology/scaling, UAC, installer and update-installation checks. No installed helper was replaced or new protocol/task registered by this work. No staging/commits/pushes.

## Next task when resuming
Start with preview/README.md and parity.md. Run preview/Test.ps1 and VerifyPreservation.ps1 when changing preview implementation. Evaluate the portable preview with controlled live acceptance, record results per parity row, then make any UI refinements. Keep current release and source baseline until the user explicitly chooses a replacement. Legacy compatibility snapshot contains inactive WinForms page classes; remove them only in a later deliberate dependency extraction, never by modifying the preserved original app.

## Final package revision
Final frontend bundle: index-Crp5jqsS.js (cancellation request ID fix included). Host has a preview-specific asInvoker / PerMonitorV2 manifest to preserve physical desktop coordinates. Full preview/Test.ps1 passed all 49 synthetic checks and DTO/build checks before the manifest-only revision; final host publish and real runtime smoke passed after it with zero warnings. Production assets were verified to contain the final bundle. Package.ps1 produces a timestamped portable ZIP with README and excludes smoke artifacts.

## Design audit revision
See design-audit.md. Replaced all dropdowns with a shared themed accessible popup; themed title bar and tray menu follow appearance and OS changes, with WPF chrome fallback when native caption colors cannot be verified. Restored previous WinForms window icon to title/tray/executable. Improved keyboard tab navigation, theme scrollbars/focus/selection, and popup viewport clipping.
Validation: full preview/Test.ps1 passes 51 synthetic checks (16 host +26 backend +4 bridge +5 UI), TS/Vite + DTO drift pass; zero-warning publish, native host smoke, four fallback caption themes, minimize/maximize/restore smoke pass. Original source/release preservation passes. Current portable package regenerated. Live game/installer tests not repeated for styling-only work.
