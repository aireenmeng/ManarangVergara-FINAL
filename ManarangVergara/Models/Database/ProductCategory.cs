using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class ProductCategory
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    // --- NEW COLUMNS ---
    public bool IsActive { get; set; } = true;
    public DateTime? LastUpdated { get; set; }
    // -------------------

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}