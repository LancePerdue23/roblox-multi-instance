using System.Diagnostics;
using System.Net.Http;
namespace RobloxLaunchHelper;

internal sealed record UpdateServices(IReleaseSource Source, IUpdateVersionProvider Versions, IUpdateDownloader Downloader,
    IUpdateSetupHandoff Handoff, IDisposable? Lifetime = null)
{
    internal static UpdateServices Create()
    {
        var http = UpdateHost.CreateClient();
        return new(new GitHubReleaseSource(http),new UpdateVersionProvider(),new UpdateDownloader(http,UpdateFiles.Staging),new UpdateSetupHandoff(),http);
    }
}

internal sealed class UpdatesPage : WorkspacePage
{
    private readonly Label headline=WorkspaceDesign.Label("Check for updates",24,true);
    private readonly Label notesHeading=WorkspaceDesign.Label("What's new",18,true);
    private readonly Label installedLabel=WorkspaceDesign.Label("Checking…",14,true);
    private readonly Label stateMark=WorkspaceDesign.Label("↓",28,true);
    private readonly Label summary = new() { AutoSize = true, MaximumSize = new(760,0), Text = "Check for a newer stable version of Roblox Multi Instance." };
    private readonly Button primary = new WorkspaceButton() { Text = "Check for updates", AutoSize = true, Tag = "primary" };
    private readonly Button cancel = new WorkspaceButton() { Text = "Cancel download", AutoSize = true, Enabled = false };
    private readonly Button check = new WorkspaceButton() { Text = "Check again", AutoSize = true };
    private readonly TextBox notes = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = "Release notes appear here after a successful check.", AccessibleName = "Release notes" };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 12, Visible = false };
    private readonly UpdateServices services;
    private readonly CancellationTokenSource stopped = new();
    private CancellationTokenSource? download;
    private Task operation = Task.CompletedTask;
    private DownloadedUpdate? downloaded;
    private UpdateRelease? release;
    private bool stopping, notificationPending;
    private int downloadGeneration;
    internal bool Busy { get; private set; }
    internal event Action? StateChanged;
    internal bool HasUpdate => release is not null || notificationPending;
    internal void NotifyAvailable()
    {
        notificationPending = true;
        if (!Busy && release is null) summary.Text = "A new stable update is available. Check for updates to view its version and release notes.";
        StateChanged?.Invoke();
    }
    internal string StatusText => summary.Text;
    internal UpdatesPage(UpdateServices services)
    {
        this.services = services;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new(0) };
        root.ColumnStyles.Add(new(SizeType.Percent,72)); root.ColumnStyles.Add(new(SizeType.Absolute,16)); root.ColumnStyles.Add(new(SizeType.Percent,28));
        var main=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Tag="surface" };
        for(int i=0;i<6;i++) main.RowStyles.Add(new(i==0?SizeType.Absolute:SizeType.AutoSize,i==0?112:0)); main.RowStyles.Add(new(SizeType.Percent,100));
        var banner=new TableLayoutPanel { Dock=DockStyle.Fill,AutoSize=false,ColumnCount=2,Tag="surface",Margin=new(0,8,0,16) };
        banner.ColumnStyles.Add(new(SizeType.Absolute,62)); banner.ColumnStyles.Add(new(SizeType.Percent,100));
        stateMark.Tag="accent"; banner.Controls.Add(stateMark,0,0);
        var title=WorkspaceDesign.Stack(headline,summary); title.AutoSize=false; summary.Font=new("Segoe UI",12); summary.Tag="muted";
        banner.Controls.Add(title,1,0); main.Controls.Add(banner,0,0);
        var actions=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,Margin=new(62,0,0,12),Tag="surface" };
        actions.Controls.Add(primary); actions.Controls.Add(cancel); actions.Controls.Add(check); main.Controls.Add(actions,0,1); main.Controls.Add(progress,0,2);
        var automatic=WorkspaceDesign.Label("Automatic checks run once per Windows boot.",10); automatic.Tag="muted"; automatic.Margin=new(62,8,0,28); main.Controls.Add(automatic,0,3);
        var divider=new Panel { Height=1,Dock=DockStyle.Top,Tag="divider",Margin=new(0,10,0,24) }; main.Controls.Add(divider,0,4);
        main.Controls.Add(notesHeading,0,5); notes.Tag="borderless"; notes.BorderStyle=BorderStyle.None; notes.Font=new("Segoe UI",12); main.Controls.Add(notes,0,6);
        var detailsCard=WorkspaceDesign.Card(main,new(28)); detailsCard.AccessibleName="Update details"; root.Controls.Add(detailsCard,0,0);
        var infoTitle=WorkspaceDesign.Label("Update preferences",16,true);
        var info=WorkspaceDesign.Label("Downloads and installation require your action.",12); info.Tag="muted"; info.MaximumSize=new(260,0); info.Margin=new(0,4,0,28);
        var installedCaption=WorkspaceDesign.Label("Installed version",11); installedCaption.Tag="muted";
        var link=WorkspaceControls.Button("Release page",()=>
        { try { Process.Start(new ProcessStartInfo((release?.Page ?? UpdateUrls.Releases).AbsoluteUri) { UseShellExecute=true })?.Dispose(); } catch { summary.Text="The release page could not be opened."; } });
        link.Font=new("Segoe UI",10); link.Margin=new(0,20,0,0);
        var sidebar=WorkspaceDesign.Stack(infoTitle,info,installedCaption,installedLabel,link);
        var preferencesCard=WorkspaceDesign.Card(sidebar,new(24)); preferencesCard.AccessibleName="Update preferences"; root.Controls.Add(preferencesCard,2,0); Controls.Add(root);
        installedLabel.Text=services.Versions.Current() is Version installed?UpdateVersion.Label(installed):"Unknown";
        cancel.Visible=false; check.Visible=false;        primary.Click += async (_,_) => { if (!Busy && !stopping) { operation = release is null ? CheckCoreAsync() : DownloadOrInstallAsync(); await operation; } };
        check.Click += async (_,_) => await CheckAsync();
        cancel.Click += (_,_) => download?.Cancel();
    }
    internal Task CheckAsync()
    {
        if (Busy || stopping) return operation;
        operation = CheckCoreAsync(); return operation;
    }
    private async Task CheckCoreAsync()
    {
        SetBusy(true); headline.Text="Checking for updates"; stateMark.Text="↓"; summary.Text = "Checking for updates…";
        try
        {
            var current = services.Versions.Current() ?? throw new UserError("The installed version could not be read. Check System → Health.");
            var value = await services.Source.LatestAsync(stopped.Token);
            if (stopping) return;
            installedLabel.Text=UpdateVersion.Label(current);
            headline.Text=value is null?"No release available":value.Version>current?"Update available":"You are up to date";
            stateMark.Text=value is not null && value.Version<=current?"✓":"↓";
            notesHeading.Text=value is null?"Release notes":"New in "+UpdateVersion.Label(value.Version);
            notificationPending = false;
            downloaded?.Dispose(); downloaded = null; release = value is not null && value.Version > current ? value : null;
            notes.Text = value is null ? "No release notes are available." : string.IsNullOrWhiteSpace(value.Notes) ? "No release notes were provided." : value.Notes.Replace("\n","\r\n");
            summary.Text = release is null ? value is null ? "No supported stable release is currently available." : $"You're up to date (version {UpdateVersion.Label(current)})."
                : $"Version {UpdateVersion.Label(value!.Version)} is available. Installed: {UpdateVersion.Label(current)}. " + (value.DownloadProblem ?? "");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!stopping) summary.Text = e is UserError ? e.Message : "Couldn't check for updates. Check your connection or try again later."; }
        finally { if (!stopping) { SetBusy(false); StateChanged?.Invoke(); } }
    }
    private async Task DownloadOrInstallAsync()
    {
        if (release?.Asset is null) return;
        SetBusy(true); download = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
        var currentDownload = download; int generation = ++downloadGeneration;
        try
        {
            if (downloaded is null)
            {
                progress.Visible = true; cancel.Visible=true; cancel.Enabled = true; headline.Text="Downloading update";
                var reporting = new Progress<long>(bytes =>
                {
                    if (stopping || !Busy || downloaded is not null || currentDownload.IsCancellationRequested || generation != downloadGeneration) return;
                    progress.Value = (int)Math.Clamp(bytes * 100 / release.Asset.Size,0,100);
                    summary.Text = $"Downloading version {UpdateVersion.Label(release.Version)}… {progress.Value}%";
                });
                downloaded = await services.Downloader.DownloadAsync(release,reporting,download.Token);
                if (!stopping) headline.Text="Ready to install";
                if (!stopping) summary.Text = "Download verified. Run setup to close helper windows and request administrator approval. Roblox games remain open.";
            }
            else
            {
                await services.Downloader.VerifyAsync(downloaded.Executable,release,download.Token);
                await services.Handoff.StartAsync(downloaded.Executable,release);
                Close();
            }
        }
        catch (OperationCanceledException) { if (!stopping) summary.Text = "Download canceled. Select Download update to retry."; }
        catch (Exception e)
        {
            downloaded?.Dispose(); downloaded = null;
            if (!stopping) summary.Text = e is InvalidDataException
                ? "The download did not match the release size, SHA-256, or executable version. Retry after checking the release."
                : "The update could not be downloaded, verified, or started. Check your connection and folder access, then retry.";
        }
        finally { downloadGeneration++; download.Dispose(); download = null; if (!stopping) { progress.Visible = false; SetBusy(false); } }
    }
    private void SetBusy(bool value)
    {
        Busy = value; if(!value) cancel.Visible=false; check.Visible=release is not null; primary.Enabled = !value && (release is null || release.Asset is not null); check.Enabled = !value; cancel.Enabled = false;
        primary.Text = downloaded is not null ? "Run setup…" : release is not null ? "Download update" : "Check for updates";
    }
    internal async Task StopAsync()
    {
        stopping = true; stopped.Cancel(); download?.Cancel();
        await operation;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { stopping = true; stopped.Cancel(); download?.Cancel(); downloaded?.Dispose(); services.Lifetime?.Dispose(); }
        base.Dispose(disposing);
    }
}
