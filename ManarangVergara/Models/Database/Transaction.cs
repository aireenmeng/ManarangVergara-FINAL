using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class Transaction
{
    public int SalesId { get; set; }

    public int EmployeeId { get; set; }

    public DateTime SalesDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public string Status { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;

    public virtual ICollection<SalesItem> SalesItems { get; set; } = new List<SalesItem>();

    public virtual ICollection<Void> Voids { get; set; } = new List<Void>();
}
