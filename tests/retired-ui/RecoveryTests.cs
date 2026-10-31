using Microsoft.Win32;
using RobloxLaunchHelper;
using System.Text.Json;
using CheckState = RobloxLaunchHelper.CheckState;

internal static class RecoveryTests
{
    private const string Old = "\"C:\\Roblox\\old\\RobloxPlayerBeta.exe\" \"%1\"";
    private const string New = "\"C:\\Roblox\\new\\RobloxPlayerBeta.exe\" \"%1\"";
    private static void Check(bool value) { if (!value) throw new Exception("Recovery assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static RegistryTree Tree(string command)
    {
        var tree = ProtocolRecovery.WrapperTree();
        tree.Children["shell"].Children["open"].Children["command"].Values = [ProtocolRecovery.String("", command)];
        return tree;
    }
    private static void RegistryTest(Action<RegistryKey> test)
    {
        string path = @"Software\RobloxLaunchHelperRecoveryTests-" + Guid.NewGuid().ToString("N");
        try { using var key = Registry.CurrentUser.CreateSubKey(path); test(key); }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }
    internal static void Run(Action<string, Action> run)
    {
        run("Recovery prefers current verified registration and skips missing or untrusted targets", () =>
        {
            string Resolve(string?[] commands) => PlayerRecovery.Resolve(commands, () => throw new Exception("Unexpected discovery"), _ => throw new Exception(), p => !p.Contains("old"), _ => true);
            Check(Resolve([New, Old]) == New);
            Check(Resolve([Old, New]) == New);
            Check(PlayerRecovery.Resolve([Old, New], () => [], _ => throw new Exception(), _ => true, p => p.Contains("new")) == New);
            Throws<UserError>(() => PlayerRecovery.Resolve(["cmd.exe /c %1", Old], () => [], _ => throw new Exception(), _ => false, _ => false));
        });
        run("Folder recovery deduplicates, requires a selection, preserves arguments and handles cancellation", () =>
        {
            string one = @"C:\Roblox\new\RobloxPlayerBeta.exe", two = @"C:\Roblox\other\RobloxPlayerBeta.exe";
            bool Exists(string p) => !p.Contains("old");
            string original = "\"C:\\Roblox\\old\\RobloxPlayerBeta.exe\" --channel \"some value\" \"%1\"";
            string recovered = PlayerRecovery.Resolve([original], () => [one, one.ToUpperInvariant()], _ => throw new Exception("Should deduplicate"), Exists, _ => true);
            Check(Launch.SplitCommand(recovered).SequenceEqual(new[] { one, "--channel", "some value", "%1" }));
            bool chosen = false;
            recovered = PlayerRecovery.Resolve([], () => [one, two], paths => { chosen = true; Check(paths.Count == 2); return two; }, Exists, _ => true);
            Check(chosen && Launch.BuildStart(recovered, "roblox:test").FileName == two);
            Throws<OperationCanceledException>(() => PlayerRecovery.Resolve([], () => [one, two], _ => throw new OperationCanceledException(), Exists, _ => true));
            Throws<UserError>(() => PlayerRecovery.Resolve([], () => [one], _ => one, Exists, _ => false));
            string[] arguments = ["", "plain", "a b", "a\"b", @"C:\some folder\", "tail\\\"", "%1", "& $(fixture)"];
            Check(Launch.SplitCommand(string.Join(" ", arguments.Select(PlayerRecovery.Quote))).SequenceEqual(arguments));
        });
        run("Legacy settings default enabled and original registry backups remain immutable", () =>
        {
            var config = JsonSerializer.Deserialize<InstallConfig>("{\"Version\":1}")!;
            Check(config.BrowserIntegrationEnabled && config.PendingPreviousEnabled is null);
            config.Backups["roblox"] = Tree(Old);
            Installation.RememberOriginal(config, "roblox", Tree(New));
            config.Backups["roblox"] = Tree(New);
            Installation.RememberOriginal(config, "roblox", null);
            Check(ProtocolRecovery.Command(config.OriginalBackups["roblox"]) == Old);
            config = JsonSerializer.Deserialize<InstallConfig>(JsonSerializer.Serialize(config))!;
            Check(ProtocolRecovery.Command(config.Backups["roblox"]) == New && ProtocolRecovery.Command(config.OriginalBackups["roblox"]) == Old);
        });
        run("Repair and recovery preserve disabled preference; only explicit enable turns it on", () =>
        {
            foreach (string action in new[] { "setup", "repair", "recover", "disable-browser-integration", "uninstall" })
            {
                Check(Installation.IsAction(action)); Check(!Installation.DesiredIntegration(action, false));
            }
            Check(Installation.DesiredIntegration("enable-browser-integration", false));
            Check(!Installation.DesiredIntegration("disable-browser-integration", true));
            Check(!Installation.IsAction("unknown"));
        });
        run("Protocol checks distinguish disabled, changed, missing, invalid and untrusted registration", () =>
        {
            Check(DiagnosticChecks.CheckProtocol("p", New, false, _ => true, _ => true).Code == DiagnosticCode.IntegrationDisabled);
            Check(DiagnosticChecks.CheckProtocol("p", New, true, _ => true, _ => true).Code == DiagnosticCode.ProtocolChanged);
            Check(DiagnosticChecks.CheckProtocol("p", null, false).Code == DiagnosticCode.ProtocolMissing);
            Check(DiagnosticChecks.CheckProtocol("p", "cmd.exe", true).Code == DiagnosticCode.InvalidConfig);
            Check(DiagnosticChecks.CheckProtocol("p", Old, false, _ => false).Code == DiagnosticCode.PlayerMissing);
            Check(DiagnosticChecks.CheckProtocol("p", Old, false, _ => true, _ => false).Code == DiagnosticCode.SignatureRejected);
            Check(DiagnosticChecks.CheckProtocol("p", Installation.WrapperCommand, true).State == CheckState.Healthy);
            Check(DiagnosticChecks.CheckProtocol("p", Installation.WrapperCommand, false).State == CheckState.Failed);
        });
        run("Disable restores only owned values and preserves other installer metadata and subkeys", () => RegistryTest(parent =>
        {
            using (var key = parent.CreateSubKey("roblox")) Tree(Old).Restore(key);
            var before = ProtocolRecovery.Capture(parent, "roblox");
            foreach (var edit in ProtocolRecovery.Enable("roblox", before)) Check(ProtocolRecovery.WriteIfMatches(parent, "roblox", edit));
            using (var key = parent.OpenSubKey("roblox", true)!)
            {
                key.SetValue("", "Installer changed description"); key.SetValue("external", new byte[] { 2, 4 }, RegistryValueKind.Binary);
                using var nested = key.CreateSubKey("external-subkey"); nested.SetValue("value", "keep");
            }
            foreach (var edit in ProtocolRecovery.Restore("roblox", ProtocolRecovery.Capture(parent, "roblox"), before, ProtocolRecovery.WrapperTree(), New))
                Check(ProtocolRecovery.WriteIfMatches(parent, "roblox", edit));
            var restored = ProtocolRecovery.Capture(parent, "roblox")!;
            Check(ProtocolRecovery.Command(restored) == New);
            Check(ProtocolRecovery.Value(restored, "", "")!.Data.GetString() == "Installer changed description");
            Check(restored.Children.ContainsKey("external-subkey") && restored.Values.Any(v => v.Name == "external"));
            Check(ProtocolRecovery.Restore("roblox", restored, before, ProtocolRecovery.WrapperTree(), New).Count == 0);
            Check(ProtocolRecovery.Restore("roblox-player", Tree(New), Tree(Old), ProtocolRecovery.WrapperTree(), Old).Count == 0);
        }));
        run("Repeated enable/disable and uninstall restoration retain nested values and absent originals", () => RegistryTest(parent =>
        {
            using (var initial = parent.CreateSubKey("roblox")) Tree(Old).Restore(initial);
            var original = ProtocolRecovery.Capture(parent, "roblox");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                foreach (var edit in ProtocolRecovery.Enable("roblox", ProtocolRecovery.Capture(parent, "roblox"))) Check(ProtocolRecovery.WriteIfMatches(parent, "roblox", edit));
                Check(ProtocolRecovery.Enable("roblox", ProtocolRecovery.Capture(parent, "roblox")).Count == 0);
                foreach (var edit in ProtocolRecovery.Restore("roblox", ProtocolRecovery.Capture(parent, "roblox"), original, ProtocolRecovery.WrapperTree(), Old)) Check(ProtocolRecovery.WriteIfMatches(parent, "roblox", edit));
                Check(ProtocolRecovery.Command(ProtocolRecovery.Capture(parent, "roblox")) == Old);
            }
            foreach (var edit in ProtocolRecovery.Enable("new", null)) Check(ProtocolRecovery.WriteIfMatches(parent, "new", edit));
            foreach (var edit in ProtocolRecovery.Restore("new", ProtocolRecovery.Capture(parent, "new"), null, ProtocolRecovery.WrapperTree(), New)) Check(ProtocolRecovery.WriteIfMatches(parent, "new", edit));
            Check(ProtocolRecovery.Value(ProtocolRecovery.Capture(parent, "new"), "", "URL Protocol")!.Data.GetString() == "");
            Check(ProtocolRecovery.Command(ProtocolRecovery.Capture(parent, "new")) == New);
        }));
        run("Restoration retains expandable command value kinds when the official target is unchanged", () =>
        {
            var backup = Tree(Old);
            var command = backup.Children["shell"].Children["open"].Children["command"];
            string raw = "\"%SystemDrive%\\Roblox\\old\\RobloxPlayerBeta.exe\" \"%1\"";
            command.Values = [new StoredValue("", RegistryValueKind.ExpandString, JsonSerializer.SerializeToElement(raw))];
            string expanded = Environment.ExpandEnvironmentVariables(raw);
            var edit = ProtocolRecovery.Restore("roblox", ProtocolRecovery.WrapperTree(), backup, ProtocolRecovery.WrapperTree(), expanded).Single();
            Check(edit.After!.Kind == RegistryValueKind.ExpandString && edit.After.Data.GetString() == raw);
            Check(ProtocolRecovery.CommandValue(edit.After) == expanded);
        });
        run("Registry transaction journals before writes and rolls back failed normal-context verification", () => RegistryTest(parent =>
        {
            using (var key = parent.CreateSubKey("roblox")) Tree(Old).Restore(key);
            var config = new InstallConfig(); int saves = 0;
            void Persist() { saves++; Check(saves != 1 || config.PendingPreviousEnabled == true); }
            bool Write(ProtocolEdit edit, bool rollback) { Check(saves > 0); return ProtocolRecovery.WriteIfMatches(parent, "roblox", edit, rollback); }
            Throws<IOException>(() => RecoveryTransaction.Run(config, false, ProtocolRecovery.Enable("roblox", Tree(Old)), Persist, Write, () => { }, () => throw new IOException("verification failed")));
            Check(config.BrowserIntegrationEnabled && config.PendingPreviousEnabled is null && saves == 2);
            Check(ProtocolRecovery.Command(ProtocolRecovery.Capture(parent, "roblox")) == Old);
            int writes = 0;
            Throws<IOException>(() => RecoveryTransaction.Run(config, false, [], () => throw new IOException("disk full"), (_, _) => { writes++; return true; }, () => { }, () => { }));
            Check(writes == 0);
        }));
        run("Interrupted journal replay is idempotent and preserves later installer commands", () => RegistryTest(parent =>
        {
            var original = Tree(Old); original.Values = [ProtocolRecovery.String("", "Official Roblox")];
            using (var key = parent.CreateSubKey("roblox")) original.Restore(key);
            var edits = ProtocolRecovery.Enable("roblox", original);
            Check(edits.Count == 3);
            var config = new InstallConfig { BrowserIntegrationEnabled = false, PendingPreviousEnabled = true, PendingChanges = edits };
            string disk = JsonSerializer.Serialize(config);
            // Crash after one edit. Replay also handles edits that had not been applied.
            Check(ProtocolRecovery.WriteIfMatches(parent, "roblox", edits[0]));
            config = JsonSerializer.Deserialize<InstallConfig>(disk)!;
            bool Write(ProtocolEdit e, bool rollback) => ProtocolRecovery.WriteIfMatches(parent, "roblox", e, rollback);
            Check(RecoveryTransaction.Resume(config, () => { }, Write));
            Check(RecoveryTransaction.Resume(config, () => { }, Write));
            Check(config.BrowserIntegrationEnabled && ProtocolRecovery.Command(ProtocolRecovery.Capture(parent, "roblox")) == Old);
            foreach (var edit in edits) Check(Write(edit, false));
            using (var command = parent.OpenSubKey(@"roblox\shell\open\command", true)!) command.SetValue("", New);
            config = JsonSerializer.Deserialize<InstallConfig>(disk)!;
            Check(!RecoveryTransaction.Resume(config, () => { }, Write));
            Check(ProtocolRecovery.Command(ProtocolRecovery.Capture(parent, "roblox")) == New);
            Check(!ProtocolRecovery.WriteIfMatches(parent, "roblox", edits.Last()));
        }));
        run("Mixed protocol ownership leaves the external scheme untouched during disable", () => RegistryTest(parent =>
        {
            using (var key = parent.CreateSubKey("roblox")) ProtocolRecovery.WrapperTree().Restore(key);
            using (var key = parent.CreateSubKey("roblox-player")) Tree(New).Restore(key);
            var owned = ProtocolRecovery.WrapperTree();
            var edits = new List<ProtocolEdit>();
            foreach (string scheme in Installation.Schemes)
                edits.AddRange(ProtocolRecovery.Restore(scheme, ProtocolRecovery.Capture(parent, scheme), Tree(Old), owned, Old));
            Check(edits.All(e => e.Scheme == "roblox"));
            var config = new InstallConfig();
            RecoveryTransaction.Run(config, false, edits, () => { }, (e, rollback) => ProtocolRecovery.WriteIfMatches(parent, e.Scheme, e, rollback), () => { }, () => { });
            Check(!config.BrowserIntegrationEnabled && config.PendingPreviousEnabled is null);
            Check(ProtocolRecovery.Command(ProtocolRecovery.Capture(parent, "roblox")) == Old);
            Check(ProtocolRecovery.Command(ProtocolRecovery.Capture(parent, "roblox-player")) == New);
        }));
        run("Canceled preparation cannot write registry values and failed commit save still rolls back", () => RegistryTest(parent =>
        {
            using (var key = parent.CreateSubKey("roblox")) Tree(Old).Restore(key);
            var config = new InstallConfig(); int writes = 0;
            bool Write(ProtocolEdit edit, bool rollback) { if (!rollback) writes++; return ProtocolRecovery.WriteIfMatches(parent, "roblox", edit, rollback); }
            Throws<OperationCanceledException>(() => RecoveryTransaction.Run(config, false, ProtocolRecovery.Enable("roblox", Tree(Old)), () => { }, Write, () => throw new OperationCanceledException(), () => { }));
            Check(writes == 0 && config.BrowserIntegrationEnabled);
            int saves = 0;
            Throws<IOException>(() => RecoveryTransaction.Run(config, false, ProtocolRecovery.Enable("roblox", Tree(Old)), () => { if (++saves == 2) throw new IOException(); }, Write, () => { }, () => { }));
            Check(saves == 3 && config.BrowserIntegrationEnabled && ProtocolRecovery.Command(ProtocolRecovery.Capture(parent, "roblox")) == Old);
        }));
    }
}
