using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace StoneFlow.Orders.Api;

/// <summary>
/// Odpowiedzi błędów modułu Orders: tekst dla użytkownika w <c>detail</c> i stały kod w
/// rozszerzeniu <c>code</c>, po którym frontend rozróżnia przypadki o tym samym statusie.
/// </summary>
public static class OrderProblems
{
    public const string NotFoundCode = "order_not_found";
    public const string ConflictCode = "order_conflict";
    public const string IdempotencyKeyRequiredCode = "idempotency_key_required";
    public const string IdempotencyKeyReusedCode = "idempotency_key_reused";
    public const string FieldOwnedByShopCode = "field_owned_by_shop";
    public const string StatusChangeRequiresActionCode = "status_change_requires_action";
    public const string AlreadyCancelledCode = "order_already_cancelled";
    public const string NotCancelledCode = "order_not_cancelled";
    public const string EntryNotFoundCode = "entry_not_found";
    public const string EntryConflictCode = "entry_conflict";
    public const string PaidWouldBeNegativeCode = "paid_would_be_negative";
    public const string RefundExceedsPaidCode = "refund_exceeds_paid";

    public const string NotFoundDetail = "Nie znaleziono zamówienia.";
    public const string ConflictDetail = "Zamówienie zostało zmienione w międzyczasie. Wczytaj aktualne dane.";
    public const string IdempotencyKeyRequiredDetail = "Brak poprawnego klucza żądania. Odśwież formularz i spróbuj ponownie.";
    public const string FieldOwnedByShopDetail = "Tych danych nie można zmienić, bo należą do zamówienia ze sklepu.";
    public const string StatusChangeRequiresActionDetail =
        "Status „Anulowane” zmieniają tylko akcje „Anuluj zamówienie” i „Przywróć zamówienie”.";
    public const string AlreadyCancelledDetail = "Zamówienie jest już anulowane.";
    public const string NotCancelledDetail = "Zamówienie nie jest anulowane.";
    public const string EntryNotFoundDetail = "Nie znaleziono wpisu. Mógł zostać usunięty.";
    public const string EntryConflictDetail = "Wpis został zmieniony w międzyczasie. Wczytaj aktualne dane.";
    public const string PaidWouldBeNegativeDetail = "Zapłacono nie może spaść poniżej 0 zł. Najpierw popraw lub usuń zwrot.";

    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    public static ProblemHttpResult NotFound() => Problem(StatusCodes.Status404NotFound, NotFoundDetail, NotFoundCode);

    public static ProblemHttpResult Conflict() => Problem(StatusCodes.Status409Conflict, ConflictDetail, ConflictCode);

    public static ProblemHttpResult IdempotencyKeyRequired() =>
        Problem(StatusCodes.Status400BadRequest, IdempotencyKeyRequiredDetail, IdempotencyKeyRequiredCode);

    /// <summary>Ten sam klucz z innymi danymi; numer zamówienia nie jest daną osobową.</summary>
    public static ProblemHttpResult IdempotencyKeyReused(Guid orderId, string orderNumber) =>
        Problem(
            StatusCodes.Status409Conflict,
            $"Ten formularz zapisał już zamówienie {orderNumber}.",
            IdempotencyKeyReusedCode,
            new Dictionary<string, object?> { ["orderId"] = orderId, ["orderNumber"] = orderNumber });

    public static ProblemHttpResult FieldOwnedByShop(IReadOnlyList<string> fields) =>
        Problem(
            StatusCodes.Status422UnprocessableEntity,
            FieldOwnedByShopDetail,
            FieldOwnedByShopCode,
            new Dictionary<string, object?> { ["fields"] = fields });

    public static ProblemHttpResult StatusChangeRequiresAction() =>
        Problem(StatusCodes.Status422UnprocessableEntity, StatusChangeRequiresActionDetail, StatusChangeRequiresActionCode);

    public static ProblemHttpResult AlreadyCancelled() => Problem(StatusCodes.Status409Conflict, AlreadyCancelledDetail, AlreadyCancelledCode);

    public static ProblemHttpResult NotCancelled() => Problem(StatusCodes.Status409Conflict, NotCancelledDetail, NotCancelledCode);

    public static ProblemHttpResult EntryNotFound() => Problem(StatusCodes.Status404NotFound, EntryNotFoundDetail, EntryNotFoundCode);

    public static ProblemHttpResult EntryConflict() => Problem(StatusCodes.Status409Conflict, EntryConflictDetail, EntryConflictCode);

    /// <summary>Ten sam klucz z innymi danymi albo przy innym zamówieniu; <paramref name="idProperty"/> to <c>paymentId</c> albo <c>refundId</c>.</summary>
    public static ProblemHttpResult EntryIdempotencyKeyReused(string idProperty, Guid entryId, string entryName) =>
        Problem(
            StatusCodes.Status409Conflict,
            $"Ten formularz zapisał już {entryName}. Odśwież formularz, aby dodać kolejny wpis.",
            IdempotencyKeyReusedCode,
            new Dictionary<string, object?> { [idProperty] = entryId });

    public static ProblemHttpResult PaidWouldBeNegative() =>
        Problem(StatusCodes.Status422UnprocessableEntity, PaidWouldBeNegativeDetail, PaidWouldBeNegativeCode);

    /// <summary>Zwrot ponad Zapłacono; <c>available</c> to kwota, do której zwrot może sięgnąć (<c>1000.00</c>).</summary>
    public static ProblemHttpResult RefundExceedsPaid(decimal available, string availableText) =>
        Problem(
            StatusCodes.Status422UnprocessableEntity,
            $"Zwrot nie może przekroczyć kwoty Zapłacono ({available.ToString("N2", Polish)} zł).",
            RefundExceedsPaidCode,
            new Dictionary<string, object?> { ["available"] = availableText });

    private static ProblemHttpResult Problem(int status, string detail, string code, Dictionary<string, object?>? extensions = null)
    {
        extensions ??= [];
        extensions["code"] = code;
        return TypedResults.Problem(detail: detail, statusCode: status, extensions: extensions);
    }
}
