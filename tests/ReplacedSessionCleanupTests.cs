using RobloxLaunchHelper;

internal static class ReplacedSessionCleanupTests
{
    static readonly DateTimeOffset Now=new(2026,11,2,21,0,0,TimeSpan.Zero);
    static readonly ProcessKey Old=new(10,Now.AddMinutes(-5).UtcDateTime.ToFileTimeUtc()),New=new(20,Now.AddSeconds(-10).UtcDateTime.ToFileTimeUtc());
    static readonly SessionEvidence Disconnected=new(123,true,273,Now.AddSeconds(-5)),Connected=new(123,false,null,null);
    static ActiveInstance Row(ProcessKey key)=>new(key,[new(key.Id,"Fixture")],null,null,false);
    static void Check(bool ok){if(!ok)throw new Exception("Replaced-session cleanup assertion failed.");}
    static string Join(string id="123")=>$"{Now.AddSeconds(-10):yyyy-MM-ddTHH:mm:ss.fffZ},1,x,7 [FLog::GameJoinLoadTime] userid:{id}, fixture\n";
    static string Disconnect(int code=273)=>$"{Now.AddSeconds(-5):yyyy-MM-ddTHH:mm:ss.fffZ},2,x,7 [FLog::Network] Disconnect reason received: {code}\n";
    internal static void Run(Action<string,Action> run)
    {
        run("Replaced sessions require exact owned-log join and duplicate-login disconnect markers",()=>{
            Check(ReplacedSessionCleanup.Parse(Join()+Disconnect())==Disconnected);
            Check(ReplacedSessionCleanup.Parse(Join()+Disconnect(264)) is {Reason:264,Disconnected:true});
            Check(ReplacedSessionCleanup.Parse(Join())==Connected);
            Check(ReplacedSessionCleanup.Parse(Disconnect()) is null);
            Check(ReplacedSessionCleanup.Parse(Join()+"[FLog::Output] fake Disconnect reason received: 273\n")==Connected);
            Check(ReplacedSessionCleanup.Parse(Join()+$"{Now:yyyy-MM-ddTHH:mm:ss.fffZ},3,x,7 [FLog::Output] [FLog::Network] Disconnect reason received: 273\n")==Connected);
            Check(ReplacedSessionCleanup.Parse(Join()+Disconnect()+$"{Now:yyyy-MM-ddTHH:mm:ss.fffZ},3,x,7 [FLog::GameJoinLoadTime] userid:123, rejoin\n")==Connected);
            Check(ReplacedSessionCleanup.Parse(Join()+Disconnect(277)) is {Disconnected:true,Reason:277});
            Check(ReplacedSessionCleanup.Parse(Join("9223372036854775808")) is null);
        });
        run("Only baseline clients displaced by a newly joined local same-account client qualify",()=>{
            var data=new Dictionary<ProcessKey,SessionEvidence?>{{Old,Disconnected},{New,Connected}};
            var cleanup=new ReplacedSessionCleanup((k,_)=>data.GetValueOrDefault(k),_=>throw new Exception(),_=>true,"owner",1,()=>Now);
            var before=new InstanceSnapshot([Row(Old)],0);var after=new InstanceSnapshot([Row(Old),Row(New)],0);
            Check(cleanup.Candidates(before,after,default).Single().Old==Old);
            data[New]=Connected with {UserId=999};Check(cleanup.Candidates(before,after,default).Length==0);
            data[New]=Connected;data[Old]=Connected;Check(cleanup.Candidates(before,after,default).Length==0);
            data[Old]=Disconnected with {Reason=277};Check(cleanup.Candidates(before,after,default).Length==0);
            data[Old]=Disconnected with {DisconnectedUtc=Now.AddMinutes(-3)};Check(cleanup.Candidates(before,after,default).Length==0);
            data[Old]=Disconnected;data[New]=Connected with {Disconnected=true,Reason=285};Check(cleanup.Candidates(before,after,default).Length==0);
            data[New]=Connected;Check(cleanup.Candidates(after,after,default).Length==0);
            Check(cleanup.Candidates(before,after with {Unavailable=1},default).Length==0);
            data[New]=null;Check(cleanup.Candidates(before,after,default).Length==0);
        });
        run("Replacement cleanup gracefully closes only a still-disconnected verified old window",()=>{
            var old=new Process(Old);var newer=new Process(New);var data=new Dictionary<ProcessKey,SessionEvidence?>{{Old,Disconnected},{New,Connected}};
            var cleanup=new ReplacedSessionCleanup((k,_)=>data.GetValueOrDefault(k),pid=>pid==Old.Id?old:newer,_=>true,"owner",1,()=>Now);
            var candidate=new ReplacedSession(Old,New,Disconnected.DisconnectedUtc!.Value);
            Check(cleanup.Execute(candidate,default)&&old.Closes==1&&newer.Closes==0);
            old.Closes=0;old.Identity=old.Identity with {Created=Old.Created+1};Check(!cleanup.Execute(candidate,default)&&old.Closes==0);
            old.Identity=old.Identity with {Created=Old.Created,Sid="another"};Check(!cleanup.Execute(candidate,default)&&old.Closes==0);
            old.Identity=old.Identity with {Sid="owner",Elevated=true};Check(!cleanup.Execute(candidate,default));
            old.Identity=old.Identity with {Elevated=false};data[Old]=Connected;Check(!cleanup.Execute(candidate,default));
            data[Old]=Disconnected;newer.IsAlive=false;Check(!cleanup.Execute(candidate,default));
            newer.IsAlive=true;int reads=0;
            var changed=new ReplacedSessionCleanup((k,_)=>k==New?Connected:++reads==1?Disconnected:Connected,pid=>pid==Old.Id?old:newer,_=>true,"owner",1,()=>Now);
            Check(!changed.Execute(candidate,default)&&old.Closes==0);
            using var canceled=new CancellationTokenSource();canceled.Cancel();
            try{cleanup.Execute(candidate,canceled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
        });
    }
    sealed class Process(ProcessKey key):IInstanceProcess
    {
        public Identity Identity{get;set;}=new("owner",1,false,@"C:\Fixture\RobloxPlayerBeta.exe",key.Created);
        public bool IsAlive{get;set;}=true;
        public int Closes;
        public long? CpuTime()=>null;public ulong? WorkingSet()=>null;
        public InstanceWindow[] Windows()=>[new(key.Id,"Fixture")];
        public bool OwnsWindow(nint window)=>window==key.Id;
        public void Restore(nint window){}public bool Focus(nint window)=>false;
        public bool Close(nint window){Closes++;return true;}
        public void Dispose(){}
    }
}
