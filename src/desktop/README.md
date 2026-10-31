# Roblox Multi Instance desktop

Build and test through root Build.ps1 and Test.ps1. Output: .build/desktop/RobloxMultiInstance.exe. Keep the complete folder together; WebView2 Evergreen Runtime is required.

A matching installed helper is required for launch and maintenance. The desktop installer installs both together. Portable builds offer explicit helper setup through System → Health. Opening the desktop never installs it. Modern-edition uninstall removes the owned desktop/helper package and retains settings/history unless explicitly requested otherwise.

Preferences live under %LOCALAPPDATA%/RobloxMultiInstance. Valid missing preferences are copied from RobloxMultiInstancePreview at first normal startup; source settings remain intact. Existing destination settings always win. Invalid Preview files produce a startup notice and are never copied. Explicit reset restores defaults. Existing old-app import remains available.

See docs/parity.md for pending live acceptance and ../../docs/distribution-preparation.md for packaging and pending manual acceptance.
