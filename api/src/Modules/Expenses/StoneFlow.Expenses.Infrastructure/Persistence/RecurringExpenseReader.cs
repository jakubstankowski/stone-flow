using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.ListRecurringExpenses;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Odczyty definicji: projekcje do DTO, filtrowanie, sortowanie i stronicowanie w bazie. Harmonogramy
/// (dni, wyłączenia, rejestr) dla wielu definicji naraz czytają się stałą liczbą zapytań, bez N+1. Stan
/// i terminy liczy domena; SQL odtwarza regułę 7 wyłącznie w predykacie filtra <c>state</c> (zgodność
/// sprawdza test listy).
/// </summary>
internal sealed class RecurringExpenseReader(ExpensesDbContext context) : IRecurringExpenseReader
{
    /// <summary>Kandydaci zadania pomijają definicje zakończone dawniej (Assumptions 14).</summary>
    private const int CandidateEndedWithinDays = 400;

    public async Task<RecurringRowsPage> ListAsync(RecurringListCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var definitions = Filter(context.RecurringDefinitions, criteria);
        var total = await definitions.CountAsync(cancellationToken);

        var skip = (long)(criteria.Page - 1) * criteria.PageSize;
        if (skip >= total)
        {
            return new RecurringRowsPage([], total);
        }

        var rows = await Sort(definitions, criteria)
            .Skip((int)skip)
            .Take(criteria.PageSize)
            .Select(d => new
            {
                d.Id,
                d.Description,
                d.Category,
                d.Counterparty,
                d.GrossAmount,
                d.DayOfMonth,
                d.StartsOn,
                d.EndsOn,
                d.LastDueDate,
                d.PausedOn,
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(row => row.Id).ToList();
        var schedules = await SchedulesAsync(context.RecurringDefinitions.Where(d => ids.Contains(d.Id)), cancellationToken);
        return new RecurringRowsPage(
            [
                .. rows.Where(row => schedules.ContainsKey(row.Id)).Select(row => new RecurringListRowDto(
                    row.Id,
                    row.Description,
                    row.Category,
                    row.Counterparty,
                    row.GrossAmount,
                    row.DayOfMonth,
                    row.StartsOn,
                    row.EndsOn,
                    row.LastDueDate,
                    row.PausedOn,
                    schedules[row.Id])),
            ],
            total);
    }

    public async Task<RecurringDefinitionRecordDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await context.RecurringDefinitions
            .Where(d => d.Id == id)
            .Select(d => new RecurringDefinitionRecordDto(
                d.Id,
                d.Description,
                d.Category,
                d.Counterparty,
                d.GrossAmount,
                d.NetAmount,
                d.PaymentMethod,
                d.Payer,
                d.Comment,
                d.DayOfMonth,
                d.StartsOn,
                d.EndsOn,
                d.LastDueDate,
                d.PausedOn,
                d.CreatedAtUtc,
                d.UpdatedAtUtc,
                EF.Property<byte[]>(d, RecurringExpenseDefinitionConfiguration.VersionProperty),
                context.Expenses.Count(e => e.RecurringDefinitionId == d.Id),
                null!))
            .SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            return null;
        }

        var schedules = await SchedulesAsync(context.RecurringDefinitions.Where(d => d.Id == id), cancellationToken);
        if (!schedules.TryGetValue(id, out var schedule))
        {
            // Usunięta po odczycie nagłówka.
            return null;
        }

        return record with
        {
            CreatedAtUtc = DateTime.SpecifyKind(record.CreatedAtUtc, DateTimeKind.Utc),
            UpdatedAtUtc = DateTime.SpecifyKind(record.UpdatedAtUtc, DateTimeKind.Utc),
            Schedule = schedule,
        };
    }

    public async Task<RecurringPreviewData?> GetPreviewDataAsync(Guid id, CancellationToken cancellationToken)
    {
        var schedules = await SchedulesAsync(context.RecurringDefinitions.Where(d => d.Id == id), cancellationToken);
        if (!schedules.TryGetValue(id, out var schedule))
        {
            return null;
        }

        // Terminy z rejestru tylko dla wystąpień, które nadal istnieją (usunięte nie „pozostaną”).
        var existing = await context.RecurringPeriods.AsNoTracking()
            .Where(period => period.DefinitionId == id && context.Expenses.Any(e => e.Id == period.ExpenseId))
            .Select(period => period.DueDate)
            .ToListAsync(cancellationToken);
        return new RecurringPreviewData(schedule, existing);
    }

    public async Task<IReadOnlyList<RecurringGenerationCandidate>> ListGenerationCandidatesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var endedAfter = today.AddDays(-CandidateEndedWithinDays);
        var candidates = context.RecurringDefinitions
            .Where(d => d.PausedOn == null && d.StartsOn <= today && (d.EndsOn == null || d.EndsOn >= endedAfter));
        var schedules = await SchedulesAsync(candidates, cancellationToken);
        return [.. schedules.Select(pair => new RecurringGenerationCandidate(pair.Key, pair.Value))];
    }

    public async Task<RecurringRequestMatch?> FindRequestAsync(Guid key, CancellationToken cancellationToken) =>
        await context.RecurringDefinitionRequests.AsNoTracking()
            .Where(request => request.Key == key)
            .Select(request => new RecurringRequestMatch(request.DefinitionId, request.RequestHash, request.CreatedOccurrences))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<byte[]?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        await context.RecurringDefinitions
            .Where(d => d.Id == id)
            .Select(d => EF.Property<byte[]>(d, RecurringExpenseDefinitionConfiguration.VersionProperty))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Harmonogramy definicji z zapytania: cztery zapytania niezależnie od liczby definicji (nagłówki,
    /// dni, wyłączenia, rejestr).
    /// </summary>
    private async Task<Dictionary<Guid, RecurringScheduleData>> SchedulesAsync(
        IQueryable<RecurringExpenseDefinition> definitions, CancellationToken cancellationToken)
    {
        var heads = await definitions
            .Select(d => new { d.Id, d.StartsOn, d.EndsOn, d.PausedOn })
            .ToListAsync(cancellationToken);
        var days = (await definitions
                .SelectMany(d => d.Days, (d, day) => new { d.Id, day.FromPeriod, day.DayOfMonth })
                .ToListAsync(cancellationToken))
            .ToLookup(day => day.Id);
        var exclusions = (await definitions
                .SelectMany(d => d.Exclusions, (d, range) => new { d.Id, range.FromDate, range.ToDate, range.Reason })
                .ToListAsync(cancellationToken))
            .ToLookup(range => range.Id);
        var registry = (await context.RecurringPeriods.AsNoTracking()
                .Where(period => definitions.Any(d => d.Id == period.DefinitionId))
                .Select(period => new { period.DefinitionId, period.Period, period.DueDate })
                .ToListAsync(cancellationToken))
            .ToLookup(period => period.DefinitionId);

        // Zapytania nie mają wspólnej migawki: definicja usunięta między nimi nie ma już dni i jest
        // pomijana (lista jej nie pokazuje, szczegóły dają 404, zadanie jej nie przetwarza), zamiast
        // wywracać odczyt wyjątkiem stanu bez historii dni.
        return heads.Where(head => days[head.Id].Any()).ToDictionary(
            head => head.Id,
            head => new RecurringScheduleData(
                new RecurringScheduleState(
                    head.StartsOn,
                    head.EndsOn,
                    head.PausedOn,
                    days[head.Id].Select(day => new RecurringDayEntry(RecurringPeriod.Of(day.FromPeriod), day.DayOfMonth)),
                    exclusions[head.Id].Select(range => new RecurringExclusionRange(range.FromDate, range.ToDate, range.Reason))),
                registry[head.Id].ToDictionary(period => RecurringPeriod.Of(period.Period), period => period.DueDate)));
    }

    private static IQueryable<RecurringExpenseDefinition> Filter(IQueryable<RecurringExpenseDefinition> definitions, RecurringListCriteria criteria)
    {
        // Predykaty odpowiadają regule RecurringSchedule.Status: „Zakończony” ma pierwszeństwo przed „Wstrzymany”.
        var today = criteria.Today;
        definitions = criteria.State switch
        {
            RecurringDefinitionStatus.Ended => definitions.Where(d => d.LastDueDate != null && d.LastDueDate <= today),
            RecurringDefinitionStatus.Paused => definitions.Where(d => (d.LastDueDate == null || d.LastDueDate > today) && d.PausedOn != null),
            RecurringDefinitionStatus.Active => definitions.Where(d => (d.LastDueDate == null || d.LastDueDate > today) && d.PausedOn == null),
            _ => definitions,
        };

        if (criteria.Search is { } search)
        {
            var pattern = "%" + ExpenseReader.EscapeLike(search) + "%";
            definitions = definitions.Where(d => EF.Functions.Like(d.Description, pattern, ExpenseReader.LikeEscape)
                || (d.Counterparty != null && EF.Functions.Like(d.Counterparty, pattern, ExpenseReader.LikeEscape)));
        }

        return definitions;
    }

    private static IOrderedQueryable<RecurringExpenseDefinition> Sort(IQueryable<RecurringExpenseDefinition> definitions, RecurringListCriteria criteria) =>
        (criteria.Sort, criteria.Descending) switch
        {
            (RecurringSort.StartsOn, false) => definitions.OrderBy(d => d.StartsOn).ThenBy(d => d.Id),
            (RecurringSort.StartsOn, true) => definitions.OrderByDescending(d => d.StartsOn).ThenByDescending(d => d.Id),
            (RecurringSort.GrossAmount, false) => definitions.OrderBy(d => d.GrossAmount).ThenBy(d => d.Id),
            (RecurringSort.GrossAmount, true) => definitions.OrderByDescending(d => d.GrossAmount).ThenByDescending(d => d.Id),
            (_, false) => definitions.OrderBy(d => d.Description).ThenBy(d => d.Id),
            (_, true) => definitions.OrderByDescending(d => d.Description).ThenByDescending(d => d.Id),
        };
}
