using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Projects;
using MyTaigaAPI.Entities;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
public class BoardController(ScrumbanDbContext db) : ControllerBase
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
        if(!await db.ProjectMembers.AnyAsync(m=>m.ProjectId==projectId&&m.UserId==UserId,ct))return 403;
        return write&&project.Status!="ACTIVE"?409:200;
    }
    private ObjectResult Denied(int code)=>Reply(code,code switch
    {400=>"Mã dự án không hợp lệ.",403=>"Bạn không thuộc dự án này.",404=>"Không tìm thấy dự án.",_=>"Dự án đã đóng."});

    [HttpGet("api/v1/projects/{projectId:long}/board")]
    public async Task<IActionResult> Get(long projectId,
        [FromQuery,Range(1,long.MaxValue)]long? sprintId=null,
        [FromQuery,Range(1,long.MaxValue)]long? assigneeId=null,
        [FromQuery,StringLength(255)]string? keyword=null,
        [FromQuery,Range(1,200)]int pageSize=50,
        [FromQuery,Range(1,int.MaxValue)]int page=1,CancellationToken ct=default)
    {
        var access=await Access(projectId,false,ct);if(access!=200)return Denied(access);
        if(sprintId.HasValue&&!await db.Sprints.AnyAsync(s=>s.SprintId==sprintId&&s.ProjectId==projectId,ct))return Reply(404,"Sprint không thuộc dự án.");
        if(assigneeId.HasValue&&!await db.ProjectMembers.AnyAsync(m=>m.UserId==assigneeId&&m.ProjectId==projectId,ct))return Reply(404,"Người thực hiện không thuộc dự án.");
        var skip=((long)page-1)*pageSize;if(skip>int.MaxValue)return Reply(400,"Trang vượt phạm vi hỗ trợ.");
        var columns=await db.BoardColumns.AsNoTracking().Where(c=>c.ProjectId==projectId).OrderBy(c=>c.Position).ThenBy(c=>c.BoardColumnId).ToListAsync(ct);
        var counts=await db.Tasks.Where(t=>t.ProjectId==projectId).GroupBy(t=>t.Status).Select(g=>new{status=g.Key,count=g.Count()}).ToDictionaryAsync(g=>g.status,g=>g.count,ct);
        var query=db.Tasks.AsNoTracking().Where(t=>t.ProjectId==projectId);
        if(sprintId.HasValue)query=query.Where(t=>t.SprintId==sprintId);
        if(assigneeId.HasValue)query=query.Where(t=>t.AssigneeId==assigneeId);
        if(!string.IsNullOrWhiteSpace(keyword))query=query.Where(t=>t.Title.Contains(keyword.Trim()));
        var result=new List<object>();
        foreach(var column in columns)
        {
            var scoped=query.Where(t=>t.Status==column.ColumnName);
            var matching=await scoped.CountAsync(ct);
            var cards=await scoped.OrderBy(t=>t.Position).ThenBy(t=>t.TaskId).Skip((int)skip).Take(pageSize)
                .Select(t=>new{t.TaskId,t.Title,t.Status,t.Position,t.Priority,t.ProgressPercent,t.AssigneeId,t.SprintId,t.EpicId,t.ParentTaskId,t.Deadline})
                .ToListAsync(ct);
            var total=counts.GetValueOrDefault(column.ColumnName);
            result.Add(new{column.BoardColumnId,column.ColumnName,column.Position,column.WipLimit,totalCount=total,
                matchingCount=matching,isOverloaded=column.WipLimit.HasValue&&total>column.WipLimit,
                hasMore=skip+cards.Count<matching,
                tasks=cards.Select(t=>new{t.TaskId,t.Title,t.Status,t.Position,t.Priority,t.ProgressPercent,t.AssigneeId,t.SprintId,t.EpicId,t.ParentTaskId,
                    deadline=t.Deadline.HasValue?DateTime.SpecifyKind(t.Deadline.Value,DateTimeKind.Utc):(DateTime?)null})});
        }
        return Reply(200,"Lấy Board thành công.",new{projectId,columns=result,page,pageSize});
    }

    [HttpPatch("api/v1/tasks/{taskId:long}/move")]
    public async Task<IActionResult> Move(long taskId,MoveTaskRequest request,CancellationToken ct)
    {
        if(taskId<=0)return Reply(400,"Mã Task không hợp lệ.");
        var projectId=await db.Tasks.Where(t=>t.TaskId==taskId).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct);
        if(!projectId.HasValue)return Reply(404,"Không tìm thấy Task.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var access=await Access(projectId.Value,true,ct);if(access!=200)return Denied(access);
        var item=await db.Tasks.SingleOrDefaultAsync(t=>t.TaskId==taskId,ct);if(item is null)return Reply(404,"Không tìm thấy Task.");
        if(item.SprintId.HasValue&&await db.Sprints.AnyAsync(s=>s.SprintId==item.SprintId&&s.Status=="CLOSED",ct))return Reply(409,"Không di chuyển Task của Sprint đã đóng.");
        var column=await db.BoardColumns.SingleOrDefaultAsync(c=>c.ProjectId==projectId&&c.ColumnName==request.ToStatus,ct);
        if(column is null)return Reply(400,"Cột đích không thuộc Board.");
        if(item.AssigneeId.HasValue&&request.ToStatus!="DONE"&&!await db.ProjectMembers.AnyAsync(m=>m.ProjectId==projectId&&m.UserId==item.AssigneeId&&m.User.Status==1,ct))
            return Reply(409,"Cần giao lại Task cho thành viên hoạt động trước khi tiếp tục.");
        if(request.ToStatus=="DONE"&&await db.Tasks.AnyAsync(t=>t.ParentTaskId==taskId&&t.Status!="DONE",ct))
            return Reply(409,"Cần hoàn thành Subtask trước Task cha.");
        if(request.ToStatus!="DONE"&&item.ParentTaskId.HasValue&&await db.Tasks.AnyAsync(t=>t.TaskId==item.ParentTaskId&&t.Status=="DONE",ct))
            return Reply(409,"Cần mở lại Task cha trước Subtask.");
        var destination=await db.Tasks.Where(t=>t.ProjectId==projectId&&t.Status==request.ToStatus&&t.TaskId!=taskId)
            .OrderBy(t=>t.Position).ThenBy(t=>t.TaskId).ToListAsync(ct);
        if(request.NewPosition>destination.Count)return Reply(400,"Vị trí vượt số thẻ trong cột đích (đánh số từ 0).");
        var from=item.Status;
        if(from!=request.ToStatus&&column.WipLimit.HasValue&&destination.Count>=column.WipLimit)return Reply(409,"Cột đích đã đạt giới hạn WIP.");
        if(from!=request.ToStatus)
        {
            var source=await db.Tasks.Where(t=>t.ProjectId==projectId&&t.Status==from&&t.TaskId!=taskId)
                .OrderBy(t=>t.Position).ThenBy(t=>t.TaskId).ToListAsync(ct);
            for(var index=0;index<source.Count;index++)source[index].Position=index;
        }
        destination.Insert(request.NewPosition,item);
        for(var index=0;index<destination.Count;index++)destination[index].Position=index;
        item.Status=request.ToStatus;item.UpdatedAt=DateTime.UtcNow;
        db.ActivityLogs.Add(new ActivityLog
        {ProjectId=projectId,UserId=UserId,EntityType="TASK",EntityId=taskId,Action="TASK_MOVED",
            Description=$"{from} -> {request.ToStatus}; position={request.NewPosition}",CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync(ct);await db.Entry(item).ReloadAsync(ct);await tx.CommitAsync(ct);
        return Reply(200,"Di chuyển Task thành công.",new{item.TaskId,fromStatus=from,toStatus=item.Status,item.Position,item.ProgressPercent,
            startedAt=item.StartedAt.HasValue?DateTime.SpecifyKind(item.StartedAt.Value,DateTimeKind.Utc):(DateTime?)null,
            completedAt=item.CompletedAt.HasValue?DateTime.SpecifyKind(item.CompletedAt.Value,DateTimeKind.Utc):(DateTime?)null});
    }
}
