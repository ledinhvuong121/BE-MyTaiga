using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Attachment
{
    public long AttachmentId { get; set; }

    public long TaskId { get; set; }

    public string FileName { get; set; } = null!;

    public string FilePath { get; set; } = null!;

    public long FileSize { get; set; }

    public long UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; }

    public virtual Task Task { get; set; } = null!;

    public virtual User UploadedByNavigation { get; set; } = null!;
}
