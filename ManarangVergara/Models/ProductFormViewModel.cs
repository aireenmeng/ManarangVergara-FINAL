using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace ManarangVergara.Models
{
    public class ProductFormViewModel
    {
        // --- PRODUCT DETAILS ---
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Product Name is required.")]
        public string Name { get; set; } = "";

        public string? Description { get; set; }

        [Required(ErrorMessage = "Manufacturer is required.")]
        public string Manufacturer { get; set; } = "";

        [Display(Name = "Category")]
        [Required(ErrorMessage = "Please select a category.")]
        public int CategoryId { get; set; }

        // --- SUPPLIER LOGIC ---
        [Display(Name = "Supplier")]
        public int? SupplierId { get; set; } // Nullable now, because user might pick "New"

        [Display(Name = "New Supplier Name")]
        public string? NewSupplierName { get; set; } // Only used if adding new

        // --- INITIAL BATCH DETAILS ---
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
        public DateTime ExpiryDate { get; set; }

        [Required]
        [Display(Name = "Batch Number")]
        public string BatchNumber { get; set; } = "";

        // --- DROPDOWNS ---
        public IEnumerable<SelectListItem>? CategoryList { get; set; }
        public IEnumerable<SelectListItem>? SupplierList { get; set; }
    }
}