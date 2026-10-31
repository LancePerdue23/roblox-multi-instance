using System.Diagnostics;
namespace RobloxLaunchHelper;
internal sealed class UpdateSetupForm : Form
{
    private readonly Version version;
    private readonly IUpdateSetupOperations operations;
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(470, 0) };
    private readonly Button retry = new() { Text = "Retry setup", AutoSize = true, Visible = false };
    private readonly Button open = new() { Text = "Open app", AutoSize = true, Visible = false };
    private readonly CancellationTokenSource closed = new();
    private bool running;
    internal UpdateSetupForm(Process parent, Version version, long size, string digest)
    {
        this.version = version; operations = new UpdateSetupOperations(parent, version, size, digest);
        Text = "Roblox Multi Instance — Update setup"; ClientSize = new(510, 230); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(18) };
        layout.Controls.Add(status); layout.Controls.Add(retry); layout.Controls.Add(open); Controls.Add(layout);
        retry.Click += async (_, _) => await InstallAsync();
        open.Click += (_, _) =>
        {
            try { var start = new ProcessStartInfo(Program.InstalledExe) { UseShellExecute = false }; start.ArgumentList.Add("--diagnostics"); Process.Start(start)?.Dispose(); Close(); }
            catch { status.Text = "The app could not be opened. Use Diagnostics in the Start menu."; }
        };
        Shown += async (_, _) => await InstallAsync();
        FormClosing += (_, e) => { if (running) { e.Cancel = true; return; } closed.Cancel(); };
    }
    private async Task InstallAsync()
    {
        if (running) return;
        running = true; retry.Visible = false;
        try
        {
            await UpdateSetupFlow.RunAsync(operations, version, text => status.Text = text, closed.Token);
            status.Text = $"Updated to version {UpdateVersion.Label(version)}."; open.Visible = true;
        }
        catch (UserError e) { status.Text = e.Message; retry.Visible = true; }
        catch (Exception) { status.Text = "Update setup could not finish. The downloaded update is retained. Close other helper windows and try again."; retry.Visible = true; }
        finally { running = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { closed.Cancel(); closed.Dispose(); operations.Dispose(); }
        base.Dispose(disposing);
    }
}
