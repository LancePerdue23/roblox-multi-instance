using System.Text.Json;
using RobloxMultiInstance;
using RobloxLaunchHelper;
using LayoutSettings = RobloxLaunchHelper.LayoutSettings;

if(args.FirstOrDefault()=="--contracts") { Console.Write(BackendContractEmitter.TypeScript());return 0; }
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
        Check(state.GetProperty("version").GetString()=="1.1.0","Primary desktop identity");
        Check(!Directory.EnumerateFiles(root).Any(),"Read-only startup creates no preferences");
        await Reject(backend,"unknown.command","{}","Unknown commands rejected");
        await Reject(backend,"system.maintenance","{\"action\":\"uninstall\"}","Maintenance requires confirmation");
        await Reject(backend,"settings.notifications","{\"showProgress\":true}","Shared notification writes require confirmation");
        await Reject(backend,"activity.clear","{}","History clearing requires confirmation");
        await Reject(backend,"instances.close","{\"id\":\"stale\"}","Close requires confirmation");
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
    string contracts=BackendContractEmitter.TypeScript();
    Check(contracts.Contains("export interface InstanceDto")&&contracts.Contains("username: string | null"),"TypeScript contracts generated from nullable C# DTOs");
    Console.WriteLine($"{passed} backend checks passed.");
    return 0;
}
finally { Directory.Delete(root,true); }
