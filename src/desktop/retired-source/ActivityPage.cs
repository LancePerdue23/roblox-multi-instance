namespace RobloxLaunchHelper;

internal sealed partial class DiagnosticsForm
{
    private readonly ComboBox activityFilter = new LayoutComboBox() { ButtonShape = true, DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Margin = new(4), AccessibleName = "Filter launch activity" };
    private readonly Label activitySummary = new() { AutoSize = true, Tag = "muted", Margin = new(0, 4, 0, 12) };
    private readonly Label activityEmpty = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Tag = "muted", AccessibleName = "Launch history status" };
    private readonly FlowLayoutPanel activityActions = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new(0) };
    private bool refreshingActivity;

    private Control BuildActivity()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        page.RowStyles.Add(new(SizeType.AutoSize));
        page.RowStyles.Add(new(SizeType.AutoSize));
        page.RowStyles.Add(new(SizeType.Percent, 100));
        page.RowStyles.Add(new(SizeType.AutoSize));
        page.RowStyles.Add(new(SizeType.Absolute, 210));
        activityFilter.Items.AddRange(["All launches", "Needs attention"]);
        activityFilter.SelectedIndex = 0;
        activityFilter.SelectedIndexChanged += (_, _) => RenderActivity();
        activityActions.Controls.Add(activityFilter);
        void Action(string text, Func<Task> action)
        {
            var button = WorkspaceControls.Button(text, async () => await RunAsync(action));
            button.UseMnemonic = false;
            button.AccessibleName = text;
            if (text == "Logs & reports") button.AccessibleName = "Open logs and reports";
            activityActions.Controls.Add(button);
        }
        Action("Refresh", RefreshActivityAsync);
        Action("Logs & reports", () => NavigateAsync(new(WorkspaceSection.System, SystemSection.Reports)));
        // Keep the existing confirmation and recovery behavior, with the action beside its history.
        var clear = buttons.Controls.OfType<Button>().Single(b => b.Text.Contains("Clear history"));
        activityActions.Controls.Add(clear);
        var refresh = activityActions.Controls.OfType<Button>().Single(b => b.Text == "Refresh");
        void AlignFilter()
        {
            if (activityFilter.DrawMode != DrawMode.Normal)
            {
                int itemHeight = Math.Max(1, activityFilter.ItemHeight + refresh.Height - activityFilter.Height);
                if (activityFilter.ItemHeight != itemHeight) activityFilter.ItemHeight = itemHeight;
            }
            // Native owner-drawn ComboBox reports a preferred height smaller than
            // its actual height. Derive spacing from actual bounds, including DPI changes.
            int gap = Math.Max(0, (refresh.Height - activityFilter.Height) / 2);
            var margin = activityFilter.Margin;
            var centered = new Padding(margin.Left, refresh.Margin.Top + gap, margin.Right, refresh.Margin.Bottom + gap);
            if (margin != centered) activityFilter.Margin = centered;
        }
        refresh.SizeChanged += (_, _) => AlignFilter();
        activityFilter.SizeChanged += (_, _) => AlignFilter();
        activityActions.Layout += (_, _) => AlignFilter();
        AlignFilter();
        page.Controls.Add(activityActions, 0, 0);
        page.Controls.Add(activitySummary, 0, 1);
        launches.Tag = "activity";
        launches.ShowItemToolTips = true;
        launches.Columns.Add("Started (local)", 210);
        launches.Columns.Add("Source", 110);
        launches.Columns.Add("Result", 160);
        launches.Columns.Add("Message", 400);
        var table = new Panel { Dock = DockStyle.Fill };
        table.Controls.Add(launches);
        table.Controls.Add(activityEmpty);
        page.Controls.Add(WorkspaceDesign.Card(table, new(8)), 0, 2);
        page.Controls.Add(new Label { AutoSize = true, Text = "Launch details", Font = new("Segoe UI", 12, FontStyle.Bold), Margin = new(0, 12, 0, 6) }, 0, 3);
        detail.Tag = "borderless";
        detail.Font = new("Segoe UI", 10);
        detail.AccessibleName = "Selected launch details";
        page.Controls.Add(WorkspaceDesign.Card(detail, new(14)), 0, 4);
        RenderActivity();
        return page;
    }

    private async Task RefreshActivityAsync()
    {
        if (refreshingActivity || closed.IsCancellationRequested) return;
        refreshingActivity = true;
        activityActions.Enabled = false;
        activitySummary.Text = "Refreshing launch history…";
        try
        {
            var result = await Task.Run(() =>
            {
                bool available = history.Read(out var records);
                return (available, records);
            }).WaitAsync(TimeSpan.FromSeconds(35), closed.Token);
            if (IsDisposed || closed.IsCancellationRequested) return;
            historyAvailable = result.available;
            recent = result.records;
            RenderActivity();
        }
        finally
        {
            refreshingActivity = false;
            if (!IsDisposed && !closed.IsCancellationRequested)
            {
                activityActions.Enabled = true;
                RenderActivity();
            }
        }
    }

    private void RenderActivity()
    {
        var selected = launches.SelectedItems.Count > 0 ? launches.SelectedItems[0].Tag as LaunchResult : null;
        var records = recent.Where(r => activityFilter.SelectedIndex != 1 ||
            r.Outcome is LaunchOutcome.Warning or LaunchOutcome.Failed or LaunchOutcome.Incomplete)
            .OrderByDescending(r => r.StartedUtc).ToArray();
        launches.BeginUpdate();
        try
        {
            launches.Items.Clear();
            foreach (var launch in records)
            {
                var row = new ListViewItem([launch.StartedUtc.LocalDateTime.ToString("g"), ActivitySource(launch.Source),
                    ActivityResult(launch.Outcome), LaunchStatusText.Text(launch.Outcome, launch.Code, launch.RobloxStarted)])
                    { Tag = launch, ToolTipText = LaunchStatusText.Text(launch.Outcome, launch.Code, launch.RobloxStarted) };
                launches.Items.Add(row);
            }
            if (launches.Items.Count > 0)
            {
                var row = launches.Items.Cast<ListViewItem>().FirstOrDefault(r => ((LaunchResult)r.Tag!).AttemptId == selected?.AttemptId) ?? launches.Items[0];
                row.Selected = true;
                row.Focused = true;
            }
        }
        finally { launches.EndUpdate(); }
        activitySummary.Text = historyAvailable ? $"{records.Length} shown · {recent.Length} recorded · Up to 100 attempts / 30 days" : "Launch history unavailable · Refresh or clear history to try again";
        activityEmpty.Text = !historyAvailable ? DiagnosticMessages.Text(DiagnosticCode.HistoryUnavailable) : recent.Length == 0
            ? "No launches recorded yet.\nLaunch Roblox to start your history. Older text logs are not included."
            : "No launches need attention.\nChoose All launches to see your history.";
        activityEmpty.Visible = records.Length == 0;
        if (activityEmpty.Visible) activityEmpty.BringToFront();
        ShowActivityDetails();
        PaintActivityResults();
    }

    private void ShowActivityDetails()
    {
        if (launches.SelectedItems.Count == 0 || launches.SelectedItems[0].Tag is not LaunchResult launch)
        {
            detail.Text = LaunchStatusText.Limit;
            return;
        }
        string elapsed = launch.DurationMs is long ms ? $"{ms / 1000d:0.###} seconds" : "Not yet available";
        string next = launch.Outcome switch
        {
            LaunchOutcome.InProgress => "Refresh to see the final result.",
            LaunchOutcome.Confirmed => "No action needed.",
            LaunchOutcome.Incomplete => "Check your Roblox window, then open System → Health if you need to troubleshoot.",
            _ => "Open System → Health for checks and repair options, or Logs & reports for troubleshooting."
        };
        detail.Text = $"{ActivityResult(launch.Outcome)} · {ActivitySource(launch.Source)}\r\n" +
            $"{LaunchStatusText.Text(launch.Outcome, launch.Code, launch.RobloxStarted)}\r\n" +
            $"Started (local): {launch.StartedUtc.LocalDateTime:G}    Ended (local): {launch.EndedUtc?.LocalDateTime.ToString("G") ?? "Not recorded"}\r\n" +
            $"Duration: {elapsed}    Roblox started: {(launch.RobloxStarted is true ? "Yes" : launch.RobloxStarted is false ? "No" : "Unknown")}" +
            (launch.ErrorCode is int error ? $"    Error code: {error}" : "") +
            $"\r\nNext step: {next}\r\n{LaunchStatusText.Limit}";
    }

    private static string ActivitySource(LaunchSource source) => source == LaunchSource.Desktop ? "Desktop" : "Browser";
    private static string ActivityResult(LaunchOutcome outcome) => outcome switch
    {
        LaunchOutcome.InProgress => "In progress",
        LaunchOutcome.Confirmed => "Helper complete",
        LaunchOutcome.Warning => "Warning",
        LaunchOutcome.Failed => "Failed",
        _ => "Incomplete"
    };

    private void PaintActivityResults()
    {
        foreach (ListViewItem row in launches.Items)
        {
            row.UseItemStyleForSubItems = false;
            if (row.Tag is not LaunchResult launch) continue;
            row.SubItems[2].ForeColor = SystemInformation.HighContrast ? Palette.Text : launch.Outcome switch
            {
                LaunchOutcome.Failed => Palette.Text.R > 150 ? Color.FromArgb(255, 151, 155) : Color.FromArgb(166, 35, 45),
                LaunchOutcome.Warning or LaunchOutcome.Incomplete => Palette.Text.R > 150 ? Color.FromArgb(237, 196, 121) : Color.FromArgb(132, 84, 10),
                _ => Palette.Accent
            };
        }
        launches.Invalidate();
    }
}
