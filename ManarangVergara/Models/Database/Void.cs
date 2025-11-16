using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class Void
{
    public int VoidId { get; set; }

    public int SalesId { get; set; }

    public int EmployeeId { get; set; }

    public DateTime VoidedAt { get; set; }

    public string VoidReason { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;

    public virtual Transaction Sales { get; set; } = null!;
}
