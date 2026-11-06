using System;
using System.Threading;
using System.Threading.Tasks;

namespace RobloxMultiInstance.Desktop;

// UI-thread state policy; no WebView2/WPF dependency so failure races use fixtures.
internal sealed class StartupReadinessPolicy
{
    internal const int MaximumAttempts=3;
    internal static readonly TimeSpan Deadline=TimeSpan.FromSeconds(20);
    internal int Attempts { get; private set; }
    internal string State { get; private set; }="Idle";
    internal bool CanRetry=>(State is "Idle" or "Failed") && Attempts<MaximumAttempts;
    internal bool Begin(out int attempt)
    {
        attempt=Attempts;
        if(!CanRetry)return false;
        attempt=++Attempts;State="Starting";return true;
    }
    internal bool IsStarting(int attempt)=>attempt==Attempts&&State=="Starting";
    internal bool Ready(int attempt)
    {
        if(!IsStarting(attempt))return false;
        State="Ready";return true;
    }
    internal bool Fail(int attempt)
    {
        if(attempt!=Attempts||State is not ("Starting" or "Ready"))return false;
        State="Failed";return true;
    }
    internal void Close()=>State="Closed";
    internal static async Task RunBoundedAsync(Func<CancellationToken,Task> work,TimeSpan timeout,CancellationToken token)
    {
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token);
        Task? pending=null;
        try { token.ThrowIfCancellationRequested();pending=work(lifetime.Token);await pending.WaitAsync(timeout,token); }
        finally
        {
            lifetime.Cancel();
            // Runtime initialization has no cancellation overload. Observe late faults;
            // callers must check the attempt token before touching controls after awaits.
            if(pending is not null)_=pending.ContinueWith(t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        }
    }
}
