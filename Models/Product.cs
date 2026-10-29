namespace InventoryCore.Models
{
    public class Product
    {
    public Guid Id { get; set;} = Guid.CreateVersion7();

    //SKU : stock keeping unit, a unique identifier for each product
    public string Sku { get; set; } ="";

    public string Name { get; set=> field= value.Trim(); } ="";

    public decimal Price { get; set; }
    public int StockQuality { get; set;}
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    }
}