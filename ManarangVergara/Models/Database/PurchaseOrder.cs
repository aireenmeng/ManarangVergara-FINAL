using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

// MODEL: THE RESTOCKING LOG (DELIVERIES)
// this file tracks when you buy new stock from outside sellers.
// it records who sent it, what they sent, and when it arrived.
public partial class PurchaseOrder
{
    // unique id number for this specific delivery record.
    public int PoId { get; set; }

    // who did we buy this from? links to the supplier ID.
    public int SupplierId { get; set; }

    // what item did we order? links to the product ID.
    public int ProductId { get; set; }

    // how many items actually arrived in the box?
    // the "?" means this can be blank (null) if we placed the order but it hasn't arrived yet.
    public int? QuantityReceIved { get; set; }

    // the exact date the delivery truck arrived at the pharmacy.
    public DateTime? DateReceived { get; set; }

    // CONNECTOR:
    // lets the code look up the product name (e.g., "Biogesic") based on the ID above.
    public virtual Product Product { get; set; } = null!;

    // CONNECTOR:
    // lets the code look up the supplier name (e.g., "Zuellig Pharma") based on the ID above.
    public virtual Supplier Supplier { get; set; } = null!;
}