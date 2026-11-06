using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RobloxLaunchHelper;

internal interface IUpdateVersionProvider { Version? Current(); }
internal sealed class UpdateVersionProvider(string? installedPath = null, string? runningPath = null, Func<string, string?>? readVersion = null) : IUpdateVersionProvider
{
    public Version? Current()
    {
        try
        {
            string installed = installedPath ?? Program.InstalledExe;
            string path = installed;
            try { _ = File.GetAttributes(installed); }
            catch (FileNotFoundException) { path = runningPath ?? Environment.ProcessPath!; }
            catch (DirectoryNotFoundException) { path = runningPath ?? Environment.ProcessPath!; }
            return UpdateVersion.Parse(readVersion is null ? FileVersionInfo.GetVersionInfo(path).FileVersion : readVersion(path));
        }
        catch { return null; }
    }
}
internal static class UpdateVersion
{
    internal static Version? Parse(string? text)
    {
        if (text is null || !Regex.IsMatch(text, @"\Av?[0-9]+(?:\.[0-9]+){1,3}\z", RegexOptions.CultureInvariant)) return null;
        if (!Version.TryParse(text.TrimStart('v'), out var version)) return null;
        return new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }
    internal static string Label(Version version) => version.Revision == 0 ? $"{version.Major}.{version.Minor}.{version.Build}" : version.ToString();
}

internal interface IBootIdentity { Guid? Read(); }
internal sealed class WindowsBootIdentity : IBootIdentity
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BootEnvironment { internal Guid Identifier; internal int Firmware; internal ulong Flags; }
    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, out BootEnvironment data, int size, out int returned);
    public Guid? Read()
    {
        try
        {
            int size = Marshal.SizeOf<BootEnvironment>();
            return NtQuerySystemInformation(90, out var info, size, out int returned) >= 0 && returned >= size && info.Identifier != Guid.Empty ? info.Identifier : null;
        }
        catch { return null; }
    }
}
internal interface IUpdateAttemptStore { bool TryClaim(Guid boot); }
internal sealed class UpdateAttemptStore(string directory, string mutexName) : IUpdateAttemptStore
{
    public bool TryClaim(Guid boot)
    {
        bool held = false;
        try
        {
            if (boot == Guid.Empty) return false;
            using var mutex = new Mutex(false, mutexName);
            try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held) return false;
            try
            {
                UpdateFiles.EnsureDirectory(directory);
                string path = Path.Combine(directory, "boot.txt");
                if (File.Exists(path))
                {
                    UpdateFiles.RejectLinks(path);
                    if (new FileInfo(path).Length > 100 || !Guid.TryParse(File.ReadAllText(path), out var previous) || previous == boot) return false;
                }
                string temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                try { File.WriteAllText(temp, boot.ToString("D")); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                return true;
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch { return false; }
    }
}

internal sealed record UpdateAsset(string Name, Uri Url, long Size, string Sha256);
internal sealed record UpdateRelease(Version Version, string Title, string Notes, Uri Page, UpdateAsset? Asset, string? DownloadProblem);
internal interface IReleaseSource { Task<UpdateRelease?> LatestAsync(CancellationToken token); }
internal static class UpdateUrls
{
    internal const string Repository = "LancePerdue23/roblox-multi-instance";
    internal static readonly Uri Releases = new($"https://github.com/{Repository}/releases");
    internal static bool RepositoryUrl(Uri uri, string suffix) => uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(uri.UserInfo) &&
        uri.AbsolutePath.StartsWith("/" + Repository + suffix, StringComparison.Ordinal) && string.IsNullOrEmpty(uri.Fragment);
    internal static bool DownloadRedirect(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (RepositoryUrl(uri, "/releases/download/") || uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com");
}
internal sealed class GitHubReleaseSource(HttpClient client) : IReleaseSource
{
    public async Task<UpdateRelease?> LatestAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{UpdateUrls.Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("RobloxLaunchHelper/" + typeof(Program).Assembly.GetName().Version);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var content = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var bytes = new MemoryStream();
        await UpdateFiles.CopyBoundedAsync(content, bytes, 1024 * 1024, null, timeout.Token);
        return Parse(bytes.ToArray(), Distribution.Installed()?.Edition);
    }
    internal static UpdateRelease? Parse(byte[] json,string? edition=null)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var version = UpdateVersion.Parse(root.GetProperty("tag_name").GetString());
        if (version is null) return null;
        if (!Uri.TryCreate(root.GetProperty("html_url").GetString(), UriKind.Absolute, out var page) || !UpdateUrls.RepositoryUrl(page, "/releases/tag/"))
            throw new InvalidDataException("Invalid release page.");
        string title = root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : "Update available";
        string notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString()! : "";
        string assetName=edition is null?Program.Product+".exe":Distribution.SetupName(edition);
        var matches = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == assetName).ToArray();
        UpdateAsset? asset = null;
        string? problem = "This release does not contain a unique Windows executable. Use the release page or try again later.";
        if (matches.Length == 1)
        {
            var item = matches[0];
            string? digest = item.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            long size = item.GetProperty("size").GetInt64();
            if (digest is null || !Regex.IsMatch(digest, @"\Asha256:[a-fA-F0-9]{64}\z", RegexOptions.CultureInvariant))
                problem = "A published SHA-256 digest is required before this update can be downloaded in the app.";
            else if (size <= 0 || size > UpdateFiles.MaxDownload)
                problem = "This release has an unsupported download size.";
            else if (!Uri.TryCreate(item.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url) || !UpdateUrls.RepositoryUrl(url, "/releases/download/"))
                problem = "This release has an unsupported download address.";
            else { asset = new(assetName, url, size, digest[7..].ToUpperInvariant()); problem = null; }
        }
        return new(version, title, notes, page, asset, problem);
    }
}

internal static class UpdateFiles
{
    internal const long MaxDownload = 512L * 1024 * 1024;
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.Product, "updates");
    internal static string Staging => Path.Combine(Root, "staging");
    internal static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Update storage must not use symbolic links or junctions.");
    }
    internal static void EnsureDirectory(string path) { RejectLinks(path); Directory.CreateDirectory(path); RejectLinks(path); }
    internal static async Task<long> CopyBoundedAsync(Stream input, Stream output, long limit, IProgress<long>? progress, CancellationToken token)
    {
        byte[] buffer = new byte[64 * 1024]; long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("The download exceeded its expected size.");
            await output.WriteAsync(buffer.AsMemory(0, read), token); progress?.Report(total);
        }
        return total;
    }
    internal static void Prune(string? stagingDirectory = null, DateTime? now = null)
    {
        try
        {
            string staging = stagingDirectory ?? Staging;
            RejectLinks(staging);
            if (!Directory.Exists(staging)) return;
            foreach (string directory in Directory.EnumerateDirectories(staging))
            {
                try
                {
                    if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || Directory.GetLastWriteTimeUtc(directory) >= (now ?? DateTime.UtcNow).AddDays(-7)) continue;
                    RejectLinks(directory);
                    // Only delete known files, never recursively follow an unexpected directory.
                    var files = Directory.GetFiles(directory);
                    if (Directory.GetDirectories(directory).Length != 0 || files.Any(p => Path.GetFileName(p) is not ("RobloxLaunchHelper.exe" or "RobloxMultiInstance-Setup.exe" or "RobloxLaunchHelper-Setup.exe" or "payload.part" or "lease"))) continue;
                    foreach (string file in files) RejectLinks(file);
                    using (new FileStream(Path.Combine(directory, "lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        foreach (string file in files.Where(p => Path.GetFileName(p) != "lease")) File.Delete(file);
                    }
                    File.Delete(Path.Combine(directory, "lease")); Directory.Delete(directory);
                }
                catch { /* In-use payloads and inaccessible directories are retained. */ }
            }
        }
        catch { }
    }
}
internal sealed class DownloadedUpdate(string executable, FileStream lease) : IDisposable
{
    internal string Executable => executable;
    public void Dispose() => lease.Dispose();
}
internal interface IUpdateDownloader
{
    Task<DownloadedUpdate> DownloadAsync(UpdateRelease release, IProgress<long>? progress, CancellationToken token);
    Task VerifyAsync(string path, UpdateRelease release, CancellationToken token);
}
internal sealed class UpdateDownloader(HttpClient client, string staging, Func<string, Version?>? fileVersion = null,
    Func<string,string,CancellationToken,Task>? inspectSetup = null, Action<string>? verifyPublisher = null) : IUpdateDownloader
{
    private readonly bool requiresConfiguredPublisher = verifyPublisher is null;
    private readonly Action<string> verifyPublisher = verifyPublisher ?? UpdatePublisherTrust.Verify;
    private readonly Func<string, Version?> fileVersion = fileVersion ?? (path => UpdateVersion.Parse(FileVersionInfo.GetVersionInfo(path).FileVersion));
    private readonly Func<string,string,CancellationToken,Task> inspectSetup = inspectSetup ?? InspectSetupAsync;
    private static async Task InspectSetupAsync(string path,string edition,CancellationToken token)
    {
        var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        foreach(string arg in new[]{"--inspect","--expect-edition",edition,"--silent"})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Setup inspection could not start.");
        try { await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(30),token); }
        catch { if(!process.HasExited)process.Kill();throw; }
        if(process.ExitCode!=0)throw new InvalidDataException("Setup edition, protocol or embedded payload inspection failed.");
    }
    public async Task<DownloadedUpdate> DownloadAsync(UpdateRelease release, IProgress<long>? progress, CancellationToken token)
    {
        if(requiresConfiguredPublisher && !UpdatePublisherTrust.IsConfigured)throw new InvalidDataException(UpdatePublisherTrust.MissingIdentity);
        var asset = release.Asset ?? throw new InvalidOperationException("No verified download is available.");
        if (!UpdateUrls.RepositoryUrl(asset.Url, "/releases/download/") || asset.Size is <= 0 or > UpdateFiles.MaxDownload) throw new InvalidDataException();
        UpdateFiles.EnsureDirectory(staging);
        string directory = Path.Combine(staging, Guid.NewGuid().ToString("N"));
        UpdateFiles.EnsureDirectory(directory);
        var lease = new FileStream(Path.Combine(directory, "lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        if(asset.Name is not ("RobloxLaunchHelper.exe" or "RobloxMultiInstance-Setup.exe" or "RobloxLaunchHelper-Setup.exe"))throw new InvalidDataException("Unsupported setup asset.");
        string partial = Path.Combine(directory, "payload.part"), executable = Path.Combine(directory, asset.Name);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var response = await GetAsync(asset.Url, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != asset.Size) throw new InvalidDataException("Unexpected download size.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                if (await UpdateFiles.CopyBoundedAsync(input, output, asset.Size, progress, timeout.Token) != asset.Size) throw new InvalidDataException("Incomplete download.");
            await VerifyFileAsync(partial, release, timeout.Token,inspect:false);
            File.Move(partial, executable);
            await VerifyAsync(executable,release,timeout.Token);
            return new(executable, lease);
        }
        catch
        {
            lease.Dispose();
            foreach (string file in new[] { partial, executable, Path.Combine(directory, "lease") }) { try { File.Delete(file); } catch { } }
            try { Directory.Delete(directory); } catch { }
            throw;
        }
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (int count = 0; count < 6; count++)
        {
            if (!UpdateUrls.DownloadRedirect(uri)) throw new InvalidDataException("Untrusted download redirect.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("RobloxLaunchHelper-Updater");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location is null) throw new InvalidDataException("Missing download redirect.");
            uri = new Uri(uri, location);
        }
        throw new InvalidDataException("Too many download redirects.");
    }
    public Task VerifyAsync(string path, UpdateRelease release, CancellationToken token)=>VerifyFileAsync(path,release,token,inspect:true);
    private async Task VerifyFileAsync(string path,UpdateRelease release,CancellationToken token,bool inspect)
    {
        var asset = release.Asset ?? throw new InvalidDataException();
        UpdateFiles.RejectLinks(path);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (file.Length != asset.Size || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Download verification failed. Download the update again.");
        token.ThrowIfCancellationRequested();
        verifyPublisher(path);
        if (fileVersion(path) != release.Version) throw new InvalidDataException("The downloaded executable version does not match this release.");
        string? edition=asset.Name switch {"RobloxMultiInstance-Setup.exe"=>"Desktop","RobloxLaunchHelper-Setup.exe"=>"Helper",_=>null};
        if(inspect&&edition is not null)await inspectSetup(path,edition,token);
    }
}
