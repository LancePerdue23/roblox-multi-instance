using RobloxMultiInstance;

internal static class ProfileCleanupTests
{
    internal static void Run(string root,Action<bool,string> check)
    {
        foreach(int phase in new[]{1,2})
        foreach(bool directory in new[]{false,true})
        {
            string fixture=Path.Combine(root,$"race-{phase}-{directory}");
            string id=Guid.NewGuid().ToString("N");
            string profile=Path.Combine(fixture,"profiles",id);
            Directory.CreateDirectory(profile);
            string transient=Path.Combine(profile,directory?"transient-directory":"DIPS-wal");
            if(directory)Directory.CreateDirectory(transient);else File.WriteAllText(transient,"transient browser data");
            File.WriteAllText(Path.Combine(profile,"remaining"),"must also be removed");
            int enumerations=0;
            IEnumerable<string> Entries(string path)
            {
                var entries=Directory.EnumerateFileSystemEntries(path).ToArray();
                if(path==profile&&++enumerations==phase)
                {
                    if(directory)Directory.Delete(transient);else File.Delete(transient);
                }
                return entries;
            }
            var store=new AccountStore(fixture,Entries);
            store.RemoveProfile(id);
            check(!Directory.Exists(profile),$"Profile cleanup tolerates a vanished {(directory?"directory":"DIPS-wal")} during {(phase==1?"validation":"deletion")}");
            store.RemoveProfile(id);
            check(!Directory.Exists(profile),"Repeated cleanup of an absent profile succeeds");
        }
        foreach(int phase in new[]{1,2})
        {
            string fixture=Path.Combine(root,$"vanished-subtree-{phase}");
            string id=Guid.NewGuid().ToString("N");
            string profile=Path.Combine(fixture,"profiles",id);
            string transient=Path.Combine(profile,"transient-directory");
            Directory.CreateDirectory(transient);
            int visits=0;
            IEnumerable<string> Entries(string path)
            {
                if(path==transient&&++visits==phase)Directory.Delete(transient);
                return Directory.EnumerateFileSystemEntries(path);
            }
            new AccountStore(fixture,Entries).RemoveProfile(id);
            check(!Directory.Exists(profile),"Cleanup tolerates a subtree disappearing before its entries are read");
        }
        string lockedRoot=Path.Combine(root,"locked");
        string lockedId=Guid.NewGuid().ToString("N");
        string lockedProfile=Path.Combine(lockedRoot,"profiles",lockedId);
        Directory.CreateDirectory(lockedProfile);
        string lockedFile=Path.Combine(lockedProfile,"DIPS-wal");
        using(var held=new FileStream(lockedFile,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
        {
            bool rejected=false;
            try { new AccountStore(lockedRoot).RemoveProfile(lockedId); }
            catch(IOException) { rejected=true; }
            check(rejected&&File.Exists(lockedFile),"Locked profile data still reports cleanup failure");
        }
        new AccountStore(lockedRoot).RemoveProfile(lockedId);
        check(!Directory.Exists(lockedProfile),"Cleanup succeeds after the browser releases a locked file");
    }
}
