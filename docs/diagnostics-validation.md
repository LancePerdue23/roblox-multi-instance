> Historical feature validation notes. Current desktop testing uses root Test.ps1 and scripts/testing/Audit.ps1; shared/helper checks use Test-Shared.ps1 (including -Integration when explicitly required). Retired UI instructions below are historical. See [milestone audit](milestone-audit.md).

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
5. Launch using desktop and both browser schemes with both launch-notification options off. Successful launches must show no dashboard, progress panel, success notification, or notification-host process. Confirm recent outcomes after Refresh. Use two separate Roblox accounts and manually verify both remain in-game; process counts and singleton-release confirmation alone are insufficient.

6. Exercise an unavailable worker and a missing saved player on a disposable installation. Confirm existing warnings, actionable recent outcomes, Roblox left running after helper-only failure, and no automatic repair. Clear history and confirm raw logs/exported files remain. A concurrent finalizer must not recreate a cleared attempt.
7. Verify uninstall still restores owned protocol backups and removes only owned shortcuts/task/files. Retained user logs/history must remain local and be removable separately. No startup trigger, timer, service, tray watcher or permanent worker should remain.

## Optional launch feedback acceptance

Use Diagnostics → Launch notifications… from a normal Windows token. Check all four combinations of progress/completion settings, confirming that both start off, save across reopening, survive Repair, and do not enable tray mode. Corrupt or unsupported settings must disable optional feedback and require an explicit reset. Do not put runtime settings in Git.

Launch from the desktop and both browser schemes, with Diagnostics/tray closed and open. Progress must appear only for attempts lasting at least 750 ms and must not take foreground focus, move a Roblox window, play sound, or request UAC. Check separate “Starting Roblox,” “player started,” “singleton release confirmed,” and “completion unconfirmed” wording. No output may claim sign-in, account identity, or joining a game. The helper may finish while a client is still loading or after it exits.

Overlap several launches, including worker-unavailable, timeout, failed-start, and successful cases where feasible. Confirm one progress panel, independent per-attempt results, mixed counts, two-second batching, at most one popup per ten seconds, and a final summary even if other attempts remain active. Dismiss progress mid-group; new attempts in that group must stay dismissed, while a later group can display again. Warnings remain available with optional feedback off. Turn both options off mid-launch: the existing host must still receive the final state without reporting a false interruption.

Check warning fallback when the coordinator is unavailable and confirm there is no second Program.Main dialog after acknowledged delivery. Multiple simultaneous fallback dialogs are a documented degraded case. Interrupt a launcher and verify completion unknown rather than success, without replaying old history as fresh notifications. Clear or deny history during a launch; live feedback and Roblox startup must continue independently.

Enable Windows notification suppression/Do Not Disturb. Confirm no repeated completion-notification spam; Diagnostics still records the outcome where history is available. Click a completion notification or the warning panel's Diagnostics button and confirm it opens Diagnostics without replaying a URI. Verify separate user/session isolation, bounded host exit after summaries (ten-second grace), and the worker's existing idle exit. Normal-token IPC acceptance and synthetic rendering are automated; live gameplay, OS suppression, installed startup races, and foreground behavior still require this manual check.

Do not commit runtime history, diagnostic logs, reports, account data, or local assistant instructions. Passing default tests and synthetic previews does not replace the live acceptance above.
