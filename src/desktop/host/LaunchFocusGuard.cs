using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RobloxMultiInstance.Desktop;

// Only reclaim focus from a new player launched while the manager was active.
// A deliberate switch to any other window ends the guard for this launch.
internal sealed class LaunchFocusGuard : IDisposable
{
    static int suspensions;
    internal static bool IsSuspended => Volatile.Read(ref suspensions)>0;
    internal static IDisposable Suspend(){Interlocked.Increment(ref suspensions);return new Suspension();}
    sealed class Suspension : IDisposable {int disposed;public void Dispose(){if(Interlocked.Exchange(ref disposed,1)==0)Interlocked.Decrement(ref suspensions);}}
    readonly Window owner;
    readonly CancellationToken closed;
    readonly DispatcherTimer timer;
    readonly DateTime started = DateTime.UtcNow;
    readonly IntPtr manager;
    DateTime deadline = DateTime.MaxValue;

    LaunchFocusGuard(Window owner, CancellationToken closed)
    {
        this.owner = owner; this.closed = closed;
        manager = new WindowInteropHelper(owner).Handle;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, Tick, owner.Dispatcher);
    }

    internal static LaunchFocusGuard? Start(Window owner, CancellationToken closed) =>
        owner.IsActive && owner.IsVisible && owner.WindowState != WindowState.Minimized ? new(owner, closed) : null;

    // A client can create its window after the backend reports the launch.
    internal void Complete() => deadline = DateTime.UtcNow.AddSeconds(8);

    void Tick(object? sender, EventArgs args)
    {
        if(closed.IsCancellationRequested || !owner.IsVisible || owner.WindowState == WindowState.Minimized || DateTime.UtcNow >= deadline)
        { Dispose(); return; }
        if(IsSuspended)return;
        var foreground = GetForegroundWindow();
        if(foreground == IntPtr.Zero || foreground == manager) return;
        try
        {
            GetWindowThreadProcessId(foreground, out uint pid);
            using var process = Process.GetProcessById(checked((int)pid));
            if(process.Id == Environment.ProcessId) return; // Owned host dialogs.
            if(process.ProcessName != "RobloxPlayerBeta" || process.StartTime.ToUniversalTime() < started)
            { Dispose(); return; }
            // Recheck after process inspection; do not override an intervening switch.
            if(!IsSuspended && GetForegroundWindow() == foreground) SetForegroundWindow(manager);
        }
        catch(ArgumentException) { } // Window/process exited between samples.
        catch(InvalidOperationException) { }
        catch(System.ComponentModel.Win32Exception) { Dispose(); }
    }

    public void Dispose() => timer.Stop();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetForegroundWindow(IntPtr window);
}
