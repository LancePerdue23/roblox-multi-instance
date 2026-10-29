using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace RobloxLaunchHelper;

internal readonly record struct ProcessKey(int Id, long Created);
internal readonly record struct HandleEntry(nint Object, int ProcessId, nint Handle, ushort Type);
internal sealed record Identity(string Sid, int Session, bool Elevated, string Image, long Created);

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(SafeProcessHandle source, nint handle, nint target, out nint copy, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, System.Text.StringBuilder image, ref int length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ProcessIdToSessionId(int pid, out int session);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int pid);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out int pid);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool GetTokenInformation(SafeAccessTokenHandle token, int info, nint data, int size, out int required);
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int info, nint data, int size, out int required);
    [DllImport("ntdll.dll")] private static extern int NtQueryObject(nint handle, int info, nint data, int size, out int required);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disableAll, ref Privileges state, int size, out Privileges previous, out int required);
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct Privileges { public int Count; public long Luid; public int Attributes; }

    internal static IDisposable DebugPrivilege()
    {
        using var current = OpenProcess(0x1000, false, Environment.ProcessId);
        if (!OpenProcessToken(current, 0x28, out var token)) throw new Win32Exception();
        if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out long luid)) { token.Dispose(); throw new Win32Exception(); }
        var state = new Privileges { Count = 1, Luid = luid, Attributes = 2 };
        bool ok = AdjustTokenPrivileges(token, false, ref state, Marshal.SizeOf<Privileges>(), out var previous, out _);
        int error = Marshal.GetLastWin32Error();
        if (!ok || error == 1300) { token.Dispose(); throw new Win32Exception(error); }
        return new RestorePrivilege(token, previous);
    }
    private sealed class RestorePrivilege(SafeAccessTokenHandle token, Privileges previous) : IDisposable
    {
        public void Dispose()
        {
            AdjustTokenPrivileges(token, false, ref previous, Marshal.SizeOf<Privileges>(), out _, out _);
            token.Dispose();
        }
    }

    internal static Identity GetIdentity(int pid)
    {
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid) throw new Win32Exception();
        return GetIdentity(process, pid);
    }

    internal static Identity GetIdentity(SafeProcessHandle process, int pid)
    {
        if (!GetProcessTimes(process, out long created, out _, out _, out _)) throw new Win32Exception();
        if (!ProcessIdToSessionId(pid, out int session)) throw new Win32Exception();
        var name = new System.Text.StringBuilder(32768); int size = name.Capacity;
        if (!QueryFullProcessImageName(process, 0, name, ref size)) throw new Win32Exception();
        if (!OpenProcessToken(process, 8, out var token)) throw new Win32Exception();
        using (token)
        {
            GetTokenInformation(token, 1, 0, 0, out int required);
            nint data = Marshal.AllocHGlobal(Math.Max(required, 16));
            try
            {
                if (!GetTokenInformation(token, 1, data, required, out _)) throw new Win32Exception();
                string sid = new SecurityIdentifier(Marshal.ReadIntPtr(data)).Value;
                if (!GetTokenInformation(token, 20, data, 4, out _)) throw new Win32Exception();
                return new Identity(sid, session, Marshal.ReadInt32(data) != 0, name.ToString(), created);
            }
            finally { Marshal.FreeHGlobal(data); }
        }
    }

    internal static List<HandleEntry> Snapshot()
    {
        int size = 1024 * 1024;
        while (size <= 256 * 1024 * 1024)
        {
            nint data = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQuerySystemInformation(64, data, size, out int needed);
                if (status == unchecked((int)0xC0000004)) { size = Math.Max(checked(size * 2), checked(needed + 65536)); continue; }
                if (status < 0) throw new InvalidOperationException($"Handle enumeration failed (0x{status:X8}).");
                long count = Marshal.ReadInt64(data);
                if (count < 0 || count > (size - 16L) / 40) throw new InvalidOperationException("Invalid handle table.");
                var result = new List<HandleEntry>((int)count);
                for (int i = 0; i < count; i++)
                {
                    nint row = data + 16 + i * 40;
                    long pid = Marshal.ReadInt64(row, 8);
                    if (pid > 0 && pid <= int.MaxValue)
                        result.Add(new(Marshal.ReadIntPtr(row), (int)pid, Marshal.ReadIntPtr(row, 16), (ushort)Marshal.ReadInt16(row, 30)));
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        throw new InvalidOperationException("Handle table exceeded the size limit.");
    }

    internal static string? ObjectString(nint handle, int info)
    {
        int size = 1024;
        while (size <= 65536)
        {
            nint data = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQueryObject(handle, info, data, size, out int needed);
                if (status is unchecked((int)0xC0000004) or unchecked((int)0x80000005) or unchecked((int)0xC0000023))
                { size = Math.Max(size * 2, needed); continue; }
                if (status < 0) return null;
                ushort length = (ushort)Marshal.ReadInt16(data);
                nint buffer = Marshal.ReadIntPtr(data, 8);
                if (length == 0) return "";
                if (buffer < data || buffer.ToInt64() + length > data.ToInt64() + size || length % 2 != 0) return null;
                return Marshal.PtrToStringUni(buffer, length / 2);
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        return null;
    }

    internal static bool IsTargetEvent(string? type, string? name) => type == "Event" &&
        name is not null && name[(name.LastIndexOf('\\') + 1)..] == "ROBLOX_singletonEvent";

    // Kept internal for the controlled-process tests. Production callers first check owner,
    // session, creation time, executable basename, and the Roblox Authenticode signature.
    internal static int CloseTargetEvents(SafeProcessHandle process, ProcessKey key)
    {
        if (GetIdentity(process, key.Id).Created != key.Created) return 0;
        using var marker = new EventWaitHandle(false, EventResetMode.ManualReset);
        var snapshot = Snapshot();
        var markerEntry = snapshot.Single(x => x.ProcessId == Environment.ProcessId && x.Handle == marker.SafeWaitHandle.DangerousGetHandle());
        int closed = 0;
        foreach (var entry in snapshot.Where(x => x.ProcessId == key.Id && x.Type == markerEntry.Type).Take(4096))
        {
            if (entry.Object == 0) continue; // Cannot safely revalidate a hidden kernel object address.
            if (!DuplicateHandle(process, entry.Handle, GetCurrentProcess(), out nint copy, 0, false, 2)) continue;
            try
            {
                if (!IsTargetEvent(ObjectString(copy, 2), ObjectString(copy, 1))) continue;
                if (GetIdentity(process, key.Id).Created != key.Created) break;
                var current = Snapshot().FirstOrDefault(x => x.ProcessId == key.Id && x.Handle == entry.Handle);
                if (current.Object != entry.Object || current.Type != entry.Type) continue;
                // Holding copy keeps the object alive while its identity is rechecked.
                // Windows offers no atomic compare-object-and-close-remote-handle API.
                bool ok = DuplicateHandle(process, entry.Handle, GetCurrentProcess(), out nint finalCopy, 0, false, 3);
                if (finalCopy != 0) CloseHandle(finalCopy);
                if (!ok) { Log.Write("close-result-uncertain", "", key.Id); break; }
                closed++;
            }
            finally { CloseHandle(copy); }
        }
        return closed;
    }
}

internal static class RobloxTrust
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public nint Handle; public nint Subject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size; public nint Policy, Sip; public uint Ui, Revocation, Choice; public nint File;
        public uint StateAction; public nint State; public nint Url; public uint Flags, Context;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(nint hwnd, ref Guid action, ref TrustData data);

    internal static bool IsSignedRoblox(string path)
    {
        if (!string.Equals(Path.GetFileName(path), "RobloxPlayerBeta.exe", StringComparison.OrdinalIgnoreCase)) return false;
        // Keep the verified on-disk file from being overwritten or renamed during verification.
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path, Handle = locked.SafeFileHandle.DangerousGetHandle() };
        nint info = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        Marshal.StructureToPtr(file, info, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = info, StateAction = 1, Flags = 0x1000 };
        Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            if (WinVerifyTrust(new nint(-1), ref action, ref data) != 0) return false;
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, false), "Roblox Corporation", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            data.StateAction = 2; WinVerifyTrust(new nint(-1), ref action, ref data);
            Marshal.DestroyStructure<FileInfo>(info); Marshal.FreeHGlobal(info);
        }
    }
}
