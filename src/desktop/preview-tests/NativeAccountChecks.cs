using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using RobloxMultiInstance.Desktop;

internal static class NativeAccountChecks
{
    internal static void Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var panel=new StackPanel();var masked=new PasswordBox {Password="FixturePassword123",IsEnabled=false};panel.Children.Add(masked);
        AccountCreationDialogs.RevealPassword(panel,masked);
        var row=(Grid)panel.Children[0];var clear=row.Children.OfType<TextBox>().Single();var eye=row.Children.OfType<Button>().Single();
        Check(!eye.IsEnabled&&clear.Visibility==Visibility.Collapsed,"Disabled password cannot be revealed");
        masked.IsEnabled=true;
        Check(eye.IsEnabled&&eye.Focusable&&eye.IsTabStop,"Eye is available to keyboard navigation");
        eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(clear.Text=="FixturePassword123"&&masked.Visibility==Visibility.Collapsed&&AutomationProperties.GetName(eye)=="Hide password","Reveal has accessible hide action");
        clear.Text="EditedFixturePassword";
        eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(masked.Password=="EditedFixturePassword"&&clear.Text==""&&masked.Visibility==Visibility.Visible,"Hide preserves edits and clears revealed text");
        eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));masked.IsEnabled=false;
        Check(clear.Text==""&&clear.Visibility==Visibility.Collapsed&&!eye.IsEnabled,"Disabling remasks and clears reveal");
        masked.IsEnabled=true;eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Check(clear.Text==""&&masked.Password=="EditedFixturePassword","Unloading clears visible copy without corrupting stored field");
        Check(!LaunchFocusGuard.IsSuspended,"Focus guard initially enabled");
        var outer=LaunchFocusGuard.Suspend();var inner=LaunchFocusGuard.Suspend();
        Check(LaunchFocusGuard.IsSuspended,"Layout suspends focus restoration");outer.Dispose();outer.Dispose();
        Check(LaunchFocusGuard.IsSuspended,"Nested suspension and double dispose cannot resume early");inner.Dispose();
        Check(!LaunchFocusGuard.IsSuspended,"Layout completion resumes focus restoration");
        try{using var scope=LaunchFocusGuard.Suspend();throw new OperationCanceledException();}catch(OperationCanceledException){}
        Check(!LaunchFocusGuard.IsSuspended,"Canceled layout releases focus suspension");
        Console.WriteLine("Native account password and focus regression checks passed.");
    }
}
