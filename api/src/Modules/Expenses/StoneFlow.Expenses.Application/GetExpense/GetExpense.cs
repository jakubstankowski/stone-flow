using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.GetExpense;

/// <summary>Surowe dane wydatku z odczytu (znaczniki w UTC).</summary>
public sealed record ExpenseRecordDto(
    Guid Id,
    string Description,
    ExpenseCategory Category,
    string? Counterparty,
    DateOnly CostDate,
    DateOnly? DueDate,
    DateOnly? PaidOn,
    decimal GrossAmount,
    decimal? NetAmount,
    string? DocumentReference,
    ExpensePaymentMethod? PaymentMethod,
    string? Payer,
    string? Comment,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] Version);

/// <summary>Szczegóły: dane i stan policzony regułą domeny <see cref="ExpensePayment.Compute"/>.</summary>
public sealed record ExpenseDetailsDto(ExpenseRecordDto Expense, ExpensePaymentInfo Payment);

public sealed record GetExpenseQuery(Guid Id) : IQuery<ExpenseDetailsDto?>;

/// <summary>Szczegóły wydatku; „dzisiaj” dla „Po terminie” w Europe/Warsaw.</summary>
public sealed class GetExpenseHandler(IExpenseReader reader, TimeProvider timeProvider) : IQueryHandler<GetExpenseQuery, ExpenseDetailsDto?>
{
    public async Task<ExpenseDetailsDto?> HandleAsync(GetExpenseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var record = await reader.GetAsync(query.Id, cancellationToken);
        return record is null
            ? null
            : new ExpenseDetailsDto(record, ExpensePayment.Compute(record.PaidOn, record.DueDate, WarsawTime.Today(timeProvider)));
    }
}
