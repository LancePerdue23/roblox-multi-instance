using System.Runtime.InteropServices;

namespace RobloxLaunchHelper;

internal interface ICompletionNotification : IDisposable
{
    event Action? Clicked;
    bool Show(string summary);
    void Hide();
}

// NotifyIcon.ShowBalloonTip does not expose the Shell's no-sound flag.
// https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-notifyicondataw
internal sealed class CompletionNotification : NativeWindow, ICompletionNotification
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct IconData
    {
        internal uint Size;
        internal nint Window;
        internal uint Id, Flags, Callback;
        internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Title;
        internal uint InfoFlags;
        internal Guid Guid;
        internal nint BalloonIcon;
    }
    internal delegate bool Notify(uint operation, ref IconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotify(uint operation, ref IconData data);
    private const uint CallbackMessage = 0x802A;
    private readonly Notify notify;
    private bool visible, disposed;
    public event Action? Clicked;
    internal CompletionNotification(Notify? notify = null)
    {
        this.notify = notify ?? ShellNotify;
        CreateHandle(new CreateParams { Caption = "Roblox helper notifications" });
    }
    private IconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<IconData>(), Window = Handle, Id = 1, Callback = CallbackMessage,
        Icon = SystemIcons.Application.Handle, Tip = "Roblox helper completion", Info = "", Title = ""
    };
    public bool Show(string summary)
    {
        if (disposed) return false;
        var data = Data();
        if (!visible)
        {
            data.Flags = 1 | 2 | 4; // MESSAGE | ICON | TIP, without a balloon.
            visible = notify(0, ref data); // NIM_ADD
            if (!visible) return false;
        }
        data.Flags = 0x10 | 0x40; // INFO | REALTIME: do not queue stale notifications.
        data.InfoFlags = 1 | 0x10 | 0x80; // INFO | NOSOUND | RESPECT_QUIET_TIME
        int limit = 254 - LaunchStatusText.Limit.Length - 1;
        data.Info = (summary.Length > limit ? summary[..(limit - 1)] + "…" : summary) + "\n" + LaunchStatusText.Limit;
        data.Title = "Roblox helper completion";
        bool ok = notify(1, ref data); // NIM_MODIFY
        if (!ok) Hide();
        return ok; // Shell acceptance is not a claim that Windows displayed it.
    }
    public void Hide()
    {
        if (!visible) return;
        var data = Data(); data.Flags = 0x10; // Empty INFO retracts a pending balloon.
        notify(1, ref data); notify(2, ref data); visible = false;
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == CallbackMessage && message.LParam.ToInt64() is 0x405 or 0x203) Clicked?.Invoke();
        base.WndProc(ref message);
    }
    public void Dispose()
    {
        if (disposed) return;
        Hide(); disposed = true; DestroyHandle();
    }
}
