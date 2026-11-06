using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RobloxMultiInstance.Desktop;

// A native, non-navigating WebView2 controller opens only the selected profile.
// No scripts, remote content, bridge, cookie export or duplicate secret file.
internal sealed class AccountLaunchSession : IAccountLaunchSession
{
    readonly Window window;
    readonly WebView2 browser;
    readonly CancellationTokenSource lifetime=new();
    public string? Cookie { get; private set; }
    AccountLaunchSession(Window window,WebView2 browser) {this.window=window;this.browser=browser;}

    internal static async Task<IAccountLaunchSession> OpenAsync(Window owner,AccountLaunchContext account,CancellationToken token)
    {
        if(!owner.Dispatcher.CheckAccess())return await (await owner.Dispatcher.InvokeAsync(()=>OpenAsync(owner,account,token)));
        token.ThrowIfCancellationRequested();AccountStorageSecurity.ValidatePath(account.ProfileDirectory);
        if(!System.IO.Directory.Exists(account.ProfileDirectory))throw new InvalidOperationException("Reconnect this account before launching.");
        AccountStorageSecurity.EnsurePrivateDirectory(account.ProfileDirectory);
        var browser=new WebView2 {Visibility=Visibility.Hidden};
        var window=new Window {Owner=owner,Content=browser,Width=1,Height=1,Opacity=0,ShowInTaskbar=false,ShowActivated=false,WindowStyle=WindowStyle.None};
        var session=new AccountLaunchSession(window,browser);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token,session.lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        Task? initialization=null;
        try
        {
            window.Show();
            async Task InitializeAsync()
            {
                var environment=await CoreWebView2Environment.CreateAsync(null,account.ProfileDirectory);
                deadline.Token.ThrowIfCancellationRequested();await browser.EnsureCoreWebView2Async(environment);deadline.Token.ThrowIfCancellationRequested();
                var core=browser.CoreWebView2;
                core.Settings.AreDevToolsEnabled=false;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;
                core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;
                core.NavigationStarting+=(_,e)=>e.Cancel=true;core.NewWindowRequested+=(_,e)=>e.Handled=true;
                core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;core.DownloadStarting+=(_,e)=>e.Cancel=true;
                var cookies=await core.CookieManager.GetCookiesAsync("https://www.roblox.com/");
                deadline.Token.ThrowIfCancellationRequested();session.Cookie=cookies.SingleOrDefault(c=>c.Name==".ROBLOSECURITY"&&c.Domain.TrimStart('.')=="roblox.com"&&c.Path=="/"&&c.IsSecure)?.Value;
            }
            initialization=InitializeAsync();await initialization.WaitAsync(deadline.Token);
            return session;
        }
        catch
        {
            // Ensure initialization never resumes against a disposed controller.
            if(initialization is {IsCompleted:false})_ = initialization.ContinueWith(t=>{_=t.Exception;},TaskScheduler.Default);
            await session.DisposeAsync();throw;
        }
    }
    public async Task PersistCookieAsync(System.Net.Cookie updated,CancellationToken token)
    {
        if(!window.Dispatcher.CheckAccess()) {await (await window.Dispatcher.InvokeAsync(()=>PersistCookieAsync(updated,token)));return;}
        token.ThrowIfCancellationRequested();
        var manager=browser.CoreWebView2.CookieManager;
        string value=updated.Expired?"":updated.Value;
        if(value.Length==0)manager.DeleteCookiesWithDomainAndPath(".ROBLOSECURITY",".roblox.com","/");
        else
        {
            var cookie=manager.CreateCookie(".ROBLOSECURITY",value,".roblox.com","/");cookie.IsSecure=true;cookie.IsHttpOnly=true;
            cookie.Expires=updated.Expires;
            manager.AddOrUpdateCookie(cookie);
        }
        Cookie=value;
        var readback=await manager.GetCookiesAsync("https://www.roblox.com/");
        if(value.Length>0&&!readback.Any(c=>c.Name==".ROBLOSECURITY"&&c.Domain.TrimStart('.')=="roblox.com"&&c.IsSecure&&c.Value==value)
            ||value.Length==0&&readback.Any(c=>c.Name==".ROBLOSECURITY"&&c.Domain.TrimStart('.')=="roblox.com"))throw new InvalidOperationException("Session rotation could not be saved.");
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if(!window.Dispatcher.CheckAccess()) {await window.Dispatcher.InvokeAsync(()=>{Cookie=null;browser.Dispose();window.Close();});return;}
        Cookie=null;browser.Dispose();window.Close();
    }
}
