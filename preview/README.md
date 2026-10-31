# Roblox Multi Instance Preview

A portable comparison app using a WPF/WebView2 host, bundled React/TypeScript/CSS frontend, and C# Windows services. The current WinForms source and release stay in place.

## Build and run
From the active checkout, run `./preview/Build.ps1` (uses the local .NET 10 SDK when available), then open `.build/preview/RobloxMultiInstancePreview.exe`. Keep the entire portable output folder together. Node.js is needed to build, not to run. WebView2 Evergreen Runtime is required; missing-runtime guidance does not install it automatically.

The original helper must already be installed for launching and maintenance. Opening the preview never installs a helper or registers browser protocols.

Run `./preview/Test.ps1` for preview checks and `./preview/VerifyPreservation.ps1` to verify the original project and executable. Original tests remain `./Test.ps1 -Dotnet ./.build/sdk/dotnet.exe`.

## Compare safely
Appearance, saved layouts, shortcuts and browser profile live under `%LOCALAPPDATA%\RobloxMultiInstancePreview`. Import preferences explicitly in Settings; subsequent changes remain private.

Launch history, launch notifications, browser integration and installed-helper maintenance are shared. A helper update updates the original installed helper; it does not update this preview. Both interfaces control the same Roblox windows. Do not initiate competing arrangements from both interfaces at once. Only one app can own global shortcuts and automatic background cleanup.

Keep using the original shortcut to return to the old interface. To remove the preview, close its window and tray process, then delete only its portable output folder and, optionally, `%LOCALAPPDATA%\RobloxMultiInstancePreview`. Do not run the helper's Uninstall action to remove the preview: that action removes the shared installed helper. Shared changes explicitly made during comparison are not automatically undone.

## Development handoff
Read `docs/migration-ledger.md` and `docs/parity.md` for ownership, commands, validation and unfinished acceptance. Live Roblox, display-scaling and installer checks are separate from fixture tests. The preview is not promoted automatically.
