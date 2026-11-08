using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RobloxLaunchHelper;

internal sealed class InstancePlatform : IInstancePlatform
{
    public int[] Candidates()
    {
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try { return processes.Select(p => p.Id).ToArray(); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public IInstanceProcess Open(int pid) => new InstanceProcess(pid);
    public bool Trusted(string image) => RobloxTrust.IsSignedRoblox(image);
    // All processor groups: denominator is total machine logical capacity, not the
    // caller's affinity-limited Environment.ProcessorCount.
    public int ProcessorCount => checked((int)InstanceNative.GetActiveProcessorCount(0xffff));
}

internal sealed class InstanceProcess : IBackgroundClientProcess, IPerformanceProcess
{
    private readonly SafeProcessHandle handle;
    private readonly int pid;
    public Identity Identity { get; }
    internal InstanceProcess(int pid, bool allowStop = false)
    {
        this.pid = pid;
        handle = Native.OpenProcess(InstanceNative.QueryLimitedInformation | InstanceNative.Synchronize |
            (allowStop ? 0x0001u : 0u), false, pid); // PROCESS_TERMINATE for cleanup or confirmed Stop All.
        try
        {
            if (handle.IsInvalid) throw new Win32Exception();
            Identity = Native.GetIdentity(handle, pid);
        }
        catch { handle.Dispose(); throw; }
    }
    public bool IsAlive => InstanceNative.WaitForSingleObject(handle, 0) == InstanceNative.WaitTimeout;
    public bool HasVisibleWindow() => InstanceNative.HasVisibleWindow(pid);
    public bool Stop() => InstanceNative.TerminateProcess(handle, 1);
    public bool WaitForExit(uint milliseconds) => InstanceNative.WaitForSingleObject(handle, milliseconds) == 0;
    public long? CpuTime()
    {
        if (!InstanceNative.GetProcessTimes(handle, out _, out _, out long kernel, out long user)) return null;
        try { return checked(kernel + user); } catch (OverflowException) { return null; }
    }
    public ulong? WorkingSet()
    {
        var counters = new InstanceNative.MemoryCounters { Size = (uint)Marshal.SizeOf<InstanceNative.MemoryCounters>() };
        return InstanceNative.K32GetProcessMemoryInfo(handle, ref counters, counters.Size) ? (ulong)counters.WorkingSet : null;
    }
    public PerformanceCounters PerformanceCounters() => PerformanceNative.Read(handle);
    public bool OwnsWindow(nint window) => IsAlive && InstanceNative.EligibleWindow(window, pid);
    public InstanceWindow[] Windows()
    {
        var result = new List<InstanceWindow>();
        InstanceNative.EnumWindowCallback callback = (window, _) =>
        {
            if (!InstanceNative.EligibleWindow(window, pid)) return true;
            // For another process GetWindowText retrieves the caption without sending
            // an unbounded cross-process WM_GETTEXT to its potentially hung thread.
            var title = new StringBuilder(1025);
            InstanceNative.GetWindowText(window, title, title.Capacity);
            if (OwnsWindow(window)) result.Add(new(window, title.ToString()));
            return true;
        };
        if (!InstanceNative.EnumWindows(callback, 0)) throw new Win32Exception();
        return result.ToArray();
    }
    public void Restore(nint window)
    {
        if (InstanceNative.IsIconic(window)) InstanceNative.ShowWindowAsync(window, 9); // SW_RESTORE
    }
    public bool Focus(nint window) => InstanceNative.SetForegroundWindow(window);
    public bool Close(nint window) => InstanceNative.SendMessageTimeout(window, 0x0010, 0, 0,
        0x0002 | 0x0020, 500, out _) != 0; // WM_CLOSE, ABORTIFHUNG | ERRORONEXIT; never broadcast.
    public void Dispose() => handle.Dispose();
}

internal static class InstanceNative
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    internal const uint QueryLimitedInformation = 0x1000, Synchronize = 0x100000, WaitTimeout = 258;
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryCounters
    {
        internal uint Size, PageFaults;
        internal nuint PeakWorkingSet, WorkingSet, QuotaPeakPaged, QuotaPaged,
            QuotaPeakNonPaged, QuotaNonPaged, Pagefile, PeakPagefile;
    }
    internal delegate bool EnumWindowCallback(nint window, nint parameter);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool K32GetProcessMemoryInfo(SafeProcessHandle process, ref MemoryCounters counters, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint window, out int pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    internal static extern int GetWindowText(nint window, StringBuilder title, int capacity);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    internal static extern nint SendMessageTimeout(nint window, uint message, nuint wparam, nint lparam,
        uint flags, uint milliseconds, out nuint result);

    internal static bool HasVisibleWindow(int pid)
    {
        bool found = false;
        EnumWindowCallback callback = (window, _) =>
        {
            if (GetWindowThreadProcessId(window, out int owner) != 0 && owner == pid && IsWindowVisible(window)) found = true;
            return true;
        };
        if (!EnumWindows(callback, 0)) throw new Win32Exception();
        GC.KeepAlive(callback);
        return found;
    }

    internal static bool EligibleWindow(nint window, int pid) => window != 0 &&
        GetWindowThreadProcessId(window, out int owner) != 0 && owner == pid &&
        IsWindowVisible(window) && GetWindow(window, 4) == 0 && // GW_OWNER
        (GetWindowLongPtr(window, -16).ToInt64() & 0x40000000) == 0 && // WS_CHILD
        (GetWindowLongPtr(window, -20).ToInt64() & 0x80) == 0; // WS_EX_TOOLWINDOW
}
