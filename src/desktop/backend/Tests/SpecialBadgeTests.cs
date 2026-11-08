using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RobloxMultiInstance;

internal static class SpecialBadgeTests
{
    internal static void Run(string root,Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        using var issuer=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pub=Convert.ToBase64String(issuer.ExportSubjectPublicKeyInfo());
        var service=new SpecialBadgeService(Path.Combine(root,"recognition-fixture"),pub);
        string Receipt(string user="123") {
            var payload=JsonSerializer.SerializeToUtf8Bytes(new BadgeGrant(1,"roblox-multi-instance","alpha-tester","Alpha Tester",user),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Convert.ToBase64String(payload)+"."+Convert.ToBase64String(issuer.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }
        var path=Path.Combine(root,"delivery.rmibadge");var receipt=Receipt();File.WriteAllText(path,receipt);
        check(service.State(["123"]).Badges.Length==0,"No special role exists before grant import");
        try {service.Import(path,receipt,"456");throw new Exception("Wrong identity accepted");}catch(InvalidOperationException){check(File.Exists(path),"Wrong-account import retains original and grants nothing");}
        var parts=receipt.Split('.');var forged=Convert.ToBase64String(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])).Replace("123","456")))+"."+parts[1];
        try {service.Verify(forged);throw new Exception("Forged grant accepted");}catch(InvalidDataException){check(true,"Edited recipient invalidates grant signature");}
        using var attacker=ECDsa.Create(ECCurve.NamedCurves.nistP256);var payload=Convert.FromBase64String(parts[0]);
        try {service.Verify(parts[0]+"."+Convert.ToBase64String(attacker.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));throw new Exception("Foreign issuer accepted");}catch(InvalidDataException){check(true,"Self-issued grants cannot unlock recognition");}
        check(service.ReadImport(path)==receipt,"Bounded native file read preserves valid delivery");
        service.Import(path,receipt,"123");
        check(!File.Exists(path),"Successful import deletes delivery only after verified durable save");
        var restarted=new SpecialBadgeService(Path.Combine(root,"recognition-fixture"),pub);
        check(restarted.State(["123"]).Badges.Single().Id=="alpha-tester","Account-bound badge survives restart offline");
        check(restarted.State(["456"]).Badges.Length==0&&restarted.State([]).Badges.Length==0,"Other accounts and removed accounts cannot display badge");
        File.WriteAllText(path,receipt);File.SetAttributes(path,FileAttributes.ReadOnly);
        try {check(service.Import(path,receipt,"123").Contains("manually")&&File.Exists(path),"Failed source deletion reports retained file without losing grant");}finally{File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}
        check(service.State(["123"]).Badges.Length==1,"Repeated imports replace the grant without duplicates");
        var binary=Directory.GetFiles(Path.Combine(root,"recognition-fixture","recognition"),"*.bin").Single();
        File.WriteAllBytes(binary,[1,2,3]);check(service.State(["123"]).Badges.Length==0,"Corrupt protected grant fails closed");
        File.WriteAllText(path,new string('a',SpecialBadgeService.MaximumReceiptBytes+1));try{service.ReadImport(path);throw new Exception("Oversized grant accepted");}catch(InvalidDataException){check(File.Exists(path),"Oversized import rejected without consuming original");}

        string salt=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string Shared(BadgeGrant grant){var bytes=JsonSerializer.SerializeToUtf8Bytes(grant,new JsonSerializerOptions(JsonSerializerDefaults.Web));return Convert.ToBase64String(bytes)+"."+Convert.ToBase64String(issuer.SignData(bytes,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));}
        var sharedGrant=new BadgeGrant(2,"roblox-multi-instance","alpha-tester","Alpha Tester",null,salt,[SpecialBadgeService.EligibilityHash(salt,"123"),SpecialBadgeService.EligibilityHash(salt,"456")]);
        string shared=Shared(sharedGrant);var sharedService=new SpecialBadgeService(Path.Combine(root,"shared"),pub);
        check(sharedService.Eligible(sharedService.Verify(shared),"123")&&sharedService.Eligible(sharedService.Verify(shared),"456")&&!sharedService.Eligible(sharedService.Verify(shared),"789"),"One shared signed grant accepts only hashed eligible accounts");
        foreach(var invalid in new[]{sharedGrant with {Salt="bad"},sharedGrant with {EligibleHashes=[]},sharedGrant with {EligibleHashes=["bad"]},sharedGrant with {RobloxUserId="123"},sharedGrant with {EligibleHashes=[..sharedGrant.EligibleHashes!,sharedGrant.EligibleHashes![0]]}}){try{sharedService.Verify(Shared(invalid));throw new Exception("Malformed shared grant accepted");}catch(InvalidDataException){check(true,"Malformed signed shared grant rejected");}}
        File.WriteAllText(path,shared);
        try{sharedService.Import(path,shared,"789");throw new Exception("Ineligible shared recipient accepted");}catch(InvalidOperationException){check(File.Exists(path)&&sharedService.State(["789"]).Badges.Length==0,"Ineligible shared claim retains delivery and grants nothing");}
        sharedService.Import(path,shared,"123");check(!File.Exists(path),"Shared delivery consumed only after protected save");
        check(new SpecialBadgeService(Path.Combine(root,"shared"),pub).State(["123"]).Badges.Single().RobloxUserId=="123"&&sharedService.State(["456"]).Badges.Length==0,"Shared list cannot grant unclaimed accounts after restart");
        File.WriteAllText(path,shared);check(sharedService.Import(path,shared,"123").StartsWith("Recognition already claimed.")&&!File.Exists(path),"Repeated shared claim is idempotent and consumes delivery");
        File.WriteAllText(path,shared);sharedService.Import(path,shared,"456");check(sharedService.State(["123","456"]).Badges.Length==2,"Same shared file can independently recognize another verified tester");
        var manager=new AccountManager(Path.Combine(root,"claim-accounts"));
        string Claim(string? id)=>manager.ClaimRecognitionAsync(user=>sharedService.Eligible(sharedGrant,user),(request,token)=>Task.FromResult(id is null?null:new AccountIdentity(id,"FixtureTester")),CancellationToken.None).GetAwaiter().GetResult();
        try{Claim(null);throw new Exception("Canceled claim accepted");}catch(OperationCanceledException){check(manager.State().Accounts.Length==0,"Canceled first-time claim saves no account");}
        try{Claim("789");throw new Exception("Ineligible claim saved account");}catch(InvalidOperationException){check(manager.State().Accounts.Length==0,"Ineligible sign-in saves no account");}
        check(Claim("123")=="123"&&manager.State().Accounts.Length==1,"Eligible fresh sign-in connects account without prior manual setup");
        check(Claim("123")=="123"&&manager.State().Accounts.Length==1,"Previously connected tester can claim without duplicate account");
    }
}
