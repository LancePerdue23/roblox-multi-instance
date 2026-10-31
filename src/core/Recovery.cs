using Microsoft.Win32;
using System.Text.Json;

namespace RobloxLaunchHelper;

internal static class PlayerRecovery
{
    internal static bool Valid(string? command, Func<string, bool>? exists = null, Func<string, bool>? trusted = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        try
        {
            string path = Launch.BuildStart(command, null).FileName;
            return (exists ?? File.Exists)(path) && (trusted ?? RobloxTrust.IsSignedRoblox)(path);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return false; }
    }

    internal static IEnumerable<string> InstalledPlayers()
    {
        string[] roots = [Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)];
        foreach (string root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string versions = Path.Combine(root, "Roblox", "Versions");
            string[] directories;
            try
            {
                if (!Directory.Exists(versions) || (File.GetAttributes(versions) & FileAttributes.ReparsePoint) != 0) continue;
                directories = Directory.GetDirectories(versions);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (string directory in directories)
            {
                bool safe;
                try { safe = (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                if (!safe) continue;
                yield return Path.Combine(directory, "RobloxPlayerBeta.exe");
            }
        }
    }

    internal static string Resolve(IEnumerable<string?> commands, Func<IEnumerable<string>> discover,
        Func<IReadOnlyList<string>, string> choose, Func<string, bool>? exists = null, Func<string, bool>? trusted = null)
    {
        var saved = commands.Where(c => !string.IsNullOrWhiteSpace(c)).Cast<string>().ToArray();
        foreach (string command in saved) if (Valid(command, exists, trusted)) return command;
        var candidates = discover().Where(p => Valid($"\"{p}\" \"%1\"", exists, trusted))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (candidates.Length == 0) throw new UserError("No trusted official Roblox player was found. Reinstall official Roblox, then run Recover/Repair. No launch registration was changed.");
        string selected = candidates.Length == 1 ? candidates[0] : choose(candidates);
        if (!candidates.Contains(selected, StringComparer.OrdinalIgnoreCase)) throw new OperationCanceledException();
        // Parse and quote each argument again; never forward commands through a shell.
        foreach (string command in saved)
        {
            try
            {
                Launch.BuildStart(command, null);
                var words = Launch.SplitCommand(command);
                return string.Join(" ", new[] { selected }.Concat(words.Skip(1)).Select(Quote));
            }
            catch (InvalidOperationException) { }
        }
        return $"{Quote(selected)} \"%1\"";
    }

    internal static string Quote(string word)
    {
        var result = new System.Text.StringBuilder("\"");
        int slashes = 0;
        foreach (char ch in word)
        {
            if (ch == '\\') { slashes++; continue; }
            result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes).Append(ch); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    internal static string Choose(IReadOnlyList<string> paths)
    {
        using var form = new Form { Text = "Choose an official Roblox player", Width = 850, Height = 330,
            StartPosition = FormStartPosition.CenterScreen, MinimizeBox = false, MaximizeBox = false };
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        list.Items.AddRange(paths.Cast<object>().ToArray());
        var ok = new Button { Text = "Use selected player", AutoSize = true, Enabled = false, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true }; buttons.Controls.AddRange([ok, cancel]);
        list.SelectedIndexChanged += (_, _) => ok.Enabled = list.SelectedIndex >= 0;
        form.Controls.Add(list); form.Controls.Add(buttons);
        form.Controls.Add(new Label { Dock = DockStyle.Top, Height = 45, Text = "Several signed official players were found. Select the installation to use; no candidate is selected automatically." });
        form.AcceptButton = ok; form.CancelButton = cancel;
        if (form.ShowDialog() != DialogResult.OK || list.SelectedItem is not string path) throw new OperationCanceledException();
        return path;
    }
}

internal sealed record ProtocolEdit(string Scheme, string Path, string Name, StoredValue? Before, StoredValue? After);

internal static class RecoveryTransaction
{
    internal static bool Resume(InstallConfig config, Action persist, Func<ProtocolEdit, bool, bool> write)
    {
        if (config.PendingPreviousEnabled is null) return true;
        bool clean = true;
        foreach (var edit in config.PendingChanges.AsEnumerable().Reverse())
            if (!write(edit, true)) clean = false;
        config.BrowserIntegrationEnabled = config.PendingPreviousEnabled.Value;
        config.PendingPreviousEnabled = null; config.PendingChanges.Clear(); config.ExpectedProtocols.Clear();
        persist();
        return clean;
    }

    internal static void Run(InstallConfig config, bool enabled, List<ProtocolEdit> edits, Action persist,
        Func<ProtocolEdit, bool, bool> write, Action prepare, Action verify)
    {
        bool previousEnabled = config.BrowserIntegrationEnabled;
        config.PendingPreviousEnabled = previousEnabled;
        config.BrowserIntegrationEnabled = enabled; config.PendingChanges = edits;
        persist(); // Must succeed before preparation or registry mutation.
        try
        {
            prepare();
            foreach (var edit in edits)
                if (!write(edit, false)) throw new UserError("Another installer changed a launch value. Its change was preserved. Check again before retrying.");
            verify();
            config.PendingPreviousEnabled = null; config.PendingChanges = []; persist();
        }
        catch
        {
            config.PendingPreviousEnabled = previousEnabled; config.PendingChanges = edits;
            if (!Resume(config, persist, write))
                throw new UserError("Another installer changed launch values during recovery. Its values were preserved. Check again before retrying.");
            throw;
        }
    }
}

internal static class ProtocolRecovery
{
    internal const string CommandPath = @"shell\open\command";
    internal static StoredValue String(string name, string value) => new(name, RegistryValueKind.String, JsonSerializer.SerializeToElement(value));
    internal static StoredValue? Value(RegistryTree? tree, string path, string name)
    {
        foreach (string part in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            tree = tree?.Children.FirstOrDefault(p => string.Equals(p.Key, part, StringComparison.OrdinalIgnoreCase)).Value;
        }
        return tree?.Values.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
    }
    internal static string? Command(RegistryTree? tree)
    {
        return CommandValue(Value(tree, CommandPath, ""));
    }
    internal static string? CommandValue(StoredValue? value)
    {
        if (value?.Data.ValueKind != JsonValueKind.String) return null;
        string command = value.Data.GetString()!;
        return value.Kind == RegistryValueKind.ExpandString ? Environment.ExpandEnvironmentVariables(command) : command;
    }
    internal static bool Equal(StoredValue? a, StoredValue? b) => a is null ? b is null : b is not null && a.Kind == b.Kind && a.Data.GetRawText() == b.Data.GetRawText();
    internal static RegistryTree WrapperTree()
    {
        var tree = new RegistryTree { Values = [String("", "URL:Roblox Protocol"), String("URL Protocol", "")] };
        tree.Children["shell"] = new(); tree.Children["shell"].Children["open"] = new();
        tree.Children["shell"].Children["open"].Children["command"] = new() { Values = [String("", Installation.WrapperCommand)] };
        return tree;
    }
    internal static readonly (string Path, string Name)[] OwnedFields = [("", ""), ("", "URL Protocol"), (CommandPath, "")];

    internal static List<ProtocolEdit> Enable(string scheme, RegistryTree? current)
        => OwnedFields.Select(f => new ProtocolEdit(scheme, f.Path, f.Name, Value(current, f.Path, f.Name), Value(WrapperTree(), f.Path, f.Name)))
            .Where(e => !Equal(e.Before, e.After)).ToList();

    internal static List<ProtocolEdit> Restore(string scheme, RegistryTree? current, RegistryTree? backup, RegistryTree owned, string? official)
    {
        var edits = new List<ProtocolEdit>();
        // A changed command transfers the protocol to its new installer. Do not touch its metadata either.
        if (!Equal(Value(current, CommandPath, ""), Value(owned, CommandPath, ""))) return edits;
        foreach (var field in OwnedFields)
        {
            var before = Value(current, field.Path, field.Name);
            if (!Equal(before, Value(owned, field.Path, field.Name))) continue;
            var after = Value(backup, field.Path, field.Name);
            // A newly supported scheme still needs its URL marker when handed to the official player.
            if (field.Path.Length == 0 && field.Name == "URL Protocol" && after is null && official is not null)
                after = Value(owned, field.Path, field.Name);
            if (field.Path == CommandPath && official is not null && !string.Equals(CommandValue(after), official, StringComparison.Ordinal))
                after = String("", official);
            if (!Equal(before, after)) edits.Add(new(scheme, field.Path, field.Name, before, after));
        }
        return edits;
    }

    internal static RegistryTree? Capture(RegistryKey parent, string path)
    {
        using var key = parent.OpenSubKey(path);
        return key is null ? null : RegistryTree.Capture(key);
    }

    internal static bool WriteIfMatches(RegistryKey parent, string root, ProtocolEdit edit, bool rollback = false)
    {
        string path = root + (edit.Path.Length == 0 ? "" : "\\" + edit.Path);
        var expected = rollback ? edit.After : edit.Before;
        var desired = rollback ? edit.Before : edit.After;
        using var existing = parent.OpenSubKey(path, true);
        StoredValue? actual = null;
        if (existing is not null && existing.GetValueNames().Contains(edit.Name, StringComparer.OrdinalIgnoreCase))
            actual = new(edit.Name, existing.GetValueKind(edit.Name), JsonSerializer.SerializeToElement(existing.GetValue(edit.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)));
        // An unapplied edit or an already reverted edit is normal after interruption.
        if (rollback && Equal(actual, desired)) return true;
        if (!Equal(actual, expected)) return false;
        if (desired is null) existing?.DeleteValue(edit.Name, false);
        else
        {
            using var key = parent.CreateSubKey(path);
            new RegistryTree { Values = [desired] }.Restore(key);
        }
        // Empty container keys are harmless. Never delete a tree that another installer can populate.
        return true;
    }
}
