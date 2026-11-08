using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace RobloxMultiInstance.Desktop;

internal static class AccountCreationStyle
{
    internal static void Apply(Window window)
    {
        Window root=window;while(root.Owner is {} parent)root=parent;
        string scheme=root is MainWindow main?main.CreationThemeScheme:"Dark";
        var theme=new WindowTheme(window,scheme);
        window.FontFamily=new System.Windows.Media.FontFamily("Segoe UI");window.FontSize=14;
        var resources=(ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{DynamicResource CreationText}"/><Setter Property="LineHeight" Value="21"/></Style>
          <Style TargetType="Control"><Setter Property="Foreground" Value="{DynamicResource CreationText}"/></Style>
          <Style TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource CreationText}"/><Setter Property="Background" Value="{DynamicResource CreationSurface}"/><Setter Property="BorderBrush" Value="{DynamicResource CreationBorder}"/><Setter Property="Padding" Value="14,10"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Frame" CornerRadius="7" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource CreationAccent}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource CreationAccent}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Frame" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="TextBox"><Setter Property="Background" Value="{DynamicResource CreationSurface}"/><Setter Property="Foreground" Value="{DynamicResource CreationText}"/><Setter Property="CaretBrush" Value="{DynamicResource CreationText}"/><Setter Property="BorderBrush" Value="{DynamicResource CreationBorder}"/><Setter Property="Padding" Value="10,8"/><Setter Property="MinHeight" Value="38"/></Style>
          <Style TargetType="PasswordBox"><Setter Property="Background" Value="{DynamicResource CreationSurface}"/><Setter Property="Foreground" Value="{DynamicResource CreationText}"/><Setter Property="CaretBrush" Value="{DynamicResource CreationText}"/><Setter Property="BorderBrush" Value="{DynamicResource CreationBorder}"/><Setter Property="Padding" Value="10,8"/></Style>
          <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource CreationText}"/><Setter Property="Padding" Value="6,3"/></Style>
        </ResourceDictionary>
        """);
        window.Resources.MergedDictionaries.Add(resources);
        void Refresh(){var c=theme.StartupColors();window.Resources["CreationText"]=c.Text;window.Resources["CreationSurface"]=c.Surface;window.Resources["CreationBorder"]=c.Border;window.Resources["CreationAccent"]=c.Accent;window.Foreground=c.Text;}
        theme.Changed+=Refresh;Refresh();theme.SetWorkspaceReady(false);
        window.Closed+=(_,_)=>theme.Dispose();
    }
}
