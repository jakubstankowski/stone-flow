using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.MarkExpensePaid;

/// <summary>„Oznacz jako opłacony”: data zapłaty, metoda (zastępuje zapisaną, <c>null</c> czyści) i wersja.</summary>
public sealed record MarkExpensePaidCommand(Guid AccountId, Guid ExpenseId, string? PaidOn, string? PaymentMethod, string? Version)
    : ICommand<ExpenseChangeResult>;

/// <summary>
/// Walidacja (400: data wymagana, nie późniejsza niż dzisiaj w Europe/Warsaw, znana metoda,
/// wersja), potem <see cref="ExpenseChangeRunner"/>: istnienie (404), wersja (409
/// <c>expense_conflict</c>), reguła domeny (409 <c>expense_already_paid</c>), zapis i audyt.
/// </summary>
public sealed class MarkExpensePaidHandler(ExpenseChangeRunner runner, TimeProvider timeProvider)
    : ICommandHandler<MarkExpensePaidCommand, ExpenseChangeResult>
{
    public Task<ExpenseChangeResult> HandleAsync(MarkExpensePaidCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        var version = ExpenseVersion.Validate(command.Version, errors);
        var paid = ExpenseInput.ValidatePaid(command.PaidOn, command.PaymentMethod, errors, WarsawTime.Today(timeProvider));
        if (!errors.IsEmpty || version is null || paid is null)
        {
            return Task.FromResult(ExpenseChangeResult.Invalid(errors));
        }

        return runner.RunAsync(
            command.AccountId,
            command.ExpenseId,
            version,
            (expense, now) => expense.MarkPaid(paid.PaidOn, paid.PaymentMethod, now),
            cancellationToken);
    }
}
