using System.Runtime.InteropServices;
namespace RobloxLaunchHelper;
// Migration only: never enumerate, read, write, or prompt for credentials.
internal static class UpdateCredentialMigration
{
    internal const string Target = "RobloxMultiInstance/GitHubUpdates/" + UpdateUrls.Repository;
    [DllImport("advapi32.dll", EntryPoint="CredDeleteW", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool Delete(string target, uint type, uint flags);
    internal static void RemoveOwnedCredential(Func<string,uint,uint,int>? remove = null)
    {
        if (Program.Elevated) return;
        int error = remove is null ? (Delete(Target, 1, 0) ? 0 : Marshal.GetLastWin32Error()) : remove(Target, 1, 0);
        if (error is not (0 or 1168)) Log.Write("update-credential-migration-failed", "", error);
    }
}
