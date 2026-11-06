using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace RobloxLaunchHelper;

internal readonly record struct ProcessPolicyValues(uint Priority, uint? EcoControl, uint? EcoState);
internal interface IProcessPolicyHandle : IDisposable
{
    Identity Identity { get; }
    bool Alive { get; }
    ProcessPolicyValues Read();
    void Priority(uint value);
    void Eco(uint control, uint state);
}
internal interface IProcessPolicyPlatform
{
    IProcessPolicyHandle Open(int pid, bool write);
    bool Trusted(string image);
}
internal sealed class ProcessPolicyPlatform : IProcessPolicyPlatform
{
    public IProcessPolicyHandle Open(int pid, bool write) => new ProcessPolicyHandle(pid, write);
    public bool Trusted(string image) => RobloxTrust.IsSignedRoblox(image);
}
internal sealed class ProcessPolicyHandle : IProcessPolicyHandle
{
    readonly SafeProcessHandle handle;
    public Identity Identity { get; }
    internal ProcessPolicyHandle(int pid, bool write)
    {
        handle = Native.OpenProcess(InstanceNative.QueryLimitedInformation | InstanceNative.Synchronize | (write ? 0x0200u : 0u), false, pid);
        try { if(handle.IsInvalid) throw new Win32Exception(); Identity = Native.GetIdentity(handle, pid); }
        catch { handle.Dispose(); throw; }
    }
    public bool Alive => InstanceNative.WaitForSingleObject(handle, 0) == InstanceNative.WaitTimeout;
    public ProcessPolicyValues Read()
    {
        uint priority = ProcessPolicyNative.GetPriorityClass(handle);
        if (priority == 0) throw new Win32Exception();
        var eco = new ProcessPolicyNative.PowerState { Version = 1 };
        bool supported = ProcessPolicyNative.GetProcessInformation(handle, 4, ref eco, 12);
        return new(priority, supported ? eco.ControlMask & 1 : null, supported ? eco.StateMask & 1 : null);
    }
    public void Priority(uint value) { if (!ProcessPolicyNative.SetPriorityClass(handle, value)) throw new Win32Exception(); }
    public void Eco(uint control, uint state)
    {
        var current = new ProcessPolicyNative.PowerState { Version = 1 };
        if (!ProcessPolicyNative.GetProcessInformation(handle, 4, ref current, 12)) throw new Win32Exception();
        current.ControlMask = (current.ControlMask & ~1u) | (control & 1);
        current.StateMask = (current.StateMask & ~1u) | (state & 1);
        if (!ProcessPolicyNative.SetProcessInformation(handle, 4, ref current, 12)) throw new Win32Exception();
    }
    public void Dispose() => handle.Dispose();
}
internal static class ProcessPolicyNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct PowerState { internal uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint GetPriorityClass(SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool SetPriorityClass(SafeProcessHandle process,uint priority);
    [DllImport("kernel32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GetProcessInformation(SafeProcessHandle process,int type,ref PowerState value,uint size);
    [DllImport("kernel32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool SetProcessInformation(SafeProcessHandle process,int type,ref PowerState value,uint size);
}
internal sealed record ProcessPolicyEntry(int Pid,long Created,uint? OriginalPriority,uint? AppliedPriority,uint? OriginalEcoControl,uint? OriginalEcoState,uint? AppliedEcoControl,uint? AppliedEcoState);
internal sealed record ProcessPolicyJournal(int Version,ProcessPolicyEntry[] Entries);
internal sealed record ProcessPolicyStatus(bool Available,bool OwnsLease,bool RecoveryRequired,ProcessPolicyValues? Values,bool OwnsPriority,bool OwnsEco,string Code,string Message);
internal sealed record ProcessPolicyRecovery(bool Succeeded,int Pending,string Message);

// The lease lasts until reset/exit. OS file sharing releases it after a crash; the journal prevents silent takeover.
internal sealed class ProcessPolicyService(string directory,IProcessPolicyPlatform platform,string sid,int session) : IDisposable
{
    readonly object sync = new();
    FileStream? lease;
    bool disposed;
    string JournalPath => Path.Combine(directory,"journal.json");
    static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow, MaxDepth=8 };
    ProcessPolicyJournal ReadJournal()
    {
        if (!File.Exists(JournalPath)) return new(1,[]);
        using var stream = new FileStream(JournalPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>64*1024)throw new InvalidDataException();
        var value=JsonSerializer.Deserialize<ProcessPolicyJournal>(stream,Json);
        if(value is null || value.Version!=1 || value.Entries is null || value.Entries.Length>256 || value.Entries.Select(x=>(x.Pid,x.Created)).Distinct().Count()!=value.Entries.Length ||
            value.Entries.Any(x=>x.Pid<=0||x.Created<=0||x.OriginalPriority.HasValue!=x.AppliedPriority.HasValue||x.OriginalPriority is uint original&&original is not (0x20 or 0x40 or 0x80 or 0x100 or 0x4000 or 0x8000)||x.AppliedPriority is uint applied&&applied is not (0x20 or 0x40 or 0x4000)||x.OriginalEcoControl.HasValue!=x.OriginalEcoState.HasValue||x.OriginalEcoControl.HasValue!=x.AppliedEcoControl.HasValue||x.OriginalEcoState.HasValue!=x.AppliedEcoState.HasValue||x.OriginalEcoControl is >1||x.OriginalEcoState is >1||x.AppliedEcoControl is >1||x.AppliedEcoState is >1)) throw new InvalidDataException();
        return value;
    }
    void Save(ProcessPolicyJournal journal)
    {
        string temporary=JournalPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) { JsonSerializer.Serialize(stream,journal,Json);stream.Flush(true); }
            File.Move(temporary,JournalPath,true);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    bool Acquire()
    {
        if(lease is not null)return true;
        Directory.CreateDirectory(directory);
        try { lease=new FileStream(Path.Combine(directory,"owner.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);return true; }
        catch(IOException){return false;}
    }
    IProcessPolicyHandle Open(ProcessKey key,bool write)
    {
        var process=platform.Open(key.Id,write);
        try
        {
            var identity=process.Identity;
            if(identity.Created!=key.Created||!InstancePolicy.Matches(identity,sid,session)||!platform.Trusted(identity.Image)||!process.Alive)throw new InvalidOperationException("The trusted process selection changed.");
            return process;
        }
        catch {process.Dispose();throw;}
    }
    internal ProcessPolicyStatus Status(ProcessKey key)
    {
        lock(sync)
        {
            try
            {
                var journal=ReadJournal();var entry=journal.Entries.FirstOrDefault(x=>x.Pid==key.Id&&x.Created==key.Created);
                using var process=Open(key,false);var values=process.Read();
                return new(true,lease is not null,lease is null&&journal.Entries.Length>0,values,lease is not null&&entry?.AppliedPriority is uint applied&&values.Priority==applied,lease is not null&&entry?.AppliedEcoControl is uint control&&values.EcoControl==control&&values.EcoState==entry.AppliedEcoState,"CURRENT","Manual controls are separate from saved profiles. Reset restores values still owned by this manager.");
            }
            catch { return new(false,lease is not null,true,null,false,false,"INSPECTION_UNAVAILABLE","Process settings or the recovery journal could not be inspected. No setting was changed."); }
        }
    }
    internal ProcessPolicyStatus Apply(ProcessKey key,string priority,string eco,CancellationToken token)
    {
        if(priority is not ("unchanged" or "normal" or "belowNormal" or "idle")||eco is not ("unchanged" or "on" or "off"))throw new ArgumentException("Choose supported manual process settings.");
        if(priority=="unchanged"&&eco=="unchanged")return Status(key) with {Code="CURRENT",Message="No manual setting changes were requested."};
        lock(sync)
        {
            if(disposed)throw new ObjectDisposedException(nameof(ProcessPolicyService));
            token.ThrowIfCancellationRequested();
            if(!Acquire())return new(false,false,false,null,false,false,"POLICY_BUSY","Another manager owns process policy. Reset its controls or close it first.");
            ProcessPolicyJournal journal;
            try {journal=ReadJournal();}
            catch {Release();return new(false,false,true,null,false,false,"JOURNAL_UNAVAILABLE","The recovery journal is unavailable and has been preserved. No setting was changed.");}
            // An acquired lease with preexisting journal entries is an interrupted owner, never an implicit new baseline.
            if(!owned && journal.Entries.Length>0) {Release();return new(false,false,true,null,false,false,"RECOVERY_REQUIRED","An earlier manager left owned settings. Review and run recovery before applying new controls.");}
            owned=true;
            try
            {
                using var process=Open(key,true);var current=process.Read();
                var entry=journal.Entries.FirstOrDefault(x=>x.Pid==key.Id&&x.Created==key.Created)??new(key.Id,key.Created,null,null,null,null,null,null);
                if(entry.AppliedPriority is uint lastPriority&&current.Priority!=lastPriority || entry.AppliedEcoControl is uint lastControl&&(current.EcoControl!=lastControl||current.EcoState!=entry.AppliedEcoState))
                    return Status(key) with { Code="EXTERNAL_CHANGE",Message="Another tool changed an owned setting. Reset ownership before applying again; external values will be preserved." };
                // Never replace a durable applied value with a new intent before resetting its original.
                // Otherwise a failed/crashed reapply could mistake our previous value for an external change.
                if(priority!="unchanged"&&entry.AppliedPriority.HasValue || eco!="unchanged"&&entry.AppliedEcoControl.HasValue)
                    return Status(key) with {Code="RESET_REQUIRED",Message="Reset this process before changing an already-owned control. Its original settings remain preserved."};
                if(eco!="unchanged"&&current.EcoControl is null)return Status(key) with {Code="ECO_UNAVAILABLE",Message="EcoQoS cannot be read reliably on this system. No settings were changed."};
                uint? nextPriority=priority switch {"normal"=>0x20u,"belowNormal"=>0x4000u,"idle"=>0x40u,_=>null};
                uint? nextEco=eco=="unchanged"?null:eco=="on"?1u:0u;
                entry=entry with {OriginalPriority=entry.OriginalPriority??(nextPriority.HasValue?current.Priority:null),AppliedPriority=nextPriority??entry.AppliedPriority,
                    OriginalEcoControl=entry.OriginalEcoControl??(nextEco.HasValue?current.EcoControl:null),OriginalEcoState=entry.OriginalEcoState??(nextEco.HasValue?current.EcoState:null),
                    AppliedEcoControl=nextEco.HasValue?1u:entry.AppliedEcoControl,AppliedEcoState=nextEco??entry.AppliedEcoState};
                journal=journal with {Entries=[..journal.Entries.Where(x=>x.Pid!=key.Id||x.Created!=key.Created),entry]};
                if(journal.Entries.Length>256)throw new InvalidOperationException();
                token.ThrowIfCancellationRequested();Save(journal); // durable intent precedes each mutation
                if(!process.Alive)throw new InvalidOperationException();
                if(nextPriority.HasValue)process.Priority(nextPriority.Value);
                if(nextEco.HasValue)process.Eco(1,nextEco.Value);
                var verified=process.Read();
                bool applied=process.Alive&&(!nextPriority.HasValue||verified.Priority==nextPriority)&&(!nextEco.HasValue||verified.EcoControl==1&&verified.EcoState==nextEco);
                return Status(key) with {Code=applied?"APPLIED":"VERIFY_FAILED",Message=applied?"Requested manual settings were read back successfully. No resource savings are assumed.":"Settings could not be verified. Reset or recovery remains available for journaled changes."};
            }
            catch(OperationCanceledException){throw;}
            catch {return Status(key) with {Code="APPLY_FAILED",Message="The process refused a setting, exited, or storage failed. Changes may be partial; reset or recover journaled settings."};}
        }
    }
    bool owned;
    internal ProcessPolicyRecovery Reset(ProcessKey? target=null)
    {
        lock(sync)
        {
            if(!Acquire())return new(false,0,"Another manager owns process policy. Its controls must be reset there.");
            ProcessPolicyJournal journal;
            try {journal=ReadJournal();}
            catch {Release();return new(false,0,"The recovery journal could not be read and is preserved. No setting was changed.");}
            var remaining=new List<ProcessPolicyEntry>();int failed=0;bool external=false;
            foreach(var entry in journal.Entries)
            {
                if(target is ProcessKey selected&&(selected.Id!=entry.Pid||selected.Created!=entry.Created)){remaining.Add(entry);continue;}
                try
                {
                    using var process=platform.Open(entry.Pid,true);
                    var identity=process.Identity;
                    if(identity.Created!=entry.Created||!process.Alive)continue; // exited lifetime cannot be restored
                    if(!InstancePolicy.Matches(identity,sid,session)||!platform.Trusted(identity.Image))throw new InvalidOperationException();
                    var current=process.Read();var pending=entry;
                    if(entry.AppliedPriority is uint applied)
                    {
                        if(current.Priority==applied) {process.Priority(entry.OriginalPriority!.Value);if(process.Read().Priority!=entry.OriginalPriority)throw new InvalidOperationException();}
                        else external=true;
                        pending=pending with {OriginalPriority=null,AppliedPriority=null};
                    }
                    if(entry.AppliedEcoControl is uint ecoControl)
                    {
                        current=process.Read();
                        if(current.EcoControl==ecoControl&&current.EcoState==entry.AppliedEcoState) {process.Eco(entry.OriginalEcoControl!.Value,entry.OriginalEcoState!.Value);var check=process.Read();if(check.EcoControl!=entry.OriginalEcoControl||check.EcoState!=entry.OriginalEcoState)throw new InvalidOperationException();}
                        else if(current.EcoControl is null)throw new InvalidOperationException();else external=true;
                        pending=pending with {OriginalEcoControl=null,OriginalEcoState=null,AppliedEcoControl=null,AppliedEcoState=null};
                    }
                }
                catch(ArgumentException) { /* Process ID no longer exists. */ }
                catch(Win32Exception error) when(error.NativeErrorCode is 87 or 1168) { /* The journaled process has exited. */ }
                catch {remaining.Add(entry);failed++;}
            }
            try {Save(new(1,remaining.ToArray()));}
            catch {return new(false,journal.Entries.Length,"Restoration ran but the recovery journal could not be updated. Review settings before retrying.");}
            owned=remaining.Count>0;
            if(remaining.Count==0)Release();
            return new(failed==0,remaining.Count,failed>0?"Some settings could not be restored. The journal is retained for retry.":external?"Reset completed. External setting changes were preserved.":"Owned settings were restored where the process lifetime still exists.");
        }
    }
    void Release(){lease?.Dispose();lease=null;owned=false;}
    public void Dispose(){lock(sync){if(disposed)return;disposed=true;if(lease is not null){try{Reset();}catch{}Release();}}}
}
