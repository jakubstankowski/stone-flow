using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Application.Payments;

/// <summary>
/// Dane zwrotu z formularza (tekst jak w JSON). Rodzaj jest wymagany i nie ma wartości domyślnej;
/// powód jest wymagany (po przycięciu od 1 do 500 znaków).
/// </summary>
public sealed record RefundInput(string? Kind, string? Amount, string? Date, string? Method, string? Reason)
{
    /// <param name="errors">Błędy pól pod kluczami JSON.</param>
    /// <param name="now">Chwila żądania; „dzisiaj” według Europe/Warsaw.</param>
    public RefundDetails? Validate(FieldErrors errors, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.IsEmpty;

        RefundKind? kind = null;
        if (OrderInput.Text(Kind) is not { } kindCode)
        {
            errors.Add(EntryFieldKeys.Kind, EntryMessages.KindRequired);
        }
        else if (OrderCodes.TryParse(kindCode, out RefundKind parsedKind))
        {
            kind = parsedKind;
        }
        else
        {
            errors.Add(EntryFieldKeys.Kind, EntryMessages.KindUnknown);
        }

        var amount = EntryInput.ValidateAmount(Amount, errors);
        var date = EntryInput.ValidateDate(Date, now, errors);

        RefundMethod? method = null;
        if (OrderInput.Text(Method) is not { } methodCode)
        {
            errors.Add(EntryFieldKeys.Method, EntryMessages.RefundMethodRequired);
        }
        else if (OrderCodes.TryParse(methodCode, out RefundMethod parsedMethod))
        {
            method = parsedMethod;
        }
        else
        {
            errors.Add(EntryFieldKeys.Method, EntryMessages.RefundMethodUnknown);
        }

        var reason = EntryInput.ValidateReason(Reason, Refund.ReasonMaxLength, EntryMessages.ReasonRequired, EntryMessages.ReasonTooLong, errors);

        return before && errors.IsEmpty && kind is { } k && amount is { } a && date is { } d && method is { } m && reason is not null
            ? new RefundDetails(k, a, d, m, reason)
            : null;
    }

    /// <summary>SHA-256 znormalizowanego dodania zwrotu razem z identyfikatorem zamówienia (N1).</summary>
    public static byte[] ComputeRequestHash(Guid orderId, RefundDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return EntryInput.Hash(new HashModel(
            orderId,
            OrderCodes.Of(details.Kind),
            OrderInput.FormatValue(details.Amount),
            EntryInput.Date(details.Date),
            OrderCodes.Of(details.Method),
            details.Reason));
    }

    private sealed record HashModel(Guid OrderId, string Kind, string Amount, string Date, string Method, string Reason);
}
