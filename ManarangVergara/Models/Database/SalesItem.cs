using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class SalesItem
{
    public int SalesItemId { get; set; }

    public int SalesId { get; set; }

    public int ProductId { get; set; }

    public int QuantitySold { get; set; }

    public decimal Price { get; set; }

    public decimal Discount { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual Transaction Sales { get; set; } = null!;
}
