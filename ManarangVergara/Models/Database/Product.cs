using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class Product
{
    public int ProductId { get; set; }

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string Manufacturer { get; set; } = null!;

    public int CategoryId { get; set; }

    public int SupplierId { get; set; }

    public ulong IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ProductCategory Category { get; set; } = null!;

    public virtual ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();

    public virtual ICollection<ItemLog> ItemLogs { get; set; } = new List<ItemLog>();

    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();

    public virtual ICollection<SalesItem> SalesItems { get; set; } = new List<SalesItem>();

    public virtual Supplier Supplier { get; set; } = null!;
}
