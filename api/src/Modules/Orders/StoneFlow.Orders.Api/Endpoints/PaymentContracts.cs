using StoneFlow.Orders.Application.Payments;

namespace StoneFlow.Orders.Api.Endpoints;

// Kontrakt HTTP wpisów jak w OrderContracts: kwoty jako tekst dziesiętny z kropką ("1234.50"), daty
// yyyy-MM-dd, słowniki jako kody camelCase, wersja wpisu jako base64 z rowversion.

/// <summary>Ciało <c>POST</c> i <c>PUT</c> wpłaty; <see cref="Version"/> tylko w <c>PUT</c>.</summary>
public sealed record PaymentRequest(string? Amount, string? Date, string? Method, string? ReceivedBy, string? Version = null);

/// <summary>Ciało <c>POST</c> i <c>PUT</c> zwrotu; <see cref="Version"/> tylko w <c>PUT</c>.</summary>
public sealed record RefundRequest(string? Kind, string? Amount, string? Date, string? Method, string? Reason, string? Version = null);

/// <summary>Odpowiedź dodania wpisu (201) i jego powtórzenia (200).</summary>
public sealed record EntryCreatedResponse(Guid Id, string Version);

internal static class PaymentContracts
{
    public static PaymentInput ToInput(PaymentRequest? request) =>
        request is null ? new PaymentInput(null, null, null, null) : new PaymentInput(request.Amount, request.Date, request.Method, request.ReceivedBy);

    public static RefundInput ToInput(RefundRequest? request) =>
        request is null
            ? new RefundInput(null, null, null, null, null)
            : new RefundInput(request.Kind, request.Amount, request.Date, request.Method, request.Reason);
}
