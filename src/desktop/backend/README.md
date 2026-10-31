# Desktop service facade

DesktopBackend owns UI-facing DTOs and operations. It references the canonical shared implementation in src/core; do not restore a Legacy source copy here. The core currently retains WinForms compatibility types pending separation of terminal and desktop presentation.

Contracts.cs generates web/src/backend.generated.ts. Run src/desktop/Contracts.ps1 -Write to regenerate and review changes. Native handles never cross the bridge. Tests use injected preference directories and do not perform live Roblox operations.
