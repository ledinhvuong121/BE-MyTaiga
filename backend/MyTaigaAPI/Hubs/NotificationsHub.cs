using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyTaigaAPI.Hubs;

[Authorize]
public class NotificationsHub:Hub
{
    public static string Group(long userId,int version)=>$"notifications:{userId}:v{version}";
    public override async Task OnConnectedAsync()
    {
        var userId=long.Parse(Context.User!.FindFirst("sub")!.Value);
        var version=int.Parse(Context.User.FindFirst("token_version")!.Value);
        await Groups.AddToGroupAsync(Context.ConnectionId,Group(userId,version));
        await base.OnConnectedAsync();
    }
    // No client method may join an arbitrary user/project group.
}
