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

internal sealed class ArrangementForm : Form
{
    private readonly DataGridView list = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AccessibleName = "Ordered window slots and monitor assignments" };
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, AccessibleName = "Layout" };
    private readonly ComboBox presets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Saved preset" };
    private readonly CheckedListBox monitorsList = new() { Width = 230, Height = 65, CheckOnClick = true, AccessibleName = "Target monitors" };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new(1000, 0) };
    private readonly LayoutPreview preview = new() { Dock = DockStyle.Fill, MinimumSize = new(300, 120) };
    private readonly Button apply = new() { Text = "&Apply", AutoSize = true, Enabled = false };
    private readonly FlowLayoutPanel controls = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private readonly List<ArrangementChoice> choices;
    private readonly LayoutPreferences preferences;
    private readonly IPlacementPlatform platform;
    private readonly CancellationTokenSource cancellation = new();
    private readonly string? initialMonitor;
    private readonly Func<CancellationToken, InstanceSnapshot> discover;
    private LayoutMonitor[] monitors = [];
    private Dictionary<InstanceTarget, LayoutWindow> live = [];
    private PlacementPlan? plan;
    private bool loading, busy, initial = true, closeAfterWork;
    internal string? SelectedPresetName => (presets.SelectedItem as WindowPreset)?.Name;
    internal ArrangementForm(List<ArrangementChoice> choices, LayoutPreferences preferences, ArrangementKind layout,
        string? presetName, string? initialMonitor, IPlacementPlatform? platform = null,
        Func<CancellationToken, InstanceSnapshot>? discover = null)
    {
        this.choices = choices; this.preferences = preferences; this.initialMonitor = initialMonitor;
        this.platform = platform ?? new PlacementPlatform();
        this.discover = discover ?? PlacementPlatform.Discover;
        Text = "Arrange Roblox windows"; Size = new(1080, 780); MinimumSize = new(850, 650);
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new(12) };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 45));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 55));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        top.Controls.Add(new Label { AutoSize = true, Text = "Layout:", Padding = new(0, 6, 0, 0) });
        kind.Items.AddRange([ArrangementKind.SideBySide, ArrangementKind.Grid, ArrangementKind.Saved, ArrangementKind.Recover]);
        kind.FormattingEnabled = true;
        kind.Format += (_, e) => e.Value = e.ListItem switch
        {
            ArrangementKind.SideBySide => "Side-by-side", ArrangementKind.Saved => "Restore preset",
            ArrangementKind.Recover => "Recover off-screen", _ => "Grid"
        };
        kind.SelectedItem = layout; top.Controls.Add(kind); top.Controls.Add(presets);
        top.Controls.Add(new Label { AutoSize = true, Text = "Monitors:" }); top.Controls.Add(monitorsList);
        root.Controls.Add(top, 0, 0); root.Controls.Add(list, 0, 1); root.Controls.Add(controls, 0, 2);
        root.Controls.Add(preview, 0, 3); root.Controls.Add(status, 0, 4);
        root.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Text =
            "Slots follow checked-window order, never accounts. Minimum client: 640×360 logical pixels.\n" +
            "Apply briefly focuses fullscreen clients to use F11, then arranges them and returns focus here. Keep keys released during Apply. New windows are unchecked." }, 0, 5);
        Controls.Add(root);
        list.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", FillWeight = 12 });
        list.Columns.Add(new DataGridViewTextBoxColumn { Name = "Window", ReadOnly = true, FillWeight = 70 });
        list.Columns.Add(new DataGridViewComboBoxColumn { Name = "Monitor", FillWeight = 30, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton });
        list.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", ReadOnly = true, FillWeight = 25 });
        Add("Move up", () => MoveChoice(-1)); Add("Move down", () => MoveChoice(1));
        Add("Distribute", Distribute); Add("Refresh windows", async () => await RefreshAsync());
        Add("&Preview", RefreshPreviewAsync); controls.Controls.Add(apply);
        Add("Save current positions", async () => await SaveAsync());
        Add("Rename preset", Rename); Add("Delete preset", Delete);
        Add("Reset saved settings", Reset);
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        controls.Controls.Add(cancel); CancelButton = cancel; AcceptButton = apply;
        apply.Click += async (_, _) => await ApplyAsync();
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
        monitorsList.ItemCheck += (_, _) => { if (!loading) BeginInvoke(() => { if (!IsDisposed) { Distribute(); } }); };
        ReloadPresets(presetName);
        Shown += async (_, _) => { await RefreshAsync(); if (!IsDisposed && !cancellation.IsCancellationRequested) await RefreshPreviewAsync(); };
        FormClosing += (_, e) =>
        {
            cancellation.Cancel();
            if (busy) { closeAfterWork = true; e.Cancel = true; }
        };
        FormClosed += (_, _) => cancellation.Dispose();
    }
    private void Add(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => { if (!busy) action(); }; controls.Controls.Add(button);
    }
    private void Add(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) => { if (!busy) await action(); }; controls.Controls.Add(button);
    }
    private void InvalidatePlan()
    {
        plan = null; preview.Plan = null; preview.Invalidate(); apply.Enabled = false;
        status.Text = "Preview the arrangement before applying.";
    }
    private void SetBusy(bool value)
    {
        busy = value; controls.Enabled = list.Enabled = kind.Enabled = presets.Enabled = monitorsList.Enabled = !value;
        if (!value && closeAfterWork) { closeAfterWork = false; Close(); }
    }
    private async Task RefreshAsync()
    {
        if (busy) return;
        SetBusy(true); InvalidatePlan();
        try
        {
            var selectedMonitors = monitorsList.CheckedIndices.Cast<int>().Where(i => i < monitors.Length).Select(i => monitors[i].Id).ToHashSet();
            var result = await Task.Run(() =>
            {
                var screens = platform.Monitors();
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
            initial = false; RenderRows();
            status.Text = preferences.Available ? "Choose windows, assign monitors, and preview." :
                "Saved settings are corrupt, unsupported, or inaccessible. Presets cannot be changed until you explicitly reset settings.";
        }
        catch (OperationCanceledException) { }
        catch { status.Text = "Windows or display information is unavailable. Refresh to try again."; }
        finally { SetBusy(false); }
    }
    private void RenderRows()
    {
        loading = true;
        try
        {
            list.Rows.Clear();
            ((DataGridViewComboBoxColumn)list.Columns[2]).Items.Clear();
            ((DataGridViewComboBoxColumn)list.Columns[2]).Items.AddRange(monitors.Select(m => (object)m.Label).ToArray());
            foreach (var c in choices)
            {
                var w = live[c.Target];
                int i = list.Rows.Add(c.Selected, $"{c.Caption} — PID {c.Target.Key.Id}, {InstancePolicy.Started(c.Target.Key):g}",
                    monitors.First(m => m.Id == c.Monitor).Label, w.NeedsWindowed && w.Movable ? "Ready — will exit fullscreen" : w.Movable ? "Ready" : w.Reason ?? "Unavailable");
                list.Rows[i].Cells[0].ToolTipText = w.Movable ? "Include this window" :
                    "You can select this window. Preview rechecks its state; unavailable windows are reported individually.";
            }
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
        SetBusy(true); InvalidatePlan();
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
            if (!PlacementCoordinator.SameDisplays(monitors, platform.Monitors())) { InvalidatePlan(); status.Text = "Displays changed. Refresh windows and preview again."; return; }
            var checkedWindows = CheckedWindows();
            if (checkedWindows.Length == 0) { InvalidatePlan(); status.Text = "Check at least one window."; return; }
            var layout = (ArrangementKind)kind.SelectedItem!;
            var skipped = checkedWindows.Where(w => !w.Movable).ToArray();
            string skippedMessage = string.Join(" ", skipped.Select(w => $"Slot {Array.IndexOf(checkedWindows, w) + 1} skipped: {w.Reason}."));
            if (layout == ArrangementKind.Saved && skipped.Length > 0)
            { InvalidatePlan(); status.Text = skippedMessage + " All saved-position slots must be ready before restoring a preset."; return; }
            var selected = checkedWindows.Where(w => w.Movable).ToArray();
            if (selected.Length == 0) { InvalidatePlan(); status.Text = skippedMessage; return; }
            if (layout is ArrangementKind.SideBySide or ArrangementKind.Grid && monitorsList.CheckedItems.Count == 0)
            { InvalidatePlan(); status.Text = "Select at least one monitor."; return; }
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
            status.Text = plan.Error ?? string.Join(" ", plan.Notes.Append($"{plan.Placements.Length} window(s). Press Apply to confirm this preview."));
        }
        catch { InvalidatePlan(); status.Text = "Preview unavailable. Refresh windows and try again."; }
    }
    private async Task ApplyAsync()
    {
        if (busy || plan is not { Valid: true } captured) return;
        SetBusy(true); apply.Enabled = false;
        try
        {
            var results = await Task.Run(() => new PlacementCoordinator(platform).ApplyAsync(captured, cancellation.Token), cancellation.Token);
            status.Text = string.Join(" ", results.Select(r => $"Slot {captured.Placements.Single(p => p.Target == r.Target).Slot}: {r.Message}"));
        }
        catch (OperationCanceledException) { status.Text = "Canceled; already requested moves may complete."; }
        catch { status.Text = "Placement stopped because a window or display changed, or access was denied. Refresh and preview again."; }
        finally { plan = null; SetBusy(false); }
    }
    private async Task SaveAsync()
    {
        if (!preferences.Available) { status.Text = "Reset saved settings before saving."; return; }
        var selected = CheckedWindows();
        if (selected.Length == 0) { status.Text = "Check windows in the order you want to save."; return; }
        string? name = AskName("Save preset", ""); if (name is null) return;
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
    private void Rename()
    {
        if (!preferences.Available || presets.SelectedItem is not WindowPreset p) return;
        string? name = AskName("Rename preset", p.Name); if (name is null) return;
        ChangePresets(preferences.Settings.Presets.Select(x => x == p ? p with { Name = name } : x).ToArray(), name);
    }
    private void Delete()
    {
        if (!preferences.Available || presets.SelectedItem is not WindowPreset p) return;
        if (MessageBox.Show(this, $"Delete preset '{p.Name}'?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
            ChangePresets(preferences.Settings.Presets.Where(x => x != p).ToArray(), null);
    }
    private void ChangePresets(WindowPreset[] items, string? name)
    {
        try { preferences.Save(preferences.Settings with { Presets = items }); ReloadPresets(name); InvalidatePlan(); }
        catch { status.Text = "Settings could not be saved. Check for duplicate names or unavailable storage."; }
    }
    private void Reset()
    {
        if (MessageBox.Show(this, "Delete all saved presets and reset shortcut preferences?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try { preferences.Reset(); ReloadPresets(null); InvalidatePlan(); } catch { status.Text = "Settings could not be reset."; }
    }
    private string? AskName(string title, string initial)
    {
        using var dialog = new Form { Text = title, ClientSize = new(400, 110), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, AutoScaleMode = AutoScaleMode.Dpi };
        var box = new TextBox { Text = initial, MaxLength = 80, Dock = DockStyle.Top, AccessibleName = "Preset name" };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(box); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        return string.IsNullOrWhiteSpace(box.Text) || box.Text.Any(char.IsControl) ? null : box.Text.Trim();
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
    internal LayoutPreview() { DoubleBuffered = true; BackColor = Color.White; AccessibleName = "Placement preview; numbered slots match checked-window order"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Plan is not { } plan || plan.Monitors.Length == 0) return;
        var desktop = plan.Monitors.Select(m => m.Work).Aggregate(Rectangle.Union);
        float scale = Math.Min((ClientSize.Width - 20f) / Math.Max(1, desktop.Width), (ClientSize.Height - 20f) / Math.Max(1, desktop.Height));
        if (scale <= 0) return;
        RectangleF Convert(Rectangle b) => new(10 + (b.X - desktop.X) * scale, 10 + (b.Y - desktop.Y) * scale, b.Width * scale, b.Height * scale);
        foreach (var monitor in plan.Monitors)
        {
            var b = Convert(monitor.Work);
            e.Graphics.FillRectangle(Brushes.WhiteSmoke, b); e.Graphics.DrawRectangle(Pens.Gray, b.X, b.Y, b.Width, b.Height);
        }
        for (int i = 0; i < plan.Placements.Length; i++)
        {
            var b = Convert(plan.Placements[i].Bounds);
            using var fill = new SolidBrush(Color.FromArgb(100, Color.SteelBlue));
            e.Graphics.FillRectangle(fill, b); e.Graphics.DrawRectangle(Pens.SteelBlue, b.X, b.Y, b.Width, b.Height);
            e.Graphics.DrawString((plan.Placements[i].Slot == 0 ? i + 1 : plan.Placements[i].Slot).ToString(), Font, Brushes.Black, b.Location);
        }
    }
}
