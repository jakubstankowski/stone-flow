using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.GetOrder;
using StoneFlow.Orders.Application.ListOrders;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Tests.Application;

/// <summary>SF-22: stan akcji anonimizacji w szczegółach zamówienia.</summary>
public sealed class GetOrderHandlerTests
{
    private static readonly Guid OrderId = Guid.Parse("8f1e2d3c-4b5a-4968-8776-655443322110");
    private static readonly DateTime OrderUpdated = new(2021, 3, 15, 9, 0, 0, DateTimeKind.Utc);

    private static OrderDetailsDto Details(DateTime? anonymizedAt = null) =>
        new(
            OrderId,
            "ZR-2021-0001",
            OrderSource.Manual,
            null,
            null,
            new DateOnly(2021, 3, 15),
            OrderChannel.Olx,
            100m,
            FulfillmentStatus.InProgress,
            TestOrders.Customer(),
            OperationalData.Empty,
            null,
            OrderUpdated,
            OrderUpdated,
            [0, 0, 0, 0, 0, 0, 0, 1],
            [],
            [],
            [])
        {
            AnonymizedAtUtc = anonymizedAt,
        };

    private static async Task<(OrderDetailsDto? Result, AnonymizationFakes Fakes)> Run(
        DateTimeOffset now, int? months, OrderDetailsDto? details, Action<AnonymizationFakes>? arrange = null)
    {
        var fakes = new AnonymizationFakes(now);
        fakes.Settings.RetentionMonths = months;
        arrange?.Invoke(fakes);
        var handler = new GetOrderHandler(new FakeReader(details), fakes.Retention);
        var result = await handler.HandleAsync(new GetOrderQuery(OrderId), CancellationToken.None);
        return (result, fakes);
    }

    [Fact]
    public async Task Disabled_without_reading_facts()
    {
        var (result, fakes) = await Run(DateTimeOffset.UtcNow, null, Details());

        Assert.Equal(new AnonymizationAvailability(AnonymizationStatus.Disabled, null, null), result!.Anonymization);
        Assert.Empty(fakes.Calls);
    }

    [Fact]
    public async Task Anonymized_even_without_configuration_and_without_reads()
    {
        var anonymizedAt = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);

        var (disabled, disabledFakes) = await Run(DateTimeOffset.UtcNow, null, Details(anonymizedAt));
        var (enabled, enabledFakes) = await Run(DateTimeOffset.UtcNow, 60, Details(anonymizedAt));

        Assert.Equal(AnonymizationStatus.Anonymized, disabled!.Anonymization!.Status);
        Assert.Equal(AnonymizationStatus.Anonymized, enabled!.Anonymization!.Status);
        Assert.Empty(disabledFakes.Calls);
        Assert.Empty(enabledFakes.Calls);
    }

    [Theory]
    [InlineData("2026-03-14T22:59:59Z", AnonymizationStatus.RetentionPending)]
    [InlineData("2026-03-14T23:00:00Z", AnonymizationStatus.Available)]
    public async Task Today_is_the_warsaw_date(string now, AnonymizationStatus expected)
    {
        var (result, fakes) = await Run(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), 60, Details());

        Assert.Equal(new AnonymizationAvailability(expected, new DateOnly(2021, 3, 15), new DateOnly(2026, 3, 15)), result!.Anonymization);
        Assert.Equal(["facts", "activity"], fakes.Calls);
    }

    [Fact]
    public async Task Entry_facts_move_the_start()
    {
        var (result, _) = await Run(
            new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero),
            60,
            Details(),
            fakes => fakes.EntryStore.Facts = new EntryRetentionFacts(new DateOnly(2022, 6, 10), new DateTime(2022, 6, 10, 8, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(new DateOnly(2022, 6, 10), result!.Anonymization!.RetentionStartDate);
    }

    [Fact]
    public async Task Deleted_entry_time_from_audit_moves_the_start_to_its_warsaw_date()
    {
        var (result, _) = await Run(
            new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero),
            60,
            Details(),
            fakes => fakes.Activity.LastRecordedAtUtc = new DateTime(2024, 12, 31, 23, 30, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(2025, 1, 1), result!.Anonymization!.RetentionStartDate);
        Assert.Equal(AnonymizationStatus.RetentionPending, result.Anonymization.Status);
    }

    [Fact]
    public async Task Missing_order_reads_no_facts()
    {
        var (result, fakes) = await Run(DateTimeOffset.UtcNow, 60, null);

        Assert.Null(result);
        Assert.Empty(fakes.Calls);
    }

    private sealed class FakeReader(OrderDetailsDto? details) : IOrderReader
    {
        public Task<OrderListPageDto> ListAsync(OrderListCriteria criteria, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OrderDetailsDto?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(details);

        public Task<CreationRequestMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
