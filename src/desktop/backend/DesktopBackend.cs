using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.IO.Pipes;
using RobloxLaunchHelper;
using LegacyProgram = RobloxLaunchHelper.Program;
using LayoutSettings = RobloxLaunchHelper.LayoutSettings;
using CheckState = RobloxLaunchHelper.CheckState;

namespace RobloxMultiInstance;

/// <summary>UI-independent preview facade. Legacy identity is deliberately retained only for shared helper operations.</summary>
public sealed partial class DesktopBackend : IDesktopBackend
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobloxMultiInstance");
    readonly string migrationWarning = "";
    readonly InstancePlatform platform = new();
    readonly InstanceCollector collector;
    readonly AccountEnrichment labels=new();
    long generation;
    int disposed;
    string probeFingerprint="";
    readonly object identityLock=new();
    readonly PlacementPlatform placement = new();
    readonly LayoutStore store;
    readonly AppearancePreferences appearance;
    public string AppearanceScheme => appearance.Scheme.ToString();
    readonly string storageRoot;
    readonly DiagnosticHistory history = new();
    readonly NotificationPreferences notifications = new();
    readonly Dictionary<string, InstanceTarget> targets = [];
    readonly Dictionary<InstanceTarget,string> ids = [];
    readonly OperationCoordinator operations = new();
    readonly SemaphoreSlim discovery = new(1,1);
    readonly SemaphoreSlim preferenceGate=new(1,1);
    readonly AsyncLocal<string?> requestId = new();
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
    BackgroundClientMonitor? background;
    readonly bool backgroundMonitoringAllowed;
    readonly string backgroundPreferencePath;
    bool backgroundEnabled = true;
    public event Action<string,string,object?>? Event;
    public DesktopBackend(string? storageRoot = null)
    {
        this.storageRoot = storageRoot ?? Root;
        backgroundMonitoringAllowed = storageRoot is null;
        backgroundPreferencePath = Path.Combine(this.storageRoot,"background-cleanup.json");
        try { if(File.Exists(backgroundPreferencePath)) backgroundEnabled=JsonSerializer.Deserialize<bool>(File.ReadAllText(backgroundPreferencePath)); }
        catch { backgroundEnabled=false; }
        if (storageRoot is null) migrationWarning = PreferenceMigration.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobloxMultiInstancePreview"), this.storageRoot);
        store = new(Path.Combine(this.storageRoot,"window-layouts.json")); appearance = new(Path.Combine(this.storageRoot,"appearance.json"));
        using var process = Process.GetCurrentProcess();
        collector = new(platform,LegacyProgram.Sid,process.SessionId);
        try { settings = store.Load(); } catch { settings = LayoutSettings.Default; settingsAvailable = false; } imported = File.Exists(Path.Combine(this.storageRoot,"imported"));
        downloader = new(http,Path.Combine(this.storageRoot,"updates","staging"));
        InitializeFeatures(storageRoot is null);
        InitializePerformance();
        StartBackgroundCleanup();
        labels.Resolved+=(version,key,name)=> { lock(identityLock) { if(Volatile.Read(ref disposed)!=0||version!=generation)return;var rows=ids.Where(x=>x.Key.Key==key).Select(x=>new { id=x.Value,username=name }).ToArray();Event?.Invoke("labels","instances.labels",new { generation=version,labels=rows }); } };
    }
    object BackgroundStatus() => new { enabled=backgroundEnabled,owns=background?.OwnsMutex??false,waiting=background is not null && !background.OwnsMutex };
    void StartBackgroundCleanup()
    {
        if(!backgroundEnabled || !backgroundMonitoringAllowed || background is not null)return;
        using var current=Process.GetCurrentProcess();
        background=new(new InstanceCollector(platform,LegacyProgram.Sid,current.SessionId,collectMetrics:false),new BackgroundClientCleanup(pid=>new InstanceProcess(pid,allowStop:true),platform.Trusted,LegacyProgram.Sid,current.SessionId),@"Local\RobloxLaunchHelper-BackgroundCleanup-"+LegacyProgram.Sid+"-"+current.SessionId);
    }
    public async Task StopBackgroundCleanupAsync()
    {
        if(background is null)return;
        await background.StopAsync();background.Dispose();background=null;
    }
    static string Text(JsonElement p,string key,string fallback="") => p.TryGetProperty(key,out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;
    static bool Bool(JsonElement p,string key) => p.TryGetProperty(key,out var v) && v.ValueKind == JsonValueKind.True;
    static void Confirm(JsonElement p) { if(!Bool(p,"confirmed")) throw new DesktopOperationException("STATE_CHANGED","Confirm this action before continuing."); }
    static string VersionLabel() => File.Exists(LegacyProgram.InstalledExe) ? FileVersionInfo.GetVersionInfo(LegacyProgram.InstalledExe).FileVersion ?? "unknown" : "not installed";
    void RequireHelper() { if(!File.Exists(LegacyProgram.InstalledExe)||File.Exists(Path.Combine(LegacyProgram.InstallDirectory,"uninstall.pending"))) throw new DesktopOperationException("STATE_CHANGED","Set up the matching helper using System → Set up helper.");var metadata=Distribution.Installed();if(metadata is not null&&(!Distribution.Compatible(metadata,VersionLabel())||metadata.Edition!="Desktop"||UpdateVersion.Parse(metadata.Version)!=UpdateVersion.Parse(typeof(DesktopBackend).Assembly.GetName().Version?.ToString())))throw new DesktopOperationException("STATE_CHANGED","The installed app or helper does not match its compatibility metadata. Run desktop setup to repair it."); }
    public async Task<object?> ExecuteAsync(string command, JsonElement payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(command is "updates.cancel") { downloadCancellation?.Cancel(); return new { canceled=true }; }
        using var operation = operations.Enter(command);
        // Automatic window changes and user window/layout commands share one native gate.
        if(OperationCoordinator.Category(command)=="windows"&&!command.StartsWith("performance.",StringComparison.Ordinal))
        {
            await performanceGate.WaitAsync(token);
            try {return await ExecuteCoreAsync(command,payload,token);}finally {performanceGate.Release();}
        }
        if(command is "instances.list" or "instances.refresh" or "instances.stopLabels") {
            await discovery.WaitAsync(token);
            try { return await ExecuteCoreAsync(command,payload,token); } finally { discovery.Release(); }
        }
        if(OperationCoordinator.Category(command)=="preferences" && !command.StartsWith("accounts.",StringComparison.Ordinal) || command is "settings.get" or "settings.read" or "layouts.get" or "layouts.preview" or "layouts.savePreset") {
            await preferenceGate.WaitAsync(token);try{return await ExecuteCoreAsync(command,payload,token);}finally{preferenceGate.Release();}
        }
        return await ExecuteCoreAsync(command,payload,token);
    }
    public async Task<object?> ExecuteOperationAsync(string id,string command,JsonElement payload,CancellationToken token)
    {
        requestId.Value=id;
        try { return await ExecuteAsync(command,payload,token); }
        finally { requestId.Value=null; }
    }
    static OperationResultDto Result(OperationResult result)=>new(result.Outcome.ToString(),result.Code,result.Message,result.Items?.Select(x=>new OperationItemDto(x.Id,x.Outcome.ToString(),x.Code,x.Message)).ToArray()??[]);
    async Task<object?> ExecuteCoreAsync(string command, JsonElement p, CancellationToken token)
    {
        if(command.StartsWith("performance.",StringComparison.Ordinal)) return await ExecutePerformanceAsync(command,p,token);
        if(command.StartsWith("telemetry.",StringComparison.Ordinal)||command.StartsWith("support.",StringComparison.Ordinal)||command.StartsWith("accounts.",StringComparison.Ordinal)||command.StartsWith("games.",StringComparison.Ordinal))
            return await ExecuteFeatureAsync(command,p,token);
        switch(command)
        {
            case "background.status": return BackgroundStatus();
            case "background.set":
                if(!p.TryGetProperty("enabled",out var enabledValue) || enabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("Choose whether background cleanup is enabled.");
                Directory.CreateDirectory(storageRoot);
                string temporary=backgroundPreferencePath+"."+Guid.NewGuid().ToString("N")+".tmp";
                try { File.WriteAllText(temporary,JsonSerializer.Serialize(enabledValue.GetBoolean()));File.Move(temporary,backgroundPreferencePath,true); }
                finally { if(File.Exists(temporary))File.Delete(temporary); }
                backgroundEnabled=enabledValue.GetBoolean();
                if(backgroundEnabled)StartBackgroundCleanup();else await StopBackgroundCleanupAsync();
                return BackgroundStatus();
            case "app.state": return new AppStateDto(typeof(DesktopBackend).Assembly.GetName().Version?.ToString(3) ?? "unknown",VersionLabel(),File.Exists(LegacyProgram.InstalledExe)&&!File.Exists(Path.Combine(LegacyProgram.InstallDirectory,"uninstall.pending")),"Browser integration and launch history are managed by the installed helper. " + migrationWarning,await ReadinessAsync(token));
            case "instances.cleanup":
                Confirm(p); using(var current=Process.GetCurrentProcess()) { var fresh=await Task.Run(()=>new InstanceCollector(platform,LegacyProgram.Sid,current.SessionId,collectMetrics:false).Collect(token),token); var cleanup=new BackgroundClientCleanup(pid=>new InstanceProcess(pid,allowStop:true),platform.Trusted,LegacyProgram.Sid,current.SessionId);if(!fresh.Available || fresh.Unavailable>0)return Result(new(OperationOutcome.Unknown,"INSPECTION_UNAVAILABLE","Cleanup could not safely inspect all clients. Nothing was stopped."));return Result(OperationResult.ForCleanup(await Task.Run(()=>BackgroundClientCleanup.Candidates(fresh,DateTime.UtcNow).Select(key=>cleanup.Execute(key,token)).ToArray(),token))); }
            case "instances.stopLabels": await labels.StopAsync();return new { stopped=true };
            case "instances.stopAll":
                Confirm(p);
                ProcessKey[] stopTargets;
                lock(identityLock)
                {
                    var requested=p.GetProperty("ids").Deserialize<string[]>()??[];
                    if(requested.Length is <1 or >4096)throw new ArgumentException("Refresh Instances before stopping clients.");
                    stopTargets=requested.Select(id=>targets.TryGetValue(id,out var item)?item.Key:throw new DesktopOperationException("STATE_CHANGED","The client list changed. Refresh Instances and review Stop all again.")).Distinct().ToArray();
                }
                using(var current=Process.GetCurrentProcess())
                    return Result(await Task.Run(()=>new InstanceStopAll(pid=>new InstanceProcess(pid,allowStop:true),platform.Trusted,LegacyProgram.Sid,current.SessionId).Execute(stopTargets,token),token));
            case "instances.list": case "instances.refresh": return await ListAsync(token);
            case "instances.focus": case "instances.close":
                if(command.EndsWith("close")) Confirm(p);
                if(!targets.TryGetValue(Text(p,"id"),out var target)) throw new DesktopOperationException("STATE_CHANGED","This selection expired. Refresh Instances and select the window again.");
                using(var process = Process.GetCurrentProcess())
                    return Result(OperationResult.ForAction(await Task.Run(()=>new InstanceActions(platform,LegacyProgram.Sid,process.SessionId).Execute(target,command.EndsWith("focus")?InstanceAction.Focus:InstanceAction.Close,token),token)));
            case "launch": case "launch.start": RequireHelper(); token.ThrowIfCancellationRequested();return Result(await Launch.RunResultAsync(null,token));
            case "layouts.get": case "layouts.state": return LayoutState();
            case "layouts.preview": return await PreviewAsync(p,token);
            case "layouts.apply":
                Confirm(p); RequirePlan(p); if(planApplied)throw new DesktopOperationException("STATE_CHANGED","This layout was already applied. Preview again."); planApplied=true;
                return new { results=(await Task.Run(()=>new PlacementCoordinator(placement).ApplyAsync(plan!,token),token)).Select(r=>new { id=GetId(r.Target),message=r.Message }).ToArray() };
            case "layouts.savePreset": case "presets.save":
                RequirePlan(p); string name=Text(p,"name").Trim();
                if(settings.Presets.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase))) throw new DesktopOperationException("STATE_CHANGED","A preset with this name exists.");
                var windows = plan!.Placements.Select(w=> { using var session=placement.Open(w.Target); return session.Read(); }).ToArray();
                Save(settings with { Presets=[..settings.Presets,new WindowPreset(name,windows.Select(w=>LayoutPlanner.Save(w,plan.Monitors)).ToArray())] }); return LayoutState();
            case "layouts.renamePreset": case "presets.rename":
                Save(settings with { Presets=settings.Presets.Select(x=>x.Name==Text(p,"name")?x with { Name=Text(p,"newName").Trim() }:x).ToArray() });return LayoutState();
            case "layouts.deletePreset": case "presets.delete":
                Confirm(p); Save(settings with { Presets=settings.Presets.Where(x=>x.Name!=Text(p,"name")).ToArray() });return LayoutState();
            case "activity.list": case "activity.read": return Activity();
            case "activity.clear": Confirm(p);if(!history.Clear()) throw new DesktopOperationException("OPERATION_FAILED","History could not be cleared.");return Activity();
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
                probeFingerprint=HelperReadiness.Fingerprint();return await HealthAsync(token);
            case "system.integration": case "integration.set": Confirm(p);RequireHelper();await BrowserIntegration.SetAsync(Bool(p,"enabled"),token);return await HealthAsync(token);
            case "system.maintenance": case "maintenance.run":
                Confirm(p);var actionName=Text(p,"action");
                if(actionName is not ("setup" or "repair" or "recover" or "uninstall")) throw new DesktopOperationException("INVALID_REQUEST","Unknown maintenance action.");
                if(actionName=="setup" || actionName is "repair" or "recover" or "uninstall" && File.Exists(Path.Combine(LegacyProgram.InstallDirectory,"Installer.exe"))) {
                    string setup=File.Exists(Path.Combine(LegacyProgram.InstallDirectory,"Installer.exe"))?Path.Combine(LegacyProgram.InstallDirectory,"Installer.exe"):Path.Combine(AppContext.BaseDirectory,Distribution.SetupName("Desktop"));
                    if(!File.Exists(setup))throw new DesktopOperationException("STATE_CHANGED","The matching setup payload is missing. Download the desktop setup from the release page.");
                    Event?.Invoke(requestId.Value??"maintenance","operation.commit",null);
                    if(actionName!="uninstall"&&string.Equals(Environment.ProcessPath,Path.Combine(LegacyProgram.InstallDirectory,"RobloxMultiInstance.exe"),StringComparison.OrdinalIgnoreCase)){
                        await UpdateSetupHandoff.StartMaintenanceAsync(setup,actionName);return Result(new(OperationOutcome.Warning,"SETUP_HANDOFF","Setup accepted the maintenance request. This app will close; setup will report its result."));
                    }
                    var setupResult=OperationResult.FromExit(await RunExecutableAsync(setup,actionName=="uninstall"?"--uninstall":actionName=="setup"?"":"--action "+actionName,token));worker=DiagnosticChecks.NotTested;
                    if(actionName=="uninstall"&&setupResult.Outcome==OperationOutcome.Succeeded)setupResult=setupResult with {Code="UNINSTALL_REQUESTED",Message="Uninstall completed. Close the app to finish removing installed files."};
                    return Result(setupResult);
                }
                RequireHelper();Event?.Invoke(requestId.Value??"maintenance","operation.commit",null);var maintenanceResult=OperationResult.FromExit(await RunHelperAsync("--"+actionName,token));worker=DiagnosticChecks.NotTested;if(actionName=="uninstall"&&maintenanceResult.Outcome==OperationOutcome.Succeeded)maintenanceResult=maintenanceResult with {Code="UNINSTALL_REQUESTED",Message="Helper integration was removed. This app will close so installed files can be removed. Settings and history are retained."};return Result(maintenanceResult);
            case "system.openLogs": case "logs.open":
                int index=Text(p,"kind") switch { "worker"=>1,"setup"=>2,_=>0 };
                var directory=DiagnosticReports.LogDirectories[index];
                if(!Directory.Exists(directory))throw new DesktopOperationException("OPERATION_FAILED","This log folder does not exist yet.");
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute=true,ArgumentList={ directory } })?.Dispose();return new { opened=true };
            case "system.export":
                Confirm(p);string path=Text(p,"path");if(!Path.IsPathFullyQualified(path)||!path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))throw new DesktopOperationException("INVALID_REQUEST","Choose a ZIP report destination.");
                bool available=history.Read(out var records);var health=await Task.Run(()=>DiagnosticChecks.Collect(worker),token);
                var package=await Task.Run(()=>DiagnosticReports.Build(health,records,available),token);DiagnosticReports.Write(path,package);return new { saved=true };
            case "settings.get": case "settings.read": return Settings();
            case "settings.save":
                if(p.TryGetProperty("scheme",out _)) { if(!Enum.TryParse<ColorScheme>(Text(p,"scheme"),true,out var scheme)||!Enum.IsDefined(scheme))throw new DesktopOperationException("INVALID_REQUEST","Unknown color scheme.");appearance.Save(scheme); }
                if(p.TryGetProperty("shortcuts",out var keys)) Save(settings with { Shortcuts=keys.Deserialize<Dictionary<string,int>>()??throw new DesktopOperationException("INVALID_REQUEST","Invalid shortcuts.") });
                return Settings();
            case "shortcuts.save": Save(settings with { Shortcuts=p.GetProperty("bindings").Deserialize<Dictionary<string,int>>()??throw new DesktopOperationException("INVALID_REQUEST","Invalid shortcuts.") });return Settings();
            case "settings.notifications": case "notifications.save":
                Confirm(p);notifications.Save(new(1,Bool(p,"showProgress")||Bool(p,"progress"),Bool(p,"showCompletion")||Bool(p,"completion")));return Settings();
            case "settings.reset": Confirm(p);appearance.Save(ColorScheme.Dark,true); store.Save(LayoutSettings.Default); settings=LayoutSettings.Default; settingsAvailable=true;return Settings();
            case "settings.resetNotifications": Confirm(p);notifications.Save(new(),reset:true);return Settings();
            case "settings.import":
                Confirm(p);if(imported)throw new DesktopOperationException("STATE_CHANGED","Preferences were already imported. Your current preview preferences are kept.");
                var importedLayouts=new LayoutStore().Load();var importedAppearance=new AppearancePreferences();if(!importedAppearance.Available)throw new InvalidDataException("Original appearance settings are unavailable.");
                Save(importedLayouts);appearance.Save(importedAppearance.Scheme,true);Directory.CreateDirectory(storageRoot);File.WriteAllText(Path.Combine(storageRoot,"imported"),"1");imported=true;return Settings();
            case "updates.check":
                using(var limit=CancellationTokenSource.CreateLinkedTokenSource(token)) { limit.CancelAfter(TimeSpan.FromSeconds(30));try { downloaded?.Dispose();downloaded=null;release=null;release=await new GitHubReleaseSource(http).LatestAsync(limit.Token); } catch(UserError error) { release=null;throw new DesktopOperationException("UPDATE_UNAVAILABLE",error.Message); } }
                if(release is not null&&Distribution.Installed() is null)release=release with {Asset=null,DownloadProblem="This legacy installation needs an explicit setup upgrade. Open the release page and choose the desktop or helper edition."};
                return UpdateState();
            case "updates.openPage":
                var page=release?.Page??UpdateUrls.Releases;
                if(page!=UpdateUrls.Releases&&!UpdateUrls.RepositoryUrl(page,"/releases/tag/"))throw new DesktopOperationException("INVALID_REQUEST","Unsupported release page.");
                Process.Start(new ProcessStartInfo(page.ToString()){UseShellExecute=true})?.Dispose();return new {opened=true};
            case "updates.download":

                if(release?.Asset is null)throw new DesktopOperationException("STATE_CHANGED","Check for an available verified update first.");
                if(!HasNewerUpdate())throw new DesktopOperationException("STATE_CHANGED","No newer update is available for this installation.");
                downloaded?.Dispose();downloaded=null;
                string downloadId=requestId.Value??"update-download";
                using(var downloadToken=CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    downloadCancellation=downloadToken;
                    try{downloaded=await downloader.DownloadAsync(release,new Progress<long>(bytes=>Event?.Invoke(downloadId,"progress",new { bytes,total=release.Asset.Size })),downloadToken.Token);}
                    finally{downloadCancellation=null;}
                }
                return UpdateState();
            case "updates.install": case "updates.setup":
                Confirm(p);if(downloaded is null||release is null)throw new DesktopOperationException("STATE_CHANGED","Download and verify the helper update first.");
                if(!HasNewerUpdate())throw new DesktopOperationException("STATE_CHANGED","The installed version is already current. Check for updates again.");
                await downloader.VerifyAsync(downloaded.Executable,release,token);
                if(Distribution.Installed() is null)return Result(new(OperationOutcome.Warning,"LEGACY_UPGRADE_REQUIRED","This installation needs an explicit upgrade. Download the desktop setup from the release page."));
                await new UpdateSetupHandoff().StartAsync(downloaded.Executable,release);
                return Result(new(OperationOutcome.Warning,"SETUP_HANDOFF","Setup accepted the update. This app will close; setup will report the installation result."));
            default: throw new DesktopOperationException("INVALID_REQUEST","Unknown command.");
        }
    }
    string GetId(InstanceTarget target) { lock(identityLock) { if(!ids.TryGetValue(target,out var id)) { id=Guid.NewGuid().ToString("N");ids[target]=id;targets[id]=target; } return id; } }
    async Task<object> ListAsync(CancellationToken token)
    {
        var result=await Task.Run(()=>collector.Collect(token),token);
        lock(identityLock) {
        var version=++generation;
        var live=new HashSet<InstanceTarget>();
        foreach(var client in result.Instances.Where(x=>x.Windows.Length==0))live.Add(new(client.Key,0));
        var rows=result.Instances.SelectMany(client=>client.Windows.Length==0 ? new[]{ new InstanceDto(GetId(new(client.Key,0)),"Background client",client.Key.Id,labels.Cached(client.Key),client.CpuPercent,client.WorkingSet,InstancePolicy.Started(client.Key),false) } : client.Windows.Select(window=> { var target=new InstanceTarget(client.Key,window.Handle);live.Add(target);return new InstanceDto(GetId(target),window.Title,client.Key.Id,labels.Cached(client.Key),client.CpuPercent,client.WorkingSet,InstancePolicy.Started(client.Key),true); })).ToArray();
        foreach(var target in ids.Keys.Where(x=>!live.Contains(x)).ToArray()){targets.Remove(ids[target]);ids.Remove(target);}
        labels.Schedule(result.Instances.Select(x=>x.Key),version);
        rows=rows.Select(row=>{var optimization=automaticPerformance.Result(new(row.Pid,row.Started.ToFileTimeUtc()));return row with {BypassOptimizations=optimization?.BypassOptimizations??false,OptimizationStatus=optimization?.State??"Unmanaged"};}).ToArray();
        return new InstanceListDto(result.Available,result.Unavailable,rows,version);
        }
    }
    static MonitorDto MapMonitor(LayoutMonitor m)=>new(m.Id,m.Label,new(m.Work.X,m.Work.Y,m.Work.Width,m.Work.Height),m.Dpi,m.Primary);
    object LayoutState()=>new LayoutStateDto(placement.Monitors().Select(MapMonitor).ToArray(),settings.Presets.Select(p=>new PresetDto(p.Name,p.Slots.Select(s=>new SlotDto(s.Monitor,s.X,s.Y,s.Width,s.Height)).ToArray())).ToArray(),settings.Shortcuts);
    async Task<object> PreviewAsync(JsonElement p,CancellationToken token)
    {
        if(!Enum.TryParse<ArrangementKind>(Text(p,"kind","Grid"),true,out var kind))throw new DesktopOperationException("INVALID_REQUEST","Unknown arrangement.");
        var selected=p.GetProperty("ids").EnumerateArray().Select(v=>targets.TryGetValue(v.GetString()!,out var t)?t:throw new DesktopOperationException("STATE_CHANGED","A window selection expired.")).Distinct().ToArray();
        var monitors=placement.Monitors();
        var windows=await Task.Run(()=>selected.Select(t=> { token.ThrowIfCancellationRequested();try { using var session=placement.Open(t);return session.Read(); } catch { return new LayoutWindow(t,"Window",Rectangle.Empty,Size.Empty,false,"Exited or unavailable"); } }).ToArray(),token).WaitAsync(token);
        token.ThrowIfCancellationRequested();
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
    void RequirePlan(JsonElement p) { if(plan is null||Text(p,"planId")!=planId||!plan.Valid)throw new DesktopOperationException("STATE_CHANGED","Create a valid layout preview first."); }
    static void ValidateShortcuts(Dictionary<string,int> bindings)
    {
        if (bindings is null || bindings.Count != LayoutSettings.Default.Shortcuts.Count || bindings.Keys.Any(k => !LayoutSettings.Default.Shortcuts.ContainsKey(k)))
            throw new DesktopOperationException("INVALID_SHORTCUT", "Provide one shortcut for each command.");
        foreach (var (command, keys) in bindings)
            if (!ShortcutPolicy.Valid(keys)) throw new DesktopOperationException("INVALID_SHORTCUT", $"{command}: use Ctrl, Alt or Shift with a letter, digit or F1–F11. Windows-key shortcuts are not supported.");
        var duplicate = bindings.GroupBy(b => b.Value).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new DesktopOperationException("DUPLICATE_SHORTCUT", $"{string.Join(" and ", duplicate.Select(b => b.Key))} use the same shortcut. Choose a different combination for each command.");
    }
    void Save(LayoutSettings next) { if(!settingsAvailable)throw new DesktopOperationException("STATE_CHANGED","Preview layout settings are unavailable. Reset them explicitly in Settings before saving."); ValidateShortcuts(next.Shortcuts); store.Save(next);settings=next;Event?.Invoke("settings","changed",Settings()); }
    object Settings() { notifications.Reload();return new SettingsDto(appearance.Scheme.ToString(),settingsAvailable&&appearance.Available,settings.Shortcuts,new(notifications.Settings.Progress,notifications.Settings.Completion,notifications.Available),imported); }
    object Activity(){bool available=history.Read(out var records);return new ActivityDto(available,records.Select(r=>new ActivityRowDto(r.AttemptId,r.StartedUtc,r.EndedUtc,r.DurationMs,r.Source.ToString(),r.Outcome.ToString(),r.Code.ToString(),DiagnosticMessages.Text(r.Code))).ToArray());}
    void InvalidateProbe(){if(probeFingerprint!=HelperReadiness.Fingerprint())worker=DiagnosticChecks.NotTested;}
    async Task<string> ReadinessAsync(CancellationToken token){InvalidateProbe();var h=await Task.Run(()=>DiagnosticChecks.Collect(worker),token);return HelperReadiness.Evaluate(h,worker,File.Exists(LegacyProgram.InstalledExe)&&!File.Exists(Path.Combine(LegacyProgram.InstallDirectory,"uninstall.pending")));}
    async Task<object> HealthAsync(CancellationToken token){InvalidateProbe();var h=await Task.Run(()=>DiagnosticChecks.Collect(worker),token);return new HealthDto(h.Overall.ToString(),VersionLabel(),h.BrowserIntegrationEnabled,h.Checks.Append(worker).Select(c=>new CheckDto(c.Id,c.Id,c.State.ToString(),c.Code.ToString(),DiagnosticMessages.Text(c.Code),c.Action.ToString())).ToArray(),HelperReadiness.Evaluate(h,worker,File.Exists(LegacyProgram.InstalledExe)&&!File.Exists(Path.Combine(LegacyProgram.InstallDirectory,"uninstall.pending"))));}
    bool HasNewerUpdate()=>release is not null&&new UpdateVersionProvider().Current() is Version installed&&release.Version>installed;
    object UpdateState()=>new UpdateDto(VersionLabel(),release is not null&&new UpdateVersionProvider().Current() is Version installed&&release.Version>installed,release?.Version.ToString(),release?.Title,release?.Notes,release?.DownloadProblem,release?.Asset is not null&&HasNewerUpdate(),downloaded is not null,release?.Page.ToString());
    Task<int> RunHelperAsync(string argument,CancellationToken token)=>RunExecutableAsync(LegacyProgram.InstalledExe,argument,token);
    static async Task<int> RunExecutableAsync(string executable,string argument,CancellationToken token){token.ThrowIfCancellationRequested();var start=new ProcessStartInfo(executable){UseShellExecute=true};foreach(var word in argument.Split(' ',StringSplitOptions.RemoveEmptyEntries))start.ArgumentList.Add(word);using var child=Process.Start(start)??throw new DesktopOperationException("OPERATION_FAILED","The helper could not start.");await child.WaitForExitAsync(CancellationToken.None);return child.ExitCode;}
    public void Dispose(){Interlocked.Exchange(ref disposed,1);DisposeFeatures();DisposePerformance();labels.Dispose();background?.Dispose();downloadCancellation?.Cancel();downloaded?.Dispose();http.Dispose();}
}









