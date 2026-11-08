using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.PauseRecurringExpense;

/// <summary>„Wstrzymaj”; <paramref name="Version"/> z odczytu (base64).</summary>
public sealed record PauseRecurringExpenseCommand(Guid AccountId, Guid DefinitionId, string? Version) : ICommand<RecurringChangeResult>;

/// <summary>
/// Wersja (400), potem w jednej jednostce pracy: blokada, istnienie (404), wersja (409), reguła domeny
/// (już wstrzymany, zakończony → 409), generowanie na dzisiaj <b>przed</b> zmianą stanu (terminy do
/// dzisiaj włącznie, które nie powstały), <c>PausedOn = dzisiaj</c>, zapis z wersją i audyt <c>Updated</c>.
/// </summary>
public sealed class PauseRecurringExpenseHandler(RecurringChangeRunner runner) : ICommandHandler<PauseRecurringExpenseCommand, RecurringChangeResult>
{
    public Task<RecurringChangeResult> HandleAsync(PauseRecurringExpenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new FieldErrors();
        if (ExpenseVersion.Validate(command.Version, errors) is not { } version)
        {
            return Task.FromResult(RecurringChangeResult.Invalid(errors));
        }

        return runner.RunAsync(
            command.DefinitionId,
            version,
            async (context, ct) =>
            {
                if (context.Definition.CheckPause(context.Today) is { } violation)
                {
                    return RecurringChangeResult.Rejected(violation);
                }

                var generated = await runner.GenerateAsync(context, new RecurringChangeResult(RecurringChangeOutcome.Saved), ct);
                if (generated.Outcome != RecurringChangeOutcome.Saved)
                {
                    return generated;
                }

                var change = context.Definition.Pause(context.Today, context.NowUtc);
                var saved = await runner.SaveAsync(context, change.Changes, command.AccountId, ct);
                return saved with { CreatedOccurrences = generated.CreatedOccurrences };
            },
            cancellationToken);
    }
}
