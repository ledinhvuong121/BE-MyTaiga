using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Projects;
using MyTaigaAPI.Entities;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
public class WipController(ScrumbanDbContext db) : ControllerBase
{
    private long UserId=>long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code,string message,object? data=null)=>StatusCode(code,new{success=code<400,message,data});
    private async Task<int> Access(long projectId,bool write,CancellationToken ct)
    {
        if(projectId<=0)return 400;
        var project=write?await db.Projects.FromSqlInterpolated($"""
            SELECT * FROM dbo.Project WITH(UPDLOCK,HOLDLOCK) WHERE ProjectId={projectId}
            """).SingleOrDefaultAsync(ct):await db.Projects.AsNoTracking().SingleOrDefaultAsync(p=>p.ProjectId==projectId,ct);
        if(project is null||project.Status=="DELETED")return 404;
        var role=await db.ProjectMembers.Where(m=>m.ProjectId==projectId&&m.UserId==UserId).Select(m=>m.Role.RoleName).SingleOrDefaultAsync(ct);
        if(role is null||(write&&role!="MANAGER"))return 403;
        return write&&project.Status!="ACTIVE"?409:200;
    }
    private ObjectResult Denied(int code)=>Reply(code,code switch
    {400=>"Mã dự án không hợp lệ.",403=>"Bạn không có quyền trong dự án này.",404=>"Không tìm thấy dự án.",_=>"Dự án đã đóng."});

    [HttpGet("api/v1/projects/{projectId:long}/wip")]
    public Task<IActionResult> Get(long projectId,CancellationToken ct)=>Read(projectId,false,ct);
    [HttpGet("api/v1/projects/{projectId:long}/wip/alerts")]
    public Task<IActionResult> Alerts(long projectId,CancellationToken ct)=>Read(projectId,true,ct);
    private async Task<IActionResult> Read(long projectId,bool alerts,CancellationToken ct)
    {
        var access=await Access(projectId,false,ct);if(access!=200)return Denied(access);
        var rows=await db.BoardColumns.AsNoTracking().Where(c=>c.ProjectId==projectId).OrderBy(c=>c.Position).ThenBy(c=>c.BoardColumnId)
            .Select(c=>new{c.BoardColumnId,c.ColumnName,c.Position,c.WipLimit,count=c.Tasks.Count()}).ToListAsync(ct);
        var data=rows.Select(c=>new{c.BoardColumnId,c.ColumnName,c.Position,c.WipLimit,taskCount=c.count,
            isOverloaded=c.WipLimit.HasValue&&c.count>c.WipLimit,
            atCapacity=c.WipLimit.HasValue&&c.count>=c.WipLimit,
            excess=c.WipLimit.HasValue?Math.Max(0,c.count-c.WipLimit.Value):0});
        if(alerts)data=data.Where(c=>c.isOverloaded);
        return Reply(200,alerts?"Lấy cảnh báo WIP thành công.":"Lấy WIP thành công.",new{projectId,columns=data,asOfUtc=DateTime.UtcNow});
    }

    [HttpPut("api/v1/projects/{projectId:long}/board/columns/{columnId:long}/wip-limit")]
    public Task<IActionResult> Set(long projectId,long columnId,WipLimitRequest request,CancellationToken ct)=>Change(projectId,columnId,request.WipLimit,ct);
    [HttpDelete("api/v1/projects/{projectId:long}/board/columns/{columnId:long}/wip-limit")]
    public Task<IActionResult> Clear(long projectId,long columnId,CancellationToken ct)=>Change(projectId,columnId,null,ct);
    private async Task<IActionResult> Change(long projectId,long columnId,int? limit,CancellationToken ct)
    {
        if(columnId<=0)return Reply(400,"Mã cột không hợp lệ.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var access=await Access(projectId,true,ct);if(access!=200)return Denied(access);
        var column=await db.BoardColumns.SingleOrDefaultAsync(c=>c.ProjectId==projectId&&c.BoardColumnId==columnId,ct);
        if(column is null)return Reply(404,"Cột không thuộc dự án này.");
        var count=await db.Tasks.CountAsync(t=>t.ProjectId==projectId&&t.Status==column.ColumnName,ct);
        var changed=column.WipLimit!=limit;
        if(changed)
        {
            column.WipLimit=limit;
            db.ActivityLogs.Add(new ActivityLog
            {ProjectId=projectId,UserId=UserId,EntityId=columnId,EntityType="BOARD_COLUMN",Action=limit.HasValue?"WIP_LIMIT_SET":"WIP_LIMIT_REMOVED",
                Description=limit.HasValue?$"WIP={limit.Value}":"Bỏ giới hạn WIP.",CreatedAt=DateTime.UtcNow});
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Reply(200,"Cập nhật giới hạn WIP thành công.",new{column.BoardColumnId,column.ColumnName,wipLimit=limit,
            taskCount=count,isOverloaded=limit.HasValue&&count>limit,changed});
    }
}
