using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using RobloxLaunchHelper;

internal static class TelemetryTests
{
    static readonly Uri Endpoint = new("https://fixture.invalid/v1/error-batches");
    static void Check(bool value) { if (!value) throw new Exception("Telemetry assertion failed."); }
    static void Store(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "RMI-telemetry-tests-" + Guid.NewGuid().ToString("N"));
        try { action(directory); } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    internal static void Run(Action<string,Action> run)
    {
        run("Telemetry is off without creating storage or attempting requests", () => Store(dir =>
        {
            int requests = 0;
            using var service = new TelemetryService(dir, new Handler((_,_) => { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }), Endpoint);
            Check(!service.Status().Enabled && !Directory.Exists(dir));
            service.RecordOperation("OPERATION_FAILED"); service.SendAsync().GetAwaiter().GetResult();
            Check(requests == 0 && !Directory.Exists(dir));
        }));
        run("Telemetry consent aggregates only allowlisted codes and disabling purges pending errors", () => Store(dir =>
        {
            string? payload = null;
            using var service = new TelemetryService(dir, new Handler(async (request,_) => { payload = await request.Content!.ReadAsStringAsync(); return new(HttpStatusCode.OK); }), Endpoint);
            Check(service.SetEnabled(true).Enabled);
            service.RecordOperation("synthetic-cookie-private-user-path-url");
            service.Record(DiagnosticCode.LaunchFailed, LaunchSource.Roblox, -1);
            service.Record(DiagnosticCode.LaunchFailed, LaunchSource.Roblox, -1);
            Check(service.Status().QueuedErrors == 2);
            service.SendAsync().GetAwaiter().GetResult();
            Check(payload is not null && !payload.Contains("synthetic-cookie") && !payload.Contains("attemptId") && !payload.Contains("username"));
            using var json = JsonDocument.Parse(payload!); var error = json.RootElement.GetProperty("errors")[0];
            Check(error.GetProperty("count").GetInt32() == 2 && error.GetProperty("code").GetString() == "LaunchFailed");
            Check(service.Status().QueuedErrors == 0);
            service.RecordOperation("OPERATION_FAILED"); Check(service.SetEnabled(false).QueuedErrors == 0);
            service.RecordOperation("OPERATION_FAILED"); Check(service.Status().QueuedErrors == 0);
            Check(service.SetEnabled(true).QueuedErrors == 0);
        }));
        run("Telemetry retries preserve batch identity and counters recorded meanwhile", () => Store(dir =>
        {
            var payloads = new List<string>();
            using var service = new TelemetryService(dir,new Handler(async (request,_) => { payloads.Add(await request.Content!.ReadAsStringAsync()); return new(payloads.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK); }),Endpoint);
            service.SetEnabled(true);service.RecordOperation("OPERATION_FAILED");
            service.SendAsync().GetAwaiter().GetResult();service.RecordOperation("OPERATION_FAILED");
            service.SendAsync().GetAwaiter().GetResult();Check(service.Status().QueuedErrors == 1);
            Check(payloads.Count == 2 && payloads[0] == payloads[1]);
            service.SendAsync().GetAwaiter().GetResult();Check(service.Status().QueuedErrors == 0 && payloads[2] != payloads[1]);
        }));
        run("Telemetry fails closed on malformed consent and expires seven-day errors", () => Store(dir =>
        {
            var now = DateTimeOffset.UtcNow;
            using var service = new TelemetryService(dir,endpoint:Endpoint,now:()=>now);
            service.SetEnabled(true);service.RecordOperation("OPERATION_FAILED");
            now = now.AddDays(7);Check(service.Status().QueuedErrors == 0);
            File.WriteAllText(Path.Combine(dir,"consent.json"),"{\"version\":1,\"enabled\":true,\"epoch\":\"invalid\"}");
            service.RecordOperation("OPERATION_FAILED");Check(!service.Status().Enabled && !service.Status().Available);
            Check(!service.SetEnabled(false).Enabled && service.Status().Available);
        }));
        run("Persisted opt-out cancels a different uploader instance", () => Store(dir =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var uploader = new TelemetryService(dir,new Handler(async (_,token) => { started.SetResult(); await Task.Delay(Timeout.Infinite,token); return new(HttpStatusCode.OK); }),Endpoint);
            using var settings = new TelemetryService(dir,endpoint:Endpoint);
            uploader.SetEnabled(true);uploader.RecordOperation("OPERATION_FAILED");
            var send = uploader.SendAsync();Check(started.Task.Wait(2000));
            Check(!settings.SetEnabled(false).Enabled);
            Check(send.Wait(2000));Check(!uploader.Status().Enabled && uploader.Status().QueuedErrors == 0);
        }));
        run("Unconfigured telemetry stores opted-in counters without network traffic", () => Store(dir =>
        {
            using var service = new TelemetryService(dir);
            Check(!service.Status().Configured);service.SetEnabled(true);service.RecordOperation("OPERATION_FAILED");
            var result = service.SendAsync().GetAwaiter().GetResult();Check(result.QueuedErrors == 1 && !result.Configured);
        }));
        run("Failed telemetry enable commits no positive consent when queue initialization fails", () => Store(dir =>
        {
            using var service = new TelemetryService(dir,endpoint:Endpoint);
            service.SetEnabled(true);File.Delete(Path.Combine(dir,"queue.json"));Directory.CreateDirectory(Path.Combine(dir,"queue.json"));
            var failed = service.SetEnabled(true);Check(!failed.Available && !failed.Enabled);
            service.RecordOperation("OPERATION_FAILED");Check(!service.Status().Enabled);
        }));
        run("Telemetry retries only staged atomic commits and bounds persistent access denial", () => Store(dir =>
        {
            int remaining=0,commits=0;var staged=new HashSet<string>();
            using var service=new TelemetryService(dir,endpoint:Endpoint)
            {
                AtomicCommit=(source,target)=>
                {
                    if(remaining>0){commits++;staged.Add(source);remaining--;throw new UnauthorizedAccessException("Fixture commit denied.");}
                    commits++;staged.Add(source);File.Move(source,target,true);
                }
            };
            service.SetEnabled(true);commits=0;staged.Clear();remaining=2;
            service.RecordOperation("OPERATION_FAILED");
            Check(commits==3&&staged.Count==1&&service.Status().QueuedErrors==1);
            string committed=File.ReadAllText(Path.Combine(dir,"queue.json"));
            commits=0;staged.Clear();remaining=100;
            service.RecordOperation("OPERATION_FAILED");
            Check(commits==4&&staged.Count==1&&service.Status().QueuedErrors==1);
            Check(File.ReadAllText(Path.Combine(dir,"queue.json"))==committed&&!Directory.EnumerateFiles(dir,"*.tmp").Any());
            remaining=0;service.RecordOperation("OPERATION_FAILED");Check(service.Status().QueuedErrors==2);
        }));
        run("Telemetry bounds daily collection and batches across queued days", () => Store(dir =>
        {
            var now = DateTimeOffset.UtcNow;var payloads = new List<string>();
            using var service = new TelemetryService(dir,new Handler(async(request,_) => {payloads.Add(await request.Content!.ReadAsStringAsync());return new(HttpStatusCode.OK);}),Endpoint,()=>now);
            service.SetEnabled(true);
            for(int i=0;i<1001;i++)service.RecordOperation("OPERATION_FAILED");
            Check(service.Status().QueuedErrors==1000);
            now=now.AddDays(1);service.RecordOperation("OPERATION_FAILED");Check(service.Status().QueuedErrors==1001);
            service.SendAsync().GetAwaiter().GetResult();Check(service.Status().QueuedErrors==1);
            using var payload=JsonDocument.Parse(payloads.Single());Check(payload.RootElement.GetProperty("errors").EnumerateArray().Sum(e=>e.GetProperty("count").GetInt32())==1000);
            Check(new FileInfo(Path.Combine(dir,"queue.json")).Length<=TelemetryService.MaxQueueBytes);
        }));
        run("Oversized telemetry queue fails closed without dispatch", () => Store(dir =>
        {
            int requests=0;using var service=new TelemetryService(dir,new Handler((_,_)=>{requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));}),Endpoint);
            service.SetEnabled(true);File.WriteAllText(Path.Combine(dir,"queue.json"),new string('x',TelemetryService.MaxQueueBytes+1));
            Check(!service.Status().Enabled&&!service.Status().Available);service.SendAsync().GetAwaiter().GetResult();Check(requests==0);
        }));
        run("Telemetry CLI supports immediate opt-out and rejects extra operands", () =>
        {
            foreach (string command in new[] { "status", "on", "off", "clear", "send" }) Terminal.ValidateArguments(["telemetry",command]);
            bool threw = false;try { Terminal.ValidateArguments(["telemetry","off","extra"]); } catch(ArgumentException) { threw = true; }Check(threw);
        });
        run("Launch telemetry finalizes once and ignores user cancellation", () => Store(dir =>
        {
            using var service=new TelemetryService(Path.Combine(dir,"telemetry"),endpoint:Endpoint);service.SetEnabled(true);
            var history=new DiagnosticHistory(Path.Combine(dir,"history"));
            using(var publisher=new LaunchNotificationPublisher((_,_)=>Task.FromResult(true)))
            using(var attempt=new LaunchAttempt(null,history,publisher,service))
            {
                attempt.Complete(LaunchOutcome.Failed,DiagnosticCode.LaunchFailed);attempt.FinishAsync().GetAwaiter().GetResult();attempt.FinishAsync().GetAwaiter().GetResult();
            }
            Check(service.Status().QueuedErrors==1);
            using(var publisher=new LaunchNotificationPublisher((_,_)=>Task.FromResult(true)))
            using(var attempt=new LaunchAttempt(null,history,publisher,service))
            {
                attempt.SuppressTelemetry();attempt.Complete(LaunchOutcome.Failed,DiagnosticCode.LaunchFailed);attempt.FinishAsync().GetAwaiter().GetResult();
            }
            Check(service.Status().QueuedErrors==1);
        }));
    }
}
