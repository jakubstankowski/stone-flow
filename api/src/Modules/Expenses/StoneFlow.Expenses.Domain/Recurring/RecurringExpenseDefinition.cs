using StoneFlow.Expenses.Domain.Expenses;

namespace StoneFlow.Expenses.Domain.Recurring;

/// <summary>
/// Definicja wydatku cyklicznego (miesięcznego). Treść kopiuje się do wystąpień tworzonych w dniu
/// terminu; o okresach decydują czyste funkcje <see cref="RecurringSchedule"/> na stanie
/// <see cref="Schedule"/>: początek, koniec, historia dni (<see cref="Days"/>), przedziały wyłączeń
/// (<see cref="Exclusions"/>) i wstrzymanie. Encja utrzymuje <see cref="DayOfMonth"/> równy dniu
/// ostatniego wpisu historii i <see cref="LastDueDate"/> (reguła 6) oraz wylicza zmienione pola audytu.
/// </summary>
public sealed class RecurringExpenseDefinition
{
    private readonly List<RecurringExpenseDay> _days = [];
    private readonly List<RecurringExpenseExclusion> _exclusions = [];

    private RecurringExpenseDefinition(Guid id, DateTime nowUtc)
    {
        Id = id;
        Description = string.Empty;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    // Dla EF Core.
    private RecurringExpenseDefinition()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Description { get; private set; }

    public ExpenseCategory Category { get; private set; }

    public string? Counterparty { get; private set; }

    /// <summary>Kwota brutto w złotych, &gt; 0.</summary>
    public decimal GrossAmount { get; private set; }

    public decimal? NetAmount { get; private set; }

    public ExpensePaymentMethod? PaymentMethod { get; private set; }

    public string? Payer { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>Bieżący dzień miesiąca: dzień ostatniego wpisu historii dni.</summary>
    public int DayOfMonth { get; private set; }

    /// <summary>Początek (włącznie); data biznesowa audytu.</summary>
    public DateOnly StartsOn { get; private set; }

    /// <summary>Koniec (włącznie) albo brak.</summary>
    public DateOnly? EndsOn { get; private set; }

    /// <summary>Ostatni termin efektywny w zakresie (reguła 6); pusty bez końca.</summary>
    public DateOnly? LastDueDate { get; private set; }

    /// <summary>Dzień wstrzymania; pusty = niewstrzymana.</summary>
    public DateOnly? PausedOn { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<RecurringExpenseDay> Days => _days;

    public IReadOnlyList<RecurringExpenseExclusion> Exclusions => _exclusions;

    /// <summary>Stan harmonogramu dla funkcji <see cref="RecurringSchedule"/>.</summary>
    public RecurringScheduleState Schedule => new(
        StartsOn,
        EndsOn,
        PausedOn,
        _days.Select(day => new RecurringDayEntry(RecurringPeriod.Of(day.FromPeriod), day.DayOfMonth)),
        _exclusions.Select(range => new RecurringExclusionRange(range.FromDate, range.ToDate, range.Reason)));

    /// <summary>
    /// Nowa definicja: wpis dnia od okresu początku, bez wyłączeń, niewstrzymana.
    /// </summary>
    /// <exception cref="ArgumentException">Dane naruszają reguły, które sprawdza walidacja przed utworzeniem.</exception>
    public static RecurringExpenseDefinition Create(Guid id, RecurringContent content, RecurringScheduleInput schedule, DateTime nowUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Brak identyfikatora definicji.", nameof(id));
        }

        Require(content);
        Require(schedule);
        var state = RecurringScheduleState.New(schedule);
        if (!RecurringSchedule.HasEffectiveDueInRange(state, RecurringSchedule.EmptyRegistry))
        {
            throw new ArgumentException("W zakresie definicji nie wypada żaden termin.", nameof(schedule));
        }

        var definition = new RecurringExpenseDefinition(id, nowUtc);
        definition.ApplyContent(content);
        definition.ApplySchedule(state, RecurringSchedule.EmptyRegistry, nowUtc);
        return definition;
    }

    /// <summary>
    /// Sprawdzenie stanu nowej definicji (bez tworzenia): <see cref="RecurringRuleViolation.NoDueInRange"/>,
    /// gdy w zakresie nie wypada żaden termin.
    /// </summary>
    public static RecurringRuleViolation? CheckNew(RecurringScheduleInput schedule) =>
        RecurringSchedule.HasEffectiveDueInRange(RecurringScheduleState.New(schedule), RecurringSchedule.EmptyRegistry)
            ? null
            : RecurringRuleViolation.NoDueInRange;

    /// <summary>
    /// Zmiana treści i harmonogramu (kolejność NC3 kroki 3–5): zakaz zmiany dnia na definicji
    /// wstrzymanej, co najmniej jeden termin efektywny w zakresie, potem nowe pola, wpis dnia od
    /// bieżącego okresu, przedział <c>StartMoved</c> i nowy <see cref="LastDueDate"/>. Rejestr (okresy
    /// z wystąpieniem i ich terminy) wczytuje wywołujący pod blokadą.
    /// </summary>
    public RecurringChangeSet Edit(
        RecurringContent content,
        RecurringScheduleInput schedule,
        DateOnly today,
        IReadOnlyDictionary<RecurringPeriod, DateOnly> registry,
        DateTime nowUtc)
    {
        Require(content);
        Require(schedule);
        ArgumentNullException.ThrowIfNull(registry);

        var edit = RecurringSchedule.Edit(Schedule, schedule, today, registry);
        if (edit.Violation is { } violation)
        {
            return RecurringChangeSet.Rejected(violation);
        }

        var before = Snapshot();
        ApplyContent(content);
        ApplySchedule(edit.Next!, registry, nowUtc);
        return Changed(before, nowUtc);
    }

    /// <summary>Reguła wstrzymania: już wstrzymana albo zakończona (bez kolejnego terminu) — bez zmiany stanu.</summary>
    public RecurringRuleViolation? CheckPause(DateOnly today) =>
        PausedOn is not null ? RecurringRuleViolation.AlreadyPaused
        : StatusOn(today) == RecurringDefinitionStatus.Ended ? RecurringRuleViolation.Ended
        : null;

    /// <summary>
    /// Wstrzymanie: tylko <c>PausedOn = dzisiaj</c>. Wywołujący najpierw tworzy terminy należne do
    /// dzisiaj włącznie (inaczej dzisiejszy termin wpadłby w przedział wstrzymania).
    /// </summary>
    public RecurringChangeSet Pause(DateOnly today, DateTime nowUtc)
    {
        if (CheckPause(today) is { } violation)
        {
            return RecurringChangeSet.Rejected(violation);
        }

        var before = Snapshot();
        ApplyState(Schedule.Paused(today), nowUtc);
        return Changed(before, nowUtc);
    }

    /// <summary>Wznowienie: przedział <c>[PausedOn, dzisiaj)</c> (pominięty, gdy pusty) i wyczyszczenie <c>PausedOn</c>.</summary>
    public RecurringChangeSet Resume(DateOnly today, DateTime nowUtc)
    {
        if (PausedOn is null)
        {
            return RecurringChangeSet.Rejected(RecurringRuleViolation.NotPaused);
        }

        var before = Snapshot();
        ApplyState(Schedule.Resumed(today), nowUtc);
        return Changed(before, nowUtc);
    }

    public RecurringDefinitionStatus StatusOn(DateOnly today) => RecurringSchedule.Status(LastDueDate, PausedOn, today);

    /// <summary>Okresy należne na dzień <paramref name="day"/>, których nie ma w rejestrze (reguła 4), rosnąco.</summary>
    public IReadOnlyList<RecurringPeriod> DuePeriods(IReadOnlyDictionary<RecurringPeriod, DateOnly> registry, DateOnly day) =>
        RecurringSchedule.DuePeriods(Schedule, registry, day);

    /// <summary>
    /// Wystąpienie okresu z bieżącą treścią definicji: data kosztu i termin płatności równe terminowi
    /// okresu, bez daty zapłaty (koszt i plan, nie wypływ) i bez referencji dokumentu, oraz wiersz rejestru.
    /// </summary>
    public RecurringOccurrence CreateOccurrence(Guid expenseId, RecurringPeriod period, DateTime nowUtc)
    {
        var due = RecurringSchedule.DueDate(Schedule, period);
        var content = new ExpenseContent(
            Description,
            Category,
            Counterparty,
            CostDate: due,
            DueDate: due,
            PaidOn: null,
            GrossAmount,
            NetAmount,
            DocumentReference: null,
            PaymentMethod,
            Payer,
            Comment);
        var expense = Expense.CreateOccurrence(expenseId, content, Id, period, nowUtc);
        return new RecurringOccurrence(expense, new RecurringExpensePeriod(Id, period, due, expenseId, nowUtc));
    }

    /// <summary>Wartości wszystkich pól objętych audytem, w stałej kolejności.</summary>
    public IReadOnlyList<RecurringFieldValue> Snapshot() =>
    [
        new(RecurringDefinitionField.Description, Description),
        new(RecurringDefinitionField.Category, Category),
        new(RecurringDefinitionField.Counterparty, Counterparty),
        new(RecurringDefinitionField.GrossAmount, GrossAmount),
        new(RecurringDefinitionField.NetAmount, NetAmount),
        new(RecurringDefinitionField.PaymentMethod, PaymentMethod),
        new(RecurringDefinitionField.Payer, Payer),
        new(RecurringDefinitionField.Comment, Comment),
        new(RecurringDefinitionField.DayOfMonth, DayOfMonth),
        new(RecurringDefinitionField.StartsOn, StartsOn),
        new(RecurringDefinitionField.EndsOn, EndsOn),
        new(RecurringDefinitionField.PausedOn, PausedOn),
    ];

    private static void Require(RecurringContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Description, nameof(content));
        if (RecurringRules.CheckAmounts(content.GrossAmount, content.NetAmount) != RecurringAmountError.None)
        {
            throw new ArgumentException("Kwoty naruszają reguły wydatku cyklicznego.", nameof(content));
        }
    }

    private static void Require(RecurringScheduleInput schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (!RecurringRules.IsValidDay(schedule.DayOfMonth)
            || !RecurringRules.IsInDateRange(schedule.StartsOn)
            || schedule.EndsOn is { } endsOn && (endsOn > RecurringRules.MaxDate || endsOn < schedule.StartsOn))
        {
            throw new ArgumentException("Harmonogram narusza reguły wydatku cyklicznego.", nameof(schedule));
        }
    }

    private void ApplyContent(RecurringContent content)
    {
        Description = content.Description;
        Category = content.Category;
        Counterparty = content.Counterparty;
        GrossAmount = content.GrossAmount;
        NetAmount = content.NetAmount;
        PaymentMethod = content.PaymentMethod;
        Payer = content.Payer;
        Comment = content.Comment;
    }

    /// <summary>Przenosi stan harmonogramu do encji i przelicza <see cref="LastDueDate"/> (z rejestrem).</summary>
    private void ApplySchedule(RecurringScheduleState state, IReadOnlyDictionary<RecurringPeriod, DateOnly> registry, DateTime nowUtc)
    {
        ApplyState(state, nowUtc);
        LastDueDate = RecurringSchedule.LastDueDate(state, registry);
    }

    /// <summary>
    /// Synchronizacja kolekcji: wpis dnia o istniejącym okresie zmienia dzień (bez usuwania i dodawania
    /// tego samego klucza), brakujące wpisy są dodawane, zbędne usuwane; przedziały są tylko dopisywane.
    /// </summary>
    private void ApplyState(RecurringScheduleState state, DateTime nowUtc)
    {
        StartsOn = state.StartsOn;
        EndsOn = state.EndsOn;
        PausedOn = state.PausedOn;

        _days.RemoveAll(day => state.Days.All(entry => entry.FromPeriod.FirstDay != day.FromPeriod));
        foreach (var entry in state.Days)
        {
            var existing = _days.Find(day => day.FromPeriod == entry.FromPeriod.FirstDay);
            if (existing is null)
            {
                _days.Add(new RecurringExpenseDay(entry.FromPeriod, entry.DayOfMonth));
            }
            else
            {
                existing.DayOfMonth = entry.DayOfMonth;
            }
        }

        foreach (var range in state.Exclusions.Skip(_exclusions.Count))
        {
            _exclusions.Add(new RecurringExpenseExclusion(Guid.NewGuid(), range, nowUtc));
        }

        DayOfMonth = state.DayOfMonth;
    }

    private RecurringChangeSet Changed(IReadOnlyList<RecurringFieldValue> before, DateTime nowUtc)
    {
        var after = Snapshot();
        var changes = new List<RecurringFieldChange>();
        for (var index = 0; index < before.Count; index++)
        {
            if (!Equals(before[index].Value, after[index].Value))
            {
                changes.Add(new RecurringFieldChange(before[index].Field, before[index].Value, after[index].Value));
            }
        }

        if (changes.Count > 0)
        {
            UpdatedAtUtc = nowUtc;
        }

        return new RecurringChangeSet(changes);
    }
}
