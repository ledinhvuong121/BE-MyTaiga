using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Projects;
using MyTaigaAPI.Entities;
using MyTaigaAPI.Services;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
public class CommentsController(ScrumbanDbContext db,SystemConfigService config) : ControllerBase
{
    private static readonly Regex Mentions=new(@"@\[[^\]\r\n]{1,100}\]\(user:(\d{1,19})\)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private long UserId=>long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code,string message,object? data=null)=>StatusCode(code,new{success=code<400,message,data});
    private static List<long>? MentionIds(string content)
    {
        var ids=new HashSet<long>();
        foreach(Match match in Mentions.Matches(content))
        {if(!long.TryParse(match.Groups[1].Value,out var id)||id<=0)return null;ids.Add(id);if(ids.Count>20)return null;}
        return ids.Order().ToList();
    }
    private static object Details(Comment c)=>new{c.CommentId,c.TaskId,c.UserId,c.Content,
        mentionedUserIds=MentionIds(c.Content)??[],createdAt=DateTime.SpecifyKind(c.CreatedAt,DateTimeKind.Utc),
        updatedAt=c.UpdatedAt.HasValue?DateTime.SpecifyKind(c.UpdatedAt.Value,DateTimeKind.Utc):(DateTime?)null};
    private async Task<int> Access(long projectId,bool write,CancellationToken ct)
    {
        var project=write?await db.Projects.FromSqlInterpolated($"""
            SELECT * FROM dbo.Project WITH(UPDLOCK,HOLDLOCK) WHERE ProjectId={projectId}
            """).SingleOrDefaultAsync(ct):await db.Projects.AsNoTracking().SingleOrDefaultAsync(p=>p.ProjectId==projectId,ct);
        if(project is null||project.Status=="DELETED")return 404;
        if(!await db.ProjectMembers.AnyAsync(m=>m.ProjectId==projectId&&m.UserId==UserId,ct))return 403;
        return write&&project.Status!="ACTIVE"?409:200;
    }
    private ObjectResult Denied(int code)=>Reply(code,code==403?"Bạn không thuộc dự án này.":code==404?"Không tìm thấy dự án/Task.":"Dự án đã đóng.");

    [HttpGet("api/v1/tasks/{taskId:long}/comments")]
    public async Task<IActionResult> List(long taskId,[FromQuery,Range(1,int.MaxValue)]int page=1,
        [FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        if(taskId<=0)return Reply(400,"Mã Task không hợp lệ.");
        var projectId=await db.Tasks.Where(t=>t.TaskId==taskId).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct);
        if(!projectId.HasValue)return Reply(404,"Không tìm thấy Task.");
        var access=await Access(projectId.Value,false,ct);if(access!=200)return Denied(access);
        var query=db.Comments.AsNoTracking().Where(c=>c.TaskId==taskId);var total=await query.CountAsync(ct);
        var skip=((long)page-1)*pageSize;if(skip>int.MaxValue)return Reply(400,"Trang vượt phạm vi hỗ trợ.");
        var rows=await query.OrderByDescending(c=>c.CreatedAt).ThenByDescending(c=>c.CommentId).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return Reply(200,"Lấy bình luận thành công.",new{items=rows.Select(Details),total,page,pageSize});
    }
    [HttpGet("api/v1/comments/{commentId:long}")]
    public async Task<IActionResult> Get(long commentId,CancellationToken ct)
    {
        if(commentId<=0)return Reply(400,"Mã bình luận không hợp lệ.");
        var row=await db.Comments.AsNoTracking().Include(c=>c.Task).SingleOrDefaultAsync(c=>c.CommentId==commentId,ct);
        if(row is null)return Reply(404,"Không tìm thấy bình luận.");
        var access=await Access(row.Task.ProjectId,false,ct);return access==200?Reply(200,"Lấy bình luận thành công.",Details(row)):Denied(access);
    }
    [HttpPost("api/v1/tasks/{taskId:long}/comments")]
    public Task<IActionResult> Create(long taskId,CommentRequest request,CancellationToken ct)=>Change(taskId,null,request,false,ct);
    [HttpPut("api/v1/comments/{commentId:long}")]
    public Task<IActionResult> Update(long commentId,CommentRequest request,CancellationToken ct)=>Change(null,commentId,request,false,ct);
    [HttpDelete("api/v1/comments/{commentId:long}")]
    public Task<IActionResult> Delete(long commentId,CancellationToken ct)=>Change(null,commentId,null,true,ct);
    private async Task<IActionResult> Change(long? taskId,long? commentId,CommentRequest? request,bool delete,CancellationToken ct)
    {
        if(taskId is <=0||commentId is <=0)return Reply(400,"Mã không hợp lệ.");
        if(commentId.HasValue)
        {taskId=await db.Comments.Where(c=>c.CommentId==commentId).Select(c=>(long?)c.TaskId).SingleOrDefaultAsync(ct);if(!taskId.HasValue)return Reply(404,"Không tìm thấy bình luận.");}
        var projectId=await db.Tasks.Where(t=>t.TaskId==taskId).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct);
        if(!projectId.HasValue)return Reply(404,"Không tìm thấy Task.");
        var content=request?.Content.Trim();var ids=content is null?[]:MentionIds(content);
        if(request is not null)
        {
            var maximum=Math.Min(5000,await config.GetPositiveIntAsync("MAX_COMMENT_LENGTH",ct));
            if(string.IsNullOrWhiteSpace(content)||content.Length>maximum)return Reply(400,$"Nội dung cần 1–{maximum} ký tự.");
            if(ids is null)return Reply(400,"Mention không hợp lệ hoặc vượt 20 người.");
        }
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var access=await Access(projectId.Value,true,ct);if(access!=200)return Denied(access);
        var task=await db.Tasks.SingleOrDefaultAsync(t=>t.TaskId==taskId,ct);if(task is null)return Reply(404,"Không tìm thấy Task.");
        if(ids is not null&&ids.Count>0&&await db.ProjectMembers.CountAsync(m=>m.ProjectId==projectId&&ids.Contains(m.UserId)&&m.User.Status==1,ct)!=ids.Count)
            return Reply(400,"Người được mention phải là thành viên hoạt động trong dự án.");
        var oldIds=new List<long>();Comment row;
        if(commentId.HasValue)
        {
            var existing=await db.Comments.SingleOrDefaultAsync(c=>c.CommentId==commentId,ct);if(existing is null)return Reply(404,"Không tìm thấy bình luận.");
            if(existing.UserId!=UserId)return Reply(403,"Chỉ tác giả được sửa/xóa bình luận của mình.");row=existing;oldIds=MentionIds(row.Content)??[];
            if(delete)db.Comments.Remove(row);else{row.Content=content!;row.UpdatedAt=DateTime.UtcNow;}
        }
        else{row=new Comment{TaskId=task.TaskId,UserId=UserId,Content=content!,CreatedAt=DateTime.UtcNow};db.Comments.Add(row);}
        await db.SaveChangesAsync(ct);
        db.ActivityLogs.Add(new ActivityLog{ProjectId=projectId,UserId=UserId,EntityType="COMMENT",EntityId=row.CommentId,
            Action=delete?"COMMENT_DELETED":commentId.HasValue?"COMMENT_UPDATED":"COMMENT_CREATED",Description="Thao tác bình luận.",CreatedAt=DateTime.UtcNow});
        if(!delete)
        {
            var mentionIds=ids!;
            var targets=mentionIds.Except(oldIds).ToHashSet();
            if(!commentId.HasValue&&task.AssigneeId.HasValue)targets.Add(task.AssigneeId.Value);
            targets.Remove(UserId);
            var recipients=await db.ProjectMembers.Where(m=>m.ProjectId==projectId&&targets.Contains(m.UserId)&&m.User.Status==1).Select(m=>m.UserId).ToListAsync(ct);
            foreach(var id in recipients)db.Notifications.Add(new Notification{UserId=id,Type=mentionIds.Contains(id)?"COMMENT_MENTION":"TASK_COMMENT",
                Title=mentionIds.Contains(id)?"Bạn được nhắc trong bình luận":"Task có bình luận mới",Content=task.Title,RefType="COMMENT",RefId=row.CommentId,CreatedAt=DateTime.UtcNow});
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Reply(commentId.HasValue?200:201,"Thao tác bình luận thành công.",delete?null:Details(row));
    }
}
