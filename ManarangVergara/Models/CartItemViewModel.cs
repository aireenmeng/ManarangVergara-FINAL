namespace ManarangVergara.Models
{
    public class CartItemViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; }

        // NEW: Discount properties
        public decimal DiscountRate { get; set; } = 0; // e.g., 0.20 for 20% off
        public decimal DiscountAmount => (Price * Quantity) * DiscountRate;
        public decimal Total => (Price * Quantity) - DiscountAmount;
    }
}