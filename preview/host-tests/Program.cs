using RobloxMultiInstancePreview.Desktop;
using System.Text.Json;

int count = 0;
void Check(bool condition) { if (!condition) throw new Exception("Bridge check failed."); count++; }
void Reject(string source, string message) { try { BridgeProtocol.Parse(source, message); } catch { count++; return; } throw new Exception("Unsafe request accepted."); }
string valid = "{\"version\":1,\"id\":\"request-1\",\"command\":\"instances.list\",\"payload\":{}}";
Check(BridgeProtocol.Parse("https://preview.local/index.html", valid).Command == "instances.list");
Check(BridgeProtocol.IsAllowedCommand("report.export"));
Check(!BridgeProtocol.IsAllowedCommand("system.export"));
Check(!BridgeProtocol.IsAllowedCommand("shell.execute"));
Check(!BridgeProtocol.IsAllowedCommand("maintenance.run"));
Reject("https://preview.local.attacker.test/", valid);
Reject("http://preview.local/", valid);
Reject("https://user@preview.local/", valid);
Reject("https://preview.local:444/", valid);
Reject("https://preview.local/", valid.Replace("\"version\":1", "\"version\":2"));
Reject("https://preview.local/", valid.Replace("\"id\":\"request-1\"", "\"id\":\"\""));
Reject("https://preview.local/", valid.Replace("\"payload\":{}", "\"payload\":[]"));
Reject("https://preview.local/", valid.Replace("\"payload\":{}", "\"payload\":{},\"shell\":\"calc\""));
Reject("https://preview.local/", valid.Replace("\"version\":1", "\"version\":1,\"version\":1"));
Reject("https://preview.local/", new string('x', BridgeProtocol.MaxMessageLength + 1));
Reject("https://preview.local/", "null");
Console.WriteLine($"Passed {count} bridge boundary checks.");
