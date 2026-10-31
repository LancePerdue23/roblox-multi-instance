using System.Security.Cryptography;
using System.Text.Json;
namespace RobloxLaunchHelper;

internal static class HelperReadiness
{
    internal static string Fingerprint()
    {
        try
        {
            static byte[] Read(string path){if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException();return File.ReadAllBytes(path);}
            var bytes=Read(Program.ConfigPath);var config=JsonSerializer.Deserialize<InstallConfig>(bytes)??throw new InvalidDataException();
            string metadata=File.Exists(Distribution.MetadataPath)?Convert.ToHexString(SHA256.HashData(Read(Distribution.MetadataPath))):"legacy";
            string players=string.Join('/',config.Commands.Values.Select(command=>{var file=new FileInfo(Launch.BuildStart(command,null).FileName);return file.Exists?$"{file.Length}:{file.LastWriteTimeUtc.Ticks}":"missing";}));
            var helper=new FileInfo(Program.InstalledExe);
            return Convert.ToHexString(SHA256.HashData(bytes))+"/"+metadata+"/"+helper.LastWriteTimeUtc.Ticks+"/"+helper.Length+"/"+players;
        }
        catch { return "unavailable"; }
    }
    internal static string Evaluate(HealthSnapshot health, HealthCheckResult probe, bool installed)
    {
        if(!installed)return "Not installed";
        var checks=health.Checks.Where(x=>x.Id is not ("history" or "logs")).ToArray();
        if(checks.Any(x=>x.Code is DiagnosticCode.HelperVersionMismatch or DiagnosticCode.LegacyInstallation))return "Update required";
        if(checks.Any(x=>x.State==CheckState.Failed||x.State==CheckState.Warning&&x.Action is Remediation.Repair or Remediation.ReinstallRoblox or Remediation.Setup))return "Needs repair";
        if(checks.Any(x=>x.State==CheckState.Unknown))return "Unable to verify";
        if(probe.State==CheckState.Failed)return "Needs repair";
        return probe.Code==DiagnosticCode.WorkerReady && probe.State==CheckState.Healthy ? "Ready" : "Installed — not tested";
    }
}
