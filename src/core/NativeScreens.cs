using System.ComponentModel;
using System.Runtime.InteropServices;
namespace RobloxLaunchHelper;

internal sealed record NativeScreen(string DeviceName,Rectangle Bounds,Rectangle WorkingArea,bool Primary);
internal static class NativeScreens
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; public readonly Rectangle Value=>Rectangle.FromLTRB(Left,Top,Right,Bottom); }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Info { public int Size;public Rect Monitor,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device; }
    delegate bool Callback(nint monitor,nint dc,nint rect,nint data);
    [DllImport("user32.dll")]static extern bool EnumDisplayMonitors(nint dc,nint clip,Callback callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(nint monitor,ref Info info);
    [DllImport("user32.dll")]static extern nint MonitorFromWindow(nint window,uint flags);
    static NativeScreen Read(nint monitor){var info=new Info{Size=Marshal.SizeOf<Info>(),Device=""};if(!GetMonitorInfo(monitor,ref info))throw new Win32Exception();return new(info.Device,info.Monitor.Value,info.Work.Value,(info.Flags&1)!=0);}
    internal static NativeScreen[] All { get {var screens=new List<NativeScreen>();if(!EnumDisplayMonitors(0,0,(m,_,_,_)=>{screens.Add(Read(m));return true;},0))throw new Win32Exception();return screens.ToArray();} }
    internal static NativeScreen FromHandle(nint window)=>Read(MonitorFromWindow(window,2));
}
