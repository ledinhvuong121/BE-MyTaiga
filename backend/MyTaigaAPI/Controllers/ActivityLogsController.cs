using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
public class ActivityLogsController(ScrumbanDbContext db):ControllerBase
{
    [HttpGet("api/v1/projects/{projectId:long}/activity-logs")]
    public async Task<IActionResult> List(long projectId,[FromQuery,StringLength(50)]string? entityType=null,
        [FromQuery,StringLength(50)]string? action=null,[FromQuery,Range(1,int.MaxValue)]int page=1,
        [FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        if(projectId<=0)return BadRequest(new{success=false,message="Mã dự án không hợp lệ."});
        if(!await db.Projects.AnyAsync(p=>p.ProjectId==projectId&&p.Status!="DELETED",ct))return NotFound();
        var userId=long.Parse(User.FindFirst("sub")!.Value);
        if(!await db.ProjectMembers.AnyAsync(m=>m.ProjectId==projectId&&m.UserId==userId,ct))return StatusCode(403);
        var query=db.ActivityLogs.AsNoTracking().Where(l=>l.ProjectId==projectId);
        if(!string.IsNullOrWhiteSpace(entityType))query=query.Where(l=>l.EntityType==entityType);
        if(!string.IsNullOrWhiteSpace(action))query=query.Where(l=>l.Action==action);
        var total=await query.CountAsync(ct);var skip=((long)page-1)*pageSize;if(skip>int.MaxValue)return BadRequest();
        var rows=await query.OrderByDescending(l=>l.CreatedAt).ThenByDescending(l=>l.ActivityLogId).Skip((int)skip).Take(pageSize)
            .Select(l=>new{l.ActivityLogId,l.UserId,l.Action,l.EntityType,l.EntityId,l.Description,l.CreatedAt}).ToListAsync(ct);
        return Ok(new{success=true,message="Lấy lịch sử hoạt động thành công.",data=new{items=rows.Select(l=>new
        {l.ActivityLogId,l.UserId,l.Action,l.EntityType,l.EntityId,l.Description,createdAt=DateTime.SpecifyKind(l.CreatedAt,DateTimeKind.Utc)}),total,page,pageSize}});
    }
}
