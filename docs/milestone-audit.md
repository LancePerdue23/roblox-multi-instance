# Current desktop milestone audit

Run commands from the active GitHub checkout with Windows x64, the local .NET 10 SDK, Node.js, Microsoft Edge and WebView2 available.

```powershell
./Test.ps1                     # Current desktop checks only
./Test-Shared.ps1              # Shared services and installed-helper regressions
./scripts/testing/Audit.ps1                    # Build + regressions + personas + 15-minute synthetic soak
./scripts/testing/Audit.ps1 -SoakSeconds 20     # Harness development only; does not replace the full soak
./scripts/testing/Audit.ps1 -Mode Live -RunDirectory .build/audits/<run>
./scripts/legacy/Test-ArchivedUI.ps1          # Explicit historical WinForms checks only
```

Audits are occasional milestone runs. They are not added to the routine command or scheduled automatically. The default audit uses seed 3102026; pass `-Seed` to reproduce another sequence. Use a new output directory for each synthetic run. Fixture tests never launch Roblox, install software, change browser protocols, or operate on existing game windows. Registry and ACL regressions use temporary fixtures and may require execution outside a restricted sandbox.

Each run writes `build.json`, `report.json`, and `report.html` under `.build/audits/<UTC timestamp>`. The manifest records revision, dirty state, build hashes, helper version, seed and soak duration. Browser evidence includes Playwright traces/screenshots for failures and a soak metrics attachment. Native evidence is separate from the production startup smoke. A pass in a fixture does not establish live Roblox behavior. Short development runs remain labeled with their actual soak duration.

The native fixture executable compiles the current host sources into a separate harness with a controlled backend and isolated preferences/WebView profile. Its automation partial class is excluded from the production host project. Production startup smoke runs the production executable, independently. No installer acceptance is claimed.

## Live checkpoints

Live mode verifies the existing build manifest and uses its harness and frontend; it does not rebuild or install the helper. Close other audit runs first. Existing clients are preserved. At most two additional clients are launched, sequentially. Each newly observed client requires positive identification; ambiguous discovery stops that phase without claiming candidates. Test-owned lifetimes and window handles remain in memory, not reports.

While the live command runs, read `live-<UTC timestamp>/checkpoint.json`. Submit a matching response in `live-<UTC timestamp>/response.json`, for example:

```json
{"id":"ready","answer":"ready"}
```

Use only one of the choices listed in the checkpoint. Checkpoints cover readiness, identification of each newly opened client, and manual confirmation of two-account gameplay. A chat assistant can present those choices and write the response after the user answers. No credentials are requested or stored. Checkpoints expire after 20 minutes and trigger cleanup of positively identified clients.

The user signs in and joins games. Automation verifies process/window observations, resource availability, focus, placement and responsiveness. Reports label gameplay as manual confirmation. The harness restores original geometry and requests graceful closure of positively identified clients; it never force-stops them. Unidentified clients and clients that resist closure remain for manual inspection. Do not terminate the audit process during cleanup.

Setup, repair, uninstall, protocol changes, real update installation and automatic background cleanup are excluded from live mode. Physical per-monitor DPI transitions and screen-reader acceptance remain separate checks. Reports distinguish passed, failed, blocked, skipped and manual confirmation; blocked/skipped results never count as passes. Exit codes: 0 for completed checks without failures, 1 for failures, 2 for an incomplete live phase.

## Historical UI tests

`tests/retired-ui` preserves the original combined runner and simulation sources. Its project is excluded from active test discovery. `scripts/legacy/Test-Legacy.ps1` is a compatibility alias for `Test-Shared.ps1`; it no longer runs retired workspace tests. Shared regressions still use controlled native fixture windows where needed to validate current Windows services, and retain installed-helper notification behavior.
