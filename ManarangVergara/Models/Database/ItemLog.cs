using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class ItemLog
{
    public int LogId { get; set; }

    public int ProductId { get; set; }

    public string Action { get; set; } = null!;

    public int Quantity { get; set; }

    public int EmployeeId { get; set; }

    public DateTime LoggedAt { get; set; }

    public string LogReason { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}
