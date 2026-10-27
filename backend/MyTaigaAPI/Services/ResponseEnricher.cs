using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Entities;
namespace MyTaigaAPI.Services;
public class ResponseEnricher(ScrumbanDbContext db,CloudinaryImages cloud,ChatPresence presence)
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    static long? Id(JsonObject n,string key)=>long.TryParse(n[key]?.ToString(),out var id)?id:null;
    static JsonNode? Node(object? value)=>JsonSerializer.SerializeToNode(value,Json);
    static string Initials(string name)=>string.Concat(name.Split(' ',StringSplitOptions.RemoveEmptyEntries).TakeLast(2).Select(w=>StringInfo.GetNextTextElement(w))).ToUpperInvariant();
    static List<JsonObject> Objects(JsonNode? root)
    {
        var list=new List<JsonObject>();void Visit(JsonNode? n){if(n is JsonObject o){list.Add(o);foreach(var p in o.ToList())Visit(p.Value);}else if(n is JsonArray a)foreach(var p in a)Visit(p);}Visit(root);return list;
    }
    public async Task<JsonNode?> EnrichAsync(object value,long viewer,CancellationToken ct)
    {
        var root=Node(value);var rows=Objects(root);
        if(!rows.Any(o=>o.Select(p=>p.Key).Any(k=>k is "taskId" or "commentId" or "notificationId" or "activityLogId" or "messageId" or "chatRoomId" or "attachmentId")))return root;
        var nids=rows.Select(o=>Id(o,"notificationId")).OfType<long>().Distinct().ToArray();
        var notices=await db.Notifications.AsNoTracking().Where(n=>nids.Contains(n.NotificationId)&&n.UserId==viewer).ToDictionaryAsync(n=>n.NotificationId,ct);
        foreach(var row in rows)if(Id(row,"notificationId") is long nid&&notices.TryGetValue(nid,out var notification)){row["actorId"]=notification.ActorId;row["userId"]=notification.UserId;}
        var logids=rows.Select(o=>Id(o,"activityLogId")).OfType<long>().Distinct().ToArray();
        var logs=await db.ActivityLogs.AsNoTracking().Where(l=>logids.Contains(l.ActivityLogId)).ToDictionaryAsync(l=>l.ActivityLogId,ct);
        var tids=rows.Select(o=>Id(o,"taskId")).OfType<long>().Distinct().ToArray();
        var tasks=await db.Tasks.AsNoTracking().Where(t=>tids.Contains(t.TaskId)).ToDictionaryAsync(t=>t.TaskId,ct);
        var mids=rows.Select(o=>Id(o,"messageId")).OfType<long>().Distinct().ToArray();
        var messages=await db.Messages.AsNoTracking().Include(m=>m.ChatRoom).Where(m=>mids.Contains(m.MessageId)).ToDictionaryAsync(m=>m.MessageId,ct);
        var fileIds=rows.Select(o=>Id(o,"messageAttachmentId")).OfType<long>().ToArray();
        var messageFiles=await db.MessageAttachments.AsNoTracking().Where(a=>fileIds.Contains(a.MessageAttachmentId)).ToDictionaryAsync(a=>a.MessageAttachmentId,ct);
        var rids=rows.Where(o=>o.ContainsKey("roomType")).Select(o=>Id(o,"chatRoomId")).OfType<long>().ToArray();
        var rooms=await db.ChatRooms.AsNoTracking().Where(r=>rids.Contains(r.ChatRoomId)).ToDictionaryAsync(r=>r.ChatRoomId,ct);
        var participants=await db.ChatParticipants.AsNoTracking().Where(p=>rids.Contains(p.ChatRoomId)&&p.IsActive).ToListAsync(ct);
        var lastIds=await db.Messages.Where(m=>rids.Contains(m.ChatRoomId)).GroupBy(m=>m.ChatRoomId).Select(g=>g.Max(m=>m.MessageId)).ToListAsync(ct);
        var lastMessages=await db.Messages.AsNoTracking().Include(m=>m.MessageAttachments).Where(m=>lastIds.Contains(m.MessageId)).ToListAsync(ct);
        var eids=rows.Select(o=>Id(o,"epicId")).OfType<long>().Concat(tasks.Values.Where(t=>t.EpicId.HasValue).Select(t=>t.EpicId!.Value)).Distinct().ToArray();
        var epics=await db.Epics.AsNoTracking().Where(e=>eids.Contains(e.EpicId)).ToDictionaryAsync(e=>e.EpicId,ct);
        var uidKeys=new[]{"assigneeId","reporterId","userId","senderId","uploadedBy","actorId","createdBy"};
        var uids=rows.SelectMany(o=>uidKeys.Select(k=>Id(o,k))).OfType<long>().Concat(tasks.Values.SelectMany(t=>new long?[]{t.AssigneeId,t.ReporterId}).OfType<long>())
            .Concat(participants.Select(p=>p.UserId)).Concat(lastMessages.Select(m=>m.SenderId)).Distinct().ToArray();
        var users=await db.Users.AsNoTracking().Where(u=>uids.Contains(u.UserId)).Select(u=>new{u.UserId,u.FullName,u.Avatar,u.TokenVersion}).ToDictionaryAsync(u=>u.UserId,ct);
        var projectIds=messages.Values.Select(m=>m.ChatRoom.ProjectId).OfType<long>().Concat(tasks.Values.Select(t=>t.ProjectId)).Concat(rooms.Values.Select(r=>r.ProjectId).OfType<long>()).Concat(logs.Values.Select(l=>l.ProjectId).OfType<long>()).Distinct().ToArray();
        var roles=await db.ProjectMembers.AsNoTracking().Where(p=>projectIds.Contains(p.ProjectId)&&uids.Contains(p.UserId)).Select(p=>new{p.ProjectId,p.UserId,p.Role.RoleName}).ToListAsync(ct);
        var systemRoles=await db.UserRoles.Where(r=>uids.Contains(r.UserId)).Select(r=>new{r.UserId,r.Role.RoleName}).ToDictionaryAsync(r=>r.UserId,r=>r.RoleName,ct);
        string? Role(long? uid,long? project)=>roles.FirstOrDefault(r=>r.UserId==uid&&r.ProjectId==project)?.RoleName ?? (project is null&&uid.HasValue?systemRoles.GetValueOrDefault(uid.Value):null);
        JsonNode? Person(long? id,long? project=null)
        {
            if(id is null||!users.TryGetValue(id.Value,out var user))return null;
            return Node(new{user.UserId,user.FullName,avatarCode=Initials(user.FullName),avatarUrl=user.Avatar is not null&&cloud.IsCloud(user.Avatar)?user.Avatar:null,role=Role(id,project)});
        }
        foreach(var row in rows)
        {
            var project=Id(row,"projectId");if(Id(row,"messageId") is long messageKey&&messages.TryGetValue(messageKey,out var messageProject))project=messageProject.ChatRoom.ProjectId;if(Id(row,"taskId") is long tid&&tasks.TryGetValue(tid,out var task))project=task.ProjectId;
            if(row.ContainsKey("taskId")&&row.ContainsKey("title")&&row.ContainsKey("status"))
            {
                var aid=Id(row,"assigneeId");var rid=Id(row,"reporterId");if(rid is null&&tasks.TryGetValue(Id(row,"taskId")??0,out var t))rid=t.ReporterId;
                row["assignee"]=Person(aid,project);row["reporter"]=Person(rid,project);
                row["assigneeName"]=aid.HasValue&&users.TryGetValue(aid.Value,out var a)?a.FullName:null;
                row["assigneeAvatarCode"]=aid.HasValue&&users.TryGetValue(aid.Value,out a)?Initials(a.FullName):null;
                row["assigneeAvatarUrl"]=Person(aid,project)?["avatarUrl"]?.DeepClone();row["reporterAvatarUrl"]=Person(rid,project)?["avatarUrl"]?.DeepClone();row["assigneeRole"]=Role(aid,project);row["reporterName"]=rid.HasValue&&users.TryGetValue(rid.Value,out var r)?r.FullName:null;
                if(row.ContainsKey("bugId")){row["taskTitle"]=tasks.GetValueOrDefault(Id(row,"taskId")??0)?.Title;row["taskCode"]="#TASK-"+Id(row,"taskId");}
                else if(tasks.TryGetValue(Id(row,"taskId")??0,out var t2))
                {
                    row["tags"]=t2.TagsJson is null?new JsonArray():JsonNode.Parse(t2.TagsJson);row["storyPoints"]=t2.StoryPoints;row.Remove("tagsJson");
                    var epic=epics.GetValueOrDefault(t2.EpicId??0);row["epic"]=epic is null?null:Node(new{epic.EpicId,epic.Title,epic.Color});row["epicTitle"]=epic?.Title;row["epicColor"]=epic?.Color;
                    var at=t2.UpdatedAt??t2.CreatedAt;var minutes=Math.Max(0,(int)(DateTime.UtcNow-at).TotalMinutes);
                    row["updatedAtFormatted"]=minutes<1?"Vừa cập nhật":minutes<60?$"Cập nhật {minutes} phút trước":minutes<1440?$"Cập nhật {minutes/60} giờ trước":$"Cập nhật {minutes/1440} ngày trước";
                }
            }
            if(row.ContainsKey("commentId"))
            {
                var uid=Id(row,"userId");row["author"]=Person(uid,project);row["authorName"]=uid.HasValue&&users.TryGetValue(uid.Value,out var u)?u.FullName:null;row["authorAvatarCode"]=uid.HasValue&&users.TryGetValue(uid.Value,out u)?Initials(u.FullName):null;row["authorAvatarUrl"]=Person(uid,project)?["avatarUrl"]?.DeepClone();row["authorRole"]=Role(uid,project);row["isEditable"]=uid==viewer;
            }
            if(row.ContainsKey("fileName")&&(row.ContainsKey("attachmentId")||row.ContainsKey("messageAttachmentId")))
            {
                row["originalFileName"]=row["fileName"]?.DeepClone();row["fileSizeBytes"]=row["fileSize"]?.DeepClone();var ext=Path.GetExtension(row["fileName"]?.ToString()??"").ToLowerInvariant();row["mimeType"]=TaskFileStore.MimeTypes.GetValueOrDefault(ext,"application/octet-stream");
                if(Id(row,"messageAttachmentId") is long fileId&&messageFiles.TryGetValue(fileId,out var file)&&cloud.IsCloud(file.FilePath))
                {row["url"]=cloud.Delivery(file.FilePath);row["previewUrl"]=cloud.Delivery(file.FilePath);row["downloadUrl"]=cloud.Delivery(file.FilePath,true);}
                if(Id(row,"messageAttachmentId") is long localFile&&messageFiles.TryGetValue(localFile,out var local)&&!cloud.IsCloud(local.FilePath))row["previewUrl"]=$"/api/v1/chat/attachments/{localFile}/preview";
                var uid=Id(row,"uploadedBy");row["uploadedByName"]=uid.HasValue&&users.TryGetValue(uid.Value,out var u)?u.FullName:null;
            }
            if(Id(row,"messageId") is long mid&&messages.TryGetValue(mid,out var message))
            {
                row["chatMessageId"]=mid;row["createdAt"]=Node(DateTime.SpecifyKind(message.SentAt,DateTimeKind.Utc));row["sender"]=Person(message.SenderId,project);row["senderName"]=users.GetValueOrDefault(message.SenderId)?.FullName;row["senderAvatarCode"]=users.TryGetValue(message.SenderId,out var u)?Initials(u.FullName):null;row["senderRole"]=Role(message.SenderId,project);row["senderAvatarUrl"]=Person(message.SenderId,project)?["avatarUrl"]?.DeepClone();row["isEditable"]=message.SenderId==viewer;row["isEdited"]=message.EditedAt.HasValue;row["editedAt"]=Node(message.EditedAt.HasValue?DateTime.SpecifyKind(message.EditedAt.Value,DateTimeKind.Utc):(DateTime?)null);row["replyToMessageId"]=message.ReplyToMessageId;
            }
            if(Id(row,"chatRoomId") is long roomId&&row.ContainsKey("roomType")&&rooms.TryGetValue(roomId,out var room))
            {
                var other=participants.FirstOrDefault(p=>p.ChatRoomId==roomId&&p.UserId!=viewer);var person=room.RoomType=="PRIVATE"?Person(other?.UserId,room.ProjectId):null;row["otherParticipant"]=person;row["otherParticipantName"]=person?["fullName"]?.DeepClone();row["otherParticipantAvatarCode"]=person?["avatarCode"]?.DeepClone();row["otherParticipantAvatarUrl"]=person?["avatarUrl"]?.DeepClone();row["otherParticipantStatus"]=other is not null&&users.TryGetValue(other.UserId,out var u)&&presence.Online(other.UserId,u.TokenVersion)?"ONLINE":"OFFLINE";
                row["participantsCount"]=participants.Count(p=>p.ChatRoomId==roomId);row["privateUserLowId"]=room.PrivateUserLowId;row["privateUserHighId"]=room.PrivateUserHighId;
                var last=lastMessages.FirstOrDefault(m=>m.ChatRoomId==roomId);row["lastMessage"]=last is null?null:Node(new{last.MessageId,last.ChatRoomId,last.SenderId,last.Content,senderName=users.GetValueOrDefault(last.SenderId)?.FullName,sentAt=DateTime.SpecifyKind(last.SentAt,DateTimeKind.Utc)});row["lastMessageSenderName"]=last is null?null:users.GetValueOrDefault(last.SenderId)?.FullName;
            }
            if(Id(row,"notificationId") is long nid&&notices.TryGetValue(nid,out var notice))
            {
                row["message"]=notice.Content;row["referenceType"]=notice.RefType;row["referenceId"]=notice.RefId;
                var p=await TargetProject(notice.RefType,notice.RefId,ct);
                var actor=Person(notice.ActorId,p);
                var actorProjectRole=p.HasValue&&notice.ActorId.HasValue?await db.ProjectMembers.Where(m=>m.ProjectId==p&&m.UserId==notice.ActorId).Select(m=>m.Role.RoleName).SingleOrDefaultAsync(ct):null;if(actor is JsonObject a){a["actorId"]=notice.ActorId;a["actorName"]=a["fullName"]?.DeepClone();a["actorAvatarCode"]=a["avatarCode"]?.DeepClone();a["actorRole"]=notice.ActorRole??actorProjectRole??Role(notice.ActorId,p);}row["actor"]=actor;
                row["actorAvatarUrl"]=actor?["avatarUrl"]?.DeepClone();row["actorName"]=actor?["actorName"]?.DeepClone();row["actorAvatarCode"]=actor?["actorAvatarCode"]?.DeepClone();row["actorRole"]=actor?["actorRole"]?.DeepClone();
                row["targetTitle"]=notice.TargetTitle??await Target(notice.RefType,notice.RefId,ct);row["isUrgent"]=notice.Type.Contains("WIP",StringComparison.OrdinalIgnoreCase)||notice.Type.Contains("OVERDUE",StringComparison.OrdinalIgnoreCase);row["isSystemAction"]=notice.ActorId==null&&notice.RefType=="SYSTEM";row["actorUnknown"]=notice.ActorId==null&&notice.RefType!="SYSTEM";
            }
            if(Id(row,"activityLogId") is long lid&&logs.TryGetValue(lid,out var log))
            {
                row["userName"]=log.UserId.HasValue?users.GetValueOrDefault(log.UserId.Value)?.FullName:null;row["userAvatarCode"]=log.UserId.HasValue&&users.TryGetValue(log.UserId.Value,out var u)?Initials(u.FullName):null;row["userAvatarUrl"]=Person(log.UserId,log.ProjectId)?["avatarUrl"]?.DeepClone();row["userRole"]=Role(log.UserId,log.ProjectId);row["isSystemAction"]=log.UserId==null;row["entityTitle"]=await Target(log.EntityType,log.EntityId,ct);row["changes"]=log.ChangesJson is null?null:JsonNode.Parse(log.ChangesJson);row.Remove("changesJson");
            }
        }
        return root;
    }
    async Task<long?> TargetProject(string? type,long? id,CancellationToken ct)=>type switch
    {
        "TASK"=>await db.Tasks.Where(t=>t.TaskId==id).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct),
        "BUG"=>await db.Bugs.Where(t=>t.BugId==id).Select(t=>(long?)t.Task.ProjectId).SingleOrDefaultAsync(ct),
        "PROJECT"=>id,
        "COMMENT"=>await db.Comments.Where(t=>t.CommentId==id).Select(t=>(long?)t.Task.ProjectId).SingleOrDefaultAsync(ct),
        "SPRINT"=>await db.Sprints.Where(t=>t.SprintId==id).Select(t=>(long?)t.ProjectId).SingleOrDefaultAsync(ct),
        "MESSAGE"=>await db.Messages.Where(t=>t.MessageId==id).Select(t=>t.ChatRoom.ProjectId).SingleOrDefaultAsync(ct),_=>null
    };
    async Task<string?> Target(string? type,long? id,CancellationToken ct)=>type switch
    {
        "USER"=>await db.Users.Where(t=>t.UserId==id).Select(t=>t.FullName).SingleOrDefaultAsync(ct),
        "TASK"=>await db.Tasks.Where(t=>t.TaskId==id).Select(t=>t.Title).SingleOrDefaultAsync(ct),
        "BUG"=>await db.Bugs.Where(t=>t.BugId==id).Select(t=>t.Title).SingleOrDefaultAsync(ct),
        "EPIC"=>await db.Epics.Where(t=>t.EpicId==id).Select(t=>t.Title).SingleOrDefaultAsync(ct),
        "PROJECT"=>await db.Projects.Where(t=>t.ProjectId==id).Select(t=>t.ProjectName).SingleOrDefaultAsync(ct),
        "MESSAGE"=>await db.Messages.Where(t=>t.MessageId==id).Select(t=>t.Content).SingleOrDefaultAsync(ct),
        _=>null
    };
}
