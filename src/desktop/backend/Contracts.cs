using System.Reflection;
using System.Text;
using System.Text.Json;
namespace RobloxMultiInstance;

public sealed record PreviewSourceDto(string Id,string Status,DateTime? Updated,string? Url);
public sealed record PreviewStateDto(bool Supported,PreviewSourceDto[] Sources);

public sealed record AppStateDto(string Version,string HelperVersion,bool HelperInstalled,string SharedNotice, string Readiness = "Unable to verify");
public sealed record InstanceDto(string Id,string Title,int Pid,string? Username,double? CpuPercent,ulong? WorkingSet,DateTime Started,bool HasWindow,bool BypassOptimizations=false,string OptimizationStatus="Unmanaged");
public sealed record InstanceListDto(bool Available,int Unavailable,InstanceDto[] Instances,long Generation=0);
public sealed record OperationItemDto(string Id,string Outcome,string Code,string Message);
public sealed record OperationResultDto(string Outcome,string Code,string Message,OperationItemDto[] Items);
public sealed record NotificationsDto(bool ShowProgress,bool ShowCompletion,bool Available);
public sealed record SettingsDto(string Scheme,bool Available,Dictionary<string,int> Shortcuts,NotificationsDto Notifications,bool Imported);
public sealed record RectDto(int X,int Y,int Width,int Height);
public sealed record MonitorDto(string Id,string Label,RectDto Work,uint Dpi,bool Primary);
public sealed record SlotDto(string Monitor,double X,double Y,double Width,double Height);
public sealed record PresetDto(string Name,SlotDto[] Slots);
public sealed record LayoutStateDto(MonitorDto[] Monitors,PresetDto[] Presets,Dictionary<string,int> Shortcuts);
public sealed record PlacementDto(string Id,int X,int Y,int Width,int Height,int Slot);
public sealed record PlanDto(string PlanId,bool Valid,string? Error,string[] Notes,MonitorDto[] Monitors,PlacementDto[] Placements);
public sealed record CheckDto(string Id,string Name,string State,string Code,string Message,string Action);
public sealed record HealthDto(string Overall,string HelperVersion,bool? BrowserIntegrationEnabled,CheckDto[] Checks,string Readiness="Unable to verify");
public sealed record ActivityRowDto(Guid Id,DateTimeOffset StartedUtc,DateTimeOffset? EndedUtc,long? DurationMs,string Source,string Outcome,string Code,string Message);
public sealed record ActivityDto(bool Available,ActivityRowDto[] Records);
public sealed record UpdateDto(string HelperVersion,bool Available,string? Version,string? Title,string? Notes,string? DownloadProblem,bool CanDownload,bool Downloaded,string? ReleasePage=null);
public sealed record TelemetryDto(bool Enabled,bool Available,bool Configured,int QueuedErrors,string? LastResult);
public sealed record SupportDto(bool Configured,bool Supported,bool ShowBadge,bool Pending,string Message,bool RobloxConfigured,bool ReceiptAvailable=false,int VerifiedTier=-1,long TotalCents=0,bool RecoveryAvailable=false,long TotalRobux=0,int RobloxTier=-1,int UsdTier=-1,RobloxSupportPass[]? RobloxPasses=null);
public sealed record AccountBatchDto(LaunchBatchResultDto Batch,string LayoutStatus);

public sealed record PerformanceProcessDto(string Id,int Pid,DateTime StartedAt,double? CpuPercent,ulong? WorkingSetBytes,ulong? PrivateWorkingSetBytes,ulong? PrivateCommitBytes,double? ReadBytesPerSecond,double? WriteBytesPerSecond,double UptimeSeconds,string Status);
public sealed record PerformanceTotalsDto(int ProcessCount,double? CpuPercent,ulong? WorkingSetBytes,ulong? PrivateWorkingSetBytes,ulong? PrivateCommitBytes,double? ReadBytesPerSecond,double? WriteBytesPerSecond);
public sealed record PerformanceSnapshotDto(long Generation,DateTimeOffset ObservedAt,bool Available,int UnavailableProcesses,PerformanceProcessDto[] Processes,PerformanceTotalsDto Totals);
public sealed record PerformanceProfileDto(string Id,string Name,string Description,string CpuPriority,int? FpsCap,int? GraphicsQuality,string EcoQos="unchanged",string TextureQuality="unchanged",bool MinimumMsaa=false,bool DisableGrass=false,bool ReducedGeometry=false,bool GraySky=false,string WindowBehavior="unchanged");
public sealed record PerformanceCapabilityDto(string Key,string Label,string State,string Reason,bool Applied,string Requested,string Effective,string Source);
public sealed record PerformanceProfilesDto(int Version,bool Available,string SelectedProfileId,PerformanceProfileDto[] Profiles,PerformanceCapabilityDto[] Capabilities,string Revision,bool Enabled=false,bool FocusBoost=true);
public sealed record OptimizationInstanceDto(string AccountId,int Pid,DateTime StartedAt,string State,string Message,string Requested,string Effective,string LaunchState,bool BypassOptimizations=false);
public sealed record OptimizationStateDto(bool Enabled,int Applied,int Bypassed,int Unavailable,int Failed,OptimizationInstanceDto[] Instances,string LaunchCompatibility,bool Available=true,string Error="");
public sealed record PerformanceExportDto(string Json);
public sealed record PerformanceControlsDto(string Id,bool Available,bool OwnsLease,bool RecoveryRequired,string? Priority,bool? EcoQos,bool OwnsPriority,bool OwnsEcoQos,string Code,string Message);
public sealed record PerformanceRecoveryDto(bool Succeeded,int Pending,string Message);
/// <summary>Deterministically generates the page-state TypeScript interfaces from these C# DTOs.</summary>
public static class BackendContractEmitter
{
    public static string TypeScript()
    {
        var text=new StringBuilder("// Generated from src/desktop/backend/Contracts.cs. Do not edit.\n");
        var context=new NullabilityInfoContext();
        foreach(var type in typeof(BackendContractEmitter).Assembly.GetTypes().Where(t=>t.Namespace==typeof(BackendContractEmitter).Namespace&&(t.Name.EndsWith("Dto")||t==typeof(RobloxSupportPass))).OrderBy(t=>t.Name,StringComparer.Ordinal))
        {
            text.Append("export interface ").Append(type.Name).Append(" {\n");
            foreach(var property in type.GetProperties(BindingFlags.Public|BindingFlags.Instance))
            {
                string ts=Map(property.PropertyType);
                if(context.Create(property).ReadState==NullabilityState.Nullable&&!ts.EndsWith(" | null"))ts+=" | null";
                text.Append("  ").Append(JsonNamingPolicy.CamelCase.ConvertName(property.Name)).Append(": ").Append(ts).Append(";\n");
            }
            text.Append("}\n");
        }
        return text.ToString();
    }
    static string Map(Type type)
    {
        if(Nullable.GetUnderlyingType(type) is Type inner)return Map(inner)+" | null";
        if(type.IsArray)return Map(type.GetElementType()!)+"[]";
        if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(Dictionary<,>))return "Record<string, "+Map(type.GenericTypeArguments[1])+">";
        if(type==typeof(string)||type==typeof(Guid)||type==typeof(DateTime)||type==typeof(DateTimeOffset))return "string";
        if(type==typeof(bool))return "boolean";
        if(type.IsPrimitive||type==typeof(decimal))return "number";
        return type.Name;
    }
}
