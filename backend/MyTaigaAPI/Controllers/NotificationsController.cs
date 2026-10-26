using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Services;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications")]
public class NotificationsController(ScrumbanDbContext db):ControllerBase
{
    private long UserId=>long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code,string message,object? data=null)=>StatusCode(code,new{success=code<400,message,data});
    [HttpGet]
    public async Task<IActionResult> List([FromQuery]bool unreadOnly=false,[FromQuery,Range(1,int.MaxValue)]int page=1,
        [FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        var query=NotificationScope.Visible(db,UserId).AsNoTracking();if(unreadOnly)query=query.Where(n=>!n.IsRead);
        var total=await query.CountAsync(ct);var skip=((long)page-1)*pageSize;if(skip>int.MaxValue)return Reply(400,"Trang vượt phạm vi hỗ trợ.");
        var rows=await query.OrderByDescending(n=>n.CreatedAt).ThenByDescending(n=>n.NotificationId).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return Reply(200,"Lấy thông báo thành công.",new{items=rows.Select(NotificationScope.Details),total,page,pageSize});
    }
    [HttpGet("unread-count")]
    public async Task<IActionResult> Count(CancellationToken ct)=>Reply(200,"Lấy số chưa đọc thành công.",new{unreadCount=await NotificationScope.Visible(db,UserId).CountAsync(n=>!n.IsRead,ct)});
    [HttpPatch("{notificationId:long}/read")]
    public async Task<IActionResult> Read(long notificationId,CancellationToken ct)
    {
        if(notificationId<=0)return Reply(400,"Mã thông báo không hợp lệ.");
        var query=NotificationScope.Visible(db,UserId).Where(n=>n.NotificationId==notificationId);
        await query.Where(n=>!n.IsRead).ExecuteUpdateAsync(set=>set.SetProperty(n=>n.IsRead,true).SetProperty(n=>n.ReadAt,DateTime.UtcNow),ct);
        var row=await query.AsNoTracking().SingleOrDefaultAsync(ct);
        if(row is null)return Reply(404,"Không tìm thấy thông báo của bạn.");
        return Reply(200,"Đánh dấu đã đọc thành công.",NotificationScope.Details(row));
    }
    [HttpPatch("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var now=DateTime.UtcNow;var count=await NotificationScope.Visible(db,UserId).Where(n=>!n.IsRead)
            .ExecuteUpdateAsync(set=>set.SetProperty(n=>n.IsRead,true).SetProperty(n=>n.ReadAt,now),ct);
        return Reply(200,"Đã đọc tất cả thông báo.",new{updated=count});
    }
}
