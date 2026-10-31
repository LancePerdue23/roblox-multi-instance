using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.IO.Pipes;
using RobloxLaunchHelper;
using LegacyProgram = RobloxLaunchHelper.Program;
using LayoutSettings = RobloxLaunchHelper.LayoutSettings;
using CheckState = RobloxLaunchHelper.CheckState;

namespace RobloxMultiInstancePreview;

/// <summary>UI-independent preview facade. Legacy identity is deliberately retained only for shared helper operations.</summary>
public sealed class PreviewBackend : IDisposable
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobloxMultiInstancePreview");
    readonly InstancePlatform platform = new();
    readonly InstanceCollector collector;
    readonly PlacementPlatform placement = new();
    readonly LayoutStore store;
    readonly AppearancePreferences appearance;
    readonly string storageRoot;
    readonly DiagnosticHistory history = new();
    readonly NotificationPreferences notifications = new();
    readonly Dictionary<string, InstanceTarget> targets = [];
    readonly Dictionary<InstanceTarget,string> ids = [];
    readonly SemaphoreSlim gate = new(1,1);
    readonly HttpClient http = UpdateHost.CreateClient();
    readonly UpdateDownloader downloader;
    LayoutSettings settings;
    PlacementPlan? plan;
    string? planId;
    HealthCheckResult worker = DiagnosticChecks.NotTested;
    UpdateRelease? release;
    DownloadedUpdate? downloaded;
    CancellationTokenSource? downloadCancellation;
    bool imported, settingsAvailable = true, planApplied;
    int launching;
    BackgroundClientMonitor? background;
    public event Action<string,string,object?>? Event;
    public PreviewBackend(string? storageRoot = null)
    {
        this.storageRoot = storageRoot ?? Root; store = new(Path.Combine(this.storageRoot,"window-layouts.json")); appearance = new(Path.Combine(this.storageRoot,"appearance.json"));
        using var process = Process.GetCurrentProcess();
        collector = new(platform,LegacyProgram.Sid,process.SessionId);
        try { settings = store.Load(); } catch { settings = LayoutSettings.Default; settingsAvailable = false; } imported = File.Exists(Path.Combine(this.storageRoot,"imported"));
        downloader = new(http,Path.Combine(this.storageRoot,"updates","staging"));
    }
    static string Text(JsonElement p,string key,string fallback="") => p.TryGetProperty(key,out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;
    static bool Bool(JsonElement p,string key) => p.TryGetProperty(key,out var v) && v.ValueKind == JsonValueKind.True;
    static void Confirm(JsonElement p) { if(!Bool(p,"confirmed")) throw new PreviewOperationException("STATE_CHANGED","Confirm this action before continuing."); }
    static string VersionLabel() => File.Exists(LegacyProgram.InstalledExe) ? FileVersionInfo.GetVersionInfo(LegacyProgram.InstalledExe).FileVersion ?? "unknown" : "not installed";
    void RequireHelper() { if(!File.Exists(LegacyProgram.InstalledExe)) throw new PreviewOperationException("STATE_CHANGED","Install the current helper using the original app, then reopen this preview."); }
    public async Task<object?> ExecuteAsync(string command, JsonElement payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(command is "launch" or "launch.start") { if(Interlocked.Exchange(ref launching,1)!=0)throw new PreviewOperationException("BUSY","A launch is already running."); try { RequireHelper(); return new { exitCode=await RunHelperAsync("--launch-roblox",token) }; } finally { Volatile.Write(ref launching,0); } }
        if(command is "updates.cancel") { downloadCancellation?.Cancel(); return new { canceled=true }; }
        if(command is "app.state" or "settings.get" or "settings.read" or "activity.list" or "activity.read" or "background.status") return await ExecuteCoreAsync(command,payload,token);
        await gate.WaitAsync(token);
        try { return await ExecuteCoreAsync(command,payload,token); }
        finally { gate.Release(); }
    }
    async Task<object?> ExecuteCoreAsync(string command, JsonElement p, CancellationToken token)
    {
        switch(command)
        {
            case "background.status": return new { enabled=background is not null,owns=background?.OwnsMutex??false,waiting=background is not null && !background.OwnsMutex };
            case "background.set":
                if(Bool(p,"enabled") && background is null) { using var current=Process.GetCurrentProcess();background=new(new InstanceCollector(platform,LegacyProgram.Sid,current.SessionId,collectMetrics:false),new BackgroundClientCleanup(pid=>new InstanceProcess(pid,allowStop:true),platform.Trusted,LegacyProgram.Sid,current.SessionId),@"Local\RobloxLaunchHelper-BackgroundCleanup-"+LegacyProgram.Sid+"-"+current.SessionId); }
                else if(!Bool(p,"enabled") && background is not null) { await background.StopAsync();background.Dispose();background=null; }
                return new { enabled=background is not null,owns=background?.OwnsMutex??false,waiting=background is not null && !background.OwnsMutex };
            case "app.state": return new AppStateDto("0.1.0-preview",VersionLabel(),File.Exists(LegacyProgram.InstalledExe),"Browser integration, helper maintenance, launch history and notification preferences are shared with the current app.");
            case "instances.cleanup":
                Confirm(p); using(var current=Process.GetCurrentProcess()) { var fresh=await Task.Run(()=>new InstanceCollector(platform,LegacyProgram.Sid,current.SessionId,collectMetrics:false).Collect(token),token); var cleanup=new BackgroundClientCleanup(pid=>new InstanceProcess(pid,allowStop:true),platform.Trusted,LegacyProgram.Sid,current.SessionId);return new { results=await Task.Run(()=>BackgroundClientCleanup.Candidates(fresh,DateTime.UtcNow).Select(key=>new { result=cleanup.Execute(key,token).ToString() }).ToArray(),token) }; }
            case "instances.list": case "instances.refresh": return await ListAsync(token);
            case "instances.focus": case "instances.close":
                if(command.EndsWith("close")) Confirm(p);
                if(!targets.TryGetValue(Text(p,"id"),out var target)) throw new PreviewOperationException("STATE_CHANGED","This selection expired. Refresh Instances and select the window again.");
                using(var process = Process.GetCurrentProcess())
                    return new { result = await Task.Run(()=>new InstanceActions(platform,LegacyProgram.Sid,process.SessionId).Execute(target,command.EndsWith("focus")?InstanceAction.Focus:InstanceAction.Close,token),token) is var action ? action.ToString() : "Unavailable" };
            case "launch": case "launch.start": RequireHelper(); return new { exitCode = await RunHelperAsync("--launch-roblox",token) };
            case "layouts.get": case "layouts.state": return LayoutState();
            case "layouts.preview": return await PreviewAsync(p,token);
            case "layouts.apply":
                Confirm(p); RequirePlan(p); if(planApplied)throw new PreviewOperationException("STATE_CHANGED","This layout was already applied. Preview again."); planApplied=true;
                return new { results=(await Task.Run(()=>new PlacementCoordinator(placement).ApplyAsync(plan!,token),token)).Select(r=>new { id=GetId(r.Target),message=r.Message }).ToArray() };
            case "layouts.savePreset": case "presets.save":
                RequirePlan(p); string name=Text(p,"name").Trim();
                if(settings.Presets.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase))) throw new PreviewOperationException("STATE_CHANGED","A preset with this name exists.");
                var windows = plan!.Placements.Select(w=> { using var session=placement.Open(w.Target); return session.Read(); }).ToArray();
                Save(settings with { Presets=[..settings.Presets,new WindowPreset(name,windows.Select(w=>LayoutPlanner.Save(w,plan.Monitors)).ToArray())] }); return LayoutState();
            case "layouts.renamePreset": case "presets.rename":
                Save(settings with { Presets=settings.Presets.Select(x=>x.Name==Text(p,"name")?x with { Name=Text(p,"newName").Trim() }:x).ToArray() });return LayoutState();
            case "layouts.deletePreset": case "presets.delete":
                Confirm(p); Save(settings with { Presets=settings.Presets.Where(x=>x.Name!=Text(p,"name")).ToArray() });return LayoutState();
            case "activity.list": case "activity.read": return Activity();
            case "activity.clear": Confirm(p);if(!history.Clear()) throw new PreviewOperationException("OPERATION_FAILED","History could not be cleared.");return Activity();
            case "system.health": case "health.read": return await HealthAsync(token);
            case "system.testWorker": case "health.testWorker":
                RequireHelper();
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(35));
                    using var pipe=await Launch.ConnectAsync(timeout.Token);
                    await Protocol.WriteAsync(pipe,"PROBE",timeout.Token);
                    bool ready=await Protocol.ReadAsync(pipe,timeout.Token)=="OK"; worker=DiagnosticChecks.Result("worker",ready?CheckState.Healthy:CheckState.Failed,ready?DiagnosticCode.WorkerReady:DiagnosticCode.WorkerUnavailable);
                }
                return await HealthAsync(token);
            case "system.integration": case "integration.set": Confirm(p);RequireHelper();await BrowserIntegration.SetAsync(Bool(p,"enabled"),token);return await HealthAsync(token);
            case "system.maintenance": case "maintenance.run":
                Confirm(p);var actionName=Text(p,"action");
                if(actionName is not ("setup" or "repair" or "recover" or "uninstall")) throw new PreviewOperationException("INVALID_REQUEST","Unknown maintenance action.");
                RequireHelper();return new { exitCode=await RunHelperAsync("--"+actionName,token) };
            case "system.openLogs": case "logs.open":
                int index=Text(p,"kind") switch { "worker"=>1,"setup"=>2,_=>0 };
                var directory=DiagnosticReports.LogDirectories[index];
                if(!Directory.Exists(directory))throw new PreviewOperationException("OPERATION_FAILED","This log folder does not exist yet.");
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute=true,ArgumentList={ directory } })?.Dispose();return new { opened=true };
            case "system.export":
                Confirm(p);string path=Text(p,"path");if(!Path.IsPathFullyQualified(path)||!path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))throw new PreviewOperationException("INVALID_REQUEST","Choose a ZIP report destination.");
                bool available=history.Read(out var records);var health=await Task.Run(()=>DiagnosticChecks.Collect(worker),token);
                var package=await Task.Run(()=>DiagnosticReports.Build(health,records,available),token);DiagnosticReports.Write(path,package);return new { saved=true };
            case "settings.get": case "settings.read": return Settings();
            case "settings.save":
                if(p.TryGetProperty("scheme",out _)) { if(!Enum.TryParse<ColorScheme>(Text(p,"scheme"),true,out var scheme)||!Enum.IsDefined(scheme))throw new PreviewOperationException("INVALID_REQUEST","Unknown color scheme.");appearance.Save(scheme); }
                if(p.TryGetProperty("shortcuts",out var keys)) Save(settings with { Shortcuts=keys.Deserialize<Dictionary<string,int>>()??throw new PreviewOperationException("INVALID_REQUEST","Invalid shortcuts.") });
                return Settings();
            case "shortcuts.save": Save(settings with { Shortcuts=p.GetProperty("bindings").Deserialize<Dictionary<string,int>>()??throw new PreviewOperationException("INVALID_REQUEST","Invalid shortcuts.") });return Settings();
            case "settings.notifications": case "notifications.save":
                Confirm(p);notifications.Save(new(1,Bool(p,"showProgress")||Bool(p,"progress"),Bool(p,"showCompletion")||Bool(p,"completion")));return Settings();
            case "settings.reset": Confirm(p);appearance.Save(ColorScheme.Dark,true); store.Save(LayoutSettings.Default); settings=LayoutSettings.Default; settingsAvailable=true;return Settings();
            case "settings.resetNotifications": Confirm(p);notifications.Save(new(),reset:true);return Settings();
            case "settings.import":
                Confirm(p);if(imported)throw new PreviewOperationException("STATE_CHANGED","Preferences were already imported. Your current preview preferences are kept.");
                var importedLayouts=new LayoutStore().Load();var importedAppearance=new AppearancePreferences();if(!importedAppearance.Available)throw new InvalidDataException("Original appearance settings are unavailable.");
                Save(importedLayouts);appearance.Save(importedAppearance.Scheme,true);Directory.CreateDirectory(storageRoot);File.WriteAllText(Path.Combine(storageRoot,"imported"),"1");imported=true;return Settings();
            case "updates.check":
                using(var limit=CancellationTokenSource.CreateLinkedTokenSource(token)) { limit.CancelAfter(TimeSpan.FromSeconds(30));release=await new GitHubReleaseSource(http).LatestAsync(limit.Token); }
                return UpdateState();
            case "updates.download":
                if(release?.Asset is null)throw new PreviewOperationException("STATE_CHANGED","Check for an available verified update first.");
                downloaded?.Dispose();downloaded=null;
                using(downloadCancellation=CancellationTokenSource.CreateLinkedTokenSource(token))
                    downloaded=await downloader.DownloadAsync(release,new Progress<long>(bytes=>Event?.Invoke("update-download","progress",new { bytes,total=release.Asset.Size })),downloadCancellation.Token);
                downloadCancellation=null;return UpdateState();
            case "updates.install": case "updates.setup":
                Confirm(p);if(downloaded is null||release is null)throw new PreviewOperationException("STATE_CHANGED","Download and verify the helper update first.");
                await downloader.VerifyAsync(downloaded.Executable,release,token);
                UpdateShutdownRequest.CloseManagers();
                for(int i=0;i<20&&!UpdateInstallReadiness.IsReady(LegacyProgram.InstalledExe);i++)await Task.Delay(500,token);
                if(!UpdateInstallReadiness.IsReady(LegacyProgram.InstalledExe))throw new PreviewOperationException("OPERATION_FAILED","The installed helper is busy. Close its windows and retry setup.");
                int exit=await RunExecutableAsync(downloaded.Executable,"--setup",token);
                var installed=UpdateVersion.Parse(VersionLabel());if(UpdateSetup.ResultMessage(exit,installed,release.Version) is string error)throw new PreviewOperationException("OPERATION_FAILED",error);
                return UpdateState();
            default: throw new PreviewOperationException("INVALID_REQUEST","Unknown command.");
        }
    }
    string GetId(InstanceTarget target) { if(!ids.TryGetValue(target,out var id)) { id=Guid.NewGuid().ToString("N");ids[target]=id;targets[id]=target; } return id; }
    async Task<object> ListAsync(CancellationToken token)
    {
        var result=await Task.Run(()=>collector.Collect(token),token);
        var live=new HashSet<InstanceTarget>();
        var rows=result.Instances.SelectMany(client=>client.Windows.Length==0 ? new[]{ new InstanceDto("","Background client",client.Key.Id,client.Username,client.CpuPercent,client.WorkingSet,InstancePolicy.Started(client.Key),false) } : client.Windows.Select(window=> { var target=new InstanceTarget(client.Key,window.Handle);live.Add(target);return new InstanceDto(GetId(target),window.Title,client.Key.Id,client.Username,client.CpuPercent,client.WorkingSet,InstancePolicy.Started(client.Key),true); })).ToArray();
        foreach(var target in ids.Keys.Where(x=>!live.Contains(x)).ToArray()){targets.Remove(ids[target]);ids.Remove(target);}
        return new InstanceListDto(result.Available,result.Unavailable,rows);
    }
    static MonitorDto MapMonitor(LayoutMonitor m)=>new(m.Id,m.Label,new(m.Work.X,m.Work.Y,m.Work.Width,m.Work.Height),m.Dpi,m.Primary);
    object LayoutState()=>new LayoutStateDto(placement.Monitors().Select(MapMonitor).ToArray(),settings.Presets.Select(p=>new PresetDto(p.Name,p.Slots.Select(s=>new SlotDto(s.Monitor,s.X,s.Y,s.Width,s.Height)).ToArray())).ToArray(),settings.Shortcuts);
    async Task<object> PreviewAsync(JsonElement p,CancellationToken token)
    {
        if(!Enum.TryParse<ArrangementKind>(Text(p,"kind","Grid"),true,out var kind))throw new PreviewOperationException("INVALID_REQUEST","Unknown arrangement.");
        var selected=p.GetProperty("ids").EnumerateArray().Select(v=>targets.TryGetValue(v.GetString()!,out var t)?t:throw new PreviewOperationException("STATE_CHANGED","A window selection expired.")).Distinct().ToArray();
        var monitors=placement.Monitors();
        var windows=await Task.Run(()=>selected.Select(t=> { try { using var session=placement.Open(t);return session.Read(); } catch { return new LayoutWindow(t,"Window",Rectangle.Empty,Size.Empty,false,"Exited or unavailable"); } }).ToArray(),token);
        var assignments=p.TryGetProperty("assignments",out var a)?a.Deserialize<string[]>()!:windows.Select(_=>monitors.FirstOrDefault(m=>m.Primary)?.Id??monitors.FirstOrDefault()?.Id??"").ToArray();
        plan=BuildPlan(monitors,windows,assignments,kind,settings.Presets.FirstOrDefault(x=>x.Name==Text(p,"preset")));
        planApplied=false; planId=Guid.NewGuid().ToString("N");return new PlanDto(planId,plan.Valid,plan.Error,plan.Notes,monitors.Select(MapMonitor).ToArray(),plan.Placements.Select(w=>new PlacementDto(GetId(w.Target),w.Bounds.X,w.Bounds.Y,w.Bounds.Width,w.Bounds.Height,w.Slot)).ToArray());
    }
    internal static PlacementPlan BuildPlan(LayoutMonitor[] monitors,LayoutWindow[] checkedWindows,string[] assignments,ArrangementKind kind,WindowPreset? preset)
    {
        if(monitors.Length==0)return new(monitors,[],[],"No monitors are available.");
        if(checkedWindows.Length==0)return new(monitors,[],[],"Check at least one window.");
        if(assignments.Length!=checkedWindows.Length)return new(monitors,[],[],"Window assignments changed. Preview again.");
        var skipped=checkedWindows.Where(w=>!w.Movable).ToArray();
        string message=string.Join(" ",skipped.Select(w=>$"Slot {Array.IndexOf(checkedWindows,w)+1} skipped: {w.Reason}."));
        if(kind==ArrangementKind.Saved&&skipped.Length>0)return new(monitors,[],[],message+" All saved-position slots must be ready before restoring a preset.");
        var selected=checkedWindows.Where(w=>w.Movable).ToArray();
        if(selected.Length==0)return new(monitors,[],[],message);
        var chosenAssignments=checkedWindows.Select((w,i)=>(w,i)).Where(x=>x.w.Movable).Select(x=>assignments[x.i]).ToArray();
        PlacementPlan result=kind switch
        {
            ArrangementKind.Recover=>LayoutPlanner.Recover(monitors,selected),
            ArrangementKind.Saved=>preset is null?new(monitors,[],[],"Choose a saved preset."):LayoutPlanner.Restore(monitors,selected,preset),
            _=>LayoutPlanner.Arrange(monitors,selected,chosenAssignments,kind)
        };
        result=result with { Placements=result.Placements.Select(p=>p with { Slot=Array.FindIndex(checkedWindows,w=>w.Target==p.Target)+1 }).ToArray(),Notes=skipped.Length==0?result.Notes:[..result.Notes,message] };
        int fullscreen=selected.Count(w=>w.NeedsWindowed);
        if(fullscreen>0)result=result with { Notes=[..result.Notes,$"Apply will briefly focus {fullscreen} fullscreen client(s), use F11 and verify windowed mode. Release modifier keys during Apply. A client that does not respond is reported individually."] };
        return result;
    }
    void RequirePlan(JsonElement p) { if(plan is null||Text(p,"planId")!=planId||!plan.Valid)throw new PreviewOperationException("STATE_CHANGED","Create a valid layout preview first."); }
    void Save(LayoutSettings next) { if(!settingsAvailable)throw new PreviewOperationException("STATE_CHANGED","Preview layout settings are unavailable. Reset them explicitly in Settings before saving."); store.Save(next);settings=next;Event?.Invoke("settings","changed",Settings()); }
    object Settings() { notifications.Reload();return new SettingsDto(appearance.Scheme.ToString(),settingsAvailable&&appearance.Available,settings.Shortcuts,new(notifications.Settings.Progress,notifications.Settings.Completion,notifications.Available),imported); }
    object Activity(){bool available=history.Read(out var records);return new ActivityDto(available,records.Select(r=>new ActivityRowDto(r.AttemptId,r.StartedUtc,r.EndedUtc,r.DurationMs,r.Source.ToString(),r.Outcome.ToString(),r.Code.ToString(),DiagnosticMessages.Text(r.Code))).ToArray());}
    async Task<object> HealthAsync(CancellationToken token){var h=await Task.Run(()=>DiagnosticChecks.Collect(worker),token);return new HealthDto(h.Overall.ToString(),VersionLabel(),h.BrowserIntegrationEnabled,h.Checks.Append(worker).Select(c=>new CheckDto(c.Id,c.Id,c.State.ToString(),c.Code.ToString(),DiagnosticMessages.Text(c.Code),c.Action.ToString())).ToArray());}
    object UpdateState()=>new UpdateDto(VersionLabel(),release is not null&&UpdateVersion.Parse(VersionLabel()) is Version installed&&release.Version>installed,release?.Version.ToString(),release?.Title,release?.Notes,release?.DownloadProblem,release?.Asset is not null,downloaded is not null);
    Task<int> RunHelperAsync(string argument,CancellationToken token)=>RunExecutableAsync(LegacyProgram.InstalledExe,argument,token);
    static async Task<int> RunExecutableAsync(string executable,string argument,CancellationToken token){var start=new ProcessStartInfo(executable){UseShellExecute=true};start.ArgumentList.Add(argument);using var child=Process.Start(start)??throw new PreviewOperationException("OPERATION_FAILED","The helper could not start.");await child.WaitForExitAsync(token);return child.ExitCode;}
    public void Dispose(){background?.Dispose();downloadCancellation?.Cancel();downloaded?.Dispose();http.Dispose();}
}









