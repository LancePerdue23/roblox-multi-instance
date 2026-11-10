namespace RobloxLaunchHelper;

// Shared by native host, facade and terminal. Leases remain held until actual work unwinds.
public sealed class OperationCoordinator
{
    readonly object sync = new();
    readonly Dictionary<string,int> active = [];
    int accountBatches;
    int accountCreation;
    public static string Category(string command) => command switch {
        "launch" or "launch.start" or "accounts.launchBatch" => "launch",
        "performance.activate" or "accounts.optimizationBypass" or "performance.saveProfiles" or "performance.import" or "accounts.organize" or "accounts.order" or "games.remove" or "games.clear" => "preferences",
        "updates.download" or "updates.check" => "download",
        "system.maintenance" or "maintenance.run" or "system.integration" or "integration.set" or "updates.install" or "updates.setup" => "maintenance",
        "instances.list" or "instances.refresh" or "instances.stopLabels" => "discovery",
        "performance.applyControls" or "performance.resetControls" or "performance.recoverControls" or "instances.focus" or "instances.close" or "instances.stopAll" or "instances.cleanup" or "layouts.apply" or "layouts.preview" or "layouts.savePreset" or "presets.save" => "windows",
        "system.testWorker" or "health.testWorker" => "probe",
        _ when command.StartsWith("settings.") && command is not ("settings.get" or "settings.read") || command is "host.tray" or "host.shortcuts" or "notifications.save" or "shortcuts.save" or "presets.rename" or "presets.delete" or "layouts.renamePreset" or "layouts.deletePreset" or "background.set" or "activity.clear" or "telemetry.set" or "telemetry.clear" or "support.start" or "support.claim" or "support.visibility" or "support.remove" or "support.forgetPending" or "support.importBadge" or "support.importBadgeFile" or "support.import" or "support.export" or "support.importFile" or "support.exportFile" or "accounts.create" or "accounts.credentials.open" or "accounts.connect" or "accounts.remove" or "accounts.rename" => "preferences",
        _ => "read"
    };
    public IDisposable Enter(string command)
    {
        string category=Category(command);
        lock(sync)
        {
            bool accountBatch=command=="accounts.launchBatch";
            bool creation=command is "accounts.create" or "accounts.credentials.open";
            bool mutation=category is not ("read" or "discovery");
            if((accountCreation>0 && category is "launch" or "preferences") || (creation && (active.GetValueOrDefault("launch")>0 || active.GetValueOrDefault("preferences")>0)) ||
               category is not ("read" or "discovery") && active.GetValueOrDefault(category)>0 ||
               mutation && active.GetValueOrDefault("maintenance")>0 ||
               accountBatch && active.GetValueOrDefault("windows")>0 ||
               category=="windows" && accountBatches>0 ||
               category=="maintenance" && active.Any(x=>x.Value>0 && x.Key is not ("read" or "discovery")))
                throw new InvalidOperationException("A conflicting operation is still running. Wait for it to finish.");
            active[category]=active.GetValueOrDefault(category)+1;
            if(accountBatch)accountBatches++;if(creation)accountCreation++;
            return new Lease(()=> { lock(sync) { if(accountBatch)accountBatches--;if(creation)accountCreation--;if(--active[category]==0)active.Remove(category); } });
        }
    }
    sealed class Lease(Action release) : IDisposable { Action? action=release; public void Dispose()=>Interlocked.Exchange(ref action,null)?.Invoke(); }
}
