using System;
using System.Collections.Generic;
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
    public bool Enabled => owner is not null;
    public ShortcutOwner(Window window)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        source.AddHook(Hook);
    }
    public string[] Enable(JsonElement bindings)
    {
        Disable();
        using var process = Process.GetCurrentProcess();
        string name = @"Local\RobloxLaunchHelper-WindowShortcuts-" + WindowsIdentity.GetCurrent().User!.Value + "-" + process.SessionId;
        var candidate = new Mutex(false, name);
        bool held;
        try { held = candidate.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) { candidate.Dispose(); return new[] { "The original app owns global shortcuts. Disable its tray mode, then retry here." }; }
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
            else conflicts.Add(item.Name + ": shortcut is unavailable; choose another combination.");
            id++;
        }
        return conflicts.ToArray();
    }
    public void Disable()
    {
        foreach (int id in commands.Keys) UnregisterHotKey(source.Handle, id);
        commands.Clear();
        if (owner is not null) { owner.ReleaseMutex(); owner.Dispose(); owner = null; }
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
