using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace RobloxLaunchHelper;

internal static class UpdateAccess
{
    const string Target="RobloxMultiInstance/GitHubUpdates/"+UpdateUrls.Repository;
    static string Preferences=>Path.Combine(UpdateFiles.Root,"access.json");
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Credential { public uint Flags,Type; public string? TargetName,Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist,AttributeCount; public IntPtr Attributes; public string? TargetAlias,UserName; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct PromptInfo { public int Size; public IntPtr Owner; public string Message,Caption; public IntPtr Banner; }
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Read(string target,uint type,uint flags,out IntPtr value);
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Write(ref Credential value,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Delete(string target,uint type,uint flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr value);
    [DllImport("credui.dll",EntryPoint="CredUIPromptForCredentialsW",CharSet=CharSet.Unicode)] static extern uint Prompt(ref PromptInfo info,string target,IntPtr reserved,uint error,StringBuilder user,uint userSize,StringBuilder password,uint passwordSize,ref bool save,uint flags);
    internal static string? ReadToken()
    {
        if(Program.Elevated)return null;
        if(!Read(Target,1,0,out var pointer))return null;
        try { var value=Marshal.PtrToStructure<Credential>(pointer); if(value.CredentialBlobSize is 0 or >4096)return null;return Marshal.PtrToStringUni(value.CredentialBlob,(int)value.CredentialBlobSize/2); }
        finally { CredFree(pointer); }
    }
    internal static bool Connected=>ReadToken() is not null;
    internal static bool IncludePrereleases { get { try { UpdateFiles.RejectLinks(Preferences);return File.Exists(Preferences)&&new FileInfo(Preferences).Length<100&&JsonSerializer.Deserialize<bool>(File.ReadAllText(Preferences)); } catch { return false; } } }
    internal static void SetPrereleases(bool enabled) { UpdateFiles.EnsureDirectory(UpdateFiles.Root);UpdateFiles.RejectLinks(Preferences);File.WriteAllText(Preferences,JsonSerializer.Serialize(enabled)); }
    internal static void Connect(IntPtr owner)
    {
        if(Program.Elevated)throw new UserError("Configure update access from the normal desktop app.");
        var info=new PromptInfo {Size=Marshal.SizeOf<PromptInfo>(),Owner=owner,Caption="GitHub update access",Message="Enter your GitHub username and a personal access token with read access to this repository. The token stays in Windows Credential Manager and is sent only to api.github.com."};
        var user=new StringBuilder(256);var password=new StringBuilder(2048);bool save=false;
        uint result=Prompt(ref info,Target,IntPtr.Zero,0,user,256,password,2048,ref save,0x40000|0x80|0x2);
        if(result==1223)return;if(result!=0)throw new UserError("Windows could not open the update credential prompt.");
        var text=password.ToString().Trim();password.Clear();
        if(text.Length is <20 or >2047||text.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))throw new UserError("Enter a valid GitHub personal access token.");
        IntPtr blob=Marshal.StringToCoTaskMemUni(text);
        try { var value=new Credential {Type=1,TargetName=Target,CredentialBlobSize=(uint)(text.Length*2),CredentialBlob=blob,Persist=2,UserName="GitHub updates"};if(!Write(ref value,0))throw new UserError("Windows could not save update access."); }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }
    internal static void Disconnect() { if(!Delete(Target,1,0)&&Marshal.GetLastWin32Error()!=1168)throw new UserError("Windows could not remove update access."); }
}
