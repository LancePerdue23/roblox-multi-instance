using RobloxLaunchHelper;
using System.Diagnostics;

internal static class WorkspaceTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint window,int message,nint wParam,nint lParam);
    internal static readonly string[] Scenarios = ["Navigation stays in one workspace", "Appearance persists and invalid settings require reset",
        "Update checks and notes stay embedded", "Offline updates recover", "Downloads survive page navigation and cancel safely",
        "Invalid downloads never reach setup", "Verified download requires a separate setup action", "Owned confirmation cancels safely",
        "Instance selection survives navigation", "Layout preview and ordering stay embedded"];
    internal static void Run(Action<string,Action> run)
    {
        UiTestContext.Install();
        run("Activity filters preserve launch selection and explain empty history", ActivityHistory);
        run("Shared scrollbar painting preserves native scrolling across themes and handle recreation", ScrollbarSurfaces);
        run("Themed scrollbars suppress the initial native paint", ScrollbarFirstPaint);
        run("Layout scrollbar retains its surface and edges across activation and page visibility", ScrollbarRepaints);
        run("First More options expansion repaints the live Layouts scrollbar after range updates", ExpandedLayoutScrollbar);
        run("Instance list restores row positions after growing a scrolled viewport", ResizeScrolledList);
        run("Layouts shows themed headings and displays while discovery is pending", LayoutsLoading);
        for (int i=0;i<Scenarios.Length;i++) { int scenario=i; run(Scenarios[i],()=>Exercise(scenario)); }
    }
    private static void Check(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class ScrollPanelProbe : Panel
    {
        internal int NativeFramePaints;
        internal void Recreate() => RecreateHandle();
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0085) NativeFramePaints++;
            base.WndProc(ref message);
        }
    }
    private sealed class ScrollBarProbe : VScrollBar
    {
        internal int NativePaints;
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x000f) NativePaints++;
            base.WndProc(ref message);
        }
    }
    private static void ScrollbarFirstPaint()
    {
        using var form = new Form { ShowInTaskbar = false, Opacity = 0, Size = new(500, 350) };
        var panel = new ScrollPanelProbe { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(new Label { Size = new(800, 1000), Text = "Scrollable layout options" });
        var bar = new ScrollBarProbe { Dock = DockStyle.Right, Maximum = 100 };
        form.Controls.Add(panel); form.Controls.Add(bar);
        // Install before handles exist, matching lazy Layouts creation.
        ThemePalette.Resolve(ColorScheme.Dark).Apply(form);
        form.Show(); UiTestContext.Install(); Application.DoEvents();
        Check(SystemInformation.HighContrast || bar.NativePaints == 0, "Initial scrollbar paint must not draw a native frame first");
        int before = panel.NativeFramePaints;
        SendMessage(panel.Handle, 0x0085, 1, 0);
        Check(SystemInformation.HighContrast || panel.NativeFramePaints == before, "Borderless viewport must paint its scrollbar without forwarding to native frame painting");
        bar.Invalidate(); bar.Update(); Application.DoEvents();
        Check(SystemInformation.HighContrast || bar.NativePaints == 0, "Later scrollbar paints must also avoid the native frame");
        panel.Size = new(300, 200); panel.AutoScrollPosition = new(0, 100);
        Check(panel.AutoScrollPosition.Y < 0, "Owning nonclient paint must preserve scrolling");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window,nint dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destination,int x,int y,int width,int height,nint source,int sourceX,int sourceY,uint operation);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point position);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x,int y);
    private static void ScrollbarRepaints()
    {
        if(SystemInformation.HighContrast) return;
        using var form=new Form { ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new(80,80),ClientSize=new(320,360) };
        var panel=new ScrollPanelProbe { Dock=DockStyle.Fill,AutoScroll=true,Tag="surface" };
        panel.Controls.Add(new Label { Size=new(120,1400),Text="Layout options" });
        form.Controls.Add(panel);
        ThemePalette.Resolve(ColorScheme.Dark).Apply(form);
        form.Show(); UiTestContext.Install(); Application.DoEvents();
        void CheckEdges(string stage)
        {
            Check(panel.VerticalScroll.Visible,"Scrollbar fixture must overflow");
            var dc=GetWindowDC(panel.Handle);
            try
            {
                int width=panel.Width-panel.ClientSize.Width;
                using var pixels=new Bitmap(width,panel.Height);
                using(var graphics=Graphics.FromImage(pixels))
                {
                    var target=graphics.GetHdc();
                    try { Check(BitBlt(target,0,0,width,panel.Height,dc,panel.ClientSize.Width,0,0x00cc0020),"Cannot capture scrollbar pixels"); }
                    finally { graphics.ReleaseHdc(target); }
                }
                int expected=panel.BackColor.ToArgb();
                foreach(int x in new[]{0,1,width-1})
                    for(int y=2;y<panel.Height-2;y++)
                        Check(pixels.GetPixel(x,y).ToArgb()==expected,$"Scrollbar edge lost its surface after {stage}: ({x},{y})");
                Check(Enumerable.Range(20,panel.Height-40).Count(y=>pixels.GetPixel(width/2,y).ToArgb()!=expected)>16,
                    $"Scrollbar thumb disappeared after {stage}");
                if(stage=="theme application")
                {
                    string output=Path.Combine(UserSimulation.Output,"scrollbars"); Directory.CreateDirectory(output);
                    pixels.Save(Path.Combine(output,$"live-track-{panel.BackColor.ToArgb():X8}.png"));
                }
            }
            finally { ReleaseDC(panel.Handle,dc); }
        }
        foreach(var scheme in new[]{ColorScheme.Dark,ColorScheme.Light,ColorScheme.WarmNeutral})
        {
            ThemePalette.Resolve(scheme).Apply(form); form.Refresh(); Application.DoEvents();
            CheckEdges("theme application");
            var hover=panel.PointToScreen(new(panel.ClientSize.Width+8,60));
            void PumpAnimation()
            {
                var animation=Stopwatch.StartNew();
                while(animation.ElapsedMilliseconds<600) { Application.DoEvents(); Thread.Sleep(10); }
            }
            GetCursorPos(out var originalCursor);
            try
            {
                SetCursorPos(hover.X,hover.Y); PumpAnimation(); CheckEdges("pointer hover");
                var outside=panel.PointToScreen(new(30,60));
                SetCursorPos(outside.X,outside.Y); PumpAnimation(); CheckEdges("pointer leave animation");
            }
            finally { SetCursorPos(originalCursor.X,originalCursor.Y); }
            for(int attempt=0;attempt<3;attempt++)
            {
                SendMessage(panel.Handle,0x0086,0,0); CheckEdges("deactivation");
                SendMessage(panel.Handle,0x0086,1,0); CheckEdges("activation");
                panel.Hide(); panel.Show(); Application.DoEvents(); CheckEdges("page visibility");
                // RecreateHandle alone does not restore ScrollableControl's native range.
                // Complete its layout before inspecting the replacement scrollbar.
                panel.Recreate(); panel.PerformLayout(); Application.DoEvents(); CheckEdges("handle recreation");
                panel.AutoScrollPosition=new(0,100+attempt*20); Application.DoEvents(); CheckEdges("scrolling");
                form.ClientSize=new(320,360+attempt*15); Application.DoEvents(); CheckEdges("resizing");
            }
        }
    }
    private static void ExpandedLayoutScrollbar()
    {
        if(SystemInformation.HighContrast) return;
        foreach(var scheme in new[]{ColorScheme.Dark,ColorScheme.Light,ColorScheme.WarmNeutral})
        {
            using var fixture=new Fixture(scheme);
            var shell=fixture.Shell;
            shell.Opacity=1; shell.Size=shell.MinimumSize;
            Pump(shell.NavigateAsync(new(WorkspaceSection.Layouts)));
            UserSimulation.Wait(()=>!shell.Layouts.Busy);
            var viewport=WorkspaceControls.Descendants(shell.Layouts).OfType<WorkspaceScrollViewport>().Single();
            var more=Button(shell.Layouts,"More options");
            using var alternate=new Form { ShowInTaskbar=false,Size=new(200,100),StartPosition=FormStartPosition.Manual,Location=new(0,0) };
            for(int attempt=0;attempt<4;attempt++)
            {
                more.PerformClick(); Application.DoEvents();
                alternate.Show(); alternate.Activate(); Application.DoEvents();
                shell.Activate(); Application.DoEvents();
                Check(viewport.Scrollbar.Visible && !viewport.VerticalScroll.Visible,"Layouts must scroll without a native nonclient scrollbar");
                // Read existing window pixels. DrawToBitmap would request another paint
                // and hide precisely the stale native scrollbar this regression checks.
                var bar=viewport.Scrollbar;
                int width=bar.Width;
                using var pixels=new Bitmap(width,bar.Height);
                var dc=GetWindowDC(bar.Handle);
                using(var graphics=Graphics.FromImage(pixels))
                {
                    var target=graphics.GetHdc();
                    try { Check(BitBlt(target,0,0,width,bar.Height,dc,0,0,0x00cc0020),"Cannot capture expanded scrollbar"); }
                    finally { graphics.ReleaseHdc(target); ReleaseDC(bar.Handle,dc); }
                }
                pixels.Save(Path.Combine(fixture.DirectoryPath,$"expanded-scrollbar-{attempt}.png"));
                foreach(int x in new[]{0,width-1})
                    for(int y=2;y<bar.Height-2;y++)
                        Check(pixels.GetPixel(x,y).ToArgb()==bar.BackColor.ToArgb(),$"More options left a native scrollbar track: {scheme}, toggle {attempt}, ({x},{y})");
                viewport.ScrollTo(viewport.MaximumOffset);
                Check(viewport.AutoScrollPosition.Y==-viewport.MaximumOffset,"Scrollbar cannot reach the end");
                viewport.Scrollbar.Focus();
                SendMessage(bar.Handle,0x0100,(nint)Keys.Home,0); Application.DoEvents();
                Check(viewport.AutoScrollPosition.Y==0,"Scrollbar Home key must return to the beginning");
                viewport.Scrollbar.AccessibilityObject.Value=viewport.MaximumOffset.ToString();
                Check(viewport.AutoScrollPosition.Y==-viewport.MaximumOffset,"Accessible scrollbar value must scroll the viewport");
                viewport.ScrollTo(0);
                nint down=(nint)(((bar.Width+8)<<16)|(bar.Width/2));
                nint end=(nint)(((bar.Height-bar.Width*2)<<16)|(bar.Width/2));
                SendMessage(bar.Handle,0x0201,1,down);
                SendMessage(bar.Handle,0x0200,1,end);
                SendMessage(bar.Handle,0x0202,0,end);
                Check(viewport.AutoScrollPosition.Y<0,"Dragging the thumb must scroll the options");
                viewport.ScrollTo(0);
                SendMessage(viewport.Handle,0x020a,unchecked((nint)(-120<<16)),0);
                Check(viewport.AutoScrollPosition.Y<0,"Mouse wheel must scroll the options");
            }
        }
    }
    private static void ScrollbarSurfaces()
    {
        using var form = new Form { Size = new(1000, 640), ShowInTaskbar = false, Opacity = 0, Font = new("Segoe UI", 11) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new(16) };
        layout.ColumnStyles.Add(new(SizeType.Percent, 50)); layout.ColumnStyles.Add(new(SizeType.Percent, 50));
        layout.RowStyles.Add(new(SizeType.Percent, 50)); layout.RowStyles.Add(new(SizeType.Percent, 50));
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Text = string.Join("\r\n", Enumerable.Range(0, 80).Select(i => $"Launch {i}: " + new string('x', 160))) };
        var list = new WorkspaceListView { Dock = DockStyle.Fill, View = View.Details };
        list.Columns.Add("Launch", 180); list.Columns.Add("Result", 500);
        foreach (int i in Enumerable.Range(0, 80)) list.Items.Add(new ListViewItem([$"Launch {i}", "Helper complete"]));
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(new Label { Location = new(8, 8), Size = new(800, 1200), Text = "Scrollable options\r\n" + new string('x', 120) });
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false };
        grid.Columns.Add("Launch", "Launch"); grid.Columns.Add("Result", "Result"); grid.Columns[1].Width = 600;
        foreach (int i in Enumerable.Range(0, 80)) grid.Rows.Add(i, "Helper complete");
        layout.Controls.Add(text, 0, 0); layout.Controls.Add(list, 1, 0); layout.Controls.Add(panel, 0, 1); layout.Controls.Add(grid, 1, 1);
        form.Controls.Add(layout); form.Show(); UiTestContext.Install();
        string output = Path.Combine(UserSimulation.Output, "scrollbars"); Directory.CreateDirectory(output);
        foreach (var scheme in new[] { ColorScheme.Dark, ColorScheme.Light, ColorScheme.WarmNeutral })
        {
            ThemePalette.Resolve(scheme).Apply(form); Application.DoEvents();
            SendMessage(text.Handle, 0x0115, 3, 0); // SB_PAGEDOWN
            Check(SendMessage(text.Handle, 0x00ce, 0, 0) > 0, "Text scrollbar must still page down");
            SendMessage(text.Handle, 0x0114, 3, 0);
            Check(text.GetPositionFromCharIndex(text.GetFirstCharIndexFromLine(0)).X < 0, "Text scrollbar must still page horizontally");
            list.EnsureVisible(50); Check(list.TopItem!.Index > 0, "List scrollbar must retain its native range");
            list.FinishResize(); list.EnsureVisible(60);
            Check(list.TopItem!.Index > 0, "Scrollbar styling must survive a recreated list handle");
            SendMessage(panel.Handle, 0x0115, 3, 0);
            Check(panel.AutoScrollPosition.Y < 0, "Options scrollbar must still page down");
            grid.FirstDisplayedScrollingRowIndex = 20;
            Check(grid.FirstDisplayedScrollingRowIndex == 20 && grid.Controls.OfType<VScrollBar>().Any(b => b.Visible),
                "Grid must retain its managed scrollbar and range");
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new(Point.Empty, form.Size)); bitmap.Save(Path.Combine(output, scheme + ".png"));
            var vertical = grid.Controls.OfType<VScrollBar>().Single(b => b.Visible);
            var horizontal = grid.Controls.OfType<HScrollBar>().Single(b => b.Visible);
            var corner = grid.PointToScreen(new(vertical.Left + vertical.Width / 2, horizontal.Top + horizontal.Height / 2));
            Check(bitmap.GetPixel(corner.X - form.Left, corner.Y - form.Top).ToArgb() == grid.BackgroundColor.ToArgb(),
                "The corner between grid scrollbars must match the grid surface");
        }
    }
    private static void ActivityHistory()
    {
        using var fixture = new Fixture();
        var shell = fixture.Shell;
        Pump(shell.NavigateAsync(new(WorkspaceSection.Activity)));
        var record = new LaunchResult(1, Guid.NewGuid(), UserSimulation.Now, UserSimulation.Now.AddSeconds(4), 4000,
            LaunchSource.Desktop, true, LaunchOutcome.Confirmed, DiagnosticCode.LaunchConfirmed, null, 0, 0);
        var warning = record with { AttemptId = Guid.NewGuid(), StartedUtc = UserSimulation.Now.AddMinutes(-1),
            Outcome = LaunchOutcome.Warning, Code = DiagnosticCode.WorkerTimeout };
        // Supply records out of order: Activity must choose the newest one itself.
        shell.Present(UserSimulation.Health(false), [warning, record], true);
        var list = WorkspaceControls.Descendants(shell).OfType<ListView>().Single(l => l.AccessibleName == "Recent launch results");
        var filter = WorkspaceControls.Descendants(shell).OfType<ComboBox>().Single(c => c.AccessibleName == "Filter launch activity");
        var refresh = filter.Parent!.Controls.OfType<Button>().Single(b => b.Text == "Refresh");
        foreach (int width in new[] { 1050, 1440 })
        {
            shell.Width = width; shell.PerformLayout(); Application.DoEvents();
            Check(Math.Abs(filter.Top + filter.Height / 2 - refresh.Top - refresh.Height / 2) <= 1,
                $"Activity dropdown must be vertically centered with its action buttons: width={width}, filter={filter.Bounds}, refresh={refresh.Bounds}");
        }
        var details = WorkspaceControls.Descendants(shell).OfType<TextBox>().Single(c => c.AccessibleName == "Selected launch details");
        Check(list.Items.Count == 2 && list.SelectedItems[0].Tag is LaunchResult r && r.AttemptId == record.AttemptId,
            "Newest launch should be selected even when history arrives out of order");
        Check(details.Text.Contains("4 seconds") && details.Text.Contains("Started (local)") && details.Text.Contains(LaunchStatusText.Limit),
            "Details should show readable duration, local time, and completion limits");
        filter.SelectedIndex = 1;
        Check(list.Items.Count == 1 && details.Text.Contains("Warning"), "Needs attention should select and explain the warning");
        shell.Present(UserSimulation.Health(false), [record, warning with { DurationMs = 5000 }], true);
        Check(details.Text.Contains("5 seconds"), "Refresh should update selected details");
        filter.SelectedIndex = 0;
        Check(list.SelectedItems[0].Tag is LaunchResult selected && selected.AttemptId == warning.AttemptId,
            "Changing filters should preserve an available selection");
        filter.SelectedIndex = 1;
        shell.Present(UserSimulation.Health(false), [record], true);
        var empty = WorkspaceControls.Descendants(shell).OfType<Label>().Single(l => l.AccessibleName == "Launch history status");
        Check(list.Items.Count == 0 && empty.Visible && empty.Text.Contains("No launches need attention"), "Filtered empty state should explain how to see all launches");
        shell.Present(UserSimulation.Health(false), [], false);
        Check(empty.Text.Contains("unavailable"), "Unreadable history must not look like an empty successful history");
        shell.Present(UserSimulation.Health(false), [], true);
        Check(empty.Text.Contains("No launches recorded yet"), "Empty history should explain when records begin");
        WorkspaceControls.Descendants(shell).OfType<Button>().Single(b => b.AccessibleName == "Open logs and reports").PerformClick();
        Check(shell.CurrentTarget == new WorkspaceTarget(WorkspaceSection.System, SystemSection.Reports), "Reports shortcut should open the reports tab");
    }
    private static void LayoutsLoading()
    {
        using var ready=new ManualResetEventSlim();
        using var fixture=new Fixture(discover:token=> { ready.Wait(token); return new([],0); });
        try
        {
            Pump(fixture.Shell.NavigateAsync(new(WorkspaceSection.Layouts)));
            var page=fixture.Shell.Layouts;
            var preview=WorkspaceControls.Descendants(page).OfType<LayoutPreview>().Single();
            UserSimulation.Wait(()=>preview.Monitors.Length>0);
            var heading=WorkspaceControls.Descendants(page).OfType<Label>().Single(l=>l.Text=="Windows");
            Check(page.Busy && heading.Enabled && heading.ForeColor==fixture.Shell.Palette.Text,
                "Loading must not disable or darken the Windows heading");
            Check(preview.Plan is null && preview.Monitors.Length==1,"Display should render before window discovery completes");
        }
        finally { ready.Set(); UserSimulation.Wait(()=>!fixture.Shell.Layouts.Busy); }
    }
    private static void ResizeScrolledList()
    {
        using var fixture = new Fixture();
        var shell=fixture.Shell;
        shell.MinimumSize=Size.Empty;
        shell.ClientSize=new(1100,950); Application.DoEvents();
        var list=WorkspaceControls.Descendants(shell.Instances).OfType<ListView>().Single();
        var painted = new HashSet<(int Row,int Column)>();
        int viewportCreations=0;
        list.HandleCreated+=(_,_)=>viewportCreations++;
        list.DrawSubItem += (_,e) => painted.Add((e.ItemIndex,e.ColumnIndex));
        int originalTop = list.GetItemRect(0).Top;
        list.Items[^1].Selected=true; Application.DoEvents();
        var selectedKey=list.Items[^1].Tag;
        for (int attempt=0;attempt<3;attempt++)
        {
            int beforeShrink=viewportCreations;
            shell.ClientSize=new(1100,600); Application.DoEvents();
            // The list's queued resize repair recreates its handle. Let that finish
            // before deliberately scrolling, otherwise it can reset the fixture.
            UserSimulation.Wait(()=>viewportCreations>beforeShrink);
            list.EnsureVisible(list.Items.Count-1); Application.DoEvents();
            shell.Instances.Present(fixture.Snapshot()); Application.DoEvents();
            Check(list.GetItemRect(0).Top<originalTop,"Fixture did not scroll");
            painted.Clear();
            int previousCreations=viewportCreations;
            shell.ClientSize=new(1100,950); Application.DoEvents();
            // No ResizeEnd/mouse-up: the viewport must recover during the drag.
            UserSimulation.Wait(()=>viewportCreations>previousCreations);
            Application.DoEvents();
            list.Update();
            Check(list.SelectedItems.Count==1 && Equals(list.SelectedItems[0].Tag,selectedKey),
                "Viewport repair lost the selected instance");
            Check(list.Columns.Cast<ColumnHeader>().Sum(column=>column.Width)<=list.ClientSize.Width,
                "Instance columns require a horizontal scrollbar");
            Check(list.GetItemRect(0).Top==originalTop,"Growing a scrolled list left a gap above its rows");
            foreach (ListViewItem row in list.Items)
                foreach (int column in new[] { 0,3,4,5 })
                    Check(painted.Contains((row.Index,column)),"A visible instance cell was not repainted after growing without hover");
            using var bitmap = new Bitmap(list.Width,list.Height);
            list.DrawToBitmap(bitmap,new(Point.Empty,list.Size));
            bitmap.Save(Path.Combine(fixture.DirectoryPath,$"resized-{attempt}.png"));
        }
    }
    internal static void Pump(Task task) { UserSimulation.Wait(()=>task.IsCompleted); task.GetAwaiter().GetResult(); }
    internal static Button Button(Control root,string text) { UiTestContext.Install(); return WorkspaceControls.Descendants(root).OfType<Button>().Single(b=>b.Text.Replace("&","") == text); }
    internal sealed class Fixture : IDisposable
    {
        internal readonly string DirectoryPath;
        internal readonly UserSimulation.Fixture Platform = new(3);
        internal readonly Services Updates;
        internal readonly AppearancePreferences Appearance;
        internal readonly DiagnosticsForm Shell;
        internal Fixture(ColorScheme scheme = ColorScheme.Dark, Func<CancellationToken,InstanceSnapshot>? discover = null)
        {
            UiTestContext.Install();
            DirectoryPath = Path.Combine(UserSimulation.Output,"workspace-fixtures",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(DirectoryPath);
            Appearance = new(Path.Combine(DirectoryPath,"appearance.json")); Appearance.Save(scheme);
            Updates = new(DirectoryPath);
            Shell = new(false, appearance: Appearance, instancePlatform: Platform,placementPlatform: Platform,
                discoverWindows:discover ?? (_=>Snapshot()),layouts:new(new LayoutStore(Path.Combine(DirectoryPath,"layouts.json"))),
                notifications:new(Path.Combine(DirectoryPath,"notifications.json")),updateServices:new(Updates,Updates,Updates,Updates));
            Shell.ShowInTaskbar=false; Shell.Opacity=0; Shell.Show(); UiTestContext.Install(); Shell.Instances.Present(Snapshot()); Shell.Present(UserSimulation.Health(false),[],true); Application.DoEvents();
        }
        internal InstanceSnapshot Snapshot() => new(Platform.Windows.Values.Select(w=>new ActiveInstance(w.Target.Key,[new(w.Target.Window,w.Caption)],2.5,620*1048576,true)).ToArray(),0);
        public void Dispose() { if (!Shell.IsDisposed && Shell.CurrentTarget.Page==WorkspaceSection.Layouts) UserSimulation.Wait(()=>!Shell.Layouts.Busy); if (!Shell.IsDisposed) Shell.Close(); UserSimulation.Wait(()=>Shell.IsDisposed); Shell.Dispose(); }
    }
    internal sealed class Services(string directory) : IReleaseSource,IUpdateVersionProvider,IUpdateDownloader,IUpdateSetupHandoff
    {
        internal bool Offline, NoUpdate, Invalid, Slow;
        internal int Downloads, Setups, Checks;
        internal static UpdateRelease Release => new(new(1,2,0,0),"Synthetic release","Synthetic release notes\n• Polished workspace",UpdateUrls.Releases,
            new("RobloxLaunchHelper.exe",new("https://github.com/LancePerdue23/roblox-multi-instance/releases/download/v1.2.0/RobloxLaunchHelper.exe"),100,new string('a',64)),null);
        public Version? Current()=>new(1,1,0,0);
        public Task<UpdateRelease?> LatestAsync(CancellationToken token)
        { Checks++; return Offline ? Task.FromException<UpdateRelease?>(new IOException()) : Task.FromResult<UpdateRelease?>(NoUpdate ? Release with { Version=new(1,1,0,0) } : Release); }
        public async Task<DownloadedUpdate> DownloadAsync(UpdateRelease release,IProgress<long>? progress,CancellationToken token)
        {
            Downloads++; progress?.Report(50);
            if (Slow) await Task.Delay(Timeout.Infinite,token);
            if (Invalid) throw new InvalidDataException();
            string path=Path.Combine(directory,"fake-download"); File.WriteAllText(path,"synthetic");
            return new(path,new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read));
        }
        public Task VerifyAsync(string path,UpdateRelease release,CancellationToken token) => Invalid ? Task.FromException(new InvalidDataException()) : Task.CompletedTask;
        public Task StartAsync(string path,UpdateRelease release) { Setups++; return Task.CompletedTask; }
    }
    internal static void Exercise(int scenario)
    {
        using var fixture = new Fixture(); var shell=fixture.Shell;
        switch(scenario)
        {
            case 0:
                var summaryCards=WorkspaceControls.Descendants(shell.Instances).OfType<StatusCard>().ToArray();
                Check(summaryCards.Length==3 && summaryCards.Max(card=>card.Width)-summaryCards.Min(card=>card.Width)<4,"Summary cards must use equal columns");
                var selectionCard=WorkspaceControls.Descendants(shell.Instances).OfType<WorkspaceCard>().Single(card=>card.AccessibleName=="Selected window");
                Check(selectionCard.Height<=170,"Selection actions must remain a compact panel");
                var instanceList = WorkspaceControls.Descendants(shell.Instances).OfType<ListView>().Single();
                var selectionTitle = WorkspaceControls.Descendants(selectionCard).OfType<Label>().Single(label => label.Text == "Select a Roblox window");
                var emptyCardBounds = selectionCard.Bounds;
                var emptyTitleLocation = selectionTitle.PointToScreen(Point.Empty);
                instanceList.Items[0].Selected = true; Application.DoEvents();
                Check(selectionCard.Bounds == emptyCardBounds && selectionTitle.PointToScreen(Point.Empty) == emptyTitleLocation,
                    "Selecting an instance shifted the details card or title");
                instanceList.Items[0].Selected = false; Application.DoEvents();
                Check(selectionCard.Bounds == emptyCardBounds && selectionTitle.PointToScreen(Point.Empty) == emptyTitleLocation,
                    "Clearing selection shifted the details card or title");
                Pump(shell.NavigateAsync(new(WorkspaceSection.Layouts)));
                Pump(shell.NavigateAsync(new(WorkspaceSection.Activity)));
                Check(shell.CurrentTarget.Page==WorkspaceSection.Activity,"Read-only layout loading must not discard navigation");
                UserSimulation.Wait(()=>!shell.Layouts.Busy);
                int forms=Application.OpenForms.Count;
                foreach(var page in Enum.GetValues<WorkspaceSection>())
                { Button(shell,page.ToString()).PerformClick(); Application.DoEvents(); if(page==WorkspaceSection.Layouts) UserSimulation.Wait(()=>!shell.Layouts.Busy); Check(shell.CurrentTarget.Page==page,"Navigation target differs"); Check(Application.OpenForms.Count==forms,"Navigation opened another window"); }
                Check(fixture.Updates.Checks==0 && fixture.Platform.Moves==0,"Navigation performed a side effect"); break;
            case 1:
                Pump(shell.NavigateAsync(new(WorkspaceSection.Settings)));
                var picker=WorkspaceControls.Descendants(shell).OfType<ComboBox>().Single(c=>c.AccessibleName=="Color scheme"); picker.SelectedIndex=2;
                Check(new AppearancePreferences(Path.Combine(fixture.DirectoryPath,"appearance.json")).Scheme==ColorScheme.WarmNeutral,"Theme did not persist");
                foreach (var control in WorkspaceControls.Descendants(shell))
                    Check(ThemePalette.For(control)==shell.Palette,"A control retained its previous paint palette");
                Pump(shell.NavigateAsync(new(WorkspaceSection.Layouts)));
                foreach (var control in WorkspaceControls.Descendants(shell.Layouts))
                    Check(ThemePalette.For(control)==shell.Palette,"A newly created layout control has the wrong palette");
                string corrupt=Path.Combine(fixture.DirectoryPath,"corrupt.json"); File.WriteAllText(corrupt,"bad"); var preferences=new AppearancePreferences(corrupt);
                Check(!preferences.Available,"Invalid settings accepted"); try { preferences.Save(ColorScheme.Light); throw new Exception("Overwrote invalid settings"); } catch(InvalidDataException) { }
                Check(File.ReadAllText(corrupt)=="bad","Invalid settings changed without reset"); preferences.Save(ColorScheme.Dark,true); break;
            case 2:
                shell.Updates.NotifyAvailable(); Check(fixture.Updates.Checks==0 && shell.Updates.HasUpdate,"Passive notice repeated the automatic network check");
                Pump(shell.NavigateAsync(new(WorkspaceSection.System,SystemSection.Updates))); Pump(shell.Updates.CheckAsync());
                Check(shell.Updates.StatusText.Contains("1.2.0"),"Available update absent");
                Check(WorkspaceControls.Descendants(shell.Updates).OfType<TextBox>().Single().Text.Contains("Synthetic release notes"),"Notes absent");
                fixture.Updates.NoUpdate=true; Pump(shell.Updates.CheckAsync()); Check(shell.Updates.StatusText.Contains("up to date"),"Current version status absent"); break;
            case 3:
                fixture.Updates.Offline=true; Pump(shell.Updates.CheckAsync()); Check(shell.Updates.StatusText.Contains("Couldn't"),"Offline failure absent");
                fixture.Updates.Offline=false; Pump(shell.Updates.CheckAsync()); Check(shell.Updates.StatusText.Contains("available"),"Retry did not recover"); break;
            case 4:
                fixture.Updates.Slow=true; Pump(shell.NavigateAsync(new(WorkspaceSection.System,SystemSection.Updates))); Pump(shell.Updates.CheckAsync());
                Button(shell.Updates,"Download update").PerformClick(); Button(shell.Updates,"Download update").PerformClick();
                Check(fixture.Updates.Downloads==1 && shell.Updates.Busy,"Duplicate download");
                Pump(shell.NavigateAsync(new(WorkspaceSection.Activity))); Check(shell.Updates.Busy,"Navigation canceled download");
                Pump(shell.NavigateAsync(new(WorkspaceSection.System,SystemSection.Updates))); Button(shell.Updates,"Cancel download").PerformClick();
                UserSimulation.Wait(()=>!shell.Updates.Busy); Check(fixture.Updates.Setups==0 && shell.Updates.StatusText.Contains("canceled"),$"Cancellation failed: busy={shell.Updates.Busy}, status={shell.Updates.StatusText}, setups={fixture.Updates.Setups}"); break;
            case 5:
                fixture.Updates.Invalid=true; Pump(shell.NavigateAsync(new(WorkspaceSection.System,SystemSection.Updates))); Pump(shell.Updates.CheckAsync());
                Button(shell.Updates,"Download update").PerformClick(); UserSimulation.Wait(()=>!shell.Updates.Busy);
                Check(shell.Updates.StatusText.Contains("SHA-256") && fixture.Updates.Setups==0,"Invalid update reached setup"); break;
            case 6:
                Pump(shell.NavigateAsync(new(WorkspaceSection.System,SystemSection.Updates))); Pump(shell.Updates.CheckAsync());
                Button(shell.Updates,"Download update").PerformClick(); UserSimulation.Wait(()=>!shell.Updates.Busy);
                Check(fixture.Updates.Setups==0,"Download installed automatically");
                Button(shell.Updates,"Run setup…").PerformClick(); UserSimulation.Wait(()=>shell.IsDisposed); Check(fixture.Updates.Setups==1,"Setup action missing"); break;
            case 7:
                int before=Application.OpenForms.Count; var confirmation=WorkspacePrompt.ConfirmAsync(shell,"Synthetic confirmation","Close window");
                Check(Application.OpenForms.Count==before,"Prompt opened another window"); Button(shell,"Cancel").PerformClick(); Pump(confirmation); Check(!confirmation.Result,"Cancel confirmed the action"); break;
            case 8:
                var list=WorkspaceControls.Descendants(shell.Instances).OfType<ListView>().Single(); list.Items[0].Selected=true; var target=shell.Instances.SelectedTarget;
                var row = list.Items[0];
                var windowPicker = WorkspaceControls.Descendants(shell.Instances).OfType<ComboBox>().Single();
                int pickerChanges = 0; windowPicker.SelectedIndexChanged += (_, _) => pickerChanges++;
                shell.Instances.Present(fixture.Snapshot());
                Check(ReferenceEquals(row,list.Items[0]) && pickerChanges==0 && shell.Instances.SelectedTarget==target,
                    "Unchanged samples must preserve the selected row and window without resetting the selector");
                Pump(shell.NavigateAsync(new(WorkspaceSection.Activity))); Pump(shell.NavigateAsync(new(WorkspaceSection.Instances)));
                shell.Instances.Present(fixture.Snapshot()); Check(target is not null && shell.Instances.SelectedTarget==target,"Navigation lost exact selection"); break;
            case 9:
                Pump(shell.NavigateAsync(new(WorkspaceSection.Layouts))); UserSimulation.Wait(()=>WorkspaceControls.Descendants(shell.Layouts).OfType<DataGridView>().Single().Rows.Count==3);
                Pump(shell.Layouts.RefreshPreviewAsync()); Check(Button(shell.Layouts,"Apply").Enabled,"Valid fixture preview must enable Apply"); Check(!UserSimulation.Clipped(Button(shell.Layouts,"Apply"),shell),"Apply must remain visible in the footer");
                var more=Button(shell.Layouts,"More options"); var refresh=Button(shell.Layouts,"Refresh windows");
                Check(more.Font.Equals(refresh.Font) && more.Height==refresh.Height,"More options differs from adjacent buttons");
                more.PerformClick(); Application.DoEvents();
                var layoutGrid=WorkspaceControls.Descendants(shell.Layouts).OfType<LayoutWindowGrid>().Single();
                Check(layoutGrid.Height>=174,"Expanded options squeezed the window list");
                var mode=WorkspaceControls.Descendants(shell.Layouts).OfType<ComboBox>().Single(c=>c.AccessibleName=="Layout");
                var monitorList=WorkspaceControls.Descendants(shell.Layouts).OfType<CheckedListBox>().Single();
                using(var image=new Bitmap(shell.Width,shell.Height))
                {
                    shell.DrawToBitmap(image,new(Point.Empty,shell.Size));
                    image.Save(Path.Combine(UserSimulation.Output,"layouts-expanded.png"));
                }
                Check(mode.Right<=mode.Parent!.ClientSize.Width && monitorList.Top>=mode.Bottom,$"Advanced controls overlap or overflow: mode {mode.Bounds}, list {monitorList.Bounds}, parent {mode.Parent.ClientSize}");
                mode.DroppedDown=true; Check(mode.DroppedDown,"The themed layout picker no longer opens"); mode.DroppedDown=false;
                var normalSize=shell.Size;
                shell.Size=shell.MinimumSize; Application.DoEvents();
                Check(layoutGrid.Height>=174 && layoutGrid.RectangleToScreen(layoutGrid.ClientRectangle).Bottom<=refresh.PointToScreen(Point.Empty).Y,
                    "Expanded controls overlap the window list at minimum size");
                var editorCard=(WorkspaceScrollViewport)layoutGrid.Parent!.Parent!;
                var reset=Button(shell.Layouts,"Reset saved settings…");
                editorCard.ScrollControlIntoView(reset); Application.DoEvents();
                Check(!UserSimulation.Clipped(reset,shell),"Advanced settings cannot be reached by scrolling");
                using(var image=new Bitmap(shell.Width,shell.Height))
                {
                    shell.DrawToBitmap(image,new(Point.Empty,shell.Size));
                    image.Save(Path.Combine(UserSimulation.Output,"layouts-expanded-narrow.png"));
                }
                mode.Focus(); editorCard.AutoScrollPosition=Point.Empty;
                int selectedMode=mode.SelectedIndex;
                SendMessage(mode.Handle,0x020a,unchecked((nint)(-120<<16)),0);
                Application.DoEvents();
                Check(mode.SelectedIndex==selectedMode,"Scrolling over a closed layout picker changed the layout");
                Check(editorCard.AutoScrollPosition.Y<0,"The closed layout picker swallowed panel scrolling");
                // Scroll back and forth across the frame: no copied border fragments
                // should appear in its otherwise empty inner padding.
                var frame=editorCard.Parent!;
                foreach(int offset in new[]{0,100,300,0,200})
                {
                    editorCard.AutoScrollPosition=new(0,offset); Application.DoEvents();
                    using var image=new Bitmap(frame.Width,frame.Height);
                    frame.DrawToBitmap(image,new(Point.Empty,frame.Size));
                    for(int y=12;y<frame.Height-12;y++)
                        Check(image.GetPixel(4,y).ToArgb()==shell.Palette.Surface.ToArgb(),"Scrolling left border fragments inside the layout card");
                }
                shell.Size=normalSize;
                more.PerformClick();
                var grid=WorkspaceControls.Descendants(shell.Layouts).OfType<DataGridView>().Single(); grid.CurrentCell=grid.Rows[1].Cells[1]; Button(shell.Layouts,"Move up").PerformClick();
                Check(!Button(shell.Layouts,"Apply").Enabled && fixture.Platform.Moves==0,"Reorder retained an actionable stale preview"); break;
        }
    }
    internal static void RenderAll(bool quick=false)
    {
        string output=Path.Combine(UserSimulation.Output,"workspace-renders"); Directory.CreateDirectory(output);
        var targets=new WorkspaceTarget[] { new(WorkspaceSection.Instances),new(WorkspaceSection.Layouts),new(WorkspaceSection.Activity),
            new(WorkspaceSection.System),new(WorkspaceSection.System,SystemSection.Updates),new(WorkspaceSection.System,SystemSection.Reports),
            new(WorkspaceSection.Settings),new(WorkspaceSection.Settings,Settings:SettingsSection.Notifications),new(WorkspaceSection.Settings,Settings:SettingsSection.Shortcuts) };
        foreach(var scheme in new[]{ColorScheme.Dark,ColorScheme.Light,ColorScheme.WarmNeutral}) foreach(int scale in quick?new[]{100}:new[]{0,100,150,200}) foreach(var target in targets)
        {
            using var fixture=new Fixture(scheme); var shell=fixture.Shell;
            if(target.Page==WorkspaceSection.Activity)
            {
                var record=new LaunchResult(1,Guid.NewGuid(),UserSimulation.Now,UserSimulation.Now.AddSeconds(4),4000,LaunchSource.Desktop,true,LaunchOutcome.Confirmed,DiagnosticCode.LaunchConfirmed,null,0,0);
                shell.Present(UserSimulation.Health(false),[record,record with { StartedUtc=UserSimulation.Now.AddMinutes(-5),Outcome=LaunchOutcome.Warning,Code=DiagnosticCode.WorkerTimeout }],true);
            }
            Pump(shell.NavigateAsync(target));
            if(target.Page is WorkspaceSection.Instances or WorkspaceSection.Activity)
            {
                var list=target.Page==WorkspaceSection.Instances?WorkspaceControls.Descendants(shell.Instances).OfType<ListView>().Single():WorkspaceControls.Descendants(shell).OfType<ListView>().Single(l=>l.AccessibleName=="Recent launch results");
                if(list.Items.Count>0)list.Items[0].Selected=true;
            }
            if(target.Page==WorkspaceSection.Layouts) UserSimulation.Wait(()=>!shell.Layouts.Busy && Button(shell.Layouts,"Apply").Enabled);
            if(target.System==SystemSection.Updates) Pump(shell.Updates.CheckAsync());
            if(scale==0) shell.Size=shell.MinimumSize;
            else if(scale!=100) shell.Scale(new SizeF(scale/100f,scale/100f)); shell.PerformLayout(); Application.DoEvents();
            if(target.Page==WorkspaceSection.Layouts) { UiTestContext.Install(); Pump(shell.Layouts.RefreshPreviewAsync()); Check(Button(shell.Layouts,"Apply").Enabled,"Rendered preview must be ready"); Check(!UserSimulation.Clipped(Button(shell.Layouts,"Apply"),shell),"Apply is clipped in a rendered layout"); }
            string name=$"{scheme}-{target.Page}-{(target.Page==WorkspaceSection.Settings?(int)target.Settings:(int)target.System)}-{scale}.png";
            using var bitmap=new Bitmap(shell.Width,shell.Height); shell.DrawToBitmap(bitmap,new(Point.Empty,shell.Size)); bitmap.Save(Path.Combine(output,name));
        }
    }
}
