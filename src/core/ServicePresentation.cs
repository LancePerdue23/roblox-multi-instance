using System.Net.Http;
using System.Diagnostics;
namespace RobloxLaunchHelper;
internal static class ServicePresentation
{
 internal static Action<string>? Warning;
 internal static Action? AutomaticUpdate=TryAutomatic;
 static void TryAutomatic()
 {
     try
     {
         if(Program.Elevated||Distribution.Installed()?.Edition!="Desktop"||new WindowsBootIdentity().Read() is not Guid boot)return;
         if(!new UpdateAttemptStore(UpdateFiles.Root,@"Global\RobloxLaunchHelper-UpdateAttempt-"+Program.Sid).TryClaim(boot))return;
         var start=new ProcessStartInfo(Path.Combine(Program.InstallDirectory,"RobloxMultiInstance.exe")){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--check-updates");Process.Start(start)?.Dispose();
     }
     catch { /* A best-effort notice must never prevent a game starting. */ }
 }
}
internal static class UpdateHost
{
 internal static HttpClient CreateClient()=>new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=Timeout.InfiniteTimeSpan };
}
