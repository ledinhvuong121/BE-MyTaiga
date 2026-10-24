using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Message
{
    public long MessageId { get; set; }

    public long ChatRoomId { get; set; }

    public long SenderId { get; set; }

    public string? Content { get; set; }

    public DateTime SentAt { get; set; }

    public virtual ChatParticipant ChatParticipant { get; set; } = null!;

    public virtual ChatRoom ChatRoom { get; set; } = null!;

    public virtual ICollection<MessageAttachment> MessageAttachments { get; set; } = new List<MessageAttachment>();
}
