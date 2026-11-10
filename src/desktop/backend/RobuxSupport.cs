using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RobloxMultiInstance;

public sealed partial class SupportService
{
    // Verified 2026-11-09 against universe 10775556311 (place 132006559657920),
    // owned by MultiInstance group 656550689. Credit is the approved denomination,
    // not an estimate of the historical price paid or the group's net proceeds.
    public static SupportConfiguration ApprovedRobloxConfiguration()=>new("",new(),RobloxPasses:[
        new("2075890740",50),new("2074990581",100),new("2076244661",200),
        new("2075560682",300),new("2074516680",400),new("2075716661",500),
        new("2076124629",700),new("2074156741",1000),new("2074480739",1250),
        new("2074492774",1500),new("2074708731",2000),new("2076184723",2500),
        new("2075872725",3000),new("2074474669",3500),new("2074894638",4000),
        new("2075140665",4500),new("2076214698",5000),new("2076136766",7500),
        new("2075278696",10000),new("2074162732",50000),new("2074942659",100000)]);

    RobloxSupportPass[] GamePasses=>(configuration?.RobloxPasses??[])
        .Where(p=>p is not null&&ValidGamePassId(p.Id)&&p.Credit is >0 and <=100000)
        .Concat(new[]{configuration?.RobloxGamePassId}.Concat(configuration?.RobloxGamePassIds??[])
            .Where(ValidGamePassId).Select(id=>new RobloxSupportPass(id!,0)))
        .DistinctBy(p=>p.Id).OrderBy(p=>p.Credit).ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray();
    string RobloxLedgerPath=>Path.Combine(root,"support-roblox-v2.bin");
    readonly object robloxLedgerLock=new();
    string lastRobloxAccounts="";
    string? lastRobloxMessage;
    DateTimeOffset lastRobloxAttempt,robloxRetryAt;
    DateTimeOffset robloxNextRequestAt;
    TimeSpan robloxRequestSpacing=TimeSpan.FromMilliseconds(100);
    readonly object robloxRateLock=new();
    sealed record RobuxLedger(int Version,string Salt,Dictionary<string,long> Purchases,Dictionary<string,DateTimeOffset>? PendingChecks=null);

    void ApplyRobloxCooldown(HttpResponseMessage response,int attempt=0)
    {
        var retry=response.Headers.RetryAfter;
        var pause=retry?.Delta??(retry?.Date-DateTimeOffset.UtcNow)??TimeSpan.FromSeconds(Math.Pow(2,attempt));
        lock(robloxRateLock){
            var until=DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(pause.TotalSeconds,1,3600));
            if(until>robloxRetryAt)robloxRetryAt=until;
            robloxRequestSpacing=TimeSpan.FromMilliseconds(Math.Min(2000,robloxRequestSpacing.TotalMilliseconds*2));
        }
    }

    // A short, fully validated page ends enumeration. Full or malformed pages never
    // establish absence. Bound both page size and traversal before direct fallback.
    async Task<string[]> ReadOwnedPassPage(string account,string cursor,CancellationToken token)
    {
        await WaitForRobloxSlot(token,()=>{});
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var response=await http.GetAsync($"https://apis.roblox.com/game-passes/v1/users/{account}/game-passes?count=100&exclusiveStartId={cursor}",HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)ApplyRobloxCooldown(response);
        response.EnsureSuccessStatusCode();
        using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
        using var body=new MemoryStream();
        var buffer=new byte[8192];int read;
        while((read=await stream.ReadAsync(buffer,deadline.Token))>0){
            if(body.Length+read>1024*1024)throw new InvalidDataException("Purchase list too large.");
            body.Write(buffer,0,read);
        }
        using var document=JsonDocument.Parse(body.ToArray());
        if(!document.RootElement.TryGetProperty("gamePasses",out var items)||items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()>100)
            throw new InvalidDataException("Invalid purchase list.");
        var result=new List<string>();
        foreach(var item in items.EnumerateArray()){
            if(!item.TryGetProperty("gamePassId",out var id))throw new InvalidDataException("Missing pass ID.");
            string value=id.ValueKind==JsonValueKind.Number&&id.TryGetInt64(out var number)?number.ToString(System.Globalization.CultureInfo.InvariantCulture):id.ValueKind==JsonValueKind.String?id.GetString()!:"";
            if(!ValidGamePassId(value)||result.Contains(value,StringComparer.Ordinal))throw new InvalidDataException("Invalid pass ID.");
            result.Add(value);
        }
        return result.ToArray();
    }

    async Task WaitForRobloxSlot(CancellationToken token,Action throttled)
    {
        while(true){
            TimeSpan wait;bool limited;
            lock(robloxRateLock){
                var now=DateTimeOffset.UtcNow;
                limited=robloxRetryAt>now;
                wait=(robloxRetryAt>robloxNextRequestAt?robloxRetryAt:robloxNextRequestAt)-now;
                if(wait<=TimeSpan.Zero){robloxNextRequestAt=now+robloxRequestSpacing;return;}
            }
            if(limited)throttled();
            await Task.Delay(wait,token);
        }
    }
    public static int RobuxTier(long total)=>total>=1500?2:total>=700?1:total>=300?0:-1;
    public static bool DeveloperToolsEnabled {
        get {
#if LOCAL_SUPPORTER_TOOLS
            return true;
#else
            return false;
#endif
        }
    }

    public async Task<SupportState> ResetRobloxVerificationAsync(CancellationToken token=default)
    {
        await robloxGate.WaitAsync(token);
        try{
            token.ThrowIfCancellationRequested();
            lock(robloxLedgerLock){
                File.Delete(RobloxLedgerPath);File.Delete(RobloxReceiptPath);
                lastRobloxAccounts="";lastRobloxMessage=null;lastRobloxCheck=default;lastRobloxAttempt=default;
                lock(robloxRateLock){robloxRetryAt=default;robloxNextRequestAt=default;robloxRequestSpacing=TimeSpan.FromMilliseconds(100);}
            }
            return State() with {Message="Robux credit and purchase checks reset. Verify a pass to start fresh."};
        }finally{robloxGate.Release();}
    }

    RobuxLedger? ReadRobuxLedger()
    {
        if(!File.Exists(RobloxLedgerPath))return null;
        // Do not silently replace an unreadable ledger: that could lose old credit or salt.
        var bytes=ProtectedData.Unprotect(ReadBounded(RobloxLedgerPath,8*1024*1024),null,DataProtectionScope.CurrentUser);
        var ledger=JsonSerializer.Deserialize<RobuxLedger>(bytes,Json)??throw new InvalidDataException("Purchase history unavailable.");
        if(ledger.Version!=2||Decode(ledger.Salt).Length!=32||ledger.Purchases is null||ledger.Purchases.Count>100000||
            ledger.Purchases.Any(p=>p.Key.Length!=64||p.Key.Any(c=>!char.IsAsciiHexDigitLower(c))||p.Value is <0 or >100000)||
            ledger.PendingChecks is { } pending&&(pending.Count>100000||pending.Keys.Any(k=>k.Length!=64||k.Any(c=>!char.IsAsciiHexDigitLower(c)))))
            throw new InvalidDataException("Purchase history unavailable.");
        return ledger;
    }
    (long Total,int Tier) RobloxCredit()
    {
        lock(robloxLedgerLock){
            try {
                var ledger=ReadRobuxLedger();
                long total=ledger?.Purchases.Values.Sum()??0;
                int tier=RobuxTier(total);
                if(ledger?.Purchases.Values.Any(c=>c==0)==true)tier=Math.Max(tier,0);
                if(File.Exists(RobloxReceiptPath)&&ReadProtected(RobloxReceiptPath)=="verified-gamepass-supporter-v1")tier=Math.Max(tier,0);
                return(total,tier);
            }catch{return(0,-1);}
        }
    }
    static string PairHash(RobuxLedger ledger,string account,string pass)=>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ledger.Salt+":"+account+":"+pass)));

    // Only native connected identities enter here. No cookies or bearer credentials leave the app.
    public async Task<SupportState> CheckRobloxAsync(IEnumerable<string> userIds,bool force=false,CancellationToken token=default,Action<string>? progress=null,string? passId=null)
    {
        var passes=GamePasses;
        if(passId is not null){
            passes=passes.Where(p=>p.Id==passId).ToArray();
            if(passes.Length==0)throw new ArgumentException("Choose a configured donation pass.");
        }
        if(passes.Length==0)return State();
        var ids=userIds.Where(ValidGamePassId).Distinct().Order(StringComparer.Ordinal).ToArray();
        string accountSet=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",",ids)+"|"+string.Join(",",passes.Select(p=>p.Id)))));
        await robloxGate.WaitAsync(token);
        try {
            if(!force&&lastRobloxAccounts==accountSet&&DateTimeOffset.UtcNow-lastRobloxCheck<TimeSpan.FromHours(6))return State();
            if(!force&&lastRobloxAccounts==accountSet&&DateTimeOffset.UtcNow-lastRobloxAttempt<TimeSpan.FromSeconds(15))return State() with {Message=lastRobloxMessage??State().Message};
            if(ids.Length==0){lastRobloxAccounts=accountSet;lastRobloxCheck=DateTimeOffset.MinValue;return State() with {Message="Connect an account to check purchases."};}
            RobuxLedger ledger;
            try { lock(robloxLedgerLock)ledger=ReadRobuxLedger()??new(2,Encode(RandomNumberGenerator.GetBytes(32)),new()); }
            catch {return State() with {Message="Saved purchase history could not be read. It has been preserved."};}
            int unavailable=0,resolved=0,owned=0;
            long totalChecks=(long)ids.Length*passes.Length;
            void Report(string? message=null){try{progress?.Invoke(message??$"Checking purchases: {Volatile.Read(ref resolved)} of {totalChecks} checks complete…");}catch{/* A closing viewer cannot interrupt durable verification. */}}
            Report();
            var jobs=ids.SelectMany(id=>passes.Select(pass=>(Account:id,Pass:pass)));
            using var scanDeadline=CancellationTokenSource.CreateLinkedTokenSource(token);scanDeadline.CancelAfter(TimeSpan.FromSeconds(90));
            try {
            if(passId is null){
                await Parallel.ForEachAsync(ids,new ParallelOptions{MaxDegreeOfParallelism=4,CancellationToken=scanDeadline.Token},async(account,ct)=>{
                    bool NeedsCheck(RobloxSupportPass pass){
                        string pair=PairHash(ledger,account,pass.Id);
                        return !ledger.Purchases.ContainsKey(pair)&&!(ledger.PendingChecks?.TryGetValue(pair,out var expires)==true&&expires>DateTimeOffset.UtcNow&&expires<=DateTimeOffset.UtcNow.AddMinutes(15));
                    }
                    lock(robloxLedgerLock)if(passes.Count(NeedsCheck)<2)return;
                    string cursor="";var seen=new HashSet<string>(StringComparer.Ordinal);
                    try{
                        for(int page=0;page<3;page++){
                            Report("Finding purchases across your connected accounts…");
                            var listed=await ReadOwnedPassPage(account,cursor,ct);
                            if(listed.Any(id=>seen.Contains(id)))break;
                            seen.UnionWith(listed);
                            bool complete=listed.Length<100;
                            lock(robloxLedgerLock){
                                var purchases=new Dictionary<string,long>(ledger.Purchases);
                                var pending=(ledger.PendingChecks??[]).Where(p=>p.Value>DateTimeOffset.UtcNow).ToDictionary();
                                foreach(var pass in passes){
                                    string pair=PairHash(ledger,account,pass.Id);
                                    if(seen.Contains(pass.Id)){purchases.TryAdd(pair,pass.Credit);pending.Remove(pair);}
                                    else if(complete&&!purchases.ContainsKey(pair))pending[pair]=DateTimeOffset.UtcNow.AddMinutes(15);
                                }
                                if(purchases.Count>100000||pending.Count>100000)throw new InvalidDataException("Purchase history full.");
                                var next=ledger with {Purchases=purchases,PendingChecks=pending};
                                SaveProtected(RobloxLedgerPath,JsonSerializer.Serialize(next,Json));ledger=next;
                                if(complete||passes.Count(NeedsCheck)<2)break;
                            }
                            cursor=listed[^1];
                        }
                    }catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                    catch{/* Direct ownership checks resolve inaccessible, malformed, or timed-out lists. */}
                });
            }
            await Parallel.ForEachAsync(jobs,new ParallelOptions{MaxDegreeOfParallelism=4,CancellationToken=scanDeadline.Token},async(job,ct)=>{
                string pair=PairHash(ledger,job.Account,job.Pass.Id);
                bool cached,checkedAbsent;
                lock(robloxLedgerLock){
                    cached=ledger.Purchases.ContainsKey(pair);
                    checkedAbsent=passId is null&&ledger.PendingChecks?.TryGetValue(pair,out var expires)==true&&expires>DateTimeOffset.UtcNow&&expires<=DateTimeOffset.UtcNow.AddMinutes(15);
                }
                if(cached){Interlocked.Increment(ref owned);Interlocked.Increment(ref resolved);Report();return;}
                if(checkedAbsent){Interlocked.Increment(ref resolved);Report();return;}
                try {
                    for(int attempt=0;attempt<5;attempt++){
                        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
                        HttpResponseMessage response;
                        await WaitForRobloxSlot(ct,()=>Report($"Roblox is limiting requests. Retrying automatically ({Volatile.Read(ref resolved)}/{totalChecks} checked)…"));
                        deadline.CancelAfter(TimeSpan.FromSeconds(10));
                        response=await http.GetAsync($"https://inventory.roblox.com/v1/users/{job.Account}/items/GamePass/{job.Pass.Id}/is-owned",HttpCompletionOption.ResponseHeadersRead,deadline.Token);
                        if(response.StatusCode==HttpStatusCode.TooManyRequests||response.StatusCode==HttpStatusCode.ServiceUnavailable){
                            ApplyRobloxCooldown(response,attempt);
                        }
                        using(response){
                            if(response.StatusCode==HttpStatusCode.TooManyRequests||response.StatusCode==HttpStatusCode.ServiceUnavailable){
                                Report($"Roblox is limiting requests. Retrying automatically ({Volatile.Read(ref resolved)}/{totalChecks} checked)…");
                                if(attempt<4)continue;
                            }
                            if(!response.IsSuccessStatusCode){Interlocked.Increment(ref unavailable);return;}
                            using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
                            byte[] bytes=new byte[65];int count=0,read;
                            while(count<bytes.Length&&(read=await stream.ReadAsync(bytes.AsMemory(count),deadline.Token))>0)count+=read;
                            if(count>64){Interlocked.Increment(ref unavailable);return;}
                            using var document=JsonDocument.Parse(bytes.AsMemory(0,count));
                            if(document.RootElement.ValueKind==JsonValueKind.True){
                                lock(robloxLedgerLock){
                                    // Save before exposing the credit, including if another request is canceled.
                                    var purchases=new Dictionary<string,long>(ledger.Purchases){[pair]=job.Pass.Credit};
                                    var pending=new Dictionary<string,DateTimeOffset>(ledger.PendingChecks??[]);pending.Remove(pair);
                                    var next=ledger with {Purchases=purchases,PendingChecks=pending};
                                    if(purchases.Count>100000)throw new InvalidDataException("Purchase history full.");
                                    SaveProtected(RobloxLedgerPath,JsonSerializer.Serialize(next,Json));ledger=next;
                                }
                                Interlocked.Increment(ref owned);
                            }else if(document.RootElement.ValueKind==JsonValueKind.False){
                                lock(robloxLedgerLock){
                                    // Checkpoint negatives only while discovery is unfinished. A new purchase
                                    // can always be checked immediately through selected-pass verification.
                                    var pending=(ledger.PendingChecks??[]).Where(p=>p.Value>DateTimeOffset.UtcNow).ToDictionary();
                                    pending[pair]=DateTimeOffset.UtcNow.AddMinutes(15);
                                    if(pending.Count>100000)throw new InvalidDataException("Purchase check history full.");
                                    var next=ledger with {PendingChecks=pending};
                                    SaveProtected(RobloxLedgerPath,JsonSerializer.Serialize(next,Json));ledger=next;
                                }
                            }else{Interlocked.Increment(ref unavailable);return;}
                            lock(robloxRateLock)robloxRequestSpacing=TimeSpan.FromMilliseconds(Math.Max(100,robloxRequestSpacing.TotalMilliseconds*0.9));
                            Interlocked.Increment(ref resolved);Report();return;
                        }
                    }
                }catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                catch {Interlocked.Increment(ref unavailable);}
            }); }catch(OperationCanceledException) when(!token.IsCancellationRequested){unavailable++;}
            if(unavailable==0&&resolved==totalChecks){
                lock(robloxLedgerLock){
                    var pending=new Dictionary<string,DateTimeOffset>(ledger.PendingChecks??[]);
                    foreach(var job in jobs)pending.Remove(PairHash(ledger,job.Account,job.Pass.Id));
                    var next=ledger with {PendingChecks=pending};
                    try{SaveProtected(RobloxLedgerPath,JsonSerializer.Serialize(next,Json));ledger=next;}
                    catch{unavailable++;}
                }
            }
            lastRobloxAccounts=accountSet;
            lastRobloxAttempt=DateTimeOffset.UtcNow;
            // Retry unresolved results on the next status request instead of caching an outage for six hours.
            lastRobloxCheck=unavailable==0&&resolved==totalChecks?DateTimeOffset.UtcNow:DateTimeOffset.MinValue;
            var state=State();
            string scope=passId is null?"All support passes checked":"Selected pass checked";
            string hint=passId is null?"":" Use Verify purchases to include other passes.";
            lastRobloxMessage=unavailable>0||resolved<totalChecks?$"Some purchases could not be checked ({resolved}/{totalChecks} complete). Your {state.TotalRobux:N0} verified Robux credit is saved. Check again to resume.":$"{scope}: {owned} owned {(owned==1?"pass":"passes")} · {state.TotalRobux:N0} Robux verified in total.{hint}";
            return state with {Message=lastRobloxMessage};
        }finally{robloxGate.Release();}
    }
}
