using RobloxMultiInstance.Desktop;

internal static class StartupReadinessTests
{
    internal static async Task<int> RunAsync()
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        var policy=new StartupReadinessPolicy();
        Check(policy.Begin(out int first)&&first==1&&!policy.CanRetry,"Startup begins one active attempt");
        Check(!policy.Begin(out _),"Concurrent startup attempts refused");
        Check(policy.Fail(first)&&policy.CanRetry,"Failed startup permits explicit retry");
        Check(!policy.Ready(first),"Late renderer readiness cannot resurrect failed startup");
        Check(policy.Begin(out int second)&&!policy.Fail(first)&&!policy.Ready(first),"Old attempt callbacks cannot fail or complete replacement");
        Check(policy.Ready(second)&&!policy.CanRetry,"Renderer handshake completes current startup");
        bool failedSecond=policy.Fail(second);bool beganThird=policy.Begin(out int third);
        Check(failedSecond&&beganThird&&third==3,"Runtime failure permits remaining explicit attempt");
        Check(policy.Fail(third)&&!policy.CanRetry&&!policy.Begin(out _),"Startup retry budget stops at three attempts");
        policy.Close();Check(!policy.Ready(third)&&!policy.Fail(third),"Closed startup refuses late callbacks");
        bool canceled=false;var hung=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool timeout=false;
        try { await StartupReadinessPolicy.RunBoundedAsync(token=>{token.Register(()=>canceled=true);return hung.Task;},TimeSpan.FromMilliseconds(30),default); }
        catch(TimeoutException){timeout=true;}
        Check(timeout&&canceled,"Hung browser initialization times out and cancels attempt grant");
        hung.SetException(new InvalidOperationException("Late browser fixture failure"));await Task.Yield();
        using var close=new CancellationTokenSource();close.Cancel();bool closeCanceled=false;
        try{await StartupReadinessPolicy.RunBoundedAsync(_=>Task.Delay(1000),TimeSpan.FromSeconds(1),close.Token);}catch(OperationCanceledException){closeCanceled=true;}
        Check(closeCanceled,"Closing cancels initialization wait without timeout UI");
        await StartupReadinessPolicy.RunBoundedAsync(_=>Task.CompletedTask,TimeSpan.FromSeconds(1),default);Check(true,"Completed initialization does not exhaust deadline");
        return checks;
    }
}
