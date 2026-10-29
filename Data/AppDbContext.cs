using Microsoft.EntityFrameworkCore;
using InventoryCore.Models;

namespace InventoryCore.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>()
                .Property(product => product.Price)
                .HasPrecision(18, 2);
        }

        public DbSet<Product> Products { get; set; }
    }
}