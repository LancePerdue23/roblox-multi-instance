using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using RobloxLaunchHelper;
using RobloxMultiInstance;

internal static class ProcessPolicyTests
{
    internal static int Fixture(string[] args)
    {
        if(args.Length==1&&args[0]=="--policy-fixture") {Console.WriteLine("READY");Console.Out.Flush();Console.In.ReadLineAsync().Wait(TimeSpan.FromSeconds(30));return 0;}
        if(args.Length!=3||args[0]!="--policy-owner"||!Path.IsPathFullyQualified(args[1])||!int.TryParse(args[2],out int pid)||pid<=0)return 2;
        using var probe=new ProcessPolicyHandle(pid,false);
        var identity=probe.Identity;
        var platform=new NativeFixturePlatform(pid);
        // Intentionally exit without Dispose to exercise OS lease release and durable interrupted ownership.
        var policy=new ProcessPolicyService(args[1],platform,identity.Sid,identity.Session);
        var result=policy.Apply(new(pid,identity.Created),"belowNormal","unchanged",default);
        Console.WriteLine(result.Code);
        Console.WriteLine(policy.Apply(new(pid,identity.Created),"idle","unchanged",default).Code);
        Console.Out.Flush();Console.In.ReadLineAsync().Wait(TimeSpan.FromSeconds(30));Environment.Exit(0);return 0;
    }
    static Process Start(params string[] arguments) => StartUsingHost(Environment.ProcessPath!,arguments);
    static Process StartUsingHost(string host,params string[] arguments)
    {
        var start=new ProcessStartInfo(host) {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        // dotnet expects a managed entrypoint; the generated apphost already embeds that entrypoint.
        if(string.Equals(Path.GetFileNameWithoutExtension(host),"dotnet",StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach(string value in arguments)start.ArgumentList.Add(value);
        return Process.Start(start)!;
    }
    internal static async Task<int> Run(string root)
    {
        int count=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
        string assembly=Assembly.GetExecutingAssembly().Location;
        string apphost=Path.ChangeExtension(assembly,".exe");
        string dotnet=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!,"..","..","..","dotnet.exe"));
        foreach(var invocation in new[]{(Name:"apphost",Path:apphost),(Name:"dotnet host",Path:dotnet)})
        {
            using(var child=StartUsingHost(invocation.Path,"--policy-fixture"))
            {
                try
                {
                    Check(await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="READY",invocation.Name+" dispatches directly to the bounded fixture entrypoint");
                    await child.StandardInput.WriteLineAsync("exit");await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Check(child.ExitCode==0,invocation.Name+" fixture exits without recursively running backend tests");
                }
                finally {if(!child.HasExited){child.Kill();child.WaitForExit(3000);}}
            }
            using(var rejected=StartUsingHost(invocation.Path,assembly,"--policy-fixture"))
            {
                try
                {
                    await rejected.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Check(rejected.ExitCode==2&&string.IsNullOrEmpty(await rejected.StandardOutput.ReadToEndAsync()),invocation.Name+" rejects an unexpected entrypoint argument before running tests");
                }
                finally {if(!rejected.HasExited){rejected.Kill();rejected.WaitForExit(3000);}}
            }
        }
        var fake=new FakePlatform();
        var key=new ProcessKey(42,fake.Created);
        string directory=Path.Combine(root,"policy-fixtures");
        using(var first=new ProcessPolicyService(directory,fake,"fixture",7))
        using(var second=new ProcessPolicyService(directory,fake,"fixture",7))
        {
            Check(first.Apply(key,"unchanged","unchanged",default).Code=="CURRENT"&&!Directory.Exists(directory),"Unchanged manual settings do not acquire ownership or create journal files");
            Check(first.Apply(key,"belowNormal","on",default).Code=="APPLIED"&&fake.Values.Priority==0x4000&&fake.Values.EcoState==1,"Manual policy reads back priority and EcoQoS");
            Check(second.Apply(key,"idle","unchanged",default).Code=="POLICY_BUSY","A competing manager cannot mutate another policy owner's clients");
            fake.Values=fake.Values with {Priority=0x80};
            Check(first.Apply(key,"idle","unchanged",default).Code=="EXTERNAL_CHANGE","Policy detects external changes before reapplication");
            var reset=first.Reset(key);
            Check(reset.Succeeded&&fake.Values.Priority==0x80&&fake.Values.EcoControl==0,"Reset preserves external priority and restores original OS-managed EcoQoS");
            Check(first.Reset(key).Succeeded,"Repeated policy reset is idempotent");
            Check(second.Apply(key,"idle","unchanged",default).Code=="APPLIED","Reset releases policy ownership for another manager");
            second.Reset();
            fake.Deny=true;
            Check(first.Apply(key,"idle","unchanged",default).Code=="APPLY_FAILED","Access denial never reports applied success");
            fake.Deny=false;
            first.Reset();
            fake.Created++;
            Check(first.Apply(key,"idle","unchanged",default).Code=="APPLY_FAILED"&&fake.Values.Priority==0x80,"Reused process IDs are refused before mutation");
            first.Reset();fake.Created--;
            fake.FailEco=true;
            var partial=first.Apply(key,"idle","on",default);
            Check(partial.Code=="APPLY_FAILED"&&fake.Values.Priority==0x40,"Partial native failure remains journaled without false success");
            fake.FailEco=false;
            Check(first.Reset().Succeeded&&fake.Values.Priority==0x80,"Reset recovers a partially applied native operation");
            first.Apply(key,"belowNormal","unchanged",default);
        }
        Check(fake.Values.Priority==0x80,"Manager disposal restores its still-owned original settings");
        var reapplyFake=new FakePlatform();
        var reapplyKey=new ProcessKey(42,reapplyFake.Created);
        string reapplyDirectory=Path.Combine(root,"policy-reapply");
        using(var policy=new ProcessPolicyService(reapplyDirectory,reapplyFake,"fixture",7))
        {
            Check(policy.Apply(reapplyKey,"belowNormal","on",default).Code=="APPLIED","Reapply regression establishes original priority and EcoQoS ownership");
            string before=File.ReadAllText(Path.Combine(reapplyDirectory,"journal.json"));
            int priorityWrites=reapplyFake.PriorityWrites,ecoWrites=reapplyFake.EcoWrites;
            reapplyFake.FailPriority=true;reapplyFake.FailEco=true;
            Check(policy.Apply(reapplyKey,"idle","unchanged",default).Code=="RESET_REQUIRED"&&reapplyFake.PriorityWrites==priorityWrites,"Owned priority replacement is rejected before a failing native write");
            Check(policy.Apply(reapplyKey,"unchanged","off",default).Code=="RESET_REQUIRED"&&reapplyFake.EcoWrites==ecoWrites,"Owned EcoQoS replacement is rejected before a failing native write");
            Check(File.ReadAllText(Path.Combine(reapplyDirectory,"journal.json"))==before&&reapplyFake.Values.Priority==0x4000&&reapplyFake.Values.EcoState==1,"Blocked reapply preserves the durable original and prior applied values");
            reapplyFake.FailPriority=false;reapplyFake.FailEco=false;
            Check(policy.Reset().Succeeded&&reapplyFake.Values==new ProcessPolicyValues(0x20,0,0),"Reset after rejected failing reapply restores both original settings");
            Check(policy.Apply(reapplyKey,"idle","unchanged",default).Code=="APPLIED","A new requested value can be applied after explicit reset");
            Check(policy.Apply(reapplyKey,"unchanged","on",default).Code=="APPLIED","An unowned control can be added while the other control remains owned");
            Check(policy.Reset().Succeeded&&reapplyFake.Values==new ProcessPolicyValues(0x20,0,0),"Adding another control retains independent original restoration");
        }
        using var target=Start("--policy-fixture");
        try
        {
            Check(await target.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="READY","Harmless native policy fixture starts without a game");
            using var native=new ProcessPolicyHandle(target.Id,true);
            var original=native.Read();
            try
            {
                native.Priority(0x4000);Check(native.Read().Priority==0x4000,"Native priority control changes the harmless fixture process");
                if(original.EcoControl.HasValue)
                {
                    native.Eco(1,1);Check(native.Read().EcoControl==1&&native.Read().EcoState==1,"Native EcoQoS control is detected and read back on the fixture process");
                    native.Eco(original.EcoControl.Value,original.EcoState!.Value);
                    Check(native.Read().EcoControl==original.EcoControl&&native.Read().EcoState==original.EcoState,"Native EcoQoS restores original explicit or OS-managed state");
                }
                else Console.WriteLine("BLOCKED Native EcoQoS readback unsupported on this Windows build; production EcoQoS remains unavailable.");
            }
            finally {native.Priority(original.Priority);if(original.EcoControl.HasValue)native.Eco(original.EcoControl.Value,original.EcoState!.Value);}
            var cachedPlatform=new NativeFixturePlatform(target.Id);
            using(var automaticService=new ProcessPolicyService(Path.Combine(root,"policy-native-observation"),cachedPlatform,native.Identity.Sid,native.Identity.Session))
            using(var observation=new OptimizationScheduling(automaticService))
            {
                var bound=new ProcessKey(target.Id,native.Identity.Created);
                try
                {
                    Check(observation.Apply(bound,"belowNormal","unchanged",default).Code=="APPLIED","Automatic observation binds a query handle to the verified fixture lifetime");
                    int validations=cachedPlatform.TrustChecks;
                    for(int i=0;i<10;i++)Check(observation.Status(bound).OwnsPriority,"Automatic observation reads actual owned native priority");
                    Check(cachedPlatform.TrustChecks==validations,"Focus polling avoids repeated signature validation on an already-bound process");
                    native.Priority(0x40);
                    Check(!observation.Status(bound).OwnsPriority,"Cached native observation detects external scheduling changes");
                    Check(observation.Reset(bound).Succeeded&&native.Read().Priority==0x40,"Automatic reset preserves an externally changed native priority");
                }
                finally {native.Priority(original.Priority);}
            }
            string crashDirectory=Path.Combine(root,"policy-crash");
            using(var owner=Start("--policy-owner",crashDirectory,target.Id.ToString()))
            {
                try
                {
                    Check(await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="APPLIED","A separate manager journals native priority ownership");
                    Check(await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="RESET_REQUIRED","A separate owner refuses reapply before its abrupt exit");
                    using var contender=new ProcessPolicyService(crashDirectory,new NativeFixturePlatform(target.Id),native.Identity.Sid,native.Identity.Session);
                    var targetKey=new ProcessKey(target.Id,native.Identity.Created);
                    Check(contender.Apply(targetKey,"idle","unchanged",default).Code=="POLICY_BUSY","Cross-process file lease prevents policy takeover");
                    await owner.StandardInput.WriteLineAsync("exit");await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Check(native.Read().Priority==0x4000,"Owner crash does not falsely promise immediate cleanup");
                    Check(contender.Apply(targetKey,"idle","unchanged",default).Code=="RECOVERY_REQUIRED","Interrupted ownership never captures applied priority as a new original");
                    Check(contender.Reset().Succeeded&&native.Read().Priority==original.Priority,"Explicit crash recovery restores the journaled original on the same process lifetime");
                }
                finally {if(!owner.HasExited){owner.Kill();owner.WaitForExit(3000);}}
            }
        }
        finally {if(!target.HasExited){try {await target.StandardInput.WriteLineAsync("exit");}catch {}if(!target.WaitForExit(3000)){target.Kill();target.WaitForExit(3000);}}}
        string corrupt=Path.Combine(root,"policy-corrupt");Directory.CreateDirectory(corrupt);File.WriteAllText(Path.Combine(corrupt,"journal.json"),"corrupt-original");
        using(var blocked=new ProcessPolicyService(corrupt,fake,"fixture",7))
        {
            Check(blocked.Apply(key,"idle","unchanged",default).Code=="JOURNAL_UNAVAILABLE"&&File.ReadAllText(Path.Combine(corrupt,"journal.json"))=="corrupt-original","Corrupt recovery journals block new mutations and preserve original bytes");
        }
        string badPriority=Path.Combine(root,"policy-bad-priority");Directory.CreateDirectory(badPriority);
        string badJson=System.Text.Json.JsonSerializer.Serialize(new ProcessPolicyJournal(1,[new(42,fake.Created,12345,0x40,null,null,null,null)]));
        File.WriteAllText(Path.Combine(badPriority,"journal.json"),badJson);
        using(var blocked=new ProcessPolicyService(badPriority,fake,"fixture",7))
        {
            Check(!blocked.Reset().Succeeded&&File.ReadAllText(Path.Combine(badPriority,"journal.json"))==badJson,"Invalid original priority journals are refused and preserved before native restoration");
        }
        return count;
    }
    sealed class NativeFixturePlatform(int pid) : IProcessPolicyPlatform
    {
        internal int TrustChecks;
        public bool Trusted(string image){TrustChecks++;return image==@"C:\Fixture\RobloxPlayerBeta.exe";}
        public IProcessPolicyHandle Open(int requested,bool write) => requested==pid?new Wrapped(new ProcessPolicyHandle(pid,write)):throw new ArgumentException();
        sealed class Wrapped(ProcessPolicyHandle inner) : IProcessPolicyHandle
        {
            public Identity Identity=>inner.Identity with {Image=@"C:\Fixture\RobloxPlayerBeta.exe"};
            public bool Alive=>inner.Alive;
            public ProcessPolicyValues Read()=>inner.Read();
            public void Priority(uint value)=>inner.Priority(value);
            public void Eco(uint control,uint state)=>inner.Eco(control,state);
            public void Dispose()=>inner.Dispose();
        }
    }
    sealed class FakePlatform : IProcessPolicyPlatform
    {
        internal long Created=DateTime.UtcNow.ToFileTimeUtc();
        internal ProcessPolicyValues Values=new(0x20,0,0);
        internal bool Deny,FailEco,FailPriority;
        internal int PriorityWrites,EcoWrites;
        public bool Trusted(string image)=>true;
        public IProcessPolicyHandle Open(int pid,bool write){if(Deny)throw new Win32Exception(5);return new Handle(this);}
        sealed class Handle(FakePlatform platform) : IProcessPolicyHandle
        {
            public Identity Identity=>new("fixture",7,false,@"C:\Fixture\RobloxPlayerBeta.exe",platform.Created);
            public bool Alive=>true;
            public ProcessPolicyValues Read()=>platform.Values;
            public void Priority(uint value){platform.PriorityWrites++;if(platform.FailPriority)throw new Win32Exception(5);platform.Values=platform.Values with {Priority=value};}
            public void Eco(uint control,uint state){platform.EcoWrites++;if(platform.FailEco)throw new Win32Exception(5);platform.Values=platform.Values with {EcoControl=control,EcoState=state};}
            public void Dispose(){}
        }
    }
}
