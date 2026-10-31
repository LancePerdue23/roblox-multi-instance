using RobloxLaunchHelper;

// Programmatic PerformClick calls happen outside Application.Run. Keep their continuations
// on a dedicated message-only control rather than a form's temporary WinForms context.
internal sealed class UiTestContext : SynchronizationContext
{
    [ThreadStatic] private static UiTestContext? context;
    private readonly MessageDispatcher dispatcher=new();
    private readonly int thread=Environment.CurrentManagedThreadId;
    private UiTestContext() { _=dispatcher.Handle; }
    internal static void Install()
    {
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA) throw new InvalidOperationException("UI test actions require the STA driver thread.");
        context ??= new(); SetSynchronizationContext(context);
    }
    private void Run(SendOrPostCallback callback,object? state)
    {
        var previous=Current; SetSynchronizationContext(this);
        try { callback(state); } finally { SetSynchronizationContext(previous); }
    }
    public override void Post(SendOrPostCallback callback,object? state) => dispatcher.BeginInvoke(()=>Run(callback,state));
    public override void Send(SendOrPostCallback callback,object? state)
    { if(Environment.CurrentManagedThreadId==thread) Run(callback,state); else dispatcher.Invoke(()=>Run(callback,state)); }
    public override SynchronizationContext CreateCopy()=>this;
}
