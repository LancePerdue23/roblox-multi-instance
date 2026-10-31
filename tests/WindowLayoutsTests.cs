using RobloxLaunchHelper;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using LayoutSettings = RobloxLaunchHelper.LayoutSettings;

internal static class WindowLayoutsTests
{
    private static readonly long Created = DateTime.UtcNow.ToFileTimeUtc();
    private static LayoutMonitor Monitor => new("monitor-interface-primary", "Primary", new(0, 0, 1920, 1040), 96, true);
    private static LayoutWindow Window(int number) => new(new(new(number, Created), number), "synthetic-private-caption", new(20, 20, 800, 500), new(16, 39), true);
    private static void Check(bool value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    { if (!value) throw new Exception($"Window layout assertion failed at line {line}."); }
    private static void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected failure"); }
    internal static void Run(Action<string, Action> run)
    {
        run("Layout columns and grid use deterministic slots, gaps and remainder pixels", () =>
        {
            var m = Monitor with { Work = new(-1920, -100, 1921, 1041) };
            var columns = LayoutPlanner.Cells(m, 2, ArrangementKind.SideBySide);
            Check(columns[0] == new Rectangle(-1912, -92, 949, 1025));
            Check(columns[1].X == columns[0].Right + 8 && columns[1].Width == 948);
            Check(columns[1].Right == m.Work.Right - 8);
            var grid = LayoutPlanner.Cells(m, 3, ArrangementKind.Grid);
            Check(grid.Length == 3 && grid[0].Y == grid[1].Y && grid[2].X == grid[0].X && grid[2].Y == grid[0].Bottom + 8);
            Check(grid[2].Width == grid[0].Width); // Empty final cell does not stretch.
        });
        run("Layout minimum includes destination-DPI frames and rejects all moves when one monitor cannot fit", () =>
        {
            var m = Monitor with { Dpi = 144 };
            Check(LayoutPlanner.Minimum(Window(1), m) == new Size(984, 599));
            var exact = Window(1) with { Frames = new Dictionary<uint, Size> { [144] = new(26, 60) } };
            Check(LayoutPlanner.Minimum(exact, m) == new Size(986, 600));
            var small = Monitor with { Id = "small", Work = new(1920, 0, 600, 400), Primary = false };
            var plan = LayoutPlanner.Arrange([Monitor, small], [Window(1), Window(2)], [Monitor.Id, small.Id], ArrangementKind.Grid);
            Check(!plan.Valid && plan.Placements.Length == 0 && plan.Error!.Contains("Maximum fitting count: 0"));
            Check(LayoutPlanner.Capacity(Monitor, [Window(1)], ArrangementKind.Grid) == 4);
            Check(LayoutPlanner.Capacity(Monitor, [Window(1)], ArrangementKind.SideBySide) == 2);
        });
        run("Layouts preserve explicit order and per-monitor assignments across unequal and negative-coordinate displays", () =>
        {
            var left = Monitor with { Id = "left", Work = new(-2560, 100, 2560, 1400), Dpi = 144, Primary = false };
            var windows = new[] { Window(3), Window(1), Window(2) };
            var plan = LayoutPlanner.Arrange([Monitor, left], windows, [left.Id, Monitor.Id, left.Id], ArrangementKind.SideBySide);
            Check(plan.Valid);
            Check(plan.Placements.Select(p => p.Target).SequenceEqual(windows.Select(w => w.Target)));
            Check(plan.Placements.Single(p => p.Target == windows[0].Target).Bounds.X < plan.Placements.Single(p => p.Target == windows[2].Target).Bounds.X);
            Check(plan.Placements.Single(p => p.Target == windows[1].Target).Bounds.X > 0);
            Check(!LayoutPlanner.Arrange([Monitor], windows, ["missing", "missing", "missing"], ArrangementKind.Grid).Valid);
        });
        run("Saved slots contain logical geometry only, preserve overlap, and map by current ordered targets", () =>
        {
            var m = Monitor with { Work = new(-1920, 40, 1920, 1040), Dpi = 144 };
            var window = Window(1) with { NormalBounds = new(-1800, 100, 1200, 750) };
            var slot = LayoutPlanner.Save(window, [m]);
            Check(slot.X == 80 && slot.Y == 40 && slot.Width == 800 && slot.Height == 500);
            var preset = new WindowPreset("private-preset-canary", [slot, slot]);
            var plan = LayoutPlanner.Restore([m], [Window(8), Window(7)], preset);
            Check(plan.Valid && plan.Placements[0].Target == Window(8).Target && plan.Placements[1].Target == Window(7).Target);
            Check(plan.Placements[0].Bounds == plan.Placements[1].Bounds);
            Check(!LayoutPlanner.Restore([m], [Window(1)], preset).Valid);
            string json = JsonSerializer.Serialize(preset);
            Check(!json.Contains("Caption") && !json.Contains("Target") && !json.Contains("Created") && !json.Contains("Handle"));
        });
        run("Restore matches monitor identities, clamps changed work areas and repacks missing monitors without altering slots", () =>
        {
            var primary = Monitor with { Work = new(0, 50, 1600, 850) };
            var preset = new WindowPreset("layout", [new("disconnected", 0, 0, 900, 600), new(primary.Id, 1500, -30, 800, 500)]);
            var windows = new[] { Window(1), Window(2) };
            var plan = LayoutPlanner.Restore([primary], windows, preset);
            Check(plan.Valid && plan.Notes.Length == 1 && plan.Placements[0].Target == windows[0].Target);
            Check(plan.Placements[1].Bounds == new Rectangle(800, 50, 800, 500));
            Check(preset.Slots[0].Monitor == "disconnected");
            var tiny = primary with { Work = new(0, 0, 600, 400) };
            Check(!LayoutPlanner.Restore([tiny], windows, preset).Valid);
        });
        run("Explicit recovery moves only off-screen title bars, cascades and preserves minimum sizes", () =>
        {
            var visible = Window(1) with { NormalBounds = new(-400, 10, 800, 500) };
            var off = Window(2) with { NormalBounds = new(-3000, -1000, 800, 500) };
            var plan = LayoutPlanner.Recover([Monitor], [visible, off, off with { Target = Window(3).Target }]);
            Check(plan.Valid && plan.Placements.Length == 2);
            Check(plan.Placements[0].Slot == 2 && plan.Placements[1].Slot == 3);
            Check(plan.Placements[0].Bounds == new Rectangle(0, 0, 800, 500) && plan.Placements[1].Bounds.Location == new Point(32, 32));
            Check(!LayoutPlanner.Recover([Monitor], [visible]).Valid);
        });
        run("Layout settings atomically round-trip, validate bounds and schema, and preserve existing files on invalid save", () =>
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".build", "layout-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var store = new LayoutStore(Path.Combine(dir, "settings.json"));
                Check(store.Load().Presets.Length == 0);
                var settings = LayoutSettings.Default with { Presets = [new("Saved", [new(Monitor.Id, 0, 0, 800, 500)])] };
                store.Save(settings); string original = File.ReadAllText(store.FilePath);
                Check(store.Load().Presets.Single().Name == "Saved");
                Throws(() => store.Save(settings with { Version = 2 }));
                Throws(() => store.Save(settings with { Presets = [new("bad", [new("x", double.NaN, 0, 1, 1)])] }));
                Check(File.ReadAllText(store.FilePath) == original && Directory.GetFiles(dir).Length == 1);
                File.WriteAllText(store.FilePath, "{bad"); Throws(() => store.Load());
                var preferences = new LayoutPreferences(store); Check(!preferences.Available);
                Throws(() => preferences.Save(LayoutSettings.Default));
                preferences.Reset(); Check(preferences.Available && store.Load().Version == 1);
            }
            finally { Directory.Delete(dir, true); }
        });
        run("Shortcut policy rejects bare, reserved and duplicate bindings; registrations release and report conflicts", () =>
        {
            Check(ShortcutPolicy.Valid(Keys.Control | Keys.Alt | Keys.D1));
            Check(!ShortcutPolicy.Valid(Keys.A) && !ShortcutPolicy.Valid(Keys.Control | Keys.F12) && !ShortcutPolicy.Valid(Keys.Control | Keys.LWin));
            var keys = new FakeKeys(); using var registrations = new HotkeyRegistrations(keys);
            Check(registrations.Set(LayoutSettings.Default.Shortcuts, true).Length == 1);
            Check(keys.Active.Count == 4 && registrations.Command(1) == "Manager");
            registrations.Set(LayoutSettings.Default.Shortcuts, false); Check(keys.Active.Count == 0 && registrations.Command(1) is null);
            var duplicate = LayoutSettings.Default.Shortcuts; duplicate["Grid"] = duplicate["Saved"];
            Check(registrations.Set(duplicate, true).Length == 1 && keys.Active.Count == 0);
        });
        run("Placement preflights every lifetime and topology before any restore or move", () => WithoutContext(async () =>
        {
            var platform = new FakePlatform { InvalidOpen = 2 };
            var coordinator = new PlacementCoordinator(platform);
            try { await coordinator.ApplyAsync(Plan(), default); throw new Exception("Expected preflight rejection"); }
            catch (InvalidOperationException) { }
            Check(platform.Restores == 0 && platform.Moves == 0 && platform.Opened == platform.Disposed);
            platform = new FakePlatform { ChangedDisplays = true };
            try { await new PlacementCoordinator(platform).ApplyAsync(Plan(), default); throw new Exception("Expected display rejection"); }
            catch (InvalidOperationException) { }
            Check(platform.Opened == 0 && platform.Moves == 0);
        }));
        run("Placement skips a changed target after preflight without repacking later windows", () => WithoutContext(async () =>
        {
            var platform = new FakePlatform { ExitAfterPreflight = 1 };
            var plan = Plan();
            var results = await new PlacementCoordinator(platform).ApplyAsync(plan, default);
            Check(results.Length == 2 && platform.Moves == 1 && results[0].Message.StartsWith("Skipped"));
            Check(platform.Requested.Single() == plan.Placements[1].Bounds && platform.Opened == platform.Disposed);
        }));
        run("Placement handles denied moves, application-enforced size, cancellation and display changes during apply", () => WithoutContext(async () =>
        {
            var platform = new FakePlatform { IgnoreMoves = true };
            var results = await new PlacementCoordinator(platform).ApplyAsync(Plan(), default);
            Check(results.All(r => r.Message.StartsWith("Placement not confirmed")) && platform.Moves == 2);
            platform = new FakePlatform { DenyMoves = true };
            results = await new PlacementCoordinator(platform).ApplyAsync(Plan(), default);
            Check(platform.Moves == 2 && results.All(r => r.Message.StartsWith("Placement not confirmed")));
            platform = new FakePlatform { ChangeOnRestore = true };
            results = await new PlacementCoordinator(platform).ApplyAsync(Plan(), default);
            Check(platform.Moves == 0 && results[1].Message.StartsWith("Skipped"));
            using var cancel = new CancellationTokenSource();
            platform = new FakePlatform { OnRestore = cancel.Cancel };
            try { await new PlacementCoordinator(platform).ApplyAsync(Plan(), cancel.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
            Check(platform.Moves == 0 && platform.Opened == platform.Disposed);
        }));
        run("Optional tray lifetime registers only on explicit enable and releases its session mutex and bindings", () =>
        {
            var keys = new FakeKeys();
            string mutexName = @"Local\RobloxLaunchHelper-TestTray-" + Guid.NewGuid().ToString("N");
            using var host = new ManagerHost(false, keys, new LayoutPreferences(new LayoutStore(Path.Combine(AppContext.BaseDirectory, "nonexistent-layout-fixture.json"))), mutexName,
                enableBackgroundCleanup: false, ownsMessageLoop: false);
            Check(!host.TrayActive && keys.Active.Count == 0);
            host.EnableTray(); Check(host.TrayActive && keys.Active.Count == 4);
            bool CanAcquire()
            {
                return Task.Run(() =>
                {
                    using var mutex = new Mutex(false, mutexName);
                    bool acquired = mutex.WaitOne(0);
                    if (acquired) mutex.ReleaseMutex();
                    return acquired;
                }).GetAwaiter().GetResult();
            }
            Check(!CanAcquire());
            host.StopTray(); Check(!host.TrayActive && keys.Active.Count == 0 && CanAcquire());
        });
        run("Fullscreen batches request windowed mode once, verify transitions and continue after a nonresponding client", () => WithoutContext(async () =>
        {
            var platform = new FakePlatform(); platform.Fullscreen.UnionWith([1, 2]);
            var results = await new PlacementCoordinator(platform).ApplyAsync(Plan(), default);
            Check(platform.WindowedRequests == 2 && platform.Moves == 2 && platform.Fullscreen.Count == 0 && results.All(r => r.Message == "Placed."));
            platform = new FakePlatform { IgnoreWindowedRequests = true }; platform.Fullscreen.Add(1);
            results = await new PlacementCoordinator(platform).ApplyAsync(Plan(), default);
            Check(platform.WindowedRequests == 1 && platform.Moves == 1 && results[0].Message.Contains("did not confirm windowed") && results[1].Message == "Placed.");
            platform = new FakePlatform { DenyWindowed = true }; platform.Fullscreen.Add(1);
            results = await new PlacementCoordinator(platform).ApplyAsync(Plan(), default);
            Check(platform.WindowedRequests == 1 && platform.Moves == 1 && results[0].Message.Contains("request failed"));
            platform = new FakePlatform();
            await new PlacementCoordinator(platform).ApplyAsync(Plan(), default); Check(platform.WindowedRequests == 0);
            using var cancel = new CancellationTokenSource();
            platform = new FakePlatform { OnWindowed = cancel.Cancel }; platform.Fullscreen.Add(1);
            try { await new PlacementCoordinator(platform).ApplyAsync(Plan(), cancel.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
            Check(platform.WindowedRequests == 1 && platform.Moves == 0 && platform.Opened == platform.Disposed);
        }));
        run("Native placement fixture reads restored bounds, excludes fixed windows and prepares fullscreen windows", () =>
        {
            using var fixture = new Form { Text = "Synthetic placement fixture", StartPosition = FormStartPosition.Manual, Location = new(100, 100), Size = new(800, 500) };
            fixture.Show(); var target = new InstanceTarget(new(Environment.ProcessId, Created), fixture.Handle);
            var normal = PlacementNative.Read(target); Check(normal.Movable && normal.Frame.Width > 0);
            var bounds = new Rectangle(120, 130, 900, 600);
            Check(PlacementNative.SetWindowPos(fixture.Handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0004 | 0x4000));
            Check(PlacementNative.Bounds(fixture.Handle) == bounds);
            fixture.WindowState = FormWindowState.Minimized;
            Check(PlacementNative.Read(target).NormalBounds.Size == bounds.Size);
            PlacementNative.ShowWindowAsync(fixture.Handle, 4); Application.DoEvents();
            Check(!PlacementNative.IsIconic(fixture.Handle));
            fixture.WindowState = FormWindowState.Maximized;
            Check(PlacementNative.Read(target).NormalBounds.Size == bounds.Size);
            PlacementNative.ShowWindowAsync(fixture.Handle, 4); Application.DoEvents();
            Check(!PlacementNative.IsZoomed(fixture.Handle));
            fixture.WindowState = FormWindowState.Normal;
            fixture.FormBorderStyle = FormBorderStyle.FixedDialog;
            Check(!PlacementNative.Read(target).Movable);
            fixture.FormBorderStyle = FormBorderStyle.None; fixture.Bounds = Screen.FromControl(fixture).Bounds;
            Check(PlacementNative.Read(target).NeedsWindowed && PlacementNative.Read(target).Movable);
            fixture.WindowState = FormWindowState.Minimized;
            Check(PlacementNative.Read(target).NeedsWindowed && PlacementNative.Read(target).Movable);
            fixture.Close();
        });
        run("Fullscreen preparation verifies foreground, sends one shortcut and restores the manager without stealing user focus", () =>
        {
            WindowedResult Prepare(FakeFullscreenInput input, Func<bool>? valid = null, CancellationToken token = default) =>
                FullscreenPreparation.Request(20, valid ?? (() => true), input, token, _ => { });
            var input = new FakeFullscreenInput();
            Check(Prepare(input) == WindowedResult.Ready && input.Toggles == 1 && input.Foreground == 10);
            Check(input.Focused.SequenceEqual(new nint[] { 20, 10 }));
            input = new() { Current = 20 };
            Check(Prepare(input) == WindowedResult.Ready && input.Toggles == 1 && input.Focused.Count == 0);
            input = new(); input.OnFocus = target => { if (target == 20) input.Current = 0; };
            Check(FullscreenPreparation.Request(20, () => true, input, default, _ => input.Current = 20) == WindowedResult.Ready);
            Check(input.Toggles == 1 && input.Foreground == 10);
            input = new(); input.OnToggle = () => input.Current = 0;
            Check(FullscreenPreparation.Request(20, () => true, input, default, _ => { if (input.Current == 0) input.Current = 20; }) == WindowedResult.Ready);
            Check(input.Toggles == 1 && input.Foreground == 10);
            input = new() { IsFullscreen = false };
            Check(Prepare(input) == WindowedResult.Ready && input.Toggles == 0 && input.Focused.Count == 0);
            input = new() { FocusAllowed = false };
            Check(Prepare(input) == WindowedResult.FocusDenied && input.Toggles == 0);
            input = new() { Released = false };
            Check(Prepare(input) == WindowedResult.KeysHeld && input.Toggles == 0 && input.Focused.Count == 0);
            input = new(); input.OnFocus = target => { if (target == 20) input.Current = 30; };
            Check(Prepare(input) == WindowedResult.Interrupted && input.Toggles == 0 && input.Foreground == 30);
            input = new() { ExitOnToggle = false }; input.OnToggle = () => input.Current = 30;
            Check(Prepare(input) == WindowedResult.Interrupted && input.Toggles == 1 && input.Foreground == 30);
            input = new() { ExitOnToggle = false };
            Check(Prepare(input) == WindowedResult.TimedOut && input.Toggles == 1 && input.Foreground == 10);
            input = new() { ToggleAllowed = false };
            Check(Prepare(input) == WindowedResult.InputDenied && input.Toggles == 1 && input.Foreground == 10);
            bool alive = true; input = new(); input.OnFocus = _ => alive = false;
            Check(Prepare(input, () => alive) == WindowedResult.Changed && input.Toggles == 0);
            input = new(); input.OnFocus = target => { if (target == 20) input.IsFullscreen = false; };
            Check(Prepare(input) == WindowedResult.Ready && input.Toggles == 0);
            using var cancel = new CancellationTokenSource(); input = new(); input.OnFocus = _ => cancel.Cancel();
            try { Prepare(input, token: cancel.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
            Check(input.Toggles == 0 && input.Foreground == 10);
        });
        run("Arrangement editor renders, maintains ordered checked targets and excludes layout settings from reports", Render);
    }
    private static PlacementPlan Plan() => LayoutPlanner.Arrange([Monitor], [Window(1), Window(2)], [Monitor.Id, Monitor.Id], ArrangementKind.SideBySide);
    private static void WithoutContext(Func<Task> test)
    {
        var previous = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(null);
        try { test().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private sealed class FakeKeys : IHotkeyPlatform
    {
        internal readonly HashSet<int> Active = [];
        public bool Register(int id, Keys keys) { if (id == 2) return false; Active.Add(id); return true; }
        public void Unregister(int id) => Active.Remove(id);
    }
    private sealed class FakePlatform : IPlacementPlatform
    {
        internal int InvalidOpen, Opened, Disposed, Moves, Restores, ExitAfterPreflight, WindowedRequests;
        internal bool ChangedDisplays, IgnoreMoves, ChangeOnRestore, DenyMoves, IgnoreWindowedRequests, DenyWindowed;
        internal Action? OnRestore, OnWindowed;
        internal readonly List<Rectangle> Requested = [];
        internal readonly HashSet<int> Fullscreen = [];
        public LayoutMonitor[] Monitors() => [ChangedDisplays ? Monitor with { Dpi = 144 } : Monitor];
        public IPlacementSession Open(InstanceTarget target) { Opened++; return new Session(this, target); }
        private sealed class Session(FakePlatform owner, InstanceTarget target) : IPlacementSession
        {
            private Rectangle bounds;
            public bool Valid => target.Key.Id != owner.InvalidOpen && !(owner.Opened >= 2 && target.Key.Id == owner.ExitAfterPreflight);
            public bool Normal => true;
            public LayoutWindow Read() => owner.Fullscreen.Contains(target.Key.Id)
                ? Window(target.Key.Id) with { NeedsWindowed = true } : Window(target.Key.Id);
            public WindowedResult RequestWindowed(CancellationToken token)
            {
                owner.WindowedRequests++; owner.OnWindowed?.Invoke();
                if (owner.DenyWindowed) return WindowedResult.InputDenied;
                if (!owner.IgnoreWindowedRequests) owner.Fullscreen.Remove(target.Key.Id);
                return WindowedResult.Ready;
            }
            public bool Restore() { owner.Restores++; owner.OnRestore?.Invoke(); if (owner.ChangeOnRestore) owner.ChangedDisplays = true; return true; }
            public bool Move(Rectangle value) { owner.Moves++; owner.Requested.Add(value); if (!owner.IgnoreMoves && !owner.DenyMoves) bounds = value; return !owner.DenyMoves; }
            public Rectangle Bounds() => bounds;
            public void Dispose() => owner.Disposed++;
        }
    }
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Render()
    {
        var previous = SynchronizationContext.Current;
        using var context = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try { RenderUI(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void RenderUI()
    {
        var platform = new FakePlatform();
        var choices = new List<ArrangementChoice>();
        var preferences = new LayoutPreferences(new LayoutStore(Path.Combine(AppContext.BaseDirectory, "nonexistent-layout-fixture.json")));
        bool replacement = false;
        InstanceSnapshot Discover(CancellationToken _) => new([new(Window(1).Target.Key, [new(1, "Synthetic client A")], null, null, false),
            new(Window(replacement ? 3 : 2).Target.Key, [new(replacement ? 3 : 2, "Synthetic client B")], null, null, false)], 0);
        using var form = new ArrangementForm(choices, preferences, ArrangementKind.SideBySide, null, Monitor.Id, platform, Discover);
        form.Show();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (choices.Count < 2 && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Check(choices.Count == 2 && choices.All(c => c.Selected));
        var buttons = Descendants(form).OfType<Button>().ToArray();
        Check(!buttons.Any(b => b.Text == "&Preview"));
        WaitFor(() => buttons.Single(b => b.Text == "&Apply").Enabled);
        void Preview()
        {
            var task = form.RefreshPreviewAsync();
            WaitFor(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }
        Preview();
        if (!buttons.Single(b => b.Text == "&Apply").Enabled)
            throw new Exception("Preview did not enable Apply: " + string.Join(" / ", Descendants(form).OfType<Label>().Select(l => l.Text)));
        var table = Descendants(form).OfType<DataGridView>().Single();
        Check(!table.AllowUserToResizeColumns && !table.AllowUserToResizeRows);
        int monitorClicks=0; table.CellMouseClick+=(_,_)=>monitorClicks++;
        var monitorCell=table.GetCellDisplayRectangle(2,1,false);
        nint monitorPoint=((monitorCell.Top+monitorCell.Height/2)<<16) | ((monitorCell.Right-14)&0xffff);
        SendMessage(table.Handle,0x0201,1,monitorPoint);
        SendMessage(table.Handle,0x0202,0,monitorPoint);
        Application.DoEvents();
        var monitorMenu=((LayoutWindowGrid)table).MonitorMenu;
        if(!monitorMenu.Visible || table.EditingControl is not null) throw new Exception($"Monitor menu not opened: clicks={monitorClicks}, cell={monitorCell}, client={table.ClientSize}, enabled={table.Enabled}, current={table.CurrentCell?.ColumnIndex}");
        Check(monitorMenu.BackColor==ThemePalette.For(table).Surface);
        using(var image=new Bitmap(monitorMenu.Width,monitorMenu.Height))
        {
            monitorMenu.DrawToBitmap(image,new(Point.Empty,monitorMenu.Size));
            image.Save(Path.Combine(AppContext.BaseDirectory,"..","..","..","..",".build","layout-monitor-menu.png"));
        }
        ((ToolStripMenuItem)monitorMenu.Items[0]).PerformClick(); monitorMenu.Close();
        Check(table.Rows[1].Cells[2].Value as string==Monitor.Label);
        Check(table.CurrentCell?.RowIndex==1);
        ClickCheckbox(table, 0);
        WaitFor(() => Descendants(form).OfType<LayoutPreview>().Single().Plan?.Placements.Length == 1);
        Check(!choices[0].Selected && table.Rows[0].Cells[0].Value is false);
        ClickCheckbox(table, 0);
        Check(choices[0].Selected && table.Rows[0].Cells[0].Value is true);
        WaitFor(() => Descendants(form).OfType<LayoutPreview>().Single().Plan?.Placements.Length == 2);
        platform.Fullscreen.Add(2); Preview();
        Check(choices[1].Selected && table.Rows[1].Cells[3].Value as string == "Ready — will exit fullscreen");
        Check(Descendants(form).OfType<LayoutPreview>().Single().Plan!.Placements.Length == 2);
        ClickCheckbox(table, 1); Check(!choices[1].Selected);
        ClickCheckbox(table, 1); Check(choices[1].Selected);
        platform.Fullscreen.Remove(2); Preview();
        Check(table.Rows[1].Cells[3].Value as string == "Ready" && buttons.Single(b => b.Text == "&Apply").Enabled);
        Check(Descendants(form).OfType<LayoutPreview>().Single().Plan!.Placements.Length == 2);
        table.CurrentCell = table.Rows[1].Cells[1]; buttons.Single(b => b.Text == "Move up").PerformClick();
        Check(choices[0].Target == Window(2).Target && !buttons.Single(b => b.Text == "&Apply").Enabled);
        string output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".build"));
        foreach (var (size, name) in new[] { (form.Size, "arrangement-preview.png"), (form.MinimumSize, "arrangement-narrow.png") })
        {
            form.Size = size; form.PerformLayout(); Preview();
            using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(output, name));
        }
        form.Scale(new SizeF(1.5f, 1.5f)); form.PerformLayout();
        using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(output, "arrangement-150.png")); }
        var health = new HealthSnapshot(1, "1.1.0", "10.0", "X64", DateTimeOffset.UtcNow, [], DiagnosticChecks.NotTested);
        string report = Encoding.UTF8.GetString(DiagnosticReports.Build(health, [], true, []).Json);
        Check(!report.Contains("monitor-interface-primary") && !report.Contains("synthetic-private-caption") && !report.Contains("window-layouts"));
        replacement = true; buttons.Single(b => b.Text == "Refresh windows").PerformClick();
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (!choices.Any(c => c.Target == Window(3).Target) && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Check(choices[0].Target == Window(1).Target && choices[0].Selected && !choices.Single(c => c.Target == Window(3).Target).Selected);
        Check(!choices.Any(c => c.Target == Window(2).Target));
        form.Close();
    }
    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Check(condition());
    }
    private sealed class FakeFullscreenInput : IFullscreenInput
    {
        internal nint Current = 10;
        internal bool IsFullscreen = true, Released = true, FocusAllowed = true, ExitOnToggle = true, ToggleAllowed = true;
        internal int Toggles;
        internal Action<nint>? OnFocus;
        internal Action? OnToggle;
        internal readonly List<nint> Focused = [];
        public nint Foreground => Current;
        public bool ManagerWindow(nint window) => window == 10;
        public bool KeysReleased => Released;
        public bool Fullscreen(nint window) => IsFullscreen;
        public bool Focus(nint window)
        {
            Focused.Add(window);
            if (!FocusAllowed && window == 20) return false;
            Current = window; OnFocus?.Invoke(window); return true;
        }
        public bool Toggle(nint window)
        {
            Check(Current == window); Toggles++; OnToggle?.Invoke();
            if (ExitOnToggle && ToggleAllowed) IsFullscreen = false; return ToggleAllowed;
        }
    }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wparam, nint lparam);
    private static void ClickCheckbox(DataGridView table, int row)
    {
        var cell = table.GetCellDisplayRectangle(0, row, false);
        var content = table.Rows[row].Cells[0].ContentBounds;
        int x = cell.X + content.X + content.Width / 2, y = cell.Y + content.Y + content.Height / 2;
        nint point = (y << 16) | (x & 0xffff);
        SendMessage(table.Handle, 0x0201, 1, point);
        SendMessage(table.Handle, 0x0202, 0, point);
        Application.DoEvents();
    }
}
