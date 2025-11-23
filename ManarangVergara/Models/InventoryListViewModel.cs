using System;

namespace ManarangVergara.Models
{
    public class InventoryListViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public int Quantity { get; set; }
        public decimal SellingPrice { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public string Status { get; set; } = "Active";
        public string BatchNumber { get; set; } = "";
    }
}