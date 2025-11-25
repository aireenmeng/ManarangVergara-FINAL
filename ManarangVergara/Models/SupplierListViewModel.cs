namespace ManarangVergara.Models
{
    public class SupplierListViewModel
    {
        public int SupplierId { get; set; }
        public string Name { get; set; } = "";
        public string ContactInfo { get; set; } = "";
        public int ProductCount { get; set; } // How many products we get from them
        public bool CanDeletePermanently { get; set; } // true if no sales/orders exist
    }
}