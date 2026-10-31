using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxLaunchHelper;

internal interface IPlacementSession : IDisposable
{
    bool Valid { get; }
    bool Normal { get; }
    LayoutWindow Read();
    WindowedResult RequestWindowed(CancellationToken token);
    bool Restore();
    bool Move(Rectangle bounds);
    Rectangle Bounds();
}
internal interface IPlacementPlatform
{
    LayoutMonitor[] Monitors();
    IPlacementSession Open(InstanceTarget target);
}
internal sealed record PlacementResult(InstanceTarget Target, string Message);
internal sealed class PlacementCoordinator(IPlacementPlatform platform)
{
    internal static bool SameDisplays(LayoutMonitor[] a, LayoutMonitor[] b) => a.SequenceEqual(b);
    internal async Task<PlacementResult[]> ApplyAsync(PlacementPlan plan, CancellationToken token)
    {
        if (!plan.Valid) return [];
        var sessions = new List<(WindowPlacement Placement, IPlacementSession Session)>();
        var results = new List<PlacementResult>();
        try
        {
            token.ThrowIfCancellationRequested();
            if (!SameDisplays(plan.Monitors, platform.Monitors())) throw new InvalidOperationException("Displays changed. Preview again.");
            // All lifetimes are preflighted before the first side effect. Hold handles throughout.
            foreach (var placement in plan.Placements)
            {
                token.ThrowIfCancellationRequested();
                var session = platform.Open(placement.Target);
                sessions.Add((placement, session));
                token.ThrowIfCancellationRequested();
                if (!Fits(session, placement, plan.Monitors)) throw new InvalidOperationException("A window changed or cannot fit. Preview again.");
            }
            foreach (var (placement, session) in sessions)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (!SameDisplays(plan.Monitors, platform.Monitors()))
                    { results.Add(new(placement.Target, "Skipped: displays changed. Preview again.")); continue; }
                    if (!Fits(session, placement, plan.Monitors))
                    { results.Add(new(placement.Target, "Skipped: window exited, changed, or became unavailable.")); continue; }
                    token.ThrowIfCancellationRequested();
                    // A minimized fullscreen client must be restored before its mode command.
                    if (!session.Normal)
                    {
                        if (!session.Restore()) { results.Add(new(placement.Target, "Restore request failed.")); continue; }
                        for (int i = 0; i < 5 && session.Valid && !session.Normal; i++)
                            await Task.Delay(200, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (!session.Valid || !session.Normal || !SameDisplays(plan.Monitors, platform.Monitors()))
                        { results.Add(new(placement.Target, "Restore was not confirmed or displays changed; no mode or move request was sent.")); continue; }
                    }
                    if (session.Read().NeedsWindowed)
                    {
                        token.ThrowIfCancellationRequested();
                        var preparation = session.RequestWindowed(token);
                        if (preparation != WindowedResult.Ready)
                        {
                            string reason = preparation switch
                            {
                                WindowedResult.FocusDenied => "Windows denied focus. Click Apply from the manager and try again.",
                                WindowedResult.KeysHeld => "Release modifier keys and F11 before clicking Apply again.",
                                WindowedResult.Interrupted => "Focus changed; fullscreen preparation stopped to avoid sending input to another window.",
                                WindowedResult.Changed => "The client exited or changed during fullscreen preparation.",
                                WindowedResult.TimedOut => "Roblox did not confirm windowed mode after F11.",
                                _ => "Windowed-mode request failed: keyboard input was blocked."
                            };
                            results.Add(new(placement.Target, reason + " No move was sent to this client.")); continue;
                        }
                        bool windowed = false;
                        for (int i = 0; i < 10; i++)
                        {
                            await Task.Delay(200, token).ConfigureAwait(false);
                            if (!session.Valid || !SameDisplays(plan.Monitors, platform.Monitors())) break;
                            if (!session.Read().NeedsWindowed) { windowed = true; break; }
                        }
                        if (!windowed)
                        { results.Add(new(placement.Target, "Roblox did not confirm windowed mode; no move was sent. Other windows will continue.")); continue; }
                        if (!Fits(session, placement, plan.Monitors))
                        { results.Add(new(placement.Target, "Windowed mode changed the required size or window identity. Preview again; no move was sent.")); continue; }
                    }
                    token.ThrowIfCancellationRequested();
                    if (!session.Restore()) { results.Add(new(placement.Target, "Restore request failed.")); continue; }
                    // Restoration and positioning are separate asynchronous requests. Never resend a move.
                    for (int i = 0; i < 5 && session.Valid && !session.Normal; i++)
                        await Task.Delay(200, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    bool sent = session.Valid && session.Normal && SameDisplays(plan.Monitors, platform.Monitors()) && session.Move(placement.Bounds);
                    bool matched = false;
                    for (int i = 0; sent && i < 5; i++)
                    {
                        await Task.Delay(200, token).ConfigureAwait(false);
                        if (!session.Valid || !SameDisplays(plan.Monitors, platform.Monitors())) break;
                        if (session.Bounds() == placement.Bounds) { matched = true; break; }
                    }
                    results.Add(new(placement.Target, matched ? "Placed." : "Placement not confirmed; the client may be busy or enforcing its own size."));
                }
                catch (OperationCanceledException) { throw; }
                catch { results.Add(new(placement.Target, "Skipped: window changed or access was denied.")); }
            }
            return results.ToArray();
        }
        finally { foreach (var item in sessions) item.Session.Dispose(); }
    }
    private static bool Fits(IPlacementSession session, WindowPlacement placement, LayoutMonitor[] monitors)
    {
        if (!session.Valid) return false;
        var window = session.Read();
        var monitor = monitors.FirstOrDefault(m => m.Work.Contains(placement.Bounds));
        if (!window.Movable || monitor is null || !session.Valid) return false;
        var minimum = LayoutPlanner.Minimum(window, monitor);
        return placement.Bounds.Width >= minimum.Width && placement.Bounds.Height >= minimum.Height;
    }
}

internal sealed class PlacementPlatform : IPlacementPlatform
{
    public LayoutMonitor[] Monitors() => PlacementNative.Monitors();
    public IPlacementSession Open(InstanceTarget target) => new PlacementSession(target);
    internal static InstanceSnapshot Discover(CancellationToken token)
    {
        var platform = new InstancePlatform();
        using var current = Process.GetCurrentProcess();
        var rows = new List<ActiveInstance>(); int unavailable = 0;
        foreach (int pid in platform.Candidates())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var process = platform.Open(pid);
                var id = process.Identity;
                if (!InstancePolicy.Matches(id, Program.Sid, current.SessionId)) continue;
                if (!platform.Trusted(id.Image)) { unavailable++; continue; }
                token.ThrowIfCancellationRequested();
                var windows = process.Windows();
                token.ThrowIfCancellationRequested();
                if (process.IsAlive) rows.Add(new(new(pid, id.Created), windows, null, null, false));
            }
            catch (OperationCanceledException) { throw; }
            catch { unavailable++; }
        }
        return new(rows.OrderBy(r => r.Key.Created).ThenBy(r => r.Key.Id).ToArray(), unavailable);
    }
}

internal sealed class PlacementSession : IPlacementSession
{
    private readonly IInstanceProcess process;
    private readonly InstanceTarget target;
    public bool Valid => process.IsAlive && process.Identity.Created == target.Key.Created && process.OwnsWindow(target.Window);
    public bool Normal => !PlacementNative.IsIconic(target.Window) && !PlacementNative.IsZoomed(target.Window);
    internal PlacementSession(InstanceTarget target)
    {
        this.target = target;
        process = new InstancePlatform().Open(target.Key.Id);
        try
        {
            using var current = Process.GetCurrentProcess();
            if (!InstancePolicy.Matches(process.Identity, Program.Sid, current.SessionId) ||
                process.Identity.Created != target.Key.Created || !RobloxTrust.IsSignedRoblox(process.Identity.Image) || !Valid)
                throw new InvalidOperationException();
        }
        catch { process.Dispose(); throw; }
    }
    public LayoutWindow Read()
    {
        if (!Valid) throw new InvalidOperationException();
        return PlacementNative.Read(target);
    }
    public WindowedResult RequestWindowed(CancellationToken token) =>
        FullscreenPreparation.Request(target.Window, () => Valid, new NativeFullscreenInput(), token);
    public bool Restore() => Valid && (Normal || PlacementNative.ShowWindowAsync(target.Window, 4)); // SW_SHOWNOACTIVATE
    public bool Move(Rectangle bounds)
    {
        if (!Valid || PlacementNative.IsIconic(target.Window) || PlacementNative.IsZoomed(target.Window)) return false;
        if (!Valid) return false;
        return PlacementNative.SetWindowPos(target.Window, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            0x0010 | 0x0004 | 0x4000 | 0x0200); // NOACTIVATE | NOZORDER | ASYNCWINDOWPOS | NOOWNERZORDER
    }
    public Rectangle Bounds() => PlacementNative.Bounds(target.Window);
    public void Dispose() => process.Dispose();
}

internal static class PlacementNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect
    {
        internal int Left, Top, Right, Bottom;
        internal readonly Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct WindowPlacement
    {
        internal int Length, Flags, Show;
        internal Point Min, Max;
        internal Rect Normal;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        internal int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
        internal int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Key;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int kind, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRectExForDpi(ref Rect rect, uint style, bool menu, uint extended, uint dpi);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    internal static LayoutMonitor[] Monitors()
    {
        return Screen.AllScreens.Select(screen =>
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            // EDD_GET_DEVICE_INTERFACE_NAME returns a persistent monitor interface path.
            if (!EnumDisplayDevices(screen.DeviceName, 0, ref device, 1) || string.IsNullOrWhiteSpace(device.Id)) throw new Win32Exception();
            var center = new Point(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y + screen.Bounds.Height / 2);
            if (GetDpiForMonitor(MonitorFromPoint(center, 2), 0, out uint dpi, out _) != 0 || dpi == 0) throw new Win32Exception();
            return new LayoutMonitor(device.Id, screen.DeviceName + (screen.Primary ? " (primary)" : ""), screen.WorkingArea, dpi, screen.Primary);
        }).OrderByDescending(m => m.Primary).ThenBy(m => m.Work.X).ThenBy(m => m.Work.Y).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
    }
    internal static Rectangle Bounds(nint window) => GetWindowRect(window, out var rect) ? rect.Rectangle : throw new Win32Exception();
    internal static Size Frame(nint window, uint dpi, bool projectWindowed = false)
    {
        uint style = (uint)GetWindowLongPtr(window, -16).ToInt64(), ex = (uint)GetWindowLongPtr(window, -20).ToInt64();
        // Budget a standard resizable frame before asking Roblox to leave fullscreen.
        // This is only geometry; never modify the game's window styles.
        if (projectWindowed) style = (style & ~0x80000000u) | 0x00cf0000u;
        var r = new Rect();
        if (!AdjustWindowRectExForDpi(ref r, style, false, ex, dpi)) throw new Win32Exception();
        return r.Rectangle.Size;
    }
    internal static bool Fullscreen(nint window)
    {
        long style = GetWindowLongPtr(window, -16).ToInt64();
        if ((style & 0x00c00000) != 0) return false;
        var screen = Screen.FromHandle(window);
        var bounds = Bounds(window);
        if (IsIconic(window))
        {
            var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (!GetWindowPlacement(window, ref placement)) throw new Win32Exception();
            bounds = placement.Normal.Rectangle;
            bounds.Offset(screen.WorkingArea.X - screen.Bounds.X, screen.WorkingArea.Y - screen.Bounds.Y);
        }
        return bounds.Contains(screen.Bounds);
    }
    internal static LayoutWindow Read(InstanceTarget target)
    {
        var b = Bounds(target.Window);
        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(target.Window, ref placement)) throw new Win32Exception();
        var screen = Screen.FromHandle(target.Window);
        if (IsIconic(target.Window) || IsZoomed(target.Window))
        {
            // WINDOWPLACEMENT normal coordinates use workspace coordinates for non-tool windows.
            b = placement.Normal.Rectangle;
            b.Offset(screen.WorkingArea.X - screen.Bounds.X, screen.WorkingArea.Y - screen.Bounds.Y);
        }
        long style = GetWindowLongPtr(target.Window, -16).ToInt64();
        bool fullscreen = Fullscreen(target.Window);
        string? reason = IsHungAppWindow(target.Window) ? "Window busy" : !fullscreen && (style & 0x00040000) == 0 ? "Not resizable" : null;
        uint dpi = GetDpiForWindow(target.Window);
        return new(target, "", b, Frame(target.Window, dpi == 0 ? 96 : dpi, fullscreen), reason is null, reason, dpi == 0 ? 96 : dpi,
            Monitors().Select(m => m.Dpi).Distinct().ToDictionary(d => d, d => Frame(target.Window, d, fullscreen)), fullscreen);
    }
}
