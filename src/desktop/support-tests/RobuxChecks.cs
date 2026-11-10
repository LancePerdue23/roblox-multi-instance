using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using RobloxMultiInstance;

static class RobuxChecks
{
    public static async Task Run(string root,Action<bool,string> check,SupportConfiguration usdConfig,string usdReceipt)
    {
        await InventoryChecks.Run(Path.Combine(root,"inventory"),check);
        var catalog=SupportService.ApprovedRobloxConfiguration();
        var passes=catalog.RobloxPasses!;
        check(passes.Length==21&&passes.Select(p=>p.Id).Distinct().Count()==21,"Approved group catalog contains all 21 distinct passes");
        check(catalog.Endpoint==""&&catalog.PublicKeys.Count==0,"Bundled Robux configuration leaves Stripe disabled");
        foreach(var (total,tier) in new (long,int)[]{(0,-1),(299,-1),(300,0),(699,0),(700,1),(1499,1),(1500,2),(100000,2)})
            check(SupportService.RobuxTier(total)==tier,$"Robux threshold {total} gives tier {tier}");
        var handler=new OwnershipHandler();using var http=new HttpClient(handler);
        string account="759286240001",other="759286240002";
        string Pass(long amount)=>passes.Single(p=>p.Credit==amount).Id;
        var targetedHandler=new OwnershipHandler();targetedHandler.Owned.Add((account,Pass(500)));targetedHandler.Owned.Add((other,Pass(200)));
        using(var targetedHttp=new HttpClient(targetedHandler))using(var service=new SupportService(Path.Combine(root,"targeted-credit"),targetedHttp,catalog)){
            var s=await service.CheckRobloxAsync([account,other],true,passId:Pass(500));
            check(targetedHandler.Calls==2&&s.TotalRobux==500,"Selected-pass verification makes one request per connected account instead of scanning all passes");
            check(s.Message.StartsWith("Selected pass checked:")&&s.Message.Contains("Verify purchases"),"Quick-check results explain that other passes have not been scanned");
            check(targetedHandler.Requests.Keys.All(p=>p.Contains('/'+Pass(500)+'/')),"Targeted verification contacts only the selected allowlisted pass");
            s=await service.CheckRobloxAsync([account,other],passId:Pass(200));
            check(targetedHandler.Calls==4&&s.TotalRobux==700&&s.VerifiedTier==1,"Changing the selected pass bypasses unrelated scan throttles and combines credit across accounts");
            int calls=targetedHandler.Calls;
            check(service.State().TotalRobux==700&&targetedHandler.Calls==calls,"Reading cached support status performs no ownership requests");
            try{await service.CheckRobloxAsync([account],true,passId:"999999999");throw new Exception("Unconfigured verification pass accepted");}catch(ArgumentException){check(targetedHandler.Calls==calls,"Targeted verification rejects unconfigured pass IDs before networking");}
            string resetRoot=Path.Combine(root,"targeted-credit");
            File.WriteAllBytes(Path.Combine(resetRoot,"support-roblox.bin"),ProtectedData.Protect(Encoding.UTF8.GetBytes("verified-gamepass-supporter-v1"),null,DataProtectionScope.CurrentUser));
            s=await service.ResetRobloxVerificationAsync();
            check(s.TotalRobux==0&&s.RobloxTier==-1&&!s.Supported&&targetedHandler.Calls==calls,"Dev reset clears real Robux credit and legacy recognition without networking");
            check(!File.Exists(Path.Combine(resetRoot,"support-roblox-v2.bin"))&&!File.Exists(Path.Combine(resetRoot,"support-roblox.bin")),"Reset removes both protected Robux caches");
            s=await service.CheckRobloxAsync([account,other],passId:Pass(200));
            check(targetedHandler.Calls==calls+2&&s.TotalRobux==200,"Next verification makes fresh ownership requests after reset, including previously verified pairs");
        }
        string history=Path.Combine(root,"robux-credit");
        handler.Owned.Add((account,Pass(500)));handler.Owned.Add((other,Pass(200)));
        using(var service=new SupportService(history,http,catalog)){
            var s=await service.CheckRobloxAsync([account,account],true);
            check(s.TotalRobux==500&&s.RobloxTier==0&&s.VerifiedTier==0&&!s.Configured,"500 Robux grants base recognition with Stripe unavailable");
            int calls=handler.Calls;
            await service.CheckRobloxAsync([account]);check(handler.Calls==calls,"Unchanged account set uses verification throttle");
            s=await service.CheckRobloxAsync([account,other]);
            check(s.TotalRobux==700&&s.VerifiedTier==1,"500 plus 200 across accounts earns Supporter+ and invalidates throttle");
            check(handler.Requests.Keys.Any(p=>p.Contains('/'+passes[20].Id+'/')),"Ownership checks include passes beyond the old 16-pass limit");
            s=await service.CheckRobloxAsync([other,account,other],true);
            check(s.TotalRobux==700,"Repeated checks and duplicate account entries never double credit");
            handler.Owned.Add((account,Pass(200)));
            s=await service.CheckRobloxAsync([account],true);
            check(s.TotalRobux==900,"The same pass owned on two distinct accounts counts twice");
            check(!s.ReceiptAvailable&&s.TotalCents==0&&s.UsdTier==-1,"Robux credit neither creates USD credit nor enables receipt export");
            service.SetBadgeVisible(false);
            check(!service.State().ShowBadge&&service.State().VerifiedTier==1,"Robux tiers respect badge visibility");
            check(service.RobloxDonationUri(Pass(200)).AbsoluteUri.EndsWith('/'+Pass(200)),"Donation URI selects the exact configured pass");
            foreach(string bad in new[]{"999999999","https://attacker.invalid/","2076244661?redirect=x"}){
                try{service.RobloxDonationUri(bad);throw new Exception("Unconfigured destination accepted");}catch(ArgumentException){check(true,"Unconfigured pass/destination rejected");}
            }
            check(handler.MaxConcurrency<=4,"Ownership verification bounds concurrent requests");
        }
        byte[] ledger=ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(history,"support-roblox-v2.bin")),null,DataProtectionScope.CurrentUser);
        string content=Encoding.UTF8.GetString(ledger);
        check(!content.Contains(account)&&!content.Contains(other)&&!content.Contains(Pass(200)),"Protected ledger uses salted pair hashes without raw account or pass IDs");
        handler.Unavailable=true;
        using(var service=new SupportService(history,http,catalog)){
            var s=await service.CheckRobloxAsync([],true);
            check(s.TotalRobux==900&&s.VerifiedTier==1,"Restart and account removal retain verified totals and tiers");
            s=await service.CheckRobloxAsync([account,other],true);
            check(s.TotalRobux==900&&s.Message.Contains("could not"),"Private inventories and API failures preserve cached credit");
        }
        handler.Unavailable=false;
        handler.Owned.Add((account,Pass(100)));
        using(var service=new SupportService(history,http,catalog)){
            using var held=new FileStream(Path.Combine(history,"support-roblox-v2.bin"),FileMode.Open,FileAccess.Read,FileShare.Read);
            var checking=service.CheckRobloxAsync([account],true);
            await Task.Delay(75);held.Dispose();
            var s=await checking;
            check(s.TotalRobux==1000&&!s.Message.Contains("could not"),"Transient Windows replacement lock retries without losing or duplicating credit");
        }
        foreach(bool split in new[]{false,true}){
            handler.Owned.Clear();handler.Owned.Add((account,Pass(split?500:700)));if(split)handler.Owned.Add((account,Pass(200)));
            using var service=new SupportService(Path.Combine(root,"same-account-"+split),http,catalog);
            var s=await service.CheckRobloxAsync([account],true);
            check(s.TotalRobux==700&&s.VerifiedTier==1,split?"500+200 on one account equals 700 credit":"A single 700 pass earns Supporter+");
        }
        handler.Owned.Clear();handler.Owned.Add((account,Pass(100)));handler.BadPass=Pass(500);
        using(var service=new SupportService(Path.Combine(root,"partial-credit"),http,catalog)){
            var s=await service.CheckRobloxAsync([account],true);
            check(s.TotalRobux==100&&s.VerifiedTier==-1&&s.Message.Contains("could not"),"Partial malformed responses retain sub-threshold credit without granting a tier");
        }
        handler.BadPass=null;
        var recoveryHandler=new OwnershipHandler{BadPass=Pass(500)};
        recoveryHandler.Owned.Add((account,Pass(200)));recoveryHandler.Owned.Add((account,Pass(500)));
        using(var recoveryHttp=new HttpClient(recoveryHandler)){
            string resumeRoot=Path.Combine(root,"resume-discovery");
            var small=catalog with {RobloxPasses=passes.Where(p=>p.Credit is 100 or 200 or 500).ToArray()};
            using(var service=new SupportService(resumeRoot,recoveryHttp,small)){
                var s=await service.CheckRobloxAsync([account],true);
                check(s.TotalRobux==200&&s.Message.Contains("2/3 complete"),"Incomplete discovery saves both positive and negative checks");
            }
            recoveryHandler.BadPass=null;int calls=recoveryHandler.Calls;
            using(var service=new SupportService(resumeRoot,recoveryHttp,small)){
                var s=await service.CheckRobloxAsync([account],true);
                check(s.TotalRobux==700&&recoveryHandler.Calls==calls+1,"Discovery resumes only the unresolved pair after restart, including cached negatives");
                recoveryHandler.Owned.Add((account,Pass(100)));calls=recoveryHandler.Calls;
                s=await service.CheckRobloxAsync([account],true);
                check(s.TotalRobux==800&&recoveryHandler.Calls==calls+1,"Completed discovery clears negative checkpoints so a fresh full scan detects new purchases");
            }
            recoveryHandler.BadPass=Pass(500);recoveryHandler.Owned.Remove((account,Pass(100)));
            using(var service=new SupportService(Path.Combine(root,"targeted-resume"),recoveryHttp,small)){
                await service.CheckRobloxAsync([account],true);
                recoveryHandler.Owned.Add((account,Pass(100)));calls=recoveryHandler.Calls;
                var s=await service.CheckRobloxAsync([account],true,passId:Pass(100));
                check(s.TotalRobux==300&&recoveryHandler.Calls==calls+1,"Selected-pass verification bypasses negative checkpoints immediately after a purchase");
            }
        }
        var fullHandler=new OwnershipHandler{OwnEverything=true};
        using(var fullHttp=new HttpClient(fullHandler))using(var service=new SupportService(Path.Combine(root,"full-198000"),fullHttp,catalog)){
            var s=await service.CheckRobloxAsync([account],true,passId:Pass(500));
            check(s.TotalRobux==500,"Quick check credits only its selected denomination");
            s=await service.CheckRobloxAsync([account],true);
            check(s.TotalRobux==198000&&fullHandler.Calls==21&&s.Message.StartsWith("All support passes checked: 21 owned passes"),"Full discovery restores all 198,000 Robux after a selected-pass check without repeating saved purchases");
            var times=fullHandler.StartTimes.Order().ToArray();
            check(times.Zip(times.Skip(1),(a,b)=>(b-a).TotalMilliseconds).All(ms=>ms>=75),"Ownership request starts are paced even with four workers");
        }
        foreach(bool dateHeader in new[]{false,true}){
            var retryHandler=new OwnershipHandler{OwnEverything=true,RateLimitRemaining=2,RetryAfter=dateHeader?DateTimeOffset.UtcNow.AddSeconds(1).ToString("R"):"1"};
            using var retryHttp=new HttpClient(retryHandler);
            using var service=new SupportService(Path.Combine(root,"rate-recovery-"+dateHeader),retryHttp,new("",new(),RobloxPasses:[new("123",500),new("456",200)]));
            var messages=new ConcurrentQueue<string>();var s=await service.CheckRobloxAsync([account],true,progress:messages.Enqueue);
            check(s.TotalRobux==700&&s.VerifiedTier==1&&retryHandler.Calls==4,"One check automatically retries throttled ownership requests and credits each pair once");
            check(messages.Any(m=>m.Contains("Retrying automatically"))&&messages.Any(m=>m.Contains("2 of 2")),"Progress explains rate-limit waits and completed ownership checks");
            int calls=retryHandler.Calls;await service.CheckRobloxAsync([account],true);
            check(retryHandler.Calls==calls,"Already verified pairs are not requested again after recovery");
        }
        var limited=new OwnershipHandler{RateLimitRemaining=100,RetryAfter="1"};
        using(var limitedHttp=new HttpClient(limited))using(var service=new SupportService(Path.Combine(root,"rate-exhausted"),limitedHttp,new("",new(),RobloxPasses:[new("123",500)]))){
            var s=await service.CheckRobloxAsync([account],true);
            check(!s.Supported&&s.TotalRobux==0&&s.Message.Contains("0/1 complete")&&limited.Calls==5,"Persistent throttling has bounded retries and clearly reports incomplete verification");
            int calls=limited.Calls;var repeated=await service.CheckRobloxAsync([account]);
            check(limited.Calls==calls&&repeated.Message==s.Message,"Automatic status requests preserve partial status and do not immediately repeat a failed scan");
        }
        string legacy=Path.Combine(root,"legacy-robux");Directory.CreateDirectory(legacy);
        File.WriteAllBytes(Path.Combine(legacy,"support-roblox.bin"),ProtectedData.Protect(Encoding.UTF8.GetBytes("verified-gamepass-supporter-v1"),null,DataProtectionScope.CurrentUser));
        using(var service=new SupportService(legacy,http,catalog)){
            check(service.State().VerifiedTier==0&&service.State().RobloxTier==0&&service.State().TotalRobux==0,"Legacy recognition keeps base tier without invented historical credit");
            handler.Owned.Clear();handler.Owned.Add((account,Pass(700)));
            var s=await service.CheckRobloxAsync([account],true);
            check(s.VerifiedTier==1&&s.TotalRobux==700,"Legacy marker does not double credit newly verified passes");
        }
        var mixed=catalog with {Endpoint=usdConfig.Endpoint,PublicKeys=usdConfig.PublicKeys};
        using(var service=new SupportService(Path.Combine(root,"mixed-currency"),http,mixed)){
            await service.CheckRobloxAsync([account],true);
            string receipt=Path.Combine(root,"mixed.rmisupport");File.WriteAllText(receipt,usdReceipt);var s=service.ImportReceipt(receipt);
            check(s.VerifiedTier==2&&s.RobloxTier==1&&s.UsdTier==2&&s.TotalRobux==700&&s.TotalCents==1500,"Higher currency tier wins without adding USD and Robux totals");
            s=await service.ResetRobloxVerificationAsync();
            check(s.TotalRobux==0&&s.VerifiedTier==2&&s.UsdTier==2&&s.TotalCents==1500&&s.ReceiptAvailable,"Robux cache reset preserves signed USD receipts and credit");
            await service.CheckRobloxAsync([account],true);
            s=service.RemoveReceipt();check(s.VerifiedTier==1&&s.TotalRobux==700,"Removing a Stripe receipt preserves Robux recognition");
        }
        // Exercise history larger than the old 16 KB receipt reader, including offline reload.
        handler.Owned.Clear();handler.OwnEverything=true;
        string large=Path.Combine(root,"large-ledger");
        using(var service=new SupportService(large,http,catalog)){
            var s=await service.CheckRobloxAsync(Enumerable.Range(100,12).Select(n=>n.ToString()),true);
            check(s.TotalRobux==12*passes.Sum(p=>p.Credit),$"All approved passes across many accounts contribute to the total ({s.TotalRobux}/{12*passes.Sum(p=>p.Credit)}; {s.Message})");
        }
        using(var service=new SupportService(large,http,catalog))check(service.State().TotalRobux==12*passes.Sum(p=>p.Credit),"Larger protected ledgers survive offline restart");
        handler.OwnEverything=false;handler.Owned.Add((account,Pass(200)));handler.BlockExcept=Pass(200);
        string canceled=Path.Combine(root,"cancel-credit");
        using(var service=new SupportService(canceled,http,catalog)){
            using var cancel=new CancellationTokenSource();var checking=service.CheckRobloxAsync([account],true,cancel.Token);
            for(int i=0;i<100&&service.State().TotalRobux!=200;i++)await Task.Delay(10);
            cancel.Cancel();try{await checking;throw new Exception("Cancellation ignored");}catch(OperationCanceledException){check(service.State().TotalRobux==200,"Cancellation retains purchases saved before interruption");}
        }
    }

    sealed class OwnershipHandler:HttpMessageHandler
    {
        public HashSet<(string Account,string Pass)> Owned=new();
        public ConcurrentDictionary<string,int> Requests=new();
        public ConcurrentQueue<DateTimeOffset> StartTimes=new();
        public bool Unavailable,OwnEverything;public string? BadPass,BlockExcept;
        public int RateLimitRemaining;public string RetryAfter="1";
        public int Calls,MaxConcurrency,active;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri!.Host=="apis.roblox.com")return new(HttpStatusCode.Forbidden);
            StartTimes.Enqueue(DateTimeOffset.UtcNow);
            Interlocked.Increment(ref Calls);int concurrent=Interlocked.Increment(ref active);
            int current;do{current=MaxConcurrency;}while(current<concurrent&&Interlocked.CompareExchange(ref MaxConcurrency,concurrent,current)!=current);
            try{
                if(request.Headers.Contains("Cookie")||request.Headers.Authorization is not null||request.RequestUri!.Host!="inventory.roblox.com")throw new Exception("Unexpected credentials/destination");
                string path=request.RequestUri!.AbsolutePath;Requests.AddOrUpdate(path,1,(_,n)=>n+1);
                var parts=path.Split('/');string account=parts[3],pass=parts[6];
                if(BlockExcept is not null&&pass!=BlockExcept)await Task.Delay(Timeout.Infinite,token);
                else await Task.Delay(1,token);
                if(Interlocked.Decrement(ref RateLimitRemaining)>=0){var limited=new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("{\"errors\":[]}")};limited.Headers.TryAddWithoutValidation("Retry-After",RetryAfter);return limited;}
                return new(Unavailable?HttpStatusCode.Forbidden:HttpStatusCode.OK){Content=new StringContent(pass==BadPass?"{\"isOwned\":true}":OwnEverything||Owned.Contains((account,pass))?"true":"false")};
            }finally{Interlocked.Decrement(ref active);}
        }
    }
}
