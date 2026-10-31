namespace RobloxLaunchHelper;

internal sealed class ArrangementChoice(InstanceTarget target, string caption, string monitor, bool selected)
{
    internal InstanceTarget Target = target;
    internal string Caption = caption, Monitor = monitor;
    internal bool Selected = selected;
}

internal sealed class LayoutPreferences
{
    internal readonly LayoutStore Store;
    internal LayoutSettings Settings = LayoutSettings.Default;
    internal bool Available = true;
    internal event Action? Changed;
    internal LayoutPreferences(LayoutStore? store = null)
    {
        Store = store ?? new LayoutStore();
        try { Settings = Store.Load(); } catch { Available = false; }
    }
    internal void Save(LayoutSettings settings)
    {
        if (!Available) throw new InvalidDataException();
        Store.Save(settings); Settings = settings; Changed?.Invoke();
    }
    internal void Reset() { Store.Save(LayoutSettings.Default); Settings = LayoutSettings.Default; Available = true; Changed?.Invoke(); }
}

internal sealed class ArrangementPage : WorkspacePage
{
    private readonly LayoutWindowGrid list = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeColumns = false, AllowUserToResizeRows = false,
        ScrollBars = ScrollBars.Vertical, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AccessibleName = "Ordered window slots and monitor assignments" };
    private readonly ComboBox kind = new LayoutComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, AccessibleName = "Layout" };
    private readonly ComboBox presets = new LayoutComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Saved preset" };
    private readonly CheckedListBox monitorsList = new() { Width = 230, Height = 65, CheckOnClick = true, AccessibleName = "Target monitors" };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new(1000, 0) };
    private readonly LayoutPreview preview = new() { Dock = DockStyle.Fill, MinimumSize = new(300, 120) };
    private readonly Button apply = new WorkspaceButton() { Text = "&Apply", AutoSize = true, Enabled = false };
    private readonly FlowLayoutPanel controls = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private readonly List<ArrangementChoice> choices;
    private readonly LayoutPreferences preferences;
    private readonly IPlacementPlatform platform;
    private readonly CancellationTokenSource cancellation = new();
    private readonly System.Windows.Forms.Timer previewDelay = new() { Interval = 180 };
    private readonly ToolTip previewDetails = new() { AutoPopDelay = 20000 };
    private readonly string? initialMonitor;
    private readonly Func<CancellationToken, InstanceSnapshot> discover;
    private LayoutMonitor[] monitors = [];
    private Dictionary<InstanceTarget, LayoutWindow> live = [];
    private PlacementPlan? plan;
    private bool loading, busy, applying, initial = true;
    internal string? SelectedPresetName => (presets.SelectedItem as WindowPreset)?.Name;
    internal ArrangementPage(List<ArrangementChoice> choices, LayoutPreferences preferences, ArrangementKind layout,
        string? presetName, string? initialMonitor, IPlacementPlatform? platform = null,
        Func<CancellationToken, InstanceSnapshot>? discover = null)
    {
        this.choices = choices; this.preferences = preferences; this.initialMonitor = initialMonitor;
        this.platform = platform ?? new PlacementPlatform();
        this.discover = discover ?? PlacementPlatform.Discover;
        Text = "Layouts"; MinimumSize = new(0, 480);
        AutoScaleMode = AutoScaleMode.Dpi;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,Margin=new(0) };
        root.ColumnStyles.Add(new(SizeType.Percent,40)); root.ColumnStyles.Add(new(SizeType.Absolute,18)); root.ColumnStyles.Add(new(SizeType.Percent,60));
        root.RowStyles.Add(new(SizeType.Percent,100)); root.RowStyles.Add(new(SizeType.Absolute,88));
        var editor=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,Tag="surface" };
        for(int i=0;i<9;i++) editor.RowStyles.Add(new(i==5?SizeType.Absolute:SizeType.AutoSize,i==5?190:0));
        editor.Controls.Add(WorkspaceDesign.Label("Layout",16,true),0,0);
        kind.Items.AddRange([ArrangementKind.SideBySide,ArrangementKind.Grid,ArrangementKind.Saved,ArrangementKind.Recover]);
        kind.FormattingEnabled=true; kind.Format+=(_,e)=>e.Value=e.ListItem switch { ArrangementKind.SideBySide=>"Side-by-side",ArrangementKind.Saved=>"Restore preset",ArrangementKind.Recover=>"Recover off-screen",_=>"Grid" };
        kind.SelectedItem=layout;
        var choicesBar=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,AutoSize=true,Margin=new(0,0,0,16),Tag="surface" };
        choicesBar.ColumnStyles.Add(new(SizeType.Percent,50)); choicesBar.ColumnStyles.Add(new(SizeType.Percent,50));
        var side=new WorkspaceButton { Text="Side-by-side",Dock=DockStyle.Fill,AutoSize=false,Height=44,Margin=new(0,0,4,0) };
        var gridButton=new WorkspaceButton { Text="Grid",Dock=DockStyle.Fill,AutoSize=false,Height=44,Margin=new(4,0,0,0) };
        side.Click+=(_,_)=>kind.SelectedItem=ArrangementKind.SideBySide; gridButton.Click+=(_,_)=>kind.SelectedItem=ArrangementKind.Grid;
        void MarkChoice() { side.ActiveChoice=kind.SelectedItem is ArrangementKind.SideBySide; gridButton.ActiveChoice=kind.SelectedItem is ArrangementKind.Grid; side.Invalidate(); gridButton.Invalidate(); }
        kind.SelectedIndexChanged+=(_,_)=>MarkChoice(); MarkChoice(); choicesBar.Controls.Add(side,0,0); choicesBar.Controls.Add(gridButton,1,0); editor.Controls.Add(choicesBar,0,1);
        editor.Controls.Add(WorkspaceDesign.Label("Saved presets",11),0,2); presets.Dock=DockStyle.Top; presets.Margin=new(0,0,0,18); editor.Controls.Add(presets,0,3);
        var windowHeading=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,Tag="surface",WrapContents=false }; windowHeading.Controls.Add(WorkspaceDesign.Label("Windows",16,true)); editor.Controls.Add(windowHeading,0,4); list.Margin=new(0,0,0,16); editor.Controls.Add(list,0,5);
        var windowActions=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Tag="surface" };
        var refreshWindows=WorkspaceControls.Button("Refresh windows",async ()=> { if(!busy) await RefreshAsync(); });
        var distribute=WorkspaceControls.Button("Distribute",()=> { if(!busy) Distribute(); });
        windowActions.Controls.Add(refreshWindows); windowActions.Controls.Add(distribute); editor.Controls.Add(windowActions,0,6);
        var advanced=new TableLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=3,Tag="surface",Margin=new(0,12,0,0) };
        advanced.ColumnStyles.Add(new(SizeType.Percent,100));
        for (int i=0;i<3;i++) advanced.RowStyles.Add(new(SizeType.AutoSize));
        kind.Dock=DockStyle.Top; kind.Font=new("Segoe UI",11); kind.Margin=new(4,0,4,12);
        monitorsList.Dock=DockStyle.Top; monitorsList.Font=new("Segoe UI",11); monitorsList.Height=64;
        monitorsList.BorderStyle=BorderStyle.None; monitorsList.Margin=new(4,0,4,12);
        var presetActions=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Tag="surface",Margin=new(0) };
        advanced.Controls.Add(kind,0,0); advanced.Controls.Add(monitorsList,0,1); advanced.Controls.Add(presetActions,0,2);
        advanced.Visible=false; editor.Controls.Add(advanced,0,8);
        var moreOptions=WorkspaceControls.Button("More options",()=>advanced.Visible=!advanced.Visible);
        windowActions.Controls.Add(moreOptions);
        list.MinimumSize=new(0,174);
        editor.Dock=DockStyle.Top; editor.AutoSize=true; editor.AutoSizeMode=AutoSizeMode.GrowAndShrink;
        editor.MinimumSize=new(0,520);
        // Keep the rounded frame outside the scrolling surface. Native scrolling
        // otherwise copies pieces of its border into the middle of the form.
        var editorViewport=new WorkspaceScrollViewport(editor) { Dock=DockStyle.Fill };
        var editorCard=WorkspaceDesign.Card(editorViewport,new(22)); root.Controls.Add(editorCard,0,0);
        void SizeAdvancedRows()
        {
            advanced.RowStyles[0].SizeType=SizeType.Absolute;
            advanced.RowStyles[0].Height=kind.Height+kind.Margin.Vertical;
            advanced.RowStyles[1].SizeType=SizeType.Absolute;
            advanced.RowStyles[1].Height=monitorsList.Height+monitorsList.Margin.Vertical;
        }
        kind.SizeChanged+=(_,_)=>SizeAdvancedRows(); monitorsList.SizeChanged+=(_,_)=>SizeAdvancedRows(); SizeAdvancedRows();
        var previewArea=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Tag="surface" };
        previewArea.RowStyles.Add(new(SizeType.AutoSize)); previewArea.RowStyles.Add(new(SizeType.Percent,100)); previewArea.RowStyles.Add(new(SizeType.AutoSize));
        previewArea.Controls.Add(WorkspaceDesign.Label("Preview",18,true),0,0); previewArea.Controls.Add(preview,0,1);
        status.Font=new("Segoe UI",10); status.Tag="muted"; status.Margin=new(8,20,8,12); previewArea.Controls.Add(status,0,2);
        root.Controls.Add(WorkspaceDesign.Card(previewArea,new(24)),2,0);
        controls.Margin=new(0,20,0,0); controls.FlowDirection=FlowDirection.RightToLeft; root.Controls.Add(controls,0,1); root.SetColumnSpan(controls,3);
        preview.AccessibleDescription="Slots match checked-window order. Apply may briefly focus fullscreen windows to use F11. Keep modifier keys released.";
        Controls.Add(root);
        controls.EnabledChanged+=(_,_)=> { windowActions.Enabled=advanced.Enabled=controls.Enabled; };        list.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", FillWeight = 10 });
        list.Columns.Add(new DataGridViewTextBoxColumn { Name = "Window", ReadOnly = true, FillWeight = 50 });
        list.Columns.Add(new DataGridViewTextBoxColumn { Name = "Monitor", ReadOnly = true, FillWeight = 36 });
        list.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", ReadOnly = true, FillWeight = 14 });
        Add("Move up", () => MoveChoice(-1)); Add("Move down", () => MoveChoice(1));

        apply.Tag="primary"; controls.Controls.Add(apply);
        Add("Save preset…", async () => await SaveAsync());
        Add("Rename preset…", RenameAsync); Add("Delete preset…", DeleteAsync);
        Add("Reset saved settings…", ResetAsync);
        var cancel = new WorkspaceButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var footer=new TableLayoutPanel { Dock=DockStyle.Fill,AutoSize=false,ColumnCount=2,Margin=new(0,16,0,0) }; footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.ColumnStyles.Add(new(SizeType.Percent,100));
        root.SetColumnSpan(controls,1); root.Controls.Remove(controls); root.Controls.Add(footer,0,1); root.SetColumnSpan(footer,3); footer.Controls.Add(cancel,0,0); footer.Controls.Add(controls,1,0); footer.SetColumnSpan(controls,1); footer.RowCount=1; footer.RowStyles.Add(new(SizeType.Percent,100)); controls.Margin=new(0); controls.AutoSize=false; controls.WrapContents=false;
        foreach(var button in controls.Controls.OfType<Button>().ToArray())
        {
            if(button.Text is "Move up" or "Move down") { button.AccessibleName=button.Text; button.AutoSize=false; button.Size=new(38,36); button.Padding=new(2); button.Margin=new(4,0,0,0); windowHeading.Controls.Add(button); }
            else if(button!=apply && button.Text!="Save preset…") presetActions.Controls.Add(button);
        }
        controls.EnabledChanged+=(_,_)=>
        {
            cancel.Enabled=moreOptions.Enabled=controls.Enabled;
            foreach(var button in windowHeading.Controls.OfType<Button>()) button.Enabled=controls.Enabled;
        };
        cancel.Click += (_, _) => { InvalidatePlan(); status.Text = "Preview canceled. No windows moved."; };
        apply.Click += async (_, _) => await ApplyAsync();
        list.Columns[3].Visible=false; list.Columns[1].FillWeight=58; list.Columns[2].FillWeight=34;
        list.ColumnHeadersVisible=false; list.CellBorderStyle=DataGridViewCellBorderStyle.None; list.RowTemplate.Height=58;
        list.Font=new("Segoe UI",10);
        list.CurrentCellDirtyStateChanged += (_, _) => { if (list.IsCurrentCellDirty) list.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        list.CellValueChanged += (_, e) =>
        {
            if (loading || e.RowIndex < 0 || e.RowIndex >= choices.Count) return;
            var row = list.Rows[e.RowIndex]; var choice = choices[e.RowIndex];
            choice.Selected = row.Cells[0].Value is true;
            choice.Monitor = monitors.FirstOrDefault(m => m.Label == row.Cells[2].Value as string)?.Id ?? choice.Monitor;
            loading = true;
            try
            {
                int monitorIndex = Array.FindIndex(monitors, m => m.Id == choice.Monitor);
                if (monitorIndex >= 0 && choice.Selected) monitorsList.SetItemChecked(monitorIndex, true);
            }
            finally { loading = false; }
            InvalidatePlan();
        };
        list.DataError += (_, e) => { e.ThrowException = false; };
        kind.SelectedIndexChanged += (_, _) => InvalidatePlan(); presets.SelectedIndexChanged += (_, _) => InvalidatePlan();
        presets.SelectionChangeCommitted+=(_,_)=>kind.SelectedItem=ArrangementKind.Saved;
        monitorsList.ItemCheck += (_, _) => { if (!loading) BeginInvoke(() => { if (!IsDisposed) { Distribute(); } }); };
        previewDelay.Tick += async (_, _) =>
        {
            if (busy) return;
            previewDelay.Stop();
            await RefreshPreviewAsync();
        };
        ReloadPresets(presetName);
        Load += async (_, _) => await RefreshAsync();
    }
    internal bool Busy => busy;
    internal bool PreventNavigation => applying;
    internal void DisplaysChanged() { if (!IsDisposed && !loading) InvalidatePlan(); }
    internal void SelectKind(ArrangementKind value) { kind.SelectedItem = value; }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { previewDelay.Dispose(); previewDetails.Dispose(); cancellation.Cancel(); }
        base.Dispose(disposing);
    }
    private void Add(string text, Action action)
    {
        var button = new WorkspaceButton { Text = text, AutoSize = true };
        button.Click += (_, _) => { if (!busy) action(); }; controls.Controls.Add(button);
    }
    private void Add(string text, Func<Task> action)
    {
        var button = new WorkspaceButton { Text = text, AutoSize = true };
        button.Click += async (_, _) => { if (!busy) await action(); }; controls.Controls.Add(button);
    }
    private void InvalidatePlan(bool refresh = true)
    {
        plan = null; preview.Plan = null; preview.Invalidate(); apply.Enabled = false;
        status.Text = "Updating preview…";
        if (refresh && !IsDisposed) { previewDelay.Stop(); previewDelay.Start(); }
    }
    private void SetBusy(bool value)
    {
        busy = value; if(IsDisposed)return; controls.Enabled = list.Enabled = kind.Enabled = presets.Enabled = monitorsList.Enabled = !value;

    }
    private async Task RefreshAsync()
    {
        if (busy) return;
        previewDelay.Stop(); SetBusy(true); InvalidatePlan(false);
        try
        {
            var selectedMonitors = monitorsList.CheckedIndices.Cast<int>().Where(i => i < monitors.Length).Select(i => monitors[i].Id).ToHashSet();
            var screens = await Task.Run(() => platform.Monitors(), cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            preview.Monitors = screens; preview.Invalidate();
            var result = await Task.Run(() =>
            {
                var snapshot = discover(cancellation.Token);
                var rows = new List<LayoutWindow>();
                foreach (var client in snapshot.Instances)
                    foreach (var window in client.Windows.OrderBy(w => w.Handle))
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        try
                        {
                            using var session = platform.Open(new(client.Key, window.Handle));
                            cancellation.Token.ThrowIfCancellationRequested();
                            var value = session.Read();
                            rows.Add(value with { Caption = Clean(window.Title) });
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { /* Exited between discovery and fresh handle validation. */ }
                    }
                return (screens, rows);
            }, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            monitors = result.screens; live = result.rows.ToDictionary(w => w.Target);
            string defaultId = monitors.FirstOrDefault(m => m.Id == initialMonitor)?.Id ?? monitors.First(m => m.Primary).Id;
            bool firstVisit = initial && choices.Count == 0;
            choices.RemoveAll(c => !live.ContainsKey(c.Target));
            foreach (var w in result.rows)
                if (!choices.Any(c => c.Target == w.Target)) choices.Add(new(w.Target, w.Caption, defaultId, firstVisit));
            foreach (var c in choices) { c.Caption = live[c.Target].Caption; if (!monitors.Any(m => m.Id == c.Monitor)) c.Monitor = defaultId; }
            loading = true;
            try
            {
                monitorsList.Items.Clear();
                foreach (var m in monitors) monitorsList.Items.Add(m.Label, selectedMonitors.Count == 0 ? m.Id == defaultId : selectedMonitors.Contains(m.Id));
            }
            finally { loading = false; }
            initial = false; list.EmptyText="No windows available."; RenderRows();
            status.Text = preferences.Available ? "Select windows to arrange." :
                "Saved settings are corrupt, unsupported, or inaccessible. Presets cannot be changed until you explicitly reset settings.";
        }
        catch (OperationCanceledException) { }
        catch { list.EmptyText="Windows unavailable. Refresh to try again."; status.Text = "Windows or display information is unavailable. Refresh to try again."; }
        finally { SetBusy(false); }
        if (!IsDisposed && !cancellation.IsCancellationRequested) BuildPreview();
    }
    private void RenderRows()
    {
        var current = list.CurrentRow?.Index;
        int column = list.CurrentCell?.ColumnIndex ?? 1;
        int firstVisible = list.FirstDisplayedScrollingRowIndex;
        loading = true;
        try
        {
            list.Rows.Clear();
            list.MonitorOptions=monitors.Select(m=>m.Label).ToArray();
            foreach (var c in choices)
            {
                var w = live[c.Target];
                int i = list.Rows.Add(c.Selected, c.Caption,
                    monitors.First(m => m.Id == c.Monitor).Label, w.NeedsWindowed && w.Movable ? "Ready — will exit fullscreen" : w.Movable ? "Ready" : w.Reason ?? "Unavailable");
                list.Rows[i].Height=58; list.Rows[i].Cells[1].ToolTipText=$"PID {c.Target.Key.Id}, started {InstancePolicy.Started(c.Target.Key):g}";
            list.Rows[i].Cells[0].ToolTipText = w.Movable ? "Include this window" :
                    "You can select this window. Preview rechecks its state; unavailable windows are reported individually.";
            }
            if (current is int index && index < list.Rows.Count) list.CurrentCell = list.Rows[index].Cells[column];
            if (firstVisible >= 0 && firstVisible < list.Rows.Count) list.FirstDisplayedScrollingRowIndex = firstVisible;
        }
        finally { loading = false; }
    }
    private void MoveChoice(int delta)
    {
        int index = list.CurrentRow?.Index ?? -1, next = index + delta;
        if (index < 0 || next < 0 || next >= choices.Count) return;
        (choices[index], choices[next]) = (choices[next], choices[index]);
        RenderRows(); list.CurrentCell = list.Rows[next].Cells[1]; InvalidatePlan();
    }
    private void Distribute()
    {
        var selected = monitorsList.CheckedIndices.Cast<int>().Select(i => monitors[i]).ToArray();
        if (selected.Length == 0) { InvalidatePlan(); status.Text = "Select at least one monitor."; return; }
        int n = 0;
        foreach (var c in choices.Where(c => c.Selected)) c.Monitor = selected[n++ % selected.Length].Id;
        RenderRows(); InvalidatePlan();
    }
    private LayoutWindow[] CheckedWindows() => choices.Where(c => c.Selected).Select(c => live[c.Target]).ToArray();
    internal async Task RefreshPreviewAsync()
    {
        if (busy || monitors.Length == 0) return;
        list.EndEdit();
        var selected = CheckedWindows();
        previewDelay.Stop(); SetBusy(true); InvalidatePlan(false);
        try
        {
            var refreshed = await Task.Run(() => selected.Select(window =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    using var session = platform.Open(window.Target);
                    cancellation.Token.ThrowIfCancellationRequested();
                    return session.Read() with { Caption = window.Caption };
                }
                catch (OperationCanceledException) { throw; }
                catch { return window with { Movable = false, Reason = "Exited or unavailable" }; }
            }).ToArray(), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            foreach (var window in refreshed) live[window.Target] = window;
            RenderRows();
        }
        catch (OperationCanceledException) { return; }
        finally { SetBusy(false); }
        if (!IsDisposed && !cancellation.IsCancellationRequested) BuildPreview();
    }
    private void BuildPreview()
    {
        if (busy || monitors.Length == 0) return;
        try
        {
            if (!PlacementCoordinator.SameDisplays(monitors, platform.Monitors())) { InvalidatePlan(false); status.Text = "Displays changed. Refresh windows and preview again."; return; }
            var checkedWindows = CheckedWindows();
            if (checkedWindows.Length == 0) { InvalidatePlan(false); status.Text = "Check at least one window."; return; }
            var layout = (ArrangementKind)kind.SelectedItem!;
            var skipped = checkedWindows.Where(w => !w.Movable).ToArray();
            string skippedMessage = string.Join(" ", skipped.Select(w => $"Slot {Array.IndexOf(checkedWindows, w) + 1} skipped: {w.Reason}."));
            if (layout == ArrangementKind.Saved && skipped.Length > 0)
            { InvalidatePlan(false); status.Text = skippedMessage + " All saved-position slots must be ready before restoring a preset."; return; }
            var selected = checkedWindows.Where(w => w.Movable).ToArray();
            if (selected.Length == 0) { InvalidatePlan(false); status.Text = skippedMessage; return; }
            if (layout is ArrangementKind.SideBySide or ArrangementKind.Grid && monitorsList.CheckedItems.Count == 0)
            { InvalidatePlan(false); status.Text = "Select at least one monitor."; return; }
            plan = layout switch
            {
                ArrangementKind.Saved when presets.SelectedItem is WindowPreset preset => LayoutPlanner.Restore(monitors, selected, preset),
                ArrangementKind.Saved => new(monitors, [], [], "Select a saved preset."),
                ArrangementKind.Recover => LayoutPlanner.Recover(monitors, selected),
                _ => LayoutPlanner.Arrange(monitors, selected, choices.Where(c => c.Selected && live[c.Target].Movable).Select(c => c.Monitor).ToArray(), layout)
            };
            plan = plan with
            {
                Placements = plan.Placements.Select(p => p with { Slot = Array.FindIndex(checkedWindows, w => w.Target == p.Target) + 1 }).ToArray(),
                Notes = skipped.Length == 0 ? plan.Notes : [.. plan.Notes, skippedMessage]
            };
            int fullscreen = selected.Count(w => w.NeedsWindowed);
            if (fullscreen > 0) plan = plan with { Notes = [.. plan.Notes,
                $"Apply will briefly focus {fullscreen} fullscreen client(s), use F11, verify windowed mode, and return focus here. Release modifier keys during Apply. A client that does not respond is reported individually."] };
            preview.Plan = plan; preview.Invalidate(); apply.Enabled = plan.Valid;
            preview.AccessibleDescription = string.Join("; ", plan.Placements.Select(p => $"Slot {p.Slot}: X {p.Bounds.X}, Y {p.Bounds.Y}, width {p.Bounds.Width}, height {p.Bounds.Height} physical pixels"));
            string details = string.Join(" ", plan.Notes);
            previewDetails.SetToolTip(status, details); previewDetails.SetToolTip(preview, details);
            status.AccessibleDescription = details;
            status.Text = plan.Error ?? $"{plan.Placements.Length} window(s) ready" +
                (skipped.Length > 0 ? $" · {skipped.Length} skipped" : "") +
                (fullscreen > 0 ? $" · {fullscreen} will exit fullscreen" : "") + ".";
        }
        catch { InvalidatePlan(false); status.Text = "Preview unavailable. Refresh windows and try again."; }
    }
    private async Task ApplyAsync()
    {
        if (busy || plan is not { Valid: true } captured) return;
        applying=true; SetBusy(true); apply.Enabled = false;
        try
        {
            var results = await Task.Run(() => new PlacementCoordinator(platform).ApplyAsync(captured, cancellation.Token), cancellation.Token);
            status.Text = string.Join(" ", results.Select(r => $"Slot {captured.Placements.Single(p => p.Target == r.Target).Slot}: {r.Message}"));
        }
        catch (OperationCanceledException) { status.Text = "Canceled; already requested moves may complete."; }
        catch { status.Text = "Placement stopped because a window or display changed, or access was denied. Refresh and preview again."; }
        finally { plan = null; applying=false; SetBusy(false); }
    }
    private async Task SaveAsync()
    {
        if (!preferences.Available) { status.Text = "Reset saved settings before saving."; return; }
        var selected = CheckedWindows();
        if (selected.Length == 0) { status.Text = "Check windows in the order you want to save."; return; }
        string? name = await WorkspacePrompt.NameAsync(this, "Save preset", ""); if (name is null) return;
        SetBusy(true);
        try
        {
            var saved = await Task.Run(() =>
            {
                if (!PlacementCoordinator.SameDisplays(monitors, platform.Monitors())) throw new InvalidOperationException();
                return selected.Select(w =>
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    using var session = platform.Open(w.Target);
                    var current = session.Read();
                    if (!current.Movable) throw new InvalidOperationException();
                    return LayoutPlanner.Save(current, monitors);
                }).ToArray();
            }, cancellation.Token);
            var preset = new WindowPreset(name, saved);
            if (preferences.Settings.Presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { status.Text = "That preset name already exists. Rename or delete it first."; return; }
            preferences.Save(preferences.Settings with { Presets = [.. preferences.Settings.Presets, preset] });
            ReloadPresets(name); InvalidatePlan(); status.Text = "Current normal window positions saved. No process identities or captions were stored.";
        }
        catch (OperationCanceledException) { }
        catch { status.Text = "Preset could not be saved. Existing settings were preserved."; }
        finally { SetBusy(false); }
    }
    private void ReloadPresets(string? name)
    {
        presets.Items.Clear(); presets.DisplayMember = nameof(WindowPreset.Name);
        presets.Items.AddRange(preferences.Settings.Presets.Cast<object>().ToArray());
        presets.SelectedItem = preferences.Settings.Presets.FirstOrDefault(p => p.Name == name) ?? preferences.Settings.Presets.FirstOrDefault();
    }
    private async Task RenameAsync()
    {
        if (!preferences.Available || presets.SelectedItem is not WindowPreset p) return;
        string? name = await WorkspacePrompt.NameAsync(this, "Rename preset", p.Name); if (name is null) return;
        ChangePresets(preferences.Settings.Presets.Select(x => x == p ? p with { Name = name } : x).ToArray(), name);
    }
    private async Task DeleteAsync()
    {
        if (!preferences.Available || presets.SelectedItem is not WindowPreset p) return;
        if (await WorkspacePrompt.ConfirmAsync(this, $"Delete preset '{p.Name}'?", "Delete preset"))
            ChangePresets(preferences.Settings.Presets.Where(x => x != p).ToArray(), null);
    }
    private void ChangePresets(WindowPreset[] items, string? name)
    {
        try { preferences.Save(preferences.Settings with { Presets = items }); ReloadPresets(name); InvalidatePlan(); }
        catch { status.Text = "Settings could not be saved. Check for duplicate names or unavailable storage."; }
    }
    private async Task ResetAsync()
    {
        if (!await WorkspacePrompt.ConfirmAsync(this, "Delete all saved presets and reset shortcut preferences?", "Reset saved settings")) return;
        try { preferences.Reset(); ReloadPresets(null); InvalidatePlan(); } catch { status.Text = "Settings could not be reset."; }
    }
    private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? "Untitled window" : new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    protected override void WndProc(ref Message m)
    {
        if (m.Msg is 0x007e or 0x02e0 or 0x001a && IsHandleCreated && !loading) InvalidatePlan();
        base.WndProc(ref m);
    }
}

internal sealed class LayoutPreview : Control
{
    internal PlacementPlan? Plan;
    internal LayoutMonitor[] Monitors = [];
    internal LayoutPreview() { DoubleBuffered = true; BackColor = Color.White; AccessibleName = "Placement preview; numbered slots match checked-window order"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var theme = ThemePalette.For(this);
        var plan = Plan ?? new PlacementPlan(Monitors, [], []);
        if (plan.Monitors.Length == 0) { TextRenderer.DrawText(e.Graphics,"Select windows to see the layout.",Font,ClientRectangle,theme.Muted,TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
        using var monitorFill=new SolidBrush(theme.Background); using var frame=new Pen(Color.FromArgb(48,55,64),8);
        using var border=new Pen(theme.Border); using var accent=new Pen(theme.Accent,2);
        var desktop=plan.Monitors.Select(m=>m.Work).Aggregate(Rectangle.Union);
        float scale=Math.Min((ClientSize.Width-48f)/Math.Max(1,desktop.Width),(ClientSize.Height-120f)/Math.Max(1,desktop.Height));
        if(scale<=0)return;
        float offsetX=(ClientSize.Width-desktop.Width*scale)/2,offsetY=(ClientSize.Height-80-desktop.Height*scale)/2;
        RectangleF Convert(Rectangle b)=>new(offsetX+(b.X-desktop.X)*scale,offsetY+(b.Y-desktop.Y)*scale,b.Width*scale,b.Height*scale);
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for(int index=0;index<plan.Monitors.Length;index++)
        {
            var monitor=plan.Monitors[index]; var bounds=Convert(monitor.Work);
            e.Graphics.FillRectangle(monitorFill,bounds); e.Graphics.DrawRectangle(frame,bounds.X,bounds.Y,bounds.Width,bounds.Height);
            float center=bounds.X+bounds.Width/2;
            e.Graphics.DrawLine(frame,center,bounds.Bottom+4,center,bounds.Bottom+18);
            e.Graphics.DrawLine(frame,center-25,bounds.Bottom+20,center+25,bounds.Bottom+20);
            var label=new Rectangle((int)bounds.X,(int)bounds.Bottom+32,(int)bounds.Width,22);
            TextRenderer.DrawText(e.Graphics,$"Display {index+1}",Font,label,theme.Text,TextFormatFlags.HorizontalCenter);
            label.Y+=24; TextRenderer.DrawText(e.Graphics,$"{monitor.Work.Width} × {monitor.Work.Height}",Font,label,theme.Muted,TextFormatFlags.HorizontalCenter);
        }
        using var numberFont=new Font("Segoe UI",22,FontStyle.Bold);
        for(int index=0;index<plan.Placements.Length;index++)
        {
            var placement=plan.Placements[index]; var bounds=Convert(placement.Bounds);
            bounds.Inflate(-4,-4);
            using var fill=new SolidBrush(Color.FromArgb(65,theme.Accent)); e.Graphics.FillRectangle(fill,bounds); e.Graphics.DrawRectangle(accent,bounds.X,bounds.Y,bounds.Width,bounds.Height);
            TextRenderer.DrawText(e.Graphics,(placement.Slot==0?index+1:placement.Slot).ToString(),numberFont,Rectangle.Round(bounds),theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }    }
}

internal sealed class LayoutWindowGrid : DataGridView
{
    internal LayoutWindowGrid()
    {
        DoubleBuffered = true;
    }
    internal string[] MonitorOptions = [];
    internal string EmptyText = "Loading windows…";
    private readonly ContextMenuStrip monitorMenu = new() { ShowImageMargin = false, ShowCheckMargin = true };
    internal ContextMenuStrip MonitorMenu => monitorMenu;
    protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
    {
        base.OnCellMouseDown(e);
        // Open after the grid completes its selection/focus work. Waiting for its
        // synthesized CellMouseClick can consume the first click on an unfocused row.
        if(e.Button==MouseButtons.Left && e.RowIndex>=0 && e.ColumnIndex==2)
            BeginInvoke(()=> { if(!IsDisposed) ShowMonitorMenu(e.RowIndex); });
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if(CurrentCell is { ColumnIndex: 2 } cell && keyData is (Keys.Alt | Keys.Down) or Keys.F4 or Keys.Space or Keys.Enter)
        {
            ShowMonitorMenu(cell.RowIndex); return true;
        }
        return base.ProcessCmdKey(ref message,keyData);
    }
    private void ShowMonitorMenu(int row)
    {
        if (!Enabled || row<0 || row>=Rows.Count) return;
        monitorMenu.Items.Clear();
        var palette=ThemePalette.For(this);
        monitorMenu.Font=Font; monitorMenu.BackColor=palette.Surface; monitorMenu.ForeColor=palette.Text;
        monitorMenu.Renderer=new MonitorMenuRenderer(palette);
        foreach(string monitor in MonitorOptions)
        {
            var item=new ToolStripMenuItem(monitor) { Checked=Equals(Rows[row].Cells[2].Value,monitor), AccessibleName=monitor };
            item.Click+=(_,_)=> { if(!IsDisposed && row<Rows.Count) Rows[row].Cells[2].Value=monitor; };
            monitorMenu.Items.Add(item);
        }
        var bounds=GetCellDisplayRectangle(2,row,true);
        monitorMenu.MinimumSize=new(bounds.Width,0);
        monitorMenu.Show(this,new Point(bounds.Left,bounds.Bottom));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(Rows.Count==0) TextRenderer.DrawText(e.Graphics,EmptyText,Font,ClientRectangle,
            ThemePalette.For(this).Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing) monitorMenu.Dispose();
        base.Dispose(disposing);
    }
    private sealed class MonitorMenuRenderer(ThemePalette palette) : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var fill=new SolidBrush(palette.Surface); e.Graphics.FillRectangle(fill,e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen=new Pen(palette.Border); e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using var fill=new SolidBrush(e.Item.Selected?palette.Selection:palette.Surface);
            e.Graphics.FillRectangle(fill,new Rectangle(Point.Empty,e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor=palette.Text; base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var b=e.ImageRectangle;
            using var pen=new Pen(palette.Accent,2);
            e.Graphics.DrawLines(pen,[new Point(b.Left+2,b.Top+b.Height/2),new Point(b.Left+b.Width/2-1,b.Bottom-4),new Point(b.Right-2,b.Top+3)]);
        }
    }
    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && Columns[e.ColumnIndex].Name == "Monitor")
        {
            var palette = ThemePalette.For(this);
            using var fill = new SolidBrush((e.State & DataGridViewElementStates.Selected) != 0 ? palette.Selection : palette.Surface);
            e.Graphics!.FillRectangle(fill, e.CellBounds);
            var text = Rectangle.Inflate(e.CellBounds, -8, 0);
            text.Width = Math.Max(0, text.Width - 20);
            TextRenderer.DrawText(e.Graphics, e.FormattedValue?.ToString(), Font, text, palette.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int x = e.CellBounds.Right - 14, y = e.CellBounds.Top + e.CellBounds.Height / 2;
            using var arrow = new Pen(palette.Muted, 1.5f);
            e.Graphics.DrawLines(arrow, [new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2)]);
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Focus);
            e.Handled = true;
            return;
        }
        base.OnCellPainting(e);
    }
}

// Keep native selection, keyboard navigation and owner-drawn popup items; paint the closed control consistently.
internal sealed class LayoutComboBox : ComboBox
{
    internal bool ButtonShape;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint window,int message,nint wParam,nint lParam);
    internal LayoutComboBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x020a && !DroppedDown) // WM_MOUSEWHEEL
        {
            for(Control? parent=Parent; parent is not null; parent=parent.Parent)
                if(parent is WorkspaceScrollViewport || parent is ScrollableControl { AutoScroll: true })
                {
                    message.Result=SendMessage(parent.Handle,message.Msg,message.WParam,message.LParam);
                    return;
                }
            message.Result=0;
            return;
        }
        base.WndProc(ref message);
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var palette=ThemePalette.For(this);
        e.Graphics.Clear(ButtonShape ? Parent?.BackColor ?? palette.Background : palette.Surface);
        using var border=new Pen(Focused?palette.Accent:palette.Border);
        if (ButtonShape)
        {
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var shape=WorkspaceCard.Rounded(new(0,0,Width-1,Height-1),8);
            using var fill=new SolidBrush(BackColor);
            e.Graphics.FillPath(fill,shape); e.Graphics.DrawPath(border,shape);
        }
        else e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);
        var text=new Rectangle(8,0,Math.Max(0,Width-36),Height);
        TextRenderer.DrawText(e.Graphics,SelectedIndex>=0?GetItemText(SelectedItem):Text,Font,text,
            Enabled?palette.Text:palette.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        int x=Width-15,y=Height/2;
        using var arrow=new Pen(palette.Muted,1.5f);
        e.Graphics.DrawLines(arrow,[new Point(x-4,y-2),new Point(x,y+2),new Point(x+4,y-2)]);
    }
}
