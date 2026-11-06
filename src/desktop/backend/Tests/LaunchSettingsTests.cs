using System.Xml.Linq;
using System.Text.Json.Nodes;
using RobloxLaunchHelper;

internal static class LaunchSettingsTests
{
    internal static int Run(string root)
    {
        int passed=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        string directory=Path.Combine(root,"launch-settings-fixture");Directory.CreateDirectory(directory);
        string serialized=Path.Combine(directory,"launch-gate");
        using(var first=PerformanceLaunchScope.EnterAsync(default,serialized).GetAwaiter().GetResult())
        {
            using var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            try {using var second=PerformanceLaunchScope.EnterAsync(cancellation.Token,serialized).GetAwaiter().GetResult();throw new Exception("Concurrent launch accepted");}
            catch(OperationCanceledException){Check(true,"A second launch waits for settings restoration and can be canceled without starting a player");}
        }
        using(PerformanceLaunchScope.EnterAsync(default,serialized).GetAwaiter().GetResult())
            Check(!File.Exists(Path.Combine(serialized,"settings.json")),"The next launch acquires the released lease without staging any unrequested settings");
        string xml=Path.Combine(directory,"settings.xml"),flags=Path.Combine(directory,"flags.json"),journal=Path.Combine(directory,"journal.json");
        string baseline=Path.Combine(directory,"original-fps.txt");
        File.WriteAllText(xml,"<roblox><int name=\"FramerateCap\">-1</int></roblox>");
        Check(PerformanceLaunchScope.OriginalFps(baseline,xml)==-1,"Original FPS preference is captured before optimization");
        File.WriteAllText(xml,"<roblox><int name=\"FramerateCap\">10</int></roblox>");
        Check(PerformanceLaunchScope.OriginalFps(baseline,xml)==-1,"Late optimized-client shutdown writes cannot replace the bypass FPS preference");
        const string original="<roblox><Item><Properties><int name=\"FramerateCap\">60</int><int name=\"GraphicsQualityLevel\">8</int><token name=\"SavedQualityLevel\">8</token><string name=\"Unrelated\">preserve</string></Properties></Item></roblox>";
        var maximum=PerformanceProfileStore.Defaults.Profiles.Single(x=>x.Id=="many-accounts");
        File.WriteAllText(xml,original);File.WriteAllText(flags,"{\"OtherTool\":123,\"DFIntTextureQualityOverride\":\"3\"}");
        using(var transaction=PerformanceLaunchSettings.Begin(xml,flags,journal,maximum,default))
        {
            Check(File.ReadAllText(xml).Contains(">10<")&&JsonNode.Parse(File.ReadAllText(flags))!["DFIntTextureQualityOverride"]!.GetValue<string>()=="0","Launch transaction stages FPS, graphics, and allowlisted visual flags");
            Check(File.Exists(journal),"Launch recovery intent is durable before settings remain staged");
            try {using var competing=PerformanceLaunchSettings.Begin(xml,flags,journal,maximum,default);throw new Exception("Concurrent transaction accepted");}catch(IOException){Check(true,"Overlapping settings-sensitive launches cannot mutate shared settings");}
            var changed=XDocument.Load(xml);changed.Descendants().Single(x=>(string?)x.Attribute("name")=="FramerateCap").Value="75";changed.Save(xml);
            var graphics=JsonNode.Parse(File.ReadAllText(flags))!;graphics["FFlagDebugSkyGray"]="external";graphics["AddedElsewhere"]=true;File.WriteAllText(flags,graphics.ToJsonString());
        }
        Check(File.ReadAllText(xml).Contains(">75<")&&File.ReadAllText(xml).Contains(">8<")&&File.ReadAllText(xml).Contains("preserve"),"Launch restoration preserves external XML edits and unrelated properties");
        var restored=JsonNode.Parse(File.ReadAllText(flags))!;
        Check(restored["DFIntTextureQualityOverride"]!.GetValue<string>()=="3"&&restored["OtherTool"]!.GetValue<int>()==123&&restored["FFlagDebugSkyGray"]!.GetValue<string>()=="external"&&restored["AddedElsewhere"]!.GetValue<bool>(),"Launch restoration merges original owned flags without overwriting another tool");
        Check(!File.Exists(journal),"Successful restoration removes the launch journal");
        File.Delete(flags);File.WriteAllText(xml,original);
        using(PerformanceLaunchSettings.Begin(xml,flags,journal,maximum,default)){}
        Check(!File.Exists(flags),"An originally absent graphics file is removed after owned entries restore");
        File.WriteAllText(xml,"<roblox/>");
        try {using var ignored=PerformanceLaunchSettings.Begin(xml,flags,journal,maximum,default);throw new Exception("Malformed settings accepted");}catch(InvalidDataException){Check(!File.Exists(flags),"Unsupported XML is refused before any client setting changes");}
        File.WriteAllText(xml,original);File.WriteAllText(flags,"not-json");
        try {using var ignored=PerformanceLaunchSettings.Begin(xml,flags,journal,maximum,default);throw new Exception("Invalid JSON accepted");}catch(System.Text.Json.JsonException){Check(File.ReadAllText(xml)==original,"Malformed graphics configuration leaves XML untouched");}
        File.Delete(flags);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        try {using var ignored=PerformanceLaunchSettings.Begin(xml,flags,journal,maximum,cancel.Token);throw new Exception("Canceled staging accepted");}catch(OperationCanceledException){Check(!File.Exists(flags)&&File.ReadAllText(xml)==original,"Canceled launch staging leaves client preferences untouched");}
        // Simulate a process dying after a durable intent and partial mutation.
        File.WriteAllText(xml,original.Replace(">60<",">10<"));
        File.WriteAllText(journal,System.Text.Json.JsonSerializer.Serialize(new LaunchSettingsJournal(1,xml,flags,false,[new("FramerateCap","60","10")],[])));
        using(PerformanceLaunchSettings.Begin(xml,flags,journal,PerformanceProfileStore.Defaults.Profiles[0],default)){}
        Check(File.ReadAllText(xml).Contains(">60<"),"Next transaction recovers interrupted launch settings before staging another client");
        File.WriteAllText(journal,System.Text.Json.JsonSerializer.Serialize(new LaunchSettingsJournal(1,xml+"-old",flags,false,[],[])));
        try {using var ignored=PerformanceLaunchSettings.Begin(xml,flags,journal,maximum,default);throw new Exception("Updated installation accepted");}catch(InvalidDataException){Check(File.Exists(journal),"Installation changes preserve unresolved recovery journals");}
        return passed;
    }
}
