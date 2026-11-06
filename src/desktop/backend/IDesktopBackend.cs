using System.Text.Json;

namespace RobloxMultiInstance;

// Host boundary shared by the production facade and isolated native audit fixtures.
public interface IDesktopBackend : IDisposable
{
    string AppearanceScheme { get; }
    // Native host only. Never serialize this lease or its handle through the bridge.
    IPreviewWindow? OpenPreviewWindow(string id) => null;
    Task StopBackgroundCleanupAsync() => Task.CompletedTask;
    event Action<string, string, object?>? Event;
    Task<object?> ExecuteOperationAsync(string id,string command,JsonElement payload,CancellationToken token) => ExecuteAsync(command,payload,token);
    Task<object?> ExecuteAsync(string command, JsonElement payload, CancellationToken token);
}

public interface IPreviewWindow : IDisposable
{
    nint Handle { get; }
    bool IsValid { get; }
}
