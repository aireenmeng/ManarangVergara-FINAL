using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class PurchaseOrder
{
    public int PoId { get; set; }

    public int SupplierId { get; set; }

    public int ProductId { get; set; }

    public int? QuantityReceIved { get; set; }

    public DateTime? DateReceived { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual Supplier Supplier { get; set; } = null!;
}
