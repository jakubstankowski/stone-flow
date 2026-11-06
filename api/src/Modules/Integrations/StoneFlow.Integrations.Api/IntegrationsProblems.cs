using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace StoneFlow.Integrations.Api;

/// <summary>
/// Odpowiedzi błędów modułu Integrations: tekst dla użytkownika w <c>detail</c> i stały kod w
/// rozszerzeniu <c>code</c>. Bez szczegółów odpowiedzi sklepu.
/// </summary>
public static class IntegrationsProblems
{
    public const string NotConfiguredCode = "integration_not_configured";
    public const string SyncInProgressCode = "sync_in_progress";
    public const string ErrorNotFoundCode = "error_not_found";
    public const string ErrorClosedCode = "error_closed";
    public const string RetryFailedCode = "retry_failed";
    public const string RetryUnavailableCode = "retry_unavailable";

    public const string NotConfiguredDetail = "Integracja ze sklepem nie ma skonfigurowanych kluczy REST.";
    public const string SyncInProgressDetail = "Synchronizacja już trwa. Poczekaj na jej zakończenie.";
    public const string ErrorNotFoundDetail = "Nie znaleziono błędu synchronizacji.";
    public const string ErrorClosedDetail = "Ten błąd jest już zamknięty.";
    public const string RetryFailedDetail = "Sklep nie zwrócił zamówienia. Błąd pozostaje otwarty; spróbuj ponownie później.";
    public const string RetryUnavailableDetail = "Nie udało się zapisać zamówienia. Błąd pozostaje otwarty; spróbuj ponownie później.";

    public static ProblemHttpResult NotConfigured() => Problem(StatusCodes.Status409Conflict, NotConfiguredDetail, NotConfiguredCode);

    public static ProblemHttpResult SyncInProgress() => Problem(StatusCodes.Status409Conflict, SyncInProgressDetail, SyncInProgressCode);

    public static ProblemHttpResult ErrorNotFound() => Problem(StatusCodes.Status404NotFound, ErrorNotFoundDetail, ErrorNotFoundCode);

    public static ProblemHttpResult ErrorClosed() => Problem(StatusCodes.Status409Conflict, ErrorClosedDetail, ErrorClosedCode);

    public static ProblemHttpResult RetryFailed() => Problem(StatusCodes.Status502BadGateway, RetryFailedDetail, RetryFailedCode);

    public static ProblemHttpResult RetryUnavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, RetryUnavailableDetail, RetryUnavailableCode);

    private static ProblemHttpResult Problem(int status, string detail, string code) =>
        TypedResults.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
