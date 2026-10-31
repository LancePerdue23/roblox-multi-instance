using System.Reflection;
using System.Text;
using System.Text.Json;
namespace RobloxMultiInstancePreview;

public sealed record AppStateDto(string PreviewVersion,string HelperVersion,bool HelperInstalled,string SharedNotice);
public sealed record InstanceDto(string Id,string Title,int Pid,string? Username,double? CpuPercent,ulong? WorkingSet,DateTime Started,bool HasWindow);
public sealed record InstanceListDto(bool Available,int Unavailable,InstanceDto[] Instances);
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
public sealed record HealthDto(string Overall,string HelperVersion,bool? BrowserIntegrationEnabled,CheckDto[] Checks);
public sealed record ActivityRowDto(Guid Id,DateTimeOffset StartedUtc,DateTimeOffset? EndedUtc,long? DurationMs,string Source,string Outcome,string Code,string Message);
public sealed record ActivityDto(bool Available,ActivityRowDto[] Records);
public sealed record UpdateDto(string HelperVersion,bool Available,string? Version,string? Title,string? Notes,string? DownloadProblem,bool CanDownload,bool Downloaded);

/// <summary>Deterministically generates the page-state TypeScript interfaces from these C# DTOs.</summary>
public static class BackendContractEmitter
{
    public static string TypeScript()
    {
        var text=new StringBuilder("// Generated from preview/backend/Contracts.cs. Do not edit.\n");
        var context=new NullabilityInfoContext();
        foreach(var type in typeof(BackendContractEmitter).Assembly.GetTypes().Where(t=>t.Namespace==typeof(BackendContractEmitter).Namespace&&t.Name.EndsWith("Dto")).OrderBy(t=>t.Name,StringComparer.Ordinal))
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
