using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace ManarangVergara.Models
{
    public class ProductFormViewModel
    {
        // --- PRODUCT Details ---
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Product Name is required.")]
        public string Name { get; set; } = "";

        public string? Description { get; set; }

        [Required(ErrorMessage = "Manufacturer is required.")]
        public string Manufacturer { get; set; } = "";

        [Display(Name = "Category")]
        [Required(ErrorMessage = "Please select a category.")]
        public int CategoryId { get; set; }

        [Display(Name = "Supplier")]
        [Required(ErrorMessage = "Please select a supplier.")]
        public int SupplierId { get; set; }

        // --- INVENTORY Initial Batch Details ---
        // We only need these when ADDING a new product for the first time.

        [Required]
        [Range(0, 999999)]
        [Display(Name = "Initial Stock")]
        public int Quantity { get; set; }

        [Required]
        [Range(0.01, 999999)]
        [Display(Name = "Cost Price")]
        public decimal CostPrice { get; set; }

        [Required]
        [Range(0.01, 999999)]
        [Display(Name = "Selling Price")]
        public decimal SellingPrice { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Expiry Date")]
        public DateTime ExpiryDate { get; set; } = DateTime.Today.AddYears(1); // Default to 1 year from now

        [Required]
        [Display(Name = "Batch Number")]
        public string BatchNumber { get; set; } = "";

        // --- DROPDOWN LISTS ---
        // These hold the data for the <select> HTML elements
        public IEnumerable<SelectListItem>? CategoryList { get; set; }
        public IEnumerable<SelectListItem>? SupplierList { get; set; }
    }
}