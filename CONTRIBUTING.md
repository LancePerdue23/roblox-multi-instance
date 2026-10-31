# Contributing

Use Windows x64 and the .NET 10 SDK. The SDK selection is defined in `global.json`.

```powershell
./Test.ps1
./Build.ps1
```

For daily development, run `./Dev.ps1` (or pass `-Dotnet ./.build/sdk/dotnet.exe` for the local SDK). It replaces `.build/dev/RobloxLaunchHelper.exe` after a successful build and retains the previous four versions in `.build/dev-backups`. Close that development app, including its tray process, before rebuilding. `Build.ps1` still produces the release output by default.

The default tests need no Roblox installation and do not launch Roblox or close its handles. They create and remove a uniquely named temporary test registry subtree. Build output and test reports remain ignored by Git.

For full integration coverage, run this command from an elevated PowerShell session with official Roblox installed:

```powershell
./Test.ps1 -Integration
```

Integration tests validate the installed Roblox signature and close a specifically named event in a controlled child process. Real browser/account behavior needs a manual check with two separate accounts; a process count alone is insufficient. Confirm both game windows remain open and the helper exits.

Keep changes focused. Preserve the existing executable name, task names, and installation paths unless an explicit migration is included. These names belong to installed users, independently of the repository name.

For the opt-in persona/workflow audit, see [the user simulation guide](docs/user-simulation.md). Its synthetic mode runs separately from regression tests; the live mode requires explicit authorization to launch and close test clients.

An optional Windows GitHub Actions workflow is provided at `docs/windows-build.yml.example`. To enable it, copy it to `.github/workflows/build.yml` using a GitHub credential permitted to manage workflows. It runs the default tests and publishes a local build on the runner; it does not install the helper or publish a release.

Do not commit runtime logs, installation backups, account identifiers, Windows profile paths, private signing keys, cookies, or authentication tickets. Use synthetic fixtures in tests and redact machine-specific details in bug reports. Before committing, review `git diff --cached` and `git status --short`.

For installer changes, verify setup, repair, normal-user task execution, real-context protocol registration, and uninstall restoration on Windows. Never substitute a check made inside an isolated registry view for a check made from the normal Windows context.

For recovery or browser integration changes, use a disposable Windows account with official Roblox installed. Check both protocols after disabling integration, confirm the desktop helper still works, and confirm Repair preserves the disabled preference. Update/reinstall Roblox, recover its new executable, re-enable integration, and uninstall. Also exercise canceled administrator approval, multiple discovered players, a missing player, and an interrupted installation. Add a synthetic unrelated registry value/subkey and change one protocol externally before restoration; those changes must survive. These manual installer/browser checks complement the temporary-registry transaction tests and the elevated controlled-handle integration check.

For diagnostic changes, preserve silent successful launches and the on-demand worker's idle exit. Dashboard refresh must remain read-only and must not start a worker. Keep persisted and exported data in typed allowlists: no arbitrary exception messages, launch links, saved commands, registry backups, account identifiers, or machine paths. Extend privacy canary tests whenever adding report fields or event types. The dashboard can be rendered with synthetic results by the default tests; preview PNGs stay under `.build/`. Live acceptance also needs normal-token dashboard/worker testing and the existing two-account launch check; synthetic rendering does not replace those checks.
