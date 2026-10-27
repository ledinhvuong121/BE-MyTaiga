using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Entities;
using MyTaigaAPI.Services;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
public class AttachmentsController(ScrumbanDbContext db,TaskFileStore store,CloudinaryImages cloud,SystemConfigService config,ILogger<AttachmentsController> logger):ControllerBase
{
    private long UserId=>long.Parse(User.FindFirst("sub")!.Value);
    private ObjectResult Reply(int code,string message,object? data=null)=>StatusCode(code,new{success=code<400,message,data});
    private object Details(Attachment a)=>new{a.AttachmentId,a.TaskId,a.FileName,a.FileSize,a.UploadedBy,
        url=cloud.IsCloud(a.FilePath)?cloud.Delivery(a.FilePath):null,
        uploadedAt=DateTime.SpecifyKind(a.UploadedAt,DateTimeKind.Utc),previewUrl=cloud.IsCloud(a.FilePath)?cloud.Delivery(a.FilePath):$"/api/v1/attachments/{a.AttachmentId}/preview",downloadUrl=cloud.IsCloud(a.FilePath)?cloud.Delivery(a.FilePath,true):$"/api/v1/attachments/{a.AttachmentId}/download"};
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
    [HttpPost("api/v1/tasks/{taskId:long}/attachments")]
    [RequestSizeLimit(272629760)]
    [RequestFormLimits(MultipartBodyLengthLimit=272629760)]
    public async Task<IActionResult> Upload(long taskId,[FromForm]List<IFormFile> files,CancellationToken ct)
    {
        if(taskId<=0)return Reply(400,"Mã Task không hợp lệ.");
        if(files.Count is <1 or >10)return Reply(400,"Mỗi lần tải lên cần 1–10 tệp.");
        var maximum=Math.Min(25,await config.GetPositiveIntAsync("MAX_FILE_SIZE_MB",ct))*1048576L;
        var configured=await db.SystemConfigs.Where(c=>c.ConfigKey=="ALLOWED_FILE_EXTENSIONS").Select(c=>c.ConfigValue).SingleOrDefaultAsync(ct);
        var allowed=(configured??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectId=await db.Tasks.Where(t=>t.TaskId==taskId).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct);
        if(!projectId.HasValue)return Reply(404,"Không tìm thấy Task.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var access=await Access(projectId.Value,true,ct);if(access!=200)return Denied(access);
        if(!await db.Tasks.AnyAsync(t=>t.TaskId==taskId,ct))return Reply(404,"Không tìm thấy Task.");
        var uploadedCloud=new List<string>();var tempImages=new List<string>();var paths=new List<string>();var rows=new List<Attachment>();var committed=false;
        try
        {
            Directory.CreateDirectory(store.Root);
            foreach(var file in files)
            {
                var name=TaskFileStore.FileName(file.FileName);var extension=Path.GetExtension(name).ToLowerInvariant();
                if(!allowed.Contains(extension)||!TaskFileStore.MimeTypes.ContainsKey(extension))return Reply(400,"Định dạng tệp không được cho phép.");
                if(file.Length<=0||file.Length>maximum)return Reply(400,$"Tệp cần có dữ liệu và không vượt {maximum/1048576} MB.");
                var key=Guid.NewGuid().ToString("N")+extension;var path=store.Resolve(key);paths.Add(path);
                await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))await file.CopyToAsync(output,ct);
                if(new FileInfo(path).Length>maximum)return Reply(400,"Tệp vượt dung lượng cho phép.");
                TaskFileStore.Validate(path,extension);
                var stored=key;
                if(extension is ".png" or ".jpg" or ".jpeg")
                {
                    await using var input=System.IO.File.OpenRead(path);stored=await cloud.UploadAsync(input,name,true,ct);uploadedCloud.Add(stored);tempImages.Add(path);
                }
                rows.Add(new Attachment{TaskId=taskId,FileName=name,FilePath=stored,FileSize=file.Length,UploadedBy=UserId,UploadedAt=DateTime.UtcNow});
            }
            db.Attachments.AddRange(rows);await db.SaveChangesAsync(ct);
            foreach(var row in rows)Log(projectId.Value,row.AttachmentId,"ATTACHMENT_UPLOADED");
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);committed=true;
            return Reply(201,"Tải tệp thành công.",new{items=rows.Select(Details)});
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidDataException or DecoderFallbackException)
        {return Reply(400,"Tệp không hợp lệ: "+ex.Message);}
        finally
        {foreach(var path in tempImages)TryDelete(path);if(!committed){foreach(var path in paths)TryDelete(path);foreach(var url in uploadedCloud)await cloud.DeleteAsync(url);}}
    }
    [HttpGet("api/v1/tasks/{taskId:long}/attachments")]
    public async Task<IActionResult> List(long taskId,[FromQuery,Range(1,int.MaxValue)]int page=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)
    {
        if(taskId<=0)return Reply(400,"Mã Task không hợp lệ.");
        var projectId=await db.Tasks.Where(t=>t.TaskId==taskId).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct);
        if(!projectId.HasValue)return Reply(404,"Không tìm thấy Task.");var access=await Access(projectId.Value,false,ct);if(access!=200)return Denied(access);
        var query=db.Attachments.AsNoTracking().Where(a=>a.TaskId==taskId);var total=await query.CountAsync(ct);var skip=((long)page-1)*pageSize;
        if(skip>int.MaxValue)return Reply(400,"Trang vượt phạm vi hỗ trợ.");
        var rows=await query.OrderByDescending(a=>a.UploadedAt).ThenByDescending(a=>a.AttachmentId).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return Reply(200,"Lấy tệp đính kèm thành công.",new{items=rows.Select(Details),total,page,pageSize});
    }
    [HttpGet("api/v1/attachments/{attachmentId:long}")]
    public Task<IActionResult> Metadata(long attachmentId,CancellationToken ct)=>Read(attachmentId,"metadata",ct);
    [HttpGet("api/v1/attachments/{attachmentId:long}/download")]
    public Task<IActionResult> Download(long attachmentId,CancellationToken ct)=>Read(attachmentId,"download",ct);
    [HttpGet("api/v1/attachments/{attachmentId:long}/preview")]
    public Task<IActionResult> Preview(long attachmentId,CancellationToken ct)=>Read(attachmentId,"preview",ct);
    private async Task<IActionResult> Read(long id,string mode,CancellationToken ct)
    {
        if(id<=0)return Reply(400,"Mã tệp không hợp lệ.");
        var row=await db.Attachments.AsNoTracking().Include(a=>a.Task).SingleOrDefaultAsync(a=>a.AttachmentId==id,ct);if(row is null)return Reply(404,"Không tìm thấy tệp.");
        var access=await Access(row.Task.ProjectId,false,ct);if(access!=200)return Denied(access);
        if(mode=="metadata")return Reply(200,"Lấy thông tin tệp thành công.",Details(row));
        if(cloud.IsCloud(row.FilePath)){Response.Headers.CacheControl="private, no-store";return Redirect(cloud.Delivery(row.FilePath,mode=="download"));}
        string path;try{path=store.Resolve(row.FilePath);}catch(ArgumentException){return Reply(404,"Tệp lưu trữ không khả dụng.");}
        if(!System.IO.File.Exists(path))return Reply(404,"Tệp lưu trữ không khả dụng.");
        var extension=Path.GetExtension(path);var mime=TaskFileStore.MimeTypes[extension];
        if(mode=="preview"&&extension is not(".png" or ".jpg" or ".jpeg" or ".pdf" or ".txt"))return Reply(415,"Định dạng này chỉ hỗ trợ tải xuống.");
        Response.Headers.CacheControl="private, no-store";Response.Headers["X-Content-Type-Options"]="nosniff";
        Response.Headers.ContentSecurityPolicy="sandbox; default-src 'none'";
        return mode=="download"?PhysicalFile(path,mime,TaskFileStore.FileName(row.FileName),enableRangeProcessing:true):PhysicalFile(path,mime,enableRangeProcessing:true);
    }
    [HttpDelete("api/v1/attachments/{attachmentId:long}")]
    public async Task<IActionResult> Delete(long attachmentId,CancellationToken ct)
    {
        if(attachmentId<=0)return Reply(400,"Mã tệp không hợp lệ.");
        var projectId=await db.Attachments.Where(a=>a.AttachmentId==attachmentId).Select(a=>(long?)a.Task.ProjectId).SingleOrDefaultAsync(ct);if(!projectId.HasValue)return Reply(404,"Không tìm thấy tệp.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);var access=await Access(projectId.Value,true,ct);if(access!=200)return Denied(access);
        var row=await db.Attachments.SingleOrDefaultAsync(a=>a.AttachmentId==attachmentId,ct);if(row is null)return Reply(404,"Không tìm thấy tệp.");
        if(row.UploadedBy!=UserId&&!await db.ProjectMembers.AnyAsync(m=>m.ProjectId==projectId&&m.UserId==UserId&&m.Role.RoleName=="MANAGER",ct))return Reply(403,"Chỉ người tải lên hoặc Manager được xóa tệp.");
        db.Attachments.Remove(row);Log(projectId.Value,attachmentId,"ATTACHMENT_DELETED");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        if(cloud.IsCloud(row.FilePath))await cloud.DeleteAsync(row.FilePath);
        else try{TryDelete(store.Resolve(row.FilePath));}catch(ArgumentException){/* Legacy metadata may not use the new store format. */}
        return Reply(200,"Xóa tệp thành công.");
    }
    private void TryDelete(string path)
    {try{System.IO.File.Delete(path);}catch(IOException){logger.LogWarning("Could not remove an unreferenced task file.");}catch(UnauthorizedAccessException){logger.LogWarning("Task file cleanup denied.");}}
    private void Log(long projectId,long id,string action)=>db.ActivityLogs.Add(new ActivityLog{ProjectId=projectId,UserId=UserId,EntityId=id,EntityType="ATTACHMENT",Action=action,Description=action,CreatedAt=DateTime.UtcNow});
}
