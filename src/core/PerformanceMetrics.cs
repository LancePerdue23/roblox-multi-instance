using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RobloxLaunchHelper;

internal readonly record struct PerformanceCounters(ulong? PrivateCommit, ulong? ReadBytes, ulong? WriteBytes);
internal interface IPerformanceProcess { PerformanceCounters PerformanceCounters(); }
internal sealed record PerformanceSample(ProcessKey Key, double? CpuPercent, ulong? WorkingSet, ulong? PrivateCommit,
    double? ReadRate, double? WriteRate, double UptimeSeconds, string Status);
internal sealed record PerformanceSnapshot(long Generation, DateTimeOffset ObservedAt, bool Available, int Unavailable, PerformanceSample[] Processes);

// One request-driven collector: no background polling and no overlapping samples.
internal sealed class PerformanceCollector(IInstancePlatform platform, string sid, int session,
    Func<double>? seconds = null, Func<DateTimeOffset>? utcNow = null)
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Dictionary<ProcessKey, (long? Cpu, ulong? Read, ulong? Write, double At)> previous = [];
    readonly Func<double> clock = seconds ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
    readonly Func<DateTimeOffset> now = utcNow ?? (() => DateTimeOffset.UtcNow);
    long generation;
    internal async Task<PerformanceSnapshot> CollectAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return await Task.Run(() => Collect(token), token); }
        finally { gate.Release(); }
    }
    PerformanceSnapshot Collect(CancellationToken token)
    {
        var observed = now();
        var result = new List<PerformanceSample>();
        var seen = new HashSet<ProcessKey>();
        int unavailable = 0;
        try
        {
            foreach (int pid in platform.Candidates().Distinct())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var process = platform.Open(pid);
                    var identity = process.Identity;
                    var key = new ProcessKey(pid, identity.Created);
                    if (!InstancePolicy.Matches(identity, sid, session)) continue;
                    if (!platform.Trusted(identity.Image)) { unavailable++; continue; }
                    if (!process.IsAlive || !seen.Add(key)) continue;
                    var cpu = process.CpuTime();
                    var memory = process.WorkingSet();
                    var counters = process is IPerformanceProcess metrics ? metrics.PerformanceCounters() : default;
                    double at = clock();
                    double? percent = null, read = null, write = null;
                    if (previous.TryGetValue(key, out var last))
                    {
                        if (last.Cpu is long oldCpu && cpu is long newCpu)
                            percent = InstanceCollector.CalculateCpu(oldCpu, newCpu, at - last.At, platform.ProcessorCount);
                        read = Rate(last.Read, counters.ReadBytes, at - last.At);
                        write = Rate(last.Write, counters.WriteBytes, at - last.At);
                    }
                    token.ThrowIfCancellationRequested();
                    if (!process.IsAlive) { seen.Remove(key); continue; }
                    previous[key] = (cpu, counters.ReadBytes, counters.WriteBytes, at);
                    result.Add(new(key, percent, memory, counters.PrivateCommit, read, write,
                        Math.Max(0, (observed.UtcDateTime - DateTime.FromFileTimeUtc(key.Created)).TotalSeconds),
                        percent is null ? "First sample or CPU unavailable" : memory is null || counters.PrivateCommit is null ? "Some counters unavailable" : "Current"));
                }
                catch (OperationCanceledException) { throw; }
                catch { unavailable++; }
            }
            foreach (var key in previous.Keys.Where(k => !seen.Contains(k)).ToArray()) previous.Remove(key);
            return new(++generation, observed, true, unavailable, result.OrderBy(x => x.Key.Created).ThenBy(x => x.Key.Id).ToArray());
        }
        catch (OperationCanceledException) { previous.Clear(); throw; }
        catch { previous.Clear(); return new(++generation, observed, false, unavailable, []); }
    }
    internal static double? Rate(ulong? before, ulong? after, double elapsed) =>
        before.HasValue && after.HasValue && after >= before && elapsed > 0 && double.IsFinite(elapsed)
            ? (after.Value - before.Value) / elapsed : null;
}

internal static class PerformanceNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryCounters
    {
        internal uint Size, PageFaults;
        internal nuint PeakWorkingSet, WorkingSet, QuotaPeakPaged, QuotaPaged,
            QuotaPeakNonPaged, QuotaNonPaged, Pagefile, PeakPagefile, PrivateUsage;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters { internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool K32GetProcessMemoryInfo(SafeProcessHandle handle, ref MemoryCounters counters, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessIoCounters(SafeProcessHandle handle, out IoCounters counters);
    internal static PerformanceCounters Read(SafeProcessHandle handle)
    {
        var memory = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
        ulong? commit = K32GetProcessMemoryInfo(handle, ref memory, memory.Size) ? (ulong)memory.PrivateUsage : null;
        bool io = GetProcessIoCounters(handle, out var counters);
        return new(commit, io ? counters.ReadBytes : null, io ? counters.WriteBytes : null);
    }
}
