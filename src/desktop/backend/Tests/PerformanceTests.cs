using System.Text.Json;
using RobloxLaunchHelper;
using RobloxMultiInstance;

internal static class PerformanceTests
{
    internal static async Task<int> Run(string root)
    {
        int passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
        void Reject(Action action, string name)
        {
            try { action(); throw new Exception("Unexpected success: " + name); }
            catch (Exception e) when (e is ArgumentException or InvalidDataException) { Check(true, name); }
        }
        using (var handle = Native.OpenProcess(InstanceNative.QueryLimitedInformation, false, Environment.ProcessId))
        {
            var native = PerformanceNative.Read(handle);
            Check(native.PrivateCommit is > 0 && native.ReadBytes.HasValue && native.WriteBytes.HasValue, "Read-only native counters work on the harmless test process with limited query rights");
        }
        var fake = new Platform();
        double at = 1;
        var collector = new PerformanceCollector(fake, "fixture-sid", 7, () => at);
        var first = await collector.CollectAsync(default);
        Check(first.Processes.Length == 1 && first.Processes[0].CpuPercent is null && first.Processes[0].ReadRate is null, "Performance first sample is unavailable and duplicate process candidates count once");
        at += 2; fake.Cpu += 20_000_000; fake.Read += 200; fake.Write += 400;
        var next = await collector.CollectAsync(default);
        Check(next.Processes.Single().CpuPercent == 25 && next.Processes.Single().ReadRate == 100 && next.Processes.Single().WriteRate == 200, "Performance CPU and I/O use monotonic lifetime deltas");
        fake.Created++;
        var reused = await collector.CollectAsync(default);
        Check(reused.Processes.Single().CpuPercent is null && reused.Processes.Single().ReadRate is null, "Performance PID reuse starts new measurement history");
        fake.Trusted = false;
        Check((await collector.CollectAsync(default)).Unavailable == 1, "Performance rejects untrusted candidates");
        fake.Trusted = true; fake.Owner = "other-owner";
        Check((await collector.CollectAsync(default)).Processes.Length == 0, "Performance excludes another owner");
        fake.Owner = "fixture-sid"; fake.ThrowEnumeration = true;
        Check(!(await collector.CollectAsync(default)).Available, "Performance enumeration failure is unavailable rather than empty success");
        Check(PerformanceCollector.Rate(100, 99, 1) is null && PerformanceCollector.Rate(1, 2, 0) is null, "Counter rollback and invalid intervals do not invent I/O rates");
        fake.ThrowEnumeration = false; fake.Delay = true;
        await Task.WhenAll(collector.CollectAsync(default), collector.CollectAsync(default));
        Check(fake.MaxActive == 1, "Performance collector serializes concurrent requests");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await collector.CollectAsync(cancellation.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(true, "Canceled performance collection does not run"); }
        string valid = PerformanceProfileStore.Export(PerformanceProfileStore.Defaults);
        Reject(() => PerformanceProfileStore.Parse(valid.Replace("\"version\":2", "\"version\":3")), "Profile future versions rejected");
        Reject(() => PerformanceProfileStore.Parse(valid.Replace("\"version\":2", "\"version\":2,\"version\":2")), "Profile duplicate keys rejected");
        Reject(() => PerformanceProfileStore.Parse(valid.Replace("\"version\":2", "\"version\":2,\"credentials\":\"secret-canary\"")), "Profiles refuse secret and arbitrary extension fields");
        Reject(() => PerformanceProfileStore.Parse(new string('x', PerformanceProfileStore.MaxBytes + 1)), "Oversized profile imports rejected before parsing");
        Reject(() => PerformanceProfileStore.Validate(PerformanceProfileStore.Defaults with { SelectedProfileId = "missing" }), "Profile selection must resolve");
        Reject(() => PerformanceProfileStore.Validate(PerformanceProfileStore.Defaults with { Profiles = [PerformanceProfileStore.Defaults.Profiles[0] with { FpsCap = 1 }] }), "Out-of-range profile values rejected");
        Check(PerformanceProfileStore.Parse("{\"version\":1,\"selectedProfileId\":\"legacy\",\"profiles\":[{\"id\":\"legacy\",\"name\":\"Legacy\",\"description\":\"\",\"cpuPriority\":\"unchanged\",\"fpsCap\":null,\"graphicsQuality\":null}]}").Profiles.Single().EcoQos=="unchanged", "Older profiles default EcoQoS to unchanged");
        var ecoProfiles=PerformanceProfileStore.Defaults with {Profiles=[PerformanceProfileStore.Defaults.Profiles[0] with {EcoQos="on"}]};
        Check(PerformanceProfileStore.Parse(PerformanceProfileStore.Export(ecoProfiles)).Profiles[0].EcoQos=="on", "EcoQoS profile values round trip");
        Reject(()=>PerformanceProfileStore.Validate(ecoProfiles with {Profiles=[ecoProfiles.Profiles[0] with {EcoQos="invalid"}]}), "Invalid EcoQoS profile values rejected");
        Reject(()=>PerformanceProfileStore.Validate(ecoProfiles with {Profiles=[ecoProfiles.Profiles[0] with {FpsCap=241}]}), "Version 2 FPS requests above 240 are refused");
        Reject(()=>PerformanceProfileStore.Validate(ecoProfiles with {Profiles=[ecoProfiles.Profiles[0] with {TextureQuality="arbitrary"}]}), "Visual requests accept only defined options");
        Reject(()=>PerformanceProfileStore.Validate(ecoProfiles with {Version=1,Enabled=true}), "Legacy preferences cannot silently enable automatic controls");
        string path = Path.Combine(root, "performance-fixture", "profiles.json");
        var store = new PerformanceProfileStore(path);
        store.Load(out bool available);
        Check(available && !File.Exists(path), "Performance profile reads do not create files");
        store.Save(PerformanceProfileStore.Defaults, default,PerformanceProfileStore.Revision(PerformanceProfileStore.Defaults));
        Check(new PerformanceProfileStore(path).Load(out available).Profiles.Length == 3 && available, "Performance profiles persist with atomic replacement");
        var changed = PerformanceProfileStore.Defaults with { SelectedProfileId = "balanced" };
        string staleRevision = PerformanceProfileStore.Revision(PerformanceProfileStore.Defaults);
        new PerformanceProfileStore(path).Save(changed,default,staleRevision);
        try {store.Save(PerformanceProfileStore.Defaults,default,staleRevision);throw new Exception("Stale profile replacement accepted");}
        catch(PerformanceProfileConflictException) {Check(store.Load(out _).SelectedProfileId=="balanced","Stale profile save preserves changes from another manager");}
        File.WriteAllText(path, "corrupt-original");
        store.Load(out available);
        Check(!available, "Corrupt profile storage reports unavailable");
        Reject(() => store.Save(PerformanceProfileStore.Defaults, default,PerformanceProfileStore.Revision(PerformanceProfileStore.Defaults)), "Saving refuses to overwrite corrupt original profiles");
        Check(File.ReadAllText(path) == "corrupt-original", "Corrupt profile original bytes survive save refusal");
        using var backend = new DesktopBackend(Path.Combine(root, "performance-backend"));
        var empty = JsonSerializer.SerializeToElement(new { });
        var state = (PerformanceProfilesDto)(await backend.ExecuteAsync("performance.profiles", empty, default))!;
        Check(state.Capabilities.All(x => !x.Applied) && state.Capabilities.Where(x => x.Key is "cpuPriority" or "fpsCap" or "graphicsQuality").All(x => x.Effective == "Unchanged"), "Profiles report requested settings separately without false applied success");
        var preview = (PerformanceProfilesDto)(await backend.ExecuteAsync("performance.previewImport", JsonSerializer.SerializeToElement(new { json = valid }), default))!;
        Check(preview.Profiles.Length == 3 && !Directory.Exists(Path.Combine(root, "performance-backend")), "Import preview validates without changing storage");
        try { await backend.ExecuteAsync("performance.import", JsonSerializer.SerializeToElement(new { json = valid }), default); throw new Exception("Import confirmation ignored"); }
        catch (DesktopOperationException) { Check(true, "Performance import requires explicit confirmation"); }
        var currentState = (PerformanceProfilesDto)(await backend.ExecuteAsync("performance.profiles",empty,default))!;
        var savePayload = JsonSerializer.SerializeToElement(new {selectedProfileId="balanced",profiles=currentState.Profiles,expectedRevision=currentState.Revision});
        await backend.ExecuteAsync("performance.saveProfiles",savePayload,default);
        try {await backend.ExecuteAsync("performance.saveProfiles",savePayload,default);throw new Exception("Stale backend save accepted");}
        catch(DesktopOperationException error) {Check(error.Code=="STATE_CHANGED","Backend stale profile save returns actionable state-change outcome");}
        try {await backend.ExecuteAsync("performance.import",JsonSerializer.SerializeToElement(new {json=valid,confirmed=true,expectedRevision=preview.Revision}),default);throw new Exception("Stale import accepted");}
        catch(DesktopOperationException error) {Check(error.Code=="STATE_CHANGED","Stale import preview cannot replace newer saved profiles");}
        Check(OperationCoordinator.Category("performance.saveProfiles") == "preferences" && OperationCoordinator.Category("performance.import") == "preferences" && OperationCoordinator.Category("performance.previewImport") == "read", "Performance mutation classification covers save and import");
        return passed;
    }
    sealed class Platform : IInstancePlatform
    {
        internal long Created = DateTime.UtcNow.AddMinutes(-1).ToFileTimeUtc(), Cpu = 10_000_000;
        internal ulong Read = 100, Write = 200;
        internal string Owner = "fixture-sid";
        internal bool Trusted = true, ThrowEnumeration, Delay;
        int active;
        internal int MaxActive;
        public int ProcessorCount => 4;
        public int[] Candidates() { if (ThrowEnumeration) throw new InvalidOperationException(); return [42, 42]; }
        public bool TrustedImage(string _) => Trusted;
        bool IInstancePlatform.Trusted(string image) => TrustedImage(image);
        public IInstanceProcess Open(int pid)
        {
            int count = Interlocked.Increment(ref active); MaxActive = Math.Max(MaxActive, count);
            if (Delay) Thread.Sleep(15);
            return new Process(this);
        }
        sealed class Process(Platform platform) : IInstanceProcess, IPerformanceProcess
        {
            public Identity Identity => new(platform.Owner, 7, false, @"C:\Fixture\RobloxPlayerBeta.exe", platform.Created);
            public bool IsAlive => true;
            public long? CpuTime() => platform.Cpu;
            public ulong? WorkingSet() => 123;
            public PerformanceCounters PerformanceCounters() => new(456, platform.Read, platform.Write);
            public InstanceWindow[] Windows() => [new(1, "one"), new(2, "two")];
            public bool OwnsWindow(nint window) => true;
            public void Restore(nint window) { }
            public bool Focus(nint window) => true;
            public bool Close(nint window) => true;
            public void Dispose() => Interlocked.Decrement(ref platform.active);
        }
    }
}
