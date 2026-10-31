using System.Diagnostics;

namespace RobloxLaunchHelper;

internal sealed class ActiveInstancesPage : WorkspacePage
{
    private readonly ListView clients = new WorkspaceListView() { View = View.Details, FullRowSelect = true, MultiSelect = false,
        HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Active Roblox clients" };
    private readonly ComboBox windows = new LayoutComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400,
        AccessibleName = "Selected client window" };
    private readonly StatusCard windowCount = new("Windows");
    private readonly StatusCard backgroundCount = new("Background");
    private readonly WorkspaceToggle integrationToggle = new() { Enabled = false, AutoCheck = false,
        AccessibleName = "Browser integration", AccessibleDescription = "Enable or disable browser integration." };
    private readonly StatusCard integrationCount;
    private readonly Label selectionTitle = WorkspaceDesign.Label("Select a Roblox window",14,true);
    private readonly SelectionCard selectionCard = new() { Dock = DockStyle.Fill, Padding = new(22,18,22,12),
        AccessibleName = "Selected window", MinimumSize = new(0,140), Margin = new(0,0,0,8) };
    internal event Action<string>? SummaryChanged;
    internal event Action? LaunchRequested;
    private readonly Panel emptyState = new() { Dock = DockStyle.Fill, Visible = false, Tag = "surface" };
    private readonly Label emptyMessage = WorkspaceDesign.Label("No clients running", 16, true);
    internal event Action? IntegrationToggleRequested;
    internal void SetIntegration(bool? enabled, bool changing = false)
    {
        integrationCount.SetValue(enabled is true ? "Enabled" : enabled is false ? "Disabled" : "Unknown");
        integrationToggle.Checked = enabled is true;
        integrationToggle.Busy = changing;
        integrationToggle.Enabled = enabled is not null && !changing;
    }
    private readonly Label status = new WorkspaceStatusLabel { AutoSize = false, Size = new(240,48), Text = "Loading active instances…" };
    private readonly Label actionResult = new() { AutoSize = true, Dock = DockStyle.Fill,
        Text = "Select a client and a window to focus or request close.", AccessibleName = "Window action result" };
    private readonly Button focus = new WorkspaceButton() { HideMnemonicUnderline = true, Text = "&Focus window", AutoSize = true, Enabled = false };
    private readonly Button close = new WorkspaceButton() { HideMnemonicUnderline = true, Text = "×  &Close window…", AutoSize = true, Enabled = false };
    private readonly InstanceRefreshController refresh;
    private readonly InstanceActions actions;
    private readonly BackgroundClientCleanup backgroundCleanup;
    private readonly Button cleanup = new WorkspaceButton() { Text = "Clean up background clients…", AutoSize = true, Enabled = false };
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private InstanceSnapshot snapshot = InstanceSnapshot.Failed;
    private bool busy, presenting, ready, fresh, disposedResources, selectionPending, choosingClient;
    private Action? sizeSelection;
    private readonly Button arrange = new WorkspaceButton() { HideMnemonicUnderline = true, Text = "&Arrange", AutoSize = true };
    private readonly List<ArrangementChoice> arrangementChoices = [];
    private readonly LayoutPreferences layoutPreferences;
    internal Func<ArrangementKind, Task>? NavigateLayout;
    internal readonly TableLayoutPanel StatusSummary = new() { Dock=DockStyle.Fill,ColumnCount=5,RowCount=1,Margin=new(0,0,0,20) };
    internal Task Completion => refresh.Completion;
    internal ActiveInstancesPage(bool collectOnShown = true, IInstancePlatform? platform = null,
        LayoutPreferences? preferences = null, Action? enableTray = null, Action? configureShortcuts = null)
    {
        integrationCount = new("Browser integration", integrationToggle);
        integrationToggle.Click += (_, _) => IntegrationToggleRequested?.Invoke();
        layoutPreferences = preferences ?? new LayoutPreferences();
        lifetimeToken = lifetime.Token;
        Text = "Roblox Multi Instance — Active instances";
        MinimumSize = new(0, 360);
        AutoScaleMode = AutoScaleMode.Dpi;
        platform ??= new InstancePlatform();
        using var current = Process.GetCurrentProcess();
        string sid = Program.Sid; int session = current.SessionId;
        actions = new(platform, sid, session);
        backgroundCleanup = new(pid => new InstanceProcess(pid, allowStop: true), platform.Trusted, sid, session);
        refresh = new(() => new InstanceCollector(platform, sid, session), Present);
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=new(0) };
        layout.ColumnStyles.Add(new(SizeType.Percent,100));
        layout.RowStyles.Add(new(SizeType.Absolute,120)); layout.RowStyles.Add(new(SizeType.Percent,100));
        layout.RowStyles.Add(new(SizeType.Absolute,140)); layout.RowStyles.Add(new(SizeType.AutoSize));
        StatusSummary.ColumnStyles.Add(new(SizeType.Percent,33.33f)); StatusSummary.ColumnStyles.Add(new(SizeType.Absolute,16));
        StatusSummary.ColumnStyles.Add(new(SizeType.Percent,33.33f)); StatusSummary.ColumnStyles.Add(new(SizeType.Absolute,16)); StatusSummary.ColumnStyles.Add(new(SizeType.Percent,33.34f));
        foreach(var card in new[]{windowCount,backgroundCount,integrationCount}) card.Dock=DockStyle.Fill;
        StatusSummary.Controls.Add(windowCount,0,0); StatusSummary.Controls.Add(backgroundCount,2,0); StatusSummary.Controls.Add(integrationCount,4,0);
        layout.Controls.Add(StatusSummary,0,0);
        clients.Tag="instances"; clients.Columns.Add("Window title",420); clients.Columns.Add("PID",0);
        clients.HeaderStyle=ColumnHeaderStyle.Nonclickable;
        clients.ColumnWidthChanging+=(_,e)=> { e.Cancel=true; e.NewWidth=clients.Columns[e.ColumnIndex].Width; };
        clients.Columns.Add("Process started (local)",0); clients.Columns.Add("CPU",110); clients.Columns.Add("Memory",160); clients.Columns.Add("State",160);
        var rowHeight=new ImageList { ImageSize=new(1,60) }; clients.SmallImageList=rowHeight; Disposed+=(_,_)=>rowHeight.Dispose();
        var tableContent = new Panel { Dock = DockStyle.Fill, Tag = "surface" };
        // Clip the native list and empty state inside the card's rounded border.
        tableContent.SizeChanged += (_, _) =>
        {
            if (tableContent.Width < 2 || tableContent.Height < 2) return;
            using var outline = WorkspaceCard.Rounded(new(0, 0, tableContent.Width, tableContent.Height), 7);
            var previous = tableContent.Region;
            tableContent.Region = new Region(outline);
            previous?.Dispose();
        };
        tableContent.Controls.Add(clients);
        var emptyStack = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Tag = "surface" };
        emptyStack.Controls.Add(emptyMessage);
        emptyStack.Controls.Add(WorkspaceDesign.Label("Launch Roblox to see your windows here.", 11));
        var emptyLaunch = new WorkspaceButton { Text = "Launch Roblox", Tag = "primary", AccessibleName = "Launch Roblox from empty instances" };
        emptyLaunch.Click += (_, _) => LaunchRequested?.Invoke(); emptyStack.Controls.Add(emptyLaunch);
        emptyState.Controls.Add(emptyStack); tableContent.Controls.Add(emptyState);
        emptyState.SizeChanged += (_, _) => emptyStack.Location = new(Math.Max(0, (emptyState.Width-emptyStack.Width)/2), Math.Max(0, (emptyState.Height-emptyStack.Height)/2));
        var tableCard=WorkspaceDesign.Card(tableContent,new(1)); tableCard.AccessibleName="Active instances table"; tableCard.Margin=new(0,0,0,20); layout.Controls.Add(tableCard,0,1);
        var selection=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new(0),Tag="surface" };
        selection.ColumnStyles.Add(new(SizeType.Percent,42)); selection.ColumnStyles.Add(new(SizeType.Percent,58));
        selection.RowStyles.Add(new(SizeType.AutoSize)); selection.RowStyles.Add(new(SizeType.AutoSize)); selection.RowStyles.Add(new(SizeType.AutoSize));
        actionResult.Font=new("Segoe UI",9); actionResult.Tag="muted"; actionResult.MaximumSize=new(460,0);
        var selectedInfo=WorkspaceDesign.Stack(selectionTitle,actionResult); selectedInfo.AutoSize=true; selectedInfo.AutoSizeMode=AutoSizeMode.GrowAndShrink;
        selectedInfo.Padding=new(58,0,0,0);
        selectedInfo.Paint+=(_,e)=>
        {
            var theme=ThemePalette.For(this);
            int size=LogicalToDeviceUnits(34);
            WorkspaceGlyph.Draw(e.Graphics,new(0,LogicalToDeviceUnits(6),size,size),"Roblox",theme.Text);
        };
        selection.Controls.Add(selectedInfo,0,0);
        var controls=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.RightToLeft,WrapContents=true,Tag="surface" };
        focus.Tag="primary"; controls.Controls.Add(close); controls.Controls.Add(focus); selection.Controls.Add(controls,1,0);
        windows.Width=360; windows.Margin=new(0,10,0,12); windows.Visible=false; selection.Controls.Add(windows,0,1); selection.SetColumnSpan(windows,2);
        var note=WorkspaceDesign.Label("Background clients are cleaned up after a one-minute grace period.",9); note.Tag="muted";
        selection.Controls.Add(note,0,2); selection.SetColumnSpan(note,2);
        selectionCard.Controls.Add(selection); layout.Controls.Add(selectionCard,0,2);
        bool sizingSelection = false;
        void SizeSelection()
        {
            if (sizingSelection) return;
            sizingSelection = true;
            try
            {
                int width = Math.Max(120, layout.ClientSize.Width - selectionCard.Padding.Horizontal);
                int textWidth = Math.Max(80, (int)(width * .42) - selectedInfo.Padding.Horizontal - 12);
                selectionTitle.MaximumSize = actionResult.MaximumSize = new(textWidth, 0);
                windows.Width = Math.Max(120, width - 12);
                // Reserve the same space for process details even when the empty prompt
                // wraps to fewer lines. Font.Height alone omits native label text padding.
                int detailsHeight = TextRenderer.MeasureText(
                    "PID 4294967295 · Started 12/31/2099 12:59 PM. Focus this window or request a graceful close.",
                    actionResult.Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak).Height;
                actionResult.MinimumSize = new(0, Math.Max(actionResult.Font.Height * 2 + 6, detailsHeight));
                int infoHeight = selectionTitle.GetPreferredSize(new(textWidth, 0)).Height + selectionTitle.Margin.Vertical +
                    actionResult.GetPreferredSize(new(textWidth, 0)).Height + actionResult.Margin.Vertical;
                int buttonsHeight = controls.GetPreferredSize(new(Math.Max(120, (int)(width * .58)), 0)).Height;
                int height = Math.Max(infoHeight, buttonsHeight) + note.GetPreferredSize(new(width, 0)).Height + note.Margin.Vertical +
                    selectionCard.Padding.Vertical + selection.Margin.Vertical;
                if (windows.Visible) height += windows.PreferredHeight + windows.Margin.Vertical;
                int desiredHeight = Math.Max(selectionCard.MinimumSize.Height, height) + selectionCard.Margin.Vertical;
                if (layout.RowStyles[2].Height != desiredHeight) layout.RowStyles[2].Height = desiredHeight;
            }
            finally { sizingSelection = false; }
        }
        sizeSelection = SizeSelection;
        layout.SizeChanged += (_, _) => SizeSelection();

        actionResult.TextChanged += (_, _) => { if (!choosingClient) SizeSelection(); };

        var utilityRow = new TableLayoutPanel { AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, Dock=DockStyle.Fill, ColumnCount=2, RowCount=1, Margin=new(0) };
        utilityRow.ColumnStyles.Add(new(SizeType.Percent,100)); utilityRow.ColumnStyles.Add(new(SizeType.AutoSize));
        utilityRow.RowStyles.Add(new(SizeType.AutoSize));
        var utilities=new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,WrapContents=true,Margin=new(0) };
        foreach (var button in new[] { arrange, cleanup })
        {
            button.Font=new("Segoe UI",11); button.MinimumSize=new(112,48); button.Margin=new(0,0,8,0);
            utilities.Controls.Add(button);
        }
        status.Font=new("Segoe UI",9); status.Tag="muted"; status.Margin=new(16,0,0,0); status.MaximumSize=new(240,0);
        // Keep the timestamp's column stable: automatic label wrapping otherwise feeds
        // back into the AutoSize column and moves/repaints the text during resizing.
        status.Anchor=AnchorStyles.Top | AnchorStyles.Right; status.TextAlign=ContentAlignment.MiddleRight;
        void SizeStatus() => status.Height = Math.Max(arrange.Height,
            status.GetPreferredSize(new(status.Width, 0)).Height);
        arrange.SizeChanged += (_, _) => SizeStatus();
        status.TextChanged += (_, _) => SizeStatus();
        SizeStatus();
        utilityRow.Controls.Add(utilities,0,0); utilityRow.Controls.Add(status,1,0);
        layout.Controls.Add(utilityRow,0,3); Controls.Add(layout);
        cleanup.Click+=async (_,_)=>await CleanupBackgroundAsync(); arrange.Click+=async (_,_)=>await OpenArrangementAsync(ArrangementKind.Grid);
        if(enableTray is not null) utilities.Controls.Add(WorkspaceControls.Button("Enable tray mode",enableTray));
        if(configureShortcuts is not null) utilities.Controls.Add(WorkspaceControls.Button("Shortcut settings",configureShortcuts));
        windows.DisplayMember = nameof(WindowChoice.Label);
        clients.SelectedIndexChanged += (_, _) =>
        {
            if (presenting) return;
            if (SelectedKey is not null)
            {
                selectionPending = false;
                ChooseClient();
            }
            else if (!selectionPending && IsHandleCreated)
            {
                // Row switches deselect the old item before selecting the new one.
                // Wait for that notification pair before showing an empty selection.
                selectionPending = true;
                BeginInvoke(() =>
                {
                    if (!selectionPending || IsDisposed) return;
                    selectionPending = false;
                    ChooseClient();
                });
            }
        };
        windows.SelectedIndexChanged += (_, _) => EnableActions();
        focus.Click += async (_, _) => await ActAsync(InstanceAction.Focus);
        close.Click += async (_, _) => await ActAsync(InstanceAction.Close);
        Load += (_, _) => { ready = collectOnShown; SyncRefresh(); };
        VisibleChanged += (_, _) => SyncRefresh(); Resize += (_, _) => SyncRefresh();

    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposedResources)
        {
            disposedResources = true;
            ready = false;
            lifetime.Cancel(); refresh.Dispose();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
    private void SyncRefresh()
    {
        bool active = ready && !busy && Visible && FindForm()?.WindowState != FormWindowState.Minimized &&
            !IsDisposed;
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
            windowCount.SetValue("Stale"); backgroundCount.SetValue("Stale");
            SummaryChanged?.Invoke("Refresh unavailable · displayed instances are stale");
            emptyMessage.Text = "Instances unavailable";
            emptyState.Visible = clients.Items.Count == 0; if (emptyState.Visible) emptyState.BringToFront();
            actionResult.Text = "Refresh unavailable. Wait for a successful refresh before using window actions.";
            status.Text = "Refresh unavailable. Displayed values are stale; actions are disabled.";
            EnableActions(); return;
        }
        presenting = true;
        clients.BeginUpdate();
        try
        {
            // Preserve native row identity and selection on routine samples instead of
            // clearing the list and rebuilding the selected-window panel every two seconds.
            var existing = clients.Items.Cast<ListViewItem>().ToDictionary(item => (ProcessKey)item.Tag!);
            var liveKeys = result.Instances.Select(client => client.Key).ToHashSet();
            foreach (var item in existing.Values.Where(item => !liveKeys.Contains((ProcessKey)item.Tag!))) clients.Items.Remove(item);
            int position = 0;
            foreach (var client in result.Instances)
            {
                string[] values = [client.Username is not null ? "Roblox - "+client.Username : client.Windows.Length == 0 ? "No visible window" :
                    string.Join(" | ", client.Windows.Select(w => Caption(w.Title))), client.Key.Id.ToString(),
                    InstancePolicy.Started(client.Key).ToString("g"), client.CpuPercent?.ToString("F1") ??
                        (client.CpuAvailable ? "Sampling…" : "Unavailable"),
                    client.WorkingSet is ulong memory ? (memory / 1048576d).ToString("F0")+" MiB" : "Unavailable",
                    client.Windows.Length == 0 ? "Background / starting" : "Open"];
                if (!existing.TryGetValue(client.Key, out var row))
                {
                    row = new ListViewItem(values) { Tag = client.Key }; clients.Items.Insert(position, row);
                }
                else
                {
                    if (row.Index != position) { clients.Items.Remove(row); clients.Items.Insert(position, row); }
                    for (int i = 0; i < values.Length; i++)
                        if (row.SubItems[i].Text != values[i]) row.SubItems[i].Text = values[i];
                }
                row.Selected = client.Key == selected;
                position++;
            }
        }
        finally { clients.EndUpdate(); presenting = false; }
        ChooseClient(target, false);
        emptyMessage.Text = "No clients running";
        emptyState.Visible = result.Instances.Length == 0;
        if (emptyState.Visible) emptyState.BringToFront();
        int windowed = result.Instances.Count(client => client.Windows.Length > 0);
        int background = result.Instances.Length - windowed;
        windowCount.SetValue(result.Instances.Sum(client => client.Windows.Length).ToString()); backgroundCount.SetValue(background.ToString());
        SummaryChanged?.Invoke($"{windowed} clients with windows · {background} background");
        status.Text = (result.Instances.Length==0 ? "No clients running. Select Launch Roblox to get started. " : "") +
            $"Last refreshed {DateTime.Now:T}." + (result.Unavailable>0?$" {result.Unavailable} candidates unavailable or unverified.":"");
    }
    private void ChooseClient(InstanceTarget? preserve = null, bool autoSelect = true)
    {
        choosingClient = true;
        var layout = selectionCard.Parent;
        layout?.SuspendLayout();
        selectionCard.SuspendLayout();
        try
        {
        var client = snapshot.Instances.FirstOrDefault(x => x.Key == SelectedKey);
        WindowChoice[] choices = snapshot.Available && client is not null
            ? client.Windows.Select((window, index) => new WindowChoice(window, index + 1, client.Username)).ToArray() : [];
        if (!windows.Items.Cast<WindowChoice>().SequenceEqual(choices))
        {
            windows.BeginUpdate();
            try { windows.Items.Clear(); windows.Items.AddRange(choices); }
            finally { windows.EndUpdate(); }
        }
        if (snapshot.Available && client is not null)
        {
            int index = preserve is InstanceTarget target && target.Key == client.Key
                ? Array.FindIndex(client.Windows, w => w.Handle == target.Window) : -1;
            // After a refresh, a vanished selected window must not silently retarget.
            if (index >= 0) windows.SelectedIndex = index;
            else if (autoSelect && preserve is null && client.Windows.Length == 1) windows.SelectedIndex = 0;
            else if (!autoSelect) windows.SelectedIndex = -1;
        }
        selectionTitle.Text=client is null?"Select a Roblox window":client.Username is not null?"Roblox - "+client.Username:client.Windows.Length==0?"Background client":Caption(client.Windows[0].Title);
        windows.Visible=windows.Items.Count>1;
        actionResult.Text = client is null ? "Select a client to view its process details and window actions." :
            $"PID {client.Key.Id} · Started {InstancePolicy.Started(client.Key):g}. " +
            (client.Windows.Length == 0 ? "No visible window. Background cleanup becomes eligible after one minute; a client may still be starting." :
            client.Windows.Length > 1 ? "Choose a window below to focus or close." :
            SelectedTarget is null ? "Reselect this client to choose its current window." : "Focus this window or request a graceful close.");
        EnableActions();
        }
        finally
        {
            choosingClient = false;
            sizeSelection?.Invoke();
            selectionCard.ResumeLayout(true);
            layout?.ResumeLayout(true);
        }
    }
    private void EnableActions()
    {
        bool available = fresh && !busy && Visible && FindForm()?.WindowState != FormWindowState.Minimized &&
            !IsDisposed;
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
            if (!await WorkspacePrompt.ConfirmAsync(this, $"Force stop {candidates.Length} Roblox background process(es) without visible windows?\n\n" +
                "Only processes older than one minute are eligible. A slow startup can also have no window; wait for current launches to finish before continuing. " +
                "Cleanup rechecks each process and skips any that now have a visible window. Unsaved background activity will be lost.",
                "Clean up background Roblox clients")) return;
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
        if (busy || IsDisposed) return;
        refresh.SetActive(false);
        await refresh.Completion;
        if (!IsDisposed && NavigateLayout is not null) await NavigateLayout(kind);
    }
    internal void HostVisibilityChanged() => SyncRefresh();
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
                if (!await WorkspacePrompt.ConfirmAsync(this, $"Request this window to close? Gameplay may be interrupted.\n\n{Caption(window.Title)}\n" +
                    $"PID {target.Key.Id}, started {InstancePolicy.Started(target.Key):g}", "Close window")) return;
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
