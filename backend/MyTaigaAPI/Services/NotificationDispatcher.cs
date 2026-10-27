using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Hubs;

namespace MyTaigaAPI.Services;

public class NotificationDispatcher(IServiceScopeFactory scopes,IHubContext<NotificationsHub> hub,IHubContext<ChatHub> chat,ILogger<NotificationDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ScrumbanDbContext>();
                var batch=await db.Notifications.Where(n=>n.PushedAt==null).OrderBy(n=>n.NotificationId).Take(100).ToListAsync(stoppingToken);
                foreach(var n in batch)
                {
                    var recipient=await db.Users.Where(u=>u.UserId==n.UserId&&u.Status==1).Select(u=>new{u.UserId,u.TokenVersion}).SingleOrDefaultAsync(stoppingToken);
                    if(recipient is not null&&await NotificationScope.Visible(db,n.UserId).AnyAsync(v=>v.NotificationId==n.NotificationId,stoppingToken))
                    {
                        var data=await scope.ServiceProvider.GetRequiredService<ResponseEnricher>().EnrichAsync(NotificationScope.Details(n),recipient.UserId,stoppingToken);
                        var target=hub.Clients.Group(NotificationsHub.Group(recipient.UserId,recipient.TokenVersion));
                        await target.SendAsync("notificationReceived",data,stoppingToken);
                        await target.SendAsync("notification.new",data,stoppingToken);
                        await chat.Clients.Group(ChatHub.Group(recipient.UserId,recipient.TokenVersion)).SendAsync("notification.new",data,stoppingToken);
                    }
                    n.PushedAt=DateTime.UtcNow;
                }
                if(batch.Count>0)await db.SaveChangesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Notification dispatch failed; will retry.");await Task.Delay(TimeSpan.FromSeconds(2),stoppingToken);}
        }
    }
}
