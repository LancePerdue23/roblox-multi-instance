using Microsoft.Win32;
namespace RobloxLaunchHelper;
internal static class BadgeFileAssociation
{
    internal const string ProgId="RobloxMultiInstance.Badge";
    internal static void Apply(RegistryKey classes,string executable,bool remove)
    {
        string command=$"\"{executable}\" --claim-badge \"%1\"";
        using var prior=classes.OpenSubKey(ProgId+@"\shell\open\command");
        string? existing=prior?.GetValue("") as string;
        if(existing is not null&&!string.Equals(existing,command,StringComparison.OrdinalIgnoreCase))return;
        if(remove){
            if(existing is null)return;
            using var ext=classes.OpenSubKey(".rmibadge",true);
            if(ext?.GetValue("") as string==ProgId)ext.DeleteValue("",false);
            using var openWith=ext?.OpenSubKey("OpenWithProgids",true);openWith?.DeleteValue(ProgId,false);
            using var cmd=classes.OpenSubKey(ProgId+@"\shell\open\command",true);cmd?.DeleteValue("",false);
            return;
        }
        using var extension=classes.CreateSubKey(".rmibadge");
        if(extension.GetValue("") is not string value||string.IsNullOrEmpty(value)||value==ProgId)extension.SetValue("",ProgId);
        using var options=extension.CreateSubKey("OpenWithProgids");options.SetValue(ProgId,Array.Empty<byte>(),RegistryValueKind.None);
        using var app=classes.CreateSubKey(ProgId);app.SetValue("","Multi Instance recognition");
        using var icon=app.CreateSubKey("DefaultIcon");icon.SetValue("",$"\"{executable}\",0");
        using var handler=app.CreateSubKey(@"shell\open\command");handler.SetValue("",command);
    }
}
