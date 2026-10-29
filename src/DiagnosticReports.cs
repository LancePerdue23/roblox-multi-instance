using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RobloxLaunchHelper;

internal sealed record ReportCheck(string Id, string State, string Code, DateTimeOffset ObservedUtc, string Action, int? ErrorCode);
internal sealed record ReportLaunch(int Number, DateTimeOffset StartedUtc, DateTimeOffset? EndedUtc, long? DurationMs,
    string Source, bool? RobloxStarted, string Outcome, string Code, int? ErrorCode);
internal sealed record ReportEvent(string Source, DateTimeOffset TimeUtc, string Code);
internal sealed record TroubleshootingReport(int SchemaVersion, DateTimeOffset ExportedUtc, string HelperVersion,
    string WindowsVersion, string Architecture, string RobloxVersion, DateTimeOffset CollectedUtc, string Overall, ReportCheck[] Checks,
    ReportCheck WorkerTest, ReportLaunch[] Launches, ReportEvent[] Events, string[] Omissions);
internal sealed record ReportPackage(string Summary, byte[] Json, int LaunchCount, int EventCount);

internal static class DiagnosticReports
{
    internal const int MaxReportBytes = 1024 * 1024;
    internal static string[] LogDirectories => [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.Product, "logs"),
        Path.Combine(Program.InstallDirectory, "logs"), Path.Combine(Program.InstallRoot, "setup-logs")];
    private static readonly HashSet<string> CheckIds = ["installation", "helper", "player-roblox", "player-roblox-player", "protocol-roblox", "protocol-roblox-player", "task", "file-permissions", "history", "logs", "worker"];
    private static readonly HashSet<string> EventCodes = ["launch-requested", "roblox-launched", "launch-warning", "worker-unavailable", "worker-result-failed",
        "worker-started", "worker-exited", "worker-client-failed", "singleton-closed", "close-result-uncertain", "process-access-denied", "roblox-signature-rejected",
        "scan-process-failed", "probe-start", "probe-complete", "setup-probe-failed", "setup-context", "setup-stage", "setup-complete", "setup-rollback",
        "protocol-registration-verified", "limited-probe-result", "operation-failed"];
    private static string Version(string input) => Regex.IsMatch(input ?? "", @"\A[0-9]{1,5}(\.[0-9]{1,5}){1,3}\z", RegexOptions.CultureInvariant) ? input! : "unknown";
    private static ReportCheck Sanitize(HealthCheckResult c) => new(CheckIds.Contains(c.Id) ? c.Id : "unknown",
        Enum.IsDefined(c.State) ? c.State.ToString() : "Unknown", Enum.IsDefined(c.Code) ? c.Code.ToString() : "InspectionUnavailable",
        c.ObservedUtc.ToUniversalTime(), Enum.IsDefined(c.Action) ? c.Action.ToString() : "None", c.ErrorCode);
    internal static ReportPackage Build(HealthSnapshot snapshot, IEnumerable<LaunchResult> records, bool historyAvailable,
        IEnumerable<(string Source, string Directory)>? sources = null)
    {
        var omissions = new List<string>();
        if (!historyAvailable) omissions.Add("history-unavailable");
        var events = new List<ReportEvent>();
        sources ??= LogDirectories.Select((d, i) => (new[] { "launcher", "worker", "setup" }[i], d));
        foreach (var source in sources)
        {
            // Caller-supplied source strings are never serialized.
            string label = source.Source is "launcher" or "worker" or "setup" ? source.Source : "unknown";
            foreach (string name in new[] { "helper.log", "helper.log.previous" })
            {
                try
                {
                    for (string? dir = Path.GetFullPath(source.Directory); dir is not null; dir = Path.GetDirectoryName(dir))
                        if (Directory.Exists(dir) && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw new IOException();
                    string path = Path.Combine(source.Directory, name);
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException();
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    const int limit = 256 * 1024;
                    bool truncated = file.Length > limit;
                    if (truncated) { file.Seek(-limit, SeekOrigin.End); omissions.Add(label + "-log-truncated"); }
                    var bytes = new byte[limit]; int read = 0, n;
                    while (read < limit && (n = file.Read(bytes, read, limit - read)) > 0) read += n;
                    using var reader = new StringReader(Encoding.UTF8.GetString(bytes, 0, read));
                    if (truncated) reader.ReadLine();
                    while (reader.ReadLine() is { } line)
                    {
                        string[] words = line.Split(' ', 3);
                        if (words.Length < 2 || !EventCodes.Contains(words[1]) || !DateTimeOffset.TryParseExact(words[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) continue;
                        events.Add(new(label, time.ToUniversalTime(), words[1]));
                    }
                }
                catch { omissions.Add(label + "-log-unavailable"); }
            }
        }
        if (events.Count > 200) omissions.Add("log-events-truncated");
        var launches = records.Where(DiagnosticHistory.Valid).OrderByDescending(r => r.StartedUtc).Take(100)
            .Select((r, i) => new ReportLaunch(i + 1, r.StartedUtc.ToUniversalTime(), r.EndedUtc?.ToUniversalTime(), r.DurationMs,
                r.Source.ToString(), r.RobloxStarted, r.Outcome.ToString(), r.Code.ToString(), r.ErrorCode)).ToArray();
        var report = new TroubleshootingReport(1, DateTimeOffset.UtcNow, Version(snapshot.HelperVersion), Version(snapshot.WindowsVersion),
            snapshot.Architecture is "X64" or "X86" or "Arm64" or "Arm" ? snapshot.Architecture : "unknown", Version(snapshot.RobloxVersion), snapshot.CollectedUtc.ToUniversalTime(), snapshot.Overall.ToString(),
            snapshot.Checks.Take(32).Select(Sanitize).ToArray(), Sanitize(snapshot.WorkerTest), launches,
            events.OrderByDescending(e => e.TimeUtc).Take(200).ToArray(), omissions.Distinct().ToArray());
        string summary = Summary(report);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(report, Program.JsonOptions);
        while (json.Length + Encoding.UTF8.GetByteCount(summary) > MaxReportBytes && report.Events.Length > 0)
        {
            report = report with { Events = report.Events.Take(report.Events.Length - 1).ToArray(), Omissions = report.Omissions.Append("report-size-truncated").Distinct().ToArray() };
            summary = Summary(report); json = JsonSerializer.SerializeToUtf8Bytes(report, Program.JsonOptions);
        }
        if (json.Length + Encoding.UTF8.GetByteCount(summary) > MaxReportBytes) throw new IOException("Report exceeds the size limit.");
        return new(summary, json, launches.Length, report.Events.Length);
    }
    private static string Summary(TroubleshootingReport r)
    {
        var text = new StringBuilder("Roblox Multi Instance troubleshooting report\n");
        text.AppendLine($"Report schema: {r.SchemaVersion}; exported UTC: {r.ExportedUtc:O}");
        text.AppendLine($"Helper: {r.HelperVersion}; Windows: {r.WindowsVersion}; Roblox: {r.RobloxVersion}; architecture: {r.Architecture}");
        text.AppendLine($"Snapshot UTC: {r.CollectedUtc:O}; configuration status: {r.Overall}");
        text.AppendLine("Configuration checks do not prove game/account success. Registry results describe this process's view; Setup/Repair verifies normal Windows context separately.");
        foreach (var c in r.Checks.Append(r.WorkerTest))
        {
            text.AppendLine($"{c.Id}: {c.State} / {c.Code}; UTC {c.ObservedUtc:O}; action {c.Action}; error {c.ErrorCode}");
            if (Enum.TryParse<DiagnosticCode>(c.Code, out var code)) text.AppendLine(DiagnosticMessages.Text(code));
        }
        text.AppendLine($"Recent attempts: {r.Launches.Length}. Timestamps and launch activity may be sensitive.");
        foreach (var launch in r.Launches) text.AppendLine($"#{launch.Number} {launch.StartedUtc:O}: {launch.Source}; {launch.Outcome}; {launch.Code}; Roblox started: {launch.RobloxStarted?.ToString() ?? "Unknown"}; duration ms: {launch.DurationMs}");
        text.AppendLine($"Sanitized log events: {r.Events.Length}. Raw logs, identities, paths, URLs, commands, and stack traces excluded.");
        foreach (var e in r.Events) text.AppendLine($"{e.TimeUtc:O} {e.Source} {e.Code}");
        text.AppendLine("Omissions: " + (r.Omissions.Length == 0 ? "none" : string.Join(", ", r.Omissions)));
        return text.ToString();
    }
    internal static void Write(string path, ReportPackage package)
    {
        if (package.Json.Length + Encoding.UTF8.GetByteCount(package.Summary) > MaxReportBytes) throw new IOException("Report exceeds the size limit.");
        string destination = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(destination)!, ".rlh-report-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("summary.txt").Open(), new UTF8Encoding(false))) writer.Write(package.Summary);
                using var stream = zip.CreateEntry("diagnostics.json").Open(); stream.Write(package.Json);
            }
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
