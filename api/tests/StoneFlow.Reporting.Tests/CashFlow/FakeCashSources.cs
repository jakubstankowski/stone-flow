using System.Globalization;
using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Tests.CashFlow;

internal sealed record CashSourceCall(string Source, DateOnly From, DateOnly To, IReadOnlySet<CashOperationKind> Kinds, int Limit);

/// <summary>Wspólny dziennik wywołań źródeł operacji, aby sprawdzać kolejność.</summary>
internal sealed class CashSourceLog
{
    public List<CashSourceCall> Calls { get; } = [];
}

/// <summary>
/// Źródło testowe: filtruje po rodzaju i dacie, porządkuje po dacie i id i zwraca najwyżej
/// <c>limit</c> wierszy jak SQL <c>TOP</c>.
/// </summary>
internal sealed class FakeCashSource(string name, CashSourceLog log, IReadOnlySet<CashOperationKind> kinds, IReadOnlyList<CashOperation> operations)
    : ICashOperationSource
{
    public IReadOnlySet<CashOperationKind> Kinds { get; } = kinds;

    public Task<IReadOnlyList<CashOperation>> ListAsync(
        DateOnly from, DateOnly to, IReadOnlySet<CashOperationKind> kinds, int limit, CancellationToken cancellationToken)
    {
        log.Calls.Add(new CashSourceCall(name, from, to, kinds, limit));
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

internal static class CashFacts
{
    /// <summary>Dwa źródła jak w hoście: Orders (wpłaty, zwroty), potem Expenses (wydatki, wypłaty).</summary>
    public static (FakeCashSource Orders, FakeCashSource Expenses, CashSourceLog Log) Sources(IReadOnlyList<CashOperation>? operations = null)
    {
        var log = new CashSourceLog();
        var all = operations ?? [];
        var orders = new FakeCashSource("orders", log, new HashSet<CashOperationKind> { CashOperationKind.Payment, CashOperationKind.Refund }, all);
        var expenses = new FakeCashSource("expenses", log, new HashSet<CashOperationKind> { CashOperationKind.PaidExpense, CashOperationKind.OwnerWithdrawal }, all);
        return (orders, expenses, log);
    }

    public static CashOperation CashOp(CashOperationKind kind, string date, decimal amount, Guid? id = null)
    {
        var operationId = id ?? Guid.NewGuid();
        return new CashOperation(
            kind, operationId, DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), amount, "transfer", null, "Opis", null, operationId, "Opis");
    }

    public static IEnumerable<CashOperation> Many(CashOperationKind kind, int count, string date = "2026-11-05") =>
        Enumerable.Range(0, count).Select(_ => CashOp(kind, date, 1m));
}
