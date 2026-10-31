# Roblox Multi Instance

The primary desktop app uses React/TypeScript/CSS in a WPF/WebView2 host with C# Windows services. Active source lives under src/desktop; shared services live under src/core and the compatible installed helper entrypoint lives under src/helper.

## Development

Use Windows x64, Node.js, and .NET 10. The scripts default to the local SDK at .build/sdk/dotnet.exe.

~~~powershell
./Test.ps1 -VerifyArchive
./Build.ps1
./Dev.ps1
~~~

Run .build/desktop/RobloxMultiInstance.exe with its entire output folder. Node.js is needed only to build. WebView2 Evergreen Runtime is required to run the desktop UI.

The desktop still requires the existing installed helper for launch and maintenance. Opening the desktop does not install the helper. Settings migrate from Preview into %LOCALAPPDATA%/RobloxMultiInstance when destination settings are absent. Originals remain intact. Shared helper history and installation identities retain their existing locations.

The frozen WinForms executable, source, verification report, and hashes are local under archive/legacy-winforms. Run VerifyArchive.ps1 to check integrity. The archive is ignored by Git and must be backed up separately if desired.

## Distribution preparation

Two installer editions are planned: desktop + helper, and helper only with terminal management. They are **not implemented or ready for release yet**. See [preparation status](docs/distribution-preparation.md) for remaining work. Build.ps1 currently produces the desktop folder, not installers.

Live Roblox, installation, repair, and uninstall require separate controlled acceptance checks. Nothing is automatically installed or published by development builds.
