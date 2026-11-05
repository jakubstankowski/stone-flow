using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Payments;

namespace StoneFlow.Orders.Application.Payments;

/// <summary>
/// Dane wpłaty z formularza (tekst jak w JSON). Normalizacja: teksty przycięte, pusty tekst to
/// <c>null</c>. „Przyjął” tylko przy gotówce: przy innej metodzie niepusta wartość to błąd.
/// </summary>
public sealed record PaymentInput(string? Amount, string? Date, string? Method, string? ReceivedBy)
{
    /// <param name="errors">Błędy pól pod kluczami JSON.</param>
    /// <param name="now">Chwila żądania; „dzisiaj” według Europe/Warsaw.</param>
    public PaymentDetails? Validate(FieldErrors errors, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.IsEmpty;

        var amount = EntryInput.ValidateAmount(Amount, errors);
        var date = EntryInput.ValidateDate(Date, now, errors);

        PaymentMethod? method = null;
        if (OrderInput.Text(Method) is not { } methodCode)
        {
            errors.Add(EntryFieldKeys.Method, EntryMessages.PaymentMethodRequired);
        }
        else if (OrderCodes.TryParse(methodCode, out PaymentMethod parsed))
        {
            method = parsed;
        }
        else
        {
            errors.Add(EntryFieldKeys.Method, EntryMessages.PaymentMethodUnknown);
        }

        var receivedBy = OrderInput.Text(ReceivedBy);
        if (receivedBy is not null)
        {
            if (method is { } known && known != PaymentMethod.Cash)
            {
                errors.Add(EntryFieldKeys.ReceivedBy, EntryMessages.ReceivedByOnlyForCash);
            }
            else if (receivedBy.Length > Payment.ReceivedByMaxLength)
            {
                errors.Add(EntryFieldKeys.ReceivedBy, EntryMessages.ReceivedByTooLong);
            }
        }

        return before && errors.IsEmpty && amount is { } a && date is { } d && method is { } m
            ? new PaymentDetails(a, d, m, receivedBy)
            : null;
    }

    /// <summary>
    /// SHA-256 znormalizowanego dodania wpłaty razem z identyfikatorem zamówienia: ten sam klucz
    /// użyty przy innym zamówieniu nie jest powtórzeniem (N1).
    /// </summary>
    public static byte[] ComputeRequestHash(Guid orderId, PaymentDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return EntryInput.Hash(new HashModel(
            orderId,
            OrderInput.FormatValue(details.Amount),
            EntryInput.Date(details.Date),
            OrderCodes.Of(details.Method),
            details.ReceivedBy));
    }

    private sealed record HashModel(Guid OrderId, string Amount, string Date, string Method, string? ReceivedBy);
}
