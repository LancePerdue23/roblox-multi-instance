using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RobloxMultiInstance;

public sealed record SupportConfiguration(string Endpoint, Dictionary<string,string> PublicKeys, string? RobloxGamePassId=null, string[]? RobloxGamePassIds=null)
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

public sealed record SupportState(bool Configured,bool Supported,bool ShowBadge,bool Pending,string Message,bool RobloxConfigured,bool ReceiptAvailable=false);

/// <summary>Payment identity stays with the payment provider. Only an offline cosmetic receipt is retained here.</summary>
public sealed class SupportService : IDisposable
{
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    const string Product="roblox-multi-instance";
    readonly string root;
    readonly SupportConfiguration? configuration;
    readonly HttpClient http;
    readonly bool ownsClient;
    readonly Uri? endpoint;
    readonly SemaphoreSlim robloxGate=new(1,1);
    DateTimeOffset lastRobloxCheck;
    string RobloxReceiptPath=>Path.Combine(root,"support-roblox.bin");
    string[] GamePassIds=>new[]{configuration?.RobloxGamePassId}.Concat(configuration?.RobloxGamePassIds??[]).Where(ValidGamePassId).Select(id=>id!).Distinct().Take(16).ToArray();
    string ReceiptPath=>Path.Combine(root,"support-receipt.bin");
    string PendingPath=>Path.Combine(root,"support-pending.bin");
    string PreferencePath=>Path.Combine(root,"support-preferences.json");

    public SupportService(string storageRoot,HttpClient? client=null,SupportConfiguration? configuration=null)
    {
        root=storageRoot;this.configuration=configuration??SupportConfiguration.Load();
        if(TrustedKeys(this.configuration)&&Uri.TryCreate(this.configuration!.Endpoint,UriKind.Absolute,out var uri)&&
            uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Query.Length==0&&uri.Fragment.Length==0&&uri.AbsolutePath=="/")endpoint=uri;
        ownsClient=client is null;
        http=client??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(20)};
    }

    public SupportState State()
    {
        bool supported=false,pending=false,visible=true,receiptAvailable=false;
        try { supported=ReadProtected(RobloxReceiptPath)=="verified-gamepass-supporter-v1"; } catch { }
        try { if(File.Exists(ReceiptPath)){VerifyReceipt(ReadProtected(ReceiptPath));supported=true;receiptAvailable=true;} } catch { }
        try { if(File.Exists(PendingPath)){var p=JsonSerializer.Deserialize<PendingClaim>(ReadProtected(PendingPath),Json);pending=p is not null&&p.ExpiresUtc>DateTimeOffset.UtcNow;} } catch { }
        try { if(File.Exists(PreferencePath))visible=JsonSerializer.Deserialize<BadgePreference>(ReadBounded(PreferencePath,1024),Json)?.ShowBadge??true; } catch { visible=false; }
        return new(endpoint is not null,supported,supported&&visible,pending,
            supported?"Thank you for supporting development. Your badge works offline.":endpoint is null?"Stripe payments are not configured in this build.":pending?"Return here after payment and check for your supporter receipt.":"Support is optional. All features are free and stay free.",GamePassIds.Length>0,receiptAvailable);
    }

    // Only the release owner's pass ID is accepted. No frontend URL or Roblox session is used.
    public Uri RobloxDonationUri()
    {
        if(GamePassIds.Length==0)throw new InvalidOperationException("Robux donations are not configured in this build yet.");
        return new Uri("https://www.roblox.com/game-pass/"+GamePassIds[0]);
    }
    static bool ValidGamePassId(string? id)=>id is {Length:>0 and <=19}&&id[0]!='0'&&id.All(char.IsAsciiDigit)&&long.TryParse(id,out long value)&&value>0;

    // IDs come from the native account store, never from a bridge payload. No cookies are sent.
    public async Task<SupportState> CheckRobloxAsync(IEnumerable<string> userIds,bool force=false,CancellationToken token=default)
    {
        if(GamePassIds.Length==0)return State();
        await robloxGate.WaitAsync(token);
        try {
            if(!force&&DateTimeOffset.UtcNow-lastRobloxCheck<TimeSpan.FromHours(6))return State();
            var ids=userIds.Where(ValidGamePassId).Distinct().Take(100).ToArray();
            if(ids.Length==0)return State() with {Message="Connect a Roblox account, then check purchases."};
            bool unavailable=false;
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(20));
            foreach(string id in ids)foreach(string pass in GamePassIds){
                try {
                    using var response=await http.GetAsync($"https://inventory.roblox.com/v1/users/{id}/items/GamePass/{pass}/is-owned",HttpCompletionOption.ResponseHeadersRead,deadline.Token);
                    if(!response.IsSuccessStatusCode){unavailable=true;continue;}
                    using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
                    byte[] bytes=new byte[65];int count=0,read;
                    while(count<bytes.Length&&(read=await stream.ReadAsync(bytes.AsMemory(count),deadline.Token))>0)count+=read;
                    if(count>64){unavailable=true;continue;}
                    using var document=JsonDocument.Parse(bytes.AsMemory(0,count));
                    if(document.RootElement.ValueKind==JsonValueKind.True){
                        SaveProtected(RobloxReceiptPath,"verified-gamepass-supporter-v1");lastRobloxCheck=DateTimeOffset.UtcNow;
                        return State() with {Message="Roblox gamepass verified. Thank you! Supporter recognition applies to this whole app and works offline."};
                    }
                    if(document.RootElement.ValueKind!=JsonValueKind.False)unavailable=true;
                }catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
                catch {unavailable=true;}
                if(deadline.IsCancellationRequested)break;
            }
            lastRobloxCheck=DateTimeOffset.UtcNow;
            return State() with {Message=unavailable?"Some purchases could not be checked. Try again later; existing supporter recognition is retained.":State().Supported?"Supporter recognition is retained. Thank you for your support!":"No qualifying gamepass found on your connected accounts. After purchasing, check again."};
        }finally{robloxGate.Release();}
    }

    public async Task<Uri> StartAsync(CancellationToken token=default)
    {
        RequireConfigured();
        PendingClaim? pending=null;
        if(File.Exists(PendingPath)){
            pending=JsonSerializer.Deserialize<PendingClaim>(ReadProtected(PendingPath),Json)??throw new InvalidDataException("Pending support claim unavailable.");
            if(pending.CheckoutUrl.Length>0){
                if(pending.CheckoutExpiresUtc<=DateTimeOffset.UtcNow)throw new InvalidOperationException("This checkout has expired. Check payment before forgetting it and starting a new checkout.");
                return CheckoutUri(pending.CheckoutUrl);
            }
        }
        string secret=pending?.Secret??Encode(RandomNumberGenerator.GetBytes(32));
        if(pending is null)SaveProtected(PendingPath,JsonSerializer.Serialize(new PendingClaim("",secret,DateTimeOffset.UtcNow.AddDays(30),"",DateTimeOffset.MinValue),Json));
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
        using var response=await PostAsync("support/start",new{claimHash=hash},token);
        var result=response.RootElement;
        string attempt=Required(result,"attemptId"),url=Required(result,"checkoutUrl");
        if(attempt.Length is <16 or >100||attempt.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new InvalidDataException("Invalid support response.");
        var checkout=CheckoutUri(url);
        SaveProtected(PendingPath,JsonSerializer.Serialize(new PendingClaim(attempt,secret,DateTimeOffset.UtcNow.AddDays(30),checkout.AbsoluteUri,DateTimeOffset.UtcNow.AddHours(24)),Json));
        return checkout;
    }

    public async Task<SupportState> ClaimAsync(CancellationToken token=default)
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
            string receipt=Required(response.RootElement,"receipt");VerifyReceipt(receipt);
            SaveProtected(ReceiptPath,receipt);File.Delete(PendingPath);
        } else if(status!="pending")throw new InvalidOperationException("The support payment could not be confirmed.");
        return State();
    }

    public SupportState SetBadgeVisible(bool visible){Save(PreferencePath,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new BadgePreference(visible),Json)));return State();}
    public SupportState RemoveReceipt(){File.Delete(ReceiptPath);return State();}
    public SupportState ClearPending(){File.Delete(PendingPath);return State();}
    public void ExportReceipt(string absolutePath)
    {
        ValidatePath(absolutePath);string receipt=ReadProtected(ReceiptPath);VerifyReceipt(receipt);
        if(File.Exists(absolutePath)&&(File.GetAttributes(absolutePath)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a regular file for the supporter receipt.");
        Save(absolutePath,Encoding.UTF8.GetBytes(receipt));
    }
    public SupportState ImportReceipt(string absolutePath)
    {
        ValidatePath(absolutePath);string receipt=Encoding.UTF8.GetString(ReadBounded(absolutePath,8192)).Trim();VerifyReceipt(receipt);SaveProtected(ReceiptPath,receipt);return State();
    }
    public void VerifyReceipt(string receipt)
    {
        try {
            if(receipt.Length>8192)throw new InvalidDataException();
            var parts=receipt.Split('.');if(parts.Length!=2)throw new InvalidDataException();
            byte[] payload=Decode(parts[0]),signature=Decode(parts[1]);
            using var doc=JsonDocument.Parse(payload);
            var value=doc.RootElement;
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in value.EnumerateObject())if(!names.Add(property.Name)||property.Name is not ("product" or "badge" or "receiptId" or "version" or "keyId"))throw new InvalidDataException();
            if(names.Count!=5||Required(value,"product")!=Product||Required(value,"badge")!="supporter"||value.GetProperty("version").GetInt32()!=1||Required(value,"receiptId").Length is <16 or >100)throw new InvalidDataException();
            string keyId=Required(value,"keyId");
            if(configuration is null||!configuration.PublicKeys.TryGetValue(keyId,out var key))throw new InvalidDataException();
            using var verifier=ECDsa.Create();byte[] spki=Convert.FromBase64String(key);verifier.ImportSubjectPublicKeyInfo(spki,out int read);
            if(read!=spki.Length||verifier.KeySize!=256||verifier.ExportParameters(false).Curve.Oid.Value!="1.2.840.10045.3.1.7"||signature.Length!=64||!verifier.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))throw new InvalidDataException();
        } catch(Exception e) when(e is not OutOfMemoryException) {throw new InvalidDataException("This supporter receipt is invalid or its signing key is not trusted.");}
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
    static void Save(string path,byte[] bytes){Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
    public void Dispose(){if(ownsClient)http.Dispose();}
    sealed record PendingClaim(string AttemptId,string Secret,DateTimeOffset ExpiresUtc,string CheckoutUrl,DateTimeOffset CheckoutExpiresUtc);
    sealed record BadgePreference(bool ShowBadge);
}
