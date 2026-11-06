using System;
using System.Linq;
using System.Text.Json;

namespace RobloxMultiInstance.Desktop;

internal sealed record PreviewConfiguration(string[] Ids,int Rate)
{
    public const int MaximumSources=128;
    // Separate from the mapped asset host: WebView2 bypasses resource events for mapped hosts.
    public const string ImageOrigin="https://previews.desktop.local";
    public static PreviewConfiguration Parse(JsonElement payload)
    {
        if(payload.EnumerateObject().Any(p=>p.Name is not ("ids" or "rate")))throw new ArgumentException("Unexpected preview option.");
        int rate=payload.GetProperty("rate").GetInt32();
        if(rate is not (2 or 5 or 15))throw new ArgumentException("Choose 2, 5 or 15 FPS.");
        var ids=payload.GetProperty("ids");
        if(ids.ValueKind!=JsonValueKind.Array || ids.GetArrayLength()>MaximumSources)throw new ArgumentException("Too many preview sources.");
        var values=ids.EnumerateArray().Select(v=>v.GetString()??"").ToArray();
        if(values.Any(id=>id.Length is <1 or >100) || values.Distinct(StringComparer.Ordinal).Count()!=values.Length)throw new ArgumentException("Invalid preview selection.");
        return new(values,rate);
    }

    public static bool CanRead(string uri,string source,string token)
    {
        if(token.Length!=32 || !BridgeProtocol.IsTrusted(source) || !Uri.TryCreate(uri,UriKind.Absolute,out var parsed))return false;
        return parsed.GetLeftPart(UriPartial.Path)==ImageOrigin+"/previews/"+token+".jpg"
            && parsed.Fragment.Length==0 && parsed.Query.StartsWith("?v=",StringComparison.Ordinal)
            && ulong.TryParse(parsed.Query.AsSpan(3),out _);
    }
}
