using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
namespace RobloxMultiInstance.Desktop;

// Keep native caption controls, snapping, resizing and the system menu.
internal sealed class WindowTheme : IDisposable
{
    private readonly Window window;
    private string scheme;
    public event Action? Changed;
    private System.Windows.Controls.Grid? caption;
    private System.Windows.Controls.Button? maximize;
    private System.Windows.Controls.Border? sidebarCaption;
    public WindowTheme(Window window,string initialScheme) { this.window=window; scheme=initialScheme; window.SourceInitialized+=OnSource; SystemEvents.UserPreferenceChanged+=OnPreference; }
    public void SetScheme(string value) { scheme=value; Apply(); }
    private void OnSource(object? sender,EventArgs e)=>Apply();
    private void OnPreference(object sender,UserPreferenceChangedEventArgs e) { if(!window.Dispatcher.HasShutdownStarted) _=window.Dispatcher.BeginInvoke(Apply); }
    private static bool LightWindows() { try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",0) is int value && value!=0; } catch { return false; } }
    private static int Color(int r,int g,int b)=>r|(g<<8)|(b<<16);
    private (bool Dark,int Background,int Text,int Border) Palette()
    {
        if(SystemParameters.HighContrast)return(false,-1,-1,-1);
        bool light=scheme=="Light"||(scheme=="FollowWindows"&&LightWindows());
        if(scheme=="WarmNeutral")return(false,Color(242,239,233),Color(61,56,47),Color(221,213,201));
        if(scheme=="Purple")return(true,Color(25,19,34),Color(243,236,252),Color(73,57,88));
        if(scheme=="Ocean")return(true,Color(16,29,39),Color(231,244,252),Color(49,83,101));
        if(scheme=="Forest")return(true,Color(18,30,25),Color(234,245,237),Color(58,87,70));
        if(scheme=="Rose")return(false,Color(250,240,243),Color(71,44,56),Color(228,203,214));
        return light?(false,Color(243,245,249),Color(32,40,56),Color(220,226,235)):(true,Color(16,18,22),Color(237,240,246),Color(48,55,68));
    }
    private void Apply()
    {
        nint handle=new WindowInteropHelper(window).Handle;if(handle==0)return;
        // Creating the caption can raise Loaded and refresh the scheme. Resolve colors afterward.
        EnsureFallback();
        var p=Palette();int dark=p.Dark?1:0,background=p.Background,text=p.Text,border=p.Border;
        // Use themed WPF chrome if native caption colors cannot be verified.
        _=DwmSetWindowAttribute(handle,20,ref dark,4);int captionResult=DwmSetWindowAttribute(handle,35,ref background,4);
        _=DwmSetWindowAttribute(handle,36,ref text,4);_=DwmSetWindowAttribute(handle,34,ref border,4);
        // Continue the sidebar surface and divider through the caption.
        if(caption is not null)ApplyFallback(p);
        window.Background=new System.Windows.Media.SolidColorBrush(SystemParameters.HighContrast?SystemColors.WindowColor:System.Windows.Media.Color.FromRgb((byte)(background&255),(byte)((background>>8)&255),(byte)((background>>16)&255)));
        Changed?.Invoke();
    }
    public void StyleMenu(System.Windows.Forms.ContextMenuStrip menu)
    {
        var p=Palette();
        System.Drawing.Color From(int value)=>System.Drawing.Color.FromArgb(value&255,(value>>8)&255,(value>>16)&255);
        menu.BackColor=SystemParameters.HighContrast?System.Drawing.SystemColors.Menu:From(p.Background);
        menu.ForeColor=SystemParameters.HighContrast?System.Drawing.SystemColors.MenuText:From(p.Text);
        foreach(System.Windows.Forms.ToolStripItem item in menu.Items)item.ForeColor=menu.ForeColor;
        menu.ShowImageMargin=false;
        menu.Renderer=SystemParameters.HighContrast?new System.Windows.Forms.ToolStripSystemRenderer():new System.Windows.Forms.ToolStripProfessionalRenderer(new MenuColors(menu.BackColor,From(p.Border),From(scheme=="Purple"?Color(53,41,67):scheme=="Ocean"?Color(34,62,79):scheme=="Forest"?Color(43,68,55):scheme=="Rose"?Color(247,230,237):p.Dark?Color(34,40,51):scheme=="WarmNeutral"?Color(240,234,225):Color(220,226,235))));
    }
    private sealed class MenuColors(System.Drawing.Color background,System.Drawing.Color border,System.Drawing.Color hover):System.Windows.Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground=>background;
        public override System.Drawing.Color MenuBorder=>border;
        public override System.Drawing.Color MenuItemSelected=>hover;
        public override System.Drawing.Color MenuItemBorder=>border;
        public override System.Drawing.Color MenuItemSelectedGradientBegin=>hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd=>hover;
        public override System.Drawing.Color MenuItemPressedGradientBegin=>hover;
        public override System.Drawing.Color MenuItemPressedGradientEnd=>hover;
    }
    private void EnsureFallback()
    {
        if(caption is not null)return;
        var body=window.Content as UIElement;window.Content=null;
        window.WindowStyle=WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(window,new System.Windows.Shell.WindowChrome {
            CaptionHeight=36,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false });
        var root=new System.Windows.Controls.Grid();root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition {Height=new GridLength(36)});root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
        caption=new System.Windows.Controls.Grid();caption.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());caption.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition {Width=GridLength.Auto});
        sidebarCaption=new System.Windows.Controls.Border {HorizontalAlignment=HorizontalAlignment.Left,BorderThickness=new Thickness(0,0,1,0),IsHitTestVisible=false};
        caption.Children.Add(sidebarCaption);
        void SizeSidebarCaption()=>sidebarCaption.Width=window.ActualWidth<=760?72:window.ActualWidth<=1100?190:224;
        window.SizeChanged+=(_,_)=>SizeSidebarCaption();SizeSidebarCaption();
        var label=new System.Windows.Controls.StackPanel {Orientation=System.Windows.Controls.Orientation.Horizontal,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        label.Children.Add(new System.Windows.Controls.Image {Source=window.Icon,Width=16,Height=16,Margin=new Thickness(0,0,8,0)});
        label.Children.Add(new System.Windows.Controls.TextBlock {Text=window.Title,FontSize=12,VerticalAlignment=VerticalAlignment.Center});caption.Children.Add(label);
        var buttons=new System.Windows.Controls.StackPanel {Orientation=System.Windows.Controls.Orientation.Horizontal};System.Windows.Controls.Grid.SetColumn(buttons,1);caption.Children.Add(buttons);
        void Add(string text,string name,Action action) {var button=new System.Windows.Controls.Button {Content=text,ToolTip=name,Width=46,Height=36,FontSize=17,Focusable=true,BorderThickness=new Thickness(0),Padding=new Thickness(0)};System.Windows.Automation.AutomationProperties.SetName(button,name);System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(button,true);button.Click+=(_,_)=>action();buttons.Children.Add(button);if(name=="Maximize")maximize=button;}
        Add("−","Minimize",()=>SystemCommands.MinimizeWindow(window));Add("□","Maximize",()=>{if(window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(window);else SystemCommands.MaximizeWindow(window);});Add("×","Close",()=>SystemCommands.CloseWindow(window));
        window.StateChanged+=(_,_)=>{if(maximize is not null){bool restored=window.WindowState!=WindowState.Maximized;maximize.Content=restored?"□":"❐";maximize.ToolTip=restored?"Maximize":"Restore";System.Windows.Automation.AutomationProperties.SetName(maximize,restored?"Maximize":"Restore");}};
        root.Children.Add(caption);if(body is not null){System.Windows.Controls.Grid.SetRow(body,1);root.Children.Add(body);}window.Content=root;
    }
    private static System.Windows.Media.Brush Brush(int value)=>new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(value&255),(byte)((value>>8)&255),(byte)((value>>16)&255)));
    private void ApplyFallback((bool Dark,int Background,int Text,int Border) p)
    {
        var background=SystemParameters.HighContrast?SystemColors.WindowBrush:Brush(p.Background);
        var foreground=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Brush(p.Text);
        caption!.Background=background;System.Windows.Controls.TextBlock.SetForeground(caption,foreground);
        sidebarCaption!.Background=SystemParameters.HighContrast?SystemColors.WindowBrush:Brush(scheme=="Purple"?Color(33,26,45):scheme=="Ocean"?Color(20,38,50):scheme=="Forest"?Color(24,40,32):scheme=="Rose"?Color(255,247,249):p.Dark?Color(21,24,29):scheme=="WarmNeutral"?Color(249,246,240):Color(255,255,255));
        sidebarCaption.BorderBrush=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Brush(p.Border);
        var buttons=(System.Windows.Controls.StackPanel)caption.Children[2];
        foreach(System.Windows.Controls.Button button in buttons.Children)
        {
            bool close=button.Content as string=="×";
            var style=new Style(typeof(System.Windows.Controls.Button));style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty,background));style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,foreground));
            var border=new FrameworkElementFactory(typeof(System.Windows.Controls.Border));border.SetValue(System.Windows.Controls.Border.BackgroundProperty,new System.Windows.Data.Binding("Background") {RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
            var presenter=new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);style.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty,new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button)){VisualTree=border}));
            var hover=new Trigger {Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty,SystemParameters.HighContrast?SystemColors.HighlightBrush:close?Brush(Color(196,43,28)):Brush(scheme=="Purple"?Color(53,41,67):scheme=="Ocean"?Color(34,62,79):scheme=="Forest"?Color(43,68,55):scheme=="Rose"?Color(247,230,237):p.Dark?Color(34,40,51):scheme=="WarmNeutral"?Color(240,234,225):Color(220,226,235))));hover.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,SystemParameters.HighContrast?SystemColors.HighlightTextBrush:close?System.Windows.Media.Brushes.White:foreground));style.Triggers.Add(hover);
            var focus=new Trigger {Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty,SystemColors.HighlightBrush));focus.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,SystemColors.HighlightTextBrush));style.Triggers.Add(focus);button.Style=style;
        }
    }
    public async System.Threading.Tasks.Task CheckCaptionControlsAsync()
    {
        if(caption is null)return;
        var buttons=(System.Windows.Controls.StackPanel)caption.Children[2];
        ((System.Windows.Controls.Button)buttons.Children[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await System.Threading.Tasks.Task.Delay(150);
        if(window.WindowState!=WindowState.Maximized)throw new InvalidOperationException("Maximize did not respond.");
        ((System.Windows.Controls.Button)buttons.Children[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await System.Threading.Tasks.Task.Delay(150);
        if(window.WindowState!=WindowState.Normal)throw new InvalidOperationException("Restore did not respond.");
        ((System.Windows.Controls.Button)buttons.Children[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await System.Threading.Tasks.Task.Delay(150);
        if(window.WindowState!=WindowState.Minimized)throw new InvalidOperationException("Minimize did not respond.");
        SystemCommands.RestoreWindow(window);await System.Threading.Tasks.Task.Delay(150);
    }
    public void CaptureCaption(string path)
    {
        if(caption is null)return;
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)caption.ActualWidth,36,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(caption);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=System.IO.File.Create(path);encoder.Save(output);
    }
    public object[] CheckCaptionColors()
    {
        string saved=scheme;
        try { var rows=new System.Collections.Generic.List<object>();foreach(string value in new[]{"Dark","Light","WarmNeutral","FollowWindows","Purple","Ocean","Forest","Rose"}) {
            SetScheme(value);window.UpdateLayout();CaptureCaption(System.IO.Path.Combine(AppContext.BaseDirectory,"host-chrome-"+value+"-smoke.png"));var expected=Palette();nint h=new WindowInteropHelper(window).Handle;
            int result=DwmGetWindowAttribute(h,35,out int actual,4),textResult=DwmGetWindowAttribute(h,36,out int actualText,4);
            if(result==0&&actual!=expected.Background||textResult==0&&actualText!=expected.Text)throw new InvalidOperationException("Caption theme mismatch.");
            rows.Add(new {scheme=value,supported=result==0,fallback=caption is not null,background=actual,text=actualText}); }return rows.ToArray(); }
        finally { SetScheme(saved); }
    }
    public void Dispose(){window.SourceInitialized-=OnSource;SystemEvents.UserPreferenceChanged-=OnPreference;}
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(nint hwnd,int attribute,out int value,int size);
}
