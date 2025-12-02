namespace ManarangVergara.Models
{
    public class VoidHistoryViewModel
    {
        public int TransactionId { get; set; }
        public DateTime VoidDate { get; set; }
        public string CashierName { get; set; } = "";
        public string ManagerName { get; set; } = ""; // for approved the void
        public decimal TotalAmount { get; set; }
        public string Reason { get; set; } = "";
    }
}