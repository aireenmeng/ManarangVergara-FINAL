using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

// MODEL: THE RECEIPT LINE (DETAIL)
// if "transaction" is the whole paper receipt, this class is just one single row on it.
// e.g., "3x Biogesic - 15.00".
public partial class SalesItem
{
    // unique id for this specific line item.
    public int SalesItemId { get; set; }

    // link to the main receipt. tells us which customer bought this.
    public int SalesId { get; set; }

    // link to the product. tells us what item was bought.
    public int ProductId { get; set; }

    // how many did they buy?
    public int QuantitySold { get; set; }

    // records the price at the exact moment of sale.
    // important: we save this separately because the main product price might change next week,
    // but we need to remember exactly what this customer paid today.
    public decimal Price { get; set; }

    // how much was deducted from this specific item (if any).
    public decimal Discount { get; set; }

    // CONNECTOR:
    // lets us see the name of the product (e.g., "Biogesic").
    public virtual Product Product { get; set; } = null!;

    // CONNECTOR:
    // lets us jump back to the main receipt to see the total total or the cashier name.
    public virtual Transaction Sales { get; set; } = null!;
}