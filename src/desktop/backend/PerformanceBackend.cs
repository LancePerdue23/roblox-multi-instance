using System.Diagnostics;
using System.Text.Json;
using RobloxLaunchHelper;
using LegacyProgram = RobloxLaunchHelper.Program;

namespace RobloxMultiInstance;

public sealed partial class DesktopBackend
{
    PerformanceCollector performanceCollector = null!;
    PerformanceProfileStore performanceProfiles = null!;
    ProcessPolicyService processPolicy = null!;
    AutomaticPerformancePolicy automaticPerformance = null!;
    Task performanceWorker = Task.CompletedTask;
    string performanceWorkerError = "";
    readonly Dictionary<ProcessKey, string> performanceIds = [];
    readonly SemaphoreSlim performanceGate = new(1, 1);
    readonly CancellationTokenSource performanceLifetime = new();
    void InitializePerformance()
    {
        using var current = Process.GetCurrentProcess();
        performanceCollector = new(platform, LegacyProgram.Sid, current.SessionId);
        performanceProfiles = new(Path.Combine(storageRoot, "performance-profiles.json"));
        processPolicy = new(Path.Combine(storageRoot,"process-policy",current.SessionId.ToString()),new ProcessPolicyPlatform(),LegacyProgram.Sid,current.SessionId);
        automaticPerformance = new(Path.Combine(storageRoot,"managed-performance.json"),new NativeOptimizationPlatform(),new OptimizationScheduling(processPolicy));
        accountLauncher.ManagedStarted = RegisterManagedPerformanceAsync;
        accountLauncher.LaunchProfile = LaunchPerformanceProfileAsync;
        if(backgroundMonitoringAllowed)performanceWorker=Task.Run(PerformanceLoopAsync);
    }
    async Task<PerformanceProfile?> LaunchPerformanceProfileAsync(AccountLaunchContext account,CancellationToken token)
    {
        await performanceGate.WaitAsync(token);
        try
        {
            var state=performanceProfiles.Load(out bool available);
            var saved=accounts.State();
            if(!available||!saved.Available)throw new InvalidOperationException("Launch performance settings are unavailable.");
            var selected=saved.Accounts.Single(x=>x.Id==account.Id&&x.RobloxUserId==account.RobloxUserId);
            return state.Enabled&&!selected.BypassOptimizations?state.Profiles.Single(x=>x.Id==state.SelectedProfileId):null;
        }
        finally{performanceGate.Release();}
    }
    void DisposePerformance()
    {
        performanceLifetime.Cancel();
        try {performanceWorker.GetAwaiter().GetResult();}catch(OperationCanceledException){}
        performanceGate.Wait();
        try {automaticPerformance.Dispose();processPolicy.Dispose();}finally {performanceGate.Release();}
    }
    async Task RegisterManagedPerformanceAsync(AccountLaunchContext account,AccountLaunchOutcome outcome,CancellationToken token)
    {
        await performanceGate.WaitAsync(token);
        try {automaticPerformance.Register(account,outcome);TryReconcilePerformance(token);}
        finally {performanceGate.Release();}
    }
    async Task PerformanceLoopAsync()
    {
        while(!performanceLifetime.IsCancellationRequested)
        {
            try
            {
                await performanceGate.WaitAsync(performanceLifetime.Token);
                try {ReconcilePerformance(performanceLifetime.Token);performanceWorkerError="";}
                finally {performanceGate.Release();}
            }
            catch(OperationCanceledException) when(performanceLifetime.IsCancellationRequested){break;}
            catch {performanceWorkerError="Automatic optimization is unavailable. Managed-session or profile storage could not be read; original files were preserved.";}
            try {await Task.Delay(500,performanceLifetime.Token);}catch(OperationCanceledException){break;}
        }
    }
    void ReconcilePerformance(CancellationToken token)
    {
        var policy=performanceProfiles.Load(out bool available);
        if(!available){automaticPerformance.Suspend();throw new InvalidOperationException("Performance profiles are unavailable; automatic changes have stopped.");}
        automaticPerformance.Reconcile(policy,accounts.State(),token);
    }
    void TryReconcilePerformance(CancellationToken token)
    {
        try {ReconcilePerformance(token);performanceWorkerError="";}
        catch(OperationCanceledException){throw;}
        catch {performanceWorkerError="The policy was saved, but automatic controls are unavailable. Managed-session or profile storage could not be accessed; inspect recovery and retry Apply.";}
    }
    async Task RefreshAutomaticPerformanceAsync(CancellationToken token)
    {
        await performanceGate.WaitAsync(token);
        try {automaticPerformance.Retry();TryReconcilePerformance(token);}finally {performanceGate.Release();}
    }
    async Task<object?> ExecutePerformanceAsync(string command, JsonElement payload, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, performanceLifetime.Token);
        token = linked.Token;
        await performanceGate.WaitAsync(token);
        try
        {
            switch (command)
            {
                case "performance.snapshot":
                    var snapshot = await performanceCollector.CollectAsync(token);
                    var keys = snapshot.Processes.Select(x => x.Key).ToHashSet();
                    foreach (var key in performanceIds.Keys.Where(x => !keys.Contains(x)).ToArray()) performanceIds.Remove(key);
                    var rows = snapshot.Processes.Select(x =>
                    {
                        if (!performanceIds.TryGetValue(x.Key, out var id)) performanceIds[x.Key] = id = Guid.NewGuid().ToString("N");
                        return new PerformanceProcessDto(id, x.Key.Id, DateTime.FromFileTimeUtc(x.Key.Created), x.CpuPercent,
                            x.WorkingSet, null, x.PrivateCommit, x.ReadRate, x.WriteRate, x.UptimeSeconds, x.Status);
                    }).ToArray();
                    return new PerformanceSnapshotDto(snapshot.Generation, snapshot.ObservedAt, snapshot.Available, snapshot.Unavailable, rows,
                        new(rows.Length, Sum(rows.Select(x => x.CpuPercent)), SumBytes(rows.Select(x => x.WorkingSetBytes)), null,
                            SumBytes(rows.Select(x => x.PrivateCommitBytes)), Sum(rows.Select(x => x.ReadBytesPerSecond)), Sum(rows.Select(x => x.WriteBytesPerSecond))));
                case "performance.controls": return PolicyDto(Text(payload,"id"),processPolicy.Status(PolicyTarget(payload)));
                case "performance.applyControls":
                    Confirm(payload);
                    var applyTarget=PolicyTarget(payload);
                    if(automaticPerformance.IsManaged(applyTarget)&&performanceProfiles.Load(out _).Enabled)throw new DesktopOperationException("AUTOMATIC_POLICY","This managed client follows the global policy. Use its account bypass or change the global preset.");
                    var applied=await Task.Run(()=>processPolicy.Apply(applyTarget,Text(payload,"priority","unchanged"),Text(payload,"ecoQos","unchanged"),token),token);
                    return PolicyDto(Text(payload,"id"),applied);
                case "performance.resetControls":
                    Confirm(payload);
                    var resetTarget=PolicyTarget(payload);
                    if(automaticPerformance.IsManaged(resetTarget)&&performanceProfiles.Load(out _).Enabled)throw new DesktopOperationException("AUTOMATIC_POLICY","Use this account's bypass to restore automatic settings, or apply Roblox defaults globally.");
                    var reset=await Task.Run(()=>processPolicy.Reset(resetTarget),token);
                    return PolicyDto(Text(payload,"id"),processPolicy.Status(resetTarget)) with {Code=reset.Succeeded?"RESET":"RESET_INCOMPLETE",Message=reset.Message};
                case "performance.recoverControls":
                    Confirm(payload);
                    var recoveryProfiles=performanceProfiles.Load(out bool recoveryAvailable,out var recoveryRevision);
                    if(!recoveryAvailable)throw new DesktopOperationException("STATE_CHANGED","Profile storage is unavailable. Restore its availability before recovering automatic controls.");
                    if(recoveryProfiles.Enabled)performanceProfiles.Save(recoveryProfiles with {Enabled=false},token,recoveryRevision);
                    automaticPerformance.Stop();
                    var recovery=await Task.Run(()=>processPolicy.Reset(),token);
                    return new PerformanceRecoveryDto(recovery.Succeeded,recovery.Pending,recovery.Message);
                case "performance.profiles": return PerformanceState();
                case "performance.optimizationState":
                    var policyState=performanceProfiles.Load(out bool policyAvailable);
                    return automaticPerformance.Snapshot(policyState.Enabled) with {Available=policyAvailable&&performanceWorkerError.Length==0,Error=performanceWorkerError};
                case "performance.activate":
                    var activation=ParsePerformancePayload(payload,true);
                    SavePerformanceProfiles(activation,payload,token);
                    automaticPerformance.Retry();TryReconcilePerformance(token);
                    return PerformanceState();
                case "performance.previewImport": return PerformanceState(PerformanceProfileStore.Parse(Text(payload, "json")));
                case "performance.export":
                    var exported = performanceProfiles.Load(out bool available);
                    if (!available) throw new DesktopOperationException("STATE_CHANGED", "Performance profile storage is unavailable. The original file is preserved.");
                    return new PerformanceExportDto(PerformanceProfileStore.Export(exported));
                case "performance.import":
                    Confirm(payload);
                    SavePerformanceProfiles(PerformanceProfileStore.Parse(Text(payload, "json")) with {Enabled=false},payload,token);automaticPerformance.Retry();TryReconcilePerformance(token); return PerformanceState();
                case "performance.saveProfiles":
                    // Build the performance-only envelope; validation rejects all unknown fields within profiles.
                    SavePerformanceProfiles(ParsePerformancePayload(payload,false),payload,token);automaticPerformance.Retry();TryReconcilePerformance(token);return PerformanceState();
                default: throw new DesktopOperationException("INVALID_REQUEST", "Unknown performance operation.");
            }
        }
        finally { performanceGate.Release(); }
    }
    static PerformanceProfiles ParsePerformancePayload(JsonElement payload,bool activate)
    {
        if(!payload.TryGetProperty("profiles",out var profiles))throw new ArgumentException("Choose performance profiles to save.");
        return PerformanceProfileStore.Parse(JsonSerializer.Serialize(new {version=2,selectedProfileId=Text(payload,"selectedProfileId"),profiles,enabled=activate,focusBoost=!payload.TryGetProperty("focusBoost",out var boost)||boost.ValueKind==JsonValueKind.True}));
    }
    void SavePerformanceProfiles(PerformanceProfiles state,JsonElement payload,CancellationToken token)
    {
        string revision=Text(payload,"expectedRevision");
        if(revision.Length!=64)throw new DesktopOperationException("STATE_CHANGED","Reload performance profiles before saving.");
        try {performanceProfiles.Save(state,token,revision);}
        catch(PerformanceProfileConflictException) {throw new DesktopOperationException("STATE_CHANGED","Performance profiles changed in another manager. Reload profiles and review your changes before saving.");}
    }
    ProcessKey PolicyTarget(JsonElement payload)
    {
        string id=Text(payload,"id");
        foreach(var pair in performanceIds)if(pair.Value==id)return pair.Key;
        throw new DesktopOperationException("STATE_CHANGED","The process selection expired. Refresh monitoring and select it again.");
    }
    static PerformanceControlsDto PolicyDto(string id,ProcessPolicyStatus status) => new(id,status.Available,status.OwnsLease,status.RecoveryRequired,
        status.Values is ProcessPolicyValues values?values.Priority switch {0x20=>"normal",0x4000=>"belowNormal",0x40=>"idle",0x80=>"high",0x100=>"realtime",0x8000=>"aboveNormal",_=>"unknown"}:null,
        status.Values is ProcessPolicyValues eco&&eco.EcoControl.HasValue ? eco.EcoControl==1&&eco.EcoState==1 : null,
        status.OwnsPriority,status.OwnsEco,status.Code,status.Message);
    static double? Sum(IEnumerable<double?> values) { var array = values.ToArray(); return array.All(x => x.HasValue) ? array.Sum(x => x!.Value) : null; }
    static ulong? SumBytes(IEnumerable<ulong?> values)
    {
        ulong total = 0;
        foreach (var value in values) { if (!value.HasValue || ulong.MaxValue - total < value.Value) return null; total += value.Value; }
        return total;
    }
    PerformanceProfilesDto PerformanceState(PerformanceProfiles? preview = null)
    {
        var stored = performanceProfiles.Load(out bool available,out string revision);
        var state = preview ?? stored;
        var selected = state.Profiles.Single(x => x.Id == state.SelectedProfileId);
        const string source = "Global policy; account bypass takes precedence";
        return new(state.Version, available, state.SelectedProfileId,
            state.Profiles.Select(x => new PerformanceProfileDto(x.Id, x.Name, x.Description, x.CpuPriority, x.FpsCap, x.GraphicsQuality, x.EcoQos,x.TextureQuality,x.MinimumMsaa,x.DisableGrass,x.ReducedGeometry,x.GraySky,x.WindowBehavior)).ToArray(),
            [new("cpuPriority", "CPU priority", state.Enabled?"Automatic":"Inactive", "Apply optimizations activates this policy for verified managed accounts. Per-client outcomes are reported separately.", false, selected.CpuPriority, "Unchanged", source),
             new("ecoQos", "EcoQoS", state.Enabled?"Automatic":"Inactive", "Background efficiency mode where Windows supports it. Focus boost releases app-owned scheduling changes.", false, selected.EcoQos, "Unchanged", source),
             new("fpsCap", "FPS cap", "Next launch", AutomaticPerformancePolicy.LaunchCompatibility, false, selected.FpsCap?.ToString() ?? "Unchanged", "Unchanged", source),
             new("graphicsQuality", "Graphics quality", "Next launch", AutomaticPerformancePolicy.LaunchCompatibility, false, selected.GraphicsQuality?.ToString() ?? "Unchanged", "Unchanged", source),
             new("textures", "Texture quality", "Unverified", AutomaticPerformancePolicy.LaunchCompatibility, false, selected.TextureQuality, "Unchanged", source),
             new("msaa", "Anti-aliasing", "Unverified", AutomaticPerformancePolicy.LaunchCompatibility, false, selected.MinimumMsaa?"Minimum":"Unchanged", "Unchanged", source),
             new("grass", "Decorative grass", "Unverified", AutomaticPerformancePolicy.LaunchCompatibility, false, selected.DisableGrass?"Disabled":"Unchanged", "Unchanged", source),
             new("geometry", "Geometry detail", "Unverified", AutomaticPerformancePolicy.LaunchCompatibility, false, selected.ReducedGeometry?"Reduced":"Unchanged", "Unchanged", source),
             new("sky", "Sky", "Unverified", AutomaticPerformancePolicy.LaunchCompatibility, false, selected.GraySky?"Gray":"Unchanged", "Unchanged", source),
             new("window", "Window behavior", state.Enabled?"Automatic":"Inactive", "Only verified background windows are changed. Existing fullscreen or maximized windows are preserved; per-client results report actual placement.", false, selected.WindowBehavior, "Unchanged", source),
             new("privateWorkingSet", "Private working set", "Unsupported", "Read-only measurements include working set and private committed bytes. Private working set is unavailable.", false, "Unavailable", "Unavailable", "Process measurements"),
             new("gpu", "GPU usage", "Unsupported", "GPU counter capability detection is not implemented in this delivery.", false, "Unavailable", "Unavailable", "Process measurements")],revision,state.Enabled,state.FocusBoost);
    }
}
