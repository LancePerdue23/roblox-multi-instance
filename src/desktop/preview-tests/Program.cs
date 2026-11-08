using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RobloxMultiInstance;
using RobloxMultiInstance.Desktop;

internal static class Program
{
    static int count;
    static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
    [STAThread] static int Main(string[] args)
    {
        var app=new Application();int code=1;
        app.Startup+=async(_,_)=>
        {
            try{await Run(args.Contains("--capture"));Console.WriteLine($"Passed {count} preview checks.");code=0;}
            catch(Exception e){Console.Error.WriteLine(e);}
            finally{app.Shutdown();}
        };
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.Run();return code;
    }
    static async Task Run(bool capture)
    {
        NativeAccountChecks.Run();
        foreach(string json in new[]{"{\"ids\":[\"one\",\"one\"],\"rate\":2}","{\"ids\":[\"one\"],\"rate\":60}","{\"ids\":[],\"rate\":2,\"hwnd\":123}","{\"ids\":[\"\"],\"rate\":2}"})
        {bool rejected=false;try{PreviewConfiguration.Parse(JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<object>(json)));}catch{rejected=true;}Check(rejected,"Invalid configuration rejected");}
        string token=new('a',32),url=PreviewConfiguration.ImageOrigin+"/previews/"+token+".jpg?v=1";
        Check(PreviewConfiguration.CanRead(url,BridgeProtocol.Origin,token),"Valid image route");
        foreach(string invalid in new[]{url.Replace("desktop.local","evil.test"),url+"&path=secret",url.Replace("?v=1","?v=../file"),url+"#fragment"})Check(!PreviewConfiguration.CanRead(invalid,BridgeProtocol.Origin,token),"Invalid route refused");
        Check(!PreviewConfiguration.CanRead(url,"https://attacker.test",token),"Foreign renderer refused");
        using var backend=new DesktopBackend(Path.Combine(Path.GetTempPath(),"rmi-preview-fixture-"+Guid.NewGuid().ToString("N")));
        Check(backend.OpenPreviewWindow("unknown") is null,"Unknown native source refused");
        using(var service=new PreviewGridService(backend,()=>false,_=>{}))
        {
            var state=(PreviewStateDto)await service.ConfigureAsync(new(["unknown"],2));
            Check(state.Sources.Length==0,"Hidden host cannot capture");
            Check(service.Read(url,BridgeProtocol.Origin) is null,"No arbitrary resource access");
            service.Stop();service.Stop();
        }
        await ServiceChecks();
        if(!capture)return;
        Check(WindowCapture.Supported,"Native capture supported");
        var fixture=new Window{Title="RMI capture fixture",Width=900,Height=600,Content=new Border{Background=Brushes.DarkSlateBlue,Child=new TextBlock{Text="Preview fixture 123",Foreground=Brushes.White,FontSize=42,Margin=new Thickness(30)}}};
        fixture.Show();
        try
        {
            await Task.Delay(500);
            using var source=new WindowCapture(new WindowInteropHelper(fixture).Handle);
            byte[]? frame=null;
            for(int i=0;i<30&&frame is null;i++){await Task.Delay(100);frame=await source.ReadAsync();}
            Check(frame is {Length:>100},"Native fixture produced a frame");
            using(var image=System.Drawing.Image.FromStream(new MemoryStream(frame!)))Check(image.Width<=480&&image.Height<=270,"Frame dimensions bounded");
            string output=Path.GetFullPath(".build/preview-fixture.jpg");Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.WriteAllBytes(output,frame!);
            fixture.Width=640;fixture.Height=480;frame=null;
            for(int i=0;i<30&&frame is null;i++){await Task.Delay(100);frame=await source.ReadAsync();}
            Check(frame is {Length:>100},"Resized source recovers within deadline");
            fixture.Close();await Task.Delay(200);Check(source.Closed,"Closed source signaled");
            Console.WriteLine("Native capture fixture: "+output);
            await RouteChecks(frame!);
        }
        finally{fixture.Close();}
    }
    static async Task RouteChecks(byte[] jpeg)
    {
        using var backend=new FakeBackend();PreviewStateDto? state=null;
        using var service=new PreviewGridService(backend,()=>true,value=>state=(PreviewStateDto)value,
            _=>new FakeCapture{Bytes=jpeg},()=>true,_=>false);
        await service.ConfigureAsync(new(["fixture"],2));await service.PollAsync();
        string url=state!.Sources.Single().Url!;
        var view=new Microsoft.Web.WebView2.Wpf.WebView2();
        var window=new Window{Title="RMI image transport fixture",Width=600,Height=400,Content=view};window.Show();
        try
        {
            string root=Path.GetFullPath(".build/preview-resource-fixture");Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root,"index.html"),"<!doctype html><meta http-equiv='Content-Security-Policy' content=\"default-src 'self'; img-src https://previews.desktop.local\"><title>Preview route fixture</title><h1>Preview delivery</h1>");
            var environment=await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"profile"));
            await view.EnsureCoreWebView2Async(environment);
            var core=view.CoreWebView2;core.SetVirtualHostNameToFolderMapping("desktop.local",root,Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
            PreviewResourceRoute.Attach(core,service,()=>true);
            var loaded=new TaskCompletionSource();core.NavigationCompleted+=(_,e)=>{if(e.IsSuccess)loaded.TrySetResult();else loaded.TrySetException(new Exception("Fixture navigation failed"));};
            core.Navigate(BridgeProtocol.Origin+"/index.html");await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
            async Task<string> Load(string imageUrl)
            {
                await core.ExecuteScriptAsync("window.result='pending';var image=new Image();image.onload=()=>window.result='loaded';image.onerror=()=>window.result='denied';image.src="+JsonSerializer.Serialize(imageUrl)+";document.body.append(image);");
                for(int i=0;i<50;i++){await Task.Delay(100);var result=await core.ExecuteScriptAsync("window.result");if(result!="\"pending\"")return result;}
                return "timeout";
            }
            Check(await Load(url)=="\"loaded\"","Production image route delivers JPEG under CSP and virtual asset mapping");
            service.Stop();
            Check(await Load(url+"0")=="\"denied\"","Revoked subscription returns no image");
            using var screenshot=File.Create(Path.GetFullPath(".build/preview-transport.png"));
            await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,screenshot);
        }
        finally{view.Dispose();window.Close();}
    }
    static async Task ServiceChecks()
    {
        using var fake=new FakeBackend();
        DateTime now=DateTime.UtcNow;bool visible=true,minimized=false;PreviewStateDto? state=null;
        var captures=new List<FakeCapture>();
        using var service=new PreviewGridService(fake,()=>visible,value=>state=(PreviewStateDto)value,
            _=>{var capture=new FakeCapture();captures.Add(capture);return capture;},()=>true,_=>minimized,()=>now);
        var initial=(PreviewStateDto)await service.ConfigureAsync(new(["one"],2));
        Check(initial.Sources.Single().Url is null,"No image before capture");
        await service.PollAsync();
        string url=state!.Sources.Single().Url!;
        Check(service.Read(url,BridgeProtocol.Origin) is {Length:3},"Latest frame available");
        Check(service.Read(url,"https://evil.test") is null,"Foreign resource requests refused");
        int reads=captures[0].Reads;await service.PollAsync();Check(captures[0].Reads==reads,"Rate limit enforced");
        minimized=true;now+=TimeSpan.FromSeconds(1);await service.PollAsync();
        Check(captures[0].Disposed&&state.Sources.Single().Status=="Minimized"&&service.Read(url,BridgeProtocol.Origin) is null,"Minimize clears image and releases capture");
        minimized=false;now+=TimeSpan.FromSeconds(1);await service.PollAsync();Check(captures.Count==2,"Restore recreates capture");
        fake.Windows.Last().Valid=false;now+=TimeSpan.FromSeconds(1);await service.PollAsync();Check(state.Sources.Single().Status=="Closed"&&captures.Last().Disposed,"Invalid identity drops capture");
        service.Stop();Check(fake.Windows.All(w=>w.Disposed),"Stop releases leases");
        await service.ConfigureAsync(new(["two"],2));now+=TimeSpan.FromSeconds(1);await service.PollAsync();
        var pending=new TaskCompletionSource<byte[]?>();captures.Last().Pending=pending.Task;
        now+=TimeSpan.FromSeconds(1);var poll=service.PollAsync();
        var oldCapture=captures.Last();var queued=service.ConfigureAsync(new(["late"],2));service.Stop();
        Check(service.Read(state!.Sources.Single().Url!,BridgeProtocol.Origin) is null,"Stop immediately revokes in-flight source");
        pending.SetResult([7,8,9]);await poll;await service.StopAsync();
        Check(((PreviewStateDto)await queued).Sources.Length==0,"Queued configuration cannot resurrect a revoked generation");
        Check(oldCapture.Disposed&&fake.Windows.All(w=>w.Disposed),"Late frame cannot retain source");
        await service.ConfigureAsync(new(["three"],2));now+=TimeSpan.FromSeconds(1);await service.PollAsync();
        now+=TimeSpan.FromSeconds(9);await service.PollAsync();Check(state!.Sources.Length==0&&captures.Last().Disposed,"Missed heartbeat stops capture");
        await service.ConfigureAsync(new(["capacity"],2));now+=TimeSpan.FromSeconds(1);await service.PollAsync();
        captures.Last().Pixels=PreviewGridService.MaximumPixels+1;now+=TimeSpan.FromSeconds(1);await service.PollAsync();
        Check(state!.Sources.Single().Status=="Capacity limit"&&captures.Last().Disposed,"Aggregate pixel budget releases excessive capture");
        service.Stop();await service.ConfigureAsync(new(["stalled"],2));now+=TimeSpan.FromSeconds(1);await service.PollAsync();
        captures.Last().Pending=Task.FromResult<byte[]?>(null);now+=TimeSpan.FromSeconds(4);await service.PollAsync();
        Check(state!.Sources.Single().Status=="Stale","No frame delivery is explicitly stale");
        visible=false;Check(((PreviewStateDto)await service.ConfigureAsync(new(["four"],2))).Sources.Length==0,"Hidden host rejects restart");
    }
    sealed class FakeBackend : IDesktopBackend
    {
        public readonly List<FakeWindow> Windows=[];
        public string AppearanceScheme=>"Dark";
        public event Action<string,string,object?>? Event {add{}remove{}}
        public IPreviewWindow? OpenPreviewWindow(string id){var window=new FakeWindow();Windows.Add(window);return window;}
        public Task<object?> ExecuteAsync(string command,JsonElement payload,CancellationToken token)=>Task.FromResult<object?>(null);
        public void Dispose(){}
    }
    sealed class FakeWindow : IPreviewWindow
    {
        public nint Handle=>1;
        public bool Valid=true,Disposed;
        public bool IsValid=>Valid&&!Disposed;
        public void Dispose()=>Disposed=true;
    }
    sealed class FakeCapture : IPreviewCapture
    {
        public bool Closed=>false;
        public long Pixels {get;set;}=1;
        public bool Disposed;public int Reads;public Task<byte[]?>? Pending;public byte[] Bytes=[1,2,3];
        public Task<byte[]?> ReadAsync(){Reads++;return Pending??Task.FromResult<byte[]?>(Bytes);}
        public void Dispose()=>Disposed=true;
    }
}
