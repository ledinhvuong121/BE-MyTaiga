using MyTaigaAPI.Data;
using MyTaigaAPI.Entities;

namespace MyTaigaAPI.Services;

public static class NotificationScope
{
    public static IQueryable<Notification> Visible(ScrumbanDbContext db,long userId)=>db.Notifications.Where(n=>n.UserId==userId&&(
        n.RefType==null||n.RefType=="SYSTEM"||(n.RefType=="USER"&&n.RefId==userId)||
        (n.RefType=="PROJECT"&&db.Projects.Any(p=>p.ProjectId==n.RefId&&p.Status!="DELETED"&&p.ProjectMembers.Any(m=>m.UserId==userId)))||
        (n.RefType=="TASK"&&db.Tasks.Any(t=>t.TaskId==n.RefId&&t.Project.Status!="DELETED"&&t.Project.ProjectMembers.Any(m=>m.UserId==userId)))||
        (n.RefType=="BUG"&&db.Bugs.Any(b=>b.BugId==n.RefId&&b.Task.Project.Status!="DELETED"&&b.Task.Project.ProjectMembers.Any(m=>m.UserId==userId)))||
        (n.RefType=="COMMENT"&&db.Comments.Any(c=>c.CommentId==n.RefId&&c.Task.Project.Status!="DELETED"&&c.Task.Project.ProjectMembers.Any(m=>m.UserId==userId)))||
        (n.RefType=="SPRINT"&&db.Sprints.Any(s=>s.SprintId==n.RefId&&s.Project.Status!="DELETED"&&s.Project.ProjectMembers.Any(m=>m.UserId==userId)))||
        (n.RefType=="ATTACHMENT"&&db.Attachments.Any(a=>a.AttachmentId==n.RefId&&a.Task.Project.Status!="DELETED"&&a.Task.Project.ProjectMembers.Any(m=>m.UserId==userId)))||
        (n.RefType=="CHAT_ROOM"&&db.ChatParticipants.Any(p=>p.ChatRoomId==n.RefId&&p.UserId==userId&&(p.ChatRoom.ProjectId==null||
            (p.ChatRoom.Project!.Status!="DELETED"&&p.ChatRoom.Project.ProjectMembers.Any(m=>m.UserId==userId)))))||
        (n.RefType=="MESSAGE"&&db.Messages.Any(message=>message.MessageId==n.RefId&&message.ChatRoom.ChatParticipants.Any(p=>p.UserId==userId)&&
            (message.ChatRoom.ProjectId==null||(message.ChatRoom.Project!.Status!="DELETED"&&message.ChatRoom.Project.ProjectMembers.Any(m=>m.UserId==userId)))))
        ));
    public static object Details(Notification n)=>new{n.NotificationId,n.Type,n.Title,n.Content,n.RefType,n.RefId,n.IsRead,
        createdAt=DateTime.SpecifyKind(n.CreatedAt,DateTimeKind.Utc),readAt=n.ReadAt.HasValue?DateTime.SpecifyKind(n.ReadAt.Value,DateTimeKind.Utc):(DateTime?)null};
}
