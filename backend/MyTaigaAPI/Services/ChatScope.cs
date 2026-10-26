using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Entities;
namespace MyTaigaAPI.Services;
public static class ChatScope
{
    public static IQueryable<ChatRoom> Visible(ScrumbanDbContext db,long userId)=>db.ChatRooms.Where(r=>
        r.ChatParticipants.Any(p=>p.UserId==userId&&p.IsActive)&&
        (r.RoomType=="GROUP" ? r.Project!.Status!="DELETED"&&r.Project.ProjectMembers.Any(m=>m.UserId==userId):
        db.ProjectMembers.Any(a=>a.UserId==r.PrivateUserLowId&&a.Project.Status!="DELETED"&&
            db.ProjectMembers.Any(b=>b.ProjectId==a.ProjectId&&b.UserId==r.PrivateUserHighId))));
    public static object Details(Message m)=>new{m.MessageId,m.ChatRoomId,m.SenderId,m.Content,sentAt=DateTime.SpecifyKind(m.SentAt,DateTimeKind.Utc),
        attachments=m.MessageAttachments.Select(a=>new{a.MessageAttachmentId,a.FileName,a.FileSize,downloadUrl=$"/api/v1/chat/attachments/{a.MessageAttachmentId}"})};
}
