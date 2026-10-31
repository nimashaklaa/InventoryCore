using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryCore.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryCore.Data
{
    public static class Seed
    {
        public static async Task RunAsync(AppDbContext db)
        {
            var categoriesByName = await db.Categories
                .ToDictionaryAsync(category => category.Name, category => category.Id);
            foreach (var categoryName in new[] { "Electronics", "Accessories", "Office Supplies" })
            {
                if (!categoriesByName.ContainsKey(categoryName))
                {
                    var category = new Category { Name = categoryName };
                    db.Categories.Add(category);
                    categoriesByName.Add(categoryName, category.Id);
                }
            }
            await db.SaveChangesAsync();

            string[] productSkus = {
                "LAP-001", "LAP-002", "DESK-001", "MON-001", "MON-002", "TAB-001", "PHN-001", "PHN-002", "SRV-001", "LPT-001",
                "MOU-001", "KEY-001", "BAG-001", "DOC-001", "HUB-001", "CAM-001", "MIC-001", "HDP-001", "SPE-001", "PAD-001",
                "PEN-001", "NOT-001", "CAL-001", "STP-001", "CHL-001", "FIL-001", "SHR-001", "LMP-001", "WHT-001", "COR-001"
            };
            var productIdsBySku = await db.Products
                .ToDictionaryAsync(product => product.Sku, product => product.Id);
            var products = new List<Product>();
            for (var index = 0; index < productSkus.Length; index++)
            {
                var sku = productSkus[index];
                if (productIdsBySku.ContainsKey(sku))
                {
                    continue;
                }

                var categoryName = sku.StartsWith("LAP") || sku.StartsWith("DES") || sku.StartsWith("MON") || sku.StartsWith("TAB") || sku.StartsWith("PHN") || sku.StartsWith("SRV") || sku.StartsWith("LPT")
                    ? "Electronics"
                    : (sku.StartsWith("MOU") || sku.StartsWith("KEY") || sku.StartsWith("BAG") || sku.StartsWith("DOC") || sku.StartsWith("HUB") || sku.StartsWith("CAM") || sku.StartsWith("MIC") || sku.StartsWith("HDP") || sku.StartsWith("SPE") || sku.StartsWith("PAD")
                        ? "Accessories"
                        : "Office Supplies");
                products.Add(new Product
                {
                    Sku = sku,
                    Name = $"Product {sku}",
                    Price = 20.00m + (index * 25.50m),
                    StockQuality = 10 + (index * 5),
                    CategoryId = categoriesByName[categoryName]
                });
            }
            db.Products.AddRange(products);
            await db.SaveChangesAsync();

            var productIds = await db.Products
                .Where(product => productSkus.Contains(product.Sku))
                .ToDictionaryAsync(product => product.Sku, product => product.Id);

            var associationConfigs = new[]
            {
                    ("LAP-001", "MOU-001", AssociationType.Accessory, 0.95m),
                    ("LAP-001", "KEY-001", AssociationType.Accessory, 0.90m),
                    ("LAP-001", "BAG-001", AssociationType.CrossSell, 0.85m),
                    ("LAP-001", "LAP-002", AssociationType.Substitute, 0.70m),
                    ("LAP-001", "HUB-001", AssociationType.Accessory, 0.80m),
                    
                    ("LAP-002", "MOU-001", AssociationType.Accessory, 0.92m),
                    ("LAP-002", "HDP-001", AssociationType.CrossSell, 0.75m),
                    ("LAP-002", "BAG-001", AssociationType.Accessory, 0.88m),
                    
                    ("MON-001", "MON-002", AssociationType.Substitute, 0.80m),
                    ("MON-001", "KEY-001", AssociationType.CrossSell, 0.65m),
                    ("MON-001", "CAM-001", AssociationType.UpSell, 0.70m),
                    ("MON-001", "HUB-001", AssociationType.Accessory, 0.85m),

                    ("PHN-001", "PHN-002", AssociationType.Substitute, 0.85m),
                    ("PHN-001", "HDP-001", AssociationType.Accessory, 0.90m),
                    ("PHN-001", "PAD-001", AssociationType.Accessory, 0.60m),

                    ("DESK-001", "LMP-001", AssociationType.CrossSell, 0.90m),
                    ("DESK-001", "CHL-001", AssociationType.Bundle, 0.95m),
                    ("DESK-001", "WHT-001", AssociationType.CrossSell, 0.65m),

                    ("CAM-001", "MIC-001", AssociationType.Bundle, 0.92m),
                    ("CAM-001", "PAD-001", AssociationType.Accessory, 0.50m),
                    
                    ("HDP-001", "MIC-001", AssociationType.CrossSell, 0.82m),
                    ("SPE-001", "MIC-001", AssociationType.CrossSell, 0.78m),

                    ("DOC-001", "FIL-001", AssociationType.Accessory, 0.80m),
                    ("DOC-001", "PEN-001", AssociationType.CrossSell, 0.70m),
                    ("PEN-001", "NOT-001", AssociationType.Bundle, 0.99m),
                    ("PEN-001", "CAL-001", AssociationType.CrossSell, 0.60m),
                    ("STP-001", "PEN-001", AssociationType.CrossSell, 0.55m),
                    ("CHL-001", "DESK-001", AssociationType.UpSell, 0.85m),
                    ("FIL-001", "SHR-001", AssociationType.CrossSell, 0.75m),
                    ("LMP-001", "COR-001", AssociationType.Accessory, 0.90m),
                    
                    ("TAB-001", "PHN-002", AssociationType.Substitute, 0.60m),
                    ("TAB-001", "HDP-001", AssociationType.Accessory, 0.80m),
                    ("SRV-001", "HUB-001", AssociationType.Accessory, 0.95m),
                    ("SRV-001", "COR-001", AssociationType.Accessory, 0.88m),
                    ("KEY-001", "MOU-001", AssociationType.Bundle, 0.94m),
                    ("BAG-001", "PAD-001", AssociationType.CrossSell, 0.50m),
                    ("HUB-001", "COR-001", AssociationType.Accessory, 0.91m),
                    ("MIC-001", "SPE-001", AssociationType.Bundle, 0.89m),
                    ("NOT-001", "CAL-001", AssociationType.CrossSell, 0.72m),
                    ("SHR-001", "FIL-001", AssociationType.Accessory, 0.68m)
            };
            var existingAssociations = await db.Associations
                .Select(association => new
                {
                    association.SourceProductId,
                    association.TargetProductId,
                    association.AssociationType
                })
                .ToListAsync();
            var associationKeys = existingAssociations
                .Select(association => (association.SourceProductId, association.TargetProductId, association.AssociationType))
                .ToHashSet();
            var associations = new List<Association>();
            foreach (var (sourceSku, targetSku, type, score) in associationConfigs)
            {
                if (!productIds.TryGetValue(sourceSku, out var sourceId) ||
                    !productIds.TryGetValue(targetSku, out var targetId))
                {
                    throw new InvalidOperationException($"Could not find products for association '{sourceSku}' -> '{targetSku}'.");
                }

                if (sourceId != targetId && associationKeys.Add((sourceId, targetId, type)))
                {
                    associations.Add(new Association
                    {
                        SourceProductId = sourceId,
                        TargetProductId = targetId,
                        AssociationType = type,
                        Score = score,
                        ValidSince = DateTimeOffset.UtcNow.AddDays(-10)
                    });
                }
            }
            db.Associations.AddRange(associations);
            await db.SaveChangesAsync();
         }
    }
}