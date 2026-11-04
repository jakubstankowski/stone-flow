using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace StoneFlow.Access.Api;

/// <summary>
/// Odpowiedzi błędów modułu: stały tekst dla użytkownika w <c>detail</c> i kod w rozszerzeniu
/// <c>code</c>, po którym frontend rozróżnia przypadki o tym samym statusie.
/// </summary>
public static class AuthProblems
{
    public const string InvalidCredentialsCode = "invalid_credentials";
    public const string CsrfInvalidCode = "csrf_invalid";
    public const string ResetLinkInvalidCode = "reset_link_invalid";
    public const string RateLimitedCode = "rate_limited";

    public const string InvalidCredentialsDetail =
        "Nieprawidłowy e-mail lub hasło. Po 5 nieudanych próbach logowanie jest blokowane na 15 minut.";

    public const string CsrfInvalidDetail = "Odśwież stronę i spróbuj ponownie.";
    public const string ResetLinkInvalidDetail = "Link wygasł lub został już użyty. Poproś o nowy link.";
    public const string RateLimitedDetail = "Zbyt wiele prób. Spróbuj ponownie za chwilę.";

    public static ProblemHttpResult InvalidCredentials() =>
        Problem(StatusCodes.Status401Unauthorized, InvalidCredentialsDetail, InvalidCredentialsCode);

    public static ProblemHttpResult CsrfInvalid() =>
        Problem(StatusCodes.Status400BadRequest, CsrfInvalidDetail, CsrfInvalidCode);

    public static ProblemHttpResult ResetLinkInvalid() =>
        Problem(StatusCodes.Status400BadRequest, ResetLinkInvalidDetail, ResetLinkInvalidCode);

    public static ProblemHttpResult TooManyRequests() =>
        Problem(StatusCodes.Status429TooManyRequests, RateLimitedDetail, RateLimitedCode);

    private static ProblemHttpResult Problem(int status, string detail, string code) =>
        TypedResults.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
