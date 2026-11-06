# Desktop startup resilience

The native host gives each explicit startup attempt twenty seconds to initialize
the browser, navigate to bundled desktop.local content, and receive the React
host.ready message. The browser remains hidden while a native loading surface is shown until
that handshake; this avoids relying on WPF overlays above a native browser HWND. Failure, navigation errors or a stopped browser process show
native retry/close actions; missing runtime keeps the Microsoft download remedy.
There are at most three attempts per process and no automatic retry. Late
callbacks from earlier attempts cannot complete or fail their replacements.
Retry replaces only the startup WebView, retaining backend lifetime, profiles and
one update-shutdown owner; shortcut ownership is disposed before replacement.

The failure surface offers an explicit restart with software rendering.
--safe-rendering selects WPF SoftwareOnly and requests Chromium --disable-gpu
before creating the WebView2 environment. Restart occurs after releasing the
single-instance mutex. It uses the separate desktop WebView2-safe-rendering
profile so a surviving normal browser process cannot reuse fixed incompatible
environment options. Account sign-in profiles, native settings and the normal
desktop profile are preserved. No profile deletion, CSP weakening, privileged
script injection or repair of existing Roblox processes occurs.

This is a requested rendering preference, not proof of effective graphics
behavior. Administrator/runtime options can override browser settings. A normal
subsequent app launch uses normal rendering; no persistent auto-recovery loop or
unattended restart is installed. Fixtures cover the timeout/retry/cancellation
policy. Actual GPU-failure recovery requires a controlled native UI acceptance
run; no app or game launch was used for these fixtures.

Official API references checked during implementation:

- [WebView2 EnsureCoreWebView2Async](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.wpf.webview2.ensurecorewebview2async): repeated initialization with the same environment reuses the original task; changing environment after initialization is not a restart mechanism.
- [WebView2 environment creation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environment.createasync): shared-browser option mismatches fail and environment/registry configuration can override options.
- [AdditionalBrowserArguments](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environmentoptions.additionalbrowserarguments): options are passed when the browser environment starts.
- [WPF ProcessRenderMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.processrendermode): process-level software rendering is a preference with documented precedence.

- [WebView2 in WPF apps](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf): the standard control uses HwndHost and can obscure ordinary WPF overlays. The startup control stays hidden until readiness instead.
