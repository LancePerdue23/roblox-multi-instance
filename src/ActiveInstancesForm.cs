using System.Diagnostics;

namespace RobloxLaunchHelper;

internal sealed class ActiveInstancesForm : Form
{
    private readonly ListView clients = new() { View = View.Details, FullRowSelect = true, MultiSelect = false,
        HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Active Roblox clients" };
    private readonly ComboBox windows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400,
        AccessibleName = "Selected client window" };
    private readonly Label status = new() { AutoSize = true, Text = "Loading active instances…" };
    private readonly Label actionResult = new() { AutoSize = true, Dock = DockStyle.Fill,
        Text = "Select a client and a window to focus or request close.", AccessibleName = "Window action result" };
    private readonly Button focus = new() { Text = "&Focus", AutoSize = true, Enabled = false };
    private readonly Button close = new() { Text = "&Close window…", AutoSize = true, Enabled = false };
    private readonly InstanceRefreshController refresh;
    private readonly InstanceActions actions;
    private readonly BackgroundClientCleanup backgroundCleanup;
    private readonly Button cleanup = new() { Text = "Clean up background clients…", AutoSize = true, Enabled = false };
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private InstanceSnapshot snapshot = InstanceSnapshot.Failed;
    private bool busy, presenting, ready, fresh, disposedResources;
    private readonly Button arrange = new() { Text = "&Arrange…", AutoSize = true };
    private readonly List<ArrangementChoice> arrangementChoices = [];
    private readonly LayoutPreferences layoutPreferences;
    private string? lastPreset;
    internal Task Completion => refresh.Completion;
    internal ActiveInstancesForm(bool collectOnShown = true, IInstancePlatform? platform = null,
        LayoutPreferences? preferences = null, Action? enableTray = null, Action? configureShortcuts = null)
    {
        layoutPreferences = preferences ?? new LayoutPreferences();
        lifetimeToken = lifetime.Token;
        Text = "Roblox Multi Instance — Active instances";
        Size = new(1120, 520); MinimumSize = new(800, 380);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        platform ??= new InstancePlatform();
        using var current = Process.GetCurrentProcess();
        string sid = Program.Sid; int session = current.SessionId;
        actions = new(platform, sid, session);
        backgroundCleanup = new(pid => new InstanceProcess(pid, allowStop: true), platform.Trusted, sid, session);
        refresh = new(() => new InstanceCollector(platform, sid, session), Present);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new(12) };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        clients.Columns.Add("Window title(s)", 360); clients.Columns.Add("PID", 70);
        clients.Columns.Add("Process started (local)", 165); clients.Columns.Add("CPU % (total)", 105);
        clients.Columns.Add("Working set (MiB)", 130); clients.Columns.Add("State", 130);
        var controls = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        controls.Controls.Add(new Label { Text = "Window:", AutoSize = true, Padding = new(0, 6, 0, 0) });
        controls.Controls.Add(windows); controls.Controls.Add(focus); controls.Controls.Add(close);
        controls.Controls.Add(arrange);
        controls.Controls.Add(cleanup);
        cleanup.Click += async (_, _) => await CleanupBackgroundAsync();
        arrange.Click += async (_, _) => await OpenArrangementAsync(ArrangementKind.Grid);
        if (enableTray is not null)
        {
            var tray = new Button { Text = "Enable tray mode", AutoSize = true };
            tray.Click += (_, _) => { if (!busy) enableTray(); }; controls.Controls.Add(tray);
        }
        if (configureShortcuts is not null)
        {
            var shortcuts = new Button { Text = "Shortcut settings…", AutoSize = true };
            shortcuts.Click += (_, _) => { if (!busy) configureShortcuts(); }; controls.Controls.Add(shortcuts);
        }
        layout.Controls.Add(status, 0, 0); layout.Controls.Add(clients, 0, 1); layout.Controls.Add(controls, 0, 2);
        layout.Controls.Add(actionResult, 0, 3);
        layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill,
            Text = "Window titles are supplied by Roblox. They do not identify or verify accounts.\n" +
                "Working set includes shared resident pages. Close requests may interrupt gameplay.\n" +
                "While the system app runs, background clients without visible windows are automatically stopped after a one-minute grace period." }, 0, 4);
        Controls.Add(layout);
        windows.DisplayMember = nameof(WindowChoice.Label);
        clients.SelectedIndexChanged += (_, _) => { if (!presenting) ChooseClient(); };
        windows.SelectedIndexChanged += (_, _) => EnableActions();
        focus.Click += async (_, _) => await ActAsync(InstanceAction.Focus);
        close.Click += async (_, _) => await ActAsync(InstanceAction.Close);
        Shown += (_, _) => { ready = collectOnShown; SyncRefresh(); };
        VisibleChanged += (_, _) => SyncRefresh(); Resize += (_, _) => SyncRefresh();
        FormClosed += (_, _) => { ready = false; lifetime.Cancel(); refresh.Dispose(); };
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (Owner is not null) Owner.Resize += OwnerResized;
    }
    private void OwnerResized(object? sender, EventArgs e) => SyncRefresh();
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposedResources)
        {
            disposedResources = true;
            if (Owner is not null) Owner.Resize -= OwnerResized;
            lifetime.Cancel(); refresh.Dispose();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
    private void SyncRefresh()
    {
        bool active = ready && !busy && Visible && WindowState != FormWindowState.Minimized &&
            Owner?.WindowState != FormWindowState.Minimized && !IsDisposed;
        refresh.SetActive(active);
        if (!active) { fresh = false; focus.Enabled = close.Enabled = cleanup.Enabled = false; }
    }
    private ProcessKey? SelectedKey => clients.SelectedItems.Count == 1 ? clients.SelectedItems[0].Tag as ProcessKey? : null;
    internal InstanceTarget? SelectedTarget => windows.SelectedItem is WindowChoice choice && SelectedKey is ProcessKey key
        ? new(key, choice.Window.Handle) : null;
    private sealed record WindowChoice(InstanceWindow Window, int Number, string? Username = null)
    {
        public string Label => $"Window {Number}: {(Username is null ? Caption(Window.Title) : "Roblox - "+Username)}";
    }
    private static string Caption(string text) => string.IsNullOrWhiteSpace(text) ? "Untitled window" :
        new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    internal void Present(InstanceSnapshot result)
    {
        if (IsDisposed || Disposing) return;
        var selected = SelectedKey;
        var target = SelectedTarget;
        snapshot = result;
        fresh = result.Available;
        if (!result.Available)
        {
            // Retain the last visible values as explicitly stale, never actionable.
            foreach (ListViewItem item in clients.Items) item.SubItems[5].Text = "Stale";
            status.Text = "Refresh unavailable. Displayed values are stale; actions are disabled.";
            EnableActions(); return;
        }
        presenting = true;
        clients.BeginUpdate();
        try
        {
            clients.Items.Clear();
            foreach (var client in result.Instances)
            {
                var row = new ListViewItem([client.Username is not null ? "Roblox - "+client.Username : client.Windows.Length == 0 ? "No visible window" :
                    string.Join(" | ", client.Windows.Select(w => Caption(w.Title))), client.Key.Id.ToString(),
                    InstancePolicy.Started(client.Key).ToString("g"), client.CpuPercent?.ToString("F1") ??
                        (client.CpuAvailable ? "Sampling…" : "Unavailable"),
                    client.WorkingSet is ulong memory ? (memory / 1048576d).ToString("F1") : "Unavailable",
                    client.Windows.Length == 0 ? "Background / starting" : "Running"]);
                row.Tag = client.Key; clients.Items.Add(row);
                row.Selected = client.Key == selected;
            }
        }
        finally { clients.EndUpdate(); presenting = false; }
        ChooseClient(target, false);
        int windowed = result.Instances.Count(client => client.Windows.Length > 0);
        int background = result.Instances.Length - windowed;
        status.Text = (result.Instances.Length == 0 ? "No Roblox processes." :
            $"{windowed} client(s) with windows; {background} background process(es) without visible windows.") +
            $" Updated {DateTime.Now:T}." + (result.Unavailable > 0 ? $" {result.Unavailable} candidate(s) unavailable or unverified." : "");
    }
    private void ChooseClient(InstanceTarget? preserve = null, bool autoSelect = true)
    {
        windows.Items.Clear();
        var client = snapshot.Instances.FirstOrDefault(x => x.Key == SelectedKey);
        if (snapshot.Available && client is not null)
        {
            for (int i = 0; i < client.Windows.Length; i++) windows.Items.Add(new WindowChoice(client.Windows[i], i + 1, client.Username));
            int index = preserve is InstanceTarget target && target.Key == client.Key
                ? Array.FindIndex(client.Windows, w => w.Handle == target.Window) : -1;
            // After a refresh, a vanished selected window must not silently retarget.
            if (index >= 0) windows.SelectedIndex = index;
            else if (autoSelect && preserve is null && client.Windows.Length == 1) windows.SelectedIndex = 0;
        }
        EnableActions();
    }
    private void EnableActions()
    {
        bool available = fresh && !busy && Visible && WindowState != FormWindowState.Minimized &&
            Owner?.WindowState != FormWindowState.Minimized;
        focus.Enabled = close.Enabled = available && SelectedTarget is InstanceTarget target && InstancePolicy.Contains(snapshot, target);
        cleanup.Enabled = available && BackgroundClientCleanup.Candidates(snapshot, DateTime.UtcNow).Length > 0;
    }

    private async Task CleanupBackgroundAsync()
    {
        if (!fresh || busy) return;
        var candidates = BackgroundClientCleanup.Candidates(snapshot, DateTime.UtcNow);
        if (candidates.Length == 0) return;
        busy = true; SyncRefresh(); windows.Enabled = clients.Enabled = arrange.Enabled = false;
        try
        {
            await refresh.Completion;
            if (lifetimeToken.IsCancellationRequested) return;
            if (MessageBox.Show(this, $"Force stop {candidates.Length} Roblox background process(es) without visible windows?\n\n" +
                "Only processes older than one minute are eligible. A slow startup can also have no window; wait for current launches to finish before continuing. " +
                "Cleanup rechecks each process and skips any that now have a visible window. Unsaved background activity will be lost.",
                "Clean up background Roblox clients", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            var results = await Task.Run(() => candidates.Select(key => backgroundCleanup.Execute(key, lifetimeToken)).ToArray());
            if (!IsDisposed) actionResult.Text = $"Background cleanup: {results.Count(result => result == BackgroundCleanupResult.Stopped)} stopped; " +
                $"{results.Count(result => result == BackgroundCleanupResult.Skipped)} skipped; " +
                $"{results.Count(result => result is BackgroundCleanupResult.Unavailable or BackgroundCleanupResult.ExitUnconfirmed)} unavailable or exit unconfirmed.";
        }
        catch (OperationCanceledException) { }
        catch { if (!IsDisposed) actionResult.Text = "Background cleanup could not finish. Refresh and try again."; }
        finally
        {
            busy = false;
            if (!IsDisposed) { windows.Enabled = clients.Enabled = arrange.Enabled = true; SyncRefresh(); }
        }
    }

    internal async Task OpenArrangementAsync(ArrangementKind kind)
    {
        if (busy || IsDisposed || Application.OpenForms.Cast<Form>().Any(f => f.Modal)) return;
        busy = true; arrange.Enabled = false; SyncRefresh(); windows.Enabled = clients.Enabled = false;
        try
        {
            await refresh.Completion;
            if (lifetimeToken.IsCancellationRequested) return;
            string? monitor = null;
            try
            {
                var work = Screen.FromControl(this).WorkingArea;
                monitor = PlacementNative.Monitors().FirstOrDefault(m => m.Work == work)?.Id;
            }
            catch { }
            using var dialog = new ArrangementForm(arrangementChoices, layoutPreferences, kind, lastPreset, monitor);
            dialog.ShowDialog(this); lastPreset = dialog.SelectedPresetName;
        }
        catch { if (!IsDisposed) actionResult.Text = "Arrangement unavailable. Reopen the panel and try again."; }
        finally
        {
            busy = false;
            if (!IsDisposed) { arrange.Enabled = true; windows.Enabled = clients.Enabled = true; SyncRefresh(); }
        }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Control? active = ActiveControl;
        while (active is ContainerControl container && container.ActiveControl is not null) active = container.ActiveControl;
        if (!busy && active is not TextBoxBase && !Application.OpenForms.Cast<Form>().Any(f => f.Modal))
        {
            ArrangementKind? command = keyData switch
            {
                Keys.Control | Keys.Alt | Keys.D1 => ArrangementKind.SideBySide,
                Keys.Control | Keys.Alt | Keys.D2 => ArrangementKind.Grid,
                Keys.Control | Keys.Alt | Keys.R => ArrangementKind.Saved,
                Keys.Control | Keys.Alt | Keys.O => ArrangementKind.Recover,
                _ => null
            };
            if (command is ArrangementKind layout) { BeginInvoke(async () => await OpenArrangementAsync(layout)); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task ActAsync(InstanceAction action)
    {
        if (!fresh || busy || SelectedTarget is not InstanceTarget target || !InstancePolicy.Contains(snapshot, target)) return;
        busy = true; SyncRefresh(); windows.Enabled = clients.Enabled = false;
        try
        {
            await refresh.Completion; // Drain collection before confirmation/action.
            if (lifetimeToken.IsCancellationRequested) return;
            if (action == InstanceAction.Close)
            {
                var window = snapshot.Instances.Single(x => x.Key == target.Key).Windows.Single(x => x.Handle == target.Window);
                if (MessageBox.Show(this, $"Request this window to close? Gameplay may be interrupted.\n\n{Caption(window.Title)}\n" +
                    $"PID {target.Key.Id}, started {InstancePolicy.Started(target.Key):g}", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            }
            var result = await Task.Run(() => actions.Execute(target, action, lifetimeToken));
            if (IsDisposed) return;
            status.Text = result switch
            {
                InstanceActionResult.Focused => "Focus requested.",
                InstanceActionResult.FocusDenied => "Windows did not allow focus; select the window from the taskbar.",
                InstanceActionResult.CloseRequested => "Close requested. Roblox may prompt or keep running.",
                InstanceActionResult.CloseFailed => "Close was not confirmed: the window may be busy, closed, or access was denied. No retry was sent.",
                InstanceActionResult.Changed => "Instance exited or changed; select it again after refresh.",
                _ => "The action was unavailable with current permissions. Select the instance again after refresh."
            };
            actionResult.Text = status.Text;
        }
        catch (OperationCanceledException) { }
        catch { if (!IsDisposed) status.Text = "The action could not complete. Refresh and select the instance again."; }
        finally
        {
            busy = false;
            if (!IsDisposed) { windows.Enabled = clients.Enabled = true; SyncRefresh(); }
        }
    }
}
