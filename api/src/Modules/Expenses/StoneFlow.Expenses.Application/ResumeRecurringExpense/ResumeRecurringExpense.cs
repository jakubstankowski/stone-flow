using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.ResumeRecurringExpense;

/// <summary>„Wznów”; <paramref name="Version"/> z odczytu (base64).</summary>
public sealed record ResumeRecurringExpenseCommand(Guid AccountId, Guid DefinitionId, string? Version) : ICommand<RecurringChangeResult>;

/// <summary>
/// Wersja (400), potem w jednej jednostce pracy: blokada, istnienie (404), wersja (409), reguła domeny
/// (niewstrzymany → 409), przedział <c>[PausedOn, dzisiaj)</c> i <c>PausedOn = null</c>, zapis z wersją,
/// audyt <c>Updated</c> i generowanie na dzisiaj: termin w dniu wznowienia powstaje, termin z przedziału nie.
/// </summary>
public sealed class ResumeRecurringExpenseHandler(RecurringChangeRunner runner) : ICommandHandler<ResumeRecurringExpenseCommand, RecurringChangeResult>
{
    public Task<RecurringChangeResult> HandleAsync(ResumeRecurringExpenseCommand command, CancellationToken cancellationToken)
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
                var change = context.Definition.Resume(context.Today, context.NowUtc);
                if (change.Violation is { } violation)
                {
                    return RecurringChangeResult.Rejected(violation);
                }

                var saved = await runner.SaveAsync(context, change.Changes, command.AccountId, ct);
                return saved.Outcome == RecurringChangeOutcome.Saved ? await runner.GenerateAsync(context, saved, ct) : saved;
            },
            cancellationToken);
    }
}
