using Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(category =>
        {
            category.Property(c => c.Name).HasMaxLength(100);
            category.HasIndex(c => c.Name).IsUnique();

            // Sample rows, inserted by the migration so there is data to query straight away.
            category.HasData(
                new Category { Id = 1, Name = "Tools" },
                new Category { Id = 2, Name = "Garden" },
                new Category { Id = 3, Name = "Kitchen" });
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.Property(p => p.Name).HasMaxLength(200);
            product.Property(p => p.Price).HasPrecision(18, 2);

            product.HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            product.HasData(
                new Product { Id = 1, Name = "Hammer", Price = 15.00m, CategoryId = 1 },
                new Product { Id = 2, Name = "Screwdriver set", Price = 24.50m, CategoryId = 1 },
                new Product { Id = 3, Name = "Garden hose", Price = 32.99m, CategoryId = 2 },
                new Product { Id = 4, Name = "Watering can", Price = 12.00m, CategoryId = 2 },
                new Product { Id = 5, Name = "Chef's knife", Price = 49.95m, CategoryId = 3 },
                new Product { Id = 6, Name = "Gift card", Price = 25.00m, CategoryId = null });
        });
    }
}
