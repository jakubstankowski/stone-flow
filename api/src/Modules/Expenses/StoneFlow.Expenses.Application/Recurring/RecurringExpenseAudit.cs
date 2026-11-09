using System.Globalization;
using StoneFlow.Expenses.Domain.Recurring;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Expenses.Application.Recurring;

/// <summary>
/// Wpisy audytu definicji cyklicznej (SF-7, „Value format”): obiekt <c>RecurringExpenseDefinition</c>
/// bez rodzica, data biznesowa to początek, etykieta „Wydatek cykliczny – {kategoria}” (bez wolnego
/// tekstu). Zmiana dnia, wstrzymanie i wznowienie to <c>Updated</c> z polami <c>dayOfMonth</c>
/// i <c>pausedOn</c>; historia dni, wyłączenia i <c>LastDueDate</c> wynikają z tych pól i nie są
/// osobnymi polami. „Płatnik” to dane osobowe.
/// </summary>
public static class RecurringExpenseAudit
{
    public static AuditEntry Created(RecurringExpenseDefinition definition, AuditAuthor author)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(author);

        var changes = definition.Snapshot()
            .Select(field => (field.Field, Value: Format(field.Value)))
            .Where(field => field.Value is not null)
            .Select(field => Change(field.Field, null, field.Value))
            .ToList();

        return new AuditEntry(author, Object(definition), AuditOperation.Created, changes);
    }

    /// <summary>Zmiana: tylko zmienione pola; data biznesowa to nowy początek.</summary>
    public static AuditEntry Updated(RecurringExpenseDefinition definition, IReadOnlyList<RecurringFieldChange> changes, AuditAuthor author)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(author);

        return new AuditEntry(
            author,
            Object(definition),
            AuditOperation.Updated,
            [.. changes.Select(change => Change(change.Field, Format(change.OldValue), Format(change.NewValue)))]);
    }

    /// <summary>Usunięcie (fizyczne): wszystkie niepuste pola jako stare wartości.</summary>
    public static AuditEntry Deleted(RecurringExpenseDefinition definition, AuditAuthor author)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(author);

        var changes = definition.Snapshot()
            .Select(field => (field.Field, Value: Format(field.Value)))
            .Where(field => field.Value is not null)
            .Select(field => Change(field.Field, field.Value, null))
            .ToList();

        return new AuditEntry(author, Object(definition), AuditOperation.Deleted, changes);
    }

    /// <summary>Etykieta obiektu: „Wydatek cykliczny – {etykieta kategorii}”.</summary>
    public static string Label(RecurringExpenseDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return $"Wydatek cykliczny – {ExpenseCodes.LabelOf(definition.Category)}";
    }

    /// <summary>Wartość pola jako tekst: jak w <see cref="ExpenseAudit.Format"/>, dzień miesiąca jako liczba.</summary>
    public static string? Format(object? value) => value switch
    {
        int day => day.ToString(CultureInfo.InvariantCulture),
        _ => ExpenseAudit.Format(value),
    };

    public static bool IsPersonalData(RecurringDefinitionField field) => field == RecurringDefinitionField.Payer;

    private static AuditChange Change(RecurringDefinitionField field, string? oldValue, string? newValue) =>
        new(RecurringCodes.Of(field), oldValue, newValue, IsPersonalData(field));

    private static AuditObject Object(RecurringExpenseDefinition definition) =>
        new(AuditObjectType.RecurringExpenseDefinition, definition.Id, definition.StartsOn, Label(definition));
}
