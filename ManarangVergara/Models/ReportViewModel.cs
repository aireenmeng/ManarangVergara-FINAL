using ManarangVergara.Models.Database;

namespace ManarangVergara.Models
{
    public class ReportViewModel
    {
        // --- FILTERS ---
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // --- TAB 1: DETAILED SALES REPORT ---
        // Changed from summary to full list
        public List<DetailedSalesData> DetailedSales { get; set; } = new();
        public decimal TotalRevenue { get; set; }

        // --- TAB 2: FULL INVENTORY STATUS ---
        // Changed from "Near Expiry" only to "All Items"
        public List<DetailedInventoryData> FullInventory { get; set; } = new();
        public decimal TotalAssetValue { get; set; }

        // --- TAB 3: PROFITABILITY TABLE ---
        // Kept tabular, but made more detailed
        public List<ProductProfitData> ProductProfitability { get; set; } = new();
        public decimal GrossProfit { get; set; }

        // --- TAB 4: AUDIT LOG ---
        public List<VoidHistoryViewModel> VoidLogs { get; set; } = new();

        // --- METADATA ---
        public string GeneratedBy { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
        public bool ShowFinancials { get; set; }
    }

    // --- NEW: Helper Class for Tabular Data ---
    public class DetailedSalesData
    {
        public DateTime Date { get; set; }
        public string TransactionId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Category { get; set; } = "";
        public int Qty { get; set; }
        public decimal Price { get; set; }
        public decimal Total => Qty * Price;
        public string Cashier { get; set; } = "";
    }

    public class ProductProfitData
    {
        public string ProductName { get; set; } = "";
        public int QtySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        // Calculated properties
        public decimal Profit => Revenue - Cost;
        public decimal MarginPercent => Revenue > 0 ? (Profit / Revenue) * 100 : 0;
    }

    public class DetailedInventoryData
    {
        public string ProductName { get; set; } = "";
        public string BatchNo { get; set; } = "";
        public DateOnly ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public string Status { get; set; } = ""; // Active, Low, Expired
        public int DaysUntilExpiry { get; set; }

        // VISUAL HELPER: Calculates a % for the visual bar (Max 100 for graph scaling)
        public int StockLevelPercent => Quantity > 100 ? 100 : Quantity;
    }
}