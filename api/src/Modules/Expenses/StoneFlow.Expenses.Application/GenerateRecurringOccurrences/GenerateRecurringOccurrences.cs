using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Expenses.Application.GenerateRecurringOccurrences;

/// <summary>Komenda zadania <c>recurring-expenses</c> dla jednej definicji; <paramref name="Today"/> to dzień startu przebiegu.</summary>
public sealed record GenerateRecurringOccurrencesCommand(Guid DefinitionId, DateOnly Today) : ICommand<GenerateRecurringOccurrencesResult>;

public enum GenerateRecurringOccurrencesOutcome
{
    /// <summary>Utworzono wystąpienia (także zero).</summary>
    Created,

    /// <summary>Brak definicji (usunięta po fazie odczytu): nie jest błędem.</summary>
    DefinitionMissing,

    /// <summary>Kolizja rejestru: jednostka wycofana; zadanie ponawia raz w nowym zakresie.</summary>
    DuplicatePeriod,
}

public sealed record GenerateRecurringOccurrencesResult(GenerateRecurringOccurrencesOutcome Outcome, int Created = 0);

/// <summary>
/// Własna jednostka pracy: blokada wiersza definicji, wczytanie z dniami i wyłączeniami i
/// <see cref="OccurrenceGenerator"/> na dzień przebiegu (okresy liczone od nowa, więc zmiana definicji
/// po fazie odczytu jest uwzględniona). Zatwierdzany jest tylko wynik <c>Created</c>. Ponowienia,
/// zakresy DI i pętla po definicjach należą do zadania w Infrastructure.
/// </summary>
public sealed class GenerateRecurringOccurrencesHandler(
    IUnitOfWork unitOfWork,
    IRecurringDefinitionLock definitionLock,
    IRecurringExpenseStore store,
    OccurrenceGenerator generator) : ICommandHandler<GenerateRecurringOccurrencesCommand, GenerateRecurringOccurrencesResult>
{
    public Task<GenerateRecurringOccurrencesResult> HandleAsync(GenerateRecurringOccurrencesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                if (!await definitionLock.LockAsync(command.DefinitionId, ct)
                    || await store.LoadAsync(command.DefinitionId, ct) is not { } stored)
                {
                    return new GenerateRecurringOccurrencesResult(GenerateRecurringOccurrencesOutcome.DefinitionMissing);
                }

                var generation = await generator.GenerateAsync(stored.Definition, command.Today, ct);
                return generation.Status switch
                {
                    OccurrenceGenerationStatus.Created => new GenerateRecurringOccurrencesResult(GenerateRecurringOccurrencesOutcome.Created, generation.Created),
                    OccurrenceGenerationStatus.DuplicatePeriod => new GenerateRecurringOccurrencesResult(GenerateRecurringOccurrencesOutcome.DuplicatePeriod),
                    _ => new GenerateRecurringOccurrencesResult(GenerateRecurringOccurrencesOutcome.DefinitionMissing),
                };
            },
            commitWhen: result => result.Outcome == GenerateRecurringOccurrencesOutcome.Created,
            cancellationToken);
    }
}
