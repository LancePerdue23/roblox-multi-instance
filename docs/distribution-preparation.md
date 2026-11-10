# Distribution implementation and acceptance

Performance now has explicit global live presets, persistent account exemptions, and a background policy coordinator. FPS and graphics requests remain gated; fixture transaction tests do not establish live Roblox behavior or RAM savings. See [performance implementation and acceptance](performance-optimizations.md) before enabling launch-time settings.

The v1.2.0 candidate includes all current development features in unsigned App + Helper, Helper only and portable packages under one release tag. The owner reported development-build testing and chose unsigned distribution; packaged installation/update acceptance remains pending because no isolated Windows environment is available. It is a manual-download validation prerelease; public stable release discovery does not offer prereleases. Historical v1.1.2/helper-v1.1.2 assets remain unchanged. The repository remains private; see [public-release readiness](public-release-readiness.md) before any visibility change.

## Build and verify

Use the local .NET 10 SDK on Windows x64. Run root `Test.ps1`, `Test-Shared.ps1`, `Package.ps1`, and `scripts/testing/Test-Distribution.ps1`. Package builds use fresh staging beneath .build/distribution. They produce these ignored local outputs under artifacts:

- RobloxMultiInstance-Setup.exe: desktop, matching helper, and rmi terminal commands.
- RobloxLaunchHelper-Setup.exe: helper and rmi, excluding desktop assets and presentation components.
- RobloxMultiInstance-Portable.zip: portable desktop folder, helper/CLI dependencies, and the matching graphical setup payload.
- Per-installer SHA-256 files and assets.json containing edition, version, protocol, sizes, digests, and payload allowlists.
- Portable ZIP SHA-256 sidecar and SHA256SUMS.txt covering both installers, the ZIP, and assets.json.

Test-Distribution only inspects embedded payloads and checks package hashes; it does not install anything. Setup's --inspect --expect-edition Desktop|Helper --silent mode provides the same read-only check. Root Build.ps1 remains the desktop development build; use Package.ps1 for distribution.

## Installed behavior

Graphical desktop setup installs the app and helper together. Browser Play integration is a checked, visible choice for fresh installations. Upgrades retain the existing preference, settings, launch history, and original protocol backups. Ordinary app startup never installs or elevates automatically. Portable builds offer explicit setup through System → Health.

Edition/version/protocol/owner metadata and installed payload manifests are protected beside the existing per-Windows-account helper installation. A helper without this metadata is legacy and requires an explicit upgrade. Switching modern editions requires uninstall first. Edition versions, explicit helper-version compatibility and helper protocol must validate; metadata and executables are verified before installation. Generated protected rollback copies are retained for recovery.

The helper entrypoint retains browser URI, worker, verification, and administrative compatibility modes. It no longer opens the retired workspace. Helper-only browser launches are windowless and on demand; they do not start notification hosts, tray mode, automatic update notices, hotkeys, or resident cleanup. Desktop notification presentation is a separate assembly. Desktop-edition launches may request a once-per-boot update check; downloads and installation remain explicit.

The shared core has no WinForms presentation reference. Inactive original workspace source is preserved under src/desktop/retired-source and excluded from active compilation. The frozen archive and retired UI tests remain separate.

## Terminal and maintenance

Run rmi --help for launch, status, diagnostics, worker tests, browser integration, setup/repair/recovery/uninstall, settings, history/report export, and manual update commands. --json emits structured results; errors use stderr in human mode. Exit codes are 0 success, 1 failure, 2 usage, 3 warning/partial, 4 unknown, and 1223 canceled. Ctrl+C cancels cooperative work; it does not terminate an already started game or an installer commit.

Mutation commands prompt unless --yes is supplied. Redirected input requires explicit confirmation flags. Use --player with an exact signed RobloxPlayerBeta.exe when recovery is ambiguous. Setup accepts --installer to select the desired edition's setup file and --disable-integration for a fresh installation. Existing preferences take precedence on upgrade.

Uninstall retains user settings/history by default. Graphical uninstall offers data removal; terminal uninstall accepts --remove-data with confirmation. Only owned shortcuts, command registrations, PATH entries, and browser values are removed. A bounded, hidden cleanup process removes protected files after app/helper processes release them. The next terminal session receives the registered rmi PATH entry.

Updates select the setup asset for the installed edition, retain verified staged downloads, and validate digest/size/version plus the setup's embedded edition/protocol/manifest. Unsupported releases expose their release page instead of running an arbitrary executable. Setup handoff acknowledges the parent, waits for graceful closure, revalidates the package, and preserves Roblox games. Busy files and canceled elevation can be retried. A handoff message is not a claim of successful installation.

## Required manual acceptance before release

Use a disposable Windows account to test both editions: fresh install, legacy upgrade, normal-user worker verification, ambiguous/missing/signed player selection, canceled elevation, protocol ownership conflicts and restoration, repair/recovery, uninstall with retained and removed data, retained-data reinstall, edition switching, interrupted setup, locked payloads, and real update handoff. Confirm no helper remains resident while idle and inspect shortcut/PATH/uninstall ownership behavior.

Separately verify two accounts remaining in-game, monitor/DPI placement, OS theme changes, and screen-reader behavior. Fixture results do not establish these outcomes. Do not publish or describe these packages as production accepted until this matrix has been completed.

## November 5 workspace polish

Preview privacy, fit and density now persist in the desktop web profile. This is preview-grid privacy, not a global streamer mode. Failed/stale cards expose Retry, which restarts the visible capture subscription without touching Roblox processes. Saved performance profiles can explicitly apply their saved CPU priority to one selected process through the existing frozen confirmation, native lifetime validation and owned-setting reset path. FPS/graphics remain unapplied preferences; EcoQoS remains manual.

The machine-local supporter build also displays its build time and source revision. Public frontend compilation removes that metadata and label; the public-build browser check asserts their exclusion. Packaging continues to reject local preview assets.

Publisher-authenticated update signing and the manual Windows acceptance matrix above remain open. These UI changes do not supply a publisher signing identity, establish live game compatibility or authorize installation/publication.

## November 5 signing and profile follow-up

Publisher signing support and compiled signer pin checks are implemented; see [publisher-signing.md](publisher-signing.md). Unsigned validation packaging remains available, while `Package.ps1 -Release` requires a real signing identity. `scripts/testing/Test-Distribution.ps1 -RequireSigned` refuses unsigned validation artifacts. No signing identity exists yet, so this is not a signed or production-accepted release.

Saved profiles now include optional EcoQoS (older files default to unchanged), with explicit apply through the existing process confirmation/reset path. CPU priority and EcoQoS requests are applied only where supported. FPS/graphics requests remain unapplied until a client integration has been verified.

The [isolated acceptance checklist](releases/windows-acceptance.md) is prepared and remains Not run; the owner has no isolated Windows environment yet.

## November 7 account and update hardening
The owner confirmed live signup works. Partial autofill now requires explicit Refill before replacing fields again. Automatic layouts suspend the launch focus guard, and account-creation summaries distinguish canceled/stopped/skipped queues with actual newly saved counts. Native regression checks cover password reveal/edit/hide, disabled states, accessible keyboard controls, and nested/canceled focus suspension; browser fixtures cover credential action spacing and account switching.

Publisher signing remains optional for repository-trusted validation builds. The desktop checks the latest published stable GitHub release on startup and offers an in-app review/download/setup flow. No download or setup starts without a user action. Unsigned builds retain repository URL, SHA-256, size, executable version, edition/protocol and embedded payload checks. Builds with a compiled publisher pin still require a matching trusted signer. Public stable install/update acceptance remains separate; the older Not run checklist has not been claimed complete.

## November 8 diagnostic and recognition release

Historical v1.1.1 and helper-v1.1.1 packages are unsigned validation prereleases. Setup phase diagnostics are described in [release notes](releases/v1.1.1.md). Current updates use anonymous public stable releases. Recognition artwork ships; unfinished supporter artwork and cumulative-payment changes remain excluded. Automated/package checks do not close the outstanding Windows acceptance matrix or establish the original tester failure is fixed.

## November 9 profile-cleanup release

v1.1.2/helper-v1.1.2 fix transient profile cleanup during existing-account badge claims. The tester confirmed the prior installer hotfix works. Badge claim confirmation and broader Windows acceptance remain pending; see [release notes](releases/v1.1.2.md). Unfinished supporter changes remain excluded.

## Next release: one release per version

Owner decision (November 9): starting with the next release, publish one `vX.Y.Z` GitHub release containing both installer options, rather than separate desktop and `helper-vX.Y.Z` releases. Leave historical releases intact. The packaging and updater rules are implemented below; publication remains a separate owner action.

- Include `RobloxMultiInstance-Setup.exe` (App + Helper, recommended), `RobloxLaunchHelper-Setup.exe` (Helper only), the portable ZIP, and matching checksums in the same release.
- Give the README clearly labeled download links pointing to existing applicable assets. After a stable release exists, the desktop can use releases/latest/download/RobloxMultiInstance-Setup.exe. Keep the helper link pinned if the latest release omits its installer.
- Publish a combined `assets.json` covering both editions and the portable package, with consistent checksum manifests.
- Verify the updater selects the matching installer for each installed edition from the shared `vX.Y.Z` tag. Retain compatibility with historical helper tags where needed, and test both edition update paths before publication.
- Keep machine-local supporter tools and private recognition deliveries excluded. Release badges refresh on release changes or manual dispatch; the public event switches both to live Shields.io badges. Downloads counts all uploaded release assets, including verification files. The package-only reporting script remains a separate metric.
- Owner follow-up: do not build or publish a new helper-only package when the helper itself has not changed. Assess its shared runtime dependencies, installation behavior, and protocol compatibility as part of that decision; a desktop-only change does not automatically require a helper release. Keep the README helper download pointing to the latest applicable existing package when no new helper package is needed.
- App-only releases use an explicit helper-version contract and a verified prior helper payload. Shared core/runtime conflicts fail packaging; omitting a helper installer alone does not establish compatibility. Desktop comparisons use the edition version, while helper and shared-core checks use the retained helper version.

## Anonymous updates and app-only packaging implemented

Private-token setup and prerelease opt-in have been removed. Current updates discover only public stable releases anonymously; historical private validation builds still require manual downloads. This does not authorize making the repository public or publishing artifacts.

The single-release layout and explicit `HelperVersion` manifest contract are implemented. App-only packaging can retain an existing helper payload and CLI when their shared core/runtime bytes remain compatible; otherwise packaging fails and a changed helper is required. See [update validation](update-checker-validation.md) for `-ExistingHelperPayload`, `scripts/testing/Test-Distribution.ps1 -AppOnly`, migration scope, release selection, and pending Windows acceptance. Keep older helper download links when a release omits a new helper installer.
