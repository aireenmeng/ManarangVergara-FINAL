namespace ManarangVergara.Models
{
    // The Main container for the Dashboard View
    public class DashboardViewModel
    {
        // KPI Tiles
        public decimal DailyGrossProfit { get; set; }
        public decimal TotalStockValue { get; set; }
        public int LowStockCount { get; set; }
        public int NearExpiryCount { get; set; }

        // The two required lists
        public List<ProactiveAlertVM> ProactiveAlerts { get; set; } = new();
        public List<TransactionPreviewVM> RecentTransactions { get; set; } = new();
    }

    // Innovation: Dedicated VM for the alerts with specific status flags
    public class ProactiveAlertVM
    {
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public int DaysLeft { get; set; }
        public string AlertType { get; set; } = ""; // "Low Stock", "Near Expiry", or "BOTH"
        public string Urgency { get; set; } = ""; // "Critical" (Red), "Warning" (Yellow)
    }

    // For the simple transaction preview list
    public class TransactionPreviewVM
    {
        public int TransactionId { get; set; }
        public DateTime Date { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string CashierName { get; set; } = "";
        public int ItemCount { get; set; }
    }
}