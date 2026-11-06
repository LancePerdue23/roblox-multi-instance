using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace RobloxLaunchHelper;

internal sealed record LaunchSettingEdit(string Name,string? Original,string Applied);
internal sealed record LaunchSettingsJournal(int Version,string XmlPath,string FlagsPath,bool FlagsExisted,LaunchSettingEdit[] Xml,LaunchSettingEdit[] Flags);

// Launch-scoped settings. Keep the transaction alive until account identity is
// confirmed; do not release it merely because the singleton helper completed.
internal sealed class PerformanceLaunchSettings : IDisposable
{
    readonly string journalPath;
    readonly FileStream lease;
    bool disposed;
    PerformanceLaunchSettings(string journalPath,FileStream lease){this.journalPath=journalPath;this.lease=lease;}
    internal static bool ClientIntegrationVerified => true;
    internal static Dictionary<string,string> Flags(PerformanceProfile profile)
    {
        var flags=new Dictionary<string,string>();
        if(profile.TextureQuality!="unchanged")
        {
            flags["DFFlagTextureQualityOverrideEnabled"]="true";
            flags["DFIntTextureQualityOverride"]=profile.TextureQuality=="lowest"?"0":"1";
        }
        if(profile.MinimumMsaa)flags["FIntDebugForceMSAASamples"]="1";
        if(profile.DisableGrass){flags["FIntFRMMinGrassDistance"]="0";flags["FIntFRMMaxGrassDistance"]="0";}
        if(profile.GraySky)flags["FFlagDebugSkyGray"]="true";
        if(profile.ReducedGeometry)
            foreach(string name in new[]{"DFIntCSGLevelOfDetailSwitchingDistance","DFIntCSGLevelOfDetailSwitchingDistanceL12","DFIntCSGLevelOfDetailSwitchingDistanceL23","DFIntCSGLevelOfDetailSwitchingDistanceL34"})flags[name]="0";
        return flags;
    }
    internal static void ValidatePath(string path)
    {
        if(!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\",StringComparison.Ordinal))throw new InvalidDataException("Local absolute settings paths are required.");
        for(string? next=Path.GetFullPath(path);next is not null;next=Path.GetDirectoryName(next))
            if((File.Exists(next)||Directory.Exists(next))&&(File.GetAttributes(next)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Redirected settings paths are unavailable.");
    }
    static string Read(string path)
    {
        ValidatePath(path);if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Client settings exceed the supported size.");
        return File.ReadAllText(path);
    }
    static void Write(string path,string value)
    {
        ValidatePath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
            using(var writer=new StreamWriter(stream)){writer.Write(value);writer.Flush();stream.Flush(true);}
            ValidatePath(path);File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    static XElement Property(XDocument xml,string name)
    {
        var values=xml.Descendants().Where(x=>(string?)x.Attribute("name")==name).ToArray();
        return values.Length==1?values[0]:throw new InvalidDataException("The current Roblox settings format is unsupported: "+name);
    }
    static XDocument Xml(string text)=>XDocument.Parse(text,LoadOptions.PreserveWhitespace);
    static JsonObject Json(string text)=>JsonNode.Parse(text) as JsonObject??throw new InvalidDataException("Invalid client graphics settings.");
    internal static PerformanceLaunchSettings Begin(string xmlPath,string flagsPath,string journalPath,PerformanceProfile profile,CancellationToken token)
    {
        foreach(string path in new[]{xmlPath,flagsPath,journalPath})ValidatePath(path);
        if(new[]{Path.GetFullPath(xmlPath),Path.GetFullPath(flagsPath),Path.GetFullPath(journalPath)}.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=3)throw new InvalidDataException("Settings and journal paths must be distinct.");
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);ValidatePath(journalPath+".lock");
        // Exclusive OS lease spans staging, player consumption, and restoration, including across processes.
        var lease=new FileStream(journalPath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var transaction=new PerformanceLaunchSettings(journalPath,lease);
        try
        {
            token.ThrowIfCancellationRequested();transaction.Restore(xmlPath,flagsPath);
            var xml=Xml(Read(xmlPath));bool existed=File.Exists(flagsPath);
            var flags=existed?Json(Read(flagsPath)):new JsonObject();
            var xmlEdits=new List<LaunchSettingEdit>();
            void SetXml(string name,int value)
            {
                var property=Property(xml,name);xmlEdits.Add(new(name,property.Value,value.ToString(System.Globalization.CultureInfo.InvariantCulture)));property.Value=xmlEdits[^1].Applied;
            }
            if(profile.FpsCap is int fps)SetXml("FramerateCap",fps);
            if(profile.GraphicsQuality is int quality){SetXml("GraphicsQualityLevel",quality);SetXml("SavedQualityLevel",quality);}
            var flagEdits=new List<LaunchSettingEdit>();
            foreach(var pair in Flags(profile))
            {
                flagEdits.Add(new(pair.Key,flags[pair.Key]?.ToJsonString(),JsonSerializer.Serialize(pair.Value)));
                flags[pair.Key]=pair.Value;
            }
            var journal=new LaunchSettingsJournal(1,Path.GetFullPath(xmlPath),Path.GetFullPath(flagsPath),existed,xmlEdits.ToArray(),flagEdits.ToArray());
            Write(journalPath,JsonSerializer.Serialize(journal));token.ThrowIfCancellationRequested();
            if(xmlEdits.Count>0)Write(xmlPath,xml.ToString(SaveOptions.DisableFormatting));
            if(flagEdits.Count>0)Write(flagsPath,flags.ToJsonString());
            return transaction;
        }
        catch
        {
            try {transaction.Restore(xmlPath,flagsPath);}finally {lease.Dispose();}
            throw;
        }
    }
    internal void Restore(string? expectedXml=null,string? expectedFlags=null)
    {
        if(!File.Exists(journalPath))return;
        var journal=JsonSerializer.Deserialize<LaunchSettingsJournal>(Read(journalPath))??throw new InvalidDataException("Missing launch recovery journal.");
        if(journal.Version!=1||journal.Xml is null||journal.Flags is null||journal.Xml.Length>3||journal.Flags.Length>20)throw new InvalidDataException("Unsupported launch recovery journal.");
        if(expectedXml is not null&&!string.Equals(journal.XmlPath,Path.GetFullPath(expectedXml),StringComparison.OrdinalIgnoreCase)||expectedFlags is not null&&!string.Equals(journal.FlagsPath,Path.GetFullPath(expectedFlags),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Client installation changed; prior settings recovery is required.");
        var xml=Xml(Read(journal.XmlPath));
        foreach(var edit in journal.Xml){var property=Property(xml,edit.Name);if(property.Value==edit.Applied)property.Value=edit.Original!;}
        if(journal.Xml.Length>0)Write(journal.XmlPath,xml.ToString(SaveOptions.DisableFormatting));
        if(journal.Flags.Length>0)
        {
            var flags=File.Exists(journal.FlagsPath)?Json(Read(journal.FlagsPath)):new JsonObject();
            foreach(var edit in journal.Flags)
                if(flags[edit.Name]?.ToJsonString()==edit.Applied)
                {if(edit.Original is null)flags.Remove(edit.Name);else flags[edit.Name]=JsonNode.Parse(edit.Original);}
            if(!journal.FlagsExisted&&flags.Count==0){ValidatePath(journal.FlagsPath);if(File.Exists(journal.FlagsPath))File.Delete(journal.FlagsPath);}
            else Write(journal.FlagsPath,flags.ToJsonString());
        }
        ValidatePath(journalPath);File.Delete(journalPath);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try {Restore();}finally {lease.Dispose();}
    }
}
