using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.ListRecurringGenerationCandidates;

/// <summary>Faza odczytu zadania: id definicji z co najmniej jednym okresem należnym na <paramref name="Today"/>.</summary>
public sealed record ListRecurringGenerationCandidatesQuery(DateOnly Today) : IQuery<IReadOnlyList<Guid>>;

/// <summary>
/// Kandydaci z bazy stałą liczbą zapytań (definicje, dni, wyłączenia, rejestr), a o należności decyduje
/// domena (Assumptions 14). Wynik to tylko podpowiedź: komenda liczy okresy od nowa pod blokadą.
/// </summary>
public sealed class ListRecurringGenerationCandidatesHandler(IRecurringExpenseReader reader)
    : IQueryHandler<ListRecurringGenerationCandidatesQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(ListRecurringGenerationCandidatesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var candidates = await reader.ListGenerationCandidatesAsync(query.Today, cancellationToken);
        return
        [
            .. candidates
                .Where(candidate => RecurringSchedule.DuePeriods(candidate.Schedule.State, candidate.Schedule.Registry, query.Today).Count > 0)
                .Select(candidate => candidate.Id),
        ];
    }
}
