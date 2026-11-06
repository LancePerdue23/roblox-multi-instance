using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using RobloxLaunchHelper;

namespace RobloxMultiInstance;

internal sealed record OptimizedWindow(long Handle,int Flags,int Show,int MinX,int MinY,int MaxX,int MaxY,int Left,int Top,int Right,int Bottom);
internal sealed record ManagedPerformanceSession(string AccountId,string UserId,int Pid,long Created,OptimizedWindow? Original=null,OptimizedWindow? Applied=null);
internal sealed record ManagedPerformanceFile(int Version,ManagedPerformanceSession[] Sessions);
internal sealed record ManagedProbe(string State,bool Focused,long Window);
internal interface IOptimizationPlatform
{
    ManagedProbe Probe(ManagedPerformanceSession session,bool verifyAccount,CancellationToken token);
    OptimizedWindow ReadWindow(ManagedPerformanceSession session,long window);
    void WriteWindow(ManagedPerformanceSession session,OptimizedWindow placement);
}
internal interface IOptimizationScheduling
{
    ProcessPolicyStatus Status(ProcessKey key);
    ProcessPolicyStatus Apply(ProcessKey key,string priority,string eco,CancellationToken token);
    ProcessPolicyRecovery Reset(ProcessKey key);
}
internal sealed class OptimizationScheduling(ProcessPolicyService service) : IOptimizationScheduling,IDisposable
{
    readonly Dictionary<ProcessKey,(ProcessPolicyHandle Handle,ProcessPolicyStatus Status)> observed=[];
    public ProcessPolicyStatus Status(ProcessKey key)
    {
        if(observed.TryGetValue(key,out var previous))
        {
            try
            {
                // Hold a query handle to the original trusted lifetime. Poll native values,
                // not Authenticode, on every focus tick. Mutation still uses the full service.
                if(!previous.Handle.Alive)throw new InvalidOperationException();
                var values=previous.Handle.Read();var owned=previous.Status;
                return owned with {Values=values,OwnsPriority=owned.OwnsPriority&&values.Priority==owned.Values?.Priority,
                    OwnsEco=owned.OwnsEco&&values.EcoControl==owned.Values?.EcoControl&&values.EcoState==owned.Values?.EcoState};
            }
            catch {previous.Handle.Dispose();observed.Remove(key);}
        }
        return Remember(key,service.Status(key));
    }
    ProcessPolicyStatus Remember(ProcessKey key,ProcessPolicyStatus status)
    {
        if(observed.Remove(key,out var old))old.Handle.Dispose();
        if(status.Available)
        {
            ProcessPolicyHandle? handle=null;
            try
            {
                handle=new(key.Id,false);
                if(handle.Identity.Created!=key.Created||!handle.Alive)throw new InvalidOperationException();
                observed[key]=(handle,status);handle=null;
            }
            catch {return status with {Available=false,OwnsPriority=false,OwnsEco=false,Code="STATE_CHANGED",Message="Managed process lifetime changed."};}
            finally {handle?.Dispose();}
        }
        return status;
    }
    public ProcessPolicyStatus Apply(ProcessKey key,string priority,string eco,CancellationToken token)=>Remember(key,service.Apply(key,priority,eco,token));
    public ProcessPolicyRecovery Reset(ProcessKey key)
    {
        if(observed.Remove(key,out var old))old.Handle.Dispose();
        return service.Reset(key);
    }
    public void Dispose(){foreach(var entry in observed.Values)entry.Handle.Dispose();observed.Clear();}
}

// Called under the backend performance gate. No UI polling is needed to enforce policy.
internal sealed class AutomaticPerformancePolicy(string path,IOptimizationPlatform platform,IOptimizationScheduling scheduling,Func<double>? clock=null) : IDisposable
{
    readonly Dictionary<ProcessKey,ManagedPerformanceSession> sessions=[];
    readonly Dictionary<ProcessKey,string> desired=[];
    readonly Dictionary<ProcessKey,double> backgroundSince=[];
    readonly HashSet<ProcessKey> verified=[];
    readonly Dictionary<ProcessKey,OptimizationInstanceDto> results=[];
    volatile OptimizationInstanceDto[] published=[];
    readonly HashSet<ProcessKey> failed=[];
    readonly HashSet<ProcessKey> windowAttempted=[];
    FileStream? lease;
    bool loaded;
    string lastPolicy="";
    double Now=>(clock??(()=>Environment.TickCount64/1000d))();
    internal static string LaunchCompatibility=>"FPS and graphics requests apply on the next saved-account launch. Launches are serialized while shared Roblox settings are staged, then original settings restore after account verification. Bypassed accounts use original preferences. Visual flags depend on Roblox's current allowlist. Existing clients require a new launch for FPS or graphics changes.";
    internal void Load()
    {
        if(loaded)return;
        if(File.Exists(path))
        {
            AccountStorageSecurity.ValidatePath(path);
            if(new FileInfo(path).Length>256*1024)throw new InvalidDataException("Managed-session storage is too large.");
            var file=JsonSerializer.Deserialize<ManagedPerformanceFile>(File.ReadAllText(path));
            if(file?.Version!=1||file.Sessions is null||file.Sessions.Length>256)throw new InvalidDataException("Managed-session storage is unavailable.");
            var pending=new Dictionary<ProcessKey,ManagedPerformanceSession>();
            foreach(var s in file.Sessions)
            {
                AccountStore.ValidId(s.AccountId);
                if(s.Pid<=0||s.Created<=0||s.Created>DateTime.MaxValue.ToFileTimeUtc()||!LaunchTargetResolver.IsPositiveId(s.UserId)||!pending.TryAdd(new(s.Pid,s.Created),s))throw new InvalidDataException("Invalid managed-session identity.");
            }
            foreach(var pair in pending)sessions.Add(pair.Key,pair.Value);
        }
        loaded=true;
    }
    void Acquire()
    {
        if(lease is not null)return;
        AccountStorageSecurity.EnsurePrivateDirectory(Path.GetDirectoryName(path)!);AccountStorageSecurity.ValidatePath(path+".lock");
        lease=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        loaded=false;sessions.Clear();verified.Clear();desired.Clear();failed.Clear();windowAttempted.Clear();results.Clear();published=[];
    }
    void Save()
    {
        AccountStorageSecurity.EnsurePrivateDirectory(Path.GetDirectoryName(path)!);AccountStorageSecurity.ValidatePath(path);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
            {JsonSerializer.Serialize(stream,new ManagedPerformanceFile(1,sessions.Values.ToArray()));stream.Flush(true);}
            AccountStorageSecurity.EnsurePrivateFile(temporary);File.Move(temporary,path,true);
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal void Register(AccountLaunchContext account,AccountLaunchOutcome outcome)
    {
        if(!outcome.Verified)return;Acquire();Load();
        var key=new ProcessKey(outcome.Pid!.Value,outcome.Created!.Value);
        sessions[key]=new(account.Id,account.RobloxUserId,key.Id,key.Created);
        try {Save();verified.Add(key);}catch {sessions.Remove(key);throw;}
    }
    internal bool IsManaged(ProcessKey key)=>sessions.ContainsKey(key);
    internal OptimizationInstanceDto? Result(ProcessKey key)=>published.FirstOrDefault(x=>x.Pid==key.Id&&x.StartedAt.ToFileTimeUtc()==key.Created);
    internal void Reconcile(PerformanceProfiles policy,AccountsStateDto accounts,CancellationToken token)
    {
        // Do not create files just to inspect an empty configuration. Acquire before reading
        // persisted sessions so a second manager cannot load a stale window journal.
        if(lease is null&&(File.Exists(path)||sessions.Count>0))Acquire();
        Load();var profile=policy.Profiles.Single(x=>x.Id==policy.SelectedProfileId);
        string configuration=JsonSerializer.Serialize(new {policy.Enabled,policy.FocusBoost,Profile=profile});
        if(configuration!=lastPolicy){Retry();lastPolicy=configuration;}
        foreach(var pair in sessions.ToArray())
        {
            token.ThrowIfCancellationRequested();var key=pair.Key;var session=pair.Value;
            string state="Unavailable",message="Account or process identity could not be verified.",effective="Unchanged",windowEffective="Original placement";
            bool bypassRequested=accounts.Accounts.Any(x=>x.Id==session.AccountId&&x.RobloxUserId==session.UserId&&x.BypassOptimizations);
            if(results.TryGetValue(key,out var previous)&&previous.BypassOptimizations!=bypassRequested){failed.Remove(key);desired.Remove(key);}
            string requested=$"Priority {profile.CpuPriority}; efficiency {profile.EcoQos}; window {profile.WindowBehavior}";
            try
            {
                var probe=platform.Probe(session,!verified.Contains(key),token);
                if(probe.State=="Exited")
                {
                    // Reset prunes only this lifetime; it never acts on a reused PID.
                    var status=scheduling.Status(key);
                    if(status.OwnsLease||status.RecoveryRequired)scheduling.Reset(key);
                    sessions.Remove(key);results.Remove(key);desired.Remove(key);verified.Remove(key);backgroundSince.Remove(key);windowAttempted.Remove(key);Save();continue;
                }
                if(failed.Contains(key))continue;
                var account=accounts.Accounts.SingleOrDefault(x=>x.Id==session.AccountId&&x.RobloxUserId==session.UserId);
                if(probe.State!="Verified"||!accounts.Available||account is null)
                {
                    // Release our existing changes when identity becomes unavailable; never adopt this as a new baseline.
                    RestoreScheduling(key);RestoreWindow(key,session);desired.Remove(key);
                }
                else
                {
                    verified.Add(key);
                    if(probe.Focused)backgroundSince.Remove(key);else backgroundSince.TryAdd(key,Now);
                    bool boost=policy.FocusBoost&&(probe.Focused||Now-backgroundSince.GetValueOrDefault(key,Now)<1);
                    bool off=!policy.Enabled||account.BypassOptimizations;
                    bool unsupportedEco=!off&&!boost&&profile.EcoQos!="unchanged"&&scheduling.Status(key).Values?.EcoControl is null;
                    string eco=unsupportedEco?"unchanged":profile.EcoQos;
                    string target=off||boost?"restore":profile.CpuPriority+"/"+eco;
                    if(desired.GetValueOrDefault(key)!=target)
                    {
                        RestoreScheduling(key);
                        if(target!="restore")
                        {
                            var applied=scheduling.Apply(key,profile.CpuPriority,eco,token);
                            if(applied.Code is not ("APPLIED" or "CURRENT"))throw new InvalidOperationException(applied.Message);
                        }
                        desired[key]=target;
                    }
                    var current=scheduling.Status(key);
                    if(target!="restore"&&(!current.Available||profile.CpuPriority!="unchanged"&&!current.OwnsPriority||eco!="unchanged"&&!current.OwnsEco))
                        throw new InvalidOperationException("Scheduling changed outside the app or could not be verified. Apply again to retry.");
                    if(off||profile.WindowBehavior=="unchanged")RestoreWindow(key,sessions[key]);
                    else if(!windowAttempted.Contains(key)&&sessions[key].Original is null&&probe.Window!=0&&!probe.Focused)
                    {
                        var original=platform.ReadWindow(session,probe.Window);
                        // Journal intent before mutation. Respect existing minimized/maximized or fullscreen windows.
                        if(original.Show==1)
                        {
                            var small=original with {Show=2,Right=original.Left+800,Bottom=original.Top+450};
                            sessions[key]=session with {Original=original,Applied=small};Save();
                            platform.WriteWindow(session,small);
                            if(!SamePlacement(platform.ReadWindow(session,probe.Window),small))throw new InvalidOperationException("Small/minimized window placement could not be verified; restoration remains available.");
                        }
                        windowAttempted.Add(key);
                    }
                    if(!off&&profile.WindowBehavior=="smallMinimized")
                    {
                        var owned=sessions[key].Applied;
                        windowEffective=owned is not null&&probe.Window==owned.Handle&&SamePlacement(platform.ReadWindow(session,owned.Handle),owned)?"Small and minimized":"Not applied (focused, window unavailable, or placement changed)";
                    }
                    state=account.BypassOptimizations?"Bypassed":!policy.Enabled||profile.CpuPriority=="unchanged"&&profile.EcoQos=="unchanged"&&profile.WindowBehavior=="unchanged"?"Defaults":boost?"Focused boost":unsupportedEco?"Partial":"Applied";
                    effective=(target=="restore"?"Original scheduling":$"Priority {profile.CpuPriority}; efficiency {(unsupportedEco?"unavailable":eco)}")+"; window: "+windowEffective;
                    message=account.BypassOptimizations?"App-owned optimizations restored; original Roblox settings preserved.":boost?"Original scheduling while focused or during background debounce.":unsupportedEco?"Supported live controls applied; Windows efficiency mode is unavailable. FPS and visuals remain unavailable pending validation.":"Live controls verified. FPS and visual integration remain unavailable pending validation.";
                }
            }
            catch(OperationCanceledException){throw;}
            catch(Exception error){state="Failed";message=error is InvalidOperationException?error.Message:"The client, window, or private storage refused a change. Owned settings remain available for recovery.";failed.Add(key);}
            results[key]=new(session.AccountId,key.Id,DateTime.FromFileTimeUtc(key.Created),state,message,bypassRequested?"Bypass global optimizations":requested,effective,"Unavailable",bypassRequested);
        }
        published=results.Values.ToArray();
    }
    void RestoreScheduling(ProcessKey key)
    {
        var current=scheduling.Status(key);
        if(current.OwnsPriority||current.OwnsEco||current.RecoveryRequired)
        {var reset=scheduling.Reset(key);if(!reset.Succeeded)throw new InvalidOperationException(reset.Message);}
    }
    void RestoreWindow(ProcessKey key,ManagedPerformanceSession session)
    {
        if(session.Original is not {} original||session.Applied is not {} applied)return;
        var probe=platform.Probe(session,false,default);
        if(probe.State=="Exited"){sessions[key]=session with {Original=null,Applied=null};Save();return;}
        var current=platform.ReadWindow(session,original.Handle);
        if(SamePlacement(current,applied))
        {
            platform.WriteWindow(session,original);
            if(!SamePlacement(platform.ReadWindow(session,original.Handle),original))throw new InvalidOperationException("Window restoration could not be verified.");
        }
        sessions[key]=session with {Original=null,Applied=null};Save();
    }
    static bool SamePlacement(OptimizedWindow a,OptimizedWindow b)=>a.Handle==b.Handle&&a.Show==b.Show&&a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom;
    internal void Retry(){desired.Clear();failed.Clear();windowAttempted.Clear();}
    internal void Stop()
    {
        foreach(var pair in sessions.ToArray())try {RestoreScheduling(pair.Key);RestoreWindow(pair.Key,pair.Value);}catch {/* Durable journals remain for recovery. */}
    }
    internal void Suspend()
    {
        Stop();desired.Clear();
        published=published.Select(x=>x with {State="Unavailable",Effective="Restoration requested; inspect recovery status",Message="Profile storage is unavailable. Automatic changes are suspended."}).ToArray();
    }
    public void Dispose(){Stop();lease?.Dispose();lease=null;(platform as IDisposable)?.Dispose();(scheduling as IDisposable)?.Dispose();}
    internal OptimizationStateDto Snapshot(bool enabled){var rows=published;return new(enabled,rows.Count(x=>x.State is "Applied" or "Focused boost" or "Partial"),rows.Count(x=>x.State=="Bypassed"),rows.Count(x=>x.State is "Unavailable" or "Partial"),rows.Count(x=>x.State=="Failed"),rows,LaunchCompatibility);}
}

internal sealed class NativeOptimizationPlatform : IOptimizationPlatform,IDisposable
{
    readonly InstancePlatform platform=new();
    readonly Dictionary<ProcessKey,IInstanceProcess> trusted=[];
    readonly Dictionary<ProcessKey,(long Handle,long Checked)> windows=[];
    readonly int session=Process.GetCurrentProcess().SessionId;
    IInstanceProcess Open(ManagedPerformanceSession target)
    {
        var key=new ProcessKey(target.Pid,target.Created);
        if(trusted.TryGetValue(key,out var existing))
        {
            if(existing.IsAlive)return existing;
            existing.Dispose();trusted.Remove(key);
            throw new ArgumentException("Managed process exited.");
        }
        var process=platform.Open(target.Pid);
        try
        {
            if(!process.IsAlive||process.Identity.Created!=target.Created||!InstancePolicy.Matches(process.Identity,RobloxLaunchHelper.Program.Sid,session)||!platform.Trusted(process.Identity.Image))throw new InvalidOperationException("Managed process identity changed.");
            trusted[key]=process;return process;
        }
        catch {process.Dispose();throw;}
    }
    public ManagedProbe Probe(ManagedPerformanceSession target,bool verifyAccount,CancellationToken token)
    {
        try
        {
            var key=new ProcessKey(target.Pid,target.Created);
            if(trusted.TryGetValue(key,out var held)&&!held.IsAlive){held.Dispose();trusted.Remove(key);windows.Remove(key);return new("Exited",false,0);}
            if(!trusted.ContainsKey(key))
            {
                using var candidate=platform.Open(target.Pid);
                if(!candidate.IsAlive||candidate.Identity.Created!=target.Created)return new("Exited",false,0);
            }
            var process=Open(target);
            if(verifyAccount&&!InstanceAccounts.ReadLogs(new(target.Pid,target.Created),token).Any(log=>InstanceAccounts.ParseUserId(log)?.ToString()==target.UserId))return new("Unavailable",false,0);
            if(!windows.TryGetValue(key,out var cached)||cached.Handle!=0&&!process.OwnsWindow((nint)cached.Handle)||Environment.TickCount64-cached.Checked>=5000)
            {
                var discovered=process.Windows();cached=(discovered.Length==1?(long)discovered[0].Handle:0,Environment.TickCount64);windows[key]=cached;
            }
            var foreground=GetForegroundWindow();
            return new("Verified",foreground!=0&&process.OwnsWindow(foreground),cached.Handle);
        }
        catch(ArgumentException){return new("Exited",false,0);}
        catch(System.ComponentModel.Win32Exception error) when(error.NativeErrorCode is 87 or 1168){return new("Exited",false,0);}
    }
    public OptimizedWindow ReadWindow(ManagedPerformanceSession target,long window)
    {
        var process=Open(target);if(!process.OwnsWindow((nint)window))throw new InvalidOperationException("Managed window changed.");
        var value=new Placement {Length=Marshal.SizeOf<Placement>()};
        if(!GetWindowPlacement((nint)window,ref value))throw new System.ComponentModel.Win32Exception();
        return new(window,value.Flags,value.Show,value.MinX,value.MinY,value.MaxX,value.MaxY,value.Left,value.Top,value.Right,value.Bottom);
    }
    public void WriteWindow(ManagedPerformanceSession target,OptimizedWindow value)
    {
        var process=Open(target);if(!process.OwnsWindow((nint)value.Handle))throw new InvalidOperationException("Managed window changed.");
        if(process.Windows().Length!=1)throw new InvalidOperationException("Multiple client windows are present. Automatic placement was skipped.");
        if(value.Show==2)
        {
            using var session=new PlacementSession(new(new(target.Pid,target.Created),(nint)value.Handle));
            if(session.Read().NeedsWindowed)throw new InvalidOperationException("Fullscreen windows are preserved. Switch this client to windowed mode before applying small-window optimization.");
        }
        var placement=new Placement {Length=Marshal.SizeOf<Placement>(),Flags=value.Flags,Show=value.Show,MinX=value.MinX,MinY=value.MinY,MaxX=value.MaxX,MaxY=value.MaxY,Left=value.Left,Top=value.Top,Right=value.Right,Bottom=value.Bottom};
        if(!SetWindowPlacement((nint)value.Handle,ref placement))throw new System.ComponentModel.Win32Exception();
    }
    [StructLayout(LayoutKind.Sequential)]struct Placement {public int Length,Flags,Show,MinX,MinY,MaxX,MaxY,Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern nint GetForegroundWindow();
    [DllImport("user32.dll",SetLastError=true)]static extern bool GetWindowPlacement(nint window,ref Placement placement);
    [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPlacement(nint window,ref Placement placement);
    public void Dispose(){foreach(var process in trusted.Values)process.Dispose();trusted.Clear();windows.Clear();}
}
