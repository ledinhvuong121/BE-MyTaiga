using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Projects;
using MyTaigaAPI.Entities;
using MyTaigaAPI.Services;
using WorkTask = MyTaigaAPI.Entities.Task;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
public class TasksController(ScrumbanDbContext db, SystemConfigService config) : ControllerBase
{
    private long UserId => long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code, string message, object? data = null) => StatusCode(code,new { success=code<400,message,data });
    private static DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value,DateTimeKind.Utc) : null;
    private static object Details(WorkTask t) => new
    {
        t.TaskId,t.ProjectId,t.SprintId,t.EpicId,t.ParentTaskId,t.AssigneeId,t.ReporterId,t.Title,t.Description,
        t.Priority,t.Status,t.StoryPoints,t.TagsJson,deadline=Utc(t.Deadline),t.ProgressPercent,t.Position,
        startedAt=Utc(t.StartedAt),completedAt=Utc(t.CompletedAt),createdAt=Utc(t.CreatedAt),updatedAt=Utc(t.UpdatedAt)
    };
    private async Task<int> Access(long projectId,bool write,bool manager,CancellationToken ct)
    {
        if(projectId<=0) return 400;
        var project=write ? await db.Projects.FromSqlInterpolated($"""
            SELECT * FROM dbo.Project WITH(UPDLOCK,HOLDLOCK) WHERE ProjectId={projectId}
            """).SingleOrDefaultAsync(ct) : await db.Projects.AsNoTracking().SingleOrDefaultAsync(p=>p.ProjectId==projectId,ct);
        if(project is null || project.Status=="DELETED") return 404;
        var role=await db.ProjectMembers.Where(m=>m.ProjectId==projectId&&m.UserId==UserId).Select(m=>m.Role.RoleName).SingleOrDefaultAsync(ct);
        if(role is null || (manager&&role!="MANAGER")) return 403;
        return write&&project.Status!="ACTIVE" ? 409 : 200;
    }
    private ObjectResult Denied(int code)=>Reply(code,code switch
    {400=>"Mã dự án không hợp lệ.",403=>"Bạn không có quyền trong dự án này.",404=>"Không tìm thấy dự án.",_=>"Dự án đã đóng."});
    private async Task<bool> ValidAssignee(long projectId,long? id,CancellationToken ct)=>!id.HasValue||await db.ProjectMembers.AnyAsync(m=>
        m.ProjectId==projectId&&m.UserId==id&&m.User.Status==1,ct);

    [HttpGet("api/v1/projects/{projectId:long}/tasks")]
    public async Task<IActionResult> List(long projectId,
        [FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,
        [FromQuery,Range(1,long.MaxValue)]long? sprintId=null,[FromQuery,Range(1,long.MaxValue)]long? assigneeId=null,
        [FromQuery,StringLength(255)]string? keyword=null,[FromQuery]string? status=null,CancellationToken ct=default)
    {
        var access=await Access(projectId,false,false,ct); if(access!=200)return Denied(access);
        if(sprintId.HasValue&&!await db.Sprints.AnyAsync(s=>s.SprintId==sprintId&&s.ProjectId==projectId,ct))return Reply(404,"Sprint không thuộc dự án.");
        if(assigneeId.HasValue&&!await db.ProjectMembers.AnyAsync(m=>m.ProjectId==projectId&&m.UserId==assigneeId,ct))return Reply(404,"Người thực hiện không thuộc dự án.");
        if(status is not null&&!await db.BoardColumns.AnyAsync(c=>c.ProjectId==projectId&&c.ColumnName==status,ct))return Reply(400,"Trạng thái không thuộc Board.");
        var query=db.Tasks.AsNoTracking().Where(t=>t.ProjectId==projectId);
        if(sprintId.HasValue)query=query.Where(t=>t.SprintId==sprintId);
        if(assigneeId.HasValue)query=query.Where(t=>t.AssigneeId==assigneeId);
        if(status is not null)query=query.Where(t=>t.Status==status);
        if(!string.IsNullOrWhiteSpace(keyword))query=query.Where(t=>t.Title.Contains(keyword.Trim()));
        var total=await query.CountAsync(ct);var skip=((long)page-1)*pageSize;
        if(skip>int.MaxValue)return Reply(400,"Trang vượt phạm vi hỗ trợ.");
        var items=await query.OrderBy(t=>t.Status).ThenBy(t=>t.Position).ThenBy(t=>t.TaskId).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return Reply(200,"Lấy danh sách Task thành công.",new{items=items.Select(Details),total,page,pageSize});
    }

    [HttpGet("api/v1/tasks/{taskId:long}")]
    public async Task<IActionResult> Get(long taskId,CancellationToken ct)
    {
        if(taskId<=0)return Reply(400,"Mã Task không hợp lệ.");
        var item=await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t=>t.TaskId==taskId,ct);
        if(item is null)return Reply(404,"Không tìm thấy Task.");
        var access=await Access(item.ProjectId,false,false,ct);if(access!=200)return Denied(access);
        var children=await db.Tasks.AsNoTracking().Where(t=>t.ParentTaskId==taskId).OrderBy(t=>t.TaskId).ToListAsync(ct);
        return Reply(200,"Lấy chi tiết Task thành công.",new{task=Details(item),subtasks=children.Select(Details)});
    }

    [HttpPost("api/v1/projects/{projectId:long}/tasks")]
    public Task<IActionResult> Create(long projectId,TaskCreateRequest request,CancellationToken ct)=>CreateCore(projectId,request,ct);

    [HttpPost("api/v1/tasks/{taskId:long}/subtasks")]
    public async Task<IActionResult> Subtask(long taskId,TaskCreateRequest request,CancellationToken ct)
    {
        if(taskId<=0)return Reply(400,"Mã Task không hợp lệ.");
        var projectId=await db.Tasks.Where(t=>t.TaskId==taskId).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct);
        if(!projectId.HasValue)return Reply(404,"Không tìm thấy Task cha.");
        if(request.ParentTaskId.HasValue&&request.ParentTaskId!=taskId)return Reply(400,"Task cha không khớp URL.");
        request.ParentTaskId=taskId;return await CreateCore(projectId.Value,request,ct);
    }

    private async Task<IActionResult> CreateCore(long projectId,TaskCreateRequest request,CancellationToken ct)
    {
        if(request.Tags is not null&&(request.Tags.Length>20||request.Tags.Any(x=>string.IsNullOrWhiteSpace(x)||x.Trim().Length>50))||string.IsNullOrWhiteSpace(request.Title))return Reply(400,"Tiêu đề không được trống.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var advanced=request.ParentTaskId.HasValue||request.AssigneeId.HasValue||request.Deadline.HasValue||request.Priority!="MEDIUM";
        var access=await Access(projectId,true,advanced,ct);if(access!=200)return Denied(access);
        if(request.ParentTaskId.HasValue)
        {
            var parent=await db.Tasks.SingleOrDefaultAsync(t=>t.TaskId==request.ParentTaskId&&t.ProjectId==projectId,ct);
            if(parent is null)return Reply(404,"Task cha không thuộc dự án.");
            if(parent.Status=="DONE")return Reply(409,"Không tạo Subtask cho Task đã hoàn thành.");
            if((request.SprintId.HasValue&&request.SprintId!=parent.SprintId)||(request.EpicId.HasValue&&request.EpicId!=parent.EpicId))
                return Reply(400,"Subtask phải cùng Sprint và Epic với Task cha.");
            request.SprintId=parent.SprintId;request.EpicId=parent.EpicId;
            var parentId=parent.ParentTaskId;var depth=1;
            while(parentId.HasValue)
            {
                if(++depth>=32)return Reply(409,"Độ sâu Subtask tối đa 32 cấp.");
                parentId=await db.Tasks.Where(t=>t.TaskId==parentId).Select(t=>t.ParentTaskId).SingleOrDefaultAsync(ct);
            }
        }
        if(request.SprintId.HasValue&&!await db.Sprints.AnyAsync(s=>s.SprintId==request.SprintId&&s.ProjectId==projectId&&s.Status!="CLOSED",ct))
            return Reply(409,"Sprint không thuộc dự án hoặc đã đóng.");
        if(request.EpicId.HasValue&&!await db.Epics.AnyAsync(e=>e.EpicId==request.EpicId&&e.ProjectId==projectId,ct))return Reply(404,"Epic không thuộc dự án.");
        if(!await ValidAssignee(projectId,request.AssigneeId,ct))return Reply(409,"Người thực hiện phải là thành viên đang hoạt động.");
        var column=await db.BoardColumns.SingleOrDefaultAsync(c=>c.ProjectId==projectId&&c.ColumnName=="TODO",ct);
        if(column is null)return Reply(503,"Board chưa có cột TODO.");
        if(column.WipLimit.HasValue&&await db.Tasks.CountAsync(t=>t.ProjectId==projectId&&t.Status=="TODO",ct)>=column.WipLimit)
            return Reply(409,"Cột TODO đã đạt giới hạn WIP.");
        var maxPosition=await db.Tasks.Where(t=>t.ProjectId==projectId&&t.Status=="TODO").Select(t=>(int?)t.Position).MaxAsync(ct)??0;
        if(maxPosition==int.MaxValue)return Reply(409,"Vị trí cột vượt phạm vi hỗ trợ.");
        var item=new WorkTask
        {
            ProjectId=projectId,ReporterId=UserId,Title=request.Title.Trim(),TagsJson=System.Text.Json.JsonSerializer.Serialize((request.Tags??[]).Select(x=>x.Trim()).Distinct()),StoryPoints=request.StoryPoints,Description=request.Description?.Trim(),
            SprintId=request.SprintId,EpicId=request.EpicId,ParentTaskId=request.ParentTaskId,AssigneeId=request.AssigneeId,
            Priority=request.Priority,Deadline=request.Deadline?.UtcDateTime,Status="TODO",Position=maxPosition+1,CreatedAt=DateTime.UtcNow
        };
        db.Tasks.Add(item);await db.SaveChangesAsync(ct);await db.Entry(item).ReloadAsync(ct);
        Log(item,request.ParentTaskId.HasValue?"SUBTASK_CREATED":"TASK_CREATED");
        NotifyAssignee(item);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        Response.Headers.Location=$"/api/v1/tasks/{item.TaskId}";
        return Reply(201,"Tạo Task thành công.",Details(item));
    }

    [HttpPut("api/v1/tasks/{taskId:long}")]
    public Task<IActionResult> Update(long taskId,TaskUpdateRequest request,CancellationToken ct)=>Change(taskId,"UPDATE",request,null,null,null,null,ct);
    [HttpDelete("api/v1/tasks/{taskId:long}")]
    public Task<IActionResult> Delete(long taskId,CancellationToken ct)=>Change(taskId,"DELETE",null,null,null,null,null,ct);
    [HttpPatch("api/v1/tasks/{taskId:long}/assignee")]
    public Task<IActionResult> Assign(long taskId,AssignTaskRequest request,CancellationToken ct)=>Change(taskId,"ASSIGN",null,request.AssigneeId,null,null,null,ct);
    [HttpPatch("api/v1/tasks/{taskId:long}/priority")]
    public Task<IActionResult> Priority(long taskId,TaskPriorityRequest request,CancellationToken ct)=>Change(taskId,"PRIORITY",null,null,request.Priority,null,null,ct);
    [HttpPatch("api/v1/tasks/{taskId:long}/deadline")]
    public Task<IActionResult> Deadline(long taskId,TaskDeadlineRequest request,CancellationToken ct)=>Change(taskId,"DEADLINE",null,null,null,request.Deadline,null,ct);
    [HttpPatch("api/v1/tasks/{taskId:long}/progress")]
    public Task<IActionResult> Progress(long taskId,TaskProgressRequest request,CancellationToken ct)=>Change(taskId,"PROGRESS",null,null,null,null,request.ProgressPercent,ct);

    private async Task<IActionResult> Change(long taskId,string operation,TaskUpdateRequest? request,long? assignee,string? priority,
        DateTimeOffset? deadline,int? progress,CancellationToken ct)
    {
        if(taskId<=0)return Reply(400,"Mã Task không hợp lệ.");
        if(request is not null&&(request.Tags is not null&&(request.Tags.Length>20||request.Tags.Any(x=>string.IsNullOrWhiteSpace(x)||x.Trim().Length>50))||string.IsNullOrWhiteSpace(request.Title)))return Reply(400,"Tiêu đề không được trống.");
        var projectId=await db.Tasks.Where(t=>t.TaskId==taskId).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct);
        if(!projectId.HasValue)return Reply(404,"Không tìm thấy Task.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var access=await Access(projectId.Value,true,operation is "ASSIGN" or "PRIORITY" or "DEADLINE",ct);if(access!=200)return Denied(access);
        var item=await db.Tasks.SingleOrDefaultAsync(t=>t.TaskId==taskId,ct);if(item is null)return Reply(404,"Không tìm thấy Task.");
        if(item.SprintId.HasValue&&await db.Sprints.AnyAsync(s=>s.SprintId==item.SprintId&&s.Status=="CLOSED",ct))return Reply(409,"Không thay đổi Task thuộc Sprint đã đóng.");
        if(operation=="DELETE")
        {
            if(await db.Tasks.AnyAsync(t=>t.ParentTaskId==taskId,ct)||await db.Bugs.AnyAsync(b=>b.TaskId==taskId,ct)||
                await db.Comments.AnyAsync(c=>c.TaskId==taskId,ct)||await db.Attachments.AnyAsync(a=>a.TaskId==taskId,ct))
                return Reply(409,"Task còn Subtask/Bug/bình luận/tệp đính kèm; không thể xóa.");
            db.Tasks.Remove(item);
        }
        else if(operation=="UPDATE"){item.Title=request!.Title.Trim();item.Description=request.Description?.Trim();if(request.StoryPointsSpecified)item.StoryPoints=request.StoryPoints;if(request.Tags is not null)item.TagsJson=System.Text.Json.JsonSerializer.Serialize((request.Tags??[]).Select(x=>x.Trim()).Distinct());}
        else if(operation=="ASSIGN")
        {
            if(!await ValidAssignee(projectId.Value,assignee,ct))return Reply(409,"Người thực hiện phải là thành viên đang hoạt động.");
            if(item.AssigneeId==assignee)return Reply(409,"Phân công không thay đổi.");item.AssigneeId=assignee;
        }
        else if(operation=="PRIORITY")item.Priority=priority!;
        else if(operation=="DEADLINE")item.Deadline=deadline?.UtcDateTime;
        else
        {
            if(item.Status=="DONE")return Reply(409,"Task DONE có tiến độ 100%; cần mở lại trên Board trước.");
            item.ProgressPercent=progress!.Value;
        }
        item.UpdatedAt=DateTime.UtcNow;Log(item,"TASK_"+operation);
        if(operation=="ASSIGN")NotifyAssignee(item);
        await db.SaveChangesAsync(ct);if(operation!="DELETE")await db.Entry(item).ReloadAsync(ct);await tx.CommitAsync(ct);
        return Reply(200,"Thao tác Task thành công.",operation=="DELETE"?null:Details(item));
    }

    [HttpGet("api/v1/projects/{projectId:long}/tasks/overdue")]
    public Task<IActionResult> Overdue(long projectId,[FromQuery,Range(1,int.MaxValue)]int page=1,
        [FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)=>Alerts(projectId,true,page,pageSize,ct);
    [HttpGet("api/v1/projects/{projectId:long}/tasks/deadline-alerts")]
    public Task<IActionResult> DeadlineAlerts(long projectId,[FromQuery,Range(1,int.MaxValue)]int page=1,
        [FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)=>Alerts(projectId,false,page,pageSize,ct);
    private async Task<IActionResult> Alerts(long projectId,bool overdueOnly,int page,int pageSize,CancellationToken ct)
    {
        var access=await Access(projectId,false,true,ct);if(access!=200)return Denied(access);
        var hours=await config.GetPositiveIntAsync("DEADLINE_WARNING_HOURS",ct);
        if(hours>87600)return Reply(503,"Cấu hình khoảng cảnh báo quá lớn.");
        var now=DateTime.UtcNow;var limit=overdueOnly?now:now.AddHours(hours);
        var query=db.Tasks.AsNoTracking().Where(t=>t.ProjectId==projectId&&t.Status!="DONE"&&t.Deadline.HasValue&&t.Deadline<=limit);
        var total=await query.CountAsync(ct);var skip=((long)page-1)*pageSize;if(skip>int.MaxValue)return Reply(400,"Trang vượt phạm vi hỗ trợ.");
        var rows=await query.OrderBy(t=>t.Deadline).ThenBy(t=>t.TaskId).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return Reply(200,"Lấy cảnh báo deadline thành công.",new{items=rows.Select(t=>new{task=Details(t),alert=t.Deadline<=now?"OVERDUE":"DUE_SOON"}),total,page,pageSize,warningHours=hours,asOfUtc=now});
    }
    private void Log(WorkTask task,string action)=>db.ActivityLogs.Add(new ActivityLog
    {ProjectId=task.ProjectId,UserId=UserId,EntityType="TASK",EntityId=task.TaskId,Action=action,Description=action,CreatedAt=DateTime.UtcNow});
    private void NotifyAssignee(WorkTask task)
    {
        if(task.AssigneeId.HasValue&&task.AssigneeId!=UserId)db.Notifications.Add(new Notification
        {UserId=task.AssigneeId.Value,Type="TASK_ASSIGNED",Title="Bạn được giao Task",Content=task.Title,RefType="TASK",RefId=task.TaskId,CreatedAt=DateTime.UtcNow});
    }
}
