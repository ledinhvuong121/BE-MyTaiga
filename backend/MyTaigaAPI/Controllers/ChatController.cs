using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Entities;
using MyTaigaAPI.Services;
namespace MyTaigaAPI.Controllers;
public class CreateRoomRequest
{
    [Required,RegularExpression("^(PRIVATE|GROUP)$")]public string RoomType {get;set;}="PRIVATE";
    public long? RecipientId {get;set;}
    public long? ProjectId {get;set;}
    [StringLength(100)]public string? RoomName {get;set;}
    [MaxLength(100)]public List<long> ParticipantIds {get;set;}=[];
}
public class SendMessageRequest{[StringLength(5000)]public string? Content {get;set;}}
public class ReadChatRequest{[Range(1,long.MaxValue)]public long? MessageId {get;set;}}
public class ParticipantsRequest{[MinLength(1),MaxLength(100)]public List<long> UserIds {get;set;}=[];}
[ApiController,Authorize,Route("api/v1/chat")]
public class ChatController(ScrumbanDbContext db,TaskFileStore files,SystemConfigService config):ControllerBase
{
    long Me=>long.Parse(User.FindFirst("sub")!.Value);
    ObjectResult Reply(int code,string message,object? data=null)=>StatusCode(code,new{success=code<400,message,data});
    async Task<ChatRoom?> Room(long id,bool write,CancellationToken ct)
    {
        var room=await ChatScope.Visible(db,Me).SingleOrDefaultAsync(r=>r.ChatRoomId==id,ct);
        if(room is null)return null;
        if(write)
        {
            // Serialize room writes and recheck membership after acquiring the lock.
            if(room.RoomType=="PRIVATE")
            {
                var common=await db.ProjectMembers.Where(a=>a.UserId==room.PrivateUserLowId&&a.Project.Status=="ACTIVE"&&db.ProjectMembers.Any(b=>b.ProjectId==a.ProjectId&&b.UserId==room.PrivateUserHighId)).OrderBy(a=>a.ProjectId).Select(a=>(long?)a.ProjectId).FirstOrDefaultAsync(ct);
                if(!common.HasValue)return null;
                await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.Project WITH(UPDLOCK,HOLDLOCK) WHERE ProjectId={common}").LoadAsync(ct);
            }
            if(room.ProjectId.HasValue)await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.Project WITH(UPDLOCK,HOLDLOCK) WHERE ProjectId={room.ProjectId}").LoadAsync(ct);
            await db.ChatRooms.FromSqlInterpolated($"SELECT * FROM dbo.ChatRoom WITH(UPDLOCK,HOLDLOCK) WHERE ChatRoomId={id}").LoadAsync(ct);
            if(!await ChatScope.Visible(db,Me).AnyAsync(r=>r.ChatRoomId==id,ct))return null;
            if(room.ProjectId.HasValue&&!await db.Projects.AnyAsync(p=>p.ProjectId==room.ProjectId&&p.Status=="ACTIVE",ct))return null;
        }
        return room;
    }
    async Task<bool> Manager(long projectId,CancellationToken ct)=>await db.ProjectMembers.AnyAsync(p=>p.ProjectId==projectId&&p.UserId==Me&&p.Role.RoleName=="MANAGER"&&p.Project.Status=="ACTIVE",ct);
    [HttpPost("rooms")]
    public async Task<IActionResult> Create(CreateRoomRequest request,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var ids=request.ParticipantIds.Distinct().Append(Me).Distinct().ToList();
        ChatRoom room;
        if(request.RoomType=="PRIVATE")
        {
            if(request.RecipientId is not >0||request.RecipientId==Me)return Reply(400,"Chọn người nhận khác bạn.");
            var other=request.RecipientId.Value;
            var lockId=Math.Min(Me,other);
            await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.[User] WITH(UPDLOCK,HOLDLOCK) WHERE UserId={lockId}").LoadAsync(ct);
            if(!await db.Users.AnyAsync(u=>u.UserId==other&&u.Status==1,ct))return Reply(404,"Không tìm thấy người nhận.");
            if(!await db.ProjectMembers.AnyAsync(a=>a.UserId==Me&&a.Project.Status=="ACTIVE"&&db.ProjectMembers.Any(b=>b.ProjectId==a.ProjectId&&b.UserId==other),ct))return Reply(403,"Người nhận phải cùng dự án đang hoạt động.");
            var low=Math.Min(Me,other);var high=Math.Max(Me,other);
            var existing=await db.ChatRooms.SingleOrDefaultAsync(r=>r.RoomType=="PRIVATE"&&r.PrivateUserLowId==low&&r.PrivateUserHighId==high,ct);
            if(existing is not null)return Reply(200,"Phòng chat đã tồn tại.",new{existing.ChatRoomId});
            room=new(){RoomType="PRIVATE",PrivateUserLowId=low,PrivateUserHighId=high,CreatedBy=Me,CreatedAt=DateTime.UtcNow};ids=[Me,other];
        }
        else
        {
            if(request.ProjectId is not >0||string.IsNullOrWhiteSpace(request.RoomName))return Reply(400,"Cần dự án và tên phòng.");
            await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.Project WITH(UPDLOCK,HOLDLOCK) WHERE ProjectId={request.ProjectId}").LoadAsync(ct);
            if(!await Manager(request.ProjectId.Value,ct))return Reply(403,"Chỉ Manager của dự án đang hoạt động được tạo nhóm.");
            if(await db.ProjectMembers.CountAsync(m=>m.ProjectId==request.ProjectId&&ids.Contains(m.UserId)&&m.User.Status==1,ct)!=ids.Count)return Reply(400,"Thành viên phải thuộc dự án và đang hoạt động.");
            room=new(){RoomType="GROUP",ProjectId=request.ProjectId,RoomName=request.RoomName.Trim(),CreatedBy=Me,CreatedAt=DateTime.UtcNow};
        }
        foreach(var id in ids)room.ChatParticipants.Add(new(){UserId=id,JoinedAt=DateTime.UtcNow});
        db.ChatRooms.Add(room);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Reply(201,"Tạo phòng chat thành công.",new{room.ChatRoomId});
    }
    [HttpGet("rooms")]
    public async Task<IActionResult> List(CancellationToken ct)=>Reply(200,"Lấy phòng chat thành công.",await ChatScope.Visible(db,Me).AsNoTracking().OrderByDescending(r=>r.ChatRoomId).Take(100).Select(r=>new{r.ChatRoomId,r.RoomType,r.RoomName,r.ProjectId,
        unreadCount=r.Messages.Count(m=>m.SenderId!=Me&&m.MessageId>r.ChatParticipants.Where(p=>p.UserId==Me).Select(p=>p.LastReadMessageId).First())}).ToListAsync(ct));
    [HttpPost("rooms/{id:long}/participants")]
    public async Task<IActionResult> Add(long id,ParticipantsRequest request,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);var room=await Room(id,true,ct);
        if(room is null)return NotFound();if(room.RoomType!="GROUP"||!await Manager(room.ProjectId!.Value,ct))return StatusCode(403);
        var ids=request.UserIds.Distinct().ToList();if(await db.ProjectMembers.CountAsync(m=>m.ProjectId==room.ProjectId&&ids.Contains(m.UserId)&&m.User.Status==1,ct)!=ids.Count)return BadRequest();
        await db.ChatParticipants.Where(p=>p.ChatRoomId==id&&ids.Contains(p.UserId)).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.IsActive,true),ct);
        var present=await db.ChatParticipants.Where(p=>p.ChatRoomId==id).Select(p=>p.UserId).ToListAsync(ct);
        foreach(var user in ids.Except(present))db.ChatParticipants.Add(new(){ChatRoomId=id,UserId=user,JoinedAt=DateTime.UtcNow});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Reply(200,"Đã thêm thành viên.");
    }
    [HttpGet("rooms/{id:long}/messages")]
    public async Task<IActionResult> History(long id,[FromQuery,Range(1,long.MaxValue)]long? before=null,[FromQuery,Range(1,100)]int limit=30,[FromQuery,StringLength(200)]string? keyword=null,CancellationToken ct=default)
    {
        if(await Room(id,false,ct) is null)return NotFound();var query=db.Messages.AsNoTracking().Include(m=>m.MessageAttachments).Where(m=>m.ChatRoomId==id);
        if(before.HasValue)query=query.Where(m=>m.MessageId<before);if(!string.IsNullOrWhiteSpace(keyword))query=query.Where(m=>m.Content!=null&&m.Content.Contains(keyword));
        var rows=await query.OrderByDescending(m=>m.MessageId).Take(limit+1).ToListAsync(ct);var more=rows.Count>limit;rows=rows.Take(limit).ToList();
        return Reply(200,"Lấy tin nhắn thành công.",new{items=rows.Select(ChatScope.Details),hasMore=more,nextBefore=more?rows.Last().MessageId:(long?)null});
    }
    [HttpPost("rooms/{id:long}/messages"),Consumes("application/json")]
    public Task<IActionResult> Send(long id,SendMessageRequest request,CancellationToken ct)=>SaveMessage(id,request.Content,[],ct);
    [HttpPost("rooms/{id:long}/messages"),Consumes("multipart/form-data"),RequestSizeLimit(272629760),RequestFormLimits(MultipartBodyLengthLimit=272629760)]
    public Task<IActionResult> SendFiles(long id,[FromForm]string? content,[FromForm]List<IFormFile> files,CancellationToken ct)=>SaveMessage(id,content,files,ct);
    async Task<IActionResult> SaveMessage(long id,string? text,List<IFormFile> uploads,CancellationToken ct)
    {
        text=text?.Trim();if((string.IsNullOrEmpty(text)&&uploads.Count==0)||text?.Length>Math.Min(5000,await config.GetPositiveIntAsync("MAX_COMMENT_LENGTH",ct))||uploads.Count>10)return Reply(400,"Tin nhắn cần nội dung tối đa 5000 ký tự hoặc tối đa 10 tệp.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);var room=await Room(id,true,ct);if(room is null)return NotFound();
        var maximum=Math.Min(25,await config.GetPositiveIntAsync("MAX_FILE_SIZE_MB",ct))*1048576L;
        var allowed=(await db.SystemConfigs.Where(c=>c.ConfigKey=="ALLOWED_FILE_EXTENSIONS").Select(c=>c.ConfigValue).SingleAsync(ct)).Split(',',StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paths=new List<string>();var committed=false;
        try
        {
            var message=new Message{ChatRoomId=id,SenderId=Me,Content=text,SentAt=DateTime.UtcNow};
            foreach(var upload in uploads)
            {
                var name=TaskFileStore.FileName(upload.FileName);var ext=Path.GetExtension(name).ToLowerInvariant();
                if(upload.Length<=0||upload.Length>maximum||!allowed.Contains(ext)||!TaskFileStore.MimeTypes.ContainsKey(ext))return BadRequest();
                Directory.CreateDirectory(files.Root);var key=Guid.NewGuid().ToString("N")+ext;var path=files.Resolve(key);paths.Add(path);
                await using(var output=System.IO.File.Create(path))await upload.CopyToAsync(output,ct);TaskFileStore.Validate(path,ext);
                message.MessageAttachments.Add(new(){FileName=name,FilePath=key,FileSize=upload.Length});
            }
            db.Messages.Add(message);await db.SaveChangesAsync(ct);
            var recipients=await db.ChatParticipants.Where(p=>p.ChatRoomId==id&&p.IsActive&&p.UserId!=Me&&p.User.Status==1).Select(p=>p.UserId).ToListAsync(ct);
            foreach(var recipient in recipients)if(await ChatScope.Visible(db,recipient).AnyAsync(r=>r.ChatRoomId==id,ct))db.Notifications.Add(new(){UserId=recipient,Type="CHAT_MESSAGE",Title="Có tin nhắn mới",RefType="MESSAGE",RefId=message.MessageId,CreatedAt=DateTime.UtcNow});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);committed=true;return Reply(201,"Gửi tin nhắn thành công.",ChatScope.Details(message));
        }
        catch(Exception e)when(e is ArgumentException or InvalidDataException or System.Text.DecoderFallbackException){return Reply(400,"Tệp không hợp lệ.");}
        finally{if(!committed)foreach(var path in paths)try{System.IO.File.Delete(path);}catch(IOException){}}
    }
    [HttpPost("rooms/{id:long}/read")]
    public async Task<IActionResult> Read(long id,ReadChatRequest request,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);if(await Room(id,false,ct) is null)return NotFound();
        var cursor=request.MessageId??await db.Messages.Where(m=>m.ChatRoomId==id).MaxAsync(m=>(long?)m.MessageId,ct)??0;
        if(cursor>0&&!await db.Messages.AnyAsync(m=>m.ChatRoomId==id&&m.MessageId==cursor,ct))return BadRequest();
        await db.ChatParticipants.Where(p=>p.ChatRoomId==id&&p.UserId==Me&&p.LastReadMessageId<cursor).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.LastReadMessageId,cursor).SetProperty(p=>p.LastReadAt,DateTime.UtcNow),ct);
        await tx.CommitAsync(ct);return Reply(200,"Đã đánh dấu đọc.");
    }
    [HttpGet("unread-count")]
    public async Task<IActionResult> Unread(CancellationToken ct)=>Reply(200,"Đếm tin chưa đọc thành công.",new{unreadCount=await db.Messages.CountAsync(m=>m.SenderId!=Me&&ChatScope.Visible(db,Me).Any(r=>r.ChatRoomId==m.ChatRoomId)&&db.ChatParticipants.Any(p=>p.ChatRoomId==m.ChatRoomId&&p.UserId==Me&&p.LastReadMessageId<m.MessageId),ct)});
    [HttpGet("rooms/{id:long}/participants")]
    public async Task<IActionResult> Participants(long id,CancellationToken ct)
    {
        if(await Room(id,false,ct) is null)return NotFound();
        return Reply(200,"Lấy thành viên thành công.",await db.ChatParticipants.Where(p=>p.ChatRoomId==id&&p.IsActive).Select(p=>new{p.UserId,p.User.FullName}).ToListAsync(ct));
    }
    [HttpDelete("rooms/{id:long}/participants/{userId:long}")]
    public async Task<IActionResult> RemoveParticipant(long id,long userId,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);var room=await Room(id,true,ct);
        if(room is null)return NotFound();if(room.RoomType!="GROUP"||!await Manager(room.ProjectId!.Value,ct))return StatusCode(403);
        if(userId==Me)return Reply(409,"Manager không tự rời phòng đang quản lý.");
        var changed=await db.ChatParticipants.Where(p=>p.ChatRoomId==id&&p.UserId==userId&&p.IsActive).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.IsActive,false),ct);
        await tx.CommitAsync(ct);return changed==0?NotFound():Reply(200,"Đã bỏ thành viên khỏi phòng; giữ lịch sử tin nhắn.");
    }
    [HttpGet("attachments/{id:long}/preview")]
    public Task<IActionResult> Preview(long id,CancellationToken ct)=>Download(id,ct,true);
    [HttpGet("attachments/{id:long}")]
    public async Task<IActionResult> Download(long id,CancellationToken ct,bool preview=false)
    {
        var attachment=await db.MessageAttachments.Include(a=>a.Message).SingleOrDefaultAsync(a=>a.MessageAttachmentId==id,ct);
        if(attachment is null||await Room(attachment.Message.ChatRoomId,false,ct) is null)return NotFound();
        string path;try{path=files.Resolve(attachment.FilePath);}catch(ArgumentException){return NotFound();}if(!System.IO.File.Exists(path))return NotFound();
        Response.Headers.CacheControl="private, no-store";Response.Headers["X-Content-Type-Options"]="nosniff";Response.Headers.ContentSecurityPolicy="sandbox; default-src 'none'";
        var extension=Path.GetExtension(path);
        if(preview&&extension is not(".png" or ".jpg" or ".jpeg" or ".pdf" or ".txt"))return StatusCode(415);
        return preview?PhysicalFile(path,TaskFileStore.MimeTypes[extension],enableRangeProcessing:true):PhysicalFile(path,TaskFileStore.MimeTypes[extension],TaskFileStore.FileName(attachment.FileName),enableRangeProcessing:true);
    }
}
