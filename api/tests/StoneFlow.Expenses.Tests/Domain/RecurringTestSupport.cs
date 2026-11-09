using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Expenses.Domain.Recurring;

namespace StoneFlow.Expenses.Tests.Domain;

/// <summary>
/// Symulacja definicji z rejestrem okresów: kroki jak w przypadkach użycia (edycja i wznowienie —
/// operacja, potem generowanie; wstrzymanie — generowanie, potem operacja; przebieg zadania —
/// generowanie). Dane fikcyjne.
/// </summary>
internal sealed class RecurringSim
{
    public static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private RecurringSim(RecurringExpenseDefinition definition) => Definition = definition;

    public RecurringExpenseDefinition Definition { get; }

    public Dictionary<RecurringPeriod, DateOnly> Registry { get; } = [];

    /// <summary>Utworzone wystąpienia: okres → termin, rosnąco.</summary>
    public IReadOnlyList<DateOnly> Created => [.. Registry.OrderBy(pair => pair.Key).Select(pair => pair.Value)];

    public static RecurringContent Content(decimal gross = 300m) =>
        new("Abonament", ExpenseCategory.Marketing, "Dostawca B", gross, null, ExpensePaymentMethod.Transfer, null, null);

    public static DateOnly D(int day, int month, int year = 2026) => new(year, month, day);

    /// <summary>Dodanie definicji i generowanie na dzień zapisu.</summary>
    public static RecurringSim Create(int day, DateOnly startsOn, DateOnly? endsOn, DateOnly today)
    {
        var sim = new RecurringSim(RecurringExpenseDefinition.Create(Guid.NewGuid(), Content(), new RecurringScheduleInput(day, startsOn, endsOn), Now));
        sim.Run(today);
        return sim;
    }

    /// <summary>Generowanie na dzień (przebieg zadania); zwraca terminy utworzonych wystąpień.</summary>
    public IReadOnlyList<DateOnly> Run(DateOnly today)
    {
        var created = new List<DateOnly>();
        foreach (var period in Definition.DuePeriods(Registry, today))
        {
            var occurrence = Definition.CreateOccurrence(Guid.NewGuid(), period, Now);
            Registry[period] = occurrence.Period.DueDate;
            created.Add(occurrence.Period.DueDate);
        }

        return created;
    }

    /// <summary>Zmiana harmonogramu (treść bez zmian) i generowanie przy udanym zapisie.</summary>
    public (RecurringChangeSet Change, IReadOnlyList<DateOnly> Created) Edit(int day, DateOnly startsOn, DateOnly? endsOn, DateOnly today, RecurringContent? content = null)
    {
        var change = Definition.Edit(content ?? Content(Definition.GrossAmount), new RecurringScheduleInput(day, startsOn, endsOn), today, Registry, Now);
        return (change, change.Succeeded && change.Changes.Count > 0 ? Run(today) : []);
    }

    /// <summary>Zmiana wybranych pól harmonogramu (pozostałe bez zmian); <paramref name="noEnd"/> usuwa koniec.</summary>
    public (RecurringChangeSet Change, IReadOnlyList<DateOnly> Created) Change(
        DateOnly today, int? day = null, DateOnly? startsOn = null, DateOnly? endsOn = null, bool noEnd = false) =>
        Edit(day ?? Definition.DayOfMonth, startsOn ?? Definition.StartsOn, noEnd ? null : endsOn ?? Definition.EndsOn, today);

    public (RecurringChangeSet Change, IReadOnlyList<DateOnly> Created) Pause(DateOnly today)
    {
        if (Definition.CheckPause(today) is { } violation)
        {
            return (RecurringChangeSet.Rejected(violation), []);
        }

        var created = Run(today);
        return (Definition.Pause(today, Now), created);
    }

    public (RecurringChangeSet Change, IReadOnlyList<DateOnly> Created) Resume(DateOnly today)
    {
        var change = Definition.Resume(today, Now);
        return (change, change.Succeeded ? Run(today) : []);
    }

    public DateOnly? NextDueDate() => RecurringSchedule.NextUncreatedDueDate(Definition.Schedule, Registry);

    public DateOnly? NextDueDateAfter(DateOnly today) => RecurringSchedule.NextDueDateAfter(Definition.Schedule, Registry, today);
}
