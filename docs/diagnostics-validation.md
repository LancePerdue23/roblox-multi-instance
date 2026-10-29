# Diagnostics acceptance checks

Run `./Test.ps1` and `./Build.ps1` with a supported .NET 10 SDK. If using an ignored repository-local SDK, pass `-Dotnet "$PWD/.build/sdk/dotnet.exe"` to each script. Default tests cover history limits and recovery, concurrent writers, read-only effective access, actionable failures, privacy canaries, bounded ZIP export, cancellation before worker startup, and synthetic dashboard rendering. Preview PNGs are written under `.build/`.

## Windows integration

In an administrator PowerShell session with official Roblox installed, run `./Test.ps1 -Integration`. This adds the real publisher check and controlled child-process handle closure. The latter must close exactly the fixture's target Event while preserving lookalikes and its same-name mutex. A non-elevated run can fail because the Windows handle table is restricted; do not treat that as a passing integration check.

## Installed dashboard and launch acceptance

Use the newly built executable's explicit Setup/Repair flow to update the installation, approving UAC with the same Windows account. Keep the existing installation executable/task names and paths.

1. Open Start menu Diagnostics without elevation. Confirm configuration results and source-specific unknown/failure states remain usable. Open or refresh it while checking Task Scheduler: no task starts solely for inspection, and no task definition or protocol registration changes.
2. Select Test worker. Confirm its timestamp/result, no game launch, and no handle scans from this probe. After the probe disconnects, the worker should exit after its normal idle period unless another launch is active. Refresh must invalidate the test result.
3. Confirm keyboard navigation, readable details, narrow-window wrapping and Windows display scaling. Export to a chosen local ZIP, inspect both entries, and confirm denied/missing logs produce omissions. Cancel export before saving and confirm no output file is created.
4. Explicitly run Repair and test worker startup again. Check successful repair and canceled UAC separately. A dashboard opened from a newer download must use that download's payload when repairing an older install. Repeat normal-context protocol verification through the existing setup flow.
5. Launch using desktop and both browser schemes. Successful launches must show no dashboard or success notification. Confirm recent outcomes after Refresh. Use two separate Roblox accounts and manually verify both remain in-game; process counts and singleton-release confirmation alone are insufficient.
6. Exercise an unavailable worker and a missing saved player on a disposable installation. Confirm existing warnings, actionable recent outcomes, Roblox left running after helper-only failure, and no automatic repair. Clear history and confirm raw logs/exported files remain. A concurrent finalizer must not recreate a cleared attempt.
7. Verify uninstall still restores owned protocol backups and removes only owned shortcuts/task/files. Retained user logs/history must remain local and be removable separately. No startup trigger, timer, service, tray watcher or permanent worker should remain.

Do not commit runtime history, diagnostic logs, reports, account data, or local assistant instructions. Passing default tests and synthetic previews does not replace the live acceptance above.
