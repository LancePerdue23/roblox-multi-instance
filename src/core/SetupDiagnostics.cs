using System.Diagnostics;
namespace RobloxLaunchHelper;

// Only fixed stages and source-code symbols enter logs. Never serialize exception
// messages, file paths, arguments, identities, or full stack traces.
internal enum SetupStage { Startup, PayloadOpen, ManifestValidation, StagingPrepare, PayloadExtract, PayloadVerify, UpgradeValidation, InstallDirectoryProtection, InstallLock, RollbackBackup, PayloadCopy, InstallerCopy, MetadataWrite, VerificationTask, Rollback, Cleanup, Complete }
internal static class SetupDiagnostics
{
    [ThreadStatic] private static SetupStage stage;
    [ThreadStatic] private static string? attempt;
    internal static void Begin() { attempt=Guid.NewGuid().ToString("N"); Step(SetupStage.Startup); Log.Write("edition-setup-version",typeof(SetupDiagnostics).Assembly.GetName().Version?.ToString()??"unknown"); }
    internal static void Step(SetupStage value) { stage=value; Log.Write("edition-setup-stage",(attempt??"none")+" "+value); }
    internal static string Describe(Exception error)
    {
        string kind=error switch { UserError=>"UserError", InvalidOperationException=>"InvalidOperationException", UnauthorizedAccessException=>"AccessDenied", System.ComponentModel.Win32Exception=>"WindowsError", System.Runtime.InteropServices.COMException=>"ComError", InvalidDataException=>"InvalidData", IOException=>"IoError", System.Text.Json.JsonException=>"JsonError", _=>"UnexpectedError" };
        var frames=new StackTrace(error,false).GetFrames()??[];
        var sites=frames.Select(f=>f.GetMethod()).Where(m=>m?.DeclaringType?.Assembly==typeof(SetupDiagnostics).Assembly || m?.DeclaringType?.Assembly.GetName().Name=="RobloxMultiInstance.Setup")
            .Take(6).Select(m=>m!.DeclaringType!.Name+"."+m.Name);
        string reason=error.Message switch {
            "The installation directory must not be a junction or symbolic link."=>"DirectoryLinkRejected",
            "Installation files must not contain symbolic links or junctions."=>"PayloadLinkRejected",
            "Installed files are writable without elevation."=>"FilePermissionsUnprotected",
            "Installed file ownership is not protected."=>"FileOwnerUnprotected",
            "The installation directory is writable without elevation."=>"DirectoryPermissionsUnprotected",
            "Installation settings do not match this account."=>"ConfigOwnerMismatch",
            "Installation settings are missing. Run Repair."=>"ConfigMissing",
            "Shortcut creation must run without elevation."=>"ShortcutTokenElevated",
            "A shortcut with this name already belongs to another application."=>"ShortcutConflict",
            _=>"Unclassified"
        };
        return kind+" reason="+reason+" site="+string.Join(">",sites);
    }
    internal static string FailureDetail(Exception error) => (attempt??"none")+" stage="+stage+" "+Describe(error);
    internal static void Failure(Exception error)
    {
        Log.Write("edition-setup-failed",FailureDetail(error),error.HResult);
        for(int depth=0;depth<4;depth++)
        {
            if(error is System.ComponentModel.Win32Exception native)Log.Write("edition-setup-native-error","",native.NativeErrorCode);
            if(error.InnerException is not {} inner)break;
            Log.Write("edition-setup-inner",Describe(inner),inner.HResult);
            error=inner;
        }
    }
}
