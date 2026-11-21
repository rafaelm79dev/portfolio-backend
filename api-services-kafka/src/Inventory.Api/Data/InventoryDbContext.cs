using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).HasMaxLength(200);
            b.ToTable(t => t.HasCheckConstraint("ck_products_stock_non_negative", "stock >= 0"));
            b.HasData(SeedData.Products);
        });

        modelBuilder.Entity<StockReservation>(b =>
        {
            b.HasKey(r => r.Id);
            b.HasIndex(r => r.OrderId);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.Topic).HasMaxLength(200);
            b.Property(m => m.Key).HasMaxLength(200);
            b.Property(m => m.Type).HasMaxLength(100);
            b.Property(m => m.Payload).HasColumnType("jsonb");
            b.HasIndex(m => m.CreatedAt).HasFilter("published_at IS NULL");
        });

        modelBuilder.Entity<ProcessedMessage>(b => b.HasKey(m => m.MessageId));
    }
}
