using RobloxLaunchHelper;
using System.Diagnostics;

namespace RobloxMultiInstance;

public sealed partial class DesktopBackend
{
    public IPreviewWindow? OpenPreviewWindow(string id)
    {
        InstanceTarget target;
        lock(identityLock)
            if(Volatile.Read(ref disposed)!=0 || !targets.TryGetValue(id,out target) || target.Window==0) return null;
        IInstanceProcess? process=null;
        try
        {
            process=platform.Open(target.Key.Id);
            using var current=Process.GetCurrentProcess();
            if(process.Identity.Created!=target.Key.Created || !InstancePolicy.Matches(process.Identity,RobloxLaunchHelper.Program.Sid,current.SessionId)
                || !platform.Trusted(process.Identity.Image) || !process.IsAlive || !process.OwnsWindow(target.Window))
            { process.Dispose(); return null; }
            return new PreviewWindowLease(process,target.Window);
        }
        catch { process?.Dispose(); return null; }
    }

    sealed class PreviewWindowLease(IInstanceProcess process,nint window) : IPreviewWindow
    {
        bool disposed;
        public nint Handle=>window;
        public bool IsValid { get { try { return !disposed && process.IsAlive && process.OwnsWindow(window); } catch { return false; } } }
        public void Dispose(){if(disposed)return;disposed=true;process.Dispose();}
    }
}
