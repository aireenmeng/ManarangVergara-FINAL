namespace ManarangVergara.Models
{
    public class CategoryListViewModel
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public int ProductCount { get; set; } // New: Stores how many products are in this category
    }
}