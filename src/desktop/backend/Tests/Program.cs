using System.Text.Json;
using RobloxMultiInstance;
using RobloxLaunchHelper;
using LayoutSettings = RobloxLaunchHelper.LayoutSettings;

if(args.FirstOrDefault() is "--policy-fixture" or "--policy-owner") return ProcessPolicyTests.Fixture(args);
if(args.Length==1&&args[0]=="--contracts") { Console.Write(BackendContractEmitter.TypeScript());return 0; }
if(args.Any(value=>value is "--policy-fixture" or "--policy-owner")) { Console.Error.WriteLine("Misplaced backend fixture invocation.");return 2; }
int passed=0;
string root=Path.Combine(Path.GetTempPath(),"RobloxPreviewTests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
JsonElement Payload(string json="{}") => JsonSerializer.Deserialize<JsonElement>(json);
void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++; }
async Task Reject(DesktopBackend backend,string cmd,string payload,string name)
{
    try { await backend.ExecuteAsync(cmd,Payload(payload),CancellationToken.None);throw new Exception("Unexpected success: "+name); }
    catch(Exception e) when(e is InvalidOperationException or ArgumentException or DesktopOperationException) { Check(true,name); }
}
try
{
    ProfileCleanupTests.Run(Path.Combine(root,"profile-cleanup"),Check);
    SpecialBadgeTests.Run(Path.Combine(root,"special-badge-fixtures"),Check);
    string gamesRoot=Path.Combine(root,"recent-games-routing");
    using(var gamesBackend=new DesktopBackend(gamesRoot))
    using(var library=new GameLibrary(gamesRoot))
    {
        async Task<GameCardDto[]> RecentCommand(string command,string payload="{}") =>
            (GameCardDto[])(await gamesBackend.ExecuteAsync(command,Payload(payload),default))!;
        Check((await RecentCommand("games.recent")).Length==0&&!Directory.Exists(gamesRoot),"Recent games dispatch reads empty history without creating storage");
        var card=new GameCardDto("123",null,null,"Fixture game",null,AccountIds:["second","first"],Preset:"Two windows");
        await library.Remember(card,default);
        await library.Remember(card with {PlaceId="456"},default);
        var history=await RecentCommand("games.recent");
        Check(history.Length==2&&history[1].AccountIds!.SequenceEqual(new[]{"second","first"}),"Recent games dispatch returns persisted cards and ordered batch accounts");
        Check((await RecentCommand("games.remove","{\"placeId\":\"123\"}")).Single().PlaceId=="456","Remove recent game dispatch preserves the other card");
        Check((await RecentCommand("games.clear")).Length==0&&(await RecentCommand("games.recent")).Length==0,"Clear recent games dispatch persists empty history");
    }
    passed += await PerformanceTests.Run(root);
    passed += await AutomaticPerformanceTests.Run(root);
    passed += LaunchSettingsTests.Run(root);
    passed += await ProcessPolicyTests.Run(root);
    string cleanupRoot=Path.Combine(root,"cleanup-preferences");
    async Task<bool> CleanupEnabled(DesktopBackend backend) => JsonSerializer.SerializeToElement(await backend.ExecuteAsync("background.status",Payload(),default)).GetProperty("enabled").GetBoolean();
    using(var cleanupBackend=new DesktopBackend(cleanupRoot))
    {
        Check(await CleanupEnabled(cleanupBackend),"Background cleanup defaults enabled without starting a live monitor in fixture storage");
        await Reject(cleanupBackend,"background.set","{}","Cleanup setting requires an explicit boolean");
        await cleanupBackend.ExecuteAsync("background.set",Payload("{\"enabled\":false}"),default);
    }
    using(var cleanupBackend=new DesktopBackend(cleanupRoot))
    {
        Check(!await CleanupEnabled(cleanupBackend),"Disabled cleanup survives restart");
        await cleanupBackend.ExecuteAsync("background.set",Payload("{\"enabled\":true}"),default);
        await cleanupBackend.StopBackgroundCleanupAsync();
    }
    using(var cleanupBackend=new DesktopBackend(cleanupRoot))
        Check(await CleanupEnabled(cleanupBackend),"Host shutdown preserves enabled cleanup preference");
    string migrationSource=Path.Combine(root,"migration-source"),migrationDestination=Path.Combine(root,"migration-destination");
    Directory.CreateDirectory(migrationSource);
    File.WriteAllText(Path.Combine(migrationSource,"appearance.json"),"{\"Version\":1,\"Scheme\":4}");
    new LayoutStore(Path.Combine(migrationSource,"window-layouts.json")).Save(LayoutSettings.Default);
    Check(PreferenceMigration.Copy(migrationSource,migrationDestination)=="","Valid Preview settings migrate");
    Check(File.ReadAllText(Path.Combine(migrationSource,"appearance.json"))==File.ReadAllText(Path.Combine(migrationDestination,"appearance.json")),"Migration preserves source bytes");
    File.WriteAllText(Path.Combine(migrationDestination,"appearance.json"),"existing");
    PreferenceMigration.Copy(migrationSource,migrationDestination);
    Check(File.ReadAllText(Path.Combine(migrationDestination,"appearance.json"))=="existing","Existing primary preferences are never overwritten");
    File.Delete(Path.Combine(migrationDestination,"appearance.json"));
    File.WriteAllText(Path.Combine(migrationSource,"appearance.json"),"invalid");
    Check(PreferenceMigration.Copy(migrationSource,migrationDestination).Contains("preserved"),"Corrupt Preview settings surface a warning");
    Check(!File.Exists(Path.Combine(migrationDestination,"appearance.json")),"Corrupt Preview settings are not copied");
    using(var backend=new DesktopBackend(root))
    {
        var state=JsonSerializer.SerializeToElement(await backend.ExecuteAsync("app.state",Payload(),default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Check(state.GetProperty("version").GetString()==typeof(DesktopBackend).Assembly.GetName().Version!.ToString(3),"Primary desktop identity");
        Check(!Directory.EnumerateFiles(root).Any(),"Read-only startup creates no preferences");
        var privacy=JsonSerializer.SerializeToElement(await backend.ExecuteAsync("telemetry.get",Payload(),default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Check(!privacy.GetProperty("enabled").GetBoolean(),"Reporting defaults off in injected storage");
        var support=JsonSerializer.SerializeToElement(await backend.ExecuteAsync("support.get",Payload(),default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Check(!support.GetProperty("supported").GetBoolean(),"No supporter badge without a verified receipt");
        var accounts=JsonSerializer.SerializeToElement(await backend.ExecuteAsync("accounts.state",Payload(),default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Check(!accounts.GetProperty("launchAvailable").GetBoolean(),"Live account launch remains gated on acceptance");
        Check(!Directory.Exists(Path.Combine(root,"privacy"))&&!Directory.Exists(Path.Combine(root,"accounts"))&&!File.Exists(Path.Combine(root,"support-preferences.json")),"Read-only feature startup writes no files");
        await Reject(backend,"telemetry.set","{}","Reporting changes require an explicit boolean");
        await Reject(backend,"support.remove","{}","Removing receipt requires confirmation");
        await Reject(backend,"accounts.remove","{}","Removing local session requires confirmation");
        await Reject(backend,"accounts.connect","{}","Missing native sign-in delegate cannot collect credentials");
        var signInStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signInClosed=new TaskCompletionSource<AccountIdentity?>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.AccountSignIn=(_,_)=>{signInStarted.SetResult();return signInClosed.Task;};
        var connecting=backend.ExecuteAsync("accounts.connect",Payload(),default);
        await signInStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try { await backend.ExecuteAsync("settings.get",Payload(),default).WaitAsync(TimeSpan.FromSeconds(2));Check(true,"Open account sign-in does not retain the shared settings gate"); }
        finally {signInClosed.TrySetResult(null);await connecting;backend.AccountSignIn=null;}
        backend.AccountSignIn=(_,_)=>Task.FromResult<AccountIdentity?>(null);
        await backend.ExecuteAsync("accounts.connect",Payload(),default);backend.AccountSignIn=null;
        Check(true,"Canceled sign-in permits another sign-in operation");
        await Reject(backend,"support.setRole","{\"role\":\"Super Supporter\"}","Supporter role cannot be set by a bridge command");
        await Reject(backend,"support.roblox","{\"passId\":\"999999999\"}","Browser cannot choose an unconfigured donation pass");
        await Reject(backend,"support.roblox","{\"passId\":\"https://attacker.invalid/\"}","Donation bridge rejects arbitrary destinations");
        Check(await backend.ExecuteAsync("support.checkRoblox",Payload(),default) is SupportDto,"Purchase verification defaults to automatic discovery without choosing a pass");
        await Reject(backend,"support.checkRoblox","{\"passId\":\"999999999\"}","Verification bridge rejects unconfigured passes");
        await Reject(backend,"support.checkRoblox","{\"scanAll\":\"true\"}","Full discovery requires a boolean choice");
        await Reject(backend,"support.checkRoblox","{\"scanAll\":true,\"passId\":\"2076244661\"}","Verification refuses ambiguous selected and full-scan scopes");
        if(SupportService.DeveloperToolsEnabled){
            await Reject(backend,"support.devResetRoblox","{}","Dev reset requires an explicit reset action");
            var reset=JsonSerializer.SerializeToElement(await backend.ExecuteAsync("support.devResetRoblox",Payload("{\"confirmed\":true}"),default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(reset.GetProperty("totalRobux").GetInt64()==0,"Development bridge resets Robux checking to zero");
        }else await Reject(backend,"support.devResetRoblox","{\"confirmed\":true}","Normal backend refuses development credit reset");
        await Reject(backend,"unknown.command","{}","Unknown commands rejected");
        var quickAccounts=JsonSerializer.SerializeToElement(await backend.ExecuteAsync("accounts.state",Payload("{\"includeAvatars\":false}"),default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Check(quickAccounts.TryGetProperty("accounts",out _),"Quick launcher can read local accounts without avatar requests");
        await Reject(backend,"accounts.state","{\"includeAvatars\":\"false\"}","Avatar option requires an explicit boolean");
        await Reject(backend,"system.maintenance","{\"action\":\"uninstall\"}","Maintenance requires confirmation");
        await Reject(backend,"settings.notifications","{\"showProgress\":true}","Shared notification writes require confirmation");
        await Reject(backend,"activity.clear","{}","History clearing requires confirmation");
        await Reject(backend,"instances.close","{\"id\":\"stale\"}","Close requires confirmation");
        await Reject(backend,"instances.stopAll","{\"ids\":[\"stale\"]}","Stop All requires confirmation");
        await Reject(backend,"instances.stopAll","{\"confirmed\":true,\"ids\":[\"stale\"]}","Stop All rejects stale selections before opening any process");
        await Reject(backend,"instances.stopAll","{\"confirmed\":true,\"ids\":[]}","Stop All requires a frozen nonempty list");
        await Reject(backend,"instances.focus","{\"id\":\"stale\"}","Stale selections rejected");
        await Reject(backend,"layouts.apply","{\"confirmed\":true,\"planId\":\"stale\"}","Stale plans rejected");
        await backend.ExecuteAsync("settings.save",Payload("{\"scheme\":\"WarmNeutral\"}"),default);
        Check(File.Exists(Path.Combine(root,"appearance.json")),"Appearance writes only injected preview store");
        foreach(string scheme in new[]{"Purple","Ocean","Forest","Rose"})
        {
            await backend.ExecuteAsync("settings.save",JsonSerializer.SerializeToElement(new {scheme}),default);
            using var reopened=new DesktopBackend(root);
            var savedAppearance=JsonSerializer.SerializeToElement(await reopened.ExecuteAsync("settings.get",Payload(),default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(savedAppearance.GetProperty("scheme").GetString()==scheme,"Appearance persists "+scheme);
        }
        await Reject(backend,"settings.save","{\"scheme\":\"unknown\"}","Invalid appearance rejected");
        await backend.ExecuteAsync("settings.reset",Payload("{\"confirmed\":true}"),default);
        Check(new LayoutStore(Path.Combine(root,"window-layouts.json")).Load().Presets.Length==0,"Explicit reset restores defaults");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        try { await backend.ExecuteAsync("app.state",Payload(),canceled.Token);throw new Exception("Cancellation ignored"); } catch(OperationCanceledException) {Check(true,"Canceled requests rejected before execution");}
    }
    // Exercise the real save DTO, on-disk validation and a fresh backend reload.
    var shortcutBindings = new Dictionary<string,int>(LayoutSettings.Default.Shortcuts);
    shortcutBindings["Manager"] = 0x7004d;
    using(var shortcutBackend = new DesktopBackend(root))
    {
        await shortcutBackend.ExecuteAsync("settings.save",JsonSerializer.SerializeToElement(new { shortcuts=shortcutBindings }),default);
        shortcutBindings["Manager"] = 393293;
        await shortcutBackend.ExecuteAsync("settings.save",JsonSerializer.SerializeToElement(new { shortcuts=shortcutBindings }),default);
        var duplicate = new Dictionary<string,int>(shortcutBindings) { ["Saved"] = 393293 };
        await Reject(shortcutBackend,"shortcuts.save",JsonSerializer.Serialize(new { bindings=duplicate }),"Duplicate shortcuts rejected");
    }
    using(var shortcutBackend = new DesktopBackend(root))
    {
        var loaded=JsonSerializer.SerializeToElement(await shortcutBackend.ExecuteAsync("settings.get",Payload(),default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Check(loaded.GetProperty("shortcuts").GetProperty("Manager").GetInt32()==393293,"Ctrl Alt M survives save and fresh backend reload");
        Check(loaded.GetProperty("shortcuts").GetProperty("Saved").GetInt32()==393298,"Rejected duplicate preserves Saved Ctrl Alt R");
    }
    File.WriteAllText(Path.Combine(root,"window-layouts.json"),"corrupt");
    using(var backend=new DesktopBackend(root))
    {
        var state=JsonSerializer.SerializeToElement(await backend.ExecuteAsync("settings.get",Payload(),default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Check(!state.GetProperty("available").GetBoolean(),"Corrupt settings leave app usable");
        await Reject(backend,"shortcuts.save","{\"bindings\":{}}","Corrupt settings cannot be overwritten implicitly");
        Check(File.ReadAllText(Path.Combine(root,"window-layouts.json"))=="corrupt","Corrupt source preserved");
        await backend.ExecuteAsync("settings.reset",Payload("{\"confirmed\":true}"),default);
        Check(new LayoutStore(Path.Combine(root,"window-layouts.json")).Load().Version==1,"Reset recovers corrupt settings");
    }
    Check(InstanceCollector.CalculateCpu(100,50,1,8) is null,"CPU counter rollback unavailable");
    Check(InstanceCollector.CalculateCpu(0,10000000,1,4)==25,"CPU uses machine processor denominator");
    try { LayoutStore.Validate(LayoutSettings.Default with { Shortcuts=new() });throw new Exception("Invalid shortcuts accepted"); } catch(InvalidDataException) { Check(true,"Shortcut schema validation retained"); }
    var m=new LayoutMonitor("test","Primary",new(0,0,1920,1080),96,true);
    Check(LayoutPlanner.Cells(m,4,ArrangementKind.Grid).Length==4,"Layout planner retains grid allocation");
    Check(DesktopBackend.BatchArrangement("Grid","")==ArrangementKind.Grid&&DesktopBackend.BatchArrangement("Columns","")==ArrangementKind.SideBySide,"Quick launch supports explicit Grid and Columns");
    Check(DesktopBackend.BatchArrangement("","Saved") is null,"Existing saved-layout launches retain their original behavior");
    foreach(var invalid in new[]{("Saved",""),("grid",""),("Grid","Saved"),("999","")})
    {
        try{DesktopBackend.BatchArrangement(invalid.Item1,invalid.Item2);throw new Exception("Invalid quick layout accepted.");}
        catch(ArgumentException){Check(true,"Invalid or conflicting quick layouts rejected before launch");}
    }
    var target1=new InstanceTarget(new ProcessKey(1,1),1);
    var target2=new InstanceTarget(new ProcessKey(2,2),2);
    var movable=new LayoutWindow(target1,"Ready",new(0,0,800,600),new(0,0),true,NeedsWindowed:true);
    var unavailable=new LayoutWindow(target2,"Unavailable",new(0,0,800,600),new(0,0),false,"Exited");
    var grid=DesktopBackend.BuildPlan([m],[unavailable,movable],[m.Id,m.Id],ArrangementKind.Grid,null);
    Check(grid.Valid&&grid.Placements.Single().Slot==2,"Immovable windows skipped while retaining original slot numbers");
    Check(grid.Notes.Any(n=>n.Contains("F11")),"Fullscreen preparation notice retained");
    var saved=DesktopBackend.BuildPlan([m],[unavailable,movable],[m.Id,m.Id],ArrangementKind.Saved,null);
    Check(!saved.Valid&&saved.Error!.Contains("All saved-position"),"Saved layouts require every selected slot");
    Check(!DesktopBackend.BuildPlan([],[],[],ArrangementKind.Grid,null).Valid,"No-display layout rejected");
    var batch=new LaunchBatchResultDto("fixture","Completed",true,"Saved",[new("account-one",1,"Started","OK",1,1),new("account-two",2,"Started","OK",2,2)]);
    var snapshot=new InstanceSnapshot([new(new(1,1),[new(11,"One")],null,null,false),new(new(2,2),[new(22,"Two")],null,null,false)],0);
    Check(DesktopBackend.BatchTargets(batch,snapshot)!.Select(t=>t.Window).SequenceEqual(new nint[]{11,22}),"Verified account batch preserves preset slot order");
    Check(DesktopBackend.BatchTargets(batch,snapshot with {Instances=[snapshot.Instances[0],snapshot.Instances[1] with {Key=new(2,99)}]}) is null,"PID reuse prevents automatic account layouts");
    Check(DesktopBackend.BatchTargets(batch,snapshot with {Instances=[snapshot.Instances[0]]}) is null,"Missing account window prevents automatic layouts");
    Check(DesktopBackend.BatchTargets(batch with {CanApplyLayout=false},snapshot) is null,"Partial account batch cannot apply a layout");
    Check(DesktopBackend.BatchTargets(batch,snapshot with {Instances=[snapshot.Instances[0] with {Windows=[new(11,"One"),new(12,"Ambiguous")]},snapshot.Instances[1]]}) is null,"Ambiguous windows prevent automatic account layouts");
    string contracts=BackendContractEmitter.TypeScript();
    Check(contracts.Contains("export interface InstanceDto")&&contracts.Contains("username: string | null"),"TypeScript contracts generated from nullable C# DTOs");
    Console.WriteLine($"{passed} backend checks passed.");
    return 0;
}
finally { Directory.Delete(root,true); }
