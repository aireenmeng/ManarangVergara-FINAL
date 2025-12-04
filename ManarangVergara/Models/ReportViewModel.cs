using ManarangVergara.Models.Database;

namespace ManarangVergara.Models
{
    public class ReportViewModel
    {
        // --- FILTERS ---
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // --- TAB 1: SALES LEDGER ---
        public List<DetailedSalesData> DetailedSales { get; set; } = new();
        public decimal TotalRevenue { get; set; }

        // --- TAB 2: INVENTORY MASTER ---
        public List<DetailedInventoryData> FullInventory { get; set; } = new();
        public decimal TotalAssetValue { get; set; }

        // --- TAB 3: FINANCIAL ANALYSIS (MERGED) ---
        public List<ProductProfitData> ProductProfitability { get; set; } = new();
        public decimal GrossProfit { get; set; } // Revenue - Cost of Sold items
        public decimal TotalLossValue { get; set; } // Cost of Damaged items
        public decimal NetProfit => GrossProfit - TotalLossValue; // True Bottom Line

        // --- TAB 4: AUDIT LOG ---
        public List<VoidHistoryViewModel> VoidLogs { get; set; } = new();

        public string GeneratedBy { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
        public bool ShowFinancials { get; set; }
    }

    // --- HELPER CLASSES ---

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

        // SALES DATA
        public int QtySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal CostOfGoodsSold { get; set; } // Cost of items sold
        public decimal GrossProfit => Revenue - CostOfGoodsSold;

        // LOSS DATA (The new part)
        public int QtyLost { get; set; } // Damaged / Expired count
        public decimal LossValue { get; set; } // Cost of damaged items

        // FINAL CALCULATION
        // We subtract the loss from the gross profit to see if we actually made money
        public decimal NetProfit => GrossProfit - LossValue;
    }

    public class DetailedInventoryData
    {
        public string ProductName { get; set; } = "";
        public string BatchNo { get; set; } = "";
        public DateOnly ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public string Status { get; set; } = "";
        public int DaysUntilExpiry { get; set; }
    }
}