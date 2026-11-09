using System.Globalization;
using StoneFlow.Reporting.Application.Operations;
using StoneFlow.Reporting.Domain.CashFlow;
using StoneFlow.Shared.Application.LaunchDate;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Tests.Operations;

public sealed class ListOperationsHandlerTests
{
    private static readonly DateOnly Launch = new(2026, 11, 3);

    [Theory]
    [InlineData(null, "2026-11-30", "from")]
    [InlineData("2026-11-01", null, "to")]
    [InlineData("01.11.2026", "2026-11-30", "from")]
    [InlineData("2026-11-01", "2026-13-01", "to")]
    [InlineData("2026-11-02", "2026-11-01", "from")]
    public async Task Period_is_required_valid_and_ordered(string? from, string? to, string key)
    {
        var (handler, orders, _) = Handler();

        var result = await handler.HandleAsync(new ListOperationsQuery(from, to), TestContext.Current.CancellationToken);

        Assert.Null(result.Page);
        Assert.True(result.Errors!.Contains(key));
        Assert.Empty(orders.Calls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task Page_must_be_an_integer_from_one(string page)
    {
        var result = await Run(Query(page: page));

        Assert.Equal(["page"], result.Errors!.ToDictionary().Keys);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("x")]
    public async Task Page_size_is_between_one_and_one_hundred(string pageSize)
    {
        var result = await Run(Query(pageSize: pageSize));

        Assert.Equal(["pageSize"], result.Errors!.ToDictionary().Keys);
    }

    [Fact]
    public async Task Unknown_kind_and_direction_are_rejected_once_per_key()
    {
        var result = await Run(Query(kind: ["payment", "salesCorrection", "cost"], direction: "up"));

        Assert.Equal(["direction", "kind"], result.Errors!.ToDictionary().Keys.Order(StringComparer.Ordinal));
        Assert.Single(result.Errors.ToDictionary()["kind"]);
    }

    [Theory]
    [InlineData("payment", CashOperationKind.Payment)]
    [InlineData("refund", CashOperationKind.Refund)]
    [InlineData("expense", CashOperationKind.PaidExpense)]
    [InlineData("withdrawal", CashOperationKind.OwnerWithdrawal)]
    public void Kind_codes_map_to_cash_operation_kinds(string code, CashOperationKind kind)
    {
        Assert.True(OperationCodes.TryParse(code, out var parsed));
        Assert.Equal(kind, parsed);
        Assert.Equal(code, OperationCodes.Of(kind));
    }

    [Fact]
    public async Task Kind_filter_passes_only_requested_kinds_and_skips_sources_without_them()
    {
        var (handler, orders, expenses) = Handler(
            orders: [Op(CashOperationKind.Payment, "2026-11-04", 100m), Op(CashOperationKind.Refund, "2026-11-04", 30m)],
            expenses: [Op(CashOperationKind.OwnerWithdrawal, "2026-11-04", 2000m)]);

        var result = await handler.HandleAsync(Query(kind: ["refund", "refund"]), TestContext.Current.CancellationToken);

        var call = Assert.Single(orders.Calls);
        Assert.Equal([CashOperationKind.Refund], call.Kinds.ToArray());
        Assert.Empty(expenses.Calls);
        var item = Assert.Single(result.Page!.Items);
        Assert.Equal(CashOperationKind.Refund, item.Operation.Kind);
        Assert.Equal(new CashFlowTotals(0m, 30m, -30m), result.Page.Totals);
    }

    [Fact]
    public async Task No_kind_filter_requests_all_kinds_of_each_source()
    {
        var (handler, orders, expenses) = Handler();

        await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal([CashOperationKind.Payment, CashOperationKind.Refund], Assert.Single(orders.Calls).Kinds.Order().ToArray());
        Assert.Equal([CashOperationKind.PaidExpense, CashOperationKind.OwnerWithdrawal], Assert.Single(expenses.Calls).Kinds.Order().ToArray());
    }

    [Fact]
    public async Task Merged_list_sorts_by_date_then_fixed_kind_order_then_id_in_both_directions()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var b = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        var withdrawal = Op(CashOperationKind.OwnerWithdrawal, "2026-11-05", 10m, a);
        var expense = Op(CashOperationKind.PaidExpense, "2026-11-05", 10m, a);
        var paymentB = Op(CashOperationKind.Payment, "2026-11-05", 10m, b);
        var paymentA = Op(CashOperationKind.Payment, "2026-11-05", 10m, a);
        var older = Op(CashOperationKind.Refund, "2026-11-04", 10m, b);
        var (handler, _, _) = Handler(orders: [paymentB, older, paymentA], expenses: [withdrawal, expense]);

        var descending = await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);
        var ascending = await handler.HandleAsync(Query(direction: "asc"), TestContext.Current.CancellationToken);

        Assert.Equal([paymentA, paymentB, expense, withdrawal, older], descending.Page!.Items.Select(i => i.Operation));
        Assert.Equal([older, paymentA, paymentB, expense, withdrawal], ascending.Page!.Items.Select(i => i.Operation));
    }

    [Fact]
    public async Task Items_carry_the_direction_from_the_policy()
    {
        var (handler, _, _) = Handler(
            orders: [Op(CashOperationKind.Payment, "2026-11-05", 10m)],
            expenses: [Op(CashOperationKind.PaidExpense, "2026-11-04", -50m)]);

        var items = (await handler.HandleAsync(Query(), TestContext.Current.CancellationToken)).Page!.Items;

        Assert.Equal([CashDirection.Inflow, CashDirection.Outflow], items.Select(i => i.Direction));
        Assert.Equal(-50m, items[1].Operation.Amount);
    }

    [Fact]
    public async Task Totals_and_total_cover_the_whole_filter_regardless_of_page_and_direction()
    {
        var (handler, _, _) = Handler(
            orders: [Op(CashOperationKind.Payment, "2026-11-04", 1000m), Op(CashOperationKind.Refund, "2026-11-05", 200m)],
            expenses: [Op(CashOperationKind.PaidExpense, "2026-11-06", -50m), Op(CashOperationKind.OwnerWithdrawal, "2026-11-07", 2000m)]);
        var expected = new CashFlowTotals(1000m, 2150m, -1150m);

        var first = await handler.HandleAsync(Query(pageSize: "2"), TestContext.Current.CancellationToken);
        var second = await handler.HandleAsync(Query(pageSize: "2", page: "2", direction: "asc"), TestContext.Current.CancellationToken);

        Assert.Equal(expected, first.Page!.Totals);
        Assert.Equal(expected, second.Page!.Totals);
        Assert.Equal(4, first.Page.Total);
        Assert.Equal(2, first.Page.Items.Count);
        Assert.Equal(2, second.Page.Page);
        Assert.Equal(2, second.Page.PageSize);
    }

    [Fact]
    public async Task Page_beyond_the_end_is_empty_with_real_total_and_totals()
    {
        var (handler, _, _) = Handler(orders: [Op(CashOperationKind.Payment, "2026-11-04", 1000m)]);

        var result = await handler.HandleAsync(Query(page: "50"), TestContext.Current.CancellationToken);

        Assert.Empty(result.Page!.Items);
        Assert.Equal(1, result.Page.Total);
        Assert.Equal(1000m, result.Page.Totals.Inflows);
        Assert.Equal(50, result.Page.Page);
    }

    [Fact]
    public async Task Huge_page_number_does_not_overflow()
    {
        var (handler, _, _) = Handler(orders: [Op(CashOperationKind.Payment, "2026-11-04", 1000m)]);

        var result = await handler.HandleAsync(Query(page: int.MaxValue.ToString(CultureInfo.InvariantCulture), pageSize: "100"), TestContext.Current.CancellationToken);

        Assert.Empty(result.Page!.Items);
    }

    [Fact]
    public async Task Defaults_are_first_page_of_25_descending()
    {
        var (handler, orders, _) = Handler(orders: [.. Enumerable.Range(1, 30).Select(day => Op(CashOperationKind.Payment, $"2026-11-{day:00}", 1m))]);

        var result = await handler.HandleAsync(new ListOperationsQuery("2026-11-01", "2026-11-30"), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Page!.Page);
        Assert.Equal(25, result.Page.PageSize);
        Assert.Equal(25, result.Page.Items.Count);
        Assert.Equal(new DateOnly(2026, 11, 30), result.Page.Items[0].Operation.Date);
        Assert.Equal(ListOperationsHandler.MaxOperations + 1, Assert.Single(orders.Calls).Limit);
    }

    [Fact]
    public async Task Second_source_gets_the_remaining_limit()
    {
        var (handler, orders, expenses) = Handler(orders: [.. Many(CashOperationKind.Payment, 4000)]);

        await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(10_001, Assert.Single(orders.Calls).Limit);
        Assert.Equal(6_001, Assert.Single(expenses.Calls).Limit);
    }

    [Fact]
    public async Task More_than_the_maximum_from_the_first_source_skips_the_next_and_reports_too_many()
    {
        var (handler, _, expenses) = Handler(orders: [.. Many(CashOperationKind.Payment, 10_001)]);

        var result = await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);

        Assert.True(result.TooManyOperations);
        Assert.Null(result.Page);
        Assert.Empty(expenses.Calls);
    }

    [Fact]
    public async Task More_than_the_maximum_across_sources_reports_too_many()
    {
        var (handler, _, expenses) = Handler(
            orders: [.. Many(CashOperationKind.Payment, 9_000)],
            expenses: [.. Many(CashOperationKind.OwnerWithdrawal, 2_000)]);

        var result = await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);

        Assert.True(result.TooManyOperations);
        Assert.Equal(1_001, Assert.Single(expenses.Calls).Limit);
    }

    [Fact]
    public async Task Exactly_the_maximum_gives_a_page_and_totals()
    {
        var (handler, _, _) = Handler(
            orders: [.. Many(CashOperationKind.Payment, 6_000)],
            expenses: [.. Many(CashOperationKind.OwnerWithdrawal, 4_000)]);

        var result = await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);

        Assert.False(result.TooManyOperations);
        Assert.Equal(10_000, result.Page!.Total);
        Assert.Equal(new CashFlowTotals(6_000m, 4_000m, 2_000m), result.Page.Totals);
    }

    [Fact]
    public async Task Sources_get_the_effective_start_after_launch()
    {
        var (handler, orders, expenses) = Handler();

        var result = await handler.HandleAsync(new ListOperationsQuery("2026-11-01", "2026-11-30"), TestContext.Current.CancellationToken);

        Assert.Equal((Launch, new DateOnly(2026, 11, 30)), (Assert.Single(orders.Calls).From, orders.Calls[0].To));
        Assert.Equal(Launch, Assert.Single(expenses.Calls).From);
        Assert.Equal(Launch, result.Page!.EffectiveFrom);
        Assert.Equal(Launch, result.Page.LaunchDate);
    }

    [Fact]
    public async Task Start_after_launch_is_passed_unchanged()
    {
        var (handler, orders, _) = Handler();

        var result = await handler.HandleAsync(new ListOperationsQuery("2026-11-10", "2026-11-30"), TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 11, 10), Assert.Single(orders.Calls).From);
        Assert.Equal(new DateOnly(2026, 11, 10), result.Page!.EffectiveFrom);
    }

    [Fact]
    public async Task Period_entirely_before_launch_queries_no_source()
    {
        var (handler, orders, expenses) = Handler(orders: [Op(CashOperationKind.Payment, "2026-10-10", 100m)]);

        var result = await handler.HandleAsync(new ListOperationsQuery("2026-10-01", "2026-10-31"), TestContext.Current.CancellationToken);

        Assert.Empty(orders.Calls);
        Assert.Empty(expenses.Calls);
        Assert.Empty(result.Page!.Items);
        Assert.Equal(0, result.Page.Total);
        Assert.Equal(new CashFlowTotals(0m, 0m, 0m), result.Page.Totals);
        Assert.Equal(Launch, result.Page.EffectiveFrom);
        Assert.Equal(Launch, result.Page.LaunchDate);
    }

    [Theory]
    [InlineData(CashOperationKind.Payment, "order")]
    [InlineData(CashOperationKind.Refund, "order")]
    [InlineData(CashOperationKind.PaidExpense, "expense")]
    [InlineData(CashOperationKind.OwnerWithdrawal, "withdrawal")]
    public void Source_type_follows_the_kind(CashOperationKind kind, string sourceType)
    {
        Assert.Equal(sourceType, OperationCodes.SourceTypeOf(kind));
    }

    private static ListOperationsQuery Query(
        string? page = null, string? pageSize = null, IReadOnlyList<string>? kind = null, string? direction = null) =>
        new("2026-11-01", "2026-11-30", kind, direction, page, pageSize);

    private static async Task<ListOperationsResult> Run(ListOperationsQuery query) =>
        await Handler().Handler.HandleAsync(query, TestContext.Current.CancellationToken);

    private static (ListOperationsHandler Handler, FakeSource Orders, FakeSource Expenses) Handler(
        IReadOnlyList<CashOperation>? orders = null, IReadOnlyList<CashOperation>? expenses = null)
    {
        var ordersSource = new FakeSource(new HashSet<CashOperationKind> { CashOperationKind.Payment, CashOperationKind.Refund }, orders ?? []);
        var expensesSource = new FakeSource(new HashSet<CashOperationKind> { CashOperationKind.PaidExpense, CashOperationKind.OwnerWithdrawal }, expenses ?? []);
        return (new ListOperationsHandler([ordersSource, expensesSource], new FixedLaunchDate(Launch)), ordersSource, expensesSource);
    }

    private static CashOperation Op(CashOperationKind kind, string date, decimal amount, Guid? id = null)
    {
        var operationId = id ?? Guid.NewGuid();
        return new CashOperation(
            kind, operationId, DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), amount, "transfer", null, "Opis", null, operationId, "Opis");
    }

    private static IEnumerable<CashOperation> Many(CashOperationKind kind, int count) =>
        Enumerable.Range(0, count).Select(_ => Op(kind, "2026-11-05", 1m));

    private sealed record FixedLaunchDate(DateOnly Value) : ILaunchDate;

    private sealed record SourceCall(DateOnly From, DateOnly To, IReadOnlySet<CashOperationKind> Kinds, int Limit);

    /// <summary>Źródło testowe: filtruje po rodzaju i dacie, zwraca najwyżej <c>limit</c> wierszy jak SQL <c>TOP</c>.</summary>
    private sealed class FakeSource(IReadOnlySet<CashOperationKind> kinds, IReadOnlyList<CashOperation> operations) : ICashOperationSource
    {
        public List<SourceCall> Calls { get; } = [];

        public IReadOnlySet<CashOperationKind> Kinds { get; } = kinds;

        public Task<IReadOnlyList<CashOperation>> ListAsync(
            DateOnly from, DateOnly to, IReadOnlySet<CashOperationKind> kinds, int limit, CancellationToken cancellationToken)
        {
            Calls.Add(new SourceCall(from, to, kinds, limit));
            IReadOnlyList<CashOperation> rows =
            [
                .. operations
                    .Where(o => kinds.Contains(o.Kind) && o.Date >= from && o.Date <= to)
                    .OrderBy(o => o.Date)
                    .ThenBy(o => o.Id)
                    .Take(limit),
            ];
            return Task.FromResult(rows);
        }
    }
}
