using System.ComponentModel.DataAnnotations;

namespace InventoryCore.DTOs.Products;
public record ProductResponse
(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,   
    int StockQuality,
    Guid CategoryId 
);
    
public record CreateProductRequest(
    [Required, StringLength(64)] string Sku,
    [Required, StringLength(300)] string Name,
    [Range(0.01, 1_000_000)] decimal Price,
    [Range(0, int.MaxValue)] int StockQuantity,
    Guid CategoryId);