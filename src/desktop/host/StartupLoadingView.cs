using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace RobloxMultiInstance.Desktop;

internal sealed class StartupLoadingView : Border
{
    private readonly WindowTheme theme;
    private readonly Border card,track,indicator;
    private readonly TextBlock brand,title,detail;

    internal StartupLoadingView(WindowTheme theme,ImageSource icon)
    {
        this.theme=theme;
        System.Windows.Documents.TextElement.SetFontFamily(this,new FontFamily("Segoe UI"));
        var content=new StackPanel();
        content.Children.Add(new Image {Source=icon,Width=40,Height=40,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,24)});
        brand=new TextBlock {Text="ROBLOX MULTI INSTANCE",FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)};
        title=new TextBlock {Text="Getting things ready",FontSize=26,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
        detail=new TextBlock {Text="Opening your workspace…",FontSize=14,Margin=new Thickness(0,10,0,28),TextWrapping=TextWrapping.Wrap};
        content.Children.Add(brand);content.Children.Add(title);content.Children.Add(detail);
        indicator=new Border {Width=80,CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Left};
        track=new Border {Height=4,CornerRadius=new CornerRadius(2),ClipToBounds=true,Child=indicator};
        content.Children.Add(track);
        card=new Border {MaxWidth=420,Padding=new Thickness(36),Margin=new Thickness(24),CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Center,Child=content};
        Child=card;
        System.Windows.Automation.AutomationProperties.SetName(this,"Opening your workspace");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(this,System.Windows.Automation.AutomationLiveSetting.Polite);
        Loaded+=(_,_)=>{
            if(!SystemParameters.ClientAreaAnimation)return;
            var motion=new TranslateTransform();indicator.RenderTransform=motion;
            motion.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-80,Math.Max(80,track.ActualWidth),TimeSpan.FromSeconds(1.6)) {RepeatBehavior=RepeatBehavior.Forever});
        };
        Unloaded+=(_,_)=>{if(indicator.RenderTransform is TranslateTransform motion)motion.BeginAnimation(TranslateTransform.XProperty,null);};
        RefreshTheme();
    }

    internal void RefreshTheme()
    {
        var colors=theme.StartupColors();
        Background=colors.Background;card.Background=colors.Surface;card.BorderBrush=colors.Border;
        brand.Foreground=colors.Accent;title.Foreground=colors.Text;detail.Foreground=colors.Muted;
        track.Background=colors.Border;indicator.Background=colors.Accent;
    }

    internal void Capture(string path)
    {
        UpdateLayout();
        var bitmap=new RenderTargetBitmap(Math.Max(1,(int)ActualWidth),Math.Max(1,(int)ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(path);encoder.Save(output);
    }
}
