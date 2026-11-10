using StoneFlow.Reporting.Application.Operations;
using StoneFlow.Reporting.Tests.CashFlow;
using StoneFlow.Reporting.Tests.Sales;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Kernel.Operations;
using static StoneFlow.Reporting.Tests.CashFlow.CashFacts;

namespace StoneFlow.Reporting.Tests.Operations;

/// <summary>RD-1 … RD-4: wspólny odczyt źródeł z bezpiecznikiem i stała kolejność operacji (SF-20).</summary>
public sealed class CashOperationReaderTests
{
    private static readonly DateOnly From = new(2026, 11, 3);
    private static readonly DateOnly To = new(2026, 11, 30);

    private static Task<IReadOnlyList<CashOperation>?> Read(
        (FakeCashSource Orders, FakeCashSource Expenses, CashSourceLog Log) sources,
        IReadOnlySet<CashOperationKind>? kinds = null) =>
        CashOperationReader.ReadAsync(
            [sources.Orders, sources.Expenses], From, To, kinds ?? CashOperationReader.AllKinds, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Sources_are_called_in_order_with_their_own_kinds_and_range()
    {
        // RD-1
        var sources = Sources();

        await Read(sources);

        Assert.Equal(["orders", "expenses"], sources.Log.Calls.Select(c => c.Source));
        Assert.Equal([CashOperationKind.Payment, CashOperationKind.Refund], sources.Log.Calls[0].Kinds.Order());
        Assert.Equal([CashOperationKind.PaidExpense, CashOperationKind.OwnerWithdrawal], sources.Log.Calls[1].Kinds.Order());
        Assert.All(sources.Log.Calls, call => Assert.Equal((From, To), (call.From, call.To)));
    }

    [Fact]
    public async Task Source_without_requested_kinds_is_skipped()
    {
        // RD-1
        var sources = Sources([CashOp(CashOperationKind.OwnerWithdrawal, "2026-11-04", 10m)]);

        var loaded = await Read(sources, new HashSet<CashOperationKind> { CashOperationKind.OwnerWithdrawal });

        var call = Assert.Single(sources.Log.Calls);
        Assert.Equal("expenses", call.Source);
        Assert.Equal([CashOperationKind.OwnerWithdrawal], call.Kinds);
        Assert.Single(loaded!);
    }

    [Fact]
    public void All_kinds_contain_every_kind()
    {
        Assert.Equal(Enum.GetValues<CashOperationKind>(), CashOperationReader.AllKinds.Order());
    }

    [Fact]
    public async Task Limit_decreases_and_exactly_the_maximum_is_a_list()
    {
        // RD-2
        var sources = Sources([.. Many(CashOperationKind.Payment, 6_000), .. Many(CashOperationKind.OwnerWithdrawal, 4_000)]);

        var loaded = await Read(sources);

        Assert.Equal([10_001, 4_001], sources.Log.Calls.Select(c => c.Limit));
        Assert.Equal(CashOperationReader.MaxOperations, loaded!.Count);
    }

    [Fact]
    public async Task More_than_the_maximum_is_null()
    {
        // RD-2
        var sources = Sources([.. Many(CashOperationKind.Payment, 9_000), .. Many(CashOperationKind.PaidExpense, 1_001)]);

        Assert.Null(await Read(sources));
        Assert.Equal(1_001, sources.Log.Calls[1].Limit);
    }

    [Fact]
    public async Task Exhausted_limit_skips_the_next_source()
    {
        // RD-2
        var sources = Sources([.. Many(CashOperationKind.Payment, 10_001), CashOp(CashOperationKind.OwnerWithdrawal, "2026-11-04", 1m)]);

        Assert.Null(await Read(sources));
        Assert.Equal("orders", Assert.Single(sources.Log.Calls).Source);
    }

    [Fact]
    public void Order_is_by_date_then_kind_declaration_then_id()
    {
        // RD-3
        var a = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var b = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        var withdrawal = CashOp(CashOperationKind.OwnerWithdrawal, "2026-11-05", 10m, a);
        var expense = CashOp(CashOperationKind.PaidExpense, "2026-11-05", 10m, a);
        var refund = CashOp(CashOperationKind.Refund, "2026-11-05", 10m, a);
        var paymentB = CashOp(CashOperationKind.Payment, "2026-11-05", 10m, b);
        var paymentA = CashOp(CashOperationKind.Payment, "2026-11-05", 10m, a);
        var older = CashOp(CashOperationKind.Refund, "2026-11-04", 10m, b);
        CashOperation[] operations = [withdrawal, paymentB, older, expense, refund, paymentA];

        Assert.Equal([paymentA, paymentB, refund, expense, withdrawal, older], CashOperationReader.Order(operations, descending: true));
        Assert.Equal([older, paymentA, paymentB, refund, expense, withdrawal], CashOperationReader.Order(operations, descending: false));
    }

    [Fact]
    public async Task First_ten_in_order_equal_the_first_page_of_the_operations_list()
    {
        // RD-4 (FIN-05): ostatnie operacje = pierwsza strona listy przy pageSize = 10.
        var shared = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var operations = new List<CashOperation>
        {
            CashOp(CashOperationKind.OwnerWithdrawal, "2026-11-20", 100m, shared),
            CashOp(CashOperationKind.PaidExpense, "2026-11-20", -50m, shared),
            CashOp(CashOperationKind.Refund, "2026-11-20", 10m),
            CashOp(CashOperationKind.Payment, "2026-11-20", 300m),
            CashOp(CashOperationKind.Payment, "2026-11-20", 200m),
        };
        operations.AddRange(Enumerable.Range(1, 9).Select(day => CashOp(CashOperationKind.Payment, $"2026-11-{day + 2:00}", day)));
        var sources = Sources(operations);
        var list = new ListOperationsHandler([sources.Orders, sources.Expenses], new FixedLaunchDate(From));

        var page = await list.HandleAsync(new ListOperationsQuery("2026-11-01", "2026-11-30", PageSize: "10"), TestContext.Current.CancellationToken);
        var loaded = await Read(Sources(operations));

        Assert.Equal(page.Page!.Items.Select(i => i.Operation), CashOperationReader.Order(loaded!, descending: true).Take(10));
    }
}
