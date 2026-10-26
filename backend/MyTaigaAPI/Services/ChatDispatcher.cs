using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Hubs;
namespace MyTaigaAPI.Services;
public class ChatDispatcher(IServiceScopeFactory scopes,IHubContext<ChatHub> hub,ILogger<ChatDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ScrumbanDbContext>();
                var rows=await db.Messages.Include(m=>m.MessageAttachments).Where(m=>m.PushedAt==null).OrderBy(m=>m.MessageId).Take(100).ToListAsync(ct);
                foreach(var m in rows)
                {
                    var users=await db.ChatParticipants.Where(p=>p.ChatRoomId==m.ChatRoomId&&p.IsActive&&p.User.Status==1).Select(p=>new{p.UserId,p.User.TokenVersion}).ToListAsync(ct);
                    foreach(var user in users)
                        if(await ChatScope.Visible(db,user.UserId).AnyAsync(r=>r.ChatRoomId==m.ChatRoomId,ct))
                            await hub.Clients.Group(ChatHub.Group(user.UserId,user.TokenVersion)).SendAsync("message.new",ChatScope.Details(m),ct);
                    m.PushedAt=DateTime.UtcNow;
                }
                if(rows.Count>0)await db.SaveChangesAsync(ct);
                await Task.Delay(1000,ct);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception e){logger.LogError(e,"Chat dispatch failed; retrying.");await Task.Delay(2000,ct);}
        }
    }
}
