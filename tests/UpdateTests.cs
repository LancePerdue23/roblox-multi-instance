using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using RobloxLaunchHelper;

internal static class UpdateTests
{
    private static readonly Version Current = new(1, 1, 0, 0), New = new(1, 2, 0, 0);
    private static readonly byte[] Payload = "Synthetic update payload, never executed"u8.ToArray();
    private static readonly string Digest = Convert.ToHexString(SHA256.HashData(Payload));
    private static void Check(bool value) { if (!value) throw new Exception("Update assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Store(Action<string> test)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { test(path); } finally { Directory.Delete(path, true); }
    }
    private static byte[] Json(string tag = "v1.2.0", bool draft = false, bool prerelease = false, string? digest = null,
        int assets = 1, long? size = null, string? url = null, string? page = null, string? body = "Changes\n- An improvement") => JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = tag, draft, prerelease, name = "A new release", body,
            html_url = page ?? "https://github.com/LancePerdue23/roblox-multi-instance/releases/tag/" + tag,
            assets = Enumerable.Range(0, assets).Select(_ => new { name = "RobloxLaunchHelper.exe", size = size ?? Payload.Length,
                digest = digest ?? "sha256:" + Digest, browser_download_url = url ?? "https://github.com/LancePerdue23/roblox-multi-instance/releases/download/v1.2.0/RobloxLaunchHelper.exe" })
        });
    private static UpdateRelease Release() => GitHubReleaseSource.Parse(Json())!;
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return send(request, token); }
    }
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new Handler((request, _) => Task.FromResult(response(request))));
    private static void Async(Func<Task> task) => Task.Run(task).GetAwaiter().GetResult();
    internal static void Run(Action<string, Action> run)
    {
        run("Private update metadata uses authenticated bounded release lists and explicit prerelease opt-in",()=>Async(async()=>
        {
            var list=JsonSerializer.SerializeToUtf8Bytes(new[]{JsonSerializer.Deserialize<JsonElement>(Json(prerelease:true))});
            using var client=Client(request=>{
                Check(request.RequestUri!.AbsoluteUri=="https://api.github.com/repos/"+UpdateUrls.Repository+"/releases?per_page=100");
                Check(request.Headers.Authorization?.Parameter=="fixture-token");
                return new(HttpStatusCode.OK){Content=new ByteArrayContent(list)};
            });
            Check(await new GitHubReleaseSource(client,()=>"fixture-token",()=>false).LatestAsync(default) is null);
            Check((await new GitHubReleaseSource(client,()=>"fixture-token",()=>true).LatestAsync(default))?.Version==New);
            var helper=JsonSerializer.SerializeToUtf8Bytes(new[]{JsonSerializer.Deserialize<JsonElement>(Json(tag:"helper-v1.2.0",prerelease:true))});
            Check(GitHubReleaseSource.ParseList(helper,"Desktop",true) is null);
            Check(GitHubReleaseSource.ParseList(helper,"Helper",true)?.Version==New);
            using var denied=Client(_=>new(HttpStatusCode.Unauthorized));
            try { await new GitHubReleaseSource(denied,()=>"fixture-token",()=>true).LatestAsync(default);throw new Exception("Expected access failure"); } catch(UserError error) { Check(!error.Message.Contains("fixture-token")); }
        }));
        run("Private asset download authenticates only the API request and strips credentials on redirects",()=>Async(async()=>
        {
            string directory=Path.Combine(AppContext.BaseDirectory,"private-update-"+Guid.NewGuid().ToString("N"));
            var api=new Uri("https://api.github.com/repos/"+UpdateUrls.Repository+"/releases/assets/123");
            int calls=0;
            using var client=Client(request=>{
                calls++;
                if(calls==1) { Check(request.RequestUri==api&&request.Headers.Authorization?.Parameter=="fixture-token");Check(request.Headers.Accept.Any(x=>x.MediaType=="application/octet-stream"));return new(HttpStatusCode.Redirect){Headers={Location=new("https://release-assets.githubusercontent.com/fixture")}}; }
                Check(request.Headers.Authorization is null);return new(HttpStatusCode.OK){Content=new ByteArrayContent(Payload)};
            });
            try {
                var release=Release();release=release with {Asset=release.Asset! with {ApiUrl=api}};
                using var downloaded=await new UpdateDownloader(client,directory,_=>New,(_,_,_)=>Task.CompletedTask,_=>{},()=>"fixture-token").DownloadAsync(release,null,default);
                Check(calls==2&&File.Exists(downloaded.Executable));
                Check(!UpdateUrls.AssetApi(new Uri("https://api.github.com/repos/evil/repo/releases/assets/123")));
                Check(!UpdateUrls.AssetApi(new Uri(api+"?token=secret")));
            } finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        }));
        run("Updates compare numeric release versions and reject unsupported tags without downgrading", () =>
        {
            Check(UpdateVersion.Parse("v1.1") == Current && UpdateVersion.Parse("1.1.0.0") == Current);
            Check(UpdateVersion.Parse("v1.10.0") > UpdateVersion.Parse("1.9.9"));
            Check(UpdateVersion.Parse("1.1.0.1") > Current && UpdateVersion.Parse("1.0.99") < Current);
            foreach (string tag in new[] { "1", "1.2.3-beta", "1.2.3+metadata", "v1.2.3.4.5", "1.2.-3", " 1.2.3", "V1.2.3", "999999999999.2" }) Check(UpdateVersion.Parse(tag) is null);
            Check(UpdateVersion.Parse(null) is null);
        });
        run("Update version source prefers the installed payload and only falls back when installation is absent", () => Store(directory =>
        {
            string installed = Path.Combine(directory, "installed.exe"), running = Path.Combine(directory, "running.exe");
            var provider = new UpdateVersionProvider(installed, running, path => path == installed ? "1.0.0" : "1.1.0");
            Check(provider.Current() == Current); File.WriteAllText(installed, "fixture");
            Check(provider.Current() == new Version(1, 0, 0, 0));
            Check(new UpdateVersionProvider(installed, running, _ => throw new IOException()).Current() is null);
        }));
        run("Twenty concurrent startup claims yield one attempt per boot; boot changes reset eligibility", () => Store(directory =>
        {
            string name = @"Local\RLH-UpdateTest-" + Guid.NewGuid().ToString("N"); var boot = Guid.NewGuid();
            var store = new UpdateAttemptStore(directory, name); int claims = 0;
            Parallel.For(0, 20, _ => { if (store.TryClaim(boot)) Interlocked.Increment(ref claims); });
            Check(claims == 1 && !store.TryClaim(boot));
            // A failed network request does not roll back the claim.
            Check(!new UpdateAttemptStore(directory, name).TryClaim(boot));
            Check(store.TryClaim(Guid.NewGuid())); Check(!store.TryClaim(Guid.Empty));
            File.WriteAllText(Path.Combine(directory, "boot.txt"), "corrupt"); Check(!store.TryClaim(Guid.NewGuid()));
        }));
        run("Update coordination fails safely on inaccessible storage and reads stable Windows boot identity", () => Store(directory =>
        {
            string file = Path.Combine(directory, "file"); File.WriteAllText(file, "fixture");
            Check(!new UpdateAttemptStore(file, @"Local\RLH-" + Guid.NewGuid().ToString("N")).TryClaim(Guid.NewGuid()));
            var boot = new WindowsBootIdentity(); Check(boot.Read() is { } id && id != Guid.Empty && boot.Read() == id);
        }));
        run("Release metadata requires one executable with a digest, supported size and repository URLs", () =>
        {
            Check(Release().Asset!.Sha256 == Digest && Release().Version == New && Release().Notes.Contains("improvement"));
            Check(GitHubReleaseSource.Parse(Json(draft: true)) is null && GitHubReleaseSource.Parse(Json(prerelease: true)) is null);
            Check(GitHubReleaseSource.Parse(Json(tag: "banana")) is null);
            foreach (var json in new[] { Json(assets: 0), Json(assets: 2), Json(digest: "sha256:invalid"), Json(size: 0), Json(size: UpdateFiles.MaxDownload + 1),
                Json(url: "https://evil.example/update.exe"), Json(url: "http://github.com/LancePerdue23/roblox-multi-instance/releases/download/v1/test.exe") })
            { var release = GitHubReleaseSource.Parse(json)!; Check(release.Asset is null && release.DownloadProblem is not null); }
            Check(GitHubReleaseSource.Parse(Json(body: null))!.Notes == "");
            Throws<InvalidDataException>(() => GitHubReleaseSource.Parse(Json(page: "https://github.com/other/project/releases/tag/v1")));
            Throws<JsonException>(() => GitHubReleaseSource.Parse("invalid"u8.ToArray()));
        });
        run("Release requests identify the app, bound content and handle missing, rate-limited and offline responses", () => Async(async () =>
        {
            using (var client = Client(request =>
            {
                Check(request.RequestUri!.Host == "api.github.com" && request.Headers.UserAgent.Count > 0 && request.Headers.Accept.Count > 0);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Json()) };
            })) Check((await new GitHubReleaseSource(client).LatestAsync(default))!.Version == New);
            using (var client = Client(_ => new(HttpStatusCode.NotFound))) { try { await new GitHubReleaseSource(client).LatestAsync(default);throw new Exception("Expected access guidance"); } catch(UserError) {} }
            foreach (var code in new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
            {
                using var client = Client(_ => new(code));
                Throws<HttpRequestException>(() => new GitHubReleaseSource(client).LatestAsync(default).GetAwaiter().GetResult());
            }
            using (var client = Client(_ => throw new HttpRequestException("offline")))
                Throws<HttpRequestException>(() => new GitHubReleaseSource(client).LatestAsync(default).GetAwaiter().GetResult());
            using (var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1024 * 1024 + 1]) }))
                Throws<InvalidDataException>(() => new GitHubReleaseSource(client).LatestAsync(default).GetAwaiter().GetResult());
            using (var client = new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); })))
            using (var cancellation = new CancellationTokenSource(30))
                Throws<OperationCanceledException>(() => new GitHubReleaseSource(client).LatestAsync(cancellation.Token).GetAwaiter().GetResult());
        }));
        run("Direct downloads follow GitHub redirects, verify size digest and version, and retain a leased executable", () => Store(directory => Async(async () =>
        {
            using var client = Client(request => request.RequestUri!.Host == "github.com"
                ? new(HttpStatusCode.Redirect) { Headers = { Location = new("https://release-assets.githubusercontent.com/fixture") } }
                : new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
            var downloader = new UpdateDownloader(client, directory, _ => New, verifyPublisher: _ => {});
            using var download = await downloader.DownloadAsync(Release(), null, default);
            Check(File.ReadAllBytes(download.Executable).SequenceEqual(Payload) && Path.GetFileName(download.Executable) == "RobloxLaunchHelper.exe");
            Check(Directory.GetFiles(Path.GetDirectoryName(download.Executable)!).Length == 2);
            Throws<IOException>(() => { using var lease = new FileStream(Path.Combine(Path.GetDirectoryName(download.Executable)!, "lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
            await downloader.VerifyAsync(download.Executable, Release(), default);
        })));
        run("Downloads reject mismatched hash, version and sizes and remove partial staging files", () => Store(directory => Async(async () =>
        {
            foreach (var payload in new[] { Payload, Payload[..^1], Payload.Concat(new byte[] { 1 }).ToArray(), new byte[Payload.Length] })
            {
                using var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
                var downloader = new UpdateDownloader(client, directory, _ => Current, verifyPublisher: _ => {});
                Throws<InvalidDataException>(() => downloader.DownloadAsync(Release(), null, default).GetAwaiter().GetResult());
                Check(!Directory.EnumerateFileSystemEntries(directory).Any());
            }
            using var validClient = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
            using var download = await new UpdateDownloader(validClient, directory, _ => New, verifyPublisher: _ => {}).DownloadAsync(Release(), null, default);
            await File.WriteAllBytesAsync(download.Executable, new byte[Payload.Length]);
            Throws<InvalidDataException>(() => new UpdateDownloader(validClient, directory, _ => New, verifyPublisher: _ => {}).VerifyAsync(download.Executable, Release(), default).GetAwaiter().GetResult());
        })));
        run("Configured publisher trust rejects untrusted signers; unsigned builds support repository updates", () => Store(directory =>
        {
            string pin = new('A', 64); bool inspected = false;
            Throws<InvalidDataException>(() => UpdatePublisherTrust.Verify("fixture", "", _ => { inspected = true; return pin; }));
            Check(!inspected);
            if(!UpdatePublisherTrust.IsConfigured){
                int requests=0;
                using var client=Client(_=>{requests++;return new(HttpStatusCode.OK){Content=new ByteArrayContent(Payload)};});
                using var unsignedDownload=new UpdateDownloader(client,directory,_=>New,(_,_,_)=>Task.CompletedTask).DownloadAsync(Release(),null,default).GetAwaiter().GetResult();
                Check(requests==1&&File.Exists(unsignedDownload.Executable));
            }
            Throws<InvalidDataException>(() => UpdatePublisherTrust.Verify("fixture", pin, _ => null));
            Throws<InvalidDataException>(() => UpdatePublisherTrust.Verify("fixture", pin, _ => new string('B', 64)));
            UpdatePublisherTrust.Verify("fixture", pin, _ => pin.ToLowerInvariant());
            string unsigned = Path.Combine(directory, "unsigned.exe"); File.WriteAllBytes(unsigned, Payload);
            Throws<InvalidDataException>(() => UpdatePublisherTrust.Verify(unsigned, pin, UpdatePublisherTrust.TrustedSigner));
        }));
        run("Publisher rejection precedes setup execution and cleans staged downloads", () => Store(directory => Async(async () =>
        {
            byte[] metadata = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(Json()).Replace("RobloxLaunchHelper.exe", "RobloxMultiInstance-Setup.exe"));
            var release = GitHubReleaseSource.Parse(metadata,"Desktop")!;
            using var client = Client(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
            int inspections = 0, signatures = 0;
            var rejected = new UpdateDownloader(client, directory, _ => New,
                (_,_,_) => { inspections++; return Task.CompletedTask; }, _ => { signatures++; throw new InvalidDataException("Untrusted fixture signer"); });
            try { using var result = await rejected.DownloadAsync(release,null,default); throw new Exception("Publisher rejection ignored"); }
            catch(InvalidDataException) { Check(signatures == 1 && inspections == 0 && !Directory.EnumerateFileSystemEntries(directory).Any()); }
            var accepted = new UpdateDownloader(client,directory,_=>New,(_,_,_)=>{inspections++;return Task.CompletedTask;},_=>signatures++);
            using var download = await accepted.DownloadAsync(release,null,default);
            int before=signatures;
            await accepted.VerifyAsync(download.Executable,release,default);
            Check(signatures==before+1 && inspections==2);
        })));
        run("Edition setup downloads inspect embedded payloads after digest and version checks and before handoff", () => Store(directory => Async(async () =>
        {
            foreach(string edition in new[]{"Desktop","Helper"})
            {
                string assetName=Distribution.SetupName(edition);
                byte[] metadata=System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(Json()).Replace("RobloxLaunchHelper.exe",assetName));
                var release=GitHubReleaseSource.Parse(metadata,edition)!;
                Check(release.Asset?.Name==assetName);
                using var client=Client(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Payload)});
                int inspections=0;
                Task Inspect(string path,string expected,CancellationToken token){Check(expected==edition&&Path.GetFileName(path)==assetName);inspections++;return Task.CompletedTask;}
                var downloader=new UpdateDownloader(client,directory,_=>New,Inspect,verifyPublisher:_=>{});
                using var download=await downloader.DownloadAsync(release,null,default);
                Check(inspections==1);await downloader.VerifyAsync(download.Executable,release,default);Check(inspections==2);
                var rejected=new UpdateDownloader(client,directory,_=>New,(_,_,_)=>throw new InvalidDataException("Rejected fixture payload."),verifyPublisher:_=>{});
                Throws<InvalidDataException>(()=>rejected.VerifyAsync(download.Executable,release,default).GetAwaiter().GetResult());
                await File.WriteAllBytesAsync(download.Executable,new byte[Payload.Length]);
                Throws<InvalidDataException>(()=>downloader.VerifyAsync(download.Executable,release,default).GetAwaiter().GetResult());Check(inspections==2);
            }
        })));
        run("Download cancellation removes incomplete files; untrusted and excessive redirects never execute", () => Store(directory =>
        {
            using (var client = new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); })))
            using (var cancellation = new CancellationTokenSource(30))
                Throws<OperationCanceledException>(() => new UpdateDownloader(client, directory, _ => New, verifyPublisher: _ => {}).DownloadAsync(Release(), null, cancellation.Token).GetAwaiter().GetResult());
            Check(!Directory.EnumerateFileSystemEntries(directory).Any());
            foreach (string redirect in new[] { "https://evil.example/payload", "http://release-assets.githubusercontent.com/payload", "https://github.com/other/project/releases/download/v1/a.exe", "https://github.com/LancePerdue23/roblox-multi-instance/releases/download/v1/loop.exe" })
            {
                using var client = Client(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new(redirect) } });
                Throws<InvalidDataException>(() => new UpdateDownloader(client, directory, _ => New, verifyPublisher: _ => {}).DownloadAsync(Release(), null, default).GetAwaiter().GetResult());
                Check(!Directory.EnumerateFileSystemEntries(directory).Any());
            }
        }));
        run("Setup handoff only accepts isolated staging payloads and distinguishes cancellation failure and completion", () =>
        {
            string root = Path.Combine(AppContext.BaseDirectory, "staging"), folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Check(UpdateSetup.IsStagedExecutable(Path.Combine(folder, "RobloxLaunchHelper.exe"), root));
            Check(!UpdateSetup.IsStagedExecutable(Path.Combine(root, "RobloxLaunchHelper.exe"), root));
            Check(!UpdateSetup.IsStagedExecutable(Path.Combine(folder, "other.exe"), root));
            Check(!UpdateSetup.IsStagedExecutable(Path.Combine(root, "..", Guid.NewGuid().ToString("N"), "RobloxLaunchHelper.exe"), root));
            Check(UpdateSetup.ResultMessage(1223, Current, New)!.Contains("canceled"));
            Check(UpdateSetup.ResultMessage(1, New, New)!.Contains("did not complete"));
            Check(UpdateSetup.ResultMessage(0, Current, New)!.Contains("could not be confirmed"));
            Check(UpdateSetup.ResultMessage(0, null, New) is not null && UpdateSetup.ResultMessage(0, New, New) is null);
            Check(UpdateSetup.Run(["--update-handoff"]) == 2);
            Check(!UpdateInstallReadiness.IsReady(Environment.ProcessPath!));
        });
        run("Setup waits for parent exit, revalidates before closing managers and retries locks without running an installer", () => Async(async () =>
        {
            using var operations = new FakeSetup();
            operations.Parent = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var install = UpdateSetupFlow.RunAsync(operations, New, _ => { }, default, (_, _) => Task.CompletedTask);
            Check(operations.Calls.SequenceEqual(new[] { "wait" }));
            operations.Parent.SetResult(); await install;
            Check(operations.Calls.SequenceEqual(new[] { "wait", "verify", "close", "ready", "install", "version" }));
            using var locked = new FakeSetup { Ready = false };
            Throws<UserError>(() => UpdateSetupFlow.RunAsync(locked, New, _ => { }, default, (_, _) => Task.CompletedTask).GetAwaiter().GetResult());
            Check(locked.Calls.Count(c => c == "ready") == 20 && !locked.Calls.Contains("install"));
            locked.Ready = true;
            await UpdateSetupFlow.RunAsync(locked, New, _ => { }, default, (_, _) => Task.CompletedTask);
            Check(locked.Calls.Count(c => c == "install") == 1);
            foreach (int code in new[] { 1223, 1 })
            {
                using var failure = new FakeSetup { ExitCode = code };
                Throws<UserError>(() => UpdateSetupFlow.RunAsync(failure, New, _ => { }, default).GetAwaiter().GetResult());
            }
            using var invalid = new FakeSetup { VerificationFailure = true };
            Throws<InvalidDataException>(() => UpdateSetupFlow.RunAsync(invalid, New, _ => { }, default).GetAwaiter().GetResult());
            Check(!invalid.Calls.Contains("close") && !invalid.Calls.Contains("install"));
        }));
        run("Staging cleanup removes old partials and completed payloads while preserving active, recent and unknown content", () => Store(directory =>
        {
            string Stage(bool old, string file)
            {
                string stage = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
                File.WriteAllText(Path.Combine(stage, file), "fixture");
                File.WriteAllText(Path.Combine(stage, "lease"), "");
                Directory.SetLastWriteTimeUtc(stage, DateTime.UtcNow.AddDays(old ? -8 : -1)); return stage;
            }
            string partial = Stage(true, "payload.part"), complete = Stage(true, "RobloxLaunchHelper.exe"), recent = Stage(false, "RobloxLaunchHelper.exe"),
                unknown = Stage(true, "unrelated.txt"), active = Stage(true, "RobloxLaunchHelper.exe");
            using (var lease = new FileStream(Path.Combine(active, "lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                UpdateFiles.Prune(directory);
                Check(!Directory.Exists(partial) && !Directory.Exists(complete));
                Check(Directory.Exists(recent) && Directory.Exists(unknown) && Directory.Exists(active));
            }
            UpdateFiles.Prune(directory); Check(!Directory.Exists(active));
        }));

    }
    private sealed class FakeSetup : IUpdateSetupOperations
    {
        internal readonly List<string> Calls = [];
        internal TaskCompletionSource? Parent;
        internal bool Ready = true, VerificationFailure;
        internal int ExitCode;
        public Task WaitForParentAsync(CancellationToken token) { Calls.Add("wait"); return Parent?.Task ?? Task.CompletedTask; }
        public Task VerifyAsync(CancellationToken token) { Calls.Add("verify"); if (VerificationFailure) throw new InvalidDataException(); return Task.CompletedTask; }
        public void CloseManagers() => Calls.Add("close");
        public Task<bool> ReadyAsync(CancellationToken token) { Calls.Add("ready"); return Task.FromResult(Ready); }
        public Task<int> InstallAsync(CancellationToken token) { Calls.Add("install"); return Task.FromResult(ExitCode); }
        public Version? InstalledVersion() { Calls.Add("version"); return New; }
        public void Dispose() { }
    }
}
