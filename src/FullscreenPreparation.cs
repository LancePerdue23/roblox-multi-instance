using System.Runtime.InteropServices;

namespace RobloxLaunchHelper;

internal enum WindowedResult { Ready, FocusDenied, KeysHeld, Interrupted, Changed, InputDenied, TimedOut }
internal interface IFullscreenInput
{
    nint Foreground { get; }
    bool ManagerWindow(nint window);
    bool Focus(nint window);
    bool Fullscreen(nint window);
    bool KeysReleased { get; }
    bool Toggle(nint window);
}

// Only used following Apply. Input goes through Windows' actual input queue, after
// foreground and process-lifetime checks, rather than a synthetic window message.
internal static class FullscreenPreparation
{
    internal static WindowedResult Request(nint window, Func<bool> valid, IFullscreenInput input,
        CancellationToken token, Action<int>? wait = null)
    {
        wait ??= milliseconds => { if (token.WaitHandle.WaitOne(milliseconds)) token.ThrowIfCancellationRequested(); };
        nint original = input.Foreground;
        bool tookFocus = false;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!valid()) return WindowedResult.Changed;
            if (!input.Fullscreen(window)) return WindowedResult.Ready;
            // Apply must still be the active interaction. Do not interrupt another app.
            if (original != window && !input.ManagerWindow(original)) return WindowedResult.Interrupted;
            if (!input.KeysReleased) return WindowedResult.KeysHeld;
            if (!valid()) return WindowedResult.Changed;
            if (original != window) { input.Focus(window); tookFocus = true; }
            for (int i = 0; i < 10; i++)
            {
                token.ThrowIfCancellationRequested();
                if (!valid()) return WindowedResult.Changed;
                nint foreground = input.Foreground;
                if (foreground == window) break;
                // GetForegroundWindow can be NULL during a legitimate activation transition.
                // Wait for activation; a concrete unrelated foreground window still aborts.
                if (foreground != 0 && foreground != original && !input.ManagerWindow(foreground)) return WindowedResult.Interrupted;
                wait(50);
            }
            if (input.Foreground != window) return WindowedResult.FocusDenied;
            wait(100); // Let the client's activation handler run before sending its shortcut.
            token.ThrowIfCancellationRequested();
            if (!valid()) return WindowedResult.Changed;
            // The client may have left fullscreen while focus was changing: never toggle it back.
            if (!input.Fullscreen(window)) return WindowedResult.Ready;
            if (!input.KeysReleased) return WindowedResult.KeysHeld;
            if (!valid() || input.Foreground != window) return WindowedResult.Interrupted;
            if (!input.Toggle(window)) return WindowedResult.InputDenied;
            for (int i = 0; i < 40; i++)
            {
                token.ThrowIfCancellationRequested();
                if (!valid()) return WindowedResult.Changed;
                nint foreground = input.Foreground;
                if (foreground != 0 && foreground != window) return WindowedResult.Interrupted;
                if (foreground == 0) { wait(50); continue; }
                if (!input.Fullscreen(window)) return WindowedResult.Ready;
                wait(50);
            }
            return WindowedResult.TimedOut;
        }
        finally
        {
            // Return focus only while it is still ours to return. Respect a user switching away.
            if (tookFocus && input.Foreground == window && input.ManagerWindow(original)) input.Focus(original);
        }
    }
}

internal sealed class NativeFullscreenInput : IFullscreenInput
{
    public nint Foreground => GetForegroundWindow();
    public bool ManagerWindow(nint window) => window != 0 &&
        InstanceNative.GetWindowThreadProcessId(window, out int pid) != 0 && pid == Environment.ProcessId;
    public bool Focus(nint window) => InstanceNative.SetForegroundWindow(window);
    public bool Fullscreen(nint window) => PlacementNative.Fullscreen(window);
    public bool KeysReleased => new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c, 0x7a }.All(key => (GetAsyncKeyState(key) & 0x8000) == 0);
    public bool Toggle(nint window)
    {
        if (Foreground != window || !KeysReleased) return false;
        ushort scan = checked((ushort)MapVirtualKey(0x7a, 0));
        var down = new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Scan = scan, Flags = 0x0008 } } };
        var up = down; up.Data.Keyboard.Flags |= 0x0002;
        if (SendInput(1, [down], Marshal.SizeOf<Input>()) != 1) return false;
        bool released;
        try
        {
            // Give a game that samples keyboard state on its render loop a real press
            // interval; an immediate down/up can be invisible between two frames.
            Thread.Sleep(150);
        }
        finally { released = SendInput(1, [up], Marshal.SizeOf<Input>()) == 1; }
        return released;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse; // INPUT union must retain native size/alignment.
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { internal ushort Key, Scan; internal uint Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
