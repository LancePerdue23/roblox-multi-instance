using System.Text.Json;

namespace RobloxMultiInstance;

// Host boundary shared by the production facade and isolated native audit fixtures.
public interface IDesktopBackend : IDisposable
{
    string AppearanceScheme { get; }
    event Action<string, string, object?>? Event;
    Task<object?> ExecuteOperationAsync(string id,string command,JsonElement payload,CancellationToken token) => ExecuteAsync(command,payload,token);
    Task<object?> ExecuteAsync(string command, JsonElement payload, CancellationToken token);
}
