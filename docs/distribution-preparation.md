# Distribution preparation status

## Implemented foundation

- Frozen WinForms 1.1.0 build with source, 122/122 test report, and SHA-256 manifest in ignored archive/legacy-winforms.
- Primary desktop source under src/desktop and executable RobloxMultiInstance.exe.
- Root build/test/dev entrypoints target the desktop.
- Shared compatibility implementation under src/core, referenced by desktop backend and helper; duplicated backend Legacy source removed.
- Version 1.1.0, desktop preference migration with source preservation, and archive verification.

## Required before two installer outputs exist

1. Remove inactive WinForms workspace pages from the shared implementation; split presentation/notification/update UI away from launch, installer, worker, history, and diagnostics services. Preserve launch security and protocol restoration safeguards.
2. Implement rmi.exe terminal commands for launch, setup/repair/recovery/uninstall, status/diagnostics/worker tests, browser integration and applicable settings, history/report export, and manual updates. Support help, stdout/stderr, exit codes, Ctrl+C, confirmations/--yes, redirected input, and explicit trusted player selection.
3. Make helper-only launches windowless and on demand, without notifications, tray, hotkeys, automatic update notices, or resident cleanup. Preserve desktop behavior separately.
4. Implement a shared installer engine with graphical setup, explicit payload allowlists, protected edition/version metadata, normal-user verification, owned command entries and shortcuts, and safe file handoff. Legacy upgrades preserve backups. Switching modern editions requires uninstall first.
5. Implement uninstall from desktop and terminal with settings/history retained by default and explicit data removal.
6. Build RobloxMultiInstance-Setup.exe and RobloxLaunchHelper-Setup.exe from clean staging, with checksums and asset manifest under artifacts/. Neither installer exists yet.
7. Update asset selection/download verification and setup handoff by installed edition. Preserve explicit installation, graceful app closure, canceled-elevation retry, busy-file handling, and running Roblox games. Older clients must safely fall back to the release page.
8. Validate both packages and retain only the intended desktop build plus curated legacy archive. Obsolete generated outputs have deliberately not been removed while replacement installers remain incomplete.

## Acceptance before publishing

Automated: CLI errors/confirmations/cancellation, source isolation, archive integrity, package allowlists and matching helper versions, corrupted/missing edition assets, digest/size/version validation, cancellation/retry/busy-file handling.

Disposable Windows account: fresh install of both editions, upgrade from legacy, setup/repair/recovery with normal-user verification, protocol ownership conflicts and restoration, ambiguous/missing signed players, canceled administrator approval, uninstall from desktop and terminal, retained-data reinstall, and edition switching. Verify no background helper process remains idle. Separately test two accounts remaining in-game and real display/layout behavior.

Publication remains manual and unauthorized in this task. Do not enable automatic release publishing or claim production acceptance from fixtures.
