using System.ComponentModel.DataAnnotations;

namespace ManarangVergara.Models
{
    public class TransactionViewModel
    {
        // The list of items currently in the cart
        public List<CartItemViewModel> Cart { get; set; } = new();

        // Transaction Totals
        [DisplayFormat(DataFormatString = "{0:N2}")]
        public decimal GrandTotal => Cart.Sum(x => x.Total);

        // Payment Details
        [Required(ErrorMessage = "Please select a payment method.")]
        public string PaymentMethod { get; set; } = "Cash"; // Default

        // For the Product Search dropdown on the POS screen
        public int SelectedProductId { get; set; }
        public int SelectedQuantity { get; set; } = 1;
    }
}