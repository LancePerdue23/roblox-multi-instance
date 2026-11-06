using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace RobloxLaunchHelper;

// Publisher pins are compiled into the installed core, never read from a release or user settings.
internal static class UpdatePublisherTrust
{
    internal static string ConfiguredPin => typeof(UpdatePublisherTrust).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdatePublisherSha256")?.Value ?? "";
    internal static bool IsConfigured => Regex.IsMatch(ConfiguredPin, "\\A[a-fA-F0-9]{64}\\z", RegexOptions.CultureInvariant);
    internal const string MissingIdentity = "Publisher trust is not configured in this build. In-app updates are disabled until a publisher-verified build is available.";
    internal static void Verify(string path) => Verify(path, ConfiguredPin, TrustedSigner);
    internal static void Verify(string path, string pin, Func<string, string?> trustedSigner)
    {
        if (!Regex.IsMatch(pin, "\\A[a-fA-F0-9]{64}\\z", RegexOptions.CultureInvariant))
            throw new InvalidDataException(MissingIdentity);
        string? actual = trustedSigner(path);
        if (actual is null || !string.Equals(actual, pin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update is unsigned, untrusted, or signed by a different publisher. It will not be run.");
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileInfo { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public nint Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)]
    struct TrustData
    {
        public uint Size; public nint Policy, Sip; public uint Ui, Revocation, Choice; public nint File;
        public uint StateAction; public nint State, Url; public uint Flags, Context;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern int WinVerifyTrust(nint hwnd, ref Guid action, ref TrustData data);
    internal static string? TrustedSigner(string path)
    {
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path, Handle = locked.SafeFileHandle.DangerousGetHandle() };
        nint info = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        Marshal.StructureToPtr(file, info, false);
        // No UI; verify the chain and revocation using cached information. Unknown trust fails closed.
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Revocation = 1, Choice = 1, File = info, StateAction = 1, Flags = 0x1080 };
        Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            if (WinVerifyTrust(new nint(-1), ref action, ref data) != 0) return null;
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return certificate.GetCertHashString(HashAlgorithmName.SHA256);
        }
        finally
        {
            data.StateAction = 2; WinVerifyTrust(new nint(-1), ref action, ref data);
            Marshal.DestroyStructure<FileInfo>(info); Marshal.FreeHGlobal(info);
        }
    }
}
