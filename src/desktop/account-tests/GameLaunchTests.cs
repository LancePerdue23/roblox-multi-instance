using System.Net;
using System.Text.Json;
using RobloxMultiInstance;

static class GameLaunchTests
{
    public static async Task<int> RunAsync()
    {
        int count=0;void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
        var a=new PublicServerDto("a",2,10,50);var b=new PublicServerDto("b",0,6,10);
        Check(GameLibrary.Assign([b,a],5).All(x=>x=="a"),"Whole batch prefers largest available capacity");
        Check(GameLibrary.Assign([a,a,b],16).SequenceEqual(Enumerable.Repeat<string?>("a",8).Concat(Enumerable.Repeat<string?>("b",6)).Concat(new string?[]{null,null})),"Server allocation splits groups without duplicate capacity and falls back for overflow");
        Check(GameLibrary.Assign([],2).All(x=>x is null),"Unavailable server list uses normal matchmaking");
        Check(GameLibrary.Assign([a with {Id="z"},a with {Id="x",Ping=10}],1)[0]=="x","Equal capacity prefers lower ping");
        Check(BrowserAccountSessionProvider.ObservedServer("[FLog::Output] ! Joining game 'aa112233-4455-6677-8899-aabbccddeeff' place 123 at hidden")=="aa112233-4455-6677-8899-aabbccddeeff","Owned player log supplies observed server separately from planned server");
        var provider=new Pipeline();var coordinator=new LaunchBatchCoordinator(provider);
        var contexts=new[]{new AccountLaunchContext("one","unused","101"),new AccountLaunchContext("two","unused","102")};
        var running=coordinator.RunAsync(contexts,new("123",null),null,null,default);
        await provider.Second.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(!running.IsCompleted,"Second client starts before the first slow join finishes");
        provider.Complete.TrySetResult(new("Started","ACCOUNT_IDENTITY_CONFIRMED",1,1));
        var result=await running;
        Check(result.Status=="Completed"&&result.Items.Select(x=>x.Slot).SequenceEqual(new[]{1,2})&&provider.Finished,"Background verification preserves ordered slots and lifetime cleanup");
        provider=new Pipeline {Ready=false};coordinator=new(provider);
        result=await coordinator.RunAsync(contexts,new("123",null),null,null,default);
        Check(provider.Calls==1&&result.Items[1].Code=="HELPER_READINESS_UNCONFIRMED","Unconfirmed helper stops pending launches without replay");
        provider=new Pipeline {DelayReady=true};coordinator=new(provider);
        using(var cancel=new CancellationTokenSource())
        {
            running=coordinator.RunAsync(contexts,new("123",null),null,null,cancel.Token);cancel.Cancel();result=await running;
            Check(provider.Calls==1&&result.Status=="Canceled"&&result.Items[0].Status=="Unknown"&&result.Items[1].Code=="BATCH_CANCELED","Pipelined cancellation preserves uncertain handoff and cancels pending accounts without replay");
        }
        string root=Path.Combine(Path.GetTempPath(),"GameLibraryTests-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var handler=new PublicApi();using var library=new GameLibrary(root,handler);
            var card=await library.Resolve("123",default);
            Check(card.Name=="Fixture Game"&&card.UniverseId=="456"&&card.RootPlaceId=="123"&&card.Artwork?.StartsWith("data:image/png;")==true&&card.ArtworkKind=="Icon","Checked target resolves square game icon and accepts Roblox's mixed-case image/Png response");
            Check((await library.Recent(default)).Length==0&&!Directory.Exists(root),"Checking targets never adds history or creates storage");
            int requests=handler.Calls;await library.Resolve("123",default);Check(handler.Calls==requests,"Metadata and artwork are cached");
            for(int i=1;i<=22;i++)await library.Remember(card with {PlaceId=i.ToString()},default);
            var recent=await library.Remember(card with {PlaceId="7"},default);
            Check(recent.Length==20&&recent[0].PlaceId=="7"&&recent.Count(x=>x.PlaceId=="7")==1,"Recent games are bounded, newest first and deduplicated");
            using var reopened=new GameLibrary(root,new PublicApi());
            Check((await reopened.Recent(default))[0].PlaceId=="7"&&(await reopened.Recent(default))[0].AccountIds is null,"Legacy game history remains readable without a saved batch");
            await library.Remember(card with {PlaceId="7",AccountIds=["second-local","first-local"],Preset="Two windows"},default);
            var saved=(await reopened.Recent(default))[0];
            Check(saved.AccountIds!.SequenceEqual(new[]{"second-local","first-local"})&&saved.Preset=="Two windows","Recent batch persists account order and layout across reopening");
            await library.Remember(card with {PlaceId="7",AccountIds=["second-local","first-local"],Preset="",Arrangement="Grid"},default);
            var quick=(await reopened.Recent(default))[0];
            Check(quick.Arrangement=="Grid"&&quick.Preset==""&&quick.AccountIds!.SequenceEqual(new[]{"second-local","first-local"}),"Quick layout and ordered account references survive restart without a saved preset");
            await library.Remember(card with {PlaceId="7",AccountIds=["first-local"],Preset=""},default);
            saved=(await reopened.Recent(default))[0];
            Check(saved.AccountIds!.SequenceEqual(new[]{"first-local"})&&saved.Preset==""&&(await reopened.Recent(default)).Count(x=>x.PlaceId=="7")==1,"A changed batch replaces the previous selection for the same game");
            Check((await library.Remove("7",false,default)).Length==19&&(await library.Remove(null,true,default)).Length==0,"History supports individual removal and clearing");
            var servers=await library.Servers("123",default);Check(servers.Length==1&&servers[0].MaxPlayers==10,"Public server lookup reads bounded capacity metadata");
            using var unavailable=new GameLibrary(root,new PublicApi {Fail=true});
            Check((await unavailable.Resolve("99",default)).Name=="Place 99"&&(await unavailable.Servers("99",default)).Length==0,"Unavailable metadata and server APIs retain a launchable place fallback");
            using var iconFallback=new GameLibrary(root,new PublicApi {LandscapeUnavailable=true});
            Check((await iconFallback.Resolve("123",default)).ArtworkKind=="Icon","Square game icons do not depend on landscape artwork being available");
            using var landscapeFallback=new GameLibrary(root,new PublicApi {IconUnavailable=true});
            Check((await landscapeFallback.Resolve("123",default)).ArtworkKind=="Thumbnail","Unavailable game icons fall back to landscape artwork");
            using var pagedHandler=new PublicApi {Paged=true};using var paged=new GameLibrary(root,pagedHandler);
            await paged.Servers("123",default);Check(pagedHandler.Calls==3,"Public server enumeration stops after three pages even with a repeated cursor");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        return count;
    }
    sealed class Pipeline : IPipelinedAccountSessionProvider
    {
        public bool Ready=true,Finished,DelayReady;public int Calls;
        public TaskCompletionSource Second=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AccountLaunchOutcome> Complete=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool LaunchAvailable=>true;public string Notice=>"fixture";
        public Task Prepare(AccountLaunchContext account,CancellationToken token)=>Task.CompletedTask;
        public Task Finish(){Finished=true;return Task.CompletedTask;}
        public Task<AccountLaunchOutcome> LaunchAsync(AccountLaunchContext account,LaunchTargetDto target,CancellationToken token)=>throw new Exception("Serialized full launch should not be used");
        public PendingAccountLaunch Start(AccountLaunchContext account,LaunchTargetDto target,Action<string,long> progress,CancellationToken token)
        {
            Calls++;if(!Ready)return new(Task.FromResult(false),Task.FromResult(new AccountLaunchOutcome("Unknown","HELPER_READINESS_UNCONFIRMED")));
            if(DelayReady){var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);token.Register(()=>{Complete.TrySetResult(new("Unknown","LAUNCH_OBSERVATION_CANCELED"));ready.TrySetResult(false);});return new(ready.Task,Complete.Task);}
            if(Calls==1)return new(Task.FromResult(true),Complete.Task);
            Second.TrySetResult();return new(Task.FromResult(true),Task.FromResult(new AccountLaunchOutcome("Started","ACCOUNT_IDENTITY_CONFIRMED",2,2)));
        }
    }
    sealed class PublicApi : HttpMessageHandler
    {
        public int Calls;public bool Fail,LandscapeUnavailable,IconUnavailable,Paged;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;if(request.Headers.Contains("Cookie"))throw new Exception("Public API received a cookie");
            string path=request.RequestUri!.AbsolutePath;
            if(Fail)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if(request.RequestUri.Host.EndsWith("rbxcdn.com"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent([137,80,78,71]) {Headers={ContentType=new("image/Png")}}});
            string json=path.Contains("/universe")?"{\"universeId\":456}":path.EndsWith("/servers/Public")?"{\"data\":[{\"id\":\"aa112233-4455-6677-8899-aabbccddeeff\",\"playing\":1,\"maxPlayers\":10,\"ping\":20}],\"nextPageCursor\":null}":path.Contains("thumbnails")?"{\"data\":[{\"thumbnails\":[{\"state\":\"Completed\",\"imageUrl\":\"https://tr.rbxcdn.com/fixture\"}]}]}":"{\"data\":[{\"id\":456,\"rootPlaceId\":123,\"name\":\"Fixture Game\"}]}";
            if(LandscapeUnavailable&&path.Contains("thumbnails"))json="{\"data\":[{\"thumbnails\":[]}]}";
            if(path.EndsWith("/icons"))json="{\"data\":[{\"state\":\"Completed\",\"imageUrl\":\"https://tr.rbxcdn.com/fixture\"}]}";
            if(IconUnavailable&&path.EndsWith("/icons"))json="{\"data\":[{\"state\":\"Pending\",\"imageUrl\":null}]}";
            if(Paged&&path.EndsWith("/servers/Public"))json=json.Replace("\"nextPageCursor\":null","\"nextPageCursor\":\"next\"");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
}
