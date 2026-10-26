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
public class BugsController(ScrumbanDbContext db) : ControllerBase
{
    private long UserId=>long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code,string message,object? data=null)=>StatusCode(code,new{success=code<400,message,data});
    private static DateTime? Utc(DateTime? value)=>value.HasValue?DateTime.SpecifyKind(value.Value,DateTimeKind.Utc):null;
    private static object Details(Bug b)=>new
    {b.BugId,b.TaskId,b.ReporterId,b.AssigneeId,b.Title,b.Description,b.Environment,b.Severity,b.Priority,b.Status,
        createdAt=Utc(b.CreatedAt),updatedAt=Utc(b.UpdatedAt),resolvedAt=Utc(b.ResolvedAt)};
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
    private Task<bool> ValidAssignee(long projectId,long? userId,CancellationToken ct)=>
        userId.HasValue?db.ProjectMembers.AnyAsync(m=>m.ProjectId==projectId&&m.UserId==userId&&m.User.Status==1,ct):System.Threading.Tasks.Task.FromResult(true);

    [HttpPost("api/v1/bugs")]
    public Task<IActionResult> Create(BugCreateRequest request,CancellationToken ct)=>CreateCore(request.TaskId,request,ct);
    [HttpPost("api/v1/tasks/{taskId:long}/bugs")]
    public Task<IActionResult> CreateForTask(long taskId,BugUpdateRequest request,CancellationToken ct)=>CreateCore(taskId,request,ct);
    private async Task<IActionResult> CreateCore(long taskId,BugUpdateRequest request,CancellationToken ct)
    {
        if(taskId<=0)return Reply(400,"Mã Task không hợp lệ.");
        if(string.IsNullOrWhiteSpace(request.Title)||string.IsNullOrWhiteSpace(request.Description))return Reply(400,"Tiêu đề và bước tái hiện không được trống.");
        var projectId=await db.Tasks.Where(t=>t.TaskId==taskId).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct);
        if(!projectId.HasValue)return Reply(404,"Không tìm thấy Task.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var access=await Access(projectId.Value,true,ct);if(access!=200)return Denied(access);
        if(!await db.Tasks.AnyAsync(t=>t.TaskId==taskId&&t.ProjectId==projectId,ct))return Reply(404,"Không tìm thấy Task.");
        var bug=new Bug{TaskId=taskId,ReporterId=UserId,Title=request.Title.Trim(),Description=request.Description.Trim(),
            Environment=request.Environment?.Trim(),Severity=request.Severity,Priority=request.Priority,Status="OPEN",CreatedAt=DateTime.UtcNow};
        db.Bugs.Add(bug);await db.SaveChangesAsync(ct);
        Log(projectId.Value,bug.BugId,"BUG_CREATED","Báo cáo Bug mới.");
        var managers=await db.ProjectMembers.Where(m=>m.ProjectId==projectId&&m.Role.RoleName=="MANAGER"&&m.UserId!=UserId&&m.User.Status==1)
            .Select(m=>m.UserId).ToListAsync(ct);
        foreach(var manager in managers)Notify(manager,bug,"BUG_CREATED","Bug mới được báo cáo");
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        Response.Headers.Location=$"/api/v1/bugs/{bug.BugId}";
        return Reply(201,"Tạo Bug thành công.",Details(bug));
    }

    [HttpGet("api/v1/projects/{projectId:long}/bugs")]
    public async Task<IActionResult> List(long projectId,[FromQuery]string? status=null,[FromQuery]string? severity=null,
        [FromQuery]string? priority=null,[FromQuery,Range(1,long.MaxValue)]long? assigneeId=null,
        [FromQuery,Range(1,long.MaxValue)]long? taskId=null,[FromQuery,StringLength(255)]string? keyword=null,
        [FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        var access=await Access(projectId,false,ct);if(access!=200)return Denied(access);
        if(status is not null&&!new[]{"OPEN","IN_PROGRESS","RESOLVED","CLOSED","REOPENED"}.Contains(status))return Reply(400,"Trạng thái không hợp lệ.");
        if(severity is not null&&!new[]{"TRIVIAL","MINOR","MAJOR","BLOCKER"}.Contains(severity))return Reply(400,"Severity không hợp lệ.");
        if(priority is not null&&!new[]{"LOW","MEDIUM","HIGH","CRITICAL"}.Contains(priority))return Reply(400,"Priority không hợp lệ.");
        if(assigneeId.HasValue&&!await db.ProjectMembers.AnyAsync(m=>m.ProjectId==projectId&&m.UserId==assigneeId,ct))return Reply(404,"Người thực hiện không thuộc dự án.");
        if(taskId.HasValue&&!await db.Tasks.AnyAsync(t=>t.ProjectId==projectId&&t.TaskId==taskId,ct))return Reply(404,"Task không thuộc dự án.");
        var query=db.Bugs.AsNoTracking().Where(b=>b.Task.ProjectId==projectId);
        if(status is not null)query=query.Where(b=>b.Status==status);
        if(severity is not null)query=query.Where(b=>b.Severity==severity);
        if(priority is not null)query=query.Where(b=>b.Priority==priority);
        if(assigneeId.HasValue)query=query.Where(b=>b.AssigneeId==assigneeId);
        if(taskId.HasValue)query=query.Where(b=>b.TaskId==taskId);
        if(!string.IsNullOrWhiteSpace(keyword))query=query.Where(b=>b.Title.Contains(keyword.Trim()));
        var total=await query.CountAsync(ct);var skip=((long)page-1)*pageSize;if(skip>int.MaxValue)return Reply(400,"Trang vượt phạm vi hỗ trợ.");
        var rows=await query.OrderByDescending(b=>b.CreatedAt).ThenByDescending(b=>b.BugId).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return Reply(200,"Lấy danh sách Bug thành công.",new{items=rows.Select(Details),total,page,pageSize});
    }

    [HttpGet("api/v1/bugs/{bugId:long}")]
    public async Task<IActionResult> Get(long bugId,CancellationToken ct)
    {
        if(bugId<=0)return Reply(400,"Mã Bug không hợp lệ.");
        var bug=await db.Bugs.AsNoTracking().Include(b=>b.Task).SingleOrDefaultAsync(b=>b.BugId==bugId,ct);
        if(bug is null)return Reply(404,"Không tìm thấy Bug.");
        var access=await Access(bug.Task.ProjectId,false,ct);if(access!=200)return Denied(access);
        var comments=db.Comments.AsNoTracking().Where(c=>c.TaskId==bug.TaskId);
        var commentCount=await comments.CountAsync(ct);
        var commentRows=await comments.OrderByDescending(c=>c.CreatedAt).ThenByDescending(c=>c.CommentId).Take(20)
            .Select(c=>new{c.CommentId,c.UserId,c.Content,c.CreatedAt,c.UpdatedAt}).ToListAsync(ct);
        var files=db.Attachments.AsNoTracking().Where(a=>a.TaskId==bug.TaskId);
        var fileCount=await files.CountAsync(ct);
        var fileRows=await files.OrderByDescending(a=>a.UploadedAt).ThenByDescending(a=>a.AttachmentId).Take(20)
            .Select(a=>new{a.AttachmentId,a.FileName,a.FileSize,a.UploadedBy,a.UploadedAt}).ToListAsync(ct);
        var logs=db.ActivityLogs.AsNoTracking().Where(l=>l.ProjectId==bug.Task.ProjectId&&l.EntityType=="BUG"&&l.EntityId==bugId);
        var logCount=await logs.CountAsync(ct);
        var logRows=await logs.OrderByDescending(l=>l.CreatedAt).ThenByDescending(l=>l.ActivityLogId).Take(50).ToListAsync(ct);
        return Reply(200,"Lấy chi tiết Bug thành công.",new
        {
            bug=Details(bug),task=new{bug.Task.TaskId,bug.Task.Title,bug.Task.Status,bug.Task.SprintId},
            taskComments=new{total=commentCount,items=commentRows.Select(c=>new{c.CommentId,c.UserId,c.Content,createdAt=Utc(c.CreatedAt),updatedAt=Utc(c.UpdatedAt)})},
            taskAttachments=new{total=fileCount,items=fileRows.Select(a=>new{a.AttachmentId,a.FileName,a.FileSize,a.UploadedBy,uploadedAt=Utc(a.UploadedAt)})},
            history=new{total=logCount,items=logRows.Select(l=>new{l.ActivityLogId,l.UserId,l.Action,l.Description,createdAt=Utc(l.CreatedAt)})}
        });
    }

    [HttpPut("api/v1/bugs/{bugId:long}")]
    public Task<IActionResult> Update(long bugId,BugUpdateRequest request,CancellationToken ct)=>Change(bugId,"UPDATE",request,null,null,ct);
    [HttpDelete("api/v1/bugs/{bugId:long}")]
    public Task<IActionResult> Delete(long bugId,CancellationToken ct)=>Change(bugId,"DELETE",null,null,null,ct);
    [HttpPatch("api/v1/bugs/{bugId:long}/assignee")]
    public Task<IActionResult> Assign(long bugId,BugAssigneeRequest request,CancellationToken ct)=>Change(bugId,"ASSIGN",null,request.AssigneeId,null,ct);
    [HttpPatch("api/v1/bugs/{bugId:long}/priority")]
    public Task<IActionResult> Priority(long bugId,BugPriorityRequest request,CancellationToken ct)=>Change(bugId,"PRIORITY",null,null,request.Priority,ct);
    [HttpPatch("api/v1/bugs/{bugId:long}/status")]
    public Task<IActionResult> Status(long bugId,BugStatusRequest request,CancellationToken ct)=>Change(bugId,"STATUS",null,null,request.Status,ct);
    [HttpPost("api/v1/bugs/{bugId:long}/close")]
    public Task<IActionResult> Close(long bugId,CancellationToken ct)=>Change(bugId,"STATUS",null,null,"CLOSED",ct);
    private async Task<IActionResult> Change(long bugId,string operation,BugUpdateRequest? request,long? assignee,string? value,CancellationToken ct)
    {
        if(bugId<=0)return Reply(400,"Mã Bug không hợp lệ.");
        if(request is not null&&(string.IsNullOrWhiteSpace(request.Title)||string.IsNullOrWhiteSpace(request.Description)))return Reply(400,"Tiêu đề và mô tả không được trống.");
        var projectId=await db.Bugs.Where(b=>b.BugId==bugId).Select(b=>(long?)b.Task.ProjectId).SingleOrDefaultAsync(ct);
        if(!projectId.HasValue)return Reply(404,"Không tìm thấy Bug.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var access=await Access(projectId.Value,true,ct);if(access!=200)return Denied(access);
        var bug=await db.Bugs.SingleOrDefaultAsync(b=>b.BugId==bugId,ct);if(bug is null)return Reply(404,"Không tìm thấy Bug.");
        var description=operation;
        if(operation=="DELETE")db.Bugs.Remove(bug);
        else if(operation=="UPDATE")
        {bug.Title=request!.Title.Trim();bug.Description=request.Description.Trim();bug.Environment=request.Environment?.Trim();bug.Severity=request.Severity;bug.Priority=request.Priority;}
        else if(operation=="PRIORITY")bug.Priority=value!;
        else if(operation=="ASSIGN")
        {
            if(!await ValidAssignee(projectId.Value,assignee,ct))return Reply(409,"Người xử lý phải là thành viên đang hoạt động của dự án.");
            if(bug.AssigneeId==assignee)return Reply(409,"Phân công không thay đổi.");bug.AssigneeId=assignee;
        }
        else
        {
            var allowed=bug.Status switch
            {"OPEN"=>new[]{"IN_PROGRESS"},"IN_PROGRESS"=>new[]{"RESOLVED"},"RESOLVED"=>new[]{"CLOSED","REOPENED"},"CLOSED"=>new[]{"REOPENED"},"REOPENED"=>new[]{"IN_PROGRESS"},_=>Array.Empty<string>()};
            if(!allowed.Contains(value!))return Reply(409,"Chuyển trạng thái không đúng workflow của Bug.");
            if((value=="IN_PROGRESS"||value=="RESOLVED")&&!await ValidAssignee(projectId.Value,bug.AssigneeId,ct))
                return Reply(409,"Cần giao lại Bug cho thành viên đang hoạt động.");
            description=$"{bug.Status} -> {value}";bug.Status=value!;
        }
        bug.UpdatedAt=DateTime.UtcNow;Log(projectId.Value,bugId,operation=="STATUS"?"BUG_STATUS_CHANGED":"BUG_"+operation,description);
        if(operation=="ASSIGN"&&bug.AssigneeId.HasValue&&bug.AssigneeId!=UserId)Notify(bug.AssigneeId.Value,bug,"BUG_ASSIGNED","Bạn được giao xử lý Bug");
        if(operation=="STATUS")
        {
            var related=new[]{(long?)bug.ReporterId,bug.AssigneeId}.Where(id=>id.HasValue&&id!=UserId).Select(id=>id!.Value).Distinct().ToList();
            var recipients=await db.ProjectMembers.Where(m=>m.ProjectId==projectId&&related.Contains(m.UserId)&&m.User.Status==1).Select(m=>m.UserId).ToListAsync(ct);
            foreach(var id in recipients)Notify(id,bug,"BUG_STATUS_CHANGED","Bug đã đổi trạng thái");
        }
        await db.SaveChangesAsync(ct);if(operation!="DELETE")await db.Entry(bug).ReloadAsync(ct);await tx.CommitAsync(ct);
        return Reply(200,"Thao tác Bug thành công.",operation=="DELETE"?null:Details(bug));
    }
    private void Log(long projectId,long bugId,string action,string description)=>db.ActivityLogs.Add(new ActivityLog
    {ProjectId=projectId,UserId=UserId,EntityType="BUG",EntityId=bugId,Action=action,Description=description,CreatedAt=DateTime.UtcNow});
    private void Notify(long userId,Bug bug,string type,string title)=>db.Notifications.Add(new Notification
    {UserId=userId,Type=type,Title=title,Content=bug.Title,RefType="BUG",RefId=bug.BugId,CreatedAt=DateTime.UtcNow});
}
