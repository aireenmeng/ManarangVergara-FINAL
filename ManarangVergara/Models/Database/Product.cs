using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

// MODEL: THE MASTER CATALOG
// this defines what a product "is" (e.g., "Biogesic 500mg"), but not how many we have.
// think of this as the index card in a library catalog.
public partial class Product
{
    // unique id number used by the computer to find this item quickly.
    public int ProductId { get; set; }

    // what the customer and cashier see (e.g., "Biogesic").
    public string Name { get; set; } = null!;

    // extra details like dosage (e.g., "500mg tablet for headache").
    public string Description { get; set; } = null!;

    // who makes it (e.g., "Unilab").
    public string Manufacturer { get; set; } = null!;

    // LINKS TO OTHER TABLES:
    // instead of typing "Antibiotics" every time, we just save the ID number of that category.
    public int CategoryId { get; set; }
    public int SupplierId { get; set; }

    // SOFT DELETE SWITCH:
    // if we stop selling this, we turn this to 0 (false) instead of deleting it.
    // this keeps old sales records safe.
    public ulong IsActive { get; set; }

    // when was this product first added to our system?
    public DateTime CreatedAt { get; set; }

    // CONNECTOR:
    // lets us see the category name (e.g., "Vitamins") instead of just ID #5.
    public virtual ProductCategory Category { get; set; } = null!;

    // CONNECTOR:
    // links to the inventory table. one product name can have many different batches/boxes on the shelf.
    public virtual ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();

    // CONNECTOR:
    // history of every time someone edited this product's stock.
    public virtual ICollection<ItemLog> ItemLogs { get; set; } = new List<ItemLog>();

    // CONNECTOR:
    // history of every time we ordered this from a supplier.
    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();

    // CONNECTOR:
    // history of every time this item was sold to a customer.
    public virtual ICollection<SalesItem> SalesItems { get; set; } = new List<SalesItem>();

    // CONNECTOR:
    // lets us see the supplier's name and phone number.
    public virtual Supplier Supplier { get; set; } = null!;
}