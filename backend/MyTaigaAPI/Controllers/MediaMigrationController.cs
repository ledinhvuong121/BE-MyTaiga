using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Services;
namespace MyTaigaAPI.Controllers;
public class MigrateImagesRequest{[Range(0,long.MaxValue)]public long AfterUserId {get;set;} [Range(0,long.MaxValue)]public long AfterAttachmentId {get;set;} [Range(0,long.MaxValue)]public long AfterMessageAttachmentId {get;set;} public bool Execute {get;set;} [Range(1,20)]public int BatchSize {get;set;}=10;}
[ApiController,Authorize(Roles="ADMIN"),Route("api/v1/admin/media")]
public class MediaMigrationController(ScrumbanDbContext db,CloudinaryImages cloud,TaskFileStore files,IWebHostEnvironment env,IConfiguration config):ControllerBase
{
    [HttpPost("migrate-images")]
    public async Task<IActionResult> Migrate(MigrateImagesRequest request,CancellationToken ct)
    {
        var users=await db.Users.Where(u=>u.UserId>request.AfterUserId&&u.Avatar!=null&&u.Avatar.StartsWith("/api/v1/users/me/avatar/")).OrderBy(u=>u.UserId).Take(request.BatchSize).ToListAsync(ct);
        var attachments=await db.Attachments.Where(a=>a.AttachmentId>request.AfterAttachmentId&&!a.FilePath.StartsWith("http")&&(a.FilePath.EndsWith(".png")||a.FilePath.EndsWith(".jpg")||a.FilePath.EndsWith(".jpeg"))).OrderBy(a=>a.AttachmentId).Take(request.BatchSize).ToListAsync(ct);
        var messages=await db.MessageAttachments.Where(a=>a.MessageAttachmentId>request.AfterMessageAttachmentId&&!a.FilePath.StartsWith("http")&&(a.FilePath.EndsWith(".png")||a.FilePath.EndsWith(".jpg")||a.FilePath.EndsWith(".jpeg"))).OrderBy(a=>a.MessageAttachmentId).Take(request.BatchSize).ToListAsync(ct);
        if(!request.Execute)return Ok(new{success=true,message="Xem trước; chưa upload hoặc sửa database.",data=new{avatars=users.Count,taskImages=attachments.Count,chatImages=messages.Count,batchSize=request.BatchSize}});
        var cursor=new{afterUserId=users.LastOrDefault()?.UserId??request.AfterUserId,afterAttachmentId=attachments.LastOrDefault()?.AttachmentId??request.AfterAttachmentId,afterMessageAttachmentId=messages.LastOrDefault()?.MessageAttachmentId??request.AfterMessageAttachmentId};
        var results=new List<object>();
        async Task Copy(string kind,long id,string original,string path,bool privateImage,Func<string,Task<int>> update)
        {
            string? url=null;
            try
            {
                if(!System.IO.File.Exists(path)){results.Add(new{kind,id,status="SOURCE_MISSING"});return;}
                TaskFileStore.Validate(path,Path.GetExtension(path).ToLowerInvariant());
                await using(var input=System.IO.File.OpenRead(path))url=await cloud.UploadAsync(input,Path.GetFileName(path),privateImage,ct);
                var changed=await update(url);if(changed==0){await cloud.DeleteAsync(url);results.Add(new{kind,id,status="CHANGED_CONCURRENTLY"});return;}
                results.Add(new{kind,id,status="MIGRATED"});
            }
            catch(CloudMediaException){if(url!=null)await cloud.DeleteAsync(url);results.Add(new{kind,id,status="CLOUD_NOT_READY_OR_FAILED"});}
            catch(Exception e)when(e is IOException or ArgumentException or InvalidDataException){if(url!=null)await cloud.DeleteAsync(url);results.Add(new{kind,id,status="SOURCE_INVALID"});}
            catch{if(url!=null)await cloud.DeleteAsync(url);throw;}
        }
        foreach(var u in users)
        {
            var leaf=Path.GetFileName(u.Avatar);if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(leaf),"N",out _)||Path.GetExtension(leaf)!=".png"){results.Add(new{kind="avatar",id=u.UserId,status="SOURCE_INVALID"});continue;}
            var root=config["Storage:AvatarsRoot"]??Path.Combine(env.ContentRootPath,"App_Data","avatars");
            await Copy("avatar",u.UserId,u.Avatar!,Path.Combine(root,leaf!),false,url=>db.Users.Where(v=>v.UserId==u.UserId&&v.Avatar==u.Avatar).ExecuteUpdateAsync(s=>s.SetProperty(v=>v.Avatar,url),ct));
        }
        foreach(var a in attachments)
        {
            string path;try{path=files.Resolve(a.FilePath);}catch(ArgumentException){results.Add(new{kind="task",id=a.AttachmentId,status="SOURCE_INVALID"});continue;}
            await Copy("task",a.AttachmentId,a.FilePath,path,true,url=>db.Attachments.Where(v=>v.AttachmentId==a.AttachmentId&&v.FilePath==a.FilePath).ExecuteUpdateAsync(s=>s.SetProperty(v=>v.FilePath,url),ct));
        }
        foreach(var a in messages)
        {
            string path;try{path=files.Resolve(a.FilePath);}catch(ArgumentException){results.Add(new{kind="chat",id=a.MessageAttachmentId,status="SOURCE_INVALID"});continue;}
            await Copy("chat",a.MessageAttachmentId,a.FilePath,path,true,url=>db.MessageAttachments.Where(v=>v.MessageAttachmentId==a.MessageAttachmentId&&v.FilePath==a.FilePath).ExecuteUpdateAsync(s=>s.SetProperty(v=>v.FilePath,url),ct));
        }
        return Ok(new{success=true,message="Đã xử lý từng ảnh; xem status từng mục. File nguồn cũ được giữ để sao lưu.",data=new{items=results,next=cursor}});
    }
}
