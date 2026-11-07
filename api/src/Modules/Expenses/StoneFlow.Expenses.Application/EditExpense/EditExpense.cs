using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.EditExpense;

/// <summary>Poprawa wydatku; <paramref name="Version"/> odczytana przy wczytaniu (base64).</summary>
public sealed record EditExpenseCommand(Guid AccountId, Guid ExpenseId, ExpenseInput Input, string? Version) : ICommand<ExpenseChangeResult>;

/// <summary>
/// Walidacja wersji i treści (400), potem <see cref="ExpenseChangeRunner"/>: istnienie (404),
/// wersja (409), poprawa w domenie, brak zmian bez zapisu, zapis i audyt <c>Updated</c>.
/// Wyczyszczenie daty zapłaty nie wymaga flagi — potwierdzenie jest tylko w UI.
/// </summary>
public sealed class EditExpenseHandler(ExpenseChangeRunner runner, TimeProvider timeProvider)
    : ICommandHandler<EditExpenseCommand, ExpenseChangeResult>
{
    public Task<ExpenseChangeResult> HandleAsync(EditExpenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        var version = ExpenseVersion.Validate(command.Version, errors);
        var content = command.Input.Validate(errors, WarsawTime.Today(timeProvider));
        if (!errors.IsEmpty || version is null || content is null)
        {
            return Task.FromResult(ExpenseChangeResult.Invalid(errors));
        }

        return runner.RunAsync(command.AccountId, command.ExpenseId, version, (expense, now) => expense.Edit(content, now), cancellationToken);
    }
}
