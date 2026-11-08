using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RobloxMultiInstance.Desktop;

public sealed record BridgeRequest(int Version, string Id, string Command, JsonElement Payload);

public static class BridgeProtocol
{
    public const string Origin = "https://desktop.local";
    public const int MaxMessageLength = 64 * 1024;
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "cancel", "app.state", "launch", "launch.start", "instances.list", "instances.stopLabels", "instances.focus", "instances.close", "instances.stopAll", "instances.cleanup",
        "layouts.get", "layouts.preview", "layouts.apply", "layouts.savePreset", "layouts.renamePreset", "layouts.deletePreset",
        "activity.list", "activity.clear", "system.health", "system.testWorker", "system.integration", "system.maintenance", "system.openLogs",
        "settings.get", "settings.save", "settings.notifications", "settings.reset", "settings.resetNotifications", "settings.import", "shortcuts.save",
        "updates.openPage", "updates.check", "updates.download", "updates.install", "updates.cancel", "background.status", "background.set",
        "host.state", "host.tray", "host.shortcuts", "report.export", "previews.configure", "previews.stop",
        "telemetry.get", "telemetry.set", "telemetry.clear", "telemetry.send",
        "support.badges", "support.importBadge", "support.get", "support.checkRoblox", "support.start", "support.roblox", "support.claim", "support.visibility", "support.remove", "support.import", "support.export", "support.forgetPending",
        "host.ready", "performance.controls", "performance.applyControls", "performance.resetControls", "performance.recoverControls",
        "performance.snapshot", "performance.profiles", "performance.saveProfiles", "performance.previewImport", "performance.import", "performance.export",
        "performance.activate", "performance.optimizationState", "accounts.optimizationBypass", "accounts.organize", "accounts.order",
        "games.recent", "games.remove", "games.clear",
        "accounts.create", "accounts.credentials.open", "accounts.state", "accounts.connect", "accounts.rename", "accounts.remove", "accounts.target", "accounts.launchBatch"
    };
    public static bool IsAllowedCommand(string command) => Commands.Contains(command);
    public static bool IsTrusted(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "desktop.local" && uri.Port == 443 && uri.UserInfo.Length == 0;

    public static BridgeRequest Parse(string source, string json)
    {
        if (!IsTrusted(source)) throw new InvalidOperationException("Untrusted document origin.");
        if (json.Length > MaxMessageLength) throw new ArgumentException("Message too large.");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.EnumerateObject())
            if (!names.Add(item.Name) || item.Name is not ("version" or "id" or "command" or "payload"))
                throw new ArgumentException("Invalid request fields.");
        if (root.GetProperty("version").GetInt32() != 1) throw new ArgumentException("Unsupported bridge version.");
        string id = root.GetProperty("id").GetString() ?? "";
        string command = root.GetProperty("command").GetString() ?? "";
        if (id.Length is < 1 or > 100 || command.Length is < 1 or > 100) throw new ArgumentException("Invalid request identity.");
        JsonElement payload = root.TryGetProperty("payload", out var value) ? value.Clone() : JsonSerializer.SerializeToElement(new { });
        if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("Payload must be an object.");
        return new(1, id, command, payload);
    }
}
