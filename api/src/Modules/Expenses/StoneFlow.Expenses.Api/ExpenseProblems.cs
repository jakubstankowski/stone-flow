using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace StoneFlow.Expenses.Api;

/// <summary>
/// Odpowiedzi błędów modułu Expenses: tekst dla użytkownika w <c>detail</c> i stały kod w
/// rozszerzeniu <c>code</c>, po którym frontend rozróżnia przypadki o tym samym statusie.
/// </summary>
public static class ExpenseProblems
{
    public const string NotFoundCode = "expense_not_found";
    public const string ConflictCode = "expense_conflict";
    public const string IdempotencyKeyRequiredCode = "idempotency_key_required";
    public const string IdempotencyKeyReusedCode = "idempotency_key_reused";
    public const string AlreadyPaidCode = "expense_already_paid";
    public const string NotPaidCode = "expense_not_paid";

    public const string NotFoundDetail = "Nie znaleziono wydatku. Mógł zostać usunięty.";
    public const string ConflictDetail = "Wydatek został zmieniony w międzyczasie. Wczytaj aktualne dane.";
    public const string IdempotencyKeyRequiredDetail = "Brak poprawnego klucza żądania. Odśwież formularz i spróbuj ponownie.";
    public const string IdempotencyKeyReusedDetail = "Ten formularz zapisał już wydatek. Odśwież formularz, aby dodać kolejny.";
    public const string AlreadyPaidDetail = "Wydatek jest już oznaczony jako opłacony.";
    public const string NotPaidDetail = "Wydatek nie jest oznaczony jako opłacony.";

    public static ProblemHttpResult NotFound() => Problem(StatusCodes.Status404NotFound, NotFoundDetail, NotFoundCode);

    public static ProblemHttpResult Conflict() => Problem(StatusCodes.Status409Conflict, ConflictDetail, ConflictCode);

    public static ProblemHttpResult IdempotencyKeyRequired() =>
        Problem(StatusCodes.Status400BadRequest, IdempotencyKeyRequiredDetail, IdempotencyKeyRequiredCode);

    public static ProblemHttpResult IdempotencyKeyReused(Guid expenseId) =>
        Problem(
            StatusCodes.Status409Conflict,
            IdempotencyKeyReusedDetail,
            IdempotencyKeyReusedCode,
            new Dictionary<string, object?> { ["expenseId"] = expenseId });

    public static ProblemHttpResult AlreadyPaid() => Problem(StatusCodes.Status409Conflict, AlreadyPaidDetail, AlreadyPaidCode);

    public static ProblemHttpResult NotPaid() => Problem(StatusCodes.Status409Conflict, NotPaidDetail, NotPaidCode);

    private static ProblemHttpResult Problem(int status, string detail, string code, Dictionary<string, object?>? extensions = null)
    {
        extensions ??= [];
        extensions["code"] = code;
        return TypedResults.Problem(detail: detail, statusCode: status, extensions: extensions);
    }
}
