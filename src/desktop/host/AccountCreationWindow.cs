using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RobloxMultiInstance.Desktop;

internal sealed class AccountCreationWindow : Window
{
    readonly WebView2 browser=new();
    readonly TextBlock notice=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(10)};
    readonly TaskCompletionSource<AccountIdentity?> result=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly AccountSignInRequest request;
    readonly AccountCredentialStore credentials;
    readonly AccountCreationOptions options;
    readonly AccountCreationCoordinator coordinator;
    readonly CancellationTokenSource lifetime;
    SignupCredential credential;
    AccountIdentity? identity;
    Task? work;
    bool closing,closed,filled,paused,stop,skip,editing;
    string attention="";
    long number;

    AccountCreationWindow(Window owner,AccountSignInRequest request,SignupCredential credential,AccountCreationOptions options,AccountCredentialStore credentials,AccountCreationCoordinator coordinator,CancellationToken token)
    {
        Owner=owner;this.request=request;this.credential=credential;this.options=options;this.credentials=credentials;this.coordinator=coordinator;
        lifetime=CancellationTokenSource.CreateLinkedTokenSource(token);number=coordinator.LastNumber??options.Start;
        Title="Create account — review and submit on Roblox";Width=1000;Height=850;MinWidth=850;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;TaskbarItemInfo=new();
        var panel=new DockPanel();var controls=new WrapPanel {Margin=new Thickness(12)};DockPanel.SetDock(controls,Dock.Bottom);DockPanel.SetDock(notice,Dock.Top);panel.Children.Add(controls);panel.Children.Add(notice);panel.Children.Add(browser);Content=panel;AccountCreationStyle.Apply(this);
        AccountCreationDialogs.Button(controls,"Open signup",()=>{if(browser.CoreWebView2 is {} core){filled=false;paused=false;attention="";core.Navigate("https://www.roblox.com/CreateAccount");}});
        AccountCreationDialogs.Button(controls,"Refill",()=>{filled=false;paused=false;attention="";});
        AccountCreationDialogs.Button(controls,"Next username",()=>Change(()=>credential with {Username=options.Username(checked(number+1)),Confirmed=false},true));
        AccountCreationDialogs.Button(controls,"Edit details",()=>
        {
            editing=true;
            try{var updated=AccountCreationDialogs.Edit(this,credential,token:lifetime.Token);if(updated is not null)Change(()=>updated with {Confirmed=false});}
            finally{editing=false;}
        });
        AccountCreationDialogs.Button(controls,"View supplied credentials",()=>
        {
            editing=true;
            try{AccountCreationDialogs.View(this,credential,lifetime.Token);}
            finally{editing=false;}
        });
        AccountCreationDialogs.Button(controls,"Retry verification",()=>{paused=false;attention="";});
        AccountCreationDialogs.Button(controls,"Skip account",()=>{skip=true;Close();});
        AccountCreationDialogs.Button(controls,"Stop queue",()=>{stop=true;Close();});
        notice.Text="Review the filled form and submit on Roblox yourself. Complete challenges on Roblox. Use Edit details to change credentials. Confirm the final supplied credentials before saving.";
        Loaded+=(_,_)=>work=RunAsync();
        Closing+=async(_,e)=>
        {
            if(closed)return;e.Cancel=true;if(closing)return;if(identity is null&&!skip)stop=true;closing=true;lifetime.Cancel();await Task.Yield();
            bool clean=await AccountSignInCleanup.RunAsync(work??Task.CompletedTask,
                ()=>identity is null&&browser.CoreWebView2 is {} core?core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile):Task.CompletedTask,
                ()=>browser.Dispose(),TimeSpan.FromSeconds(5));
            if(clean)result.TrySetResult(identity);else result.TrySetException(new InvalidOperationException("Signup browser cleanup did not finish. Pending credentials were preserved; restart before retrying."));
            closed=true;Close();
        };
    }
    void Change(Func<SignupCredential> update,bool increment=false)
    {
        try
        {
            var next=update();
            // A prior form may already have been submitted. Never lose its supplied password when editing a candidate.
            if(filled&&(next.Username!=credential.Username||next.Password!=credential.Password))credentials.SavePending(Guid.NewGuid().ToString("N"),credential with {Confirmed=false});
            credentials.SavePending(request.Id,next);credential=next;
            if(increment){number=checked(number+1);coordinator.LastNumber=number;}
            filled=false;paused=false;attention="";
        }
        catch{paused=true;Notice("Cannot save these signup details. Check the sequence or retry after fixing local storage.","Storage");}
    }
    void Notice(string message,string episode)
    {
        if(attention==episode)return;attention=episode;notice.Text=message;TaskbarItemInfo.ProgressState=TaskbarItemProgressState.Paused;TaskbarItemInfo.ProgressValue=1;
    }
    internal static async Task<AccountIdentity?> ShowAsync(Window owner,AccountSignInRequest request,SignupCredential credential,AccountCreationOptions options,AccountCredentialStore credentials,AccountCreationCoordinator coordinator,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var window=new AccountCreationWindow(owner,request,credential,options,credentials,coordinator,token);
        using var cancellation=token.Register(()=>owner.Dispatcher.BeginInvoke(new Action(()=>{if(!window.closing)window.Close();})));
        try{window.Show();var value=await window.result.Task;if(window.stop&&value is null)throw new OperationCanceledException("Queue stopped.",token);return value;}
        finally{window.credential=new("","");window.lifetime.Dispose();}
    }
    async Task RunAsync()
    {
        try
        {
            AccountStorageSecurity.EnsurePrivateDirectory(request.ProfileDirectory);
            var environment=await CoreWebView2Environment.CreateAsync(null,request.ProfileDirectory);
            if(closing)return;await browser.EnsureCoreWebView2Async(environment);if(closing)return;
            var core=browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled=false;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;
            core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;
            core.NavigationStarting+=(_,e)=>{if(!AccountSignInPolicy.AllowedNavigation(e.Uri))e.Cancel=true;};
            core.FrameNavigationStarting+=(_,e)=>{if(!AccountSignInPolicy.AllowedFrame(e.Uri))e.Cancel=true;};
            core.NewWindowRequested+=(_,e)=>{e.Handled=true;if(AccountSignInPolicy.AllowedNavigation(e.Uri))core.Navigate(e.Uri);};
            core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;core.DownloadStarting+=(_,e)=>e.Cancel=true;
            core.Navigate("https://www.roblox.com/CreateAccount");
            while(!closing)
            {
                await Task.Delay(2000,lifetime.Token);if(paused||editing)continue;
                try
                {
                    if(await VerifyAsync(core))return;if(closing||paused)continue;
                    string script=SignupPageAdapter.Script(filled?"":credential.Username,filled?"":credential.Password,options.Birthdate,!filled);
                    using var document=JsonDocument.Parse(await core.ExecuteScriptAsync(script));
                    string state=document.RootElement.TryGetProperty("state",out var value)?value.GetString()??"Unrecognized":"Unrecognized";
                    switch(state)
                    {
                        case "Filled":filled=true;attention="";TaskbarItemInfo.ProgressState=TaskbarItemProgressState.None;notice.Text="Review and submit on Roblox. Use Edit details above for changes. Final credentials must be confirmed before saving.";break;
                        case "Partial":filled=true;Notice("Some signup fields could not be filled. Your edits will be preserved. Complete the form manually or choose Refill to replace the fields.",state);break;
                        case "Challenge":Notice("Complete the Roblox challenge in this window. The queue is waiting for you.",state);break;
                        case "UsernameRejected":Notice("Username rejected. Choose Next username or Edit details, then refill and submit yourself.",state);break;
                        case "Rejected":Notice("Roblox rejected signup details. Review the form before submitting again.",state);break;
                        case "Unrecognized":Notice("Signup fields were not recognized. Complete signup manually and confirm the final credentials. Refill is available if the form finishes loading.",state);break;
                    }
                }
                catch(OperationCanceledException) when(lifetime.IsCancellationRequested){throw;}
                catch{paused=true;Notice("Verification or page inspection is unavailable. Review Roblox, then choose Retry verification. No signup submission is retried automatically.","Unavailable");}
            }
        }
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch{paused=true;Notice("Signup could not open. Pending credentials remain available in Unfinished signups. Skip or stop the queue.","Startup");}
    }
    async Task<bool> VerifyAsync(CoreWebView2 core)
    {
        var cookies=await core.CookieManager.GetCookiesAsync("https://www.roblox.com/");
        var session=cookies.SingleOrDefault(c=>c.Name==".ROBLOSECURITY"&&c.Domain.TrimStart('.')=="roblox.com"&&c.Path=="/"&&c.IsSecure);
        if(session is null)return false;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var validator=new AccountSessionValidator();var validation=await validator.ValidateAsync(session.Value,null,timeout.Token);
        if(closing||editing)return false;
        if(validation.Status!="Valid"||validation.Identity is null){paused=true;Notice(validation.Status=="RateLimited"?"Roblox is limiting requests. Wait before choosing Retry verification.":"Session verification needs attention. Complete Roblox’s requirements, then choose Retry verification.",validation.Status);return false;}
        if(request.ExcludedRobloxUserIds?.Contains(validation.Identity.RobloxUserId)==true){paused=true;Notice("This account is already saved. Skip this signup or use a different account.","Duplicate");return false;}
        var confirmed=AccountCreationDialogs.Edit(this,credential,validation.Identity.Username,lifetime.Token);
        if(confirmed is null){paused=true;Notice("Choose Retry verification to confirm the final credentials and save this account.","Confirm");return false;}
        // Confirmation can take time; recheck the session before accepting its identity.
        var latest=await core.CookieManager.GetCookiesAsync("https://www.roblox.com/");
        if(closing||!latest.Any(c=>c.Name==".ROBLOSECURITY"&&c.Domain.TrimStart('.')=="roblox.com"&&c.Path=="/"&&c.IsSecure&&c.Value==session.Value))return false;
        credentials.SavePending(request.Id,confirmed);credential=confirmed;identity=validation.Identity;
        // Let the work task complete before Closing waits for it.
        _ = Dispatcher.BeginInvoke(new Action(Close));return true;
    }
}
