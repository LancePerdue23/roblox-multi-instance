namespace RobloxLaunchHelper;

// Shared by native host, facade and terminal. Leases remain held until actual work unwinds.
public sealed class OperationCoordinator
{
    readonly object sync = new();
    readonly Dictionary<string,int> active = [];
    public static string Category(string command) => command switch {
        "launch" or "launch.start" => "launch",
        "updates.download" => "download",
        "system.maintenance" or "maintenance.run" or "system.integration" or "integration.set" or "updates.install" or "updates.setup" => "maintenance",
        "instances.list" or "instances.refresh" or "instances.stopLabels" => "discovery",
        "instances.focus" or "instances.close" or "instances.cleanup" or "layouts.apply" or "layouts.preview" or "layouts.savePreset" or "presets.save" => "windows",
        "updates.check" => "download",
        "system.testWorker" or "health.testWorker" => "probe",
        _ when command.StartsWith("settings.") && command is not ("settings.get" or "settings.read") || command is "host.tray" or "host.shortcuts" or "notifications.save" or "shortcuts.save" or "presets.rename" or "presets.delete" or "layouts.renamePreset" or "layouts.deletePreset" or "background.set" or "activity.clear" => "preferences",
        _ => "read"
    };
    public IDisposable Enter(string command)
    {
        string category=Category(command);
        lock(sync)
        {
            bool mutation=category is not ("read" or "discovery");
            if(category is not ("read" or "discovery") && active.GetValueOrDefault(category)>0 ||
               mutation && active.GetValueOrDefault("maintenance")>0 ||
               category=="maintenance" && active.Any(x=>x.Value>0 && x.Key is not ("read" or "discovery")))
                throw new InvalidOperationException("A conflicting operation is still running. Wait for it to finish.");
            active[category]=active.GetValueOrDefault(category)+1;
        }
        return new Lease(()=> { lock(sync) { if(--active[category]==0)active.Remove(category); } });
    }
    sealed class Lease(Action release) : IDisposable { Action? action=release; public void Dispose()=>Interlocked.Exchange(ref action,null)?.Invoke(); }
}
