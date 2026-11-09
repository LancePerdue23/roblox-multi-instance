using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace RobloxMultiInstance;
public sealed record SpecialBadgeDto(string Id,string Title,string RobloxUserId);
public sealed record SpecialBadgeStateDto(SpecialBadgeDto[] Badges,string Message="",string? ClaimedBadgeId=null);
public sealed record BadgeGrant(int Schema,string Product,string Badge,string Title,string? RobloxUserId=null,string? Salt=null,string[]? EligibleHashes=null);
public sealed record SavedBadgeGrant(string Receipt,string RobloxUserId);
public sealed class SpecialBadgeService
{
    public const int MaximumReceiptBytes=1024*1024;
    readonly string root,publicKey;
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public SpecialBadgeService(string storageRoot,string? verificationKey=null)
    {
        root=Path.Combine(storageRoot,"recognition");
        using var stream=typeof(SpecialBadgeService).Assembly.GetManifestResourceStream("RobloxMultiInstance.BadgePublicKey");
        publicKey=verificationKey??(stream is null?"":new StreamReader(stream).ReadToEnd().Trim());
    }
    public static string EligibilityHash(string salt,string userId)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("rmi.badge.v2\n"+salt+"\n"+userId)));
    static bool Hex(string? s,int length)=>s?.Length==length&&s.All(char.IsAsciiHexDigit);
    public bool Eligible(BadgeGrant grant,string userId)=>ValidId(userId)&&(grant.Schema==1?grant.RobloxUserId==userId:grant.EligibleHashes!.Contains(EligibilityHash(grant.Salt!,userId),StringComparer.OrdinalIgnoreCase));
    public BadgeGrant Verify(string receipt)
    {
        try {
            if(receipt.Length>MaximumReceiptBytes)throw new InvalidDataException();
            var parts=receipt.Split('.');if(parts.Length!=2)throw new InvalidDataException();
            var payload=Convert.FromBase64String(parts[0]);var signature=Convert.FromBase64String(parts[1]);
            using var key=ECDsa.Create();var spki=Convert.FromBase64String(publicKey);key.ImportSubjectPublicKeyInfo(spki,out int read);
            if(read!=spki.Length||key.KeySize!=256||key.ExportParameters(false).Curve.Oid.Value!="1.2.840.10045.3.1.7"||signature.Length!=64||!key.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))throw new InvalidDataException();
            var g=JsonSerializer.Deserialize<BadgeGrant>(payload,Json)??throw new InvalidDataException();
            if(g.Product!="roblox-multi-instance"||g.Badge is not {Length:>0 and <=48}||g.Badge.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')||g.Title is not {Length:>0 and <=48}||g.Title.Any(char.IsControl))throw new InvalidDataException();
            if(g.Schema==1){if(!ValidId(g.RobloxUserId)||g.Salt is not null||g.EligibleHashes is not null)throw new InvalidDataException();}
            else if(g.Schema==2){if(g.RobloxUserId is not null||!Hex(g.Salt,64)||g.EligibleHashes is not {Length:>0 and <=10000}||g.EligibleHashes.Any(h=>!Hex(h,64))||g.EligibleHashes.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=g.EligibleHashes.Length)throw new InvalidDataException();}
            else throw new InvalidDataException();
            return g;
        } catch {throw new InvalidDataException("This recognition file is invalid or was not issued by the developer.");}
    }
    static bool ValidId(string? value)=>value is {Length:>0 and <=19}&&value[0]!='0'&&value.All(char.IsAsciiDigit)&&long.TryParse(value,out long id)&&id>0;
    public string ReadImport(string path)
    {
        if(!Path.IsPathFullyQualified(path)||!string.Equals(Path.GetExtension(path),".rmibadge",StringComparison.OrdinalIgnoreCase)||new FileInfo(path).LinkTarget is not null)throw new ArgumentException("Choose an original .rmibadge file.");
        using var input=File.OpenRead(path);if(input.Length is <1 or >MaximumReceiptBytes)throw new InvalidDataException("Recognition file is too large or empty.");
        var bytes=new byte[(int)input.Length];input.ReadExactly(bytes);return Encoding.UTF8.GetString(bytes).Trim();
    }
    public string Import(string path,string receipt,string verifiedRobloxUserId)
    {
        var grant=Verify(receipt);if(!Eligible(grant,verifiedRobloxUserId))throw new InvalidOperationException("This Roblox account is not eligible for this recognition file.");
        Directory.CreateDirectory(root);
        var destination=Path.Combine(root,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(verifiedRobloxUserId+":"+grant.Badge)))+".bin");
        bool already=State([verifiedRobloxUserId]).Badges.Any(b=>b.Id==grant.Badge);
        var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        var saved=new SavedBadgeGrant(receipt,verifiedRobloxUserId);
        try {
            File.WriteAllBytes(temporary,ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(saved,Json),null,DataProtectionScope.CurrentUser));
            File.Move(temporary,destination,true);
            var restored=JsonSerializer.Deserialize<SavedBadgeGrant>(ProtectedData.Unprotect(File.ReadAllBytes(destination),null,DataProtectionScope.CurrentUser),Json);
            if(restored!=saved||!Eligible(Verify(restored.Receipt),restored.RobloxUserId))throw new IOException("Recognition could not be saved.");
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
        var prefix=already?"Recognition already claimed.":"Recognition saved.";
        try {if(ReadImport(path)!=receipt)return prefix+" The original changed and was left untouched.";File.Delete(path);return prefix+" The imported file has been deleted.";}
        catch {return prefix+" The original file could not be deleted; you can remove it manually.";}
    }
    public SpecialBadgeStateDto State(IEnumerable<string> connectedIds)
    {
        var ids=connectedIds.ToHashSet(StringComparer.Ordinal);var badges=new List<SpecialBadgeDto>();if(!Directory.Exists(root))return new([]);
        try {foreach(var path in Directory.EnumerateFiles(root,"*.bin").Take(256))try {
            if(new FileInfo(path).Length>2*MaximumReceiptBytes)continue;
            var data=Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));
            string receipt,user;
            if(data.StartsWith('{')){var saved=JsonSerializer.Deserialize<SavedBadgeGrant>(data,Json)??throw new InvalidDataException();receipt=saved.Receipt;user=saved.RobloxUserId;}
            else {receipt=data;user=Verify(receipt).RobloxUserId??"";}
            var g=Verify(receipt);if(ids.Contains(user)&&Eligible(g,user))badges.Add(new(g.Badge,g.Title,user));
        }catch { }}catch { }
        return new(badges.Distinct().OrderBy(b=>b.Title).ToArray());
    }
}
