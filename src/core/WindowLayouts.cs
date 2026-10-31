using System.Text.Json;

namespace RobloxLaunchHelper;

internal enum ArrangementKind { SideBySide, Grid, Saved, Recover }
internal sealed record LayoutMonitor(string Id, string Label, Rectangle Work, uint Dpi, bool Primary);
internal sealed record LayoutWindow(InstanceTarget Target, string Caption, Rectangle NormalBounds,
    Size Frame, bool Movable, string? Reason = null, uint FrameDpi = 96, IReadOnlyDictionary<uint, Size>? Frames = null,
    bool NeedsWindowed = false);
internal sealed record LayoutSlot(string Monitor, double X, double Y, double Width, double Height);
internal sealed record WindowPreset(string Name, LayoutSlot[] Slots);
internal sealed record WindowPlacement(InstanceTarget Target, Rectangle Bounds, int Slot = 0);
internal sealed record PlacementPlan(LayoutMonitor[] Monitors, WindowPlacement[] Placements, string[] Notes, string? Error = null)
{
    internal bool Valid => Error is null && Placements.Length > 0;
}
internal sealed record LayoutSettings(int Version, WindowPreset[] Presets, Dictionary<string, int> Shortcuts)
{
    internal static LayoutSettings Default => new(1, [], new()
    {
        ["Manager"] = 393293,
        ["SideBySide"] = 393265,
        ["Grid"] = 393266,
        ["Saved"] = 393298,
        ["Recover"] = 393295
    });
}

// Geometry is pure, in physical desktop pixels. Only saved slots use logical units.
internal static class LayoutPlanner
{
    internal static int Scale(double value, uint dpi) => checked((int)Math.Round(value * dpi / 96d, MidpointRounding.AwayFromZero));
    internal static Size Minimum(LayoutWindow window, LayoutMonitor monitor)
    {
        var frame = window.Frames?.GetValueOrDefault(monitor.Dpi) ?? new Size(
            Scale(window.Frame.Width * 96d / window.FrameDpi, monitor.Dpi), Scale(window.Frame.Height * 96d / window.FrameDpi, monitor.Dpi));
        return new(Scale(640, monitor.Dpi) + frame.Width, Scale(360, monitor.Dpi) + frame.Height);
    }
    internal static Rectangle[] Cells(LayoutMonitor monitor, int count, ArrangementKind kind)
    {
        if (count <= 0) return [];
        int columns = kind == ArrangementKind.SideBySide ? count : (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (count + columns - 1) / columns, gap = Scale(8, monitor.Dpi);
        int width = monitor.Work.Width - gap * (columns + 1), height = monitor.Work.Height - gap * (rows + 1);
        if (width <= 0 || height <= 0) return [];
        return Enumerable.Range(0, count).Select(i =>
        {
            int x = i % columns, y = i / columns;
            return new Rectangle(monitor.Work.X + gap * (x + 1) + x * (width / columns) + Math.Min(x, width % columns),
                monitor.Work.Y + gap * (y + 1) + y * (height / rows) + Math.Min(y, height % rows),
                width / columns + (x < width % columns ? 1 : 0), height / rows + (y < height % rows ? 1 : 0));
        }).ToArray();
    }
    internal static int Capacity(LayoutMonitor monitor, LayoutWindow[] windows, ArrangementKind kind)
    {
        if (windows.Length == 0) return 0;
        Size min = new(windows.Max(w => Minimum(w, monitor).Width), windows.Max(w => Minimum(w, monitor).Height));
        // Bounded by the minimum area, not by an arbitrary maximum client count.
        long bound = Math.Min(10000, (long)monitor.Work.Width * monitor.Work.Height / Math.Max(1L, (long)min.Width * min.Height));
        int maximum = 0;
        for (int n = 1; n <= bound; n++)
            if (Cells(monitor, n, kind).All(r => r.Width >= min.Width && r.Height >= min.Height)) maximum = n;
        return maximum;
    }
    internal static PlacementPlan Arrange(LayoutMonitor[] monitors, LayoutWindow[] windows, string[] assignments, ArrangementKind kind)
    {
        var placements = new List<WindowPlacement>();
        if (windows.Length != assignments.Length) return new(monitors, [], [], "Window assignments changed. Preview again.");
        foreach (var group in windows.Select((w, i) => (Window: w, Monitor: assignments[i])).GroupBy(x => x.Monitor))
        {
            var monitor = monitors.FirstOrDefault(m => m.Id == group.Key);
            if (monitor is null) return new(monitors, [], [], "A selected monitor disconnected. Preview again.");
            var items = group.Select(x => x.Window).ToArray();
            var cells = Cells(monitor, items.Length, kind);
            for (int i = 0; i < items.Length; i++)
            {
                var min = Minimum(items[i], monitor);
                if (cells.Length != items.Length || cells[i].Width < min.Width || cells[i].Height < min.Height)
                    return new(monitors, [], [], $"{monitor.Label}: minimum client area is 640×360 logical pixels. Maximum fitting count: {Capacity(monitor, items, kind)}. Select fewer windows or change monitor assignments.");
                placements.Add(new(items[i].Target, cells[i]));
            }
        }
        return new(monitors, windows.Select((w, i) => placements.Single(p => p.Target == w.Target) with { Slot = i + 1 }).ToArray(), []);
    }
    internal static LayoutSlot Save(LayoutWindow window, LayoutMonitor[] monitors)
    {
        var monitor = monitors.OrderByDescending(m => Area(Rectangle.Intersect(m.Work, window.NormalBounds))).First();
        var b = window.NormalBounds;
        return new(monitor.Id, (b.X - monitor.Work.X) * 96d / monitor.Dpi, (b.Y - monitor.Work.Y) * 96d / monitor.Dpi,
            b.Width * 96d / monitor.Dpi, b.Height * 96d / monitor.Dpi);
    }
    internal static PlacementPlan Restore(LayoutMonitor[] monitors, LayoutWindow[] windows, WindowPreset preset)
    {
        if (windows.Length != preset.Slots.Length) return new(monitors, [], [],
            $"This preset has {preset.Slots.Length} slots. Check exactly that many windows and order them to assign the slots.");
        var primary = monitors.First(m => m.Primary);
        var placements = new List<WindowPlacement>();
        var missing = new List<LayoutWindow>();
        for (int i = 0; i < windows.Length; i++)
        {
            var slot = preset.Slots[i];
            var monitor = monitors.FirstOrDefault(m => m.Id == slot.Monitor);
            if (monitor is null) { missing.Add(windows[i]); continue; }
            var result = Clamp(windows[i], monitor, new(monitor.Work.X + Scale(slot.X, monitor.Dpi),
                monitor.Work.Y + Scale(slot.Y, monitor.Dpi), Scale(slot.Width, monitor.Dpi), Scale(slot.Height, monitor.Dpi)));
            if (result is not Rectangle bounds) return new(monitors, [], [], $"{monitor.Label}: saved slot cannot fit the minimum client size.");
            placements.Add(new(windows[i].Target, bounds));
        }
        if (missing.Count > 0)
        {
            var fallback = Arrange(monitors, missing.ToArray(), missing.Select(_ => primary.Id).ToArray(), ArrangementKind.Grid);
            if (fallback.Error is not null) return fallback;
            placements.AddRange(fallback.Placements);
        }
        return new(monitors, windows.Select((w, i) => placements.Single(p => p.Target == w.Target) with { Slot = i + 1 }).ToArray(), missing.Count == 0 ? [] :
            ["A saved monitor is disconnected. Its slots will be arranged as a grid on the primary monitor; they may overlap other saved slots. The saved preset is unchanged."]);
    }
    internal static PlacementPlan Recover(LayoutMonitor[] monitors, LayoutWindow[] windows)
    {
        var primary = monitors.First(m => m.Primary);
        var placements = new List<WindowPlacement>();
        foreach (var window in windows)
        {
            var b = window.NormalBounds;
            // A visible title-bar segment is enough; never move partially visible windows silently.
            var title = new Rectangle(b.X, b.Y, b.Width, Scale(32, window.FrameDpi));
            if (monitors.Any(m => Area(Rectangle.Intersect(m.Work, title)) > 0)) continue;
            var size = Clamp(window, primary, new(primary.Work.Location, b.Size));
            if (size is not Rectangle fit) return new(monitors, [], [], "Primary monitor cannot fit the minimum client size. Choose another monitor using Arrange.");
            int step = Scale(32, primary.Dpi), index = placements.Count;
            int x = (int)((long)index * step % Math.Max(1, primary.Work.Width - fit.Width + 1));
            int y = (int)((long)index * step % Math.Max(1, primary.Work.Height - fit.Height + 1));
            placements.Add(new(window.Target, new(primary.Work.X + x, primary.Work.Y + y, fit.Width, fit.Height), Array.IndexOf(windows, window) + 1));
        }
        return new(monitors, placements.ToArray(), placements.Count == 0 ? ["No off-screen windows need recovery."] : []);
    }
    private static Rectangle? Clamp(LayoutWindow window, LayoutMonitor monitor, Rectangle bounds)
    {
        var min = Minimum(window, monitor);
        if (min.Width > monitor.Work.Width || min.Height > monitor.Work.Height) return null;
        int width = Math.Clamp(bounds.Width, min.Width, monitor.Work.Width), height = Math.Clamp(bounds.Height, min.Height, monitor.Work.Height);
        return new Rectangle(Math.Clamp(bounds.X, monitor.Work.Left, monitor.Work.Right - width),
            Math.Clamp(bounds.Y, monitor.Work.Top, monitor.Work.Bottom - height), width, height);
    }
    private static long Area(Rectangle r) => Math.Max(0L, r.Width) * Math.Max(0L, r.Height);
}

internal sealed class LayoutStore(string? file = null)
{
    internal string FilePath { get; } = file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.Product, "window-layouts.json");
    internal LayoutSettings Load()
    {
        if (!File.Exists(FilePath)) return LayoutSettings.Default;
        if (new FileInfo(FilePath).Length > 1024 * 1024) throw new InvalidDataException();
        var settings = JsonSerializer.Deserialize<LayoutSettings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException();
        Validate(settings);
        return settings;
    }
    internal void Save(LayoutSettings settings)
    {
        Validate(settings);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Program.JsonOptions);
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void Validate(LayoutSettings settings)
    {
        if (settings.Version != 1 || settings.Presets is null || settings.Shortcuts is null || settings.Presets.Length > 100 ||
            settings.Presets.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Presets.Length)
            throw new InvalidDataException();
        foreach (var p in settings.Presets)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 80 || p.Name.Any(char.IsControl) || p.Slots is null || p.Slots.Length is < 1 or > 256) throw new InvalidDataException();
            foreach (var s in p.Slots)
                if (s is null || string.IsNullOrWhiteSpace(s.Monitor) || s.Monitor.Length > 1024 ||
                    !new[] { s.X, s.Y, s.Width, s.Height }.All(v => double.IsFinite(v) && Math.Abs(v) <= 1000000) || s.Width <= 0 || s.Height <= 0)
                    throw new InvalidDataException();
        }
        if (settings.Shortcuts.Count != LayoutSettings.Default.Shortcuts.Count ||
            settings.Shortcuts.Keys.Any(k => !LayoutSettings.Default.Shortcuts.ContainsKey(k)) ||
            settings.Shortcuts.Values.Any(k => !ShortcutPolicy.Valid(k)) ||
            settings.Shortcuts.Values.Distinct().Count() != settings.Shortcuts.Count) throw new InvalidDataException();
    }
}

internal static class ShortcutPolicy
{
    internal static string Label(string command) => command switch
    {
        "SideBySide" => "Side-by-side", "Saved" => "Restore preset", "Recover" => "Recover off-screen", _ => command
    };
    internal static bool Valid(int keys)
    {
        var code = keys & 65535;
        return (code is >= 65 and <= 90 or >= 48 and <= 57 or >= 112 and <= 122) &&
            (keys & 0x70000) != 0 && (keys & ~0x7ffff) == 0;
    }
}
