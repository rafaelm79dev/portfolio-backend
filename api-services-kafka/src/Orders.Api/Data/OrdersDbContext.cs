using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;

namespace Orders.Api.Data;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(o => o.RejectionReason).HasMaxLength(500);
            b.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(b => b.HasKey(i => i.Id));

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
