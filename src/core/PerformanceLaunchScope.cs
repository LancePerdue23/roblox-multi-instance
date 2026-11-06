namespace RobloxLaunchHelper;

// All current launch entrypoints share this cross-process lease. A saved-account
// launch retains it through identity verification, beyond singleton confirmation.
internal sealed class PerformanceLaunchScope : IDisposable
{
    readonly FileStream lease;
    readonly string root;
    PerformanceLaunchSettings? settings;
    PerformanceLaunchScope(FileStream lease,string root){this.lease=lease;this.root=root;}
    internal static async Task<PerformanceLaunchScope> EnterAsync(CancellationToken token,string? directory=null)
    {
        string root=directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Program.Product,"performance-launch");
        PerformanceLaunchSettings.ValidatePath(root);
        Directory.CreateDirectory(root);
        string path=Path.Combine(root,"launch.lock");
        while(true)
        {
            token.ThrowIfCancellationRequested();
            PerformanceLaunchSettings.ValidatePath(path);
            try{return new(new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None),root);}
            catch(IOException error) when((error.HResult&0xffff) is 32 or 33){await Task.Delay(100,token);}
        }
    }
    internal void Stage(string player,PerformanceProfile? profile,CancellationToken token)
    {
        string journal=Path.Combine(root,"settings.json");
        string xml=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Roblox","GlobalBasicSettings_13.xml");
        string baseline=Path.Combine(root,"original-fps.txt");
        PerformanceLaunchSettings.ValidatePath(baseline);
        if(profile is null&&!File.Exists(journal)&&!File.Exists(baseline))return;
        int original=OriginalFps(baseline,xml);
        // A client can persist its staged cap on exit, long after restoration.
        // Defaults/bypass must use the preserved preference, not that later write.
        profile??=PerformanceProfileStore.Defaults.Profiles[0];
        if(profile.FpsCap is null)profile=profile with {FpsCap=original};
        string flags=Path.Combine(Path.GetDirectoryName(player)!,"ClientSettings","ClientAppSettings.json");
        settings=PerformanceLaunchSettings.Begin(xml,flags,journal,profile,token);
    }
    internal static int OriginalFps(string path,string xml)
    {
        PerformanceLaunchSettings.ValidatePath(path);PerformanceLaunchSettings.ValidatePath(xml);
        if(!File.Exists(path))
        {
            var document=System.Xml.Linq.XDocument.Load(xml);
            string value=document.Descendants().Single(x=>(string?)x.Attribute("name")=="FramerateCap").Value;
            if(!int.TryParse(value,out int cap)||cap < -1||cap>10000)throw new InvalidDataException("Invalid original FPS preference.");
            using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            using var writer=new StreamWriter(stream);writer.Write(value);writer.Flush();stream.Flush(true);
        }
        if(new FileInfo(path).Length>16||!int.TryParse(File.ReadAllText(path),out int saved)||saved < -1||saved>10000)throw new InvalidDataException("Original FPS preference is unavailable.");
        return saved;
    }
    public void Dispose(){try{settings?.Dispose();}finally{lease.Dispose();}}
}
