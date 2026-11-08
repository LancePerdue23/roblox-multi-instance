using RobloxMultiInstance.Desktop;
using System.Text.Json;

int count = 0;
void Check(bool condition) { if (!condition) throw new Exception("Bridge check failed."); count++; }
Check(BridgeProtocol.IsAllowedCommand("support.importBadge"));Check(BridgeProtocol.IsAllowedCommand("support.badges"));Check(!BridgeProtocol.IsAllowedCommand("support.importBadgeFile"));
var signInChecks=new AccountSignInChecks();
Check(signInChecks.Begin(0));Check(!signInChecks.Begin(100));
signInChecks.Complete("NoSession",100);Check(!signInChecks.Begin(2099));Check(signInChecks.Begin(2100));
signInChecks.Complete("RateLimited",2100);Check(!signInChecks.Begin(62099));Check(signInChecks.Begin(62100));
signInChecks.Complete("WrongIdentity",62100);Check(!signInChecks.Begin(72099));Check(signInChecks.Begin(72100));
foreach(string command in new[]{"games.recent","games.remove","games.clear"})Check(BridgeProtocol.IsAllowedCommand(command));
void Reject(string source, string message) { try { BridgeProtocol.Parse(source, message); } catch { count++; return; } throw new Exception("Unsafe request accepted."); }
string valid = "{\"version\":1,\"id\":\"request-1\",\"command\":\"instances.list\",\"payload\":{}}";
Check(BridgeProtocol.Parse("https://desktop.local/index.html", valid).Command == "instances.list");
Check(BridgeProtocol.IsAllowedCommand("report.export"));
Check(!BridgeProtocol.IsAllowedCommand("system.export"));
foreach(string command in new[]{"performance.activate","performance.optimizationState","accounts.optimizationBypass","performance.snapshot","performance.profiles","performance.saveProfiles","performance.previewImport","performance.import","performance.export","accounts.organize","accounts.order"})Check(BridgeProtocol.IsAllowedCommand(command));
foreach(string command in new[]{"performance.apply","performance.trim","performance.importFile","performance.readProcessMemory"})Check(!BridgeProtocol.IsAllowedCommand(command));
foreach(string command in new[]{"telemetry.get","telemetry.set","support.get","support.roblox","support.import","accounts.connect","accounts.launchBatch"})Check(BridgeProtocol.IsAllowedCommand(command));
foreach(string command in new[]{"support.importFile","support.exportFile","accounts.readCookie","accounts.launchTicket","shell.openUrl"})Check(!BridgeProtocol.IsAllowedCommand(command));
Reject("https://www.roblox.com/login",valid);
Check(!BridgeProtocol.IsAllowedCommand("shell.execute"));
Check(!BridgeProtocol.IsAllowedCommand("maintenance.run"));
Reject("https://desktop.local.attacker.test/", valid);
Reject("http://preview.local/", valid);
Reject("https://user@preview.local/", valid);
Reject("https://desktop.local:444/", valid);
Reject("https://desktop.local/", valid.Replace("\"version\":1", "\"version\":2"));
Reject("https://desktop.local/", valid.Replace("\"id\":\"request-1\"", "\"id\":\"\""));
Reject("https://desktop.local/", valid.Replace("\"payload\":{}", "\"payload\":[]"));
Reject("https://desktop.local/", valid.Replace("\"payload\":{}", "\"payload\":{},\"shell\":\"calc\""));
Reject("https://desktop.local/", valid.Replace("\"version\":1", "\"version\":1,\"version\":1"));
Reject("https://desktop.local/", new string('x', BridgeProtocol.MaxMessageLength + 1));
Reject("https://desktop.local/", "null");
var hung=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
bool disposed=false;
Check(!await AccountSignInCleanup.RunAsync(null,()=>hung.Task,()=>disposed=true,TimeSpan.FromMilliseconds(30)));Check(disposed);
disposed=false;bool cleared=false;
Check(!await AccountSignInCleanup.RunAsync(hung.Task,()=>{cleared=true;return Task.CompletedTask;},()=>disposed=true,TimeSpan.FromMilliseconds(30)));Check(disposed&&!cleared);
disposed=false;
Check(!await AccountSignInCleanup.RunAsync(null,()=>Task.FromException(new Exception("browser closed")),()=>disposed=true,TimeSpan.FromSeconds(1)));Check(disposed);
Check(await AccountSignInCleanup.RunAsync(Task.CompletedTask,()=>Task.CompletedTask,()=>{},TimeSpan.FromSeconds(1)));
count+=await StartupReadinessTests.RunAsync();


// Synthetic local activation: spaces and shell punctuation remain data.
var badgePath=Path.Combine(Path.GetTempPath(),"alpha & tester.rmibadge");
Check(BadgeActivation.Parse(["--claim-badge",badgePath])==badgePath);
Check(BadgeActivation.Parse([badgePath])==badgePath);
using(var activation=new MemoryStream()){BadgeActivation.Write(activation,badgePath);activation.Position=0;Check(activation.ReadByte()==3);Check(BadgeActivation.ReadAsync(activation,CancellationToken.None).GetAwaiter().GetResult()==badgePath);}
foreach(var invalid in new[]{"relative.rmibadge",Path.Combine(Path.GetTempPath(),"bad.exe")}){try{BadgeActivation.Validate(invalid);throw new Exception("Unsafe activation accepted");}catch(ArgumentException){Check(true);}}
using(var activation=new MemoryStream(BitConverter.GetBytes(8193))){try{BadgeActivation.ReadAsync(activation,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Oversized activation accepted");}catch(InvalidDataException){Check(true);}}

Check(BadgeActivation.Parse(["--safe-rendering","--claim-badge",badgePath])==badgePath);
try{BadgeActivation.Parse(["--claim-badge"]);throw new Exception("Missing path accepted");}catch(ArgumentException){Check(true);}
Console.WriteLine($"Passed {count} bridge, activation and sign-in lifetime checks.");
