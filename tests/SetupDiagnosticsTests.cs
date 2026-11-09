using RobloxLaunchHelper;
internal static class SetupDiagnosticsTests
{
    internal static void Run(Action<string,Action> run)
    {
        run("Setup diagnostics identify stage and omit sensitive exception contents",()=>
        {
            SetupDiagnostics.Step(SetupStage.PayloadCopy);
            const string secret="PRIVATE_CANARY_COOKIE_C:\\Users\\Example";
            Exception error;
            try { throw new IOException(secret,new InvalidOperationException(secret)); } catch(Exception ex) { error=ex; }
            string detail=SetupDiagnostics.FailureDetail(error);
            if(!detail.Contains("stage=PayloadCopy")||!detail.Contains("IoError")||detail.Contains(secret)||detail.Contains("Users"))throw new Exception("Unsafe or incomplete setup diagnostic");
            if(!SetupDiagnostics.Describe(new InvalidOperationException("Shortcut creation must run without elevation.")).Contains("reason=ShortcutTokenElevated"))throw new Exception("Known failure reason lost");
            if(SetupDiagnostics.Describe(error.InnerException!).Contains(secret))throw new Exception("Inner error leaked");
            if(!SetupDiagnostics.Describe(new System.ComponentModel.Win32Exception(5,secret)).StartsWith("WindowsError"))throw new Exception("Windows error classification lost");
        });
    }
}
