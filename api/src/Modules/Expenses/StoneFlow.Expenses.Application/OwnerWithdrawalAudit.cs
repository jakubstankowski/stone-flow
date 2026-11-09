using StoneFlow.Expenses.Domain.OwnerWithdrawals;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Expenses.Application;

/// <summary>
/// Wpisy audytu wypłaty właściciela (SF-7, konwencje „Value format”): obiekt
/// <c>OwnerWithdrawal</c>, data biznesowa to data wypłaty, bez rodzica, etykieta „Wypłata
/// właściciela” (bez osoby). Pole <c>person</c> to dane osobowe.
/// </summary>
public static class OwnerWithdrawalAudit
{
    public const string Label = "Wypłata właściciela";

    /// <summary>Utworzenie: wszystkie pola jako nowe wartości.</summary>
    public static AuditEntry Created(OwnerWithdrawal withdrawal, AuditAuthor author)
    {
        ArgumentNullException.ThrowIfNull(withdrawal);
        ArgumentNullException.ThrowIfNull(author);

        return new AuditEntry(
            author,
            Object(withdrawal),
            AuditOperation.Created,
            [.. withdrawal.Snapshot().Select(field => Change(field.Field, null, Format(field.Value)))]);
    }

    /// <summary>Zmiana: tylko zmienione pola; data biznesowa to nowa data wypłaty.</summary>
    public static AuditEntry Updated(OwnerWithdrawal withdrawal, IReadOnlyList<OwnerWithdrawalFieldChange> changes, AuditAuthor author)
    {
        ArgumentNullException.ThrowIfNull(withdrawal);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(author);

        return new AuditEntry(
            author,
            Object(withdrawal),
            AuditOperation.Updated,
            [.. changes.Select(change => Change(change.Field, Format(change.OldValue), Format(change.NewValue)))]);
    }

    /// <summary>Usunięcie (fizyczne): wszystkie pola jako stare wartości z nową <c>null</c> — ślad usunięcia.</summary>
    public static AuditEntry Deleted(OwnerWithdrawal withdrawal, AuditAuthor author)
    {
        ArgumentNullException.ThrowIfNull(withdrawal);
        ArgumentNullException.ThrowIfNull(author);

        return new AuditEntry(
            author,
            Object(withdrawal),
            AuditOperation.Deleted,
            [.. withdrawal.Snapshot().Select(field => Change(field.Field, Format(field.Value), null))]);
    }

    /// <summary>Wartość pola jako tekst: kwota <c>2000.00</c>, data <c>yyyy-MM-dd</c>, metoda jako kod.</summary>
    public static string Format(object value) => value switch
    {
        DateOnly date => ExpenseInput.FormatDate(date),
        decimal amount => ExpenseInput.FormatAmount(amount),
        OwnerWithdrawalMethod method => OwnerWithdrawalCodes.Of(method),
        string text => text,
        _ => throw new ArgumentException($"Nieobsługiwany typ wartości: {value.GetType().Name}.", nameof(value)),
    };

    public static bool IsPersonalData(OwnerWithdrawalField field) => field == OwnerWithdrawalField.Person;

    private static AuditChange Change(OwnerWithdrawalField field, string? oldValue, string? newValue) =>
        new(OwnerWithdrawalCodes.Of(field), oldValue, newValue, IsPersonalData(field));

    private static AuditObject Object(OwnerWithdrawal withdrawal) =>
        new(AuditObjectType.OwnerWithdrawal, withdrawal.Id, withdrawal.WithdrawnOn, Label);
}
