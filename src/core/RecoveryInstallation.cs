using Microsoft.Win32;
using System.Text.Json;

namespace RobloxLaunchHelper;

internal static partial class Installation
{
    internal static bool IsAction(string action) => action is "setup" or "repair" or "recover" or "uninstall" or "enable-browser-integration" or "disable-browser-integration";
    internal static bool DesiredIntegration(string action, bool previous) => action switch
    {
        "enable-browser-integration" => true,
        "disable-browser-integration" or "uninstall" => false,
        _ => previous
    };

    internal static void VerifyProtocolRegistration()
    {
        var config = ReadConfig();
        foreach (string scheme in Schemes)
        {
            string? expected = config.ExpectedProtocols.TryGetValue(scheme, out var saved) ? saved : WrapperCommand;
            if (!string.Equals(CurrentCommand(scheme), expected, StringComparison.Ordinal))
                throw new InvalidOperationException("The Windows browser launch registration changed or was not applied.");
            if (IsWrapper(expected) || config.PendingChanges.Any(e => e.Scheme == scheme && e.Path == ProtocolRecovery.CommandPath))
            {
                using var user = Registry.CurrentUser.OpenSubKey(ProtocolPath(scheme));
                using var machine = Registry.LocalMachine.OpenSubKey(ProtocolPath(scheme));
                if (!(user?.GetValueNames().Contains("URL Protocol", StringComparer.OrdinalIgnoreCase) == true ||
                    machine?.GetValueNames().Contains("URL Protocol", StringComparer.OrdinalIgnoreCase) == true))
                    throw new InvalidOperationException("Windows could not confirm this launch target as a URL protocol. Its external metadata may have changed.");
            }
        }
    }

    internal static void SaveConfig(InstallConfig config)
    {
        string temporary = Path.Combine(Program.InstallDirectory, "installation-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, config, Program.JsonOptions); file.Flush(true);
            }
            ProtectFile(temporary);
            File.Move(temporary, Program.ConfigPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void RememberOriginal(InstallConfig config, string scheme, RegistryTree? current)
    {
        if (config.OriginalBackups.ContainsKey(scheme)) return;
        config.OriginalBackups[scheme] = config.Backups.TryGetValue(scheme, out var legacy) ? legacy : current;
    }

    private static void RollbackPending(InstallConfig config)
    {
        if (config.PendingPreviousEnabled is null) return;
        bool conflict = !RecoveryTransaction.Resume(config, () => SaveConfig(config), WriteEdit);
        ShellAssociations.NotifyChanged();
        Log.Write(conflict ? "recovery-rollback-conflict" : "recovery-rolled-back");
        if (conflict) throw new UserError("An interrupted operation was recovered, but another installer changed launch values. Those values were preserved. Check again before retrying.");
    }

    private static bool WriteEdit(ProtocolEdit edit, bool rollback)
        => ProtocolRecovery.WriteIfMatches(Registry.CurrentUser, ProtocolPath(edit.Scheme), edit, rollback);

    internal static void SetBrowserIntegration(bool enabled) => Execute(
        enabled ? "enable-browser-integration" : "disable-browser-integration", Program.Sid, silent: true);

    internal static void Execute(string action, string owner, bool silent = false)
    {
        if (!Program.Elevated || owner != Program.Sid || !IsAction(action)) throw new InvalidOperationException("Setup needs administrator access from this Windows account.");
        using var gate = new Mutex(false, @"Global\RobloxLaunchHelper-Installation-" + owner);
        bool held = false;
        try
        {
            try { held = gate.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new UserError("Another installation action is running. Wait for it to finish, then retry.");
            ExecuteLocked(action, owner, silent);
        }
        finally { if (held) gate.ReleaseMutex(); }
    }

    private static void ExecuteLocked(string action, string owner, bool silent)
    {
        bool existing = File.Exists(Program.ConfigPath);
        if (!existing && action != "setup") throw new UserError("No installation was found for this Windows account. Run Setup first.");
        var config = existing ? ReadConfig() : new InstallConfig { OwnerSid = owner };
        RollbackPending(config);
        string? selectedPlayer=null;
        if(File.Exists(Distribution.RequestPath))
        {
            var request=JsonSerializer.Deserialize<SetupRequest>(File.ReadAllBytes(Distribution.RequestPath))??throw new InvalidDataException();
            if(!existing)config.BrowserIntegrationEnabled=request.Integration;
            if(request.Player is string selected)
            {
                if(!PlayerRecovery.Valid($"\"{selected}\" \"%1\""))throw new UserError("The selected player failed verification.");
                selectedPlayer=selected;
                PlayerRecovery.PlayerSelector=paths=>paths.Contains(selected,StringComparer.OrdinalIgnoreCase)?selected:throw new UserError("The selected player changed.");
            }
        }
        bool previousEnabled = config.BrowserIntegrationEnabled;
        bool desiredEnabled = DesiredIntegration(action, previousEnabled);
        var before = Schemes.ToDictionary(s => s, s => ProtocolRecovery.Capture(Registry.CurrentUser, ProtocolPath(s)));
        var edits = new List<ProtocolEdit>();
        var targets = new List<string>();
        foreach (string scheme in Schemes)
        {
            RememberOriginal(config, scheme, before[scheme]);
            string? current = CurrentCommand(scheme);
            bool wrapper = IsWrapper(current);
            // Disable/uninstall leave externally owned protocols untouched and need no player for them.
            bool needsPlayer = action is not ("disable-browser-integration" or "uninstall") || wrapper;
            if (needsPlayer)
            {
                string? backup = ProtocolRecovery.Command(config.Backups.GetValueOrDefault(scheme));
                string? original = ProtocolRecovery.Command(config.OriginalBackups.GetValueOrDefault(scheme));
                config.Commands[scheme] = PlayerRecovery.Resolve([selectedPlayer is null?null:PlayerRecovery.WithTarget(config.Commands.GetValueOrDefault(scheme)??(wrapper?null:current),selectedPlayer),wrapper ? null : current, config.Commands.GetValueOrDefault(scheme), backup, original,
                    config.Commands.GetValueOrDefault("roblox-player")], PlayerRecovery.InstalledPlayers,
                    PlayerRecovery.Choose);
                targets.Add(config.Commands[scheme]);
            }
            if (desiredEnabled)
            {
                if (!wrapper)
                {
                    config.Backups[scheme] = before[scheme];
                    config.WrittenProtocols[scheme] = ProtocolRecovery.WrapperTree();
                    edits.AddRange(ProtocolRecovery.Enable(scheme, before[scheme]));
                }
            }
            else if (wrapper)
            {
                var owned = config.WrittenProtocols.GetValueOrDefault(scheme) ?? ProtocolRecovery.WrapperTree();
                var restoration = ProtocolRecovery.Restore(scheme, before[scheme], config.Backups.GetValueOrDefault(scheme), owned, config.Commands[scheme]);
                if (!restoration.Any(e => e.Path == ProtocolRecovery.CommandPath))
                    throw new UserError("The helper registration was changed and its ownership could not be established. It was preserved. Reinstall official Roblox before retrying.");
                edits.AddRange(restoration);
            }
        }
        ProtectDirectory(Program.InstallRoot, true); ProtectDirectory(Program.InstallDirectory, false); RejectReparseTree(Program.InstallDirectory);
        config.ExpectedProtocols = Schemes.ToDictionary(s => s, s => ProtocolRecovery.CommandValue(edits.LastOrDefault(e => e.Scheme == s && e.Path == ProtocolRecovery.CommandPath)?.After) ?? CurrentCommand(s));
        bool registered = false;
        try
        {
            RecoveryTransaction.Run(config, desiredEnabled, edits, () => SaveConfig(config), WriteEdit, () =>
            {
                if (action is "setup" or "repair" or "recover")
                {
                    CopyPayload(); ScheduledWorker.Register(); registered = true;
                    if (RunUnelevated("--verify", TimeSpan.FromSeconds(35)) != 0)
                        throw new UserError("The helper could not be started from a normal Windows token. Check Task Scheduler permissions and diagnostics.");
                }
                foreach (string command in targets.Distinct(StringComparer.Ordinal))
                    if (!PlayerRecovery.Valid(command)) throw new UserError("The selected Roblox player disappeared or no longer passes its signature check. Reinstall official Roblox and retry recovery.");
            }, () =>
            {
                ShellAssociations.NotifyChanged();
                if (RunUnelevated("--verify-protocols", TimeSpan.FromSeconds(15)) != 0)
                    throw new UserError("Windows did not confirm the browser launch registration. Changes will be rolled back where still owned.");
                if (action is "setup" or "repair" or "recover")
                    if (RunUnelevated("--shortcuts install", TimeSpan.FromSeconds(15)) != 0) throw new UserError("Shortcut setup failed.");
            });
            Log.Write("recovery-complete");
        }
        catch
        {
            ShellAssociations.NotifyChanged();
            if (!existing && registered) ScheduledWorker.Remove();
            throw;
        }
        if (action == "uninstall") { Uninstall(); return; }
        if (silent) return;
    }
}
