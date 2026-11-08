using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RobloxMultiInstance.Desktop;

internal static class AccountCreationDialogs
{
    internal static Button Button(Panel panel,string label,Action action)
    {
        var button=new Button {Content=label,Margin=new Thickness(4),Padding=new Thickness(10,6,10,6)};
        button.Click+=(_,_)=>action();panel.Children.Add(button);return button;
    }
    static Window Dialog(Window owner,string title,Panel panel) { var window=new Window {Owner=owner,Title=title,Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Width=620,Height=790,MinWidth=500,MinHeight=400,WindowStartupLocation=WindowStartupLocation.CenterOwner};AccountCreationStyle.Apply(window);return window; }
    static TextBox Field(Panel panel,string label,string value)
    {
        panel.Children.Add(new TextBlock {Text=label,Margin=new Thickness(0,8,0,3)});var field=new TextBox {Text=value};panel.Children.Add(field);return field;
    }
    internal static void RevealPassword(Panel panel,PasswordBox password)
    {
        int index=panel.Children.IndexOf(password);panel.Children.Remove(password);
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});panel.Children.Insert(index,row);
        password.Height=38;var clear=new TextBox {Visibility=Visibility.Collapsed,Height=password.Height,Margin=password.Margin};row.Children.Add(password);row.Children.Add(clear);
        var icon=new System.Windows.Shapes.Path {Width=20,Height=16,Stretch=System.Windows.Media.Stretch.Uniform,StrokeThickness=1.6,Data=System.Windows.Media.Geometry.Parse("M1,8 Q10,-3 19,8 Q10,19 1,8 M13,8 A3,3 0 1 1 7,8 A3,3 0 1 1 13,8")};
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground") {RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(Button),1)});
        var reveal=new Button {Content=icon,ToolTip="Show password",Padding=new Thickness(10,6,10,6),Margin=new Thickness(8,0,0,0),MinWidth=42};Grid.SetColumn(reveal,1);row.Children.Add(reveal);
        bool shown=false;
        void Show(bool value){shown=value;clear.Text=value?password.Password:"";clear.Visibility=value?Visibility.Visible:Visibility.Collapsed;password.Visibility=value?Visibility.Collapsed:Visibility.Visible;string label=value?"Hide password":"Show password";reveal.ToolTip=label;System.Windows.Automation.AutomationProperties.SetName(reveal,label);icon.Data=System.Windows.Media.Geometry.Parse(value?"M1,8 Q10,-3 19,8 Q10,19 1,8 M13,8 A3,3 0 1 1 7,8 A3,3 0 1 1 13,8 M2,1 L18,15":"M1,8 Q10,-3 19,8 Q10,19 1,8 M13,8 A3,3 0 1 1 7,8 A3,3 0 1 1 13,8");}
        clear.TextChanged+=(_,_)=>{if(shown)password.Password=clear.Text;};
        reveal.Click+=(_,_)=>Show(!shown);
        password.IsEnabledChanged+=(_,_)=>{reveal.IsEnabled=password.IsEnabled;if(!password.IsEnabled)Show(false);};
        reveal.IsEnabled=password.IsEnabled;Show(false);
        panel.Unloaded+=(_,_)=>{shown=false;clear.Clear();};
    }
    internal static AccountCreationOptions? Setup(Window owner,int capacity,CancellationToken token)
    {
        var panel=new StackPanel {Margin=new Thickness(28)};var window=Dialog(owner,"Create Roblox accounts",panel);
        panel.Children.Add(new TextBlock {Text="Set up your queue",FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        panel.Children.Add(new TextBlock {Text=$"{capacity} account slots available. Review and submit each signup yourself. Complete any Roblox challenge in the signup window.",TextWrapping=TextWrapping.Wrap});
        var prefix=Field(panel,"Username prefix","");
        var sequence=new Grid();for(int i=0;i<3;i++)sequence.ColumnDefinitions.Add(new ColumnDefinition());panel.Children.Add(sequence);
        TextBox SequenceField(string label,string value,int column){var cell=new StackPanel {Margin=new Thickness(0,0,column<2?12:0,0)};Grid.SetColumn(cell,column);sequence.Children.Add(cell);return Field(cell,label,value);}
        var start=SequenceField("Starting number","1",0);var width=SequenceField("Suffix digits","2",1);var count=SequenceField("Account count","1",2);
        panel.Children.Add(new TextBlock {Text="Your accurate birthdate",Margin=new Thickness(0,16,0,3)});
        var dates=new Grid();for(int i=0;i<3;i++)dates.ColumnDefinitions.Add(new ColumnDefinition());panel.Children.Add(dates);
        TextBox DatePart(string label,int column,int length){var cell=new StackPanel {Margin=new Thickness(0,0,column<2?12:0,0)};Grid.SetColumn(cell,column);dates.Children.Add(cell);var field=Field(cell,label,"");field.MaxLength=length;System.Windows.Automation.AutomationProperties.SetName(field,label);return field;}
        var month=DatePart("Month (1–12)",0,2);var day=DatePart("Day",1,2);var year=DatePart("Year (YYYY)",2,4);
        var shared=new CheckBox {Content="Reuse one password for this queue",Margin=new Thickness(0,12,0,4)};panel.Children.Add(shared);
        var password=new PasswordBox {IsEnabled=false};panel.Children.Add(password);RevealPassword(panel,password);shared.Checked+=(_,_)=>password.IsEnabled=true;shared.Unchecked+=(_,_)=>{password.Clear();password.IsEnabled=false;};
        panel.Children.Add(new TextBlock {Text="Otherwise, a unique password is generated for every account. Supplied credentials are encrypted on this Windows account; a saved session does not prove the password is correct.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
        var preview=new TextBlock {TextWrapping=TextWrapping.Wrap};panel.Children.Add(preview);var error=new TextBlock {TextWrapping=TextWrapping.Wrap};panel.Children.Add(error);
        AccountCreationOptions? result=null;string? reviewed=null;
        AccountCreationOptions Read()
        {
            if(!long.TryParse(start.Text,NumberStyles.None,CultureInfo.InvariantCulture,out long n)||!int.TryParse(width.Text,out int w)||!int.TryParse(count.Text,out int c)||!int.TryParse(month.Text,out int m)||!int.TryParse(day.Text,out int d)||!int.TryParse(year.Text,out int y))throw new ArgumentException("Enter the sequence numbers and your accurate birthdate.");
            return new(prefix.Text.Trim(),n,w,c,new DateOnly(y,m,d),shared.IsChecked==true?password.Password:null);
        }
        var actions=new WrapPanel {Margin=new Thickness(0,12,0,0)};panel.Children.Add(actions);
        Button(actions,"Preview usernames",()=>{try{var o=Read();reviewed=string.Join(", ",o.Preview(capacity));preview.Text=reviewed;error.Text="";}catch{reviewed=null;error.Text="Check the username sequence, count, birthdate, and password (8–128 characters).";}});
        Button(actions,"Start queue",()=>{try{var o=Read();string names=string.Join(", ",o.Preview(capacity));if(reviewed!=names){preview.Text=names;reviewed=names;error.Text="Review the usernames above, then select Start queue again.";return;}result=o;window.Close();}catch{error.Text="Check the username sequence, count, birthdate, and password (8–128 characters).";}});
        Button(actions,"Cancel",window.Close);
        using var cancellation=token.Register(()=>window.Dispatcher.BeginInvoke(new Action(window.Close)));
        window.ShowDialog();password.Clear();return token.IsCancellationRequested?null:result;
    }
    internal static SignupCredential? Edit(Window owner,SignupCredential credential,string? verifiedUsername=null,CancellationToken token=default)
    {
        var panel=new StackPanel {Margin=new Thickness(18)};var window=Dialog(owner,"Confirm supplied credentials",panel);window.Height=390;
        panel.Children.Add(new TextBlock {Text="Confirm the final details used on Roblox. This records supplied credentials; it does not verify the password.",TextWrapping=TextWrapping.Wrap});
        var username=Field(panel,"Username",verifiedUsername??credential.Username);username.IsReadOnly=verifiedUsername is not null;
        panel.Children.Add(new TextBlock {Text="Password",Margin=new Thickness(0,8,0,3)});var password=new PasswordBox {Password=credential.Password};panel.Children.Add(password);
        var notice=new TextBlock();panel.Children.Add(notice);SignupCredential? result=null;
        Button(panel,"Confirm",()=>{if(!System.Text.RegularExpressions.Regex.IsMatch(username.Text,@"\A[A-Za-z0-9_]{3,20}\z")||password.Password.Length is <8 or >128||password.Password.Any(char.IsControl)){notice.Text="Check the username and password.";return;}result=new(username.Text,password.Password,true);window.Close();});Button(panel,"Cancel",window.Close);
        using var cancellation=token.Register(()=>window.Dispatcher.BeginInvoke(new Action(window.Close)));
        window.ShowDialog();password.Clear();return token.IsCancellationRequested?null:result;
    }
    internal static void View(Window owner,SignupCredential credential,CancellationToken token=default)
    {
        var panel=new StackPanel {Margin=new Thickness(18)};var window=Dialog(owner,"Supplied credentials",panel);window.Height=350;
        panel.Children.Add(new TextBlock {Text="These are supplied credentials, not a verified password. They are protected for this Windows user.",TextWrapping=TextWrapping.Wrap});
        Field(panel,"Username",credential.Username).IsReadOnly=true;
        var masked=new PasswordBox {Password=credential.Password,Height=38,Margin=new Thickness(0,12,0,8)};panel.Children.Add(masked);
        var clear=new TextBox {Visibility=Visibility.Collapsed,IsReadOnly=true,Height=masked.Height,Margin=masked.Margin};panel.Children.Add(clear);
        Button(panel,"Reveal / hide",()=>{bool reveal=clear.Visibility!=Visibility.Visible;clear.Text=reveal?masked.Password:"";clear.Visibility=reveal?Visibility.Visible:Visibility.Collapsed;masked.Visibility=reveal?Visibility.Collapsed:Visibility.Visible;});
        var notice=new TextBlock();panel.Children.Add(notice);
        Button(panel,"Copy password",()=>{try{CopyTemporarily(masked.Password);notice.Text="Copied. Clipboard clears after 30 seconds if unchanged.";}catch{notice.Text="Clipboard unavailable. Try again.";}});
        Button(panel,"Close",window.Close);
        using var cancellation=token.Register(()=>window.Dispatcher.BeginInvoke(new Action(window.Close)));
        window.ShowDialog();masked.Clear();clear.Clear();
    }
    static void CopyTemporarily(string value)
    {
        Clipboard.SetText(value);var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(30)};
        timer.Tick+=(_,_)=>{timer.Stop();try{if(Clipboard.ContainsText()&&Clipboard.GetText()==value)Clipboard.Clear();}catch{}value="";};timer.Start();
    }
}
