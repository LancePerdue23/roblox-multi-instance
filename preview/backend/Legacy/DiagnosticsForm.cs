using System.Diagnostics;

namespace RobloxLaunchHelper;

internal sealed partial class DiagnosticsForm : Form, IWorkspaceNavigation
{
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(1100, 0), Text = "Loading configuration checks…" };
    private readonly ListView checks = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, AccessibleName = "Health checks" };
    private readonly ListView launches = new WorkspaceListView() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Recent launch results" };
    private readonly TextBox detail = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AccessibleName = "Selected diagnostic details" };
    private readonly FlowLayoutPanel buttons = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private readonly CancellationTokenSource closed = new();
    private readonly DiagnosticHistory history = new();
    private readonly Button integration;
    private readonly Button launchRoblox;
    private readonly Func<ProcessStartInfo, CancellationToken, Task> launchProcess;
    private readonly Action<string> launchError;
    private readonly Func<bool, CancellationToken, Task> setBrowserIntegration;
    private ActiveInstancesPage? activeInstances;
    private readonly ManagerHost? managerHost;
    private HealthSnapshot? snapshot;
    private LaunchResult[] recent = [];
    private bool historyAvailable;
    private HealthCheckResult worker = DiagnosticChecks.NotTested;
    internal DiagnosticsForm(bool collectOnShown = true, ManagerHost? managerHost = null,
        Func<ProcessStartInfo, CancellationToken, Task>? launchProcess = null, Action<string>? launchError = null, AppearancePreferences? appearance = null,
        IInstancePlatform? instancePlatform = null, IPlacementPlatform? placementPlatform = null,
        Func<CancellationToken, InstanceSnapshot>? discoverWindows = null, LayoutPreferences? layouts = null,
        NotificationPreferences? notifications = null, UpdateServices? updateServices = null,
        Func<bool, CancellationToken, Task>? setBrowserIntegration = null)
    {
        this.appearance = appearance ?? new AppearancePreferences();
        this.instancePlatform = instancePlatform; this.placementPlatform = placementPlatform; this.discoverWindows = discoverWindows;
        injectedLayouts = layouts; injectedNotifications = notifications; injectedUpdates = updateServices; testMode = !collectOnShown;
        this.managerHost = managerHost;
        this.setBrowserIntegration = setBrowserIntegration ?? BrowserIntegration.SetAsync;
        this.launchProcess = launchProcess ?? RunLaunchChildAsync;
        this.launchError = launchError ?? (message => workspaceStatus.Text = message);
        launchRoblox = new WorkspaceButton { Text = "&Launch Roblox", AutoSize = true, Margin = new Padding(4), AccessibleName = "Launch Roblox" };
        launchRoblox.Click += async (_, _) => await LaunchRobloxAsync();

        AddButton("&Check again", RefreshAsync); AddButton("&Test worker", TestAsync); AddButton("&Recover/Repair", () => InstallerAsync("--recover"));
        integration = AddButton("Disable browser integration", ToggleIntegrationAsync);
        integration.Enabled = false;
        AddButton("&Setup", () => InstallerAsync("--setup")); AddButton("&Export report…", ExportAsync); AddButton("&Clear history…", ClearAsync);
        AddButton("User logs", () => OpenLogsAsync(0)); AddButton("Worker logs", () => OpenLogsAsync(1)); AddButton("Setup logs", () => OpenLogsAsync(2));
        launches.SelectedIndexChanged += (_, _) => ShowActivityDetails();
        BuildWorkspace(collectOnShown);
        if (collectOnShown) Shown += async (_, _) => await RunAsync(RefreshAsync);
        FormClosed += (_, _) => closed.Cancel();
    }
    internal Task InstanceCompletion => activeInstances?.Completion ?? Task.CompletedTask;
    private static async Task RunLaunchChildAsync(ProcessStartInfo start, CancellationToken token)
    {
        using var process = Process.Start(start) ?? throw new IOException("The Roblox launcher could not start.");
        // Cancel only the wait when the dashboard closes. Disposing Process never terminates the child.
        await process.WaitForExitAsync(token);
    }
    internal async Task LaunchRobloxAsync()
    {
        if (IsDisposed || !launchRoblox.Enabled) return;
        launchRoblox.Enabled = false;
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add("--launch-roblox");
            try { await launchProcess(start, closed.Token); }
            catch (OperationCanceledException) when (closed.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                if (!IsDisposed && !closed.IsCancellationRequested)
                    launchError($"The Roblox launcher could not start or be monitored (0x{e.HResult:X8}). Try again or check the user logs.");
                return;
            }
            // The child owns all launch warnings; do not repeat them based on its exit code.
            if (!IsDisposed && !closed.IsCancellationRequested) await RunAsync(CollectAsync);
        }
        finally { if (!IsDisposed && !closed.IsCancellationRequested) launchRoblox.Enabled = true; }
    }
    internal Task OpenInstancesAsync(ArrangementKind? command = null) => command is ArrangementKind kind
        ? OpenLayoutAsync(kind) : NavigateAsync(new(WorkspaceSection.Instances));    private Button AddButton(string text, Func<Task> action)
    {
        var button = new WorkspaceButton { Text = text, AutoSize = true, Margin = new Padding(4) };
        button.Click += async (_, _) => await RunAsync(action); buttons.Controls.Add(button);
        return button;
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (IsDisposed || !buttons.Enabled) return;
        buttons.Enabled = false;
        activeInstances?.SetIntegration(snapshot?.BrowserIntegrationEnabled, changing: true);
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) workspaceStatus.Text = $"The action could not finish (0x{e.HResult:X8}). Try again or check the logs."; }
        finally
        {
            if (!IsDisposed)
            {
                buttons.Enabled = true;
                activeInstances?.SetIntegration(snapshot?.BrowserIntegrationEnabled);
            }
        }
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
        RenderActivity();
        integrationStatus.Text = "Browser integration   " + (health.BrowserIntegrationEnabled is true ? "Enabled" : health.BrowserIntegrationEnabled is false ? "Disabled" : "Unknown");
        systemStatus.Text = health.Overall == CheckState.Healthy ? "● System ready" : "● " + health.Overall;
        activeInstances?.SetIntegration(health.BrowserIntegrationEnabled, changing: !buttons.Enabled);
    }
    private async Task TestAsync()
    {
        status.Text = "Testing authenticated worker startup; Roblox will not be launched…";
        worker = await DiagnosticChecks.TestWorkerAsync(closed.Token);
        if (!IsDisposed) await CollectAsync();
    }
    private async Task ToggleIntegrationAsync()
    {
        if (snapshot?.BrowserIntegrationEnabled is not bool enabled) return;
        workspaceStatus.Text = enabled ? "Disabling browser integration…" : "Enabling browser integration…";
        try
        {
            await setBrowserIntegration(!enabled, closed.Token);
            if (!IsDisposed && !closed.IsCancellationRequested)
            {
                await RefreshAsync();
                workspaceStatus.Text = snapshot?.BrowserIntegrationEnabled == !enabled
                    ? "Browser integration " + (!enabled ? "enabled." : "disabled.")
                    : "The change could not be confirmed. Check System → Health and try again.";
            }
        }
        catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (IsDisposed || closed.IsCancellationRequested) return;
            // A disconnected client may have missed a completed transaction. Read the saved state again.
            try { await RefreshAsync(); } catch { }
            workspaceStatus.Text = e is UserError ? e.Message : "Browser integration could not be changed. Check System → Health and try again.";
        }
    }
    private async Task InstallerAsync(string mode)
    {
        // A dashboard opened from a newer download must repair using that payload, not the older installed copy.
        string executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = true }; start.ArgumentList.Add(mode);
        using var process = Process.Start(start) ?? throw new IOException();
        await process.WaitForExitAsync(closed.Token);
        bool canceled = process.ExitCode == 1223;
        if (!canceled && process.ExitCode != 0) workspaceStatus.Text = "The installation action did not complete. Check setup logs and run Check again.";
        await RefreshAsync();
        if (!IsDisposed && canceled) workspaceStatus.Text = "Administrator approval was canceled. No repair failure was recorded.";
    }
    private Task OpenLogsAsync(int index)
    {
        string path = DiagnosticReports.LogDirectories[index];
        if (!Directory.Exists(path)) workspaceStatus.Text = "This log folder does not exist yet. Some logs are created only after setup or a launch.";
        else { try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { path } })?.Dispose(); }
            catch { workspaceStatus.Text = "This log folder could not be opened with your current permissions."; } }
        return Task.CompletedTask;
    }
    private async Task ExportAsync()
    {
        if (snapshot is null) return;
        var package = await Task.Run(() => DiagnosticReports.Build(snapshot, recent, historyAvailable)).WaitAsync(TimeSpan.FromSeconds(35), closed.Token);
        if (IsDisposed) return;
        if (!await WorkspacePrompt.ConfirmAsync(this, $"Export configuration checks, versions, UTC timestamps/durations, {package.LaunchCount} launch results and {package.EventCount} sanitized log events?\n\nNo identities, paths, launch links, commands or raw logs are included. Timestamps and launch activity may still be sensitive. Nothing is uploaded. Review the report before sharing.", "Export report")) return;
        using var dialog = new SaveFileDialog { Filter = "Troubleshooting ZIP (*.zip)|*.zip", FileName = "RobloxMultiInstance-report.zip", DefaultExt = "zip", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await Task.Run(() => DiagnosticReports.Write(dialog.FileName, package));
        if (!IsDisposed) workspaceStatus.Text = "Redacted troubleshooting report saved locally. Review it before sharing. No data was uploaded.";
    }
    private async Task ClearAsync()
    {
        if (!await WorkspacePrompt.ConfirmAsync(this, "Clear structured recent-launch history? Existing text logs and exported reports remain. Concurrent launches may create new records.", "Clear history")) return;
        if (!await Task.Run(history.Clear)) workspaceStatus.Text = "History could not be cleared. Check access and try again.";
        await RefreshActivityAsync();
    }
}
