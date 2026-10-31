namespace RobloxLaunchHelper;

// Discovery never waits on log or network access. Cache is accessed only by its owning worker.
internal sealed class AccountEnrichment : IDisposable
{
    readonly CancellationTokenSource stop=new();
    readonly SemaphoreSlim limit=new(4,4);
    readonly object sync=new();
    readonly object delivery=new();
    readonly Dictionary<ProcessKey,Task> pending=[];
    readonly Dictionary<ProcessKey,(string? Name,DateTime Next)> cache=[];
    readonly Func<ProcessKey,CancellationToken,string?> resolve;
    HashSet<ProcessKey> live=[];
    long generation;
    internal event Action<long,ProcessKey,string>? Resolved;
    internal AccountEnrichment(Func<ProcessKey,CancellationToken,string?>? resolve=null)
        => this.resolve=resolve??((key,token)=>new InstanceAccounts().Resolve(key,token));
    internal string? Cached(ProcessKey key) { lock(sync)return cache.GetValueOrDefault(key).Name; }
    internal void Schedule(IEnumerable<ProcessKey> keys,long version)
    {
        lock(sync)
        {
            if(stop.IsCancellationRequested)return;
            generation=version;live=[..keys];
            foreach(var key in cache.Keys.Where(k=>!live.Contains(k)).ToArray())cache.Remove(key);
            foreach(var key in live)
            {
                if(pending.ContainsKey(key)||cache.TryGetValue(key,out var value)&&value.Next>DateTime.UtcNow)continue;
                pending[key]=Task.Run(()=>RunAsync(key));
            }
        }
    }
    async Task RunAsync(ProcessKey key)
    {
        bool entered=false;
        try
        {
            await limit.WaitAsync(stop.Token);entered=true;
            lock(sync)if(!live.Contains(key))return;
            string? name=await Task.Run(()=>resolve(key,stop.Token),stop.Token);
            long version;
            lock(sync)
            {
                if(stop.IsCancellationRequested||!live.Contains(key))return;
                cache[key]=(name,DateTime.UtcNow.AddSeconds(name is null?10:30));
                version=generation;
            }
            lock(delivery)
            {
                lock(sync)if(stop.IsCancellationRequested||!live.Contains(key)||version!=generation)return;
                if(name is not null)Resolved?.Invoke(version,key,name);
            }
        }
        catch(OperationCanceledException) { }
        catch { lock(sync)if(live.Contains(key))cache[key]=(null,DateTime.UtcNow.AddSeconds(10)); }
        finally { if(entered)limit.Release();lock(sync)pending.Remove(key); }
    }
    internal async Task StopAsync() { Task[] jobs;lock(sync){live.Clear();generation++;jobs=pending.Values.ToArray();}await Task.WhenAll(jobs); }
    public void Dispose() { lock(delivery)lock(sync){stop.Cancel();live.Clear();Resolved=null;} }
}
