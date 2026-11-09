using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace StoneFlow.Expenses.Api.Endpoints;

/// <summary>Odczyt tożsamości i nagłówka idempotencji wspólny dla endpointów wydatków i wypłat.</summary>
internal static class EndpointSupport
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Klucz idempotencji: jeden UUID z nagłówka; brak, pusty, wielokrotny albo inny tekst to <c>null</c>.</summary>
    public static Guid? IdempotencyKey(HttpRequest request) =>
        request.Headers[IdempotencyKeyHeader] is { Count: 1 } values
        && Guid.TryParse(values[0], out var key)
        && key != Guid.Empty
            ? key
            : null;

    /// <summary>Konto zalogowanego właściciela z <see cref="ClaimTypes.NameIdentifier"/>.</summary>
    public static Guid? CurrentAccount(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
