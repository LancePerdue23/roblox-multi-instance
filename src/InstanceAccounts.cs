using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Text.Json;
namespace RobloxLaunchHelper;

// Account names are transient UI data. Never write log contents or names to diagnostics.
internal sealed class InstanceAccounts(
    Func<ProcessKey,CancellationToken,IEnumerable<string>>? readLogs = null,
    Func<long,CancellationToken,string?>? lookup = null,
    Func<DateTime>? now = null)
{
    private readonly Func<DateTime> clock = now ?? (() => DateTime.UtcNow);
    private static readonly HttpClient client = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 64 * 1024 };
    private readonly Dictionary<ProcessKey,(DateTime Next,string? Name)> cache=[];
    private static readonly Regex userId = new(@"\[FLog::GameJoinLoadTime\][^\r\n]*\buserid:([1-9][0-9]{0,18})(?=\s*[,\r\n]|\s*$)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static readonly Regex username=new("\"UserName\"\\s*:\\s*\"([A-Za-z0-9_]{3,20})\"",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal string? Resolve(ProcessKey key,CancellationToken token)
    {
        if(cache.TryGetValue(key,out var saved) && saved.Next>clock()) return saved.Name;
        string? name=null;
        try
        {
            foreach(var text in (readLogs ?? ReadLogs)(key,token))
            {
                token.ThrowIfCancellationRequested();
                name=Parse(text);
                if(name is null && ParseUserId(text) is long id) name=(lookup ?? Lookup)(id,token);
                if(name is not null) break;
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested) { /* Profile request timed out. */ }
        catch(OperationCanceledException) { throw; }
        catch { /* Unavailable logs never prevent discovery or expose exception data. */ }
        cache[key]=(clock().AddSeconds(name is null?10:30),name);
        return name;
    }
    private static IEnumerable<string> ReadLogs(ProcessKey key,CancellationToken token)
    {
        var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Roblox","logs");
        foreach(var file in OwnedLogs(directory,key,token))
        {
            string text;
            try
            {
                using var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                text=ReadSegment(stream,0);
                if(stream.Length>4*1024*1024) text+="\n"+ReadSegment(stream,Math.Max(0,stream.Length-512*1024));
            }
            catch { continue; } // A rotating/unreadable log must not hide other owned logs.
            yield return text;
        }
    }
    internal static long? ParseUserId(string text)
    {
        var matches=userId.Matches(text);
        return matches.Count>0 && long.TryParse(matches[^1].Groups[1].Value,out long id)?id:null;
    }
    private static string? Lookup(long id,CancellationToken token)
    {
        // Public profile only: no cookies, tickets, credentials, or persisted account data.
        using var response=client.GetAsync($"https://users.roblox.com/v1/users/{id}",token).GetAwaiter().GetResult();
        if(!response.IsSuccessStatusCode) return null;
        using var json=JsonDocument.Parse(response.Content.ReadAsByteArrayAsync(token).GetAwaiter().GetResult());
        return ParseProfile(json.RootElement,id);
    }
    internal static string? ParseProfile(JsonElement profile,long id)
    {
        if(!profile.TryGetProperty("id",out var value) || !value.TryGetInt64(out long actual) || actual!=id ||
            !profile.TryGetProperty("name",out var name) || name.ValueKind!=JsonValueKind.String) return null;
        string? result=name.GetString();
        return result is not null && Regex.IsMatch(result,@"\A[A-Za-z0-9_]{3,20}\z",RegexOptions.CultureInvariant)?result:null;
    }
    internal void Retain(HashSet<ProcessKey> live)
    { foreach(var key in cache.Keys.Where(k=>!live.Contains(k)).ToArray()) cache.Remove(key); }
    private static string ReadSegment(FileStream stream,long offset)
    {
        stream.Seek(offset,SeekOrigin.Begin);
        byte[] bytes=new byte[offset==0?4*1024*1024:512*1024]; int count=0,read;
        while(count<bytes.Length && (read=stream.Read(bytes,count,bytes.Length-count))>0) count+=read;
        return Encoding.UTF8.GetString(bytes,0,count);
    }
    internal static string? Parse(string text)
    {
        // Roblox's join metadata may encode JSON punctuation inside the local log.
        text=Uri.UnescapeDataString(text).Replace("\\\"","\"");
        var matches=username.Matches(text);
        return matches.Count==0?null:matches[^1].Groups[1].Value;
    }
    private static IEnumerable<FileInfo> OwnedLogs(string directory,ProcessKey key,CancellationToken token)
    {
        using var process=Native.OpenProcess(0x1040,false,key.Id);
        if(process.IsInvalid || Native.GetIdentity(process,key.Id).Created!=key.Created) yield break;
        // Determine the File object type without depending on an arbitrary old Roblox log
        // still being readable after clients exit and rotate their logs.
        using var marker=File.OpenHandle(Environment.ProcessPath!,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        var snapshot=Native.Snapshot();
        var fileType=snapshot.FirstOrDefault(e=>e.ProcessId==Environment.ProcessId && e.Handle==marker.DangerousGetHandle()).Type;
        foreach(var entry in snapshot.Where(e=>e.ProcessId==key.Id && e.Type==fileType).Take(4096))
        {
            token.ThrowIfCancellationRequested();
            if(!DuplicateHandle(process,entry.Handle,GetCurrentProcess(),out var copy,0,false,2)) continue;
            string? path=null;
            try
            {
                if(GetFileType(copy)!=1) continue; // Never query pipe names.
                var name=new StringBuilder(32768);
                uint length=GetFinalPathNameByHandle(copy,name,(uint)name.Capacity,0);
                if(length>0 && length<name.Capacity) path=name.ToString();
            }
            finally { Native.CloseHandle(copy); }
            if(path is null) continue;
            if(path.StartsWith("\\\\?\\",StringComparison.Ordinal)) path=path[4..];
            if(!string.Equals(Path.GetDirectoryName(path),directory,StringComparison.OrdinalIgnoreCase)) continue;
            string filename=Path.GetFileName(path);
            if(!filename.Contains("_Player_",StringComparison.Ordinal) || filename.Contains("CrashHandler",StringComparison.OrdinalIgnoreCase) || !filename.EndsWith(".log",StringComparison.OrdinalIgnoreCase)) continue;
            if(Native.GetIdentity(process,key.Id).Created!=key.Created) yield break;
            yield return new FileInfo(path);
        }
    }
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool DuplicateHandle(Microsoft.Win32.SafeHandles.SafeProcessHandle process,nint source,nint target,out nint copy,uint access,bool inherit,uint options);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(nint file);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern uint GetFinalPathNameByHandle(nint file,StringBuilder path,uint capacity,uint flags);
}
