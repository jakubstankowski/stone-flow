using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.GetRecurringExpense;

/// <summary>Skutek wznowienia dzisiaj: liczba wystąpień od razu i następny termin po zapisie.</summary>
public sealed record RecurringResumePreviewDto(int OccurrencesToCreate, DateOnly? NextDueDate);

/// <summary>
/// Szczegóły: dane, stan (reguła 7), najbliższy nieutworzony termin (reguła 6) i — tylko przy
/// wstrzymaniu — podgląd wznowienia.
/// </summary>
public sealed record RecurringExpenseDetailsDto(
    RecurringDefinitionRecordDto Definition,
    RecurringDefinitionStatus Status,
    DateOnly? NextDueDate,
    RecurringResumePreviewDto? ResumePreview);

public sealed record GetRecurringExpenseQuery(Guid Id) : IQuery<RecurringExpenseDetailsDto?>;

/// <summary>Szczegóły definicji; „dzisiaj” w Europe/Warsaw. Terminy i stan liczy domena.</summary>
public sealed class GetRecurringExpenseHandler(IRecurringExpenseReader reader, TimeProvider timeProvider)
    : IQueryHandler<GetRecurringExpenseQuery, RecurringExpenseDetailsDto?>
{
    public async Task<RecurringExpenseDetailsDto?> HandleAsync(GetRecurringExpenseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var record = await reader.GetAsync(query.Id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var today = WarsawTime.Today(timeProvider);
        var (state, registry) = (record.Schedule.State, record.Schedule.Registry);
        RecurringResumePreviewDto? resume = null;
        if (state.IsPaused)
        {
            var outlook = RecurringSchedule.Outlook(state.Resumed(today), registry, today);
            resume = new RecurringResumePreviewDto(outlook.ImmediateDueDates.Count, outlook.NextDueDate);
        }

        return new RecurringExpenseDetailsDto(
            record,
            RecurringSchedule.Status(record.LastDueDate, record.PausedOn, today),
            RecurringSchedule.NextUncreatedDueDate(state, registry),
            resume);
    }
}
