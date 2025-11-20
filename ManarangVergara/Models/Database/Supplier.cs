using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class Supplier
{
    public int SupplierId { get; set; }

    public string Name { get; set; } = null!;

    public string? ContactInfo { get; set; }

    // --- NEW COLUMNS ---
    public bool IsActive { get; set; } = true;
    public DateTime? LastUpdated { get; set; }
    // -------------------

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}