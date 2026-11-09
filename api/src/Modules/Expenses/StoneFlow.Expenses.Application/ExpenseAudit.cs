using System.Globalization;
using StoneFlow.Expenses.Domain.Expenses;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Expenses.Application;

/// <summary>
/// Wpisy audytu wydatku (SF-7, konwencje „Value format”): obiekt <c>Expense</c>, data biznesowa
/// to data kosztu, etykieta „Wydatek – {kategoria}” (bez wolnego tekstu, więc bez danych
/// osobowych). Rodzic: definicja cykliczna dla wystąpienia (<see cref="ParentOf"/>), dla
/// pozostałych brak. „Płatnik” to dane osobowe.
/// </summary>
public static class ExpenseAudit
{
    /// <summary>Utworzenie: nowe wartości pól, bez pól pustych.</summary>
    public static AuditEntry Created(Expense expense, AuditAuthor author, AuditObjectReference? parent = null)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentNullException.ThrowIfNull(author);

        var changes = expense.Snapshot()
            .Select(field => (field.Field, Value: Format(field.Value)))
            .Where(field => field.Value is not null)
            .Select(field => Change(field.Field, null, field.Value))
            .ToList();

        return new AuditEntry(author, Object(expense, parent), AuditOperation.Created, changes);
    }

    /// <summary>Zmiana (poprawa i akcje zapłaty): tylko zmienione pola; data biznesowa to nowa data kosztu.</summary>
    public static AuditEntry Updated(
        Expense expense, IReadOnlyList<ExpenseFieldChange> changes, AuditAuthor author, AuditObjectReference? parent = null)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(author);

        return new AuditEntry(
            author,
            Object(expense, parent),
            AuditOperation.Updated,
            [.. changes.Select(change => Change(change.Field, Format(change.OldValue), Format(change.NewValue)))]);
    }

    /// <summary>Usunięcie (fizyczne): wszystkie niepuste pola jako stare wartości z nową <c>null</c> — ślad usunięcia.</summary>
    public static AuditEntry Deleted(Expense expense, AuditAuthor author, AuditObjectReference? parent = null)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentNullException.ThrowIfNull(author);

        var changes = expense.Snapshot()
            .Select(field => (field.Field, Value: Format(field.Value)))
            .Where(field => field.Value is not null)
            .Select(field => Change(field.Field, field.Value, null))
            .ToList();

        return new AuditEntry(author, Object(expense, parent), AuditOperation.Deleted, changes);
    }

    /// <summary>Rodzic wpisu: definicja cykliczna dla wystąpienia (także po usunięciu definicji), dla pozostałych brak.</summary>
    public static AuditObjectReference? ParentOf(Expense expense)
    {
        ArgumentNullException.ThrowIfNull(expense);
        return expense.RecurringDefinitionId is { } definitionId
            ? new AuditObjectReference(AuditObjectType.RecurringExpenseDefinition, definitionId)
            : null;
    }

    /// <summary>Etykieta obiektu: „Wydatek – {etykieta kategorii}”.</summary>
    public static string Label(Expense expense)
    {
        ArgumentNullException.ThrowIfNull(expense);
        return $"Wydatek – {ExpenseCodes.LabelOf(expense.Category)}";
    }

    /// <summary>Wartość pola jako tekst: kwota <c>-1234.50</c>, data <c>yyyy-MM-dd</c>, słownik jako kod; brak wartości to <c>null</c>.</summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal amount => ExpenseInput.FormatAmount(amount),
        ExpenseCategory category => ExpenseCodes.Of(category),
        ExpensePaymentMethod method => ExpenseCodes.Of(method),
        string text => text,
        _ => throw new ArgumentException($"Nieobsługiwany typ wartości: {value.GetType().Name}.", nameof(value)),
    };

    public static bool IsPersonalData(ExpenseField field) => field == ExpenseField.Payer;

    private static AuditChange Change(ExpenseField field, string? oldValue, string? newValue) =>
        new(ExpenseCodes.Of(field), oldValue, newValue, IsPersonalData(field));

    private static AuditObject Object(Expense expense, AuditObjectReference? parent) =>
        new(AuditObjectType.Expense, expense.Id, expense.CostDate, Label(expense), parent);
}
