# Backend provenance and boundaries

Baseline archive: .build/migration-baseline/20261030-211138/ (local project date October 30, 2026). Snapshot source identity is recorded in the baseline source-hashes manifest and the per-file provenance manifest below.

`Legacy/` is a frozen compatibility snapshot of the current working-tree `src/*.cs`, captured after the original preservation archive. Original files remain untouched. `provenance.json` records source and copied-file SHA-256 hashes. This includes inactive WinForms types because the original static installation/diagnostic services share dependencies with that assembly. The preview facade does not construct any of the styled legacy pages or run its Main method. A future post-comparison extraction can remove that compatibility dependency without changing the original release.

Three snapshot files are deliberately adapted: Program.cs routes compatibility logging into the preview log directory; DiagnosticChecks.cs does not compare the separately versioned preview executable against the installed helper; BackgroundClientCleanup.cs exposes mutex ownership status to the preview. The actual elevated helper, installed paths, worker authentication, protocol identities, history locks, and notification paths keep their original identities.

`PreviewBackend` is the only UI-facing service entrypoint. Page-state DTOs in `Contracts.cs` are the source for generated TypeScript interfaces (`BackendContractEmitter.TypeScript()`). Native handles remain in the backend map and are never serialized to the frontend. Layout apply uses the original coordinator, including per-session preflight, topology checks, fullscreen preparation, and confirmation of requested moves. Plans are single-use for placement.

The constructor optionally accepts an isolated storage directory for tests. This affects private preview settings/cache only; shared history/notification operations remain shared and require explicit confirmations. Backend tests never invoke a confirmed shared mutation, launch, cleanup, installation, or network action.

Regression command (from repository root): `.\.build\sdk\dotnet.exe run --project preview\backend\Tests\Backend.Tests.csproj`. Atomic file replacement may need an unsandboxed test run; each test writes only its unique temporary directory and removes it afterward. No real Roblox clients are launched or stopped.

