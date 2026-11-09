using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace StoneFlow.Expenses.Api;

/// <summary>
/// Odpowiedzi błędów wypłat właściciela: tekst dla użytkownika w <c>detail</c> i stały kod w
/// rozszerzeniu <c>code</c>. Kody idempotencji są te same co przy wydatkach.
/// </summary>
public static class OwnerWithdrawalProblems
{
    public const string NotFoundCode = "owner_withdrawal_not_found";
    public const string ConflictCode = "owner_withdrawal_conflict";
    public const string IdempotencyKeyRequiredCode = ExpenseProblems.IdempotencyKeyRequiredCode;
    public const string IdempotencyKeyReusedCode = ExpenseProblems.IdempotencyKeyReusedCode;

    public const string NotFoundDetail = "Nie znaleziono wypłaty. Mogła zostać usunięta.";
    public const string ConflictDetail = "Wypłata została zmieniona w międzyczasie. Wczytaj aktualne dane.";
    public const string IdempotencyKeyRequiredDetail = ExpenseProblems.IdempotencyKeyRequiredDetail;
    public const string IdempotencyKeyReusedDetail = "Ten formularz zapisał już wypłatę. Odśwież formularz, aby dodać kolejną.";

    public static ProblemHttpResult NotFound() => Problem(StatusCodes.Status404NotFound, NotFoundDetail, NotFoundCode);

    public static ProblemHttpResult Conflict() => Problem(StatusCodes.Status409Conflict, ConflictDetail, ConflictCode);

    public static ProblemHttpResult IdempotencyKeyRequired() =>
        Problem(StatusCodes.Status400BadRequest, IdempotencyKeyRequiredDetail, IdempotencyKeyRequiredCode);

    public static ProblemHttpResult IdempotencyKeyReused(Guid ownerWithdrawalId) =>
        Problem(
            StatusCodes.Status409Conflict,
            IdempotencyKeyReusedDetail,
            IdempotencyKeyReusedCode,
            new Dictionary<string, object?> { ["ownerWithdrawalId"] = ownerWithdrawalId });

    private static ProblemHttpResult Problem(int status, string detail, string code, Dictionary<string, object?>? extensions = null)
    {
        extensions ??= [];
        extensions["code"] = code;
        return TypedResults.Problem(detail: detail, statusCode: status, extensions: extensions);
    }
}
