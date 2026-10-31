# Contributing

Work in the active GitHub checkout. Use Windows x64, Node.js, and .NET 10 selected by global.json. Default commands are ./Test.ps1 and ./Build.ps1; ./Dev.ps1 builds and opens the desktop app.

Frontend changes live in src/desktop/web. Hosting code lives in src/desktop/host; the desktop facade lives in src/desktop/backend. Shared compatibility services are in src/core, referenced by both the facade and src/helper. Do not create another copy of these services.

Use ./Test-Shared.ps1 -Dotnet ./.build/sdk/dotnet.exe when changing installed-helper behavior. This retains the existing regression suite; some tests exercise temporary registry keys and filesystem ACLs and require execution outside the Codex sandbox. Integration tests remain explicitly opt-in.

Run ./VerifyArchive.ps1 after changes touching archival handling. Never regenerate the frozen archive as part of a normal build. Preserve .build/migration-baseline/20261030-211138 and work/update-aware-recovery.

Keep binaries, runtime data, credentials, downloaded tools, and local configuration out of commits. Do not stage, commit, push, publish, or install as part of routine development. Avoid concurrent edits/builds in this checkout.

See docs/distribution-preparation.md for unfinished distribution work. Historical migration records describe the earlier comparison build and are not current instructions.

Occasional current-build persona, native-host, stress and opt-in live audits: see [milestone audit](docs/milestone-audit.md). Retired UI tests are manual-only through Test-ArchivedUI.ps1.

Distribution builds use `Package.ps1`; read-only package checks use `Test-Distribution.ps1`. Read `docs/distribution-preparation.md` before changing setup, editions, terminal commands, or update handoff. Shared core changes must keep its assembly free of WinForms/WPF references.
