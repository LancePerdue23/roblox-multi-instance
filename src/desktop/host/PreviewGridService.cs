using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RobloxMultiInstance.Desktop;

// Dispatcher owned. Invalidation revokes images synchronously; disposal waits for an in-flight read.
internal sealed class PreviewGridService(IDesktopBackend backend,Func<bool> visible,Action<object> publish,
    Func<nint,IPreviewCapture>? captureFactory=null,Func<bool>? supported=null,Func<nint,bool>? minimized=null,Func<DateTime>? clock=null) : IDisposable
{
    sealed class Source(string id,IPreviewWindow window)
    {
        public readonly string Id=id,Token=Guid.NewGuid().ToString("N");
        public readonly IPreviewWindow Window=window;
        public IPreviewCapture? Capture;
        public byte[]? Bytes;
        public string Status="Starting";
        public DateTime? Updated;
        public DateTime Started;
        public ulong Revision;
        public void Dispose(){Bytes=null;try{Capture?.Dispose();}finally{Capture=null;Window.Dispose();}}
    }
    readonly Dictionary<string,Source> sources=new(StringComparer.Ordinal);
    string[] requested=[];
    readonly SemaphoreSlim gate=new(1,1);
    readonly System.Windows.Threading.DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(200)};
    DateTime heartbeat,next;
    int generation,rate=2;
    bool disposed,ticking;
    Task cleanup=Task.CompletedTask;
    internal const long MaximumPixels=24*1024*1024;
    DateTime Now=>(clock??(()=>DateTime.UtcNow))();
    bool Minimized(nint window)=>(minimized??IsIconic)(window);
    public bool Supported=>(supported??(()=>WindowCapture.Supported))();
    public void Start(){timer.Tick+=Tick;timer.Start();}
    public async Task<object> ConfigureAsync(PreviewConfiguration config)
    {
        int version=generation;
        await gate.WaitAsync();
        try
        {
            if(version!=generation)return Snapshot();
            if(disposed || !visible() || !Supported){Stop();return Snapshot();}
            heartbeat=Now;rate=config.Rate;timer.Interval=TimeSpan.FromSeconds(1d/rate);
            requested=config.Ids;
            foreach(var key in sources.Keys.Except(config.Ids).ToArray()){sources[key].Dispose();sources.Remove(key);}
            foreach(string id in config.Ids)
            {
                if(sources.ContainsKey(id))continue;
                var window=backend.OpenPreviewWindow(id);
                if(window is not null)sources[id]=new(id,window){Started=Now};
            }
            return Snapshot();
        }
        finally{gate.Release();}
    }
    PreviewStateDto Snapshot()=>new(Supported,sources.Values.Select(s=>new PreviewSourceDto(s.Id,s.Status,s.Updated,s.Bytes is null?null:PreviewConfiguration.ImageOrigin+"/previews/"+s.Token+".jpg?v="+s.Revision))
        .Concat(requested.Except(sources.Keys).Select(id=>new PreviewSourceDto(id,"Unavailable",null,null))).ToArray());
    public byte[]? Read(string uri,string origin)
    {
        if(disposed || !visible())return null;
        return sources.Values.FirstOrDefault(s=>s.Bytes is not null && PreviewConfiguration.CanRead(uri,origin,s.Token) && s.Window.IsValid)?.Bytes;
    }
    public void Stop()
    {
        generation++;
        var old=sources.Values.ToArray();sources.Clear();requested=[];
        foreach(var source in old)source.Bytes=null;
        if(old.Length>0){var pending=DisposeSourcesAsync(old);cleanup=cleanup.IsCompleted?pending:Task.WhenAll(cleanup,pending);}
    }
    public async Task StopAsync(){Stop();await cleanup;}
    async Task DisposeSourcesAsync(Source[] old)
    {
        await gate.WaitAsync();
        try{foreach(var source in old)source.Dispose();}finally{gate.Release();}
    }
    async void Tick(object? sender,EventArgs args)
    {
        try{await PollAsync();}
        catch{Stop();publish(Snapshot());}
    }
    internal async Task PollAsync()
    {
        if(disposed||ticking)return;
        if(!visible() || Now-heartbeat>TimeSpan.FromSeconds(8)){if(sources.Count>0){Stop();publish(Snapshot());}return;}
        if(Now<next || !await gate.WaitAsync(0))return;
        ticking=true;int version=generation;
        try
        {
            foreach(var source in sources.Values.ToArray())
            {
                if(version!=generation)break;
                if(!source.Window.IsValid){source.Bytes=null;source.Status="Closed";source.Capture?.Dispose();source.Capture=null;continue;}
                if(Minimized(source.Window.Handle)){source.Bytes=null;source.Status="Minimized";source.Capture?.Dispose();source.Capture=null;continue;}
                if(source.Status is "Unavailable" or "Capacity limit")continue;
                try
                {
                    source.Capture??=(captureFactory??(window=>new WindowCapture(window)))(source.Window.Handle);
                    if(sources.Values.Sum(s=>s.Capture?.Pixels??0)>MaximumPixels)
                    {source.Bytes=null;source.Status="Capacity limit";source.Capture.Dispose();source.Capture=null;continue;}
                    if(source.Capture.Closed){source.Bytes=null;source.Status="Closed";source.Capture.Dispose();source.Capture=null;continue;}
                    byte[]? frame=await source.Capture.ReadAsync();
                    if(version!=generation)break;
                    if(sources.Values.Sum(s=>s.Capture?.Pixels??0)>MaximumPixels)
                    {source.Bytes=null;source.Status="Capacity limit";source.Capture.Dispose();source.Capture=null;continue;}
                    if(!source.Window.IsValid || Minimized(source.Window.Handle)){source.Bytes=null;source.Status="Unavailable";continue;}
                    if(frame is not null){source.Bytes=frame;source.Revision++;source.Updated=Now;source.Status="Live";}
                    else if(Now-(source.Updated??source.Started)>TimeSpan.FromSeconds(3))source.Status="Stale";
                }
                catch {source.Bytes=null;source.Status="Unavailable";source.Capture?.Dispose();source.Capture=null;}
            }
            if(version==generation)publish(Snapshot());
        }
        finally{next=Now+TimeSpan.FromSeconds(1d/rate);ticking=false;gate.Release();}
    }
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Tick-=Tick;Stop();}
    [DllImport("user32.dll")]static extern bool IsIconic(nint window);
}
