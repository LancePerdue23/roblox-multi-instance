using RobloxLaunchHelper;
using System.Reflection;
using CheckState=RobloxLaunchHelper.CheckState;

internal static class ReliabilityTests
{
    static void Check(bool value){if(!value)throw new Exception("Reliability assertion failed.");}
    static void Reject(Action action){try{action();throw new Exception("Unexpected success.");}catch(InvalidOperationException){}catch(InvalidDataException){}}
    internal static void Run(Action<string,Action> run)
    {
        run("Interactive CLI actions use valid commands and human output handles empty and nested results",()=>{
            foreach(var action in TerminalMenu.Actions)
            {
                var command=action.Command.ToList();
                Terminal.ValidateArguments(command.ToArray());
                Check(!command.Contains("--yes"));
            }
            string text=TerminalMenu.Format(new { browserIntegration=true, records=Array.Empty<string>(), health=new { message="Ready", problem=(string?)null } });
            Check(text.Contains("Browser Integration: True")&&text.Contains("None")&&text.Contains("Message: Ready")&&text.Contains("Problem: Unavailable"));
        });
        run("Operation outcomes preserve legacy uncertainty, elevation cancellation, focus denial and requested close",()=>{
            Check(OperationResult.FromExit(1,true).Outcome==OperationOutcome.Unknown);
            Check(OperationResult.FromExit(1223).Outcome==OperationOutcome.Canceled);
            Check(OperationResult.ForAction(InstanceActionResult.FocusDenied).Outcome==OperationOutcome.Warning);
            Check(OperationResult.ForAction(InstanceActionResult.CloseRequested).Message=="Close requested.");
            Check(OperationResult.ForAction(InstanceActionResult.CloseFailed).Outcome==OperationOutcome.Failed);
            Check(new OperationResult(OperationOutcome.PartiallySucceeded,"test","").ExitCode==3);
        });
        run("Cleanup aggregates successful, skipped, failed and unconfirmed clients without inventing success",()=>{
            var partial=OperationResult.ForCleanup([BackgroundCleanupResult.Stopped,BackgroundCleanupResult.Skipped,BackgroundCleanupResult.Unavailable,BackgroundCleanupResult.ExitUnconfirmed]);
            Check(partial.Outcome==OperationOutcome.PartiallySucceeded&&partial.Items?.Length==4);
            Check(OperationResult.ForCleanup([BackgroundCleanupResult.Skipped]).Outcome==OperationOutcome.Succeeded);
            Check(OperationResult.ForCleanup([BackgroundCleanupResult.ExitUnconfirmed]).Outcome==OperationOutcome.Unknown);
            Check(OperationResult.ForCleanup([BackgroundCleanupResult.Unavailable]).Outcome==OperationOutcome.Failed);
        });
        run("Resource guards permit launch download and reads but retain conflicting mutations until lease disposal",()=>{
            var coordinator=new OperationCoordinator();
            using(var launch=coordinator.Enter("launch")){
                using var download=coordinator.Enter("updates.download");using var read=coordinator.Enter("system.health");using var discovery=coordinator.Enter("instances.list");
                Reject(()=>coordinator.Enter("launch"));Reject(()=>coordinator.Enter("system.maintenance"));
                using var window=coordinator.Enter("layouts.apply");Reject(()=>coordinator.Enter("instances.close"));
            }
            using(var maintenance=coordinator.Enter("system.maintenance")){Reject(()=>coordinator.Enter("settings.save"));Reject(()=>coordinator.Enter("launch"));using var read=coordinator.Enter("app.state");}
            using(var batch=coordinator.Enter("accounts.launchBatch")){Reject(()=>coordinator.Enter("layouts.apply"));Reject(()=>coordinator.Enter("launch"));using var read=coordinator.Enter("accounts.state");}
            using(var windows=coordinator.Enter("layouts.apply")){Reject(()=>coordinator.Enter("accounts.launchBatch"));}
            using var final=coordinator.Enter("launch");
        });
        run("Readiness distinguishes installed untested ready repair incompatible and unknown without a worker launch",()=>{
            var healthy=DiagnosticChecks.Result("helper",CheckState.Healthy,DiagnosticCode.Ready);
            HealthSnapshot Snapshot(HealthCheckResult check)=>new(1,"test","Windows","x64",DateTimeOffset.UtcNow,[check],DiagnosticChecks.NotTested);
            Check(HelperReadiness.Evaluate(Snapshot(healthy),DiagnosticChecks.NotTested,false)=="Not installed");
            Check(HelperReadiness.Evaluate(Snapshot(healthy),DiagnosticChecks.NotTested,true)=="Installed — not tested");
            Check(HelperReadiness.Evaluate(Snapshot(healthy),DiagnosticChecks.Result("worker",CheckState.Healthy,DiagnosticCode.WorkerReady),true)=="Ready");
            Check(HelperReadiness.Evaluate(Snapshot(healthy with {State=CheckState.Failed}),DiagnosticChecks.NotTested,true)=="Needs repair");
            Check(HelperReadiness.Evaluate(Snapshot(healthy with {Code=DiagnosticCode.HelperVersionMismatch}),DiagnosticChecks.NotTested,true)=="Update required");
            Check(HelperReadiness.Evaluate(Snapshot(healthy with {State=CheckState.Unknown}),DiagnosticChecks.NotTested,true)=="Unable to verify");
            Check(HelperReadiness.Evaluate(Snapshot(healthy with {Code=DiagnosticCode.IntegrationDisabled}),DiagnosticChecks.NotTested,true)=="Installed — not tested");
        });
        run("Account enrichment returns immediately, bounds four workers, coalesces lifetimes and drops exited results",()=>{
            using var release=new ManualResetEventSlim();int running=0,peak=0,calls=0;
            using var service=new AccountEnrichment((key,token)=>{int n=Interlocked.Increment(ref running);int before;do{before=peak;}while(n>before&&Interlocked.CompareExchange(ref peak,n,before)!=before);Interlocked.Increment(ref calls);release.Wait(token);Interlocked.Decrement(ref running);return "Fixture";});
            var keys=Enumerable.Range(1,20).Select(i=>new ProcessKey(i,i)).ToArray();var updates=new List<(long,ProcessKey)>();service.Resolved+=(version,key,_)=>{lock(updates)updates.Add((version,key));};
            service.Schedule(keys,1);service.Schedule(keys,2);Check(service.Cached(keys[0]) is null);
            Check(SpinWait.SpinUntil(()=>Volatile.Read(ref running)==4,3000));Check(peak==4&&calls==4);
            service.Schedule([keys[0]],3);release.Set();service.StopAsync().GetAwaiter().GetResult();
            lock(updates)Check(updates.All(x=>x.Item1==3&&x.Item2==keys[0]));Check(peak<=4);
            Check(service.Cached(new(keys[0].Id,100)) is null);
        });
        run("Core assembly has no desktop presentation references and helper manifests reject desktop or escaped payloads",()=>{
            Check(!typeof(OperationResult).Assembly.GetReferencedAssemblies().Any(x=>x.Name is "System.Windows.Forms" or "PresentationFramework" or "RobloxMultiInstance.Presentation"));
            var hash=new string('A',64);var files=new[]{new PayloadFile("RobloxLaunchHelper.exe",1,hash),new PayloadFile("rmi.exe",1,hash)};
            var manifest=new EditionManifest(1,"Helper","1.1.0",1,"fixture",files);Distribution.Validate(manifest);
            Reject(()=>Distribution.Validate(manifest with {Files=[..files,new("web/index.html",1,hash)]}));
            Reject(()=>Distribution.Validate(manifest with {Files=[..files,new("../outside.exe",1,hash)]}));
            Reject(()=>Distribution.Validate(manifest with {Files=[..files,files[0]]}));
            Reject(()=>Distribution.Validate(manifest with {Protocol=9}));
            Check(Distribution.SetupName("Desktop")=="RobloxMultiInstance-Setup.exe");
        });
        run("Release 1.0.0 rejects modern and legacy downgrades, unreadable versions and edition switches",()=>{
            var hash=new string('A',64);
            var incoming=new EditionManifest(1,"Helper","1.0.0",1,"fixture",[new("RobloxLaunchHelper.exe",1,hash),new("rmi.exe",1,hash)]);
            void Block(EditionManifest? previous,bool exists,string? version){try{Distribution.ValidateUpgrade(incoming,previous,exists,version);}catch(UserError){return;}throw new Exception("Unsafe installation accepted.");}
            Distribution.ValidateUpgrade(incoming,null,false,null);
            Distribution.ValidateUpgrade(incoming,null,true,"0.9.0");
            Distribution.ValidateUpgrade(incoming,incoming,true,"1.0.0.0");
            Block(null,true,"1.0.1");Block(null,true,"1.1.0");Block(null,true,null);Block(null,true,"invalid");
            Block(incoming with {Version="1.1.0"},false,null);
            Block(incoming,true,"1.1.0");
            Block(incoming with {Edition="Desktop"},true,"1.0.0");
        });
        run("Terminal rejects unknown commands and canceled work before reading or changing Windows state",()=>{
            Check(Terminal.ValidCommand(["status"])&&!Terminal.ValidCommand(["bogus"]));
            foreach(var invalid in new[]{new[]{"setup","--bogus","--yes"},new[]{"setup","--player"},new[]{"status","unexpected"},new[]{"report","export"},new[]{"settings","reset","unexpected"}})
            {try{Terminal.ValidateArguments(invalid);throw new Exception("Invalid arguments accepted.");}catch(ArgumentException){}}
            Terminal.ValidateArguments(["setup","--yes","--player",@"C:\fixture\RobloxPlayerBeta.exe"]);
            var relocated=Launch.BuildStart(PlayerRecovery.WithTarget("\"C:\\old\\RobloxPlayerBeta.exe\" --fixture \"%1\"",@"C:\new\RobloxPlayerBeta.exe"),"roblox:fixture");
            Check(relocated.FileName==@"C:\new\RobloxPlayerBeta.exe"&&relocated.ArgumentList.SequenceEqual(new[]{"--fixture","roblox:fixture"}));
            using var stop=new CancellationTokenSource();stop.Cancel();
            try{Terminal.ExecuteAsync(["setup","--yes"],stop.Token).GetAwaiter().GetResult();throw new Exception("Cancellation ignored.");}catch(OperationCanceledException){}
            try{Terminal.ExecuteAsync(["integration","bogus"],default).GetAwaiter().GetResult();throw new Exception("Invalid usage accepted.");}catch(ArgumentException){}
        });
        run("Terminal JSON usage errors agree with process exit codes and do not mix stderr into stdout",()=>{
            var originalOut=Console.Out;var originalError=Console.Error;
            try
            {
                foreach(var args in new[]{new[]{"bogus","--json"},new[]{"integration","bogus","--json"}})
                {
                    using var output=new StringWriter();using var error=new StringWriter();Console.SetOut(output);Console.SetError(error);
                    int exit=Terminal.RunAsync(args).GetAwaiter().GetResult();
                    using var document=System.Text.Json.JsonDocument.Parse(output.ToString());
                    Check(exit==2&&document.RootElement.GetProperty("exitCode").GetInt32()==2&&error.ToString()=="");
                }
            }
            finally{Console.SetOut(originalOut);Console.SetError(originalError);}
        });
    }
}
