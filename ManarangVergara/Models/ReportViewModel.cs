using ManarangVergara.Models.Database;

namespace ManarangVergara.Models
{
    public class ReportViewModel
    {
        // --- FILTERS ---
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // --- TAB 1: SALES & REVENUE ---
        public decimal TotalRevenue { get; set; }
        public int TransactionCount { get; set; }
        public List<CategorySalesData> SalesByCategory { get; set; } = new();
        public List<TopProductData> TopSellingProducts { get; set; } = new();

        // --- TAB 2: INVENTORY HEALTH ---
        public decimal TotalInventoryValue { get; set; } // Asset Value
        public List<InventoryListViewModel> NearExpiryItems { get; set; } = new();
        public List<InventoryListViewModel> LowStockItems { get; set; } = new();

        // --- TAB 3: PROFITABILITY ---
        public decimal GrossProfit { get; set; } // Revenue - Cost
        public List<ProductProfitData> ProductProfitability { get; set; } = new();

        // --- TAB 4: AUDIT & LOSS ---
        public List<VoidHistoryViewModel> RecentVoids { get; set; } = new();

        // --- METADATA ---
        public string GeneratedBy { get; set; } = "";
        public DateTime GeneratedAt { get; set; }

        // --- SECURITY FLAG ---
        public bool ShowFinancials { get; set; } // True = Owner/Admin, False = Manager
    }

    // Helper Classes for the Charts/Tables
    public class CategorySalesData
    {
        public string Category { get; set; } = "";
        public decimal TotalSales { get; set; }
    }

    public class TopProductData
    {
        public string ProductName { get; set; } = "";
        public int QuantitySold { get; set; }
        public decimal Revenue { get; set; }
    }

    public class ProductProfitData
    {
        public string ProductName { get; set; } = "";
        public int QtySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public decimal Profit => Revenue - Cost;
        public decimal MarginPercent => Revenue > 0 ? (Profit / Revenue) * 100 : 0;
    }


}