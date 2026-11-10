using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace RobloxMultiInstance.Desktop;

internal sealed class ShortcutOwner : IDisposable
{
    private readonly HwndSource source;
    private readonly Dictionary<int, string> commands = new();
    private Mutex? owner;
    public event Action<string>? Pressed;
    public bool Enabled => commands.Count > 0;
    public bool Requested { get; private set; }
    public int Registered => commands.Count;
    public int Total { get; private set; }
    public string[] Conflicts { get; private set; } = [];
    private JsonElement bindings;
    private bool capturing;
    public string Status => capturing && Requested ? "capturing" : !Requested ? "disabled" : Registered == 0 ? "failed" : Registered < Total ? "partial" : "enabled";
    public void Capture(bool value)
    {
        capturing = value;
        if (value) ReleaseRegistrations(false);
        else if (Requested) Enable(bindings);
    }
    public ShortcutOwner(Window window)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        source.AddHook(Hook);
    }
    public string[] Enable(JsonElement bindings)
    {
        ReleaseRegistrations();
        this.bindings = bindings.Clone();
        Requested = true;
        Total = bindings.EnumerateObject().Count();
        Conflicts = [];
        if (capturing) return [];
        using var process = Process.GetCurrentProcess();
        string name = @"Local\RobloxLaunchHelper-WindowShortcuts-" + WindowsIdentity.GetCurrent().User!.Value + "-" + process.SessionId;
        var candidate = new Mutex(false, name);
        bool held;
        try { held = candidate.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) { candidate.Dispose(); return Conflicts = ["Another app instance owns global shortcuts. Disable its shortcuts or exit that instance, then retry."]; }
        owner = candidate;
        var conflicts = new List<string>();
        int id = 1;
        foreach (var item in bindings.EnumerateObject())
        {
            int key = item.Value.GetInt32();
            uint code = (uint)(key & 0xffff);
            uint modifiers = 0x4000;
            if ((key & 0x10000) != 0) modifiers |= 4;
            if ((key & 0x20000) != 0) modifiers |= 2;
            if ((key & 0x40000) != 0) modifiers |= 1;
            if (RegisterHotKey(source.Handle, id, modifiers, code)) commands[id] = item.Name;
            else
            {
                int error = Marshal.GetLastPInvokeError();
                string reason = error == 1409 ? "This combination is already registered by another app or Windows. Release that registration or choose another combination." : new System.ComponentModel.Win32Exception(error).Message;
                conflicts.Add($"{item.Name} ({Label(key)}): {reason} (Windows error {error}). Retry after resolving the Windows registration failure.");
            }
            id++;
        }
        Conflicts = conflicts.ToArray();
        if (commands.Count == 0) ReleaseRegistrations();
        return Conflicts;
    }
    public void Disable()
    {
        Requested = false; capturing = false; Conflicts = []; Total = 0;
        ReleaseRegistrations();
    }
    private void ReleaseRegistrations(bool releaseOwner = true)
    {
        foreach (int id in commands.Keys) UnregisterHotKey(source.Handle, id);
        commands.Clear();
        if (releaseOwner && owner is not null) { owner.ReleaseMutex(); owner.Dispose(); owner = null; }
    }
    private static string Label(int key)
    {
        int code = key & 0xffff;
        var parts = new List<string>();
        if ((key & 0x20000) != 0) parts.Add("Ctrl");
        if ((key & 0x40000) != 0) parts.Add("Alt");
        if ((key & 0x10000) != 0) parts.Add("Shift");
        parts.Add(code is >= 112 and <= 122 ? "F" + (code - 111) : ((char)code).ToString());
        return string.Join(" + ", parts);
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && commands.TryGetValue(wParam.ToInt32(), out var command)) { Pressed?.Invoke(command); handled = true; }
        return 0;
    }
    public void Dispose() { Disable(); source.RemoveHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
