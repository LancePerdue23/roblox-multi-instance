using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RobloxMultiInstance;

public sealed record SupportConfiguration(string Endpoint, Dictionary<string,string> PublicKeys, string? RobloxGamePassId=null, string[]? RobloxGamePassIds=null, RobloxSupportPass[]? RobloxPasses=null)
{
    public static SupportConfiguration? Load()
    {
        // Configuration is a release-owner supplied public resource, never a frontend URL.
        try {
            string path=Path.Combine(AppContext.BaseDirectory,"support-config.json");
            if(!File.Exists(path)||new FileInfo(path).Length>32768)return null;
            return JsonSerializer.Deserialize<SupportConfiguration>(File.ReadAllText(path),SupportService.Json);
        } catch { return null; }
    }
}

public sealed record RobloxSupportPass(string Id,long Credit);
public sealed record SupportState(bool Configured,bool Supported,bool ShowBadge,bool Pending,string Message,bool RobloxConfigured,bool ReceiptAvailable=false,int VerifiedTier=-1,long TotalCents=0,bool RecoveryAvailable=false,long TotalRobux=0,int RobloxTier=-1,int UsdTier=-1,RobloxSupportPass[]? RobloxPasses=null);

/// <summary>Payment identity stays with the payment provider. A pseudonymous recovery credential and offline cosmetic receipt are protected locally.</summary>
public sealed partial class SupportService : IDisposable
{
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    const string Product="roblox-multi-instance";
    readonly string root;
    readonly SupportConfiguration? configuration;
    readonly HttpClient http;
    readonly bool ownsClient;
    readonly Uri? endpoint;
    readonly object walletLock=new();
    readonly SemaphoreSlim paymentGate=new(1,1);
    readonly SemaphoreSlim robloxGate=new(1,1);
    DateTimeOffset lastRobloxCheck;
    string RobloxReceiptPath=>Path.Combine(root,"support-roblox.bin");
    string ReceiptPath=>Path.Combine(root,"support-receipt.bin");
    string LegacyReceiptPath=>Path.Combine(root,"support-legacy-receipt.bin");
    string WalletPath=>Path.Combine(root,"support-wallet.bin");
    string PendingPath=>Path.Combine(root,"support-pending.bin");
    string PreferencePath=>Path.Combine(root,"support-preferences.json");

    public SupportService(string storageRoot,HttpClient? client=null,SupportConfiguration? configuration=null)
    {
        root=storageRoot;this.configuration=configuration??SupportConfiguration.Load()??ApprovedRobloxConfiguration();
        if(TrustedKeys(this.configuration)&&Uri.TryCreate(this.configuration!.Endpoint,UriKind.Absolute,out var uri)&&
            uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Query.Length==0&&uri.Fragment.Length==0&&uri.AbsolutePath=="/")endpoint=uri;
        ownsClient=client is null;
        http=client??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(20)};
    }

    public SupportState State()
    {
        bool supported=false,pending=false,visible=true,receiptAvailable=false;int tier=-1;long total=0;
        var roblox=RobloxCredit();
        supported=roblox.Tier>=0;
        try { supported|=ReadProtected(RobloxReceiptPath)=="verified-gamepass-supporter-v1"; } catch { }
        try { if(File.Exists(LegacyReceiptPath)&&ReadReceipt(ReadProtected(LegacyReceiptPath)).SupporterId is null){supported=true;tier=0;} } catch { }
        try { if(File.Exists(ReceiptPath)){var receipt=ReadReceipt(ReadProtected(ReceiptPath));tier=Math.Max(tier,receipt.Tier);total=receipt.TotalCents;supported=tier>=0||supported;receiptAvailable=true;} } catch { }
        try { if(File.Exists(PendingPath)){var p=JsonSerializer.Deserialize<PendingClaim>(ReadProtected(PendingPath),Json);pending=p is not null&&p.ExpiresUtc>DateTimeOffset.UtcNow;} } catch { }
        try { if(File.Exists(PreferencePath))visible=JsonSerializer.Deserialize<BadgePreference>(ReadBounded(PreferencePath,1024),Json)?.ShowBadge??true; } catch { visible=false; }
        return new(endpoint is not null,supported,supported&&visible,pending,
            supported?"Thank you! Your supporter badge works offline.":pending?"Return after checkout to check payment.":"Support is optional. All features stay free.",GamePasses.Length>0,receiptAvailable,supported?Math.Max(Math.Max(tier,roblox.Tier),0):-1,total,File.Exists(WalletPath),roblox.Total,roblox.Tier,tier,GamePasses);
    }

    // Only the release owner's pass ID is accepted. No frontend URL or Roblox session is used.
    public Uri RobloxDonationUri(string? passId=null)
    {
        var passes=GamePasses;
        if(passes.Length==0)throw new InvalidOperationException("Robux donations are not configured in this build yet.");
        passId??=passes[0].Id;
        if(!passes.Any(p=>p.Id==passId))throw new ArgumentException("Choose a configured donation pass.");
        return new Uri("https://www.roblox.com/game-pass/"+passId);
    }
    static bool ValidGamePassId(string? id)=>id is {Length:>0 and <=19}&&id[0]!='0'&&id.All(char.IsAsciiDigit)&&long.TryParse(id,out long value)&&value>0;

    public Task<Uri> StartAsync(CancellationToken token=default)=>StartAsync(500,token);
    public async Task<Uri> StartAsync(int amountCents,CancellationToken token=default)
    {
        await paymentGate.WaitAsync(token);try{return await StartCoreAsync(amountCents,token);}finally{paymentGate.Release();}
    }
    async Task<Uri> StartCoreAsync(int amountCents,CancellationToken token)
    {
        if(amountCents is <50 or >100000)throw new ArgumentException("Choose between $0.50 and $1,000 USD.");
        RequireConfigured();
        PendingClaim? pending=null;
        if(File.Exists(PendingPath)){
            pending=JsonSerializer.Deserialize<PendingClaim>(ReadProtected(PendingPath),Json)??throw new InvalidDataException("Pending support claim unavailable.");
            if(pending.CheckoutUrl.Length>0){
                if(pending.CheckoutExpiresUtc<=DateTimeOffset.UtcNow)throw new InvalidOperationException("This checkout has expired. Check payment before forgetting it and starting a new checkout.");
                return CheckoutUri(pending.CheckoutUrl);
            }
        }
        string walletSecret=RecoveryCode();
        string walletId=WalletId(walletSecret);
        if(File.Exists(ReceiptPath)){var old=ReadReceipt(ReadProtected(ReceiptPath));if(old.SupporterId is not null&&old.SupporterId!=walletId)throw new InvalidOperationException("Restore the recovery code for this receipt before adding contributions.");}
        amountCents=pending?.AmountCents??amountCents;
        string secret=pending?.Secret??Encode(RandomNumberGenerator.GetBytes(32));
        if(pending is null)SaveProtected(PendingPath,JsonSerializer.Serialize(new PendingClaim("",secret,DateTimeOffset.UtcNow.AddDays(30),"",DateTimeOffset.MinValue,amountCents,walletId),Json));
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
        using var response=await PostAsync("support/start-v2",new{claimHash=hash,walletSecret,amountCents},token);
        var result=response.RootElement;
        string attempt=Required(result,"attemptId"),url=Required(result,"checkoutUrl");
        if(attempt.Length is <16 or >100||attempt.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new InvalidDataException("Invalid support response.");
        var checkout=CheckoutUri(url);
        SaveProtected(PendingPath,JsonSerializer.Serialize(new PendingClaim(attempt,secret,DateTimeOffset.UtcNow.AddDays(30),checkout.AbsoluteUri,DateTimeOffset.UtcNow.AddHours(24),amountCents,walletId),Json));
        return checkout;
    }

    public async Task<SupportState> ClaimAsync(CancellationToken token=default)
    {
        await paymentGate.WaitAsync(token);try{return await ClaimCoreAsync(token);}finally{paymentGate.Release();}
    }
    async Task<SupportState> ClaimCoreAsync(CancellationToken token)
    {
        RequireConfigured();
        if(!File.Exists(PendingPath))return State();
        PendingClaim claim;
        try {claim=JsonSerializer.Deserialize<PendingClaim>(ReadProtected(PendingPath),Json)??throw new InvalidDataException();}
        catch {throw new InvalidDataException("The pending support claim cannot be read. Your payment provider retains your payment record.");}
        if(claim.AttemptId.Length==0)return State();
        if(claim.ExpiresUtc<=DateTimeOffset.UtcNow)throw new InvalidOperationException("This pending claim has expired. Contact the developer using your payment provider receipt.");
        using var response=await PostAsync("support/claim",new{attemptId=claim.AttemptId,secret=claim.Secret},token);
        string status=Required(response.RootElement,"status");
        if(status=="paid"){
            string receipt=Required(response.RootElement,"receipt");var verified=ReadReceipt(receipt);
            if(claim.SupporterId is not null&&verified.SupporterId!=claim.SupporterId)throw new InvalidDataException("Receipt belongs to another supporter wallet.");
            StoreReceipt(receipt);File.Delete(PendingPath);
        } else if(status!="pending")throw new InvalidOperationException("The support payment could not be confirmed.");
        return State();
    }

    public SupportState SetBadgeVisible(bool visible){Save(PreferencePath,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new BadgePreference(visible),Json)));return State();}
    public SupportState RemoveReceipt(){File.Delete(ReceiptPath);File.Delete(LegacyReceiptPath);return State();}
    public SupportState ClearPending(){File.Delete(PendingPath);return State();}
    public void ExportReceipt(string absolutePath)
    {
        ValidatePath(absolutePath);string receipt=ReadProtected(ReceiptPath);var claims=ReadReceipt(receipt);
        if(claims.Tier<0&&File.Exists(LegacyReceiptPath)){receipt=ReadProtected(LegacyReceiptPath);VerifyReceipt(receipt);}
        if(File.Exists(absolutePath)&&(File.GetAttributes(absolutePath)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a regular file for the supporter receipt.");
        Save(absolutePath,Encoding.UTF8.GetBytes(receipt));
    }
    public SupportState ImportReceipt(string absolutePath)
    {
        ValidatePath(absolutePath);string receipt=Encoding.UTF8.GetString(ReadBounded(absolutePath,8192)).Trim();VerifyReceipt(receipt);StoreReceipt(receipt);return State();
    }
    public void VerifyReceipt(string receipt)=>ReadReceipt(receipt);
    sealed record ReceiptClaims(int Tier,long TotalCents,long Revision,string? SupporterId);
    ReceiptClaims ReadReceipt(string receipt)
    {
        try {
            if(receipt.Length>8192)throw new InvalidDataException();
            var parts=receipt.Split('.');if(parts.Length!=2)throw new InvalidDataException();
            byte[] payload=Decode(parts[0]),signature=Decode(parts[1]);
            using var doc=JsonDocument.Parse(payload);
            var value=doc.RootElement;
            var names=new HashSet<string>(StringComparer.Ordinal);
            int version=value.GetProperty("version").GetInt32();
            string[] allowed=version==1?["product","badge","receiptId","version","keyId"]:version==2?["product","badge","receiptId","version","keyId","supporterId","tier","totalCents","revision","currency"]:throw new InvalidDataException();
            foreach(var property in value.EnumerateObject())if(!names.Add(property.Name)||!allowed.Contains(property.Name))throw new InvalidDataException();
            if(names.Count!=allowed.Length||Required(value,"product")!=Product||Required(value,"badge")!="supporter"||Required(value,"receiptId").Length is <16 or >100)throw new InvalidDataException();
            string keyId=Required(value,"keyId");
            if(configuration is null||!configuration.PublicKeys.TryGetValue(keyId,out var key))throw new InvalidDataException();
            using var verifier=ECDsa.Create();byte[] spki=Convert.FromBase64String(key);verifier.ImportSubjectPublicKeyInfo(spki,out int read);
            if(read!=spki.Length||verifier.KeySize!=256||verifier.ExportParameters(false).Curve.Oid.Value!="1.2.840.10045.3.1.7"||signature.Length!=64||!verifier.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))throw new InvalidDataException();
            if(version==1)return new(0,0,0,null);
            string supporterId=Required(value,"supporterId");int tier=value.GetProperty("tier").GetInt32();long total=value.GetProperty("totalCents").GetInt64(),revision=value.GetProperty("revision").GetInt64();
            if(supporterId.Length!=64||supporterId.Any(c=>!char.IsAsciiHexDigitLower(c))||Required(value,"receiptId")!=supporterId||Required(value,"currency")!="usd"||tier is < -1 or >2||total is <0 or >9007199254740991||revision is <0 or >9007199254740991||tier<(total>=1500?2:total>=700?1:total>=300?0:-1))throw new InvalidDataException();
            return new(tier,total,revision,supporterId);
        } catch(Exception e) when(e is not OutOfMemoryException) {throw new InvalidDataException("This supporter receipt is invalid or its signing key is not trusted.");}
    }
    ReceiptClaims? CurrentReceipt(){try{return File.Exists(ReceiptPath)?ReadReceipt(ReadProtected(ReceiptPath)):null;}catch(Exception e) when(e is not OutOfMemoryException){return null;}}
    void StoreReceipt(string receipt)
    {
        var next=ReadReceipt(receipt);
        var previous=CurrentReceipt();
        if(previous is not null){
            if(previous.SupporterId is not null&&previous.SupporterId==next.SupporterId&&(previous.Revision>next.Revision||previous.Tier>next.Tier))throw new InvalidDataException("An older receipt cannot replace newer supporter recognition.");
        }
        if(previous is {SupporterId:null}&&next.SupporterId is not null)SaveProtected(LegacyReceiptPath,ReadProtected(ReceiptPath));
        SaveProtected(ReceiptPath,receipt);
    }
    static string WalletId(string secret)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
    public string RecoveryCode()
    {
        RequireConfigured();
        lock(walletLock){if(!File.Exists(WalletPath))SaveProtected(WalletPath,Encode(RandomNumberGenerator.GetBytes(32)));
        return ReadProtected(WalletPath);}
    }
    public async Task<SupportState> RefreshAsync(string? recoveryCode=null,CancellationToken token=default)
    {
        RequireConfigured();await paymentGate.WaitAsync(token);
        try {
            if(recoveryCode is not null&&File.Exists(PendingPath))throw new InvalidOperationException("Check or forget the pending checkout before restoring another wallet.");
            string code=recoveryCode??RecoveryCode();
            if(code.Length!=43||code.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new ArgumentException("Enter a valid supporter recovery code.");
            using var response=await PostAsync("support/wallet",new{walletSecret=code},token);
            string receipt=Required(response.RootElement,"receipt");var claims=ReadReceipt(receipt);
            if(claims.SupporterId!=WalletId(code))throw new InvalidDataException("Receipt belongs to another supporter wallet.");
            StoreReceipt(receipt);
            if(recoveryCode is not null)SaveProtected(WalletPath,code);
            return State();
        } finally {paymentGate.Release();}
    }

    async Task<JsonDocument> PostAsync(string path,object value,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response=await http.PostAsJsonAsync(new Uri(endpoint!,path),value,Json,timeout.Token);
        if(!response.IsSuccessStatusCode)throw new HttpRequestException("The support service is unavailable. Your receipt and payment remain safe.");
        if(response.Content.Headers.ContentLength>16384)throw new InvalidDataException("Support response too large.");
        using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var buffer=new MemoryStream();byte[] chunk=new byte[4096];
        int count;while((count=await stream.ReadAsync(chunk,timeout.Token))>0){if(buffer.Length+count>16384)throw new InvalidDataException("Support response too large.");buffer.Write(chunk,0,count);}
        return JsonDocument.Parse(buffer.ToArray(),new JsonDocumentOptions{MaxDepth=8});
    }
    void RequireConfigured(){if(endpoint is null)throw new InvalidOperationException("Support payments are not configured in this build.");}
    static bool TrustedKeys(SupportConfiguration? config)
    {
        try{
            if(config is not {PublicKeys.Count:>0 and <=16})return false;
            foreach(var entry in config.PublicKeys){
                if(entry.Key.Length is <1 or >64||entry.Key.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))return false;
                using var verifier=ECDsa.Create();byte[] spki=Convert.FromBase64String(entry.Value);verifier.ImportSubjectPublicKeyInfo(spki,out int read);
                if(read!=spki.Length||verifier.KeySize!=256||verifier.ExportParameters(false).Curve.Oid.Value!="1.2.840.10045.3.1.7")return false;
            }
            return true;
        }catch{return false;}
    }
    static Uri CheckoutUri(string url){if(!Uri.TryCreate(url,UriKind.Absolute,out var checkout)||checkout.Scheme!="https"||!checkout.IsDefaultPort||checkout.UserInfo.Length!=0||checkout.Host!="checkout.stripe.com")throw new InvalidDataException("Invalid payment address.");return checkout;}
    static string Required(JsonElement value,string property)=>value.GetProperty(property).GetString()??throw new InvalidDataException("Invalid support response.");
    static string Encode(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
    static byte[] Decode(string value){if(value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new InvalidDataException();return Convert.FromBase64String(value.Replace('-','+').Replace('_','/')+new string('=',(4-value.Length%4)%4));}
    static byte[] ReadBounded(string path,int max){using var stream=File.OpenRead(path);if(stream.Length>max)throw new InvalidDataException("Support file too large.");var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);return bytes;}
    static void ValidatePath(string path){if(!Path.IsPathFullyQualified(path)||Path.GetExtension(path)!=".rmisupport")throw new ArgumentException("Choose an absolute .rmisupport file path.");}
    static void SaveProtected(string path,string value)=>Save(path,ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));
    static string ReadProtected(string path)=>Encoding.UTF8.GetString(ProtectedData.Unprotect(ReadBounded(path,16384),null,DataProtectionScope.CurrentUser));
    static void Save(string path,byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            File.WriteAllBytes(temporary,bytes);
            // Windows scanners can briefly retain a handle after replacement. Keep the
            // previous durable file intact and retry the same atomic move, never delete it.
            for(int attempt=0;;attempt++){
                try{File.Move(temporary,path,true);break;}
                catch(Exception e) when(attempt<4&&e is IOException or UnauthorizedAccessException){Thread.Sleep(25*(attempt+1));}
            }
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public void Dispose(){if(ownsClient)http.Dispose();}
    sealed record PendingClaim(string AttemptId,string Secret,DateTimeOffset ExpiresUtc,string CheckoutUrl,DateTimeOffset CheckoutExpiresUtc,int AmountCents=500,string? SupporterId=null);
    sealed record BadgePreference(bool ShowBadge);
}
