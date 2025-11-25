using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

// MODEL: THE STOCKROOM (BATCHES)
// this doesn't just list "paracetamol". it lists specific boxes of paracetamol 
// so we can track which ones expire first (fifo logic).
public partial class Inventory
{
    public int InventoryId { get; set; }

    // link to the main product list. tells us "this box contains paracetamol 500mg".
    public int ProductId { get; set; }

    // how many tablets/bottles are left in this specific box/batch?
    public int Quantity { get; set; }

    // critical for pharmacy: when does this specific batch go bad?
    public DateOnly ExpiryDate { get; set; }

    // how much we paid the supplier for this item (capital).
    public decimal CostPrice { get; set; }

    // how much we sell it to the customer for (srp).
    public decimal SellingPrice { get; set; }

    // the manufacture code printed on the box. useful if there is a factory recall.
    public string BatchNumber { get; set; } = null!;

    // simple flag: 0 = good, 1 = expired. 
    // the system checks dates automatically and flips this switch so we don't sell bad meds.
    public sbyte IsExpired { get; set; }

    // remembers the exact moment this stock number changed (sold or restocked).
    public DateTime LastUpdated { get; set; }

    // CONNECTOR:
    // allows the code to reach over to the product table and grab the name (e.g., "Biogesic").
    public virtual Product Product { get; set; } = null!;
}