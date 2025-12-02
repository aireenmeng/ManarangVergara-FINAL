using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

// MODEL: THE CATEGORY LABELS
// this acts like the aisle signs in a supermarket (e.g., "vitamins", "antibiotics").
// it helps organize thousands of products into manageable groups so they are easier to find.
public partial class ProductCategory
{
    // unique id number for the database to track this category.
    public int CategoryId { get; set; }

    // the actual name displayed to the user (e.g., "baby care" or "pain killers").
    public string CategoryName { get; set; } = null!;



    // SOFT DELETE SWITCH:
    // if true = this category shows up in the dropdown list when adding new products.
    // if false = it's hidden/deleted, but we keep the record so old sales data doesn't break.
    public bool IsActive { get; set; } = true;

    // timestamp: remembers the last time someone changed the name or status of this category.
    public DateTime? LastUpdated { get; set; }



    // CONNECTOR:
    // this creates a list of all products that belong to this specific category.
    // e.g., if this category is "cough syrup", this list automatically collects "robitussin", "solmux", etc.
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}