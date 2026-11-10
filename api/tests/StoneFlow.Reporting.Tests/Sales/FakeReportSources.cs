using Microsoft.Extensions.Time.Testing;
using StoneFlow.Shared.Application.Costs;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.Sales;
using StoneFlow.Shared.Kernel.Sales;

namespace StoneFlow.Reporting.Tests.Sales;

internal sealed record FixedLaunchDate(DateOnly Value) : ILaunchDate;

internal sealed record SourceCall(string Source, DateOnly From, DateOnly To, int Limit);

/// <summary>Wspólny dziennik wywołań obu źródeł, aby sprawdzać kolejność.</summary>
internal sealed class SourceLog
{
    public List<SourceCall> Calls { get; } = [];
}

/// <summary>Źródło testowe: filtruje po dacie i zwraca najwyżej <c>limit</c> wierszy jak SQL <c>TOP</c>.</summary>
internal sealed class FakeSalesSource(SourceLog log, IReadOnlyList<SalesComponentRecord> records) : ISalesComponentSource
{
    public IReadOnlyList<string> Channels { get; } = ["shop", "olx", "acquaintance", "phone", "other"];

    public Task<IReadOnlyList<SalesComponentRecord>> ListAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        log.Calls.Add(new SourceCall("sales", from, to, limit));
        IReadOnlyList<SalesComponentRecord> result = [.. records.Where(r => r.Date >= from && r.Date <= to).Take(limit)];
        return Task.FromResult(result);
    }
}

internal sealed class FakeCostSource(SourceLog log, IReadOnlyList<CostRecord> records) : ICostSource
{
    public IReadOnlyList<string> Categories { get; } =
        ["materialPurchase", "cutting", "hdsTransport", "marketing", "otherOperations", "vatTax", "ppeTax"];

    public Task<IReadOnlyList<CostRecord>> ListAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        log.Calls.Add(new SourceCall("costs", from, to, limit));
        IReadOnlyList<CostRecord> result = [.. records.Where(r => r.CostDate >= from && r.CostDate <= to).Take(limit)];
        return Task.FromResult(result);
    }
}

internal static class ReportFacts
{
    /// <summary>Zegar ustawiony na południe UTC danego dnia (ten sam dzień w Europe/Warsaw).</summary>
    public static FakeTimeProvider Clock(string today) =>
        new(new DateTimeOffset(DateOnly.Parse(today).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));

    public static SalesComponentRecord Order(string date, decimal value, string channel = "olx", bool counts = true, Guid? id = null)
    {
        var orderId = id ?? Guid.NewGuid();
        return new SalesComponentRecord(SalesComponentKind.Order, orderId, DateOnly.Parse(date), value, orderId, "Z/2026/1", DateOnly.Parse(date), channel, counts);
    }

    public static SalesComponentRecord Entry(SalesComponentKind kind, SalesComponentRecord order, string date, decimal amount, Guid? id = null) =>
        new(kind, id ?? Guid.NewGuid(), DateOnly.Parse(date), amount, order.OrderId, order.OrderNumber, order.OrderDate, order.Channel, order.OrderCountsTowardSales);

    public static CostRecord Cost(string date, decimal amount, string category = "materialPurchase") =>
        new(Guid.NewGuid(), DateOnly.Parse(date), amount, category);
}
