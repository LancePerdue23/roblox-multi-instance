# Roblox Multi Instance desktop

Build and test through root Build.ps1 and Test.ps1. Output: .build/desktop/RobloxMultiInstance.exe. Keep the complete folder together; WebView2 Evergreen Runtime is required.

The existing installed helper remains required for launch and maintenance. Opening the desktop never installs it. Helper Uninstall removes the shared installed helper; it is not currently a desktop-package uninstall action.

Preferences live under %LOCALAPPDATA%/RobloxMultiInstance. Valid missing preferences are copied from RobloxMultiInstancePreview at first normal startup; source settings remain intact. Existing destination settings always win. Invalid Preview files produce a startup notice and are never copied. Explicit reset restores defaults. Existing old-app import remains available.

See docs/parity.md for pending live acceptance and ../../docs/distribution-preparation.md for installer work still required.
