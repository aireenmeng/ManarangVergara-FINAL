using ManarangVergara.Models.Database;

namespace ManarangVergara.Models
{
    public class ReportViewModel
    {
        // --- FILTERS (Inputs) ---
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? PaymentMethod { get; set; }
        public int? CashierId { get; set; }

        // --- DROPDOWN DATA ---
        public IEnumerable<Employee>? CashierList { get; set; }

        // --- RESULTS (Outputs) ---
        public List<Transaction> Transactions { get; set; } = new();
        public decimal TotalRevenue { get; set; }
        public int TransactionCount { get; set; }

        // --- METADATA (For Print) ---
        public string GeneratedBy { get; set; } = "";
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }
}