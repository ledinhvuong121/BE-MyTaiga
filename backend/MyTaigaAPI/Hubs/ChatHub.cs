using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyTaigaAPI.Hubs;

[Authorize]
public class ChatHub(MyTaigaAPI.Services.ChatPresence presence):Hub
{
    public static string Group(long userId,int version)=>$"chat:{userId}:v{version}";
    public override async Task OnConnectedAsync()
    {
        var userId=long.Parse(Context.User!.FindFirst("sub")!.Value);
        var version=int.Parse(Context.User.FindFirst("token_version")!.Value);
        await Groups.AddToGroupAsync(Context.ConnectionId,Group(userId,version));
        var expires=long.TryParse(Context.User.FindFirst("exp")?.Value,out var exp)?DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime:DateTime.UtcNow.AddMinutes(1);
        presence.Connect(Context.ConnectionId,userId,version,expires);
        await base.OnConnectedAsync();
    }
    public override System.Threading.Tasks.Task OnDisconnectedAsync(Exception? exception)
    {presence.Disconnect(Context.ConnectionId);return base.OnDisconnectedAsync(exception);}
    // No client method may join an arbitrary user/project group.
}
