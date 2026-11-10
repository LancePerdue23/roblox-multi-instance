using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using RobloxMultiInstance;

static class InventoryChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var small=new SupportConfiguration("",new(),RobloxPasses:[new("123",200),new("456",500),new("789",1000)]);
        string Page(params string[] ids)=>JsonSerializer.Serialize(new {gamePasses=ids.Select(id=>new {gamePassId=id,price=999999,isForSale=false})});
        string FullPage(int page)=>Page(Enumerable.Range(page*100+10000,99).Select(n=>n.ToString()).Prepend(page==0?"123":(page+90000).ToString()).ToArray());
        async Task<SupportState> RunCase(string name,ListingHandler handler,SupportConfiguration? config=null){
            using var http=new HttpClient(handler);
            using var service=new SupportService(Path.Combine(root,name),http,config??small);
            return await service.CheckRobloxAsync(["111"],true);
        }

        var catalog=SupportService.ApprovedRobloxConfiguration();
        var all=new ListingHandler{List=(_,_)=>Page(catalog.RobloxPasses!.Select(p=>p.Id).Append("999999").ToArray())};
        using(var http=new HttpClient(all))using(var service=new SupportService(Path.Combine(root,"all"),http,catalog)){
            var s=await service.CheckRobloxAsync(["111","111"],true);
            check(s.TotalRobux==198000&&all.ListCalls==1&&all.DirectCalls==0,"One inventory response verifies all 198,000 Robux using fixed credit, ignoring unrelated IDs, sale status, and listed prices");
            await service.CheckRobloxAsync(["111"],true);
            check(all.ListCalls==1&&all.DirectCalls==0,"Already verified complete catalog needs no requests even on manual verification");
        }
        var empty=new ListingHandler{List=(_,_)=>Page()};
        var state=await RunCase("empty",empty);
        check(state.TotalRobux==0&&empty.ListCalls==1&&empty.DirectCalls==0&&!state.Message.Contains("could not"),"A complete empty inventory resolves non-ownership in one request");

        var paged=new ListingHandler{List=(_,page)=>page==0?FullPage(0):Page("456","789")};
        state=await RunCase("paged",paged);
        check(state.TotalRobux==1700&&paged.ListCalls==2&&paged.DirectCalls==0,"Ownership across two pages accumulates without individual checks");
        check(paged.Queries.Last().EndsWith("exclusiveStartId=10098"),"Next page starts at the last ID of the preceding page");

        var large=new ListingHandler{List=(_,page)=>FullPage(page),Owned=["456"]};
        state=await RunCase("large",large);
        check(state.TotalRobux==700&&large.ListCalls==3&&large.DirectCalls==2,"Large inventories stop after three pages and directly check only unresolved support passes");

        var repeating=new ListingHandler{List=(_,_)=>FullPage(0),Owned=["456"]};
        state=await RunCase("repeating",repeating);
        check(state.TotalRobux==700&&repeating.ListCalls==2&&repeating.DirectCalls==2,"Repeated pages stop traversal without treating unseen passes as unowned");

        foreach(string fault in new[]{"forbidden","later-failure","malformed","duplicate","missing-id","oversized","throttled"}){
            var handler=new ListingHandler{Owned=["456"],List=(_,page)=>fault switch {
                "later-failure" when page==0=>FullPage(0),
                "malformed"=>"{\"gamePasses\":null}",
                "duplicate"=>Page("123","123"),
                "missing-id"=>"{\"gamePasses\":[{\"gamePassId\":123},{}]}",
                "oversized"=>new string('x',1024*1024+1),
                _=>null
            },ListStatus=fault=="throttled"?HttpStatusCode.TooManyRequests:HttpStatusCode.Forbidden};
            state=await RunCase(fault,handler);
            check(state.TotalRobux==(fault=="later-failure"?700:500)&&handler.DirectCalls==(fault=="later-failure"?2:3)&&!state.Message.Contains("could not"),$"{fault} listing falls back without inventing absence or losing verified matches");
            if(fault=="throttled")check(handler.FirstDirect-handler.LastList>=TimeSpan.FromMilliseconds(950),"Listing throttles are respected before fallback requests start");
        }

        var accounts=new ListingHandler{List=(account,_)=>account=="111"?Page("123"):Page("123","456")};
        using(var http=new HttpClient(accounts))using(var service=new SupportService(Path.Combine(root,"accounts"),http,small)){
            state=await service.CheckRobloxAsync(["111","222","111"],true);
            check(state.TotalRobux==900&&accounts.ListCalls==2&&accounts.DirectCalls==0,"Each connected account is listed once and the same pass on different accounts counts separately");
        }

        var canceled=new ListingHandler{List=(_,_)=>FullPage(0),BlockSecond=true};
        using(var http=new HttpClient(canceled))using(var service=new SupportService(Path.Combine(root,"cancel"),http,small)){
            using var cancel=new CancellationTokenSource();
            var task=service.CheckRobloxAsync(["111"],true,cancel.Token);
            await canceled.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            try{await task;throw new Exception("Listing cancellation ignored");}
            catch(OperationCanceledException){check(service.State().TotalRobux==200,"Canceling a later inventory page preserves credit from earlier pages");}
        }
    }

    sealed class ListingHandler:HttpMessageHandler
    {
        public Func<string,int,string?> List=(_,_)=>null;
        public HashSet<string> Owned=[];
        public ConcurrentQueue<string> Queries=new();
        readonly ConcurrentDictionary<string,int> pages=new();
        public HttpStatusCode ListStatus=HttpStatusCode.Forbidden;
        public int ListCalls,DirectCalls;
        public DateTimeOffset LastList,FirstDirect;
        public bool BlockSecond;
        public TaskCompletionSource SecondStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var uri=request.RequestUri!;
            if(request.Headers.Contains("Cookie")||request.Headers.Authorization is not null||uri.Scheme!="https")throw new Exception("Unexpected credentials/destination");
            if(uri.Host=="apis.roblox.com"){
                Interlocked.Increment(ref ListCalls);LastList=DateTimeOffset.UtcNow;Queries.Enqueue(uri.Query);
                var account=uri.AbsolutePath.Split('/')[4];int page=pages.AddOrUpdate(account,0,(_,p)=>p+1);
                if(BlockSecond&&page==1){SecondStarted.TrySetResult();await Task.Delay(Timeout.Infinite,token);}
                string? body=List(account,page);
                var response=new HttpResponseMessage(body is null?ListStatus:HttpStatusCode.OK){Content=new StringContent(body??"{}")};
                if(response.StatusCode==HttpStatusCode.TooManyRequests)response.Headers.TryAddWithoutValidation("Retry-After","1");
                return response;
            }
            if(uri.Host!="inventory.roblox.com")throw new Exception("Unexpected destination");
            if(Interlocked.Increment(ref DirectCalls)==1)FirstDirect=DateTimeOffset.UtcNow;
            return new(HttpStatusCode.OK){Content=new StringContent(Owned.Contains(uri.AbsolutePath.Split('/')[6])?"true":"false")};
        }
    }
}
