using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

namespace RobloxMultiInstance.Desktop;

internal sealed partial class MainWindow : Window
{
    private readonly WebView2 view = new();
    private readonly WindowTheme windowTheme;
    private readonly System.Drawing.Icon trayIcon = LoadTrayIcon();
    private readonly IDesktopBackend backend;
    private readonly string? isolatedProfile;
    private readonly CancellationTokenSource closed = new();
    private readonly Dictionary<string, CancellationTokenSource> running = new(StringComparer.Ordinal);
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Queue<string> recent = new();
    private readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private Forms.NotifyIcon? tray;
    private ShortcutOwner? shortcuts;
    private bool exiting;
    private readonly bool smoke;
    private bool draining, readyToClose;
    private int layoutOperations;
    private TaskCompletionSource idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool handlingShortcut;
    private bool openUpdates;
    private bool hostReady;
    public void RequestUpdates(){openUpdates=true;if(hostReady){openUpdates=false;Send(new {version=1,@event="navigate",data=new {page="System",updates=true}});}}
    private RobloxLaunchHelper.UpdateShutdownRequest? updateShutdown;
    private readonly RobloxLaunchHelper.OperationCoordinator operations = new();

    public MainWindow(bool smoke = false) : this(null, null, smoke) { }

    internal MainWindow(IDesktopBackend? injectedBackend, string? profile, bool smoke = false)
    {
        this.smoke = smoke;
        isolatedProfile = profile;
        backend = injectedBackend ?? new DesktopBackend(smoke ? Path.Combine(AppContext.BaseDirectory, "smoke-preferences") : null);
        windowTheme = new WindowTheme(this, backend.AppearanceScheme);
        windowTheme.Changed += () => { if (tray?.ContextMenuStrip is Forms.ContextMenuStrip menu) windowTheme.StyleMenu(menu); };
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico"));
        idle.SetResult();
        Title = "Roblox Multi Instance";
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1440, workArea.Width); Height = Math.Min(960, workArea.Height);
        MinWidth = Math.Min(900, workArea.Width); MinHeight = Math.Min(640, workArea.Height); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = view;
        Loaded += async (_, _) => await InitializeAsync();
        Closing += OnClosing;
        Closed += (_, _) => { closed.Cancel(); windowTheme.Dispose(); updateShutdown?.Dispose(); shortcuts?.Dispose(); tray?.Dispose(); trayIcon.Dispose(); view.Dispose(); backend.Dispose(); };
        backend.Event += (operationId, name, data) =>
        {
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke(() => Send(new { version = 1, operationId, @event = name, data }));
        };
    }

    public void Reopen() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }

    private async Task InitializeAsync()
    {
        try
        {
            await RefreshWindowThemeAsync();
            string assets = Path.Combine(AppContext.BaseDirectory, "web");
            if (!File.Exists(Path.Combine(assets, "index.html"))) throw new FileNotFoundException("Bundled interface missing.");
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobloxMultiInstance", "WebView2");
            if (smoke) profile = Path.Combine(AppContext.BaseDirectory, "smoke-profile");
            if (isolatedProfile is not null) profile = isolatedProfile;
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            await view.EnsureCoreWebView2Async(environment);
            var core = view.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping("desktop.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (!BridgeProtocol.IsTrusted(e.Uri)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.WebMessageReceived += OnMessage;
            if (smoke) core.NavigationCompleted += async (_, navigation) =>
            {
                try
                {
                    if (!navigation.IsSuccess) throw new IOException("Navigation failed.");
                    await Task.Delay(3000, closed.Token);
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "host-theme-smoke.json"), JsonSerializer.Serialize(windowTheme.CheckCaptionColors(), json));
                    await windowTheme.CheckCaptionControlsAsync();
                    windowTheme.CaptureCaption(Path.Combine(AppContext.BaseDirectory, "host-chrome-smoke.png"));
                    using var file = File.Create(Path.Combine(AppContext.BaseDirectory, "host-smoke.png"));
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "host-smoke.txt"), "WebView2 initialized; local navigation completed; screenshot captured. No helper or maintenance action requested.");
                }
                catch { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "host-smoke.txt"), "Host smoke failed."); }
                finally { exiting = true; Close(); }
            };
            updateShutdown=new RobloxLaunchHelper.UpdateShutdownRequest(()=>Dispatcher.InvokeAsync(()=>{exiting=true;Close();}).Task);
            shortcuts = new ShortcutOwner(this);
            shortcuts.Pressed += async command =>
            {
                if (handlingShortcut || layoutOperations > 0 || draining) return;
                handlingShortcut = true;
                try
                {
                    string editing = await core.ExecuteScriptAsync("document.hasFocus() && !!document.activeElement && (['INPUT','TEXTAREA','SELECT'].includes(document.activeElement.tagName) || document.activeElement.isContentEditable)");
                    if (editing == "true") return;
                    Reopen(); Send(new { version = 1, operationId = "shortcuts", @event = "host.shortcut", data = new { command } });
                }
                catch { }
                finally { handlingShortcut = false; }
            };
            core.Navigate(BridgeProtocol.Origin + "/index.html");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            var panel = new StackPanel { Margin = new Thickness(40) };
            panel.Children.Add(new TextBlock { Text = "Microsoft Edge WebView2 Runtime is required", FontSize = 24 });
            panel.Children.Add(new TextBlock { Margin = new Thickness(0, 20, 0, 20), Text = "Install the Evergreen runtime from Microsoft, then reopen Roblox Multi Instance.", TextWrapping = TextWrapping.Wrap });
            var button = new Button { Content = "Open Microsoft’s runtime download page", Padding = new Thickness(15), HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            panel.Children.Add(button);
            var retry=new Button {Content="Retry after installing WebView2",Padding=new Thickness(15),Margin=new Thickness(0,12,0,0),HorizontalAlignment=HorizontalAlignment.Left};
            retry.Click+=async (_,_)=>{retry.IsEnabled=false;Content=view;await InitializeAsync();};panel.Children.Add(retry);Content = panel;
        }
        catch
        {
            Content = new TextBlock { Margin = new Thickness(40), TextWrapping = TextWrapping.Wrap,
                Text = "The desktop interface could not start. Rebuild using Build.ps1 and run the executable from its complete output folder." };
        }
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeRequest request;
        try { request = BridgeProtocol.Parse(e.Source, e.WebMessageAsJson); }
        catch { return; }
        if (!BridgeProtocol.IsAllowedCommand(request.Command)) { SendError(request.Id, "unsupported_command", "This command is not available through the preview interface."); return; }
        if (draining) { SendError(request.Id, "closing", "The preview is closing."); return; }
        if (running.ContainsKey(request.Id) || !seen.Add(request.Id)) { SendError(request.Id, "duplicate_request", "This request was already received."); return; }
        recent.Enqueue(request.Id);
        if (recent.Count > 4096) seen.Remove(recent.Dequeue());
        if (request.Command == "cancel")
        {
            if (request.Payload.TryGetProperty("operationId", out var id) && id.ValueKind == JsonValueKind.String && running.TryGetValue(id.GetString()!, out var pending)) pending.Cancel();
            Send(new { version = 1, id = request.Id, data = new { canceled = true } }); return;
        }
        if (running.Count >= 16) { SendError(request.Id, "busy", "Too many operations are running. Wait and retry."); return; }
        IDisposable lease;
        try { lease=operations.Enter(request.Command); }
        catch(InvalidOperationException) { SendError(request.Id,"busy","A conflicting operation is still running. Wait for it to finish.");return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closed.Token);
        if (running.Count == 0) idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        running.Add(request.Id, cancellation);
        bool layout = request.Command is "layouts.preview" or "layouts.apply";
        if (layout) { layoutOperations++; Send(new { version = 1, operationId = request.Id, @event = "host.busy", data = new { busy = true } }); }
        try
        {
            object? data;
            if (request.Command == "host.tray")
            {
                bool enabled = request.Payload.GetProperty("enabled").GetBoolean();
                SetTray(enabled); data = new { enabled };
            }
            else if (request.Command == "host.shortcuts")
            {
                bool enabled = request.Payload.GetProperty("enabled").GetBoolean();
                string[] conflicts = Array.Empty<string>();
                if (enabled)
                {
                    var state = JsonSerializer.SerializeToElement(await backend.ExecuteAsync("settings.get", JsonSerializer.SerializeToElement(new { }), cancellation.Token), json);
                    conflicts = shortcuts!.Enable(state.GetProperty("shortcuts"));
                    if (shortcuts.Enabled) SetTray(true);
                }
                else shortcuts?.Disable();
                data = new { enabled = shortcuts?.Enabled == true, conflicts };
            }
            else if (request.Command == "host.state") data = new { tray = tray is not null, shortcuts = shortcuts?.Enabled == true };
            else if (request.Command == "report.export")
            {
                if (!request.Payload.TryGetProperty("confirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True)
                    throw new ArgumentException("Confirmation required.");
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Troubleshooting ZIP (*.zip)|*.zip", FileName = "RobloxMultiInstance-report.zip", DefaultExt = ".zip", AddExtension = true, OverwritePrompt = true };
                data = dialog.ShowDialog(this) == true ? await backend.ExecuteAsync("system.export", JsonSerializer.SerializeToElement(new { path = dialog.FileName, confirmed = true }), cancellation.Token) : new { canceled = true };
            }
            else
            {
                data = await backend.ExecuteOperationAsync(request.Id, request.Command, request.Payload, cancellation.Token);
                if (request.Command is "settings.save" or "settings.reset" or "settings.import") await RefreshWindowThemeAsync();
                if (shortcuts?.Enabled == true && request.Command is "settings.save" or "shortcuts.save" or "settings.reset")
                {
                    var state = JsonSerializer.SerializeToElement(await backend.ExecuteAsync("settings.get", JsonSerializer.SerializeToElement(new { }), cancellation.Token), json);
                    string[] conflicts = shortcuts.Enable(state.GetProperty("shortcuts"));
                    Send(new { version = 1, operationId = request.Id, @event = "host.shortcutConflicts", data = new { conflicts } });
                }
            }
            Send(new { version = 1, id = request.Id, data });
            if(request.Command=="host.state"){hostReady=true;if(openUpdates)RequestUpdates();}
            if(data is RobloxMultiInstance.OperationResultDto result && result.Code is "SETUP_HANDOFF" or "UNINSTALL_REQUESTED")_ = Dispatcher.BeginInvoke(()=>{exiting=true;Close();});
        }
        catch (OperationCanceledException) { SendError(request.Id, "canceled", "Operation canceled."); }
        catch (DesktopOperationException ex) { SendError(request.Id, ex.Code, ex.Message); }
        catch (Exception ex)
        {
            // Backend supplies safe user-facing errors; never expose arbitrary native exception details.
            string message = ex is ArgumentException ? "The requested action or its options are invalid." : "The action could not complete. Refresh the page or check System health and try again.";
            SendError(request.Id, "operation_failed", message);
        }
        finally
        {
            if (layout) { layoutOperations--; Send(new { version = 1, operationId = request.Id, @event = "host.busy", data = new { busy = layoutOperations > 0 } }); }
            running.Remove(request.Id);
            lease.Dispose();
            if (running.Count == 0) idle.TrySetResult();
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream;
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }

    private async Task RefreshWindowThemeAsync()
    {
        var state = JsonSerializer.SerializeToElement(await backend.ExecuteAsync("settings.get", JsonSerializer.SerializeToElement(new { }), closed.Token), json);
        windowTheme.SetScheme(state.GetProperty("scheme").GetString() ?? "Dark");
    }

    private void SetTray(bool enabled)
    {
        if (!enabled) { shortcuts?.Disable(); tray?.Dispose(); tray = null; return; }
        if (tray is not null) return;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Roblox Multi Instance", null, (_, _) => Dispatcher.Invoke(Reopen));
        menu.Items.Add("Exit Roblox Multi Instance", null, (_, _) => Dispatcher.Invoke(() => { exiting = true; Close(); }));
        windowTheme.StyleMenu(menu);
        tray = new Forms.NotifyIcon { Text = Title, Icon = trayIcon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reopen);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (readyToClose) return;
        if (layoutOperations > 0)
        {
            e.Cancel = true;
            Send(new { version = 1, operationId = "lifetime", @event = "host.closeBlocked", data = new { message = "Wait for the layout operation to finish before closing." } });
            return;
        }
        if (!exiting && tray is not null) { e.Cancel = true; Hide(); return; }
        e.Cancel = true;
        if (draining) return;
        draining = true;
        // Always unwind the first Closing event before attempting the final Close.
        await Task.Yield();
        closed.Cancel();
        await idle.Task;
        try { await backend.ExecuteAsync("background.set", JsonSerializer.SerializeToElement(new { enabled = false }), CancellationToken.None); } catch { }
        readyToClose = true;
        Close();
    }
    private void SendError(string id, string code, string message) => Send(new { version = 1, id, error = new { code, message } });
    private void Send(object value)
    {
        if (!closed.IsCancellationRequested && view.CoreWebView2 is not null)
            view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, json));
    }
}
