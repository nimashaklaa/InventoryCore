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

            modelBuilder.Entity<Association>(e =>
            {
                e.Property(e=>e.Score).HasPrecision(5,4);

                e.HasOne(e=>e.SourceProduct)
                .WithMany()
                .HasForeignKey(e=> e.SourceProductId)
                .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(e=>e.TargetProduct)
                .WithMany()
                .HasForeignKey(e=> e.TargetProductId)
                .OnDelete(DeleteBehavior.Restrict);

            });
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Association> Associations { get; set; }
    }
}