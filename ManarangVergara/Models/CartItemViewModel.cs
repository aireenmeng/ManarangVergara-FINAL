namespace ManarangVergara.Models
{
    public class CartItemViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; }

        public decimal DiscountRate { get; set; } = 0; 
        public decimal DiscountAmount => (Price * Quantity) * DiscountRate;
        public decimal Total => (Price * Quantity) - DiscountAmount;
    }
}