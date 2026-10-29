using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxLaunchHelper;

internal interface IHotkeyPlatform
{
    bool Register(int id, Keys keys);
    void Unregister(int id);
}
internal sealed class HotkeyRegistrations(IHotkeyPlatform platform) : IDisposable
{
    private readonly Dictionary<int, string> registered = [];
    internal string[] Set(Dictionary<string, int> shortcuts, bool enabled)
    {
        Dispose();
        if (!enabled) return [];
        if (shortcuts.Values.Any(k => !ShortcutPolicy.Valid((Keys)k)) || shortcuts.Values.Distinct().Count() != shortcuts.Count)
            return ["Invalid or duplicate shortcut bindings."];
        var conflicts = new List<string>(); int id = 1;
        foreach (var (command, keys) in shortcuts)
        {
            if (platform.Register(id, (Keys)keys)) registered[id] = command;
            else conflicts.Add(ShortcutPolicy.Label(command) + ": shortcut is unavailable; choose another combination.");
            id++;
        }
        return conflicts.ToArray();
    }
    internal string? Command(int id) => registered.GetValueOrDefault(id);
    public void Dispose() { foreach (int id in registered.Keys) platform.Unregister(id); registered.Clear(); }
}

internal sealed class HotkeyWindow : NativeWindow, IHotkeyPlatform, IDisposable
{
    internal event Action<int>? Pressed;
    internal HotkeyWindow() => CreateHandle(new CreateParams { Caption = "Roblox window shortcuts", Parent = new nint(-3) });
    public bool Register(int id, Keys keys)
    {
        uint modifiers = 0x4000;
        if ((keys & Keys.Control) != 0) modifiers |= 2;
        if ((keys & Keys.Alt) != 0) modifiers |= 1;
        if ((keys & Keys.Shift) != 0) modifiers |= 4;
        return RegisterHotKey(Handle, id, modifiers, (uint)(keys & Keys.KeyCode));
    }
    public void Unregister(int id) => UnregisterHotKey(Handle, id);
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312) Pressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }
    public void Dispose() => DestroyHandle();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}

// Diagnostics owns the normal lifetime. Only explicit tray mode keeps the UI loop alive.
internal sealed class ManagerHost : ApplicationContext
{
    internal readonly LayoutPreferences Preferences;
    private DiagnosticsForm? diagnostics;
    private readonly HotkeyWindow hotkeyWindow = new();
    private readonly HotkeyRegistrations registrations;
    private NotifyIcon? tray;
    private Mutex? trayMutex;
    private bool globalEnabled, handling, exiting;
    private Task draining = Task.CompletedTask;
    private readonly bool collectOnShown;
    private readonly string trayMutexName;
    private readonly UpdateShutdownRequest updateShutdown;
    private readonly BackgroundClientMonitor? backgroundCleanup;
    internal bool TrayActive => tray is not null;
    internal static string TrayMutexName
    {
        get { using var process = Process.GetCurrentProcess(); return @"Local\RobloxLaunchHelper-WindowShortcuts-" + Program.Sid + "-" + process.SessionId; }
    }
    internal ManagerHost(bool collectOnShown = true, IHotkeyPlatform? hotkeys = null, LayoutPreferences? preferences = null,
        string? mutexName = null, bool enableBackgroundCleanup = true)
    {
        trayMutexName = mutexName ?? TrayMutexName;
        this.collectOnShown = collectOnShown; Preferences = preferences ?? new LayoutPreferences();
        registrations = new(hotkeys ?? hotkeyWindow);
        hotkeyWindow.Pressed += async id =>
        {
            if (handling || exiting || Application.OpenForms.Cast<Form>().Any(f => f.Modal)) return;
            Control? active = Form.ActiveForm?.ActiveControl;
            while (active is ContainerControl container && container.ActiveControl is not null) active = container.ActiveControl;
            if (active is TextBoxBase) return;
            string? command = registrations.Command(id); if (command is null) return;
            handling = true;
            try
            {
                await OpenAsync();
                if (diagnostics is not null) await diagnostics.OpenInstancesAsync(command == "Manager" ? null : Enum.Parse<ArrangementKind>(command));
            }
            catch { tray?.ShowBalloonTip(4000, "Window shortcuts", "Command unavailable. Open the manager and try again.", ToolTipIcon.Warning); }
            finally { handling = false; }
        };
        Preferences.Changed += UpdateKeys;
        updateShutdown = new UpdateShutdownRequest(ExitAsync);
        if (enableBackgroundCleanup)
        {
            using var process = Process.GetCurrentProcess();
            string sid = Program.Sid; int session = process.SessionId;
            var platform = new InstancePlatform();
            backgroundCleanup = new(new InstanceCollector(platform, sid, session, collectMetrics: false),
                new(pid => new InstanceProcess(pid, allowStop: true), platform.Trusted, sid, session),
                @"Local\RobloxLaunchHelper-BackgroundCleanup-" + sid + "-" + session);
        }
        ShowDiagnostics();
    }
    private void ShowDiagnostics()
    {
        diagnostics = new DiagnosticsForm(collectOnShown, this);
        var current = diagnostics;
        current.FormClosed += async (_, _) =>
        {
            draining = current.InstanceCompletion;
            if (ReferenceEquals(diagnostics, current)) diagnostics = null;
            await draining;
            if (tray is null && diagnostics is null)
            {
                if (backgroundCleanup is not null) await backgroundCleanup.StopAsync();
                ExitThread();
            }
        };
        current.Show();
    }
    private async Task OpenAsync()
    {
        await draining;
        if (exiting) return;
        if (diagnostics is null || diagnostics.IsDisposed) ShowDiagnostics();
        if (diagnostics!.WindowState == FormWindowState.Minimized) diagnostics.WindowState = FormWindowState.Normal;
        diagnostics.Show(); diagnostics.Activate();
    }
    internal void EnableTray()
    {
        if (tray is not null) return;
        var candidate = new Mutex(false, trayMutexName);
        bool acquired;
        try { acquired = candidate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            candidate.Dispose(); MessageBox.Show("Tray mode is already running in this Windows session. Use its tray icon.", "Window shortcuts"); return;
        }
        trayMutex = candidate;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open manager", null, async (_, _) => { if (!exiting) { await OpenAsync(); if (diagnostics is not null) await diagnostics.OpenInstancesAsync(); } });
        var enabled = new ToolStripMenuItem("Enable global shortcuts") { CheckOnClick = true, Checked = true };
        enabled.CheckedChanged += (_, _) => { globalEnabled = enabled.Checked; UpdateKeys(); };
        menu.Items.Add(enabled); menu.Items.Add("Shortcut settings…", null, (_, _) => ConfigureShortcuts());
        menu.Items.Add("Disable tray mode", null, async (_, _) =>
        {
            StopTray(); if (diagnostics is null)
            {
                await draining;
                if (backgroundCleanup is not null) await backgroundCleanup.StopAsync();
                ExitThread();
            }
        });
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        tray = new NotifyIcon { Text = "Roblox window shortcuts", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += async (_, _) => { await OpenAsync(); if (diagnostics is not null) await diagnostics.OpenInstancesAsync(); };
        globalEnabled = true; UpdateKeys();
    }
    private void UpdateKeys()
    {
        var errors = registrations.Set(Preferences.Settings.Shortcuts, tray is not null && globalEnabled && Preferences.Available);
        if (errors.Length > 0) tray?.ShowBalloonTip(5000, "Shortcut conflicts", string.Join("\n", errors), ToolTipIcon.Warning);
        if (!Preferences.Available && tray is not null) tray.ShowBalloonTip(5000, "Window shortcuts", "Saved settings are unavailable. Reset them in Arrange to enable global shortcuts.", ToolTipIcon.Warning);
    }
    internal void ConfigureShortcuts()
    {
        if (Application.OpenForms.Cast<Form>().Any(f => f.Modal)) return;
        using var form = new ShortcutSettingsForm(Preferences);
        form.ShowDialog(Application.OpenForms.Cast<Form>().LastOrDefault());
    }
    internal void StopTray()
    {
        registrations.Dispose(); globalEnabled = false;
        if (tray is not null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); tray = null; }
        if (trayMutex is not null) { trayMutex.ReleaseMutex(); trayMutex.Dispose(); trayMutex = null; }
    }
    private async Task ExitAsync()
    {
        if (exiting) return;
        if (Application.OpenForms.Cast<Form>().Any(f => f.Modal))
        { tray?.ShowBalloonTip(3000, "Window shortcuts", "Close the open dialog before exiting.", ToolTipIcon.Info); return; }
        exiting = true; StopTray();
        var current = diagnostics;
        current?.Close();
        if (current is not null) await current.InstanceCompletion;
        await draining;
        if (backgroundCleanup is not null) await backgroundCleanup.StopAsync();
        ExitThread();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { backgroundCleanup?.Dispose(); updateShutdown.Dispose(); Preferences.Changed -= UpdateKeys; StopTray(); registrations.Dispose(); hotkeyWindow.Dispose(); diagnostics?.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class ShortcutSettingsForm : Form
{
    internal ShortcutSettingsForm(LayoutPreferences preferences)
    {
        Text = "Global shortcut settings"; ClientSize = new(550, 350); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(12) };
        root.Controls.Add(new Label { AutoSize = true, Text = "Click a binding and press a modified letter, digit, or F1–F11.\nGlobal keys work only in explicitly enabled tray mode." });
        var bindings = new Dictionary<string, Keys>();
        foreach (var (command, value) in preferences.Settings.Shortcuts)
        {
            bindings[command] = (Keys)value;
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(new Label { Text = ShortcutPolicy.Label(command), Width = 130, Padding = new(0, 5, 0, 0) });
            var box = new TextBox { ReadOnly = true, Width = 300, Text = new KeysConverter().ConvertToString((Keys)value), AccessibleName = ShortcutPolicy.Label(command) + " global binding" };
            box.KeyDown += (_, e) =>
            {
                e.SuppressKeyPress = true;
                if (ShortcutPolicy.Valid(e.KeyData)) { bindings[command] = e.KeyData; box.Text = new KeysConverter().ConvertToString(e.KeyData); }
            };
            row.Controls.Add(box); root.Controls.Add(row);
        }
        var status = new Label { AutoSize = true, MaximumSize = new(500, 0) };
        var save = new Button { Text = "Save", AutoSize = true, Enabled = preferences.Available };
        save.Click += (_, _) =>
        {
            try
            {
                preferences.Save(preferences.Settings with { Shortcuts = bindings.ToDictionary(p => p.Key, p => (int)p.Value) });
                DialogResult = DialogResult.OK;
            }
            catch { status.Text = "Bindings must be unique and valid. Settings storage must be available."; }
        };
        root.Controls.Add(save); root.Controls.Add(status); Controls.Add(root);
    }
}
