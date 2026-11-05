using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Application.Payments;

/// <summary>
/// Dane korekty sprzedaży z formularza (tekst jak w JSON): kwota, data i powód, bez metody.
/// Walidacja jak w <see cref="RefundInput"/> bez rodzaju i metody.
/// </summary>
public sealed record SalesCorrectionInput(string? Amount, string? Date, string? Reason)
{
    /// <param name="errors">Błędy pól pod kluczami JSON.</param>
    /// <param name="now">Chwila żądania; „dzisiaj” według Europe/Warsaw.</param>
    public SalesCorrectionDetails? Validate(FieldErrors errors, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.IsEmpty;

        var amount = EntryInput.ValidateAmount(Amount, errors);
        var date = EntryInput.ValidateDate(Date, now, errors);
        var reason = EntryInput.ValidateReason(
            Reason, SalesCorrection.ReasonMaxLength, EntryMessages.CorrectionReasonRequired, EntryMessages.CorrectionReasonTooLong, errors);

        return before && errors.IsEmpty && amount is { } a && date is { } d && reason is not null
            ? new SalesCorrectionDetails(a, d, reason)
            : null;
    }

    /// <summary>SHA-256 znormalizowanego dodania korekty razem z identyfikatorem zamówienia (jak w SF-9, N1).</summary>
    public static byte[] ComputeRequestHash(Guid orderId, SalesCorrectionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return EntryInput.Hash(new HashModel(orderId, OrderInput.FormatValue(details.Amount), EntryInput.Date(details.Date), details.Reason));
    }

    private sealed record HashModel(Guid OrderId, string Amount, string Date, string Reason);
}
