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
    private WebView2 view = new();
    // Keep the window body stable while WindowTheme wraps it with caption chrome.
    // SourceInitialized can raise Loaded while that wrapper is being assembled.
    private readonly ContentControl body = new();
    private readonly StartupReadinessPolicy startup = new();
    private TaskCompletionSource rendererReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool safeRendering;
    public bool RestartInSafeRendering { get; private set; }
    private readonly WindowTheme windowTheme;
    private readonly System.Drawing.Icon trayIcon = LoadTrayIcon();
    private readonly IDesktopBackend backend;
    private readonly PreviewGridService previews;
    private bool sessionLocked;
    private void OnSessionSwitch(object sender,Microsoft.Win32.SessionSwitchEventArgs args)
    {
        Dispatcher.BeginInvoke(()=>{
            if(args.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect or Microsoft.Win32.SessionSwitchReason.SessionLogoff)sessionLocked=true;
            else if(args.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.RemoteConnect or Microsoft.Win32.SessionSwitchReason.ConsoleConnect or Microsoft.Win32.SessionSwitchReason.SessionLogon)sessionLocked=false;
            previews.Stop();
        });
    }
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
    private readonly bool cosmeticPreview;
    private bool draining, readyToClose;
    private int layoutOperations;
    private TaskCompletionSource idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool handlingShortcut;
    private bool openUpdates;
    private bool hostReady;
    public void RequestUpdates(){openUpdates=true;if(hostReady){openUpdates=false;Send(new {version=1,@event="navigate",data=new {page="System",updates=true}});}}
    private string? pendingBadge;
    private bool claimingBadge;
    public void RequestBadgeClaim(string path)
    {
        if(claimingBadge||pendingBadge is not null){Reopen();return;}
        pendingBadge=BadgeActivation.Validate(path);
        if(hostReady)_=ClaimPendingBadgeAsync();
    }
    private async Task ClaimPendingBadgeAsync()
    {
        if(!hostReady||claimingBadge||pendingBadge is null)return;
        string path=pendingBadge;pendingBadge=null;claimingBadge=true;
        try {
            Reopen();
            var state=await backend.ExecuteAsync("support.importBadgeFile",JsonSerializer.SerializeToElement(new {path}),closed.Token);
            var element=JsonSerializer.SerializeToElement(state,json);
            string message=element.TryGetProperty("message",out var text)?text.GetString()??"":"";
            if(message.Contains("manually")||message.Contains("untouched")||message.StartsWith("Recognition already claimed"))MessageBox.Show(this,message,"Account recognition");
        }catch(OperationCanceledException){ }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Account recognition",MessageBoxButton.OK,MessageBoxImage.Information);}
        finally {claimingBadge=false;}
    }
    private RobloxLaunchHelper.UpdateShutdownRequest? updateShutdown;
    private readonly RobloxLaunchHelper.OperationCoordinator operations = new();

    public MainWindow(bool smoke = false,bool safeRendering = false,bool cosmeticPreview = false) : this(null, null, smoke,safeRendering,cosmeticPreview) { }

    internal MainWindow(IDesktopBackend? injectedBackend, string? profile, bool smoke = false,bool safeRendering = false,bool cosmeticPreview = false)
    {
        this.smoke = smoke;this.safeRendering=safeRendering;this.cosmeticPreview=cosmeticPreview;
        isolatedProfile = profile;
        backend = injectedBackend ?? new DesktopBackend(cosmeticPreview?Path.Combine(AppContext.BaseDirectory,"cosmetic-preview-preferences"):smoke ? Path.Combine(AppContext.BaseDirectory, "smoke-preferences") : null);
        previews=new(backend,()=>!sessionLocked && IsVisible && WindowState!=WindowState.Minimized && hostReady,
            data=>Send(new {version=1,@event="previews.frames",data}));
        previews.Start();
        IsVisibleChanged+=(_,_)=>{if(!IsVisible)previews.Stop();};
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)previews.Stop();};
        Microsoft.Win32.SystemEvents.SessionSwitch+=OnSessionSwitch;
        if(backend is DesktopBackend desktopBackend)
        {
            desktopBackend.AccountSignIn=(request,token)=>AccountSignInWindow.ShowAsync(this,request,token);
            desktopBackend.AccountLayoutFocusScope=LaunchFocusGuard.Suspend;
            desktopBackend.AccountCreate=(accounts,progress,token)=>AccountCreationHost.CreateAsync(this,accounts,progress,token);
            desktopBackend.AccountCredentialsOpen=(accounts,id,token)=>AccountCreationHost.CredentialsAsync(this,accounts,id,token);
            desktopBackend.AccountLaunchSession=(account,token)=>AccountLaunchSession.OpenAsync(this,account,token);
        }
        windowTheme = new WindowTheme(this, backend.AppearanceScheme);
        windowTheme.Changed += () => { if (tray?.ContextMenuStrip is Forms.ContextMenuStrip menu) windowTheme.StyleMenu(menu); };
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico"));
        idle.SetResult();
        Title = "Roblox Multi Instance";
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1440, workArea.Width); Height = Math.Min(960, workArea.Height);
        MinWidth = Math.Min(900, workArea.Width); MinHeight = Math.Min(640, workArea.Height); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        body.Content = view;
        Content = body;
        Loaded += async (_, _) => await InitializeAsync();
        Closing += OnClosing;
        Closed += (_, _) => { Microsoft.Win32.SystemEvents.SessionSwitch-=OnSessionSwitch;previews.Dispose();startup.Close();closed.Cancel(); windowTheme.Dispose(); updateShutdown?.Dispose(); shortcuts?.Dispose(); DisposeTray(); trayIcon.Dispose(); view.Dispose(); backend.Dispose(); };
        backend.Event += (operationId, name, data) =>
        {
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke(() => Send(new { version = 1, operationId, @event = name, data }));
        };
    }

    internal string CreationThemeScheme => backend.AppearanceScheme;
    public void Reopen() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }

    private async Task InitializeAsync()
    {
        if(closed.IsCancellationRequested||!startup.Begin(out int attempt))return;
        windowTheme.SetWorkspaceReady(false);hostReady=false;rendererReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
        if(attempt>1){view.Dispose();view=new WebView2();}
        var startupView=view;
        // HwndHost airspace ignores WPF overlays. Reveal only after renderer readiness.
        startupView.Visibility=Visibility.Hidden;
        body.Content=null;var loading=new Grid();loading.Children.Add(view);
        var splash=new StartupLoadingView(windowTheme,Icon);
        loading.Children.Add(splash);body.Content=loading;
        windowTheme.Changed+=splash.RefreshTheme;
        try
        {
            if(smoke)await Dispatcher.InvokeAsync(()=>splash.Capture(Path.Combine(AppContext.BaseDirectory,"host-loading-smoke.png")),System.Windows.Threading.DispatcherPriority.Render);
            await StartupReadinessPolicy.RunBoundedAsync(token=>InitializeBrowserAsync(startupView,attempt,token),StartupReadinessPolicy.Deadline,closed.Token);
            if(startup.Ready(attempt))
            {
                loading.Children.Remove(view);body.Content=view;view.Visibility=Visibility.Visible;windowTheme.SetWorkspaceReady(true);hostReady=true;if(openUpdates)RequestUpdates();_=ClaimPendingBadgeAsync();
            }
        }
        catch(OperationCanceledException) when(closed.IsCancellationRequested) { }
        catch(WebView2RuntimeNotFoundException) { if(startup.Fail(attempt))ShowStartupFailure("Microsoft Edge WebView2 Runtime is required.",true); }
        catch(TimeoutException) { if(startup.Fail(attempt))ShowStartupFailure("The desktop interface did not become ready within 20 seconds.",false); }
        catch { if(startup.Fail(attempt))ShowStartupFailure("The desktop interface could not start. Run the executable from its complete build or installed folder.",false); }
        finally { windowTheme.Changed-=splash.RefreshTheme; }
    }

    private async Task InitializeBrowserAsync(WebView2 startupView,int attempt,CancellationToken token)
    {
            var readiness=rendererReady;
            await RefreshWindowThemeAsync();token.ThrowIfCancellationRequested();
            string assets = Path.Combine(AppContext.BaseDirectory, "web");
            if (!File.Exists(Path.Combine(assets, "index.html"))) throw new FileNotFoundException("Bundled interface missing.");
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobloxMultiInstance", safeRendering?"WebView2-safe-rendering":"WebView2");
            if (smoke) profile = Path.Combine(AppContext.BaseDirectory, "smoke-profile");
            if(cosmeticPreview)profile=Path.Combine(AppContext.BaseDirectory,"cosmetic-preview-profile");
            if (isolatedProfile is not null) profile = isolatedProfile;
            var options=new CoreWebView2EnvironmentOptions();
            if(safeRendering)options.AdditionalBrowserArguments="--disable-gpu";
            var environment = await CoreWebView2Environment.CreateAsync(null, profile,options);
            token.ThrowIfCancellationRequested();
            await startupView.EnsureCoreWebView2Async(environment);
            token.ThrowIfCancellationRequested();
            var core = startupView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping("desktop.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { previews.Stop();if (!BridgeProtocol.IsTrusted(e.Uri)) e.Cancel = true; };
            PreviewResourceRoute.Attach(core,previews,()=>ReferenceEquals(startupView,view));
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.WebMessageReceived += OnMessage;
            core.NavigationCompleted+=(_,navigation)=>{if(!navigation.IsSuccess)readiness.TrySetException(new IOException("Local interface navigation failed."));};
            core.ProcessFailed+=(_,_)=>{if(startup.Fail(attempt)){readiness.TrySetException(new IOException("Desktop browser process stopped."));hostReady=false;ShowStartupFailure("The desktop browser process stopped.",false);}};
            if (smoke) core.NavigationCompleted += async (_, navigation) =>
            {
                if(!ReferenceEquals(startupView,view)||startup.State is "Failed" or "Closed")return;
                try
                {
                    if (!navigation.IsSuccess) throw new IOException("Navigation failed.");
                    await Task.Delay(3000, closed.Token);
                    if(!ReferenceEquals(startupView,view)||startup.State is "Failed" or "Closed")return;
                    string launchProfile=Path.Combine(AppContext.BaseDirectory,"smoke-account-launch",Guid.NewGuid().ToString("N"));
                    AccountStorageSecurity.EnsurePrivateDirectory(launchProfile);
                    await using(var launchSession=await AccountLaunchSession.OpenAsync(this,new("smoke",launchProfile,"101"),closed.Token))
                    {
                        if(launchSession.Cookie is not null)throw new InvalidOperationException("An empty launch profile contained a session.");
                    }
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "host-theme-smoke.json"), JsonSerializer.Serialize(windowTheme.CheckCaptionColors(), json));
                    await windowTheme.CheckCaptionControlsAsync();
                    windowTheme.CaptureCaption(Path.Combine(AppContext.BaseDirectory, "host-chrome-smoke.png"));
                    using var file = File.Create(Path.Combine(AppContext.BaseDirectory, "host-smoke.png"));
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "host-smoke.txt"), "WebView2 initialized; local navigation completed; screenshot captured. No helper or maintenance action requested.");
                }
                catch { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "host-smoke.txt"), "Host smoke failed."); }
                finally { if(ReferenceEquals(startupView,view)&&startup.State is "Starting" or "Ready"){exiting = true; Close();} }
            };
            updateShutdown??=new RobloxLaunchHelper.UpdateShutdownRequest(()=>Dispatcher.InvokeAsync(()=>{exiting=true;Close();}).Task);
            shortcuts?.Dispose();shortcuts = new ShortcutOwner(this);
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
            core.Navigate(BridgeProtocol.Origin + "/index.html"+(cosmeticPreview?"?supporter-preview":""));
            await readiness.Task.WaitAsync(token);
    }

    private void ShowStartupFailure(string message,bool runtimeMissing)
    {
        windowTheme.SetWorkspaceReady(false);
        hostReady=false;shortcuts?.Dispose();shortcuts=null;view.Dispose();
        var panel=new StackPanel {Margin=new Thickness(40)};
        panel.Children.Add(new TextBlock {Text="Desktop startup needs attention",FontSize=24});
        panel.Children.Add(new TextBlock {Text=message,Margin=new Thickness(0,16,0,16),TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock {Text="Your settings and account profiles have been preserved. Close and reopen the app if these attempts fail.",TextWrapping=TextWrapping.Wrap});
        if(runtimeMissing)
        {
            var download=new Button {Content="Open Microsoft’s WebView2 download page",Margin=new Thickness(0,12,0,0),Padding=new Thickness(12),HorizontalAlignment=HorizontalAlignment.Left};
            download.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/"){UseShellExecute=true})?.Dispose();}catch{}};panel.Children.Add(download);
        }
        var retry=new Button {Content=$"Retry startup ({startup.Attempts}/{StartupReadinessPolicy.MaximumAttempts} attempts used)",IsEnabled=startup.CanRetry,Margin=new Thickness(0,12,0,0),Padding=new Thickness(12),HorizontalAlignment=HorizontalAlignment.Left};
        retry.Click+=async (_,_)=>{retry.IsEnabled=false;await InitializeAsync();};panel.Children.Add(retry);
        if(!safeRendering)
        {
            var safe=new Button {Content="Restart with software rendering",Margin=new Thickness(0,12,0,0),Padding=new Thickness(12),HorizontalAlignment=HorizontalAlignment.Left};
            safe.Click+=(_,_)=>{RestartInSafeRendering=true;exiting=true;Close();};panel.Children.Add(safe);
            panel.Children.Add(new TextBlock {Text="Software rendering starts a fresh desktop browser profile and may use more CPU. Existing account sign-in profiles stay intact.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        }
        else panel.Children.Add(new TextBlock {Text="Software rendering is already requested. Windows or administrator WebView2 policy can override browser options.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        var quit=new Button {Content="Close app",Margin=new Thickness(0,12,0,0),Padding=new Thickness(12),HorizontalAlignment=HorizontalAlignment.Left};quit.Click+=(_,_)=>{exiting=true;Close();};panel.Children.Add(quit);body.Content=panel;
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if(startup.State is not ("Starting" or "Ready")||!ReferenceEquals(sender,view.CoreWebView2))return;
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
        bool layout = request.Command == "layouts.apply";
        if(request.Command=="layouts.preview")cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        if (layout) { layoutOperations++; Send(new { version = 1, operationId = request.Id, @event = "host.busy", data = new { busy = true } }); }
        using var layoutFocusScope=layout?LaunchFocusGuard.Suspend():null;
        var launchFocus = request.Command is "accounts.launchBatch" or "launch" or "launch.start" ? LaunchFocusGuard.Start(this, closed.Token) : null;
        try
        {
            object? data;
            if(request.Command=="previews.configure") data=await previews.ConfigureAsync(PreviewConfiguration.Parse(request.Payload));
            else if(request.Command=="previews.stop") {await previews.StopAsync();data=new {stopped=true};}
            else if(request.Command=="host.ready") { rendererReady.TrySetResult();data=new {ready=true}; }
            else if (request.Command == "host.tray")
            {
                bool enabled = request.Payload.GetProperty("enabled").GetBoolean();
                SetTray(enabled); data = new { enabled };
            }
            else if (request.Command == "host.shortcutCapture")
            {
                shortcuts!.Capture(request.Payload.GetProperty("enabled").GetBoolean());
                if (shortcuts.Enabled) SetTray(true);
                PublishShortcutState(); data = new { captured = true };
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
                PublishShortcutState(); data = new { enabled = shortcuts?.Enabled == true, conflicts, outcome = conflicts.Length == 0 ? "Succeeded" : shortcuts?.Enabled == true ? "PartiallySucceeded" : "Failed", message = conflicts.Length > 0 ? string.Join(" ", conflicts) : enabled ? "Global shortcuts enabled." : "Global shortcuts disabled." };
            }
            else if (request.Command == "host.state") data = new { tray = tray is not null, shortcuts = shortcuts?.Enabled == true, status = shortcuts?.Status ?? "disabled", registered = shortcuts?.Registered ?? 0, total = shortcuts?.Total ?? 0 };
            else if(request.Command == "support.importBadge")
            {
                var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Account recognition (*.rmibadge)|*.rmibadge",CheckFileExists=true,Multiselect=false};
                data=dialog.ShowDialog(this)==true
                    ? await backend.ExecuteAsync("support.importBadgeFile",JsonSerializer.SerializeToElement(new {path=dialog.FileName}),cancellation.Token)
                    : await backend.ExecuteAsync("support.badges",JsonSerializer.SerializeToElement(new {}),cancellation.Token);
            }
            else if(request.Command is "support.import" or "support.export")
            {
                bool importing=request.Command=="support.import";
                string? receiptPath=null;
                if(importing)
                {
                    var dialog=new Microsoft.Win32.OpenFileDialog { Filter="Supporter receipt (*.rmisupport)|*.rmisupport",CheckFileExists=true,Multiselect=false };
                    if(dialog.ShowDialog(this)==true)receiptPath=dialog.FileName;
                }
                else
                {
                    var dialog=new Microsoft.Win32.SaveFileDialog { Filter="Supporter receipt (*.rmisupport)|*.rmisupport",FileName="RobloxMultiInstance-supporter.rmisupport",DefaultExt=".rmisupport",AddExtension=true,OverwritePrompt=true };
                    if(dialog.ShowDialog(this)==true)receiptPath=dialog.FileName;
                }
                data=receiptPath is null
                    ? await backend.ExecuteAsync("support.get",JsonSerializer.SerializeToElement(new {}),cancellation.Token)
                    : await backend.ExecuteAsync(importing?"support.importFile":"support.exportFile",JsonSerializer.SerializeToElement(new {path=receiptPath}),cancellation.Token);
            }
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
                if (shortcuts?.Requested == true && request.Command is "settings.save" or "shortcuts.save" or "settings.reset" or "settings.import")
                {
                    var state = JsonSerializer.SerializeToElement(await backend.ExecuteAsync("settings.get", JsonSerializer.SerializeToElement(new { }), cancellation.Token), json);
                    shortcuts.Enable(state.GetProperty("shortcuts"));
                    if (shortcuts.Enabled) SetTray(true);
                    PublishShortcutState();
                }
            }
            Send(new { version = 1, id = request.Id, data });
            if(data is RobloxMultiInstance.OperationResultDto result && result.Code is "SETUP_HANDOFF" or "UNINSTALL_REQUESTED")_ = Dispatcher.BeginInvoke(()=>{exiting=true;Close();});
        }
        catch (OperationCanceledException) { SendError(request.Id, "canceled", "Operation canceled."); }
        catch (System.IO.InvalidDataException) when(request.Command.StartsWith("updates.",StringComparison.Ordinal)) { SendError(request.Id,"update_verification_failed","Update metadata or payload verification failed. Check again and download a fresh copy; setup was not started."); }
        catch (RobloxLaunchHelper.UserError ex) { SendError(request.Id,"update_unavailable",ex.Message); }
        catch (DesktopOperationException ex) { SendError(request.Id, ex.Code, ex.Message); }
        catch (Exception ex)
        {
            if(ex is not ArgumentException && backend is DesktopBackend desktopBackend)desktopBackend.RecordTechnicalFailure(ex.HResult);
            // Backend supplies safe user-facing errors; never expose arbitrary native exception details.
            string message = ex is ArgumentException ? "The requested action or its options are invalid." : "The action could not complete. Refresh the page or check System health and try again.";
            SendError(request.Id, "operation_failed", message);
        }
        finally
        {
            if (layout) { layoutOperations--; Send(new { version = 1, operationId = request.Id, @event = "host.busy", data = new { busy = layoutOperations > 0 } }); }
            if(cancellation.IsCancellationRequested)launchFocus?.Dispose();
            else launchFocus?.Complete();
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

    private void PublishShortcutState() => Send(new { version = 1, operationId = "shortcuts", @event = "host.shortcutState", data = new { enabled = shortcuts?.Enabled == true, status = shortcuts?.Status ?? "disabled", registered = shortcuts?.Registered ?? 0, total = shortcuts?.Total ?? 0, conflicts = shortcuts?.Conflicts ?? [], tray = tray is not null } });

    private void DisposeTray()
    {
        if (tray is null) return;
        var menu = tray.ContextMenuStrip;
        tray.Dispose(); tray = null;
        menu?.Dispose();
    }

    private void SetTray(bool enabled)
    {
        if (!enabled) { if (!IsVisible) Reopen(); shortcuts?.Disable(); DisposeTray(); PublishShortcutState(); return; }
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
        shortcuts?.Disable();
        // Always unwind the first Closing event before attempting the final Close.
        await Task.Yield();
        await previews.StopAsync();
        closed.Cancel();
        await idle.Task;
        try { await backend.StopBackgroundCleanupAsync(); } catch { }
        readyToClose = true;
        Close();
    }
    private void SendError(string id, string code, string message) => Send(new { version = 1, id, error = new { code, message } });
    private void Send(object value)
    {
        if (!closed.IsCancellationRequested && (startup.State is "Starting" or "Ready") && view.CoreWebView2 is not null)
            view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, json));
    }
}
