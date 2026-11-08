using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace RobloxMultiInstance.Desktop;
internal static class BadgeActivation
{
    internal static string? Parse(string[] args)
    {
        int claim=Array.IndexOf(args,"--claim-badge");
        if(claim>=0&&(claim+1>=args.Length||args.Length!=(Array.IndexOf(args,"--safe-rendering")>=0?3:2)))throw new ArgumentException("Use --claim-badge followed by the recognition file path.");
        string? path=claim>=0?args[claim+1]:args.Length==1&&args[0].EndsWith(".rmibadge",StringComparison.OrdinalIgnoreCase)?args[0]:null;
        return path is null?null:Validate(path);
    }
    internal static string Validate(string path)
    {
        if(path.Length>4096||path.IndexOf('\0')>=0||!Path.IsPathFullyQualified(path)||!path.EndsWith(".rmibadge",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Open an original .rmibadge file using its full path.");
        return path;
    }
    internal static void Write(Stream stream,string path)
    {
        var bytes=Encoding.UTF8.GetBytes(Validate(path));if(bytes.Length>8192)throw new ArgumentException("Recognition path is too long.");
        stream.WriteByte(3);stream.Write(BitConverter.GetBytes(bytes.Length));stream.Write(bytes);
    }
    internal static async Task<string> ReadAsync(Stream stream,CancellationToken token)
    {
        byte[] size=new byte[4];await stream.ReadExactlyAsync(size,token);int length=BitConverter.ToInt32(size);
        if(length is <1 or >8192)throw new InvalidDataException("Invalid recognition activation.");
        byte[] bytes=new byte[length];await stream.ReadExactlyAsync(bytes,token);
        return Validate(new UTF8Encoding(false,true).GetString(bytes));
    }
}
