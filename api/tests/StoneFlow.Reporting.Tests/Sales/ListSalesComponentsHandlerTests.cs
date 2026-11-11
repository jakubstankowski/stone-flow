using StoneFlow.Reporting.Application.Sales;
using StoneFlow.Shared.Application.Sales;
using StoneFlow.Shared.Kernel.Sales;
using static StoneFlow.Reporting.Tests.Sales.ReportFacts;

namespace StoneFlow.Reporting.Tests.Sales;

/// <summary>H-2: zestawienie składników sprzedaży.</summary>
public sealed class ListSalesComponentsHandlerTests
{
    private static (ListSalesComponentsHandler Handler, SourceLog Log) Handler(IReadOnlyList<SalesComponentRecord>? sales = null, string launch = "2026-01-01")
    {
        var log = new SourceLog();
        return (new ListSalesComponentsHandler(new FakeSalesSource(log, sales ?? []), new FixedLaunchDate(DateOnly.Parse(launch)), Clock("2026-11-09")), log);
    }

    private static Task<ListSalesComponentsResult> Run(ListSalesComponentsHandler handler, ListSalesComponentsQuery query) =>
        handler.HandleAsync(query, TestContext.Current.CancellationToken);

    private static readonly SalesComponentRecord ShopOrder = Order("2026-11-04", 1000m, channel: "shop", id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly SalesComponentRecord OlxOrder = Order("2026-11-04", 500m, channel: "olx", id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly SalesComponentRecord Return = Entry(SalesComponentKind.SalesReturn, OlxOrder, "2026-11-04", 300m, Guid.Parse("00000000-0000-0000-0000-000000000003"));
    private static readonly SalesComponentRecord Correction = Entry(SalesComponentKind.SalesCorrection, ShopOrder, "2026-11-10", 100m, Guid.Parse("00000000-0000-0000-0000-000000000004"));
    private static readonly SalesComponentRecord Cancelled = Order("2026-11-05", 700m, counts: false);
    private static readonly SalesComponentRecord LateOrder = Order("2026-11-20", 50m, channel: "phone", id: Guid.Parse("00000000-0000-0000-0000-000000000005"));

    private static readonly SalesComponentRecord[] All = [ShopOrder, OlxOrder, Return, Correction, Cancelled, LateOrder];

    [Theory]
    [InlineData("page", "0")]
    [InlineData("pageSize", "0")]
    [InlineData("pageSize", "101")]
    [InlineData("channel", "allegro")]
    [InlineData("kind", "refund")]
    [InlineData("direction", "up")]
    public async Task Invalid_parameters_are_rejected(string key, string value)
    {
        var (handler, log) = Handler(All);
        var query = key switch
        {
            "page" => new ListSalesComponentsQuery(Page: value),
            "pageSize" => new ListSalesComponentsQuery(PageSize: value),
            "channel" => new ListSalesComponentsQuery(Channel: value),
            "kind" => new ListSalesComponentsQuery(Kind: value),
            _ => new ListSalesComponentsQuery(Direction: value),
        };

        var result = await Run(handler, query);

        Assert.Null(result.Page);
        Assert.Equal([key], result.Errors!.ToDictionary().Keys);
        Assert.Empty(log.Calls);
    }

    [Fact]
    public async Task Period_parameters_are_validated_like_the_report()
    {
        var (handler, _) = Handler(All);

        var result = await Run(handler, new ListSalesComponentsQuery("custom", null, "2026-11-10", "2026-11-01", Page: "0"));

        Assert.Equal(["from", "page"], result.Errors!.ToDictionary().Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Before_launch_has_no_items_and_no_sum_without_reading_the_source()
    {
        var (handler, log) = Handler(All, launch: "2026-11-03");

        var page = (await Run(handler, new ListSalesComponentsQuery("previousMonth"))).Page!;

        Assert.True(page.Period.BeforeLaunch);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.Null(page.Sum);
        Assert.Empty(log.Calls);
    }

    [Fact]
    public async Task Without_filters_the_sum_equals_sales_and_excluded_components_are_skipped()
    {
        var (handler, log) = Handler(All);

        var page = (await Run(handler, new ListSalesComponentsQuery())).Page!;

        Assert.Equal(5, page.Total);
        Assert.Equal(1150m, page.Sum); // 1000 + 500 − 300 − 100 + 50
        Assert.DoesNotContain(page.Items, i => i.Component.Id == Cancelled.Id);
        Assert.Equal([new SourceCall("sales", new(2026, 11, 1), new(2026, 11, 30), 10_001)], log.Calls);
    }

    [Fact]
    public async Task Channel_filter_sums_to_the_channel_value()
    {
        var (handler, _) = Handler(All);

        var page = (await Run(handler, new ListSalesComponentsQuery(Channel: "olx"))).Page!;

        Assert.Equal(2, page.Total);
        Assert.Equal(200m, page.Sum);
    }

    [Fact]
    public async Task Kind_filter_counts_orders_and_returns_have_negative_sum()
    {
        var (handler, _) = Handler(All);

        var orders = (await Run(handler, new ListSalesComponentsQuery(Kind: "order"))).Page!;
        var returns = (await Run(handler, new ListSalesComponentsQuery(Kind: "salesReturn"))).Page!;

        Assert.Equal(3, orders.Total);
        Assert.Equal(-300m, returns.Sum);
        Assert.Equal(-300m, Assert.Single(returns.Items).SignedAmount);
    }

    [Fact]
    public async Task Sorted_by_date_then_kind_then_id_in_both_directions()
    {
        var (handler, _) = Handler(All);

        var desc = (await Run(handler, new ListSalesComponentsQuery())).Page!;
        var asc = (await Run(handler, new ListSalesComponentsQuery(Direction: "asc"))).Page!;

        Assert.Equal([LateOrder.Id, Correction.Id, ShopOrder.Id, OlxOrder.Id, Return.Id], desc.Items.Select(i => i.Component.Id));
        Assert.Equal([ShopOrder.Id, OlxOrder.Id, Return.Id, Correction.Id, LateOrder.Id], asc.Items.Select(i => i.Component.Id));
    }

    [Fact]
    public async Task Sum_does_not_depend_on_the_page_and_page_past_the_end_is_empty()
    {
        var (handler, _) = Handler(All);

        var first = (await Run(handler, new ListSalesComponentsQuery(PageSize: "2"))).Page!;
        var second = (await Run(handler, new ListSalesComponentsQuery(Page: "2", PageSize: "2"))).Page!;
        var past = (await Run(handler, new ListSalesComponentsQuery(Page: "9", PageSize: "2"))).Page!;

        Assert.Equal(2, first.Items.Count);
        Assert.Equal(first.Sum, second.Sum);
        Assert.Empty(past.Items);
        Assert.Equal(5, past.Total);
        Assert.Equal(1150m, past.Sum);
    }

    [Fact]
    public async Task More_than_ten_thousand_components_trip_the_fuse()
    {
        var (handler, _) = Handler([.. Enumerable.Range(0, 10_001).Select(_ => Order("2026-11-04", 1m))]);

        var result = await Run(handler, new ListSalesComponentsQuery());

        Assert.True(result.TooManyRows);
        Assert.Null(result.Page);
    }
}
