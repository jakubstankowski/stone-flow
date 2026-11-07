using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.UnmarkExpensePaid;

/// <summary>„Cofnij oznaczenie zapłaty”: usuwa datę zapłaty (metoda zostaje).</summary>
public sealed record UnmarkExpensePaidCommand(Guid AccountId, Guid ExpenseId, string? Version) : ICommand<ExpenseChangeResult>;

/// <summary>
/// Walidacja wersji (400), potem <see cref="ExpenseChangeRunner"/>: istnienie (404), wersja (409
/// <c>expense_conflict</c>), reguła domeny (409 <c>expense_not_paid</c>), zapis i audyt
/// <c>Updated</c> z <c>paidOn</c> → <c>null</c>.
/// </summary>
public sealed class UnmarkExpensePaidHandler(ExpenseChangeRunner runner) : ICommandHandler<UnmarkExpensePaidCommand, ExpenseChangeResult>
{
    public Task<ExpenseChangeResult> HandleAsync(UnmarkExpensePaidCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        if (ExpenseVersion.Validate(command.Version, errors) is not { } version)
        {
            return Task.FromResult(ExpenseChangeResult.Invalid(errors));
        }

        return runner.RunAsync(command.AccountId, command.ExpenseId, version, (expense, now) => expense.UnmarkPaid(now), cancellationToken);
    }
}
