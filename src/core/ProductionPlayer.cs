using System.Text.Json;
using System.Text.RegularExpressions;

namespace RobloxLaunchHelper;

internal static class ProductionPlayer
{
    internal static string Select(string version,string command,IEnumerable<string> installed,Func<string,bool>? exists=null,Func<string,bool>? trusted=null)
    {
        if(!Regex.IsMatch(version,@"\Aversion-(?:[a-fA-F0-9]{16}|[a-fA-F0-9]{32})\z",RegexOptions.CultureInvariant))throw new InvalidDataException("Invalid production player version.");
        string current=Launch.BuildStart(command,null).FileName;
        foreach(string path in new[]{current}.Concat(installed).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(!string.Equals(Path.GetFileName(Path.GetDirectoryName(path)),version,StringComparison.OrdinalIgnoreCase))continue;
            string candidate=PlayerRecovery.WithTarget(command,path);
            if(PlayerRecovery.Valid(candidate,exists,trusted))return candidate;
        }
        throw new UserError("The current production Roblox player is not installed. Close your games and update Roblox normally before launching this batch; no game or installer was started.");
    }
    internal static async Task<string> ResolveAsync(string command,CancellationToken token)
    {
        using var handler=new HttpClientHandler {UseCookies=false,AllowAutoRedirect=false};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(8)};
        using var response=await http.GetAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer",HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(token);
        byte[] bytes=new byte[8193];int count=0,read;
        while(count<bytes.Length&&(read=await stream.ReadAsync(bytes.AsMemory(count),token))>0)count+=read;
        if(count>8192)throw new InvalidDataException("Invalid production player metadata.");
        using var json=JsonDocument.Parse(bytes.AsMemory(0,count));
        return Select(json.RootElement.GetProperty("clientVersionUpload").GetString()??"",command,PlayerRecovery.InstalledPlayers());
    }
}
