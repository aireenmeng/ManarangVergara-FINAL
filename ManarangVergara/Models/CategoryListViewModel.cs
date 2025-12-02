namespace ManarangVergara.Models
{
    public class CategoryListViewModel
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public int ProductCount { get; set; } 
        public bool CanDeletePermanently { get; set; }
    }
}