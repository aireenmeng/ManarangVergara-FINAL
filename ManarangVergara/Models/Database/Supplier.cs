using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

// MODEL: THE VENDORS / WHOLESALERS
// this tracks the companies you buy stock from (e.g., "zuellig pharma", "unilab", or a local distributor).
public partial class Supplier
{
    // unique id for this company.
    public int SupplierId { get; set; }

    // the company name (e.g., "Metro Drug Inc.").
    public string Name { get; set; } = null!;

    // phone number, email, or address so you know how to order more stock.
    // the "?" means this can be left blank if you don't have it yet.
    public string? ContactInfo { get; set; }



    // SOFT DELETE SWITCH:
    // if true = this supplier shows up in the "order stock" dropdown.
    // if false = you stopped doing business with them, but we keep their record for history.
    public bool IsActive { get; set; } = true;

    // timestamp: remembers the last time you updated their phone number or status.
    public DateTime? LastUpdated { get; set; }



    // CONNECTOR:
    // a list of all the different medicines this specific supplier sells to us.
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    // CONNECTOR:
    // a history of every delivery transaction we have ever done with this company.
    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}