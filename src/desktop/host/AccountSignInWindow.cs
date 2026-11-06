using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RobloxMultiInstance.Desktop;

/// <summary>A separate Roblox website surface, with no desktop bridge or injected scripts.</summary>
internal sealed class AccountSignInWindow : Window
{
    readonly WebView2 browser=new();
    readonly TextBlock notice=new() { Text="Sign in on Roblox's website. This window closes automatically once your account is confirmed. Credentials and this browser session stay on this PC.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12) };
    readonly AccountSignInChecks checks=new();
    readonly TaskCompletionSource<AccountIdentity?> result=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource lifetime;
    readonly AccountSignInRequest request;
    AccountIdentity? identity;
    Task? initialization;
    Task? monitoring;
    bool initializing,closing,readyToClose;
    AccountSignInWindow(Window owner,AccountSignInRequest request,CancellationToken token)
    {
        Owner=owner;this.request=request;lifetime=CancellationTokenSource.CreateLinkedTokenSource(token);
        Title="Sign in directly with Roblox";Width=850;Height=800;MinWidth=600;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new DockPanel();var controls=new StackPanel { Orientation=Orientation.Horizontal };
        var cancel=new Button { Content="Cancel",Margin=new Thickness(8),Padding=new Thickness(12,6,12,6) };controls.Children.Add(cancel);
        DockPanel.SetDock(controls,Dock.Bottom);DockPanel.SetDock(notice,Dock.Top);panel.Children.Add(controls);panel.Children.Add(notice);panel.Children.Add(browser);Content=panel;
        cancel.Click+=(_,_)=>Close();Loaded+=async (_,_)=>{initialization=InitializeAsync();await initialization;};
        Closing+=async (_,e)=>
        {
            if(readyToClose)return;
            e.Cancel=true;
            if(closing)return;
            closing=true;lifetime.Cancel();
            await Task.Yield(); // Unwind WPF's first Closing event before calling Close again.
            // Clear browser data before destroying its window, with a bounded wait.
            bool cleaned=await AccountSignInCleanup.RunAsync(Task.WhenAll(initialization??Task.CompletedTask,monitoring??Task.CompletedTask),
                ()=>identity is null&&browser.CoreWebView2 is not null
                    ?browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile):Task.CompletedTask,
                ()=>browser.Dispose(),TimeSpan.FromSeconds(5));
            try
            {
                if(cleaned)result.TrySetResult(identity);
                else result.TrySetException(new InvalidOperationException("The sign-in browser did not finish cleanup. The account was not added; retry sign-in or restart the app before removing its local profile."));
            }
            finally { readyToClose=true;Close(); }
        };
    }
    public static async Task<AccountIdentity?> ShowAsync(Window owner,AccountSignInRequest request,CancellationToken token)
    {
        if(!owner.Dispatcher.CheckAccess())return await (await owner.Dispatcher.InvokeAsync(()=>ShowAsync(owner,request,token)));
        token.ThrowIfCancellationRequested();var window=new AccountSignInWindow(owner,request,token);
        using var cancellation=token.Register(()=>owner.Dispatcher.BeginInvoke(new Action(()=>{if(!window.closing)window.Close();})));
        try { window.Show();return await window.result.Task; }
        finally { window.lifetime.Dispose(); }
    }
    internal static bool AllowedNavigation(string value)
    {
        return AccountSignInPolicy.AllowedNavigation(value);
    }
    async Task InitializeAsync()
    {
        if(initializing)return;initializing=true;
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            AccountStorageSecurity.EnsurePrivateDirectory(request.ProfileDirectory);
            var environment=await CoreWebView2Environment.CreateAsync(null,request.ProfileDirectory);
            if(closing)return;await browser.EnsureCoreWebView2Async(environment);if(closing)return;
            var core=browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled=false;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;
            core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;
            core.NavigationStarting+=(_,e)=>{if(!AllowedNavigation(e.Uri))e.Cancel=true;};
            // Challenge frames may use Roblox's third-party providers. They have no
            // privileged host surface; prohibit local schemes and app origin explicitly.
            core.FrameNavigationStarting+=(_,e)=>
            {
                if(!AccountSignInPolicy.AllowedFrame(e.Uri))e.Cancel=true;
            };
            core.NewWindowRequested+=(_,e)=>{e.Handled=true;if(AllowedNavigation(e.Uri))core.Navigate(e.Uri);};
            core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
            core.DownloadStarting+=(_,e)=>e.Cancel=true;
            core.Navigate("https://www.roblox.com/login");monitoring=MonitorAsync();
        }
        catch(OperationCanceledException) { if(!closing)Close(); }
        catch { if(!closing)notice.Text="Roblox sign-in could not open. Check that WebView2 is installed and retry. No account was added."; }
    }
    async Task MonitorAsync()
    {
        try
        {
            // Yield before the first check so initialization can finish before a
            // saved session causes Close to wait for initialization and monitoring.
            await Task.Delay(500,lifetime.Token);
            while(!closing)
            {
                await VerifyAsync();
                await Task.Delay(2000,lifetime.Token);
            }
        }
        catch(OperationCanceledException) { }
    }
    async Task VerifyAsync()
    {
        if(closing||browser.CoreWebView2 is null||!checks.Begin(Environment.TickCount64))return;
        string status="Unavailable";
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var cookies=await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com/");
            var session=cookies.SingleOrDefault(c=>c.Name==".ROBLOSECURITY"&&c.Domain.TrimStart('.')=="roblox.com"&&c.Path=="/"&&c.IsSecure);
            timeout.Token.ThrowIfCancellationRequested();
            if(session is null) {status="NoSession";return;}
            using var validator=new AccountSessionValidator();
            var validation=await validator.ValidateAsync(session.Value,request.ExpectedRobloxUserId,timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();status=validation.Status;
            if(validation.Status!="Valid"||validation.Identity is null)
            {
                notice.Text=validation.Status switch
                {
                    "WrongIdentity"=>"This is a different Roblox account. Sign in with the saved account, or cancel and add this account separately.",
                    "Expired"=>"This Roblox session has expired. Sign in again; confirmation will happen automatically.",
                    "Challenged"=>"Complete Roblox's security challenge in this window. Your account will then be checked automatically.",
                    "RateLimited"=>"Roblox is limiting session checks. Automatic confirmation will retry after a minute.",
                    _=>"Roblox could not verify this session. Complete sign-in or try again later."
                };
                return;
            }
            string identityId=validation.Identity.RobloxUserId;
            if(request.ExcludedRobloxUserIds?.Contains(identityId)==true)
            { notice.Text="This Roblox account is already saved locally. Sign in with a different account or cancel.";return; }
            // A user can switch accounts while the identity request is in flight.
            // Persist only the profile session whose identity was actually checked.
            var latest=await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com/");
            timeout.Token.ThrowIfCancellationRequested();
            if(!latest.Any(c=>c.Name==".ROBLOSECURITY"&&c.Domain.TrimStart('.')=="roblox.com"&&c.Path=="/"&&c.IsSecure&&c.Value==session.Value))
            {status="NoSession";return;}
            if(closing||lifetime.IsCancellationRequested)return;
            identity=validation.Identity;Close();
        }
        catch(OperationCanceledException) { if(!closing)notice.Text="Confirmation timed out. Your session remains local; it will be checked again automatically."; }
        catch { if(!closing)notice.Text="Roblox identity could not be verified. Complete sign-in and try again. No credentials were sent to the app developer."; }
        finally {checks.Complete(status,Environment.TickCount64);}
    }
}
