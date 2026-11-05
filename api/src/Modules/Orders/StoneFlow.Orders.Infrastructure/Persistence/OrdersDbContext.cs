using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Infrastructure.Persistence;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public const string Schema = "orders";

    public DbSet<Order> Orders => Set<Order>();

    internal DbSet<OrderNumberCounter> OrderNumberCounters => Set<OrderNumberCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
    }
}
