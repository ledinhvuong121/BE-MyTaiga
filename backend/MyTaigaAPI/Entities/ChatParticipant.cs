using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class ChatParticipant
{
    public long ChatRoomId { get; set; }

    public long UserId { get; set; }

    public DateTime JoinedAt { get; set; }

    public DateTime? LastReadAt { get; set; }

    public virtual ChatRoom ChatRoom { get; set; } = null!;

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual User User { get; set; } = null!;
}
