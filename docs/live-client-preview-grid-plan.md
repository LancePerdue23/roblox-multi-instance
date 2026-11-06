# Live client preview grid implementation plan (G18)

Status: experimental implementation, November 4, 2026. Native fixture and browser verification completed; live Roblox and hardware/performance release acceptance remain open. This expands M7 in [the feature parity plan](feature-parity-implementation-plan.md) and phase 5 of [the next gap closure plan](next-gap-closure-plan.md).

## Implemented scope and remaining acceptance

Open Instances → Grid → Enable experimental previews. Capture defaults off on each grid visit. Grid supports selection, Focus, 12-card pages, visible-card subscriptions, density, 1/2/5 FPS limits, pause and privacy hiding. Native capture uses Windows.Graphics.Capture, bounded one-buffer pools, sequential reads and JPEG encoding at up to 480 x 270. Source windows over 4096 x 2160 are refused; aggregate active capture dimensions are capped at 24 Mi pixels. Capacity/error cards can be retried by toggling previews off/on. Frame conversion currently copies the native surface to a software bitmap before scaling during encoding; GPU scaling remains a performance optimization to evaluate.

The host validates opaque instance IDs through canonical process lifetime/user/session/signer/window checks, retains native-only leases, and rechecks ownership before publishing frames. Images use a dedicated `https://previews.desktop.local` host intercepted in memory, with subscription tokens, trusted renderer/referrer checks and no-store responses. A separate image host is necessary because WebView2 bypasses resource interception for mapped asset hosts. Nothing is served over a listening network socket. The CSP permits this image host only for images. Generated DTOs carry state and opaque image URLs; native handles stay outside the bridge.

Capture stops on view cleanup, hidden/minimized host, session lock/disconnect, missing renderer heartbeat, navigation and disposal. Privacy removes pixels and names immediately from the grid and revokes native frames. This is a grid-specific privacy control, not a global streamer mode. Reparenting/input forwarding and the separate presentation/streaming view remain out of scope. Desktop SDK targeting now includes Windows 10 capture APIs; minimum declared Windows version is 1903.

Verification: root Test.ps1 passed with preview lifetime checks, backend and feature checks, 14 frontend unit tests and 37 browser workflows. Root Build.ps1 published the local desktop. An explicitly invoked native fixture captured a harmless WPF window, verified bounded JPEG dimensions, resizing and closure, and exercised the production image route in WebView2 under CSP, including revoked-token refusal. Fixture artifacts are local under `.build/preview-fixture.jpg` and `.build/preview-transport.png`. Normal automated tests do not launch capture windows or Roblox; use `dotnet run --project src/desktop/preview-tests/Preview.Tests.csproj -c Release -- --capture` with the local SDK for native acceptance.

Still required before default enablement: live Roblox source mapping/Focus/minimize acceptance, GPU/device-loss and mixed-DPI/multi-monitor/session-lock hardware checks, and the 1/6/12-source performance matrix plus 30-minute resource soak described below. No measured capacity, universal GPU compatibility or low-overhead claim is made. Keep G18 Partial and capture opt-in until those gates are recorded. The remaining sections retain the design and acceptance checklist.

## Intended experience

Add a Grid/Table switch to Instances. Grid displays live Roblox window previews together inside the desktop app, matching the monitoring workflow visible in the user-supplied RoALT screenshots. Each card has a stable client label, verified account name when available, capture status, and Focus action. Focus uses the existing validated native action to bring the real game window forward. Preserve selection and filters across views. Account launch parity is not required to preview already-running, verified clients; unverified account associations must remain unnamed.

This first release is a monitoring grid. Reparenting game windows, forwarding keyboard/mouse input, synchronized inputs, recording, audio capture and streaming are outside its scope. Screenshots do not demonstrate that RoALT supports direct gameplay inside its tiles.

## 1. Prove the capture path

Build a disposable local fixture spike using Windows.Graphics.Capture targeting a verified HWND. Microsoft's [CreateForWindow documentation](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow) specifies Windows 10 version 1903 as the minimum for this interop API. Detect capability at runtime and retain Table when unavailable; review compatibility with the project's existing target before changing SDK dependencies.

Validate capture, resize, occlusion, minimize/restore, source closure, mixed DPI and GPU/device loss in a fixture window first. Microsoft's [Win32 capture sample](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/blob/master/cpp/ScreenCaptureforHWND/README.md) explicitly excludes minimized windows from capture. Do not promise fresh minimized previews or restore games automatically. Later live Roblox checks require a controlled manual session, not test-driven launches.

Compare a bounded downscaled image path into WebView2 with a native composition prototype. Prefer the image path for normal React card layout if measured CPU, GPU, allocations and latency pass phase 5. Do not commit to native overlays before checking WebView2 layering, scrolling, clipping, DPI and accessibility. Exit: choose and document a working transport and measured concurrency limit; if neither is acceptable, retain the feature gate and document the blocker.

## 2. Native capture service and identity

- Keep capture ownership in `src/desktop/host`, alongside WPF/WebView2 lifetime management. Use the backend's canonical instance validation and `src/core/ActiveInstanceNative.cs` ownership checks; extract a narrow internal resolver if needed rather than duplicating trust policy.
- Resolve opaque instance IDs to process creation identity and an eligible owned window immediately before starting capture. Native handles and arbitrary process IDs/paths must never be accepted from the renderer. Revalidate on source replacement; discard frames from a previous generation after close, process exit or handle reuse.
- Use one shared scheduler, bounded sessions and at most one pending frame per client. Downscale before encoding, replace old frames, and never accumulate a backlog. Dispose frame pools, textures, callbacks and subscriptions on every exit path.
- Represent starting, live, paused, minimized, stale, unavailable and closed explicitly. Timestamp the last received frame; an unchanged game image alone is not evidence of capture failure. Clear retained frames on privacy changes, source invalidation and session disposal.

## 3. Bridge and delivery

Extend `Contracts.cs`, `IDesktopBackend.cs`, backend routing and the host bridge allowlist only as needed. Proposed operations: subscribe/update visible IDs and rate, pause, and dispose. Issue an opaque subscription ID scoped to the current trusted renderer lifetime. Bounds apply to IDs, dimensions, rates, sessions and message sizes. Stop subscriptions on navigation, reload or host disposal.

Keep control/status messages on the existing typed bridge. For the initial image prototype, use a dedicated host-owned WebView2 resource route with opaque subscription/generation tokens and no disk files. Accept requests only from the trusted app origin and active renderer session, validate exact tokens and deny arbitrary paths. Keep just the latest bounded encoded frame per source, return no-store responses, and revoke tokens on disposal. Do not send full-resolution base64 frames through general command responses. The spike must validate route isolation and throughput before this becomes the production transport.

Regenerate `web/src/backend.generated.ts` using `src/desktop/Contracts.ps1 -Write` after contract changes. Include stale-generation, size-limit and unauthorized-origin cases in host tests.

## 4. React grid and lifecycle

Extract the grid into a dedicated component rather than expanding `main.tsx` indefinitely. Reuse instance identity, selection, filtering and Focus behavior. Show responsive cards with aspect-preserving images, density control and an accessible label/status/action area. Keep DOM ordering stable while metrics update; keyboard users must be able to reach every card and action.

Provisional defaults to validate: 2 frames/second, maximum 480 x 270 output, and 12 concurrently active visible cards; allow 1/2/5 FPS within the shared budget. These are starting limits, not measured capacity claims. Virtualize or paginate larger lists. Cards outside the active budget show an explicit paused placeholder; a large client count must not silently create hundreds of capture sessions.

Capture only while Grid is visible and the host is visible. Stop native sessions when switching to Table, navigating away, hiding/minimizing to tray, locking, or enabling privacy mode. Resume only the current visible eligible set. Cancel pending frame requests and revoke old URLs during transitions. Show a labeled placeholder for minimized/unsupported clients and a last-updated indicator for stalled delivery. Do not change game window geometry or rendering settings to obtain a preview.

Privacy mode must hide captured game pixels as well as account labels. Keep frames in memory only, exclude them from diagnostics/telemetry, and clear React references and native buffers when privacy is enabled. Saved preferences contain view/density/rate only, never frame content or native identities.

## 5. Verification and release gate

1. Service tests with fake sources: recycled handles/processes, stale callbacks, bounded scheduling, cancellation, closed sources, privacy clearing and repeated disposal. Backend/host tests must prove renderer input cannot select arbitrary windows or access another subscription.
2. Web fixtures: empty and large lists, filter/selection continuity, keyboard navigation, high contrast, mixed aspect ratios, unavailable/minimized/stale states, privacy changes and repeated Grid/Table transitions. Frames should have deterministic fixture images; no real Roblox required.
3. Native fixture acceptance: Windows versions supported by the app, capability refusal, occlusion, minimize/restore, mixed DPI, monitor changes, session lock, device loss, host reload and tray transitions. Confirm no sessions remain when hidden and no unintended focus/window movement occurs.
4. Measure manager process-tree CPU, private memory, GPU usage and frame latency with capture off and with 1/6/12 visible sources at each rate. Include a list much larger than the active cap, plus at least 30 minutes of scroll/filter/view switching. Record machine/OS/GPU, source dimensions and failures; require bounded queues, no continuing hidden capture and no sustained resource growth. Set numerical performance budgets from this baseline before enabling by default; reduce caps if necessary.
5. Controlled live Roblox acceptance: verify window ownership, correct source-to-card mapping, Focus, source exit/restart and normal/occluded/minimized behavior without credentials or captured pixels entering logs. Do not infer support from fixture success alone.

Run the relevant desktop tests via root `Test.ps1` and build via `Build.ps1` using the project's local SDK. If canonical shared services change, also run `Test-Shared.ps1`. Follow existing fixture conventions; never launch/stop real Roblox just to run automated tests.

Deliver in order: capture/transport spike with measurements; bounded native service and contracts; accessible grid and lifecycle; fixture and live acceptance evidence. Ship behind a feature flag until identity, privacy, lifecycle and resource gates pass. Leave G18 marked Gap until implementation exists, then Partial until live acceptance is recorded.
