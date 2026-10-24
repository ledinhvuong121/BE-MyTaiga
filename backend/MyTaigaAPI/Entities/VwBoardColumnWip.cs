using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class VwBoardColumnWip
{
    public long ProjectId { get; set; }

    public long BoardColumnId { get; set; }

    public string ColumnName { get; set; } = null!;

    public int Position { get; set; }

    public int? WipLimit { get; set; }

    public int? CurrentCount { get; set; }

    public bool? IsOverLimit { get; set; }
}
