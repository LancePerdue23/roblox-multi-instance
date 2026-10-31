using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace RobloxMultiInstance.Desktop;

// This partial class exists only in AuditHarness.csproj, never in the shipped host.
internal sealed partial class MainWindow
{
    internal async Task<bool> AuditProbe(string expression)
    {
        if (view.CoreWebView2 is null) return false;
        return await view.CoreWebView2.ExecuteScriptAsync($"Boolean({expression})") == "true";
    }
    internal Task AuditScript(string script) => view.CoreWebView2.ExecuteScriptAsync(script);
    internal async Task AuditCapture(string file)
    {
        if (view.CoreWebView2 is null) return;
        using var stream = File.Create(file);
        await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
    }
    internal void AuditExit() { exiting = true; Close(); }
}

internal sealed record AuditResult(string Scenario, string Persona, string Outcome, double DurationMs, string Detail, string? Artifact = null);

internal static class AuditEntry
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    [STAThread]
    public static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 1;
        app.Startup += async (_, _) =>
        {
            try { result = args.Contains("--live") ? await LiveAudit.Run(output) : await Run(output); }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, "harness-error.txt"), args.Contains("--live") ? ex.GetType().Name : ex.ToString());
                result = 1;
            }
            finally { app.Shutdown(result); }
        };
        app.Run(); return result;
    }

    internal static async Task Wait(MainWindow window, string expression)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (await window.AuditProbe(expression)) return;
            await Task.Delay(80);
        }
        throw new TimeoutException("Native desktop condition did not become true: " + expression);
    }
    internal static async Task Click(MainWindow window, string label)
    {
        string match = $"[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==={JsonSerializer.Serialize(label)} || (b.matches('.nav-item') && b.textContent.trim().endsWith({JsonSerializer.Serialize(label)})))";
        await Wait(window, $"!!({match}) && !({match}).disabled");
        await window.AuditScript($"({match}).click()");
        await Task.Delay(100);
    }

    private static async Task<int> Run(string output)
    {
        var results = new List<AuditResult>();
        var backend = new FixtureBackend(Path.Combine(output, "preferences"));
        var window = new MainWindow(backend, Path.Combine(output, "webview-profile"));
        window.Show();
        async Task Case(string name, string persona, Func<Task> action)
        {
            var timer = Stopwatch.StartNew();
            try { await action(); results.Add(new(name, persona, "passed", timer.Elapsed.TotalMilliseconds, "Verified in WPF/WebView2 with isolated backend fixtures.")); }
            catch (Exception ex)
            {
                string file = Path.Combine(output, $"native-failure-{results.Count}.png");
                try { await window.AuditCapture(file); } catch { }
                results.Add(new(name, persona, "failed", timer.Elapsed.TotalMilliseconds, ex.Message, file));
            }
            File.WriteAllText(Path.Combine(output, "native.json"), JsonSerializer.Serialize(results, Json));
        }
        try
        {
            await Case("Live ownership refuses unconfirmed and ambiguous candidates", "power", () =>
            {
                var key=new RobloxLaunchHelper.ProcessKey(42,100);
                var candidate=new RobloxLaunchHelper.ActiveInstance(key,[new(123,"Synthetic client")],null,null,false);
                if(LiveAudit.ConfirmOwned([candidate],[],false) is not null ||
                    LiveAudit.ConfirmOwned([candidate,candidate with { Key=new(43,101) }],[],true) is not null ||
                    LiveAudit.ConfirmOwned([candidate],[key],true) is not null ||
                    LiveAudit.ConfirmOwned([candidate with { Windows=[] }],[],true) is not null ||
                    LiveAudit.ConfirmOwned([candidate],[],true) is null)
                    throw new Exception("Live client ownership safeguards failed.");
                return Task.CompletedTask;
            });
            await Case("Native startup and navigation", "basic", async () =>
            {
                await Wait(window, "document.querySelector('h1')?.textContent==='Instances' && document.querySelector('main')?.getAttribute('aria-busy')==='false'");
                foreach (string name in new[] { "Layouts", "Activity", "System", "Settings", "Instances" })
                {
                    await Click(window, name);
                    await Wait(window, $"document.querySelector('h1')?.textContent==={JsonSerializer.Serialize(name)} && document.querySelector('main').getAttribute('aria-busy')==='false'");
                }
                if (backend.Mutations != 0) throw new Exception("Startup/navigation requested a mutation.");
            });
            await Case("Native close confirmation cancellation", "basic", async () =>
            {
                await Click(window, "Instances");
                await window.AuditScript("document.querySelector('tbody input[type=checkbox]').click()");
                await Click(window, "Close window…");await Wait(window, "document.querySelector('dialog')?.open");
                await Click(window, "Cancel");await Wait(window, "!document.querySelector('dialog')?.open");
                if (backend.Calls.Contains("instances.close")) throw new Exception("Cancel dispatched close.");
            });
            await Case("Native appearance persistence across restart", "basic", async () =>
            {
                await Click(window, "Settings");
                // Theme choice includes nested elements, so match its strong label.
                await window.AuditScript("[...document.querySelectorAll('.theme-choice')].find(b=>b.querySelector('strong')?.textContent==='Light').click()");
                await Wait(window, "document.documentElement.dataset.theme==='Light'");
                window.AuditExit();await Task.Delay(350);
                backend = new FixtureBackend(Path.Combine(output, "preferences"));
                window = new MainWindow(backend, Path.Combine(output, "webview-profile"));window.Show();
                await Wait(window, "document.documentElement.dataset.theme==='Light' && document.querySelector('main')?.getAttribute('aria-busy')==='false'");
            });
            await Case("Native tray close, reopen and release", "power", async () =>
            {
                await Click(window, "Settings");await Click(window, "Shortcuts & tray");await Click(window, "Enable tray mode");
                await Wait(window, "[...document.querySelectorAll('button')].some(b=>b.textContent==='Disable tray mode')");
                window.Close();if (window.IsVisible) throw new Exception("Tray close did not hide the window.");
                window.Reopen();if (!window.IsVisible) throw new Exception("Tray reopen failed.");
                await Click(window, "Disable tray mode");await Wait(window, "[...document.querySelectorAll('button')].some(b=>b.textContent==='Enable tray mode')");
            });
            await Case("Native delayed operation duplicate protection", "power", async () =>
            {
                await Click(window, "Instances");await Wait(window, "document.querySelector('main').getAttribute('aria-busy')==='false'");
                await window.AuditScript("const b=[...document.querySelectorAll('button')].find(b=>b.textContent.includes('Launch Roblox'));b.click();b.click()");
                await Task.Delay(600);
                await Wait(window, "document.querySelector('main').getAttribute('aria-busy')==='false'");
                // React can receive both clicks before rerender; host must serialize the action.
                if (backend.Calls.Count(c => c == "launch") != 1) throw new Exception("Host dispatched duplicate launches.");
            });
            await Case("Native recoverable backend error", "power", async () =>
            {
                backend.FailNext = "updates.check";await Click(window, "System");await Wait(window, "document.querySelector('main').getAttribute('aria-busy')==='false'");
                await Click(window, "Check for updates");await Wait(window, "!!document.querySelector('[role=alert]')");
                await Click(window, "Check for updates");await Wait(window, "!document.querySelector('[role=alert]') && document.body.textContent.includes('9.9.9')");
            });
            await window.AuditCapture(Path.Combine(output, "native-final.png"));
        }
        finally { window.AuditExit(); }
        return results.Any(r => r.Outcome == "failed") ? 1 : 0;
    }
}

internal sealed class FixtureBackend : IDesktopBackend
{
    private readonly string root;
    internal readonly List<string> Calls = [];
    internal string? FailNext;
    internal int Mutations;
    public string AppearanceScheme { get; private set; }
    public event Action<string, string, object?>? Event { add { } remove { } }
    public FixtureBackend(string root)
    {
        this.root = root;Directory.CreateDirectory(root);
        AppearanceScheme = File.Exists(Path.Combine(root, "scheme.txt")) ? File.ReadAllText(Path.Combine(root, "scheme.txt")) : "Dark";
    }
    public async Task<object?> ExecuteAsync(string command, JsonElement p, CancellationToken token)
    {
        Calls.Add(command);token.ThrowIfCancellationRequested();
        if (FailNext == command) { FailNext = null; throw new DesktopOperationException("fixture", "Synthetic recoverable error"); }
        switch (command)
        {
            case "app.state": return new { version = "native-fixture", helperVersion = "fixture", helperInstalled = true };
            case "settings.get": return new { scheme = AppearanceScheme, available = true, shortcuts = new Dictionary<string,int> { ["Manager"] = 393293 }, notifications = new { showProgress = false, showCompletion = false, available = true }, imported = false };
            case "settings.save":
                Mutations++;AppearanceScheme = p.GetProperty("scheme").GetString()!;File.WriteAllText(Path.Combine(root,"scheme.txt"),AppearanceScheme);
                return await ExecuteAsync("settings.get",p,token);
            case "instances.list": return new { available = true, unavailable = 0, instances = new[] { new { id = "native-1", pid = 123, title = "Synthetic native client", hasWindow = true, cpuPercent = 1, workingSet = 104857600 } } };
            case "layouts.get": return new { monitors = Array.Empty<object>(), presets = Array.Empty<object>(), shortcuts = new { } };
            case "activity.list": return new { available = true, records = Array.Empty<object>() };
            case "system.health": return new { overall = "Pass", browserIntegrationEnabled = false, checks = Array.Empty<object>() };
            case "background.status": case "background.set": return new { enabled = false, owns = false, waiting = false };
            case "launch": Mutations++;await Task.Delay(400,token);return new { exitCode = 0 };
            case "updates.check": return new { available = true, version = "9.9.9", notes = "Synthetic notes", canDownload = true, downloaded = false };
            default: throw new DesktopOperationException("fixture", "Unsupported native fixture action.");
        }
    }
    public void Dispose() { }
}
