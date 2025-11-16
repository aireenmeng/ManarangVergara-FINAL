using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class Inventory
{
    public int InventoryId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public DateOnly ExpiryDate { get; set; }

    public decimal CostPrice { get; set; }

    public decimal SellingPrice { get; set; }

    public string BatchNumber { get; set; } = null!;

    public sbyte IsExpired { get; set; }

    public DateTime LastUpdated { get; set; }

    public virtual Product Product { get; set; } = null!;
}
