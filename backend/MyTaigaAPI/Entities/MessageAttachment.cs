using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class MessageAttachment
{
    public long MessageAttachmentId { get; set; }

    public long MessageId { get; set; }

    public string FileName { get; set; } = null!;

    public string FilePath { get; set; } = null!;

    public long FileSize { get; set; }

    public virtual Message Message { get; set; } = null!;
}
