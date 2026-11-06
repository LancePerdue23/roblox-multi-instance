using System;
using System.Threading;
using System.Threading.Tasks;

namespace RobloxMultiInstance.Desktop;

internal static class AccountSignInCleanup
{
    internal static async Task<bool> RunAsync(Task? initialization,Func<Task> clear,Action dispose,TimeSpan timeout)
    {
        bool cleaned=false;
        try
        {
            using var deadline=new CancellationTokenSource(timeout);
            if(initialization is not null)await initialization.WaitAsync(deadline.Token);
            await clear().WaitAsync(deadline.Token);
            cleaned=true;
        }
        catch { /* Browser failure must still release the sign-in operation. */ }
        finally { try { dispose(); } catch { cleaned=false; } }
        return cleaned;
    }
}
