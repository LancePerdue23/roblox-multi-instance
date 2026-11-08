using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RobloxMultiInstance.Desktop;

internal static class AccountCreationHost
{
    internal static async Task<AccountsStateDto> CreateAsync(Window owner,AccountManager accounts,Action<AccountCreationProgressDto> progress,CancellationToken token)
    {
        if(!owner.Dispatcher.CheckAccess())return await(await owner.Dispatcher.InvokeAsync(()=>CreateAsync(owner,accounts,progress,token)));
        var state=accounts.State();if(!state.Available)throw new InvalidOperationException("Local account storage is unavailable.");
        accounts.Credentials.Reconcile(state.Accounts);
        var options=AccountCreationDialogs.Setup(owner,100-state.Accounts.Length,token);if(options is null){progress(new(0,0,0,"Canceled"));return accounts.State();}
        var coordinator=new AccountCreationCoordinator(accounts);
        try
        {
            return await coordinator.RunAsync(options,(request,credential,birthdate,ct)=>AccountCreationWindow.ShowAsync(owner,request,credential,options,accounts.Credentials,coordinator,ct),progress,token,
                ()=>Task.FromResult(!token.IsCancellationRequested&&MessageBox.Show(owner,"The verified account could not be saved locally. Retry saving? Signup will not be submitted again. Cancel preserves pending credentials for recovery.","Account storage needs attention",MessageBoxButton.OKCancel)==MessageBoxResult.OK));
        }
        catch(OperationCanceledException){return accounts.State();}

    }
    internal static async Task CredentialsAsync(Window owner,AccountManager accounts,string id,CancellationToken token)
    {
        if(!owner.Dispatcher.CheckAccess()){await(await owner.Dispatcher.InvokeAsync(()=>CredentialsAsync(owner,accounts,id,token)));return;}
        token.ThrowIfCancellationRequested();var state=accounts.State();if(!state.Available)throw new InvalidOperationException("Account storage is unavailable.");
        accounts.Credentials.Reconcile(state.Accounts);
        if(id.Length>0)
        {
            if(!state.Accounts.Any(a=>a.Id==id))throw new ArgumentException("Select a saved account.");
            var credential=accounts.Credentials.Get(id)??throw new InvalidOperationException("No supplied credentials are stored for this account.");
            AccountCreationDialogs.View(owner,credential,token);return;
        }
        var panel=new StackPanel {Margin=new Thickness(16)};
        var window=new Window {Owner=owner,Title="Unfinished signups",Width=650,Height=550,Content=new ScrollViewer {Content=panel},WindowStartupLocation=WindowStartupLocation.CenterOwner};
        bool recovering=false;
        window.Closing+=(_,e)=>{if(recovering)e.Cancel=true;};
        panel.Children.Add(new TextBlock {Text="These signups may or may not have completed on Roblox. No queue will resume automatically. Sign in to recover a created account, or view and remove the supplied credentials.",TextWrapping=TextWrapping.Wrap});
        var pending=accounts.Credentials.Pending();if(pending.Length==0)panel.Children.Add(new TextBlock {Text="No unfinished signups."});
        foreach(var entry in pending)
        {
            var row=new StackPanel {Margin=new Thickness(0,12,0,0)};panel.Children.Add(row);row.Children.Add(new TextBlock {Text=entry.Credential.Username});
            AccountCreationDialogs.Button(row,"View supplied credentials",()=>AccountCreationDialogs.View(window,entry.Credential,token));
            var recover=AccountCreationDialogs.Button(row,"Open sign-in",()=>{});
            recover.Click+=async(_,_)=>
            {
                recovering=true;panel.IsEnabled=false;
                try
                {
                    string? recoveredId=null;
                    await accounts.ConnectAsync(null,"",async(request,ct)=>
                    {
                        var identity=await AccountSignInWindow.ShowAsync(window,request,ct);if(identity is null)return null;
                        var confirmed=AccountCreationDialogs.Edit(window,entry.Credential,identity.Username,token);if(confirmed is null)return null;
                        accounts.Credentials.SavePending(request.Id,confirmed);recoveredId=request.Id;return identity;
                    },token,commitVerified:true);
                    if(recoveredId is not null){accounts.Credentials.Finalize(recoveredId);accounts.Credentials.DeletePending(entry.Id);panel.Children.Remove(row);}
                }
                catch(OperationCanceledException){}
                catch{MessageBox.Show(window,"The account could not be saved. Supplied credentials were preserved. Retry sign-in or restart the app.","Recovery needs attention");}
                finally{recovering=false;panel.IsEnabled=true;if(token.IsCancellationRequested)window.Close();}
            };
            AccountCreationDialogs.Button(row,"Delete pending credentials",()=>{if(MessageBox.Show(window,"Delete this local credential record? This does not delete an account on Roblox.","Delete supplied credentials",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;try{accounts.Credentials.DeletePending(entry.Id);panel.Children.Remove(row);}catch{MessageBox.Show(window,"The credential record could not be deleted.");}});
        }
        AccountCreationDialogs.Button(panel,"Close",window.Close);
        using var cancellation=token.Register(()=>owner.Dispatcher.BeginInvoke(new Action(window.Close)));window.ShowDialog();
    }
}
