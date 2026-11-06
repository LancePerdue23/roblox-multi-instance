using System.Security.AccessControl;
using System.Security.Principal;

namespace RobloxMultiInstance;

/// <summary>Native-only storage guard. Never follow local profile redirections.</summary>
public static class AccountStorageSecurity
{
    public static void ValidatePath(string path)
    {
        string full=Path.GetFullPath(path);
        if(full.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Account storage must be on a local drive.");
        for(string? current=full;current is not null;current=Path.GetDirectoryName(current))
        {
            try
            {
                if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Account storage contains a redirected file or directory. The operation was stopped.");
            }
            catch(FileNotFoundException) { }
            catch(DirectoryNotFoundException) { }
        }
    }
    public static void EnsurePrivateDirectory(string path)
    {
        ValidatePath(path);Directory.CreateDirectory(path);ValidatePath(path);
        using var identity=WindowsIdentity.GetCurrent();var sid=identity.User??throw new IOException("The Windows account could not be identified.");
        var security=new DirectorySecurity();security.SetOwner(sid);security.SetAccessRuleProtection(true,false);
        foreach(var allowed in Allowed(sid))
            security.AddAccessRule(new FileSystemAccessRule(allowed,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(path),security);
    }
    public static void EnsurePrivateFile(string path)
    {
        ValidatePath(path);
        using var identity=WindowsIdentity.GetCurrent();var sid=identity.User??throw new IOException("The Windows account could not be identified.");
        var security=new FileSecurity();security.SetOwner(sid);security.SetAccessRuleProtection(true,false);
        foreach(var allowed in Allowed(sid))security.AddAccessRule(new FileSystemAccessRule(allowed,FileSystemRights.FullControl,AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(new FileInfo(path),security);
    }
    static SecurityIdentifier[] Allowed(SecurityIdentifier owner) =>
        [owner,new(WellKnownSidType.LocalSystemSid,null),new(WellKnownSidType.BuiltinAdministratorsSid,null)];
}
