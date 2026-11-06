using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RobloxLaunchHelper;

// Local observations only. Never persist account IDs, log contents, or game targets.
internal sealed record SessionEvidence(long UserId,bool Disconnected,int? Reason,DateTimeOffset? DisconnectedUtc);
internal sealed record ReplacedSession(ProcessKey Old,ProcessKey Replacement,DateTimeOffset DisconnectedUtc);

internal sealed class ReplacedSessionCleanup(
    Func<ProcessKey,CancellationToken,SessionEvidence?> read,
    Func<int,IInstanceProcess> open,Func<string,bool> trusted,string sid,int session,
    Func<DateTimeOffset>? now=null)
{
    static readonly Regex events=new(@"(?m)^(?<time>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)[^\r\n\[]*\[FLog::(?:(?:GameJoinLoadTime)\][^\r\n]*\buserid:(?<user>[1-9][0-9]{0,18})(?=\s*[,\r\n]|\s*$)|Network\]\s*(?:Disconnect reason received:|Disconnection Notification\. Reason:|Sending disconnect with reason:)\s*(?<reason>[0-9]{1,4})(?=\s*$))",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    readonly Func<DateTimeOffset> clock=now??(()=>DateTimeOffset.UtcNow);

    internal static SessionEvidence? Parse(string text)
    {
        long? user=null;int? reason=null;DateTimeOffset? disconnected=null;
        DateTimeOffset? last=null;
        foreach(Match match in events.Matches(text))
        {
            if(!DateTimeOffset.TryParse(match.Groups["time"].Value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var time))return null;
            if(last is not null&&time<last)return null;
            last=time;
            if(match.Groups["user"].Success)
            {
                if(!long.TryParse(match.Groups["user"].Value,out long value))return null;
                user=value;reason=null;disconnected=null;
            }
            else if(user is not null)
            {
                reason=int.Parse(match.Groups["reason"].Value,CultureInfo.InvariantCulture);disconnected=time;
            }
        }
        return user is long id?new(id,disconnected is not null,reason,disconnected):null;
    }

    internal static SessionEvidence? Read(ProcessKey key,CancellationToken token)
    {
        try
        {
            var observations=InstanceAccounts.ReadLogs(key,token).Select(Parse).ToArray();
            // Multiple held logs are ambiguous. Never choose a convenient account/session.
            if(observations.Length!=1)return null;
            return observations[0];
        }
        catch(OperationCanceledException){throw;}
        catch{return null;}
    }

    internal ReplacedSession[] Candidates(InstanceSnapshot before,InstanceSnapshot after,CancellationToken token)
    {
        if(!before.Available||before.Unavailable>0||!after.Available||after.Unavailable>0)return [];
        var baseline=before.Instances.Select(x=>x.Key).ToHashSet();
        var newer=after.Instances.Where(x=>!baseline.Contains(x.Key)&&x.Windows.Length>0).ToArray();
        if(newer.Length is <1 or >8)return [];
        var connected=new List<(ProcessKey Key,SessionEvidence Evidence)>();
        foreach(var client in newer)
        {
            token.ThrowIfCancellationRequested();var evidence=read(client.Key,token);
            if(evidence is {Disconnected:false})connected.Add((client.Key,evidence));
        }
        var result=new List<ReplacedSession>();
        foreach(var old in after.Instances.Where(x=>baseline.Contains(x.Key)&&x.Windows.Length>0).Take(32))
        {
            token.ThrowIfCancellationRequested();var evidence=read(old.Key,token);
            if(evidence is not {Disconnected:true,Reason:264 or 273,DisconnectedUtc:not null})continue;
            var replacements=connected.Where(x=>x.Evidence.UserId==evidence.UserId&&x.Key.Created>old.Key.Created&&
                evidence.DisconnectedUtc>=DateTime.FromFileTimeUtc(x.Key.Created)&&
                evidence.DisconnectedUtc<=clock().AddSeconds(5)&&clock()-evidence.DisconnectedUtc<=TimeSpan.FromMinutes(2)).ToArray();
            if(replacements.Length==1)result.Add(new(old.Key,replacements[0].Key,evidence.DisconnectedUtc.Value));
        }
        return result.ToArray();
    }

    bool Valid(IInstanceProcess process,ProcessKey key)
    {
        var identity=process.Identity;
        return process.IsAlive&&identity.Created==key.Created&&!identity.Elevated&&InstancePolicy.Matches(identity,sid,session)&&trusted(identity.Image);
    }
    internal bool Execute(ReplacedSession candidate,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if(candidate.Replacement.Created<=candidate.Old.Created||candidate.DisconnectedUtc<DateTime.FromFileTimeUtc(candidate.Replacement.Created)||
                candidate.DisconnectedUtc>clock().AddSeconds(5)||clock()-candidate.DisconnectedUtc>TimeSpan.FromMinutes(2))return false;
            using var old=open(candidate.Old.Id);using var replacement=open(candidate.Replacement.Id);
            if(!Valid(old,candidate.Old)||!Valid(replacement,candidate.Replacement))return false;
            var oldState=read(candidate.Old,token);var newState=read(candidate.Replacement,token);
            if(oldState is not {Disconnected:true,Reason:264 or 273}||oldState.DisconnectedUtc!=candidate.DisconnectedUtc||
                newState is not {Disconnected:false}||oldState.UserId!=newState.UserId)return false;
            var windows=old.Windows();if(windows.Length!=1)return false;
            // Recheck both original handles and evidence before a graceful close request.
            if(!Valid(old,candidate.Old)||!Valid(replacement,candidate.Replacement)||!old.OwnsWindow(windows[0].Handle))return false;
            if(read(candidate.Old,token)!=oldState||read(candidate.Replacement,token)!=newState)return false;
            token.ThrowIfCancellationRequested();
            return old.Close(windows[0].Handle);
        }
        catch(OperationCanceledException){throw;}
        catch{return false;}
    }

    internal static async Task AfterLaunchAsync(InstanceSnapshot baseline,CancellationToken token)
    {
        if(baseline.Instances.Length==0)return;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            await Task.Run(()=>
            {
                using var current=Process.GetCurrentProcess();
                var after=LaunchStartup.Collector().Collect(deadline.Token);
                var cleanup=new ReplacedSessionCleanup(Read,pid=>new InstanceProcess(pid),RobloxTrust.IsSignedRoblox,Program.Sid,current.SessionId);
                foreach(var candidate in cleanup.Candidates(baseline,after,deadline.Token))
                    if(cleanup.Execute(candidate,deadline.Token))Log.Write("replaced-session-close-requested");
            },deadline.Token).WaitAsync(deadline.Token);
        }
        catch { /* Optional cleanup must never turn a successful launch into a failure. */ }
    }
}
