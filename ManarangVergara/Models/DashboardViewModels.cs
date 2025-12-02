namespace ManarangVergara.Models
{
    // The Main container for the Dashboard View
    public class DashboardViewModel
    {
        // --- KPI Tiles ---
        public decimal DailyGrossProfit { get; set; }
        public decimal TotalStockValue { get; set; }
        public int LowStockCount { get; set; }
        public int NearExpiryCount { get; set; }

        // --- Lists ---
        public List<ProactiveAlertVM> ProactiveAlerts { get; set; } = new();
        public List<TransactionPreviewVM> RecentTransactions { get; set; } = new();



        // Bar Chart: Sales for the last 7 days
        public string[] BarChartLabels { get; set; } = Array.Empty<string>();
        public decimal[] BarChartData { get; set; } = Array.Empty<decimal>();

        // Pie Chart: Top 5 Categories by Sales
        public string[] PieChartLabels { get; set; } = Array.Empty<string>();
        public int[] PieChartData { get; set; } = Array.Empty<int>();
    }


    public class ProactiveAlertVM
    {
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public int DaysLeft { get; set; }
        public string AlertType { get; set; } = ""; // "Low Stock", "Near Expiry", or "BOTH"
        public string Urgency { get; set; } = ""; // "Critical" (Red), "Warning" (Yellow)
    }


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