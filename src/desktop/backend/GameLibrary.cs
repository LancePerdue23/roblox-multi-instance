using System.Net;
using System.Text.Json;

namespace RobloxMultiInstance;

public sealed record GameCardDto(string PlaceId,string? UniverseId,string? RootPlaceId,string Name,string? Artwork,string ArtworkState="Unavailable",string ArtworkKind="Unknown",string[]? AccountIds=null,string? Preset=null,string? Arrangement=null);
public sealed record PublicServerDto(string Id,int Playing,int MaxPlayers,int? Ping);

// Public metadata requests only. History also stores local account references and layout.
// No account cookies, tickets or authenticated HTTP client.
public sealed class GameLibrary(string root,HttpMessageHandler? handler=null) : IDisposable
{
    readonly HttpClient http=new(handler??new HttpClientHandler {UseCookies=false,AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(5)};
    readonly SemaphoreSlim gate=new(1,1);
    readonly Dictionary<string,(DateTime At,GameCardDto Card)> cache=[];
    string HistoryPath=>Path.Combine(root,"recent-games.json");
    async Task<JsonDocument> Get(string url,CancellationToken token)
    {
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength is >1048576)throw new IOException();
        await using var stream=await response.Content.ReadAsStreamAsync(token);
        using var bytes=new MemoryStream();byte[] buffer=new byte[8192];int read;
        while((read=await stream.ReadAsync(buffer,token))>0){if(bytes.Length+read>1048576)throw new IOException();bytes.Write(buffer,0,read);}
        return JsonDocument.Parse(bytes.ToArray());
    }
    public async Task<string?> Universe(string place,CancellationToken token)
    {
        if(!LaunchTargetResolver.IsPositiveId(place))return null;
        using var json=await Get($"https://apis.roblox.com/universes/v1/places/{place}/universe",token);
        return json.RootElement.GetProperty("universeId").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    public async Task<GameCardDto> Resolve(string place,CancellationToken token)
    {
        if(!LaunchTargetResolver.IsPositiveId(place))throw new ArgumentException("Invalid Place ID.");
        await gate.WaitAsync(token);
        try
        {
            if(cache.TryGetValue(place,out var saved)&&DateTime.UtcNow-saved.At<TimeSpan.FromMinutes(10))return saved.Card;
            var card=new GameCardDto(place,null,null,"Place "+place,null);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                string? universe=await Universe(place,deadline.Token);
                using var game=await Get($"https://games.roblox.com/v1/games?universeIds={universe}",deadline.Token);
                var data=game.RootElement.GetProperty("data").EnumerateArray().Single();
                if(data.GetProperty("id").GetInt64().ToString()!=universe)throw new IOException();
                card=card with {UniverseId=universe,RootPlaceId=data.GetProperty("rootPlaceId").GetInt64().ToString(),Name=data.GetProperty("name").GetString()??card.Name};
                string? art=null;
                foreach(string url in new[]{$"https://thumbnails.roblox.com/v1/games/icons?universeIds={universe}&size=420x420&format=Png&isCircular=false",$"https://thumbnails.roblox.com/v1/games/multiget/thumbnails?universeIds={universe}&countPerUniverse=1&defaults=true&size=768x432&format=Png&isCircular=false"})
                {
                    try
                    {
                        using var images=await Get(url,deadline.Token);
                        var item=images.RootElement.GetProperty("data")[0];
                        if(item.TryGetProperty("thumbnails",out var thumbnails))item=thumbnails[0];
                        if(item.GetProperty("state").GetString()=="Completed")art=await Image(item.GetProperty("imageUrl").GetString(),deadline.Token);
                        if(art is not null){card=card with {ArtworkKind=url.Contains("/icons?")?"Icon":"Thumbnail"};break;}
                    }
                    catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
                    catch { }
                }
                card=card with {Artwork=art,ArtworkState=art is null?"Unavailable":"Ready"};
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch { }
            if(cache.Count>=40)cache.Remove(cache.Keys.First());cache[place]=(DateTime.UtcNow,card);return card;
        }
        finally{gate.Release();}
    }
    readonly Dictionary<string,(DateTime At,string? Image)> avatars=[];
    public async Task<string?> Avatar(string userId,CancellationToken token)
    {
        if(!LaunchTargetResolver.IsPositiveId(userId))return null;
        if(avatars.TryGetValue(userId,out var cached)&&DateTime.UtcNow-cached.At<TimeSpan.FromMinutes(cached.Image is null?1:10))return cached.Image;
        string? image=null;
        try
        {
            using var json=await Get($"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false",token);
            var item=json.RootElement.GetProperty("data").EnumerateArray().Single();
            if(item.GetProperty("targetId").GetInt64().ToString()==userId&&item.GetProperty("state").GetString()=="Completed")image=await Image(item.GetProperty("imageUrl").GetString(),token);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch{}
        if(avatars.Count>=128)avatars.Remove(avatars.Keys.First());
        avatars[userId]=(DateTime.UtcNow,image);return image;
    }
    async Task<string?> Image(string? url,CancellationToken token)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.Host.EndsWith(".rbxcdn.com",StringComparison.OrdinalIgnoreCase)||uri.Port!=443)return null;
        using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
        string? type=response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if(type is not ("image/png" or "image/jpeg" or "image/webp")||response.Content.Headers.ContentLength is >524288)return null;
        await using var stream=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();byte[] buffer=new byte[8192];int read;
        while((read=await stream.ReadAsync(buffer,token))>0){if(bytes.Length+read>524288)return null;bytes.Write(buffer,0,read);}
        return "data:"+type+";base64,"+Convert.ToBase64String(bytes.ToArray());
    }
    public async Task<PublicServerDto[]> Servers(string place,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var servers=new List<PublicServerDto>();string? cursor=null;
        try
        {
            for(int page=0;page<3;page++)
            {
                using var json=await Get($"https://games.roblox.com/v1/games/{place}/servers/Public?sortOrder=Asc&excludeFullGames=true&limit=100"+(cursor is null?"":"&cursor="+Uri.EscapeDataString(cursor)),deadline.Token);
                foreach(var item in json.RootElement.GetProperty("data").EnumerateArray())
                {
                    string id=item.GetProperty("id").GetString()??"";int playing=item.GetProperty("playing").GetInt32(),max=item.GetProperty("maxPlayers").GetInt32();
                    if(Guid.TryParseExact(id,"D",out var job)&&job!=Guid.Empty&&playing>=0&&max>playing)
                        servers.Add(new(job.ToString("D"),playing,max,item.TryGetProperty("ping",out var ping)&&ping.TryGetInt32(out int value)&&value>=0?value:null));
                }
                cursor=json.RootElement.TryGetProperty("nextPageCursor",out var next)?next.GetString():null;
                if(string.IsNullOrEmpty(cursor))break;
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch { }
        return servers.DistinctBy(s=>s.Id).ToArray();
    }
    public static string?[] Assign(PublicServerDto[] servers,int count)
    {
        var result=new string?[count];int slot=0;
        foreach(var server in servers.DistinctBy(s=>s.Id).Where(s=>s.Playing>=0&&s.MaxPlayers>s.Playing).OrderByDescending(s=>s.MaxPlayers-s.Playing).ThenBy(s=>s.Ping??int.MaxValue).ThenBy(s=>s.Id,StringComparer.Ordinal))
            for(int free=server.MaxPlayers-server.Playing;free>0&&slot<count;free--)result[slot++]=server.Id;
        return result;
    }
    public async Task<GameCardDto[]> Recent(CancellationToken token)
    {
        await gate.WaitAsync(token);try{return Read();}finally{gate.Release();}
    }
    GameCardDto[] Read()
    {
        AccountStorageSecurity.ValidatePath(HistoryPath);if(!File.Exists(HistoryPath))return [];
        if(new FileInfo(HistoryPath).Length>15*1024*1024)throw new IOException("Recent games could not be read.");
        return (JsonSerializer.Deserialize<GameCardDto[]>(File.ReadAllText(HistoryPath))??[]).Where(x=>LaunchTargetResolver.IsPositiveId(x.PlaceId)).DistinctBy(x=>x.PlaceId).Take(20).ToArray();
    }
    public async Task<GameCardDto[]> Remember(GameCardDto card,CancellationToken token)=>await Update(card,null,false,token);
    public async Task<GameCardDto[]> Remove(string? place,bool clear,CancellationToken token)=>await Update(null,place,clear,token);
    async Task<GameCardDto[]> Update(GameCardDto? card,string? remove,bool clear,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            AccountStorageSecurity.EnsurePrivateDirectory(root);
            string leasePath=HistoryPath+".lock";AccountStorageSecurity.ValidatePath(leasePath);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(5));
            FileStream? lease=null;
            while(lease is null)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try{lease=new FileStream(leasePath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                catch(IOException){await Task.Delay(50,deadline.Token);}
            }
            using var heldLease=lease;
            var rows=clear?[]:Read().Where(x=>x.PlaceId!=(card?.PlaceId??remove));
            var next=(card is null?rows:new[]{card}.Concat(rows)).Take(20).ToArray();
            AccountStorageSecurity.EnsurePrivateDirectory(root);string temporary=HistoryPath+".tmp-"+Guid.NewGuid().ToString("N");
            try{File.WriteAllText(temporary,JsonSerializer.Serialize(next));AccountStorageSecurity.EnsurePrivateFile(temporary);AccountStorageSecurity.ValidatePath(HistoryPath);File.Move(temporary,HistoryPath,true);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return next;
        }
        finally{gate.Release();}
    }
    public void Dispose()=>http.Dispose();
}
