namespace ManarangVergara.Models
{
    public class TransactionListViewModel
    {
        public int TransactionId { get; set; }
        public DateTime Date { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = "";
        public int ItemCount { get; set; }
        public string Status { get; set; } = "";
        public string CashierName { get; set; } = "";
    }
}