using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using RobloxLaunchHelper;
using Helper = RobloxLaunchHelper.Program;

namespace RobloxMultiInstance.Desktop;

internal static class LiveAudit
{
    internal static async Task<int> Run(string output)
    {
        var results = new List<AuditResult>();
        var elapsed = Stopwatch.StartNew();
        var owned = new List<(InstanceTarget Target, System.Drawing.Rectangle? Bounds)>();
        var baselineKeys = new HashSet<ProcessKey>();
        bool baselineKnown = false;
        var platform = new InstancePlatform();
        using var current = Process.GetCurrentProcess();
        var collector = new InstanceCollector(platform, Helper.Sid, current.SessionId);
        var actions = new InstanceActions(platform, Helper.Sid, current.SessionId);
        var placement = new PlacementPlatform();
        using var real = new DesktopBackend(Path.Combine(output, "preferences"));
        var window = new MainWindow(new ReadOnlyLiveBackend(real), Path.Combine(output, "webview-profile"));
        JsonElement Payload(object? value = null) => JsonSerializer.SerializeToElement(value ?? new { });
        void Save() => File.WriteAllText(Path.Combine(output,"live.json"), JsonSerializer.Serialize(results, AuditEntry.Json));
        void Record(string name,string outcome,string detail,string persona="live")
        { results.Add(new(name,persona,outcome,elapsed.Elapsed.TotalMilliseconds,detail));elapsed.Restart();Save(); }
        async Task<string> Checkpoint(string id,string message,string[] choices)
        {
            string response = Path.Combine(output,"response.json");
            if (File.Exists(response)) File.Delete(response);
            File.WriteAllText(Path.Combine(output,"checkpoint.json"),JsonSerializer.Serialize(new { id,message,choices },AuditEntry.Json));
            Console.WriteLine("CHECKPOINT " + id + ": " + message);
            var until = DateTime.UtcNow.AddMinutes(20);
            try
            {
                while (DateTime.UtcNow < until)
                {
                    if (File.Exists(response))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(File.ReadAllText(response));
                            if (doc.RootElement.GetProperty("id").GetString() == id)
                            {
                                string answer=doc.RootElement.GetProperty("answer").GetString() ?? "";
                                if (choices.Contains(answer)) return answer;
                            }
                        }
                        catch (IOException) { } catch (JsonException) { }
                    }
                    await Task.Delay(500);
                }
                return "timeout";
            }
            finally { File.Delete(Path.Combine(output,"checkpoint.json")); }
        }
        try
        {
            if (Helper.Elevated || !File.Exists(Helper.InstalledExe))
            { Record("Live prerequisites","blocked","Run as a normal user with the existing installed helper. No installation was attempted.");return 2; }
            string installed = FileVersionInfo.GetVersionInfo(Helper.InstalledExe).FileVersion ?? "unknown";
            Record("Installed helper identity","passed","Installed helper version: " + installed);
            var baseline = collector.Collect(default);
            if (!baseline.Available || baseline.Unavailable > 0)
            { Record("Existing-client baseline","blocked","Could not safely inspect all existing clients.");return 2; }
            baselineKeys = baseline.Instances.Select(x=>x.Key).ToHashSet();baselineKnown = true;
            Record("Existing-client baseline","passed",$"Preserving {baselineKeys.Count} existing process lifetimes.");
            window.Show();await AuditEntry.Wait(window,"document.querySelector('main')?.getAttribute('aria-busy')==='false'");
            Record("Live desktop startup","passed","Current host loaded with isolated desktop preferences and read-only UI operations.","basic");
            if (await Checkpoint("ready","Ready to launch up to two test clients? Avoid other Roblox launches during this phase.",["ready","skip"]) != "ready")
            { Record("Live client phase","skipped","User did not start the client phase.");return 2; }
            for(int i=0;i<2;i++)
            {
                var before=collector.Collect(default);
                if(!before.Available || before.Unavailable>0) { Record("Launch preflight","blocked","Client enumeration became unavailable.");break; }
                var known=before.Instances.Select(x=>x.Key).ToHashSet();
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                try
                {
                    var launched = JsonSerializer.SerializeToElement(await real.ExecuteAsync("launch",Payload(),deadline.Token),AuditEntry.Json);
                    if(launched.GetProperty("exitCode").GetInt32()!=0)
                    { Record($"Test launch {i+1}","failed","Installed helper returned a nonzero launch exit code. No client was claimed.");break; }
                    Record($"Test launch {i+1}","passed","Installed helper returned success; client ownership and gameplay are checked separately.");
                }
                catch { Record($"Test launch {i+1}","failed","Helper launch did not complete. No client was claimed.");break; }
                ActiveInstance[] candidates=[];
                var timer=Stopwatch.StartNew();
                while(timer.Elapsed<TimeSpan.FromSeconds(45))
                {
                    var snapshot=collector.Collect(default);
                    if(!snapshot.Available || snapshot.Unavailable>0)break;
                    candidates=snapshot.Instances.Where(x=>!known.Contains(x.Key)).ToArray();
                    if(candidates.Length>1 || candidates.Length==1 && candidates[0].Windows.Length==1)break;
                    await Task.Delay(500);
                }
                if(candidates.Length!=1 || candidates[0].Windows.Length!=1)
                { Record($"Test client {i+1} ownership","blocked","New client identity was missing or ambiguous. No candidate was controlled or claimed.");break; }
                if(await Checkpoint($"identify-{i+1}",$"Exactly one new verified client appeared for launch {i+1}. Confirm the newly opened Roblox window is the intended test client.",["confirm","skip"])!="confirm")
                { Record($"Test client {i+1} ownership","blocked","User did not positively identify the new client. It was left untouched.");break; }
                var identified=ConfirmOwned(candidates,known,true);
                if(identified is not InstanceTarget target) { Record("Ownership revalidation","blocked","Candidate identity could not be established.");break; }
                if(baselineKeys.Contains(target.Key))throw new InvalidOperationException("Baseline ownership violation");
                owned.Add((target,null));
                Record($"Test client {i+1} ownership","manual confirmation","User positively identified the new client.");
                using(var session=placement.Open(target))
                {
                    if(!session.Valid || !session.Normal) { Record("Client window state","blocked","The identified window was not in a normal movable state.");break; }
                    owned[^1]=(target,session.Read().NormalBounds);
                }
                var focused=actions.Execute(target,InstanceAction.Focus,default);
                Record($"Test client {i+1} focus",focused==InstanceActionResult.Focused?"passed":"blocked","Production identity-checked focus result: "+focused,"basic");
                await Task.Delay(1200);var metrics=collector.Collect(default).Instances.FirstOrDefault(x=>x.Key==target.Key);
                Record($"Test client {i+1} metrics",metrics?.WorkingSet is not null && metrics.CpuAvailable?"passed":"blocked","Checked resource availability without retaining account labels or process identifiers.");
            }
            if(owned.Count==2 && owned.All(x=>x.Bounds is not null))
            {
                var targets=owned.Select(x=>x.Target).ToArray();
                var monitors=placement.Monitors();var windows=targets.Select(target=>{using var session=placement.Open(target);return session.Read();}).ToArray();
                var plan=LayoutPlanner.Arrange(monitors,windows,targets.Select(_=>monitors[0].Id).ToArray(),ArrangementKind.SideBySide);
                if(plan.Valid)
                {
                    var applied=await new PlacementCoordinator(placement).ApplyAsync(plan,default);
                    Record("Live placement",applied.Length==2 && applied.All(x=>x.Message=="Placed.")?"passed":"failed","Applied production placement checks to the two identified test windows.","power");
                }
                else Record("Live placement","blocked","Current window sizes or display capacity did not permit the plan.");
                await AuditEntry.Click(window,"Instances");
                await AuditEntry.Wait(window,"document.querySelector('main')?.getAttribute('aria-busy')==='false'");
                Record("Live desktop responsiveness","passed","Desktop remained responsive after placement.");
                string gameplay=await Checkpoint("gameplay","Sign in with two test accounts and join a game in each client. Do both remain in-game together?",["both-in-game","not-working","skip"]);
                Record("Two-account gameplay",gameplay=="both-in-game"?"manual confirmation":gameplay=="not-working"?"failed":"skipped",gameplay=="both-in-game"?"User confirmed two accounts remained in-game; automation did not inspect authentication or gameplay.":"Two-account gameplay was not confirmed.");
            }
            else Record("Two-account gameplay","blocked","Two positively identified clients were not available.");
        }
        catch(Exception ex) { Record("Live execution","failed","Live phase stopped: "+ex.GetType().Name); }
        finally
        {
            foreach(var item in owned)
            {
                try
                {
                    using var session=placement.Open(item.Target);
                    if(item.Bounds is System.Drawing.Rectangle original && session.Valid && session.Normal)
                    {
                        bool moved=session.Move(original),matched=false;
                        for(int check=0;moved&&check<5;check++)
                        {
                            await Task.Delay(200);
                            if(!session.Valid)break;
                            if(session.Bounds()==original){matched=true;break;}
                        }
                        Record("Restore test-window position",matched?"passed":"blocked","Attempted identity-checked restoration of original geometry.");
                    }
                    else Record("Restore test-window position","blocked","Original process/window identity was unavailable.");
                }
                catch { Record("Restore test-window position","blocked","Restoration was unavailable; graceful closure is checked independently."); }
                try
                {
                    var close=actions.Execute(item.Target,InstanceAction.Close,default);
                    Record("Graceful test-window close",close==InstanceActionResult.CloseRequested?"passed":"blocked","Production close result: "+close+". No force stop requested.");
                }
                catch { Record("Graceful test-window close","blocked","Graceful closure was unavailable. No force stop requested."); }
            }
            await Task.Delay(1500);
            var final=collector.Collect(default);
            if(final.Available)
            {
                int remaining=final.Instances.Count(x=>owned.Any(o=>o.Target.Key==x.Key));
                Record("Remaining test clients",remaining==0?"passed":"blocked",$"{remaining} identified test clients remain; inspect Roblox manually if needed.");
                if(baselineKnown)
                {
                    int unclaimed=final.Instances.Count(x=>!baselineKeys.Contains(x.Key) && owned.All(o=>o.Target.Key!=x.Key));
                    if(unclaimed>0)Record("Unclaimed new clients","blocked",$"{unclaimed} new clients could not be claimed safely and were left untouched. Inspect Roblox manually.");
                }
            }
            else Record("Remaining test clients","blocked","Final client enumeration was unavailable.");
            window.AuditExit();Save();
        }
        return results.Any(x=>x.Outcome=="failed")?1:results.Any(x=>x.Outcome=="blocked"||x.Outcome=="skipped")?2:0;
    }

    // A discovery delta alone is never ownership. Confirmation is mandatory even for one candidate.
    internal static InstanceTarget? ConfirmOwned(ActiveInstance[] candidates,HashSet<ProcessKey> known,bool confirmed)
    {
        if(!confirmed || candidates.Length!=1 || candidates[0].Windows.Length!=1 || known.Contains(candidates[0].Key))return null;
        return new(candidates[0].Key,candidates[0].Windows[0].Handle);
    }

    private sealed class ReadOnlyLiveBackend(DesktopBackend backend) : IDesktopBackend
    {
        public string AppearanceScheme=>backend.AppearanceScheme;
        public event Action<string,string,object?>? Event { add { } remove { } }
        public Task<object?> ExecuteAsync(string command,JsonElement payload,CancellationToken token)
        {
            if(command is not ("app.state" or "settings.get" or "instances.list" or "layouts.get" or "activity.list" or "system.health" or "background.status"))
                throw new DesktopOperationException("audit_read_only","Live audit controls this action through explicit checkpoints.");
            return backend.ExecuteAsync(command,payload,token);
        }
        public void Dispose(){ }
    }
}
