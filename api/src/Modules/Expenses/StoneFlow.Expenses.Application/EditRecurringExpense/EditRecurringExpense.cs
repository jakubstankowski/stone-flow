using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.EditRecurringExpense;

/// <summary>Zmiana definicji; <paramref name="Version"/> z odczytu (base64). Dzień miesiąca jest zawsze w żądaniu.</summary>
public sealed record EditRecurringExpenseCommand(Guid AccountId, Guid DefinitionId, RecurringExpenseInput Input, string? Version)
    : ICommand<RecurringChangeResult>;

/// <summary>
/// Kolejność NC3: walidacja pól bez stanu i wersji (400), potem w jednej jednostce pracy blokada,
/// istnienie (404) i wersja (409), reguła domeny (zmiana dnia na wstrzymanej → 409) przed walidacją
/// harmonogramu na stanie po zmianie (400, klucz <c>endsOn</c>), operacja domeny <c>Edit</c>, brak
/// zmian bez zapisu, audytu i generowania, zapis z wersją, audyt <c>Updated</c> i generowanie na
/// dzisiaj z nowym stanem (zmiana dotyczy wystąpień nieutworzonych, w tym zaległych).
/// </summary>
public sealed class EditRecurringExpenseHandler(RecurringChangeRunner runner, IRecurringExpenseStore store)
    : ICommandHandler<EditRecurringExpenseCommand, RecurringChangeResult>
{
    public Task<RecurringChangeResult> HandleAsync(EditRecurringExpenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input, nameof(command));

        var errors = new FieldErrors();
        var version = ExpenseVersion.Validate(command.Version, errors);
        var input = command.Input.Validate(errors);
        if (!errors.IsEmpty || version is null || input is null)
        {
            return Task.FromResult(RecurringChangeResult.Invalid(errors));
        }

        return runner.RunAsync(
            command.DefinitionId,
            version,
            async (context, ct) =>
            {
                var registry = await store.RegisteredPeriodsAsync(context.Definition.Id, ct);
                var change = context.Definition.Edit(input.Content, input.Schedule, context.Today, registry, context.NowUtc);
                if (change.Violation is { } violation)
                {
                    return RecurringChangeResult.Rejected(violation);
                }

                if (change.Changes.Count == 0)
                {
                    return new RecurringChangeResult(RecurringChangeOutcome.Saved, context.Stored.Version);
                }

                var saved = await runner.SaveAsync(context, change.Changes, command.AccountId, ct);
                return saved.Outcome == RecurringChangeOutcome.Saved ? await runner.GenerateAsync(context, saved, ct) : saved;
            },
            cancellationToken);
    }
}
