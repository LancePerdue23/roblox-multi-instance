using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RobloxLaunchHelper;
using CheckState = RobloxLaunchHelper.CheckState;
using LayoutSettings = RobloxLaunchHelper.LayoutSettings;

// Audit fixtures never launch a process, send real window input, or use default preference paths.
internal static class UserSimulation
{
    internal static readonly string Output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.build/user-simulation"));
    internal static readonly DateTimeOffset Now = new(2026, 10, 29, 16, 0, 0, TimeSpan.Zero);
    internal static readonly long Created = Now.AddMinutes(-5).UtcDateTime.ToFileTimeUtc();
    internal sealed record Persona(string Id, string User, string Goal, string Success);
    internal sealed record Journey(int Id, string Name, string Steps, string Success, string Layer);
    internal sealed record Result(int Session, int Seed, int Journey, string Persona, int Clients, int EffectiveFixtureClients, int TimingMs,
        bool Degraded, string Layer, string Outcome, double TechnicalMs, int InvariantChecks, int? ExecutedActions, int? Backtracking,
        int? UnexpectedDialogs, string Recovery, string[] Observations, string? Error);
    internal static readonly Persona[] Personas = [
        new("P01", "First-time mouse user, one monitor", "Open Roblox without learning internals", "Find launch and understand the result"),
        new("P02", "Occasional returning beginner", "Resume after a Roblox update", "Find a useful recovery action"),
        new("P03", "Cautious beginner with two sessions", "Close one client safely", "Keep the other session unchanged"),
        new("P04", "Daily two-client player", "Switch between identical game titles", "Focus the intended client"),
        new("P05", "Small laptop, high scaling", "Arrange two games", "Reach controls and understand fitting limits"),
        new("P06", "Keyboard-first everyday player", "Launch and manage without a mouse", "Reach actions with tab/mnemonics"),
        new("P07", "Heavy user, eight clients", "Repeat a saved arrangement", "Preserve explicit slot ordering"),
        new("P08", "Heavy user, multiple monitors", "Recover after unplugging a display", "Preview and recover reachable windows"),
        new("P09", "Heavy user, long sessions", "Use tray shortcuts and clean leftovers", "Keep windowed games running"),
        new("P10", "Nontechnical troubleshooting user", "Understand unconfirmed completion", "Distinguish helper status from gameplay"),
        new("P11", "Support helper, privacy conscious", "Export useful evidence", "Exclude private identifiers and raw logs"),
        new("P12", "Technical troubleshooter, offline", "Recover from unavailable settings or update service", "Receive bounded actionable failure and retry")
    ];
    internal static readonly Journey[] Journeys = [
        new(1,"First run", "Open downloaded helper before installation", "Route to explicit setup", "workflow"),
        new(2,"Returning user", "Open installed helper", "Open dashboard without launching", "workflow"),
        new(3,"App launch", "Build desktop launch request", "Pass -app without shell", "workflow"),
        new(4,"Browser launch", "Forward synthetic protocol payload", "Preserve original argument exactly", "workflow"),
        new(5,"Overlapping launches", "Start two observations; introduce new clients", "Claim distinct process lifetimes", "workflow"),
        new(6,"No new client", "Start launch; advance deadline", "Finish without false confirmation", "workflow"),
        new(7,"Repeated launch click", "Click actual Launch button twice while pending; close form", "One request; close cancels wait only", "controls"),
        new(8,"Launch startup failure", "Click actual Launch button with injected failure", "Display failure and enable retry", "controls"),
        new(9,"Find active clients", "Collect synthetic official clients", "Correct verified client count", "workflow"),
        new(10,"Select duplicate titles", "Select list row; refresh same clients", "Preserve exact lifetime selection", "controls"),
        new(11,"Focus a client", "Select verified target; request focus", "Respect foreground denial", "workflow"),
        new(12,"Graceful close", "Request close through action coordinator", "Report request without claiming process exit", "workflow"),
        new(13,"Exited selection", "Replace process lifetime before focus", "Never act on replacement", "workflow"),
        new(14,"Unavailable discovery", "Fail enumeration; recover", "Report failed snapshot then fresh data", "workflow"),
        new(15,"Background cleanup", "Check windowless aged client; execute cleanup", "Only eligible verified clients stop", "workflow"),
        new(16,"Slow startup cleanup", "Observe aged windowless client continuously for 60 seconds", "Document startup/leftover ambiguity", "workflow"),
        new(17,"Cleanup grace reset", "Observe background client; interrupt observation", "Restart grace after visibility or gap", "workflow"),
        new(18,"Side-by-side", "Plan ordered checked windows", "Fit all slots or explain capacity", "workflow"),
        new(19,"Grid", "Plan ordered checked windows", "Keep targets and bounds correct", "workflow"),
        new(20,"Display capacity", "Plan two clients on small high-DPI display", "Block impossible minimum size", "workflow"),
        new(21,"Preset order", "Save ordered geometry; restore reversed targets", "Assign by explicit order, never account", "workflow"),
        new(22,"Missing preset display", "Restore slots from disconnected monitor", "Explain primary-monitor fallback", "workflow"),
        new(23,"Preset count mismatch", "Restore wrong number of clients", "Block without moving anything", "workflow"),
        new(24,"Off-screen recovery", "Preview disconnected-window recovery", "Recover only off-screen title bars", "workflow"),
        new(25,"Reorder and cancel", "Wait for automatic preview and Move up; cancel actual editor", "Invalidate preview; no native move", "controls"),
        new(26,"Fullscreen preparation", "Prepare fullscreen fixture with focus checks", "One verified F11; focus restored", "workflow"),
        new(27,"Fullscreen interruption", "Switch to unrelated foreground before preparation", "No shortcut or move", "workflow"),
        new(28,"Display topology change", "Change monitors after preview", "Reject before restoring/moving", "workflow"),
        new(29,"Tray shortcut conflict", "Register fixture shortcuts with one conflict", "Report conflict and release registrations", "workflow"),
        new(30,"Shortcut validation", "Validate modified and reserved keys", "Reject unsupported and duplicate bindings", "workflow"),
        new(31,"Notification preferences", "Save local opt-in; reopen settings", "Defaults off; explicit selection persists", "workflow"),
        new(32,"Notification grouping", "Finish overlapping attempts; advance virtual clock", "Group terminal outcomes after two seconds", "workflow"),
        new(33,"Dismiss progress", "Show delayed progress; dismiss; finish attempt", "Dismissal never cancels launch", "workflow"),
        new(34,"Launch result clarity", "Present starting/confirmed/warning/unknown states", "Do not equate helper status to gameplay", "workflow"),
        new(35,"Diagnostic next steps", "Present synthetic health checks", "Unknown stays unknown; next steps present", "presentation"),
        new(36,"Report privacy", "Build report from synthetic snapshot/events", "Typed export excludes private canary", "workflow"),
        new(37,"History corruption", "Open corrupt local history; explicitly clear", "Fail safely; clear recovers", "workflow"),
        new(38,"Update metadata", "Parse synthetic release with valid/invalid digest", "Offer only verified downloadable metadata", "workflow"),
        new(39,"Recovery selection", "Discover multiple fixture players; select or cancel", "No implicit choice or registry edits", "workflow"),
        new(40,"Unavailable settings", "Load unsupported fixture layout/notification schemas", "Preserve bytes until explicit reset", "workflow"),
        .. WorkspaceTests.Scenarios.Select((name,index)=>new Journey(41+index,name,"Drive embedded workspace controls","No unintended window or operation","controls"))
    ];
    internal sealed class Session(int seed, int count, int timing, bool degraded, string directory)
    {
        internal readonly Random Random = new(seed);
        internal int Actions;
        internal int? InputActions;
        internal readonly List<string> Notes = [];
        internal readonly Fixture Platform = new(count);
        internal int Count => count;
        internal int Timing => timing;
        internal bool Degraded => degraded;
        internal string File(string name) => Path.Combine(directory, name);
        internal void Check(bool condition, string invariant) { Actions++; if (!condition) throw new InvalidOperationException(invariant); }
        internal LayoutWindow[] Windows => Platform.Windows.Values.ToArray();
    }
    internal static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        UiTestContext.Install();
        int seed = 20261029;
        if (args.Length != 0 && !(args.Length == 2 && args[0] == "--seed" && int.TryParse(args[1], out seed)))
        { Console.Error.WriteLine("Usage: tests --user-simulation [--seed integer]"); return 2; }
        Directory.CreateDirectory(Output);
        string runDirectory = Path.Combine(Output, "seed-" + seed, "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        var results = new List<Result>();
        var random = new Random(seed);
        int[] counts = [0,1,2,8,20,50];
        for (int i = 0; i < 1000; i++)
        {
            UiTestContext.Install();
            var journey = Journeys[i % Journeys.Length];
            int sessionSeed = random.Next();
            int count = counts[(i / 40 + journey.Id) % counts.Length];
            int timing = new[] { 0, 50, 749, 750, 2000, 60001 }[(i / 40 + journey.Id / 2) % 6];
            bool degraded = (i / 40 + journey.Id) % 2 == 0;
            string directory = Path.Combine(runDirectory, "session-" + (i + 1));
            Directory.CreateDirectory(directory);
            var session = new Session(sessionSeed, count, timing, degraded, directory);
            var timer = Stopwatch.StartNew();
            string? error = null;
            try { if (journey.Id > 40) { WorkspaceTests.Exercise(journey.Id-41); session.Check(true,"Workspace control journey completed"); } else Exercise(journey.Id, session); }
            catch (Exception e) { error = e is InvalidOperationException or TimeoutException ? e.Message : e.GetType().Name; }
            if(error is not null) Console.WriteLine($"FAILED J{journey.Id}: {error}");
            results.Add(new(i+1, sessionSeed, journey.Id, Personas[(i / 40 + i % 40) % 12].Id,
                count, session.Platform.Windows.Count, timing, degraded, journey.Layer, error is null ? "passed" : "failed", timer.Elapsed.TotalMilliseconds,
                session.Actions, session.InputActions, null, null,
                error is null ? "Expected invariant satisfied; see observations" : "Not established", session.Notes.ToArray(), error));
            if ((i+1)%100 == 0) Console.WriteLine($"SIMULATION {i+1}/1000; failures={results.Count(r => r.Outcome == "failed")}");
        }
        var renders = RenderAll();
        WorkspaceTests.RenderAll();
        File.WriteAllText(Path.Combine(Output,"results.json"), JsonSerializer.Serialize(new {
            schemaVersion=1, seed, sessions=results, personas=Personas, journeys=Journeys, renders,
            workspaceRenders=Directory.GetFiles(Path.Combine(Output,"workspace-renders"),"*.png").Where(path=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),@"-(0|100|150|200)\.png$")).Select(Path.GetFileName).ToArray(),
            limitations=new[] {"Persona assignment is coverage bookkeeping, not measured human behavior.",
                "Virtual timing values affect timed reducers; they are not physical delays or user task durations.",
                "Workflow sessions exercise bounded journey segments, not full end-to-end installation/gameplay.",
                "Backtracking and satisfaction are unmeasured. Controls are programmatically driven, not human input.",
                "Scale() screenshots approximate layout stress; they do not establish real per-monitor DPI or screen reader behavior."}
        }, Program.JsonOptions));
        var matrix = new StringBuilder("# Scenario coverage\n\nEvery journey runs 20 times. Counts and degradation vary within each journey; combinations are not exhaustive.\n\n| ID | Journey | Steps | Acceptance | Layer | Passed / run |\n|---|---|---|---|---|---|\n");
        foreach (var j in Journeys) matrix.AppendLine($"| J{j.Id:00} | {j.Name} | {j.Steps} | {j.Success} | {j.Layer} | {results.Count(r=>r.Journey==j.Id && r.Outcome=="passed")} / 20 |");
        matrix.AppendLine("\n## Personas\n\n| ID | User | Goal | Success |\n|---|---|---|---|");
        foreach(var p in Personas) matrix.AppendLine($"| {p.Id} | {p.User} | {p.Goal} | {p.Success} |");
        File.WriteAllText(Path.Combine(Output,"scenario-matrix.md"), matrix.ToString());
        Console.WriteLine($"AUDIT complete: {results.Count(r=>r.Outcome=="passed")}/1000 passed; {renders.Count} focused forms plus the workspace theme/size matrix.");
        return results.Any(r=>r.Outcome=="failed") ? 1 : 0;
    }
    internal static void Exercise(int id, Session s)
    {
        var p = s.Platform;
        var windows = s.Windows;
        var monitors = p.Monitors();
        switch(id)
        {
            case 1: s.Check(Program.SelectStartup([], false)==Program.StartupMode.Setup,"First use must route to setup"); break;
            case 2: s.Check(Program.SelectStartup([], true)==Program.StartupMode.Dashboard,"Returning open must not launch game"); break;
            case 3:
                var app=Launch.BuildStart("\"C:\\Fixture\\RobloxPlayerBeta.exe\" \"%1\"",null);
                s.Check(!app.UseShellExecute && app.ArgumentList.SequenceEqual(new[]{"-app"}),"Desktop argument preservation"); break;
            case 4:
                string uri="roblox-player:1+synthetic:"+s.Random.Next()+"\" & $(fixture)";
                var launch=Launch.BuildStart("\"C:\\Fixture\\RobloxPlayerBeta.exe\" \"%1\"",uri);
                s.Check(!launch.UseShellExecute && launch.ArgumentList.Single()==uri,"Browser URI must remain one exact argument"); break;
            case 5:
                var original=p.Windows.Keys.Select(t=>t.Key).ToHashSet();
                var a=new PendingLaunch(original){Started=true,Deadline=100000};
                var b=new PendingLaunch(original){Started=true,Deadline=100000};
                var claims=new HashSet<ProcessKey>();
                var first=new ProcessKey(1001,Created); var second=new ProcessKey(1002,Created);
                a.Update(original.Append(first).ToHashSet(),[first],claims,s.Timing);
                b.Update(original.Append(first).ToHashSet(),[first],claims,s.Timing);
                s.Check(a.Done.Task.IsCompletedSuccessfully && !b.Done.Task.IsCompleted,"One new lifetime cannot satisfy two launches");
                b.Update(original.Concat([first,second]).ToHashSet(),[first,second],claims,s.Timing+1);
                s.Check(b.Done.Task.IsCompletedSuccessfully && claims.Count==2,"Second distinct process confirms second launch"); break;
            case 6:
                var pending=new PendingLaunch([]){Started=true,Deadline=10};
                pending.Update([],[],[],11);
                s.Check(pending.Done.Task.IsCompleted && pending.Done.Task.Result!="OK","Timeout must not report confirmation"); break;
            case 7: case 8: LaunchControls(s,id==8); break;
            case 9: case 14:
                p.FailEnumeration=id==14;
                var collector=new InstanceCollector(p,Program.Sid,Process.GetCurrentProcess().SessionId);
                var collected=collector.Collect(default);
                s.Check(id==14 ? !collected.Available : collected.Instances.Length==s.Count,"Discovery count and availability");
                p.FailEnumeration=false;
                s.Check(collector.Collect(default).Instances.Length==s.Count,"Discovery recovers"); break;
            case 10: SelectionControls(s); break;
            case 11: case 12: case 13:
                p.EnsureOne(); var target=p.Windows.Keys.First();
                p.DenyFocus=s.Degraded; p.Replaced=id==13;
                var result=new InstanceActions(p,Program.Sid,Process.GetCurrentProcess().SessionId).Execute(target,id==12?InstanceAction.Close:InstanceAction.Focus,default);
                s.Check(result==(id==13?InstanceActionResult.Changed:id==12?InstanceActionResult.CloseRequested:s.Degraded?InstanceActionResult.FocusDenied:InstanceActionResult.Focused),"Action must match current lifetime and focus permission");
                s.Check(id!=13 || p.FocusCalls==0,"Replacement never receives focus"); break;
            case 15:
                p.EnsureOne(); p.Windowless=true;
                var clean=new BackgroundClientCleanup(pid=>new Fixture.Client(p,pid),_=>true,Program.Sid,Process.GetCurrentProcess().SessionId,()=>Now.UtcDateTime);
                s.Check(clean.Execute(p.Windows.Keys.First().Key,default)==BackgroundCleanupResult.Stopped && p.Stops==1,"Eligible fixture cleanup"); break;
            case 16: case 17:
                var tracker=new BackgroundCleanupTracker();
                var key=new ProcessKey(1,Created);
                var background=new InstanceSnapshot([new(key,[],null,null,false)],0);
                s.Check(tracker.Observe(background,0,Now.UtcDateTime).Length==0,"Grace starts at observation");
                foreach(int at in new[]{5,10,15,20,25,30,35,40,45,50,55}) tracker.Observe(background,at,Now.UtcDateTime);
                if(id==17) { tracker.Observe(InstanceSnapshot.Failed,56,Now.UtcDateTime); s.Check(tracker.Observe(background,60,Now.UtcDateTime).Length==0,"Failure resets grace"); }
                else { s.Check(tracker.Observe(background,60,Now.UtcDateTime).Length==1,"Continuous startup becomes eligible after a minute"); s.Notes.Add("F01: slow startup and abandoned client are indistinguishable to automatic cleanup"); }
                break;
            case 18: case 19:
                var plan=LayoutPlanner.Arrange(monitors,windows,windows.Select(_=>monitors[0].Id).ToArray(),id==18?ArrangementKind.SideBySide:ArrangementKind.Grid);
                if(windows.Length==0) s.Check(!plan.Valid,"No empty apply");
                else if(plan.Valid) s.Check(plan.Placements.Length==windows.Length && plan.Placements.All(x=>monitors[0].Work.Contains(x.Bounds)),"All targets fit");
                else s.Check(plan.Error!.Contains("Maximum fitting count"),"Capacity failure must explain next step"); break;
            case 20:
                p.EnsureOne(); var small=new LayoutMonitor("small","Small",new(0,0,1280,720),192,true);
                var smallPlan=LayoutPlanner.Arrange([small],s.Windows,s.Windows.Select(_=>"small").ToArray(),ArrangementKind.Grid);
                s.Check(!smallPlan.Valid && smallPlan.Error!.Contains("Maximum fitting count"),"High scaling must retain minimum client area"); break;
            case 21: case 22: case 23:
                p.EnsureOne(); windows=s.Windows;
                var slots=windows.Select(w=>LayoutPlanner.Save(w,monitors)).ToArray();
                if(id==22) slots=slots.Select(x=>x with{Monitor="disconnected"}).ToArray();
                if(id==23) slots=slots.Append(slots[0]).ToArray();
                var preset=new WindowPreset("Fixture",slots);
                var restore=LayoutPlanner.Restore(monitors,windows.Reverse().ToArray(),preset);
                if(id==23) s.Check(!restore.Valid && restore.Error!.Contains("Check exactly"),"Preset count mismatch explained");
                else if(id==22) s.Check(restore.Valid ? restore.Notes.Length>0 : restore.Error is not null,"Fallback or capacity explained");
                else { s.Check(restore.Valid && restore.Placements[0].Target==windows.Last().Target,"Preset maps current explicit order"); s.Notes.Add("F04: presets preserve slots, not client/account identities"); }
                break;
            case 24:
                p.EnsureOne(); windows=s.Windows; var off=windows[0] with{NormalBounds=new(-10000,-10000,800,500)};
                var recovery=LayoutPlanner.Recover(monitors,[off]);
                s.Check(recovery.Valid && monitors[0].Work.Contains(recovery.Placements[0].Bounds),"Off-screen target recovered into work area"); break;
            case 25: ArrangementControls(s); break;
            case 26: case 27:
                var input=new FullscreenFixture{Foreground=id==27?(nint)99:10,Denied=s.Degraded};
                var preparation=FullscreenPreparation.Request(20,()=>true,input,default,_=>{});
                s.Check(preparation==(id==27?WindowedResult.Interrupted:s.Degraded?WindowedResult.FocusDenied:WindowedResult.Ready),"Fullscreen permission/interruption outcome");
                s.Check(input.Toggles==(id==27 || s.Degraded?0:1),"Only verified focus receives F11"); break;
            case 28:
                p.EnsureOne(); var before=LayoutPlanner.Arrange(monitors,s.Windows,s.Windows.Select(_=>monitors[0].Id).ToArray(),ArrangementKind.Grid);
                p.ChangedDisplays=true;
                if(before.Valid) { bool rejected=false;try { Pump(new PlacementCoordinator(p).ApplyAsync(before,default)); } catch(InvalidOperationException) { rejected=true; } s.Check(rejected && p.Moves==0,"No moves after topology change"); }
                else s.Check(before.Error is not null,"Capacity prevented apply"); break;
            case 29:
                var hotkeys=new Hotkeys{Conflict=s.Degraded}; using(var registrations=new HotkeyRegistrations(hotkeys))
                { var conflicts=registrations.Set(LayoutSettings.Default.Shortcuts,true); s.Check(conflicts.Length==(s.Degraded?1:0),"Conflicts explained"); }
                s.Check(hotkeys.Held.Count==0,"Disposed keys are unregistered"); break;
            case 30:
                s.Check(ShortcutPolicy.Valid(Keys.Control|Keys.Alt|Keys.D1) && !ShortcutPolicy.Valid(Keys.F12) && !ShortcutPolicy.Valid(Keys.A),"Shortcut validation");
                var defaults=LayoutSettings.Default; defaults.Shortcuts["Manager"]=defaults.Shortcuts["Grid"];
                try { LayoutStore.Validate(defaults); s.Check(false,"Duplicate bindings rejected"); } catch(InvalidDataException) { s.Check(true,"Duplicates rejected"); } break;
            case 31:
                var preferences=new NotificationPreferences(s.File("notifications.json"));
                s.Check(!preferences.Settings.Progress && !preferences.Settings.Completion,"Notifications initially off");
                preferences.Save(new(1,true,s.Degraded));
                s.Check(new NotificationPreferences(s.File("notifications.json")).Settings==new NotificationSettings(1,true,s.Degraded),"Preference round trip"); break;
            case 32: case 33:
                long clock=0; var state=new LaunchNotificationState(()=>clock); var settings=new NotificationSettings(1,true,true);
                var status=new LaunchStatus(DeterministicGuid(s.Random),1,1,Created,LaunchOutcome.InProgress,DiagnosticCode.LaunchStarting,false);
                s.Check(state.Accept(status,settings),"Accept initial state"); clock=s.Timing;
                s.Check(state.ProgressReady==(clock>=750),"Progress delay boundary");
                if(id==33) { state.Dismiss(); s.Check(!state.ProgressReady && state.Active==1,"Dismiss doesn't cancel launch"); }
                s.Check(state.Accept(status with{Revision=2,Outcome=LaunchOutcome.Confirmed,Code=DiagnosticCode.LaunchConfirmed,RobloxStarted=true},settings),"Accept terminal state");
                clock+=2000; s.Check(state.TakePopup(settings) is not null && state.Active==0,"Terminal summary drained"); break;
            case 34:
                s.Check(LaunchStatusText.Text(LaunchOutcome.Warning,DiagnosticCode.WorkerTimeout,true).Contains("unconfirmed") && LaunchStatusText.Text(LaunchOutcome.Incomplete,DiagnosticCode.Interrupted,null).Contains("unknown"),"Unconfirmed and unknown distinct");
                s.Check(LaunchStatusText.Limit.Contains("does not verify"),"Gameplay caveat exists"); s.Notes.Add("F06: helper confirmation wording requires technical knowledge"); break;
            case 35:
                using(var form=new DiagnosticsForm(false) { ShowInTaskbar=false, Opacity=0 }) { form.Present(Health(s.Degraded),[],true); form.Show();
                    s.Check(Descendants(form).OfType<ListView>().Single(l=>l.AccessibleName == "Health checks").Items.Count>0,"Health rows presented");
                    s.Check(Health(true).Overall==CheckState.Unknown,"Unknown inspection isn't healthy"); form.Close(); }
                break;
            case 36:
                var package=DiagnosticReports.Build(Health(false),[],true,[]);
                var text=Encoding.UTF8.GetString(package.Json);
                s.Check(!text.Contains("synthetic-private") && !text.Contains("fixture-display") && text.Length>0,"No fixture identity in typed export"); break;
            case 37:
                Directory.CreateDirectory(s.File("history")); File.WriteAllText(s.File("history/history.json"),"corrupt");
                var history=new DiagnosticHistory(s.File("history"),()=>Now,_=>false);
                s.Check(!history.Read(out _),"Corrupt history unavailable");
                s.Check(history.Clear() && history.Read(out var records) && records.Length==0,"Explicit clear recovers"); break;
            case 38:
                var metadata=JsonSerializer.SerializeToUtf8Bytes(new{draft=false,prerelease=false,tag_name="v1.2.0",html_url="https://github.com/LancePerdue23/roblox-multi-instance/releases/tag/v1.2.0",assets=new[]{new{name="RobloxLaunchHelper.exe",size=100,digest=s.Degraded?"invalid":"sha256:"+new string('a',64),browser_download_url="https://github.com/LancePerdue23/roblox-multi-instance/releases/download/v1.2.0/RobloxLaunchHelper.exe"}}});
                var release=GitHubReleaseSource.Parse(metadata)!;
                s.Check(s.Degraded?release.Asset is null && release.DownloadProblem is not null:release.Asset is not null,"Download digest requirement"); break;
            case 39:
                if(s.Degraded) { try { PlayerRecovery.Resolve([],()=>[@"C:\Fixture\A\RobloxPlayerBeta.exe",@"C:\Fixture\B\RobloxPlayerBeta.exe"],_=>throw new OperationCanceledException(),_=>true,_=>true); s.Check(false,"Canceled selection must stop"); } catch(OperationCanceledException) { s.Check(true,"Selection canceled"); } }
                else { bool selected=false; var command=PlayerRecovery.Resolve([],()=>[@"C:\Fixture\A\RobloxPlayerBeta.exe",@"C:\Fixture\B\RobloxPlayerBeta.exe"],choices=>{selected=true;return choices[1];},_=>true,_=>true); s.Check(selected && command.Contains("\\B\\"),"Explicit player selected"); } break;
            case 40:
                const string corrupt="{\"Version\":999}";
                File.WriteAllText(s.File("layout.json"),corrupt); File.WriteAllText(s.File("notifications.json"),corrupt);
                var layouts=new LayoutPreferences(new LayoutStore(s.File("layout.json")));
                var notices=new NotificationPreferences(s.File("notifications.json"));
                s.Check(!layouts.Available && !notices.Available,"Unsupported settings unavailable");
                s.Check(File.ReadAllText(s.File("layout.json"))==corrupt && File.ReadAllText(s.File("notifications.json"))==corrupt,"Invalid bytes preserved");
                layouts.Reset(); notices.Save(new(),reset:true); s.Check(layouts.Available && notices.Available,"Explicit reset recovers"); break;
        }
    }
    internal static Guid DeterministicGuid(Random random) { byte[] bytes=new byte[16]; random.NextBytes(bytes); return new(bytes); }
    internal static HealthSnapshot Health(bool degraded) => new(1,"1.1.0","10.0","X64",Now,
        [new("installation",degraded?CheckState.Unknown:CheckState.Healthy,degraded?DiagnosticCode.InspectionUnavailable:DiagnosticCode.Ready,Now)],DiagnosticChecks.NotTested,BrowserIntegrationEnabled:true);
    internal static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Descendants(c)));
    internal static Button Button(Form form,string text)=>Descendants(form).OfType<Button>().Single(b=>b.Text==text);
    internal static void Wait(Func<bool> ready, [System.Runtime.CompilerServices.CallerArgumentExpression("ready")] string? condition = null)
    {
        var clock=Stopwatch.StartNew();
        while(!ready() && clock.Elapsed<TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(1); }
        if(!ready()) throw new TimeoutException("Timed out waiting for " + condition);
    }
    internal static void Pump(Task task) { Wait(()=>task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void LaunchControls(Session s,bool fail)
    {
        int requests=0,errors=0; var pending=new TaskCompletionSource();
        using var form=new DiagnosticsForm(false,launchProcess:(_,token)=> { requests++; return fail?Task.FromException(new IOException()):pending.Task.WaitAsync(token); },launchError:_=>errors++);
        form.Show(); var launch=Button(form,"&Launch Roblox"); Click(s,launch); Click(s,launch);
        s.Check(requests==(fail?2:1),"Repeated click suppression or failure retry");
        s.Check(fail?errors==2 && launch.Enabled:!launch.Enabled,"Launch button busy/failure state");
        form.Close(); s.InputActions++; Application.DoEvents(); s.Check(!pending.Task.IsCompleted,"Closing never terminates child");
    }
    private static void SelectionControls(Session s)
    {
        s.Platform.EnsureOne(); using var form=new ActiveInstancesForm(false,s.Platform,new LayoutPreferences(new LayoutStore(s.File("layouts.json"))));
        form.Show(); var snapshot=new InstanceCollector(s.Platform,Program.Sid,Process.GetCurrentProcess().SessionId).Collect(default);
        form.Present(snapshot); var list=Descendants(form).OfType<ListView>().Single(); list.Items[0].Selected=true; s.InputActions=1; Application.DoEvents();
        var target=form.SelectedTarget; s.Check(target is not null,"Row selection chooses sole window");
        form.Present(new(snapshot.Instances.Reverse().ToArray(),0)); s.Check(form.SelectedTarget==target,"Selection survives reordered refresh");
        if(s.Degraded) { form.Present(InstanceSnapshot.Failed); s.Check(!Button(form,"&Focus window").Enabled,"Stale snapshot disables actions"); }
        s.Notes.Add("F03: identical captions require PID/start-time interpretation"); form.Close(); s.InputActions++;
    }
    private static void ArrangementControls(Session s)
    {
        s.Platform.EnsureOne(); var choices=new List<ArrangementChoice>();
        InstanceSnapshot Discover(CancellationToken _) => new(s.Platform.Windows.Values.Select(w=>new ActiveInstance(w.Target.Key,[new(w.Target.Window,w.Caption)],null,null,false)).ToArray(),0);
        using var form=new ArrangementForm(choices,new LayoutPreferences(new LayoutStore(s.File("layout.json"))),ArrangementKind.Grid,null,"fixture-display",s.Platform,Discover);
        form.Show(); Wait(()=>choices.Count>0 && Button(form,"&Apply").Enabled);
        s.Check(Descendants(form).OfType<LayoutPreview>().Single().Plan is { Valid: true },"Automatic preview is ready");
        var grid=Descendants(form).OfType<DataGridView>().Single();
        if(choices.Count>1) { grid.CurrentCell=grid.Rows[1].Cells[1]; s.InputActions++; var target=choices[1].Target; Click(s,Button(form,"Move up")); s.Check(choices[0].Target==target,"Actual reorder control changes slot order"); }
        s.Check(!Button(form,"&Apply").Enabled || choices.Count==1,"Changes invalidate preview");
        Click(s,Button(form,"Cancel")); if(!form.IsDisposed)form.Close();
        s.Check(s.Platform.Moves==0,"Cancel never moves fixture windows");
    }
    private static void Click(Session s,Button button) { UiTestContext.Install(); s.InputActions=(s.InputActions??0)+1; button.PerformClick(); }
    internal sealed record RenderEvidence(string Form,int ScalePercent,string Screenshot,int Buttons,int MissingAccessibleNames,string[] OutsideParent,string[] TabSequence,string[] DuplicateMnemonics);
    private static List<RenderEvidence> RenderAll()
    {
        var evidence=new List<RenderEvidence>();
        foreach(int scale in new[]{100,150,200}) foreach(string name in new[]{"dashboard","instances","arrangement","notifications","shortcuts"})
        {
            string directory=Path.Combine(Output,"render-fixtures"); Directory.CreateDirectory(directory);
            var p=new Fixture(2); var preferences=new LayoutPreferences(new LayoutStore(Path.Combine(directory,"layout.json")));
            var choices=new List<ArrangementChoice>();
            Form form=name switch {
                "dashboard"=>new DiagnosticsForm(false),
                "instances"=>new ActiveInstancesForm(false,p,preferences),
                "arrangement"=>new ArrangementForm(choices,preferences,ArrangementKind.Grid,null,"fixture-display",p,_=>new(p.Windows.Values.Select(w=>new ActiveInstance(w.Target.Key,[new(w.Target.Window,w.Caption)],null,null,false)).ToArray(),0)),
                "notifications"=>new NotificationSettingsForm(new NotificationPreferences(Path.Combine(directory,"notifications.json"))),
                _=>new ShortcutSettingsForm(preferences)
            };
            using(form)
            {
                if(form is DiagnosticsForm dashboard)dashboard.Present(Health(true),[],true);
                if(form is ActiveInstancesForm instances) instances.Present(new InstanceCollector(p,Program.Sid,Process.GetCurrentProcess().SessionId).Collect(default));
                form.Show(); if(name=="arrangement")Wait(()=>choices.Count==2 && Button(form,"&Apply").Enabled);
                if(form.MinimumSize.Width>0)form.Size=form.MinimumSize;
                if(scale!=100)form.Scale(new SizeF(scale/100f,scale/100f));
                form.PerformLayout(); Application.DoEvents();
                string filename=$"{name}-{scale}.png";
                using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new(Point.Empty,form.Size));bitmap.Save(Path.Combine(Output,filename));}
                var controls=Descendants(form).Where(c=>c.Visible).ToArray();
                var outside=controls.Where(c=>c is System.Windows.Forms.Button or ComboBox or TextBox or DataGridView or ListView or Label)
                    .Where(c=>Clipped(c,form)).Select(c=>$"{c.GetType().Name}: {c.Text}").ToArray();
                var buttons=controls.OfType<Button>().ToArray();
                var duplicates=buttons.Where(b=>b.Text.Contains('&')).GroupBy(b=>char.ToUpperInvariant(b.Text[b.Text.IndexOf('&')+1])).Where(g=>g.Count()>1).Select(g=>g.Key+": "+string.Join(", ",g.Select(b=>b.Text))).ToArray();
                evidence.Add(new(name,scale,filename,buttons.Length,buttons.Count(b=>string.IsNullOrEmpty(b.AccessibleName)),outside,
                    TabSequence(form),duplicates));
                form.Close();
            }
        }
        return evidence;
    }
    internal static bool Clipped(Control control,Form form)
    {
        var bounds=control.RectangleToScreen(control.ClientRectangle);
        for(Control? parent=control.Parent;parent is not null;parent=parent.Parent)
            if(!parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds))return true;
        return false;
    }
    private static string[] TabSequence(Form form)
    {
        var sequence=new List<string>(); Control? current=null;
        for(int i=0;i<100;i++)
        {
            current=form.GetNextControl(current,true);if(current is null)break;
            if(current.Visible && current.Enabled && current.TabStop)sequence.Add(current.AccessibleName ?? (string.IsNullOrEmpty(current.Text)?current.GetType().Name:current.Text));
        }
        return sequence.ToArray();
    }
    internal sealed class Fixture : IInstancePlatform,IPlacementPlatform
    {
        internal readonly Dictionary<InstanceTarget,LayoutWindow> Windows=[];
        internal bool FailEnumeration,DenyFocus,Replaced,Windowless,ChangedDisplays;
        internal int FocusCalls,Stops,Moves;
        internal Fixture(int count) { for(int i=1;i<=count;i++)Add(i); }
        private void Add(int i) { var target=new InstanceTarget(new(i,Created+i),i); Windows[target]=new(target,"Synthetic Roblox",new(20+i*10,30+i*10,800,500),new(16,39),true); }
        internal void EnsureOne() { if(Windows.Count==0)Add(1); }
        public int[] Candidates()=>FailEnumeration?throw new IOException():Windows.Keys.Select(t=>t.Key.Id).ToArray();
        public IInstanceProcess Open(int pid)=>new Client(this,pid);
        public bool Trusted(string image)=>true;
        public int ProcessorCount=>8;
        public LayoutMonitor[] Monitors()=>[new("fixture-display","Synthetic monitor",new(ChangedDisplays?10:0,0,1920,1040),96,true)];
        IPlacementSession IPlacementPlatform.Open(InstanceTarget target)=>new Placement(this,target);
        internal sealed class Client(Fixture owner,int pid):IBackgroundClientProcess
        {
            private InstanceTarget Target=>owner.Windows.Keys.First(t=>t.Key.Id==pid);
            public Identity Identity=>new(Program.Sid,Process.GetCurrentProcess().SessionId,false,@"C:\Fixture\RobloxPlayerBeta.exe",Target.Key.Created+(owner.Replaced?1:0));
            public bool IsAlive=>true;
            public long? CpuTime()=>1000;
            public ulong? WorkingSet()=>1048576;
            public InstanceWindow[] Windows()=>owner.Windowless?[]:[new(Target.Window,"Synthetic Roblox")];
            public bool OwnsWindow(nint window)=>window==Target.Window;
            public void Restore(nint window){}
            public bool Focus(nint window){owner.FocusCalls++;return !owner.DenyFocus;}
            public bool Close(nint window)=>true;
            public bool HasVisibleWindow()=>!owner.Windowless;
            public bool Stop(){owner.Stops++;return true;}
            public bool WaitForExit(uint milliseconds)=>true;
            public void Dispose(){}
        }
        private sealed class Placement(Fixture owner,InstanceTarget target):IPlacementSession
        {
            private Rectangle bounds=owner.Windows[target].NormalBounds;
            public bool Valid=>!owner.Replaced;
            public bool Normal=>true;
            public LayoutWindow Read()=>owner.Windows[target] with{NormalBounds=bounds};
            public WindowedResult RequestWindowed(CancellationToken token)=>WindowedResult.Ready;
            public bool Restore()=>true;
            public bool Move(Rectangle value){owner.Moves++;bounds=value;return true;}
            public Rectangle Bounds()=>bounds;
            public void Dispose(){}
        }
    }
    private sealed class FullscreenFixture:IFullscreenInput
    {
        public nint Foreground{get;set;}=10;
        internal bool Denied,FullscreenState=true;
        internal int Toggles;
        public bool ManagerWindow(nint window)=>window==10;
        public bool Focus(nint window){if(Denied && window==20)return false;Foreground=window;return true;}
        public bool Fullscreen(nint window)=>FullscreenState;
        public bool KeysReleased=>true;
        public bool Toggle(nint window){Toggles++;FullscreenState=false;return true;}
    }
    internal sealed class Hotkeys:IHotkeyPlatform
    {
        internal bool Conflict;
        internal readonly HashSet<int> Held=[];
        public bool Register(int id,Keys keys)=>!(Conflict && id==1) && Held.Add(id);
        public void Unregister(int id)=>Held.Remove(id);
    }
}
