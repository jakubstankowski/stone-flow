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

    public const string RecurringNotFoundCode = "recurring_expense_not_found";
    public const string RecurringConflictCode = "recurring_expense_conflict";
    public const string RecurringAlreadyPausedCode = "recurring_expense_already_paused";
    public const string RecurringNotPausedCode = "recurring_expense_not_paused";
    public const string RecurringEndedCode = "recurring_expense_ended";
    public const string RecurringDeletedCode = "recurring_expense_deleted";
    public const string RecurringPausedCode = "recurring_expense_paused";
    public const string RecurringBusyCode = "recurring_expense_busy";

    public const string RecurringNotFoundDetail = "Nie znaleziono wydatku cyklicznego. Mógł zostać usunięty.";
    public const string RecurringConflictDetail = "Wydatek cykliczny został zmieniony w międzyczasie. Wczytaj aktualne dane.";
    public const string RecurringAlreadyPausedDetail = "Wydatek cykliczny jest już wstrzymany.";
    public const string RecurringNotPausedDetail = "Wydatek cykliczny nie jest wstrzymany.";
    public const string RecurringEndedDetail = "Wydatek cykliczny jest zakończony, więc nie można go wstrzymać.";
    public const string RecurringDeletedDetail =
        "Ten formularz zapisał już wydatek cykliczny, który został później usunięty. Odśwież formularz, aby dodać nowy.";
    public const string RecurringPausedDetail = "Wznów wydatek cykliczny, aby zmienić dzień miesiąca.";
    public const string RecurringBusyDetail = "Nie udało się zapisać z powodu równoczesnej operacji. Spróbuj ponownie.";
    public const string RecurringIdempotencyKeyReusedDetail = "Ten formularz zapisał już wydatek cykliczny. Odśwież formularz, aby dodać kolejny.";

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

    public static ProblemHttpResult RecurringNotFound() => Problem(StatusCodes.Status404NotFound, RecurringNotFoundDetail, RecurringNotFoundCode);

    public static ProblemHttpResult RecurringConflict() => Problem(StatusCodes.Status409Conflict, RecurringConflictDetail, RecurringConflictCode);

    public static ProblemHttpResult RecurringAlreadyPaused() =>
        Problem(StatusCodes.Status409Conflict, RecurringAlreadyPausedDetail, RecurringAlreadyPausedCode);

    public static ProblemHttpResult RecurringNotPaused() => Problem(StatusCodes.Status409Conflict, RecurringNotPausedDetail, RecurringNotPausedCode);

    public static ProblemHttpResult RecurringEnded() => Problem(StatusCodes.Status409Conflict, RecurringEndedDetail, RecurringEndedCode);

    public static ProblemHttpResult RecurringDeleted() => Problem(StatusCodes.Status409Conflict, RecurringDeletedDetail, RecurringDeletedCode);

    public static ProblemHttpResult RecurringPaused() => Problem(StatusCodes.Status409Conflict, RecurringPausedDetail, RecurringPausedCode);

    public static ProblemHttpResult RecurringBusy() => Problem(StatusCodes.Status503ServiceUnavailable, RecurringBusyDetail, RecurringBusyCode);

    public static ProblemHttpResult RecurringIdempotencyKeyReused(Guid recurringExpenseId) =>
        Problem(
            StatusCodes.Status409Conflict,
            RecurringIdempotencyKeyReusedDetail,
            IdempotencyKeyReusedCode,
            new Dictionary<string, object?> { ["recurringExpenseId"] = recurringExpenseId });

    private static ProblemHttpResult Problem(int status, string detail, string code, Dictionary<string, object?>? extensions = null)
    {
        extensions ??= [];
        extensions["code"] = code;
        return TypedResults.Problem(detail: detail, statusCode: status, extensions: extensions);
    }
}
