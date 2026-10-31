using Microsoft.Win32;
namespace RobloxLaunchHelper;

internal sealed partial class DiagnosticsForm
{
    private readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new(30,4,30,20) };
    private readonly Label pageHeading = new WorkspaceLabel() { AutoSize = true, Font = new("Segoe UI",28,FontStyle.Bold) };
    private readonly Label pageSubtitle = new() { AutoSize=true, Font=new("Segoe UI",12), Tag="muted", Margin=new(0,6,0,0) };
    private string instanceSummary="Manage your Roblox windows in one place.";
    private readonly Label workspaceStatus = new() { AutoSize = true, Dock = DockStyle.Fill, Tag = "muted" };
    private readonly Label integrationStatus = new() { AutoSize = true, Text = "Browser integration   Checking…", Margin = new(12), Tag = "muted" };
    private readonly Label systemStatus = new() { AutoSize = true, Text = "System: checking…", Margin = new(12), Tag = "muted" };
    private readonly Dictionary<WorkspaceSection, Control> pages = [];
    private readonly Dictionary<WorkspaceSection, Button> navigation = [];
    private readonly WorkspaceTabs systemTabs = new() { Dock = DockStyle.Fill };
    private readonly WorkspaceTabs settingsTabs = new() { Dock = DockStyle.Fill };
    private readonly AppearancePreferences appearance;
    private readonly IInstancePlatform? instancePlatform;
    private readonly IPlacementPlatform? placementPlatform;
    private readonly Func<CancellationToken, InstanceSnapshot>? discoverWindows;
    private readonly bool testMode;
    private readonly LayoutPreferences? injectedLayouts;
    private readonly NotificationPreferences? injectedNotifications;
    private readonly UpdateServices? injectedUpdates;
    private ArrangementPage? arrangement;
    private UpdatesPage? updates;
    private bool allowClose, closingWorkspace, updateChecked;
    internal WorkspaceTarget CurrentTarget { get; private set; }
    internal ColorScheme Scheme => appearance.Scheme;
    internal ThemePalette Palette { get; private set; } = ThemePalette.Resolve(ColorScheme.Dark);
    private bool wrappingLabelsQueued;
    internal ActiveInstancesPage Instances => activeInstances!;
    internal UpdatesPage Updates => updates!;
    internal ArrangementPage Layouts => arrangement!;
    internal bool CanCloseWorkspace => arrangement?.PreventNavigation != true;

    private void BuildWorkspace(bool collectOnShown)
    {
        Text = "Roblox Multi Instance"; Size = new(1440,960); MinimumSize = new(1050,760);
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi; Font = new("Segoe UI",12);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        root.ColumnStyles.Add(new(SizeType.Absolute,228)); root.ColumnStyles.Add(new(SizeType.Percent,100));
        root.RowStyles.Add(new(SizeType.Absolute,138)); root.RowStyles.Add(new(SizeType.Percent,100)); root.RowStyles.Add(new(SizeType.AutoSize));
        var sidebar = new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new(12,26,12,16),Tag="sidebar" };
        sidebar.RowStyles.Add(new(SizeType.Percent,100)); sidebar.RowStyles.Add(new(SizeType.AutoSize));
        var nav = new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Tag="sidebar" };
        foreach (var section in Enum.GetValues<WorkspaceSection>())
        {
            var target=section;
            var button=new NavigationButton { Section=section,Text=section.ToString(),AutoSize=false,Size=new(200,56),Margin=new(0,0,0,10),TextAlign=ContentAlignment.MiddleLeft };
            button.Click += async (_,_)=>await NavigateAsync(new(target));
            button.AccessibleName="Navigate to "+section; navigation[section]=button; nav.Controls.Add(button);
        }
        sidebar.Controls.Add(nav,0,0);
        var footer=new TableLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Padding=new(8,16,8,0),Tag="sidebar" };
        footer.ColumnStyles.Add(new(SizeType.Percent,100)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        systemStatus.Font=new("Segoe UI",9); systemStatus.Margin=new(0);
        footer.Controls.Add(systemStatus,0,0); footer.Controls.Add(new Label { Text="1.1.0",AutoSize=true,Font=new("Segoe UI",9),Tag="muted",Margin=new(8,0,0,0) },1,0);
        sidebar.Controls.Add(footer,0,1); root.Controls.Add(sidebar,0,0); root.SetRowSpan(sidebar,3);
        var header=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,Padding=new(30,28,30,16) };
        header.ColumnStyles.Add(new(SizeType.Percent,100)); header.ColumnStyles.Add(new(SizeType.AutoSize));
        var heading=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false };
        heading.Controls.Add(pageHeading); heading.Controls.Add(pageSubtitle);
        launchRoblox.Tag="primary"; launchRoblox.Text="&Launch Roblox"; launchRoblox.Font=new("Segoe UI",12,FontStyle.Bold); launchRoblox.MinimumSize=new(190,50);
        header.Controls.Add(heading,0,0); header.Controls.Add(launchRoblox,1,0);        root.Controls.Add(header,1,0); root.Controls.Add(content,1,1);
        workspaceStatus.Padding = new(24,8,24,8); workspaceStatus.MaximumSize = new(1000,0); root.Controls.Add(workspaceStatus,1,2); Controls.Add(root);

        activeInstances = new ActiveInstancesPage(collectOnShown, instancePlatform, injectedLayouts ?? managerHost?.Preferences);
        activeInstances.NavigateLayout = OpenLayoutAsync;
        activeInstances.IntegrationToggleRequested += async () => await RunAsync(ToggleIntegrationAsync);
        activeInstances.LaunchRequested += async () => await LaunchRobloxAsync();
        activeInstances.SummaryChanged += text => { instanceSummary=text; if(CurrentTarget.Page==WorkspaceSection.Instances) pageSubtitle.Text=text; };
        pages[WorkspaceSection.Instances] = activeInstances;
        pages[WorkspaceSection.Activity] = BuildActivity();

        var health = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new(12) };
        health.RowStyles.Add(new(SizeType.AutoSize)); health.RowStyles.Add(new(SizeType.Percent,100)); health.RowStyles.Add(new(SizeType.AutoSize));
        checks.Columns.Add("Check",155); checks.Columns.Add("Status",95); checks.Columns.Add("Message / next step",560);
        status.MaximumSize = new(780,0); health.Controls.Add(status,0,0); health.Controls.Add(checks,0,1); health.Controls.Add(buttons,0,2);
        var healthDetail = new TextBox { ReadOnly = true, Multiline = true, Dock = DockStyle.Bottom, Height = 90, ScrollBars = ScrollBars.Vertical, AccessibleName = "Health check details", Tag="borderless" };
        checks.SelectedIndexChanged += (_,_) => healthDetail.Text = checks.SelectedItems.Count>0 ? checks.SelectedItems[0].Tag as string : "";
        health.Controls.Add(healthDetail,0,3); health.RowCount = 4; health.RowStyles.Add(new(SizeType.Absolute,90));
        updates = new UpdatesPage(injectedUpdates ?? UpdateServices.Create());
        updates.StateChanged += () => { if(updates.HasUpdate) { navigation[WorkspaceSection.System].Text = "System · update"; workspaceStatus.Text = "An update is available in System → Updates."; } else navigation[WorkspaceSection.System].Text = "System"; };
        updates.CloseRequested += () => Close();
        AddTab(systemTabs,"Health",WorkspaceDesign.Card(health,new(16))); AddTab(systemTabs,"Updates",updates);
        var reports = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new(20), AutoScroll = true, WrapContents = false };
        foreach (var button in buttons.Controls.OfType<Button>().Where(b=>b.Text.Contains("logs") || b.Text.Contains("report") || b.Text.Contains("history")).ToArray()) reports.Controls.Add(button);
        reports.Controls.Add(new Label { AutoSize = true, MaximumSize = new(660,0), Text = "Reports are saved locally. Review timestamps and launch activity before sharing. Raw logs and account identifiers are excluded." });
        AddTab(systemTabs,"Logs & reports",WorkspaceDesign.Card(reports)); pages[WorkspaceSection.System] = systemTabs;

        var appearancePage = BuildAppearance(); AddTab(settingsTabs,"Appearance",WorkspaceDesign.Card(appearancePage));
        AddTab(settingsTabs,"Notifications",WorkspaceDesign.Card(new NotificationSettingsPage(injectedNotifications)));
        var shortcuts = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        shortcuts.Controls.Add(new ShortcutSettingsPage(injectedLayouts ?? managerHost?.Preferences ?? new LayoutPreferences()));
        var trayButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        trayButtons.Controls.Add(WorkspaceControls.Button("Enable tray mode",()=>managerHost?.EnableTray()));
        trayButtons.Controls.Add(WorkspaceControls.Button("Disable tray mode",()=>managerHost?.StopTray()));
        shortcuts.Controls.Add(trayButtons); AddTab(settingsTabs,"Shortcuts & tray",WorkspaceDesign.Card(shortcuts)); pages[WorkspaceSection.Settings] = settingsTabs;
        foreach (var page in pages.Values) { page.Dock = DockStyle.Fill; page.Visible = false; content.Controls.Add(page); }
        Resize += (_,_) => activeInstances?.HostVisibilityChanged();
        content.SizeChanged += (_,_) => QueueLabelLayout();
        FormClosing += async (_,e) =>
        {
            if (allowClose) return;
            e.Cancel = true;
            if (arrangement?.PreventNavigation == true) { workspaceStatus.Text = "Wait for the layout operation to finish before closing."; return; }
            if (closingWorkspace) return;
            closingWorkspace = true; closed.Cancel(); activeInstances?.Dispose();
            await updates.StopAsync(); await InstanceCompletion;
            if (!IsDisposed) { allowClose = true; Close(); }
        };
        FormClosed += (_,_) => { SystemEvents.UserPreferenceChanged -= OnWindowsAppearance; updates.Dispose(); };
        SystemEvents.UserPreferenceChanged += OnWindowsAppearance;
        systemTabs.SelectedIndexChanged += async (_,_) => { if (!testMode && systemTabs.SelectedIndex == 1 && !updateChecked) { updateChecked = true; await updates.CheckAsync(); } };
        ApplyTheme(); _ = NavigateAsync(new(WorkspaceSection.Instances));
    }
    private static void AddTab(WorkspaceTabs tabs,string text,Control control) => tabs.AddPage(text,control);
    internal async Task OpenLayoutAsync(ArrangementKind kind)
    {
        await NavigateAsync(new(WorkspaceSection.Layouts)); arrangement?.SelectKind(kind);
    }
    public async Task NavigateAsync(WorkspaceTarget target)
    {
        if (IsDisposed || closed.IsCancellationRequested) return;
        if (arrangement?.PreventNavigation == true && target.Page != WorkspaceSection.Layouts)
        { workspaceStatus.Text = "Wait for the layout operation to finish before leaving Layouts."; return; }
        // Hiding cancels collection. The controller drains native work and rejects
        // late results itself; read-only navigation need not wait for that drain.
        if (target.Page != WorkspaceSection.Instances) activeInstances!.Visible = false;
        if (target.Page == WorkspaceSection.Layouts && arrangement is null)
        {
            arrangement = new ArrangementPage([], injectedLayouts ?? managerHost?.Preferences ?? new LayoutPreferences(),ArrangementKind.Grid,null,null,placementPlatform,discoverWindows);
            pages[WorkspaceSection.Layouts] = arrangement;
            arrangement.Visible = false;
            Palette.Apply(arrangement); content.Controls.Add(arrangement);
        }
        foreach (var pair in pages) pair.Value.Visible = pair.Key == target.Page;
        CurrentTarget = target; pageHeading.Text = target.Page.ToString();
        pageSubtitle.Text=target.Page switch {
            WorkspaceSection.Instances=>instanceSummary,
            WorkspaceSection.Layouts=>"Arrange your Roblox windows across displays.",
            WorkspaceSection.Activity=>"Recent launches and helper completion results.",
            WorkspaceSection.System=>snapshot is null?"Health, updates, and troubleshooting.":$"Last checked {snapshot.CollectedUtc.LocalDateTime:t}",
            _=>"Appearance, notifications, and workspace preferences."
        };
        if (target.Page == WorkspaceSection.System) systemTabs.SelectedIndex = (int)target.System;
        if (target.Page == WorkspaceSection.Settings) settingsTabs.SelectedIndex = (int)target.Settings;
        WrapPageLabels(); ApplyNavigation(); activeInstances?.HostVisibilityChanged();
        if (target.Page == WorkspaceSection.Activity && !testMode) await RunAsync(RefreshActivityAsync);
    }
    private Control BuildAppearance()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), FlowDirection = FlowDirection.TopDown, AutoScroll = true, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Color scheme", AutoSize = true, Font = new("Segoe UI",14,FontStyle.Bold) });
        var picker = new LayoutComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "Color scheme" };
        picker.Items.AddRange(["Dark","Light","Warm Neutral","Follow Windows"]); picker.SelectedIndex = (int)appearance.Scheme; picker.Enabled = appearance.Available;
        var message = new Label { AutoSize = true, MaximumSize = new(650,0), Text = appearance.Available ? "Changes apply immediately. Windows high contrast takes priority." : "Appearance settings are unavailable. Reset them to enable changes." };
        picker.SelectedIndexChanged += (_,_) => { try { appearance.Save((ColorScheme)picker.SelectedIndex); ApplyTheme(); message.Text = "Appearance saved."; } catch { message.Text = "Appearance could not be saved. Check folder access or reset settings."; } };
        panel.Controls.Add(picker); panel.Controls.Add(message);
        panel.Controls.Add(WorkspaceControls.Button("Reset appearance…",async ()=>
        {
            if (!await WorkspacePrompt.ConfirmAsync(this,"Reset appearance to Dark?","Reset appearance")) return;
            try { appearance.Save(ColorScheme.Dark,true); picker.Enabled = true; picker.SelectedIndex = 0; ApplyTheme(); message.Text = "Appearance reset."; }
            catch { message.Text = "Appearance could not be reset."; }
        }));
        return panel;
    }
    private void QueueLabelLayout()
    {
        if (wrappingLabelsQueued || !IsHandleCreated || IsDisposed) return;
        wrappingLabelsQueued = true;
        BeginInvoke(() =>
        {
            wrappingLabelsQueued = false;
            if (!IsDisposed && !closed.IsCancellationRequested) WrapPageLabels();
        });
    }
    private void WrapPageLabels()
    {
        foreach(var page in pages.Values.Where(page => page.Visible))
            foreach(var label in WorkspaceControls.Descendants(page).OfType<Label>())
                if(label.AutoSize && (label.Dock!=DockStyle.None || label.MaximumSize.Width>0) && label.Parent is not null)
                {
                    var maximum = new Size(Math.Max(200,label.Parent.ClientSize.Width-label.Parent.Padding.Horizontal-label.Margin.Horizontal-24),0);
                    if (label.MaximumSize != maximum) label.MaximumSize = maximum;
                }
    }
    private void OnWindowsAppearance(object sender,UserPreferenceChangedEventArgs args)
    { if (!IsDisposed && IsHandleCreated) { try { BeginInvoke(ApplyTheme); } catch (InvalidOperationException) { } } }
    internal void ApplyTheme()
    {
        Palette = ThemePalette.Resolve(Scheme);
        Palette.Apply(this); WorkspaceChrome.Apply(this,Palette); ApplyNavigation();
        PaintActivityResults();
    }
    protected override void OnResizeEnd(EventArgs args)
    {
        base.OnResizeEnd(args);
        FinishWorkspaceResize();
        Invalidate(true);
    }
    internal void FinishWorkspaceResize()
    {
        foreach (var list in WorkspaceControls.Descendants(content).OfType<WorkspaceListView>().Where(list=>list.Visible))
            list.FinishResize();
    }
    protected override void OnHandleCreated(EventArgs args) { base.OnHandleCreated(args); WorkspaceChrome.Apply(this,Palette); }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x007e or 0x02e0 or 0x001a) arrangement?.DisplaysChanged();
        base.WndProc(ref message);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closed.Cancel(); SystemEvents.UserPreferenceChanged -= OnWindowsAppearance;
            updates?.Dispose(); activeInstances?.Dispose(); arrangement?.Dispose();
        }
        base.Dispose(disposing);
    }
    private void ApplyNavigation()
    {
        var theme = Palette;
        foreach (var pair in navigation) { ((NavigationButton)pair.Value).Selected=pair.Key==CurrentTarget.Page; pair.Value.ForeColor=theme.Text; pair.Value.Invalidate(); }
    }
}
