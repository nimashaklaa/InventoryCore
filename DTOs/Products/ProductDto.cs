namespace InventoryCore.DTOs.Products
{
    public class ProductDto
    {
        public Guid Id { get; set; }
        public required string Sku { get; set; }
        public required string Name { get; set; }
        public decimal Price { get; set; }
        public int StockQuality { get; set; }
        public Guid CategoryId { get; set; }
    }
}