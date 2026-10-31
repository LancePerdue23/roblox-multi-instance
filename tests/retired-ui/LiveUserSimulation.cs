using System.Diagnostics;
using System.Text.Json;
using RobloxLaunchHelper;

// Explicit opt-in only. Never terminates or moves clients that existed at entry.
internal static class LiveUserSimulation
{
    private sealed record Outcome(string Id,string Journey,string Status,string Evidence,double TechnicalMs);
    private static readonly List<Outcome> results=[];
    private static readonly HashSet<ProcessKey> owned=[];
    private static readonly Dictionary<InstanceTarget,Rectangle> original=[];
    private static HashSet<ProcessKey> baseline=[];
    private static bool placed;
    private static InstanceSnapshot Snapshot()=>new InstanceCollector(new InstancePlatform(),Program.Sid,Process.GetCurrentProcess().SessionId).Collect(default);
    private static ActiveInstance[] NewClients()
    {
        var clients=Snapshot().Instances.Where(i=>!baseline.Contains(i.Key)).ToArray();
        foreach(var client in clients)owned.Add(client.Key);
        return clients;
    }
    private static void Require(bool condition,string reason) {if(!condition)throw new LiveBlocked(reason);}
    private sealed class LiveBlocked(string message):Exception(message);
    private static void Step(string id,string name,Func<string> action)
    {
        var clock=Stopwatch.StartNew(); string status="passed",evidence;
        try {evidence=action();}
        catch(LiveBlocked e){status="blocked";evidence=e.Message;}
        catch(Exception e){status="failed";evidence=e.GetType().Name;}
        results.Add(new(id,name,status,evidence,clock.Elapsed.TotalMilliseconds));
        Console.WriteLine($"{id} {status}: {evidence}"); Save();
    }
    private static void Save()
    {
        Directory.CreateDirectory(UserSimulation.Output);
        File.WriteAllText(Path.Combine(UserSimulation.Output,"live-results.json"),JsonSerializer.Serialize(new {
            schemaVersion=1,baselineClients=baseline.Count,ownedTestClients=owned.Count,outcomes=results,
            constraints=new[]{"Original clients excluded from close, arrangement and fullscreen targets.","No account sign-in or gameplay claims.","No raw titles, process identifiers or monitor identities persisted."}
        },Program.JsonOptions));
    }
    internal static int Run(string[] args)
    {
        if(args.Length!=0){Console.Error.WriteLine("Usage: tests --live-user-simulation");return 2;}
        Application.EnableVisualStyles();
        UiTestContext.Install();
        Directory.CreateDirectory(UserSimulation.Output);
        baseline=Snapshot().Instances.Select(i=>i.Key).ToHashSet();
        Console.WriteLine($"LIVE preflight: {baseline.Count} existing verified clients; elevated={Program.Elevated}");
        if(Program.Elevated) {Console.Error.WriteLine("Run live audit with the normal Windows token.");return 2;}
        try
        {
            Step("L01","Open installed dashboard",()=>{
                Require(File.Exists(Program.InstalledExe),"Installed helper unavailable");
                using var process=Process.Start(new ProcessStartInfo(Program.InstalledExe){UseShellExecute=false,ArgumentList={"--diagnostics"}})!;
                try {Wait(()=>{process.Refresh();return process.HasExited || process.MainWindowHandle!=0;},15000);
                    Require(!process.HasExited && process.MainWindowHandle!=0,"Installed dashboard did not create a visible main window");
                    Require(Snapshot().Instances.Count(i=>baseline.Contains(i.Key))==baseline.Count,"Existing client count changed during observation");
                    return "Dashboard opened without adding a player; existing clients preserved.";
                } finally {if(!process.HasExited){process.CloseMainWindow();process.WaitForExit(5000);}}
            });
            Step("L02","Launch additional client A",Launch);
            Step("L03","Launch additional client B",Launch);
            Step("L04","Discover and sample real clients",()=>{
                var collector=new InstanceCollector(new InstancePlatform(),Program.Sid,Process.GetCurrentProcess().SessionId);
                var first=collector.Collect(default);Thread.Sleep(250);var second=collector.Collect(default);
                Require(owned.Count>0,"No audit-owned client was launched");
                Require(second.Instances.Any(i=>owned.Contains(i.Key)),"Test client no longer discoverable");
                return $"Verified {second.Instances.Length} clients; {second.Instances.Count(i=>i.CpuPercent is not null)} CPU samples available. Account identities not inspected.";
            });
            Step("L05","Focus a newly launched client",()=>{
                var client=NewClients().FirstOrDefault(i=>i.Windows.Length>0);Require(client is not null,"No audit-owned window available");
                var result=new InstanceActions(new InstancePlatform(),Program.Sid,Process.GetCurrentProcess().SessionId).Execute(new(client!.Key,client.Windows[0].Handle),InstanceAction.Focus,default);
                Require(result==InstanceActionResult.Focused,"Windows denied foreground activation or target changed");
                return "Focus request accepted for test client; no original client targeted.";
            });
            Step("L06","Arrange newly launched clients through editor controls",()=>Arrange(false));
            Step("L07","Restore original test-client geometry",()=>{
                Require(placed,"No successful arrangement to restore; dependent step skipped");
                Require(original.Count>0,"No test-window geometry captured");
                var platform=new PlacementPlatform();
                var plan=new PlacementPlan(platform.Monitors(),original.Select((x,i)=>new WindowPlacement(x.Key,x.Value,i+1)).ToArray(),[]);
                var task=new PlacementCoordinator(platform).ApplyAsync(plan,default);UserSimulation.Pump(task);
                Require(task.Result.All(x=>x.Message=="Placed."),"One or more restore placements were unconfirmed");
                return "Placement coordinator restored captured test-window bounds; existing game windows excluded.";
            });
            Step("L08","Fullscreen entry and verified exit through Apply",()=>{
                var client=NewClients().FirstOrDefault(i=>i.Windows.Length>0);Require(client is not null,"No audit-owned window");
                var target=new InstanceTarget(client!.Key,client.Windows[0].Handle);
                using var manager=new Form{Text="Audit fullscreen preparation",Size=new(500,200)};manager.Show();manager.Activate();Application.DoEvents();
                var input=new NativeFullscreenInput();
                Require(input.Focus(target.Window),"Windows denied test-window focus");Thread.Sleep(200);
                if(!input.Fullscreen(target.Window)) {Require(input.Toggle(target.Window),"Windows denied fullscreen input");Wait(()=>input.Fullscreen(target.Window),4000);}
                Require(input.Fullscreen(target.Window),"Roblox did not enter fullscreen");
                string result=Arrange(true,target);
                Require(!input.Fullscreen(target.Window),"Fullscreen exit was not confirmed");manager.Close();return result;
            });
            Step("L09","Passive completion notification delivery",()=>{
                throw new LiveBlocked("Live notification delivery not verified: options remain unchanged and no passive popup observer is available. Reducer/settings coverage is synthetic.");
            });
            Step("L10","Close audit-owned clients gracefully",()=>{
                int requests=0;
                foreach(var client in NewClients()) foreach(var window in client.Windows) {
                    var result=new InstanceActions(new InstancePlatform(),Program.Sid,Process.GetCurrentProcess().SessionId).Execute(new(client.Key,window.Handle),InstanceAction.Close,default);
                    if(result==InstanceActionResult.CloseRequested)requests++;
                }
                Require(requests>0,"No test windows available to close");
                Wait(()=>NewClients().All(i=>i.Windows.Length==0),10000);
                Require(NewClients().All(i=>i.Windows.Length==0),"At least one test client did not close its window");
                // Roblox can start a fresh windowless lifetime as its visible client closes.
                var settle=Stopwatch.StartNew();while(settle.ElapsedMilliseconds<3000){_=NewClients();Thread.Sleep(100);}
                return $"Sent {requests} graceful close request(s); test windows closed. Process exit is checked separately.";
            });
            Step("L11","Clean up audit-owned background clients",()=>{
                var remaining=NewClients();
                if(remaining.Length==0)return "All audit-owned processes exited after graceful close; no force-stop needed.";
                Require(remaining.All(i=>i.Windows.Length==0),"A test client still has a window; no force-stop attempted");
                Wait(()=>NewClients().All(i=>BackgroundClientCleanup.OldEnough(i.Key,DateTime.UtcNow)),65000);
                var platform=new InstancePlatform();var cleanup=new BackgroundClientCleanup(pid=>new InstanceProcess(pid,true),platform.Trusted,Program.Sid,Process.GetCurrentProcess().SessionId);
                var outcomes=NewClients().Select(i=>cleanup.Execute(i.Key,default)).ToArray();
                Require(outcomes.All(x=>x==BackgroundCleanupResult.Stopped),"Some cleanup requests were skipped or unconfirmed");
                return $"Stopped {outcomes.Length} aged windowless audit-owned clients; existing clients excluded.";
            });
            Step("L12","Explicit tray lifetime and original-client preservation",()=>{
                var preferences=new LayoutPreferences(new LayoutStore(Path.Combine(UserSimulation.Output,"live-layout-fixture.json")));
                using(var host=new ManagerHost(false,new UserSimulation.Hotkeys(),preferences,@"Local\RLH-Audit-"+Guid.NewGuid().ToString("N"),enableBackgroundCleanup:false,ownsMessageLoop:false)) {
                    Require(!host.TrayActive,"Tray unexpectedly active before opt-in");host.EnableTray();Require(host.TrayActive,"Tray opt-in failed");
                    foreach(var form in Application.OpenForms.Cast<Form>().ToArray())form.Close();Application.DoEvents();Require(host.TrayActive,"Tray did not survive dashboard close");
                }
                Require(Snapshot().Instances.Count(i=>baseline.Contains(i.Key))==baseline.Count,"An original client exited during audit");
                return "Real tray lifetime survived dashboard close and disposed afterward; fixture hotkeys avoided global conflicts. All original process lifetimes remain.";
            });
        }
        finally {Save();}
        return results.Any(r=>r.Status=="failed")?1:0;
    }
    private static string Launch()
    {
        Require(File.Exists(Program.InstalledExe),"Installed helper unavailable");
        var before=Snapshot().Instances.Select(i=>i.Key).ToHashSet();
        using var process=Process.Start(new ProcessStartInfo(Program.InstalledExe){UseShellExecute=false,ArgumentList={"--launch-roblox"}})!;
        Wait(()=>Snapshot().Instances.Any(i=>!before.Contains(i.Key)),65000);
        var added=Snapshot().Instances.Where(i=>!before.Contains(i.Key) && !baseline.Contains(i.Key)).ToArray();
        foreach(var client in added)owned.Add(client.Key);
        Require(added.Length>0,"No newly launched verified Roblox client observed before deadline");
        Wait(()=>NewClients().Any(i=>i.Windows.Length>0),15000);
        return $"Observed {added.Length} new verified player process(es); sign-in and gameplay remain unverified.";
    }
    private static string Arrange(bool fullscreen,InstanceTarget? fullscreenTarget=null)
    {
        var allTargets=NewClients().SelectMany(c=>c.Windows.Select(w=>new InstanceTarget(c.Key,w.Handle)));
        var targets=(fullscreenTarget is InstanceTarget selected?allTargets.Where(t=>t==selected):fullscreen?allTargets.Take(1):allTargets).ToHashSet();
        Require(targets.Count>0,"No audit-owned windows available");
        var platform=new PlacementPlatform();
        foreach(var target in targets) {using var session=platform.Open(target);original.TryAdd(target,session.Read().NormalBounds);}
        var choices=new List<ArrangementChoice>();
        using var form=new ArrangementForm(choices,new LayoutPreferences(new LayoutStore(Path.Combine(UserSimulation.Output,"live-layout-fixture.json"))),ArrangementKind.Grid,null,null,platform,
            _=>new(NewClients().Select(c=>c with {Windows=c.Windows.Where(w=>targets.Contains(new(c.Key,w.Handle))).ToArray()}).Where(c=>c.Windows.Length>0).ToArray(),0));
        UiTestContext.Install();
        form.Opacity=1; form.ShowInTaskbar=true; form.Show();form.Activate();Application.DoEvents();
        UserSimulation.Wait(()=>choices.Count>0 && UserSimulation.Button(form,"&Preview").Enabled);
        // Drain Shown's automatic preview before injecting the next control action.
        var settle=Stopwatch.StartNew();while(settle.ElapsedMilliseconds<250){Application.DoEvents();Thread.Sleep(10);}
        UserSimulation.Wait(()=>UserSimulation.Button(form,"&Preview").Enabled);
        UiTestContext.Install();
        UserSimulation.Button(form,"&Preview").PerformClick();UserSimulation.Wait(()=>UserSimulation.Button(form,"&Preview").Enabled);
        if(!UserSimulation.Button(form,"&Apply").Enabled)
        {
            int movable=0,notResizable=0,busy=0;
            foreach(var target in targets){using var session=platform.Open(target);var read=session.Read();if(read.Movable)movable++;if(read.Reason=="Not resizable")notResizable++;if(read.Reason=="Window busy")busy++;}
            var windows=targets.Select(t=>{using var session=platform.Open(t);return session.Read();}).ToArray();
            string capacity=string.Join(", ",platform.Monitors().Select((m,i)=>$"display {i+1} grid capacity {LayoutPlanner.Capacity(m,windows,ArrangementKind.Grid)}"));
            var labels=UserSimulation.Descendants(form).OfType<Label>().Select(l=>l.Text).ToArray();
            string reason=new[]{"Preview unavailable","Displays changed","Check at least","Select at least","skipped:","Maximum fitting count","Preview the arrangement","Press Apply"}.FirstOrDefault(token=>labels.Any(l=>l.Contains(token)))??"unspecified";
            throw new LiveBlocked($"Preview blocked: {targets.Count} test windows; {choices.Count(c=>c.Selected)} checked; {movable} movable, {notResizable} nonresizable, {busy} busy; {capacity}; status category '{reason}'. No placement claim.");
        }
        UiTestContext.Install();
        UserSimulation.Button(form,"&Apply").PerformClick();
        UserSimulation.Wait(()=>UserSimulation.Button(form,"&Preview").Enabled);
        string status=string.Join(" ",UserSimulation.Descendants(form).OfType<Label>().Where(l=>l.Text.StartsWith("Slot ")).Select(l=>l.Text));
        Require(status.Contains("Placed.") && !status.Contains("not confirmed"),"Placement completed without confirmed result");
        placed=true;
        form.Close();return $"Actual Preview/Apply controls placed {targets.Count} test window(s)"+(fullscreen?" including fullscreen preparation.":".");
    }
    private static void Wait(Func<bool> ready,int milliseconds)
    {
        var clock=Stopwatch.StartNew();while(!ready() && clock.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(100);}
    }
}
