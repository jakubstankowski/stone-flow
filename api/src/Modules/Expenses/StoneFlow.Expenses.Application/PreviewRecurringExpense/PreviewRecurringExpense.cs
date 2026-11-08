using System.Globalization;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.Recurring;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.LaunchDate;

namespace StoneFlow.Expenses.Application.PreviewRecurringExpense;

/// <summary>Parametry podglądu z zapytania HTTP (tekst); <see cref="DefinitionId"/> przy edycji.</summary>
public sealed record PreviewRecurringExpenseQuery(string? DayOfMonth, string? StartsOn, string? EndsOn, string? DefinitionId)
    : IQuery<PreviewRecurringExpenseResult>;

/// <summary>
/// Podgląd zapisu. Liczby spoza zakresu (<see cref="DroppedDueCount"/>, <see cref="CreatedAfterEndCount"/>,
/// <see cref="CreatedBeforeStartCount"/>) tylko przy edycji, inaczej 0. <see cref="Paused"/>: definicja
/// wstrzymana — nic nie powstaje do wznowienia (tekst NC1).
/// </summary>
public sealed record RecurringExpensePreviewDto(
    int OccurrencesToCreate,
    DateOnly? FirstImmediateDueDate,
    int BeforeLaunchCount,
    DateOnly? NextDueDate,
    bool Paused,
    int DroppedDueCount,
    int CreatedAfterEndCount,
    int CreatedBeforeStartCount);

public enum PreviewRecurringExpenseOutcome
{
    Previewed,
    ValidationFailed,
    NotFound,
    PausedDayChange,
}

public sealed record PreviewRecurringExpenseResult(
    PreviewRecurringExpenseOutcome Outcome, RecurringExpensePreviewDto? Preview = null, FieldErrors? Errors = null);

/// <summary>
/// Podgląd liczony przez domenę na kopii stanu tymi samymi operacjami co zapis (bez zapisu): nowa
/// definicja albo zmiana istniejącej (kolejność reguł jak w <c>PUT</c>: zmiana dnia na wstrzymanej
/// → 409 przed „nie wypada żaden termin” → 400).
/// </summary>
public sealed class PreviewRecurringExpenseHandler(IRecurringExpenseReader reader, ILaunchDate launchDate, TimeProvider timeProvider)
    : IQueryHandler<PreviewRecurringExpenseQuery, PreviewRecurringExpenseResult>
{
    public async Task<PreviewRecurringExpenseResult> HandleAsync(PreviewRecurringExpenseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new FieldErrors();
        var day = ExpenseInput.Text(query.DayOfMonth) is { } dayText
            && int.TryParse(dayText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDay)
                ? parsedDay
                : (int?)null;
        var schedule = RecurringExpenseInput.ValidateSchedule(day, query.StartsOn, query.EndsOn, errors);

        Guid? definitionId = null;
        if (ExpenseInput.Text(query.DefinitionId) is { } idText)
        {
            if (Guid.TryParse(idText, out var parsedId))
            {
                definitionId = parsedId;
            }
            else
            {
                errors.Add(RecurringFieldKeys.DefinitionId, RecurringMessages.DefinitionIdInvalid);
            }
        }

        if (!errors.IsEmpty || schedule is null)
        {
            return new PreviewRecurringExpenseResult(PreviewRecurringExpenseOutcome.ValidationFailed, Errors: errors);
        }

        var today = WarsawTime.Today(timeProvider);
        if (definitionId is not { } id)
        {
            var state = RecurringScheduleState.New(schedule);
            if (RecurringExpenseDefinition.CheckNew(schedule) is not null)
            {
                return NoDueInRange();
            }

            return Previewed(RecurringSchedule.Outlook(state, RecurringSchedule.EmptyRegistry, today), paused: false, 0, 0, 0);
        }

        var data = await reader.GetPreviewDataAsync(id, cancellationToken);
        if (data is null)
        {
            return new PreviewRecurringExpenseResult(PreviewRecurringExpenseOutcome.NotFound);
        }

        var (current, registry) = (data.Schedule.State, data.Schedule.Registry);
        var edit = RecurringSchedule.Edit(current, schedule, today, registry);
        switch (edit.Violation)
        {
            case RecurringRuleViolation.PausedDayChange:
                return new PreviewRecurringExpenseResult(PreviewRecurringExpenseOutcome.PausedDayChange);
            case not null:
                return NoDueInRange();
        }

        var next = edit.Next!;
        var dropped = RecurringSchedule.DuePeriods(current, registry, today).Count(period => !RecurringSchedule.Qualifies(next, registry, period));
        var afterEnd = data.ExistingOccurrenceDueDates.Count(due => next.EndsOn is { } endsOn && due > endsOn);
        var beforeStart = data.ExistingOccurrenceDueDates.Count(due => due < next.StartsOn);
        return Previewed(RecurringSchedule.Outlook(next, registry, today), next.IsPaused, dropped, afterEnd, beforeStart);
    }

    private static PreviewRecurringExpenseResult NoDueInRange() =>
        new(PreviewRecurringExpenseOutcome.ValidationFailed, Errors: new FieldErrors().Add(RecurringFieldKeys.EndsOn, RecurringMessages.NoDueInRange));

    private PreviewRecurringExpenseResult Previewed(RecurringOutlook outlook, bool paused, int dropped, int afterEnd, int beforeStart)
    {
        var immediate = outlook.ImmediateDueDates;
        return new PreviewRecurringExpenseResult(
            PreviewRecurringExpenseOutcome.Previewed,
            new RecurringExpensePreviewDto(
                immediate.Count,
                immediate.Count > 0 ? immediate[0] : null,
                immediate.Count(due => due < launchDate.Value),
                outlook.NextDueDate,
                paused,
                dropped,
                afterEnd,
                beforeStart));
    }
}
