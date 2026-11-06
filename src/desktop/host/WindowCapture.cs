using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace RobloxMultiInstance.Desktop;

// One bounded frame pool per source; the owner serializes reads and disposal.
internal interface IPreviewCapture : IDisposable
{
    bool Closed {get;}
    long Pixels {get;}
    Task<byte[]?> ReadAsync();
}
internal sealed class WindowCapture : IPreviewCapture
{
    readonly IDirect3DDevice device;
    readonly GraphicsCaptureItem item;
    readonly Direct3D11CaptureFramePool pool;
    readonly GraphicsCaptureSession session;
    Windows.Graphics.SizeInt32 size;
    volatile bool closed;
    bool disposed;
    public bool Closed=>closed;
    public long Pixels=>(long)size.Width*size.Height;
    public static bool Supported {get {try{return GraphicsCaptureSession.IsSupported();}catch{return false;}}}
    public WindowCapture(nint window)
    {
        device=CreateDevice();
        try
        {
            var factory=ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            var interopId=new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr,in interopId,out nint interop));
            var iid=new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            try
            {
                var create=Marshal.GetDelegateForFunctionPointer<CreateForWindow>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(interop),3*IntPtr.Size));
                Marshal.ThrowExceptionForHR(create(interop,window,ref iid,out nint pointer));
                try { item=GraphicsCaptureItem.FromAbi(pointer); } finally {Marshal.Release(pointer);}
            }
            finally {Marshal.Release(interop);GC.KeepAlive(factory);}
            size=item.Size;
            CheckSize(size);
            pool=Direct3D11CaptureFramePool.CreateFreeThreaded(device,DirectXPixelFormat.B8G8R8A8UIntNormalized,1,size);
            try
            {
                session=pool.CreateCaptureSession(item);
                item.Closed+=OnClosed;
                try { session.StartCapture(); } catch {item.Closed-=OnClosed;session.Dispose();throw;}
            }
            catch {pool.Dispose();throw;}
        }
        catch {device.Dispose();throw;}
    }
    void OnClosed(GraphicsCaptureItem sender,object args)=>closed=true;
    static void CheckSize(Windows.Graphics.SizeInt32 size)
    {
        if(size.Width<=0 || size.Height<=0 || size.Width>4096 || size.Height>2160)throw new InvalidOperationException("Source dimensions exceed preview limits.");
    }
    public async Task<byte[]?> ReadAsync()
    {
        if(Closed)return null;
        using var frame=pool.TryGetNextFrame();
        if(frame is null)return null;
        CheckSize(frame.ContentSize);
        if(frame.ContentSize.Width!=size.Width || frame.ContentSize.Height!=size.Height)
        {
            size=frame.ContentSize;
            // Drop the old-size frame. The next poll uses the recreated buffers.
            frame.Dispose();pool.Recreate(device,DirectXPixelFormat.B8G8R8A8UIntNormalized,1,size);return null;
        }
        using var bitmap=await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
        using var stream=new InMemoryRandomAccessStream();
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,stream);
        encoder.SetSoftwareBitmap(bitmap);
        double scale=Math.Min(1,Math.Min(480d/bitmap.PixelWidth,270d/bitmap.PixelHeight));
        encoder.BitmapTransform.ScaledWidth=(uint)Math.Max(1,bitmap.PixelWidth*scale);
        encoder.BitmapTransform.ScaledHeight=(uint)Math.Max(1,bitmap.PixelHeight*scale);
        encoder.IsThumbnailGenerated=false;
        await encoder.FlushAsync();
        if(stream.Size>256*1024)throw new InvalidOperationException("Preview frame exceeds limit.");
        stream.Seek(0);
        using var reader=new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        byte[] bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);return bytes;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;closed=true;
        // A source/device can disappear while disposing. Always release every owned object.
        try{item.Closed-=OnClosed;}catch{}
        try{session.Dispose();}catch{}
        try{pool.Dispose();}catch{}
        try{device.Dispose();}catch{}
    }

    static IDirect3DDevice CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0,1,0,0x20,0,0,7,out nint device,out _,out nint context));
        nint dxgi=0,inspectable=0;
        try
        {
            var iid=new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device,in iid,out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out inspectable));
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally{if(inspectable!=0)Marshal.Release(inspectable);if(dxgi!=0)Marshal.Release(dxgi);Marshal.Release(context);Marshal.Release(device);}
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateForWindow(nint self,nint window,ref Guid iid,out nint result);
    [DllImport("d3d11.dll")]static extern int D3D11CreateDevice(nint adapter,int type,nint software,uint flags,nint levels,uint count,uint version,out nint device,out int level,out nint context);
    [DllImport("d3d11.dll")]static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi,out nint device);
}
