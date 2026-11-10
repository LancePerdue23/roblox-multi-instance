using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RobloxMultiInstance;

string root=Path.Combine(Environment.CurrentDirectory,".build","RmiSupportTests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);int passed=0;
void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
void Reject(Action action,string message){try{action();throw new Exception("Accepted: "+message);}catch(InvalidDataException){Check(true,message);}}
string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
string MakeReceipt(string product="roblox-multi-instance",string keyId="test"){
    byte[] payload=JsonSerializer.SerializeToUtf8Bytes(new{product,badge="supporter",receiptId=new string('A',32),version=1,keyId});
    return Encode(payload)+"."+Encode(key.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}
var config=new SupportConfiguration("https://support.invalid/",new(){{"test",Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}});
string MakeWallet(string id,int tier,long total,long revision=1,string currency="usd"){
 byte[] payload=JsonSerializer.SerializeToUtf8Bytes(new{product="roblox-multi-instance",badge="supporter",receiptId=id,version=2,keyId="test",supporterId=id,tier,totalCents=total,revision,currency});
 return Encode(payload)+"."+Encode(key.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}
var fake=new FakeHandler{Receipt=MakeReceipt(),SignWallet=id=>{byte[] payload=JsonSerializer.SerializeToUtf8Bytes(new{product="roblox-multi-instance",badge="supporter",receiptId=id,version=2,keyId="test",supporterId=id,tier=0,totalCents=500,revision=1,currency="usd"});return Encode(payload)+"."+Encode(key.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));}};using var http=new HttpClient(fake);
try {
    int beforeRoblox=fake.Calls;
    using(var roblox=new SupportService(Path.Combine(root,"roblox"),http,new("",new(),"123456789"))){
        Check(roblox.State().RobloxConfigured&&!roblox.State().Configured,"Gamepass donations work independently of Stripe configuration");
        Check(roblox.RobloxDonationUri().AbsoluteUri=="https://www.roblox.com/game-pass/123456789","Gamepass opens only the canonical Roblox browser URL");
        Check(!roblox.State().Supported&&!roblox.State().Pending,"Opening a gamepass never grants a receipt or creates a payment claim");
    }
    foreach(string? id in new string?[]{null,"","0","01","-1","1.5","123/other","123?redirect=bad","https://attacker.invalid/"," 123","１２３","9223372036854775808"}){
        using var invalidPass=new SupportService(root,http,new(config.Endpoint,config.PublicKeys,id));
        Check(!invalidPass.State().RobloxConfigured&&invalidPass.State().Configured,"Invalid or missing gamepass ID leaves Stripe available");
        try{invalidPass.RobloxDonationUri();throw new Exception("Invalid gamepass accepted");}catch(InvalidOperationException){Check(true,"Invalid gamepass cannot open a browser URL");}
    }
    Check(fake.Calls==beforeRoblox,"Gamepass configuration and URL checks make no network requests");
    using(var disabled=new SupportService(Path.Combine(root,"disabled"),http,new("",new()))){Check(!disabled.State().Configured&&!disabled.State().Supported,"Unconfigured support is disabled");try{await disabled.StartAsync();throw new Exception("Unconfigured started");}catch(InvalidOperationException){Check(fake.Calls==0,"Unconfigured builds make no network requests");}}
    using(var service=new SupportService(root,http,config)){
        Check(service.State().Configured&&!service.State().Pending,"Configured startup is local and creates no files");Check(!Directory.EnumerateFiles(root).Any(),"Read-only startup does not write settings");
        Reject(()=>service.VerifyReceipt(MakeReceipt("other-product")),"Wrong-product receipts rejected");
        Reject(()=>service.VerifyReceipt(MakeReceipt(keyId:"unknown")),"Unknown signing key rejected");
        Reject(()=>service.VerifyReceipt(fake.Receipt[..^10]+"AAAAAAAAAA"),"Tampered signature rejected");
        Uri checkout=await service.StartAsync();Check(checkout.Host=="checkout.stripe.com"&&service.State().Pending,"Checkout saves a recoverable pending claim");
        int starts=fake.Calls;Check(await service.StartAsync()==checkout&&fake.Calls==starts,"Repeated support action resumes checkout without losing its claim secret");
        await service.ClaimAsync();Check(service.State().Pending&&!service.State().Supported,"Unpaid webhook state remains pending");
        byte[] protectedBytes=File.ReadAllBytes(Path.Combine(root,"support-pending.bin"));Check(!Encoding.UTF8.GetString(protectedBytes).Contains(fake.Secret!),"Pending secret is encrypted at rest");
    }
    using(var restarted=new SupportService(root,http,config)){
        Check(restarted.State().Pending,"Pending claim survives restart");fake.Paid=true;
        Check((await restarted.ClaimAsync()).Supported&&!restarted.State().Pending,"Verified paid claim becomes offline receipt");
        int requests=fake.Calls;Check(restarted.State().Supported&&fake.Calls==requests,"Badge display makes no network requests");
        Check(!restarted.SetBadgeVisible(false).ShowBadge&&restarted.State().Supported,"Hiding badge retains verified supporter status");
        string export=Path.Combine(root,"backup.rmisupport");restarted.ExportReceipt(export);
        Check(File.ReadAllText(export)==fake.Receipt,"Explicit export is portable signed receipt only");
        File.WriteAllText(export,"replace-me");restarted.ExportReceipt(export);Check(File.ReadAllText(export)==fake.Receipt,"Native-selected receipt export can atomically overwrite confirmed file");
        restarted.RemoveReceipt();Check(!restarted.State().Supported,"Local receipt can be removed");
        Check(restarted.ImportReceipt(export).Supported&&!restarted.State().ShowBadge,"Import restores receipt while respecting hidden preference");
        File.WriteAllText(Path.Combine(root,"bad.rmisupport"),"bad");Reject(()=>restarted.ImportReceipt(Path.Combine(root,"bad.rmisupport")),"Invalid import cannot replace receipt");Check(restarted.State().Supported,"Existing receipt survives invalid import");
    }
    using(var invalidKeys=new SupportService(root,http,new(config.Endpoint,new(){{"bad","not-spki"}}))){Check(!invalidKeys.State().Configured,"Malformed trusted public keys disable payments");}
    fake.BadUrl=true;using(var service=new SupportService(root,http,config)){try{await service.StartAsync();throw new Exception("Accepted URL");}catch(InvalidDataException){Check(true,"Non-Stripe checkout URL rejected");}}
    fake.BadUrl=false;fake.Oversized=true;using(var service=new SupportService(root,http,config)){try{await service.StartAsync();throw new Exception("Accepted response");}catch(InvalidDataException){Check(true,"Oversized service responses rejected");}}
    fake.Oversized=false;fake.DropStartOnce=true;using(var service=new SupportService(Path.Combine(root,"retry"),http,config)){
        try{await service.StartAsync();throw new Exception("Expected dropped response");}catch(HttpRequestException){Check(service.State().Pending,"Failed start retains protected claim secret for recovery");}
        string firstHash=fake.LastClaimHash!;await service.StartAsync();Check(firstHash==fake.LastClaimHash,"Lost start response retries same claim hash");
        service.RemoveReceipt();Check(service.State().Pending,"Removing a badge receipt preserves a pending payment claim");
        Check(!service.ClearPending().Pending,"Explicit forgetting clears pending state");
    }
    var passHandler=new PassHandler();using var passHttp=new HttpClient(passHandler);
    string passRoot=Path.Combine(root,"pass-verification");
    var passConfig=new SupportConfiguration("",new(),null,["123","456"]);
    using(var service=new SupportService(passRoot,passHttp,passConfig)){
        Check(!(await service.CheckRobloxAsync(["11"],true)).Supported,"Non-owner never receives supporter recognition");
        Check((await service.CheckRobloxAsync(["11","22"],true)).Supported,"Any connected account owning any configured pass qualifies the whole app");
        Check(!service.State().ReceiptAvailable,"Gamepass recognition never enables Stripe receipt export");
        Check(!service.SetBadgeVisible(false).ShowBadge&&service.State().Supported,"Gamepass recognition respects badge visibility");
    }
    passHandler.Unavailable=true;
    using(var service=new SupportService(passRoot,passHttp,passConfig)){
        Check(service.State().Supported,"Gamepass recognition survives restart offline");
        Check((await service.CheckRobloxAsync(["22"],true)).Supported,"API failure cannot revoke verified recognition");
        Check((await service.CheckRobloxAsync([],true)).Supported,"Removing all accounts retains recognition");
    }
    using(var service=new SupportService(Path.Combine(root,"pass-failure"),passHttp,passConfig))
        Check(!(await service.CheckRobloxAsync(["22"],true)).Supported,"API failure never grants recognition");
    int passCalls=passHandler.Calls;
    using(var service=new SupportService(root,passHttp,new("",new())))await service.CheckRobloxAsync(["22"],true);
    Check(passHandler.Calls==passCalls,"Blank pass configuration makes no ownership requests");
    using(var cumulative=new SupportService(Path.Combine(root,"cumulative"),http,config)){
        fake.BadUrl=false;fake.Oversized=false;fake.Paid=true;
        string code=cumulative.RecoveryCode();string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
        Check(code.Length==43&&cumulative.RecoveryCode()==code,"Recovery credential is stable and generated once");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"cumulative","support-wallet.bin"))).Contains(code),"Recovery credential is protected at rest");
        fake.Receipt=MakeWallet(id,1,700,2);var state=await cumulative.RefreshAsync();Check(state.VerifiedTier==1&&state.TotalCents==700,"Signed cumulative total and tier drive native state");
        fake.Receipt=MakeWallet(id,1,200,3);state=await cumulative.RefreshAsync();Check(state.VerifiedTier==1&&state.TotalCents==200,"Refund total keeps earned cosmetics");
        fake.Receipt=MakeWallet(id,0,500,1);try{await cumulative.RefreshAsync();throw new Exception("Accepted stale receipt");}catch(InvalidDataException){Check(cumulative.State().TotalCents==200,"Stale receipt cannot roll back the verified wallet");}
        fake.Receipt=MakeWallet(new string('b',64),2,1500,4);try{await cumulative.RefreshAsync();throw new Exception("Accepted foreign wallet");}catch(InvalidDataException){Check(cumulative.State().VerifiedTier==1,"Valid signature alone cannot replace claim ownership");}
        Reject(()=>cumulative.VerifyReceipt(MakeWallet(id,0,700)),"Signed inconsistent cumulative tier rejected");
        Reject(()=>cumulative.VerifyReceipt(MakeWallet(id,2,-1)),"Signed negative cumulative total rejected");
        Reject(()=>cumulative.VerifyReceipt(MakeWallet(id,1,700,currency:"eur")),"Signed foreign currency rejected");
        fake.Receipt=MakeWallet(id,1,200,3);
        using var otherDevice=new SupportService(Path.Combine(root,"recovered"),http,config);state=await otherDevice.RefreshAsync(code);Check(state.VerifiedTier==1&&otherDevice.RecoveryCode()==code,"Private code recovers verified tier and future payment identity on another device");
        fake.SignWallet=wallet=>MakeWallet(new string('b',64),2,1500);await otherDevice.StartAsync(500);try{await otherDevice.ClaimAsync();throw new Exception("Foreign claim accepted");}catch(InvalidDataException){Check(otherDevice.State().Pending&&otherDevice.State().VerifiedTier==1,"Wrong-wallet payment receipt preserves pending credentials and prior entitlement");}
        string legacy=Path.Combine(root,"legacy.rmisupport");File.WriteAllText(legacy,MakeReceipt());cumulative.ImportReceipt(legacy);fake.Receipt=MakeWallet(id,-1,100,4);state=await cumulative.RefreshAsync();Check(state.Supported&&state.VerifiedTier==0&&state.TotalCents==100,"Starting cumulative credit cannot erase legacy base cosmetics");
        cumulative.VerifyReceipt(MakeReceipt());Check(true,"Version-one receipts remain valid base recognition without invented amounts");
    }
    await RobuxChecks.Run(root,Check,config,MakeWallet(new string('c',64),2,1500));
    Console.WriteLine($"{passed} support checks passed.");
} finally {Directory.Delete(root,true);}

sealed class FakeHandler:HttpMessageHandler {
    public Func<string,string>? SignWallet;public string Receipt="";public string? Secret,LastClaimHash,WalletSecret;public bool Paid,BadUrl,Oversized,DropStartOnce;public int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
        Calls++;using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        if(request.RequestUri!.AbsolutePath=="/support/start-v2"){WalletSecret=body.RootElement.GetProperty("walletSecret").GetString();LastClaimHash=body.RootElement.GetProperty("claimHash").GetString();if(DropStartOnce){DropStartOnce=false;throw new HttpRequestException("Fixture dropped response");}}
        if(request.RequestUri!.AbsolutePath=="/support/claim"){Secret=body.RootElement.GetProperty("secret").GetString();if(Paid&&WalletSecret is not null){var walletId=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WalletSecret))).ToLowerInvariant();Receipt=SignWallet!(walletId);}}
        string response=Oversized?new string('x',16385):request.RequestUri.AbsolutePath=="/support/start-v2"?JsonSerializer.Serialize(new{attemptId=new string('A',32),checkoutUrl=BadUrl?"https://attacker.invalid/":"https://checkout.stripe.com/c/pay/example"}):Paid?JsonSerializer.Serialize(new{status="paid",receipt=Receipt}):"{\"status\":\"pending\"}";
        return new(HttpStatusCode.OK){Content=new StringContent(response,Encoding.UTF8,"application/json")};
    }
}

sealed class PassHandler:HttpMessageHandler {
    public bool Unavailable;public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
        Calls++;
        if(request.Headers.Contains("Cookie")||request.RequestUri!.Host!="inventory.roblox.com")throw new Exception("Unexpected credentials or destination");
        bool owned=request.RequestUri!.AbsolutePath=="/v1/users/22/items/GamePass/456/is-owned";
        return Task.FromResult(new HttpResponseMessage(Unavailable?HttpStatusCode.Forbidden:HttpStatusCode.OK){Content=new StringContent(owned?"true":"false")});
    }
}
