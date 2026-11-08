using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace RobloxMultiInstance;

// Native-only: never return these records through the web bridge.
public sealed record SignupCredential(string Username,string Password,bool Confirmed=false)
{
    public override string ToString()=>"Supplied signup credentials (redacted)";
}
public sealed record PendingSignup(string Id,SignupCredential Credential)
{
    public override string ToString()=>"Pending signup (redacted)";
}
public sealed record AccountCreationOptions(string Prefix,long Start,int Width,int Count,DateOnly Birthdate,string? SharedPassword=null)
{
    public override string ToString()=>"Account creation options (redacted)";
    public string Username(long number)
    {
        if(number<0||Width is <1 or >19)throw new ArgumentException("Use a nonnegative number and suffix width from 1 to 19.");
        string value=Prefix+number.ToString("D"+Width,CultureInfo.InvariantCulture);
        if(!ValidUsername(value))throw new ArgumentException("Numbered usernames must contain 3–20 letters or digits, with at most one underscore inside the name.");
        return value;
    }
    public string[] Preview(int capacity)
    {
        if(Count<1||Count>capacity)throw new ArgumentException("Choose a count within the remaining account capacity.");
        if(Birthdate>DateOnly.FromDateTime(DateTime.Today)||Birthdate.Year<1900)throw new ArgumentException("Enter your accurate birthdate.");
        if(SharedPassword is not null&&(SharedPassword.Length is <8 or >128||SharedPassword.Any(char.IsControl)))throw new ArgumentException("Use a password of 8–128 characters without control characters.");
        try{return Enumerable.Range(0,Count).Select(i=>Username(checked(Start+i))).ToArray();}
        catch(OverflowException){throw new ArgumentException("The username sequence exceeds the supported number range.");}
    }
    public static string GeneratePassword()
    {
        const string alphabet="ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return "Aa7"+new string(Enumerable.Range(0,21).Select(_=>alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }
    public static bool ValidUsername(string value)=>AccountStore.ValidUsername(value)&&!value.StartsWith('_')&&!value.EndsWith('_')&&value.Count(c=>c=='_')<=1;
}

public sealed record AccountCreationProgressDto(int Completed,int Skipped,int Remaining,string Status,string Code="");

public sealed class AccountCredentialStore
{
    readonly string root;
    readonly WindowsAccountVault pending,linked;
    public AccountCredentialStore(string accountRoot)
    {
        root=Path.Combine(accountRoot,"credentials");pending=new(Path.Combine(root,"pending"));linked=new(Path.Combine(root,"linked"));
    }
    public void SavePending(string id,SignupCredential value)
    {
        if(!AccountStore.ValidUsername(value.Username)||value.Password.Length is <8 or >128||value.Password.Any(char.IsControl))throw new ArgumentException("Check the supplied username and password.");
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value);
        try{pending.Save(id,bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    static SignupCredential? Read(IAccountVault vault,string id)
    {
        byte[]? bytes=vault.Read(id);if(bytes is null)return null;
        try{return JsonSerializer.Deserialize<SignupCredential>(bytes)??throw new InvalidDataException("Credentials are unavailable.");}
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public SignupCredential? Get(string id)=>Read(linked,id);
    public SignupCredential? GetPending(string id)=>Read(pending,id);
    public bool Has(string id)
    {
        string path=Path.Combine(root,"linked",AccountStore.ValidId(id)+".bin");AccountStorageSecurity.ValidatePath(path);return File.Exists(path);
    }
    public PendingSignup[] Pending()
    {
        string directory=Path.Combine(root,"pending");AccountStorageSecurity.ValidatePath(directory);
        if(!Directory.Exists(directory))return [];
        return Directory.EnumerateFiles(directory,"*.bin").Select(path=>new PendingSignup(AccountStore.ValidId(Path.GetFileNameWithoutExtension(path)),Read(pending,Path.GetFileNameWithoutExtension(path))!)).ToArray();
    }
    public void Finalize(string id)
    {
        var value=Read(pending,id);if(value is null)return;
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value);
        try{linked.Save(id,bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
        pending.Remove(id);
    }
    public void Reconcile(IEnumerable<AccountDto> accounts)
    {
        foreach(var account in accounts)Finalize(account.Id);
    }
    public void DeletePending(string id)=>pending.Remove(id);
    public void Remove(string id){linked.Remove(id);pending.Remove(id);}
}

// Holds no page bridge or password-bearing progress payloads. The native host owns setup and interaction.
public sealed class AccountCreationCoordinator(AccountManager accounts)
{
    public async Task<AccountsStateDto> RunAsync(AccountCreationOptions options,
        Func<AccountSignInRequest,SignupCredential,DateOnly,CancellationToken,Task<AccountIdentity?>> signup,
        Action<AccountCreationProgressDto> progress,CancellationToken token,Func<Task<bool>>? retrySave=null)
    {
        var state=accounts.State();if(!state.Available)throw new InvalidOperationException("Account storage is unavailable.");
        options.Preview(100-state.Accounts.Length);long number=options.Start;int completed=0,skipped=0;bool finished=false;
        try
        {
            for(int slot=0;slot<options.Count;slot++)
            {
                token.ThrowIfCancellationRequested();
                LastNumber=number;
                var credential=new SignupCredential(options.Username(number),options.SharedPassword??AccountCreationOptions.GeneratePassword());
                progress(new(completed,skipped,options.Count-slot,"Creating"));
                bool verified=false;
                await accounts.ConnectAsync(null,"",async(request,ct)=>
                {
                    accounts.Credentials.SavePending(request.Id,credential);
                    var identity=await signup(request,credential,options.Birthdate,ct);
                    verified=identity is not null;
                    // The native window updates the pending record after explicit final-credential confirmation.
                    var final=accounts.Credentials.GetPending(request.Id)??throw new InvalidOperationException("Pending credentials are unavailable.");
                    if(identity is not null&&(!final.Confirmed||!string.Equals(final.Username,identity.Username,StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Confirm the final supplied credentials before saving this account.");
                    return identity;
                },token,commitVerified:true,retrySave:retrySave);
                if(verified){completed++;accounts.Credentials.Reconcile(accounts.State().Accounts);}else skipped++;
                // Include any native Next username choices in the next sequence position.
                if(slot+1<options.Count)number=checked(Math.Max(number,LastNumber??number)+1);
                LastNumber=null;
            }
            finished=true;
        }
        finally{progress(new(completed,skipped,Math.Max(0,options.Count-completed-skipped),finished?(completed==0?"Skipped":"Finished"):token.IsCancellationRequested?"Canceled":"Stopped"));}
        return accounts.State();
    }
    public long? LastNumber {get;set;}
}
