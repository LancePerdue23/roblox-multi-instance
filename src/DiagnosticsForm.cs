using System.Diagnostics;

namespace RobloxLaunchHelper;

internal sealed class DiagnosticsForm : Form
{
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(1100, 0), Text = "Loading configuration checks…" };
    private readonly ListView checks = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, AccessibleName = "Health checks" };
    private readonly ListView launches = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, AccessibleName = "Recent launch results" };
    private readonly TextBox detail = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AccessibleName = "Selected diagnostic details" };
    private readonly FlowLayoutPanel buttons = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private readonly CancellationTokenSource closed = new();
    private readonly DiagnosticHistory history = new();
    private readonly Button integration;
    private ActiveInstancesForm? activeInstances;
    private HealthSnapshot? snapshot;
    private LaunchResult[] recent = [];
    private bool historyAvailable;
    private HealthCheckResult worker = DiagnosticChecks.NotTested;
    internal DiagnosticsForm(bool collectOnShown = true)
    {
        Text = "Roblox Multi Instance — Diagnostics"; Size = new(1100, 760); MinimumSize = new(740, 560);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 45));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 35));
        layout.RowStyles.Add(new(SizeType.Percent, 20)); layout.RowStyles.Add(new(SizeType.AutoSize));
        checks.Columns.Add("Check", 165); checks.Columns.Add("Status", 90); checks.Columns.Add("Message / next step", 760);
        launches.Columns.Add("Started (local)", 165); launches.Columns.Add("Source", 95); launches.Columns.Add("Result", 100); launches.Columns.Add("Message", 650);
        layout.Controls.Add(status, 0, 0); layout.Controls.Add(checks, 0, 1);
        layout.Controls.Add(new Label { Text = "Recent launches — up to 100 attempts / 30 days. Select a row for details; Check again reloads results.", AutoSize = true }, 0, 2);
        layout.Controls.Add(launches, 0, 3); layout.Controls.Add(detail, 0, 4); layout.Controls.Add(buttons, 0, 5); Controls.Add(layout);
        AddButton("&Check again", RefreshAsync); AddButton("&Test worker", TestAsync); AddButton("&Recover/Repair", () => InstallerAsync("--recover"));
        integration = AddButton("Disable browser integration", () => InstallerAsync(snapshot?.BrowserIntegrationEnabled == false ? "--enable-browser-integration" : "--disable-browser-integration"));
        integration.Enabled = false;
        AddButton("&Setup", () => InstallerAsync("--setup")); AddButton("&Export report", ExportAsync); AddButton("&Clear history", ClearAsync);
        AddButton("&Active instances", async () =>
        {
            // A closed panel's native call may still be draining. Serialize across
            // form lifetimes as well as hide/show generations within one panel.
            if (activeInstances is { IsDisposed: true }) await activeInstances.Completion;
            if (IsDisposed) return;
            if (activeInstances is null || activeInstances.IsDisposed) activeInstances = new ActiveInstancesForm();
            if (!activeInstances.Visible) activeInstances.Show(this);
            if (activeInstances.WindowState == FormWindowState.Minimized) activeInstances.WindowState = FormWindowState.Normal;
            activeInstances.Activate();
        });
        AddButton("Launch notifications…", () => { using var form = new NotificationSettingsForm(); form.ShowDialog(this); return Task.CompletedTask; });
        AddButton("User &logs", () => OpenLogsAsync(0)); AddButton("&Worker logs", () => OpenLogsAsync(1)); AddButton("Setup l&ogs", () => OpenLogsAsync(2));
        checks.SelectedIndexChanged += (_, _) => { if (checks.SelectedItems.Count > 0) detail.Text = checks.SelectedItems[0].Tag as string ?? ""; };
        launches.SelectedIndexChanged += (_, _) => { if (launches.SelectedItems.Count > 0) detail.Text = launches.SelectedItems[0].Tag as string ?? ""; };
        if (collectOnShown) Shown += async (_, _) => await RunAsync(RefreshAsync);
        FormClosed += (_, _) => closed.Cancel();
    }
    private Button AddButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(4) };
        button.Click += async (_, _) => await RunAsync(action); buttons.Controls.Add(button);
        return button;
    }
    private async Task RunAsync(Func<Task> action)
    {
        buttons.Enabled = false;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) MessageBox.Show(this, $"The diagnostic action could not finish (0x{e.HResult:X8}). Launch behavior is unaffected.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { if (!IsDisposed) buttons.Enabled = true; }
    }
    private async Task RefreshAsync()
    {
        worker = DiagnosticChecks.NotTested; await CollectAsync();
    }
    private async Task CollectAsync()
    {
        status.Text = "Checking configuration… Worker startup is tested only when you select Test worker.";
        var collected = await Task.Run(() =>
        {
            var health = DiagnosticChecks.Collect(worker);
            bool available = history.Read(out var records);
            return (health, available, records);
        }).WaitAsync(TimeSpan.FromSeconds(35), closed.Token);
        if (IsDisposed) return;
        Present(collected.health, collected.records, collected.available);
    }
    internal void Present(HealthSnapshot health, LaunchResult[] records, bool available)
    {
        snapshot = health; historyAvailable = available; recent = records; worker = health.WorkerTest;
        integration.Text = health.BrowserIntegrationEnabled == false ? "Enable browser integration" : "Disable browser integration";
        integration.Enabled = health.BrowserIntegrationEnabled is not null;
        status.Text = $"Configuration: {snapshot.Overall} • checked {snapshot.CollectedUtc.LocalDateTime:g}. Worker test: {worker.State}.\n" +
            "Idle is normal. These checks do not verify sign-in or in-game success. Registry checks describe this process's view; Setup/Repair verifies Windows context separately.";
        checks.Items.Clear();
        foreach (var check in snapshot.Checks.Append(worker))
        {
            string message = DiagnosticMessages.Text(check.Code);
            var row = new ListViewItem([check.Id, check.State.ToString(), message]);
            row.Tag = $"{check.Id}: {check.State}\r\n{message}\r\nObserved: {check.ObservedUtc.LocalDateTime:g}\r\nAction: {check.Action}\r\nError code: {check.ErrorCode}";
            checks.Items.Add(row);
        }
        launches.Items.Clear();
        foreach (var launch in recent)
        {
            string message = LaunchStatusText.Text(launch.Outcome, launch.Code, launch.RobloxStarted);
            var row = new ListViewItem([launch.StartedUtc.LocalDateTime.ToString("g"), launch.Source.ToString(), launch.Outcome.ToString(), message]);
            row.Tag = $"{message}\r\nStarted: {launch.StartedUtc:O}\r\nEnded: {launch.EndedUtc:O}\r\nElapsed ms: {launch.DurationMs}\r\nRoblox started: {launch.RobloxStarted?.ToString() ?? "Unknown"}\r\nError code: {launch.ErrorCode}";
            launches.Items.Add(row);
        }
        detail.Text = recent.Length == 0 ? historyAvailable ? "No recent launch results. History starts with this version; existing logs are not reconstructed as launch outcomes." : DiagnosticMessages.Text(DiagnosticCode.HistoryUnavailable) : "Select a health check or launch result for details.";
    }
    private async Task TestAsync()
    {
        status.Text = "Testing authenticated worker startup; Roblox will not be launched…";
        worker = await DiagnosticChecks.TestWorkerAsync(closed.Token);
        if (!IsDisposed) await CollectAsync();
    }
    private async Task InstallerAsync(string mode)
    {
        // A dashboard opened from a newer download must repair using that payload, not the older installed copy.
        string executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = true }; start.ArgumentList.Add(mode);
        using var process = Process.Start(start) ?? throw new IOException();
        await process.WaitForExitAsync(closed.Token);
        bool canceled = process.ExitCode == 1223;
        if (!canceled && process.ExitCode != 0) MessageBox.Show(this, "The installation action did not complete. Check setup logs and run Check again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        await RefreshAsync();
        if (!IsDisposed && canceled) detail.Text = "Administrator approval was canceled. No repair failure was recorded.";
    }
    private Task OpenLogsAsync(int index)
    {
        string path = DiagnosticReports.LogDirectories[index];
        if (!Directory.Exists(path)) MessageBox.Show(this, "This log folder does not exist yet. Some logs are created only after setup or a launch.", Text);
        else { try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { path } })?.Dispose(); }
            catch { MessageBox.Show(this, "This log folder could not be opened with your current permissions.", Text); } }
        return Task.CompletedTask;
    }
    private async Task ExportAsync()
    {
        if (snapshot is null) return;
        var package = await Task.Run(() => DiagnosticReports.Build(snapshot, recent, historyAvailable)).WaitAsync(TimeSpan.FromSeconds(35), closed.Token);
        if (IsDisposed) return;
        if (MessageBox.Show(this, $"Export configuration checks, versions, UTC timestamps/durations, {package.LaunchCount} launch results and {package.EventCount} sanitized log events?\n\nNo identities, paths, launch links, commands or raw logs are included. Timestamps and launch activity may still be sensitive. Nothing is uploaded. Review the report before sharing.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        using var dialog = new SaveFileDialog { Filter = "Troubleshooting ZIP (*.zip)|*.zip", FileName = "RobloxMultiInstance-report.zip", DefaultExt = "zip", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await Task.Run(() => DiagnosticReports.Write(dialog.FileName, package));
        if (!IsDisposed) detail.Text = "Redacted troubleshooting report saved locally. Review it before sharing. No data was uploaded.";
    }
    private async Task ClearAsync()
    {
        if (MessageBox.Show(this, "Clear structured recent-launch history? Existing text logs and exported reports remain. Concurrent launches may create new records.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        if (!await Task.Run(history.Clear)) MessageBox.Show(this, "History could not be cleared. Check access and try again.", Text);
        await RefreshAsync();
    }
}
