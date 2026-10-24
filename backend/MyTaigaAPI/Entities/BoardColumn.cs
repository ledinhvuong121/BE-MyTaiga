using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class BoardColumn
{
    public long BoardColumnId { get; set; }

    public long ProjectId { get; set; }

    public string ColumnName { get; set; } = null!;

    public int Position { get; set; }

    public int? WipLimit { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();
}
