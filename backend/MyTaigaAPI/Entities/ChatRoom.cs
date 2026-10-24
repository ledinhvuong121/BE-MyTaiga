using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class ChatRoom
{
    public long ChatRoomId { get; set; }

    public long? ProjectId { get; set; }

    public string RoomType { get; set; } = null!;

    public string? RoomName { get; set; }

    public long? PrivateUserLowId { get; set; }

    public long? PrivateUserHighId { get; set; }

    public long? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<ChatParticipant> ChatParticipants { get; set; } = new List<ChatParticipant>();

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual User? PrivateUserHigh { get; set; }

    public virtual User? PrivateUserLow { get; set; }

    public virtual Project? Project { get; set; }
}
