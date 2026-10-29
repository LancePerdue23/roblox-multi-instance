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
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private InstanceSnapshot snapshot = InstanceSnapshot.Failed;
    private bool busy, presenting, ready, fresh, disposedResources;
    internal Task Completion => refresh.Completion;
    internal ActiveInstancesForm(bool collectOnShown = true, IInstancePlatform? platform = null)
    {
        lifetimeToken = lifetime.Token;
        Text = "Roblox Multi Instance — Active instances";
        Size = new(1120, 520); MinimumSize = new(800, 380);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        platform ??= new InstancePlatform();
        using var current = Process.GetCurrentProcess();
        string sid = Program.Sid; int session = current.SessionId;
        actions = new(platform, sid, session);
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
        layout.Controls.Add(status, 0, 0); layout.Controls.Add(clients, 0, 1); layout.Controls.Add(controls, 0, 2);
        layout.Controls.Add(actionResult, 0, 3);
        layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill,
            Text = "Window titles are supplied by Roblox. They do not identify or verify accounts.\n" +
                "Working set includes shared resident pages. Close requests may interrupt gameplay." }, 0, 4);
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
        if (!active) { fresh = false; focus.Enabled = close.Enabled = false; }
    }
    private ProcessKey? SelectedKey => clients.SelectedItems.Count == 1 ? clients.SelectedItems[0].Tag as ProcessKey? : null;
    internal InstanceTarget? SelectedTarget => windows.SelectedItem is WindowChoice choice && SelectedKey is ProcessKey key
        ? new(key, choice.Window.Handle) : null;
    private sealed record WindowChoice(InstanceWindow Window, int Number)
    {
        public string Label => $"Window {Number}: {Caption(Window.Title)}";
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
                var row = new ListViewItem([client.Windows.Length == 0 ? "No window yet" :
                    string.Join(" | ", client.Windows.Select(w => Caption(w.Title))), client.Key.Id.ToString(),
                    InstancePolicy.Started(client.Key).ToString("g"), client.CpuPercent?.ToString("F1") ??
                        (client.CpuAvailable ? "Sampling…" : "Unavailable"),
                    client.WorkingSet is ulong memory ? (memory / 1048576d).ToString("F1") : "Unavailable",
                    client.Windows.Length == 0 ? "No window yet" : "Running"]);
                row.Tag = client.Key; clients.Items.Add(row);
                row.Selected = client.Key == selected;
            }
        }
        finally { clients.EndUpdate(); presenting = false; }
        ChooseClient(target, false);
        status.Text = (result.Instances.Length == 0 ? "No active instances." : $"{result.Instances.Length} active client(s).") +
            $" Updated {DateTime.Now:T}." + (result.Unavailable > 0 ? $" {result.Unavailable} candidate(s) unavailable or unverified." : "");
    }
    private void ChooseClient(InstanceTarget? preserve = null, bool autoSelect = true)
    {
        windows.Items.Clear();
        var client = snapshot.Instances.FirstOrDefault(x => x.Key == SelectedKey);
        if (snapshot.Available && client is not null)
        {
            for (int i = 0; i < client.Windows.Length; i++) windows.Items.Add(new WindowChoice(client.Windows[i], i + 1));
            int index = preserve is InstanceTarget target && target.Key == client.Key
                ? Array.FindIndex(client.Windows, w => w.Handle == target.Window) : -1;
            // After a refresh, a vanished selected window must not silently retarget.
            if (index >= 0) windows.SelectedIndex = index;
            else if (autoSelect && preserve is null && client.Windows.Length == 1) windows.SelectedIndex = 0;
        }
        EnableActions();
    }
    private void EnableActions() => focus.Enabled = close.Enabled = fresh && !busy && Visible &&
        WindowState != FormWindowState.Minimized && Owner?.WindowState != FormWindowState.Minimized &&
        SelectedTarget is InstanceTarget target && InstancePolicy.Contains(snapshot, target);

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
