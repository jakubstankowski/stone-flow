using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace StoneFlow.Reporting.Api;

/// <summary>Odpowiedzi błędów modułu Reporting: tekst w <c>detail</c> i stały kod w rozszerzeniu <c>code</c>.</summary>
public static class ReportingProblems
{
    public const string RangeTooLargeCode = "operations_range_too_large";

    public const string RangeTooLargeDetail = "Za dużo operacji w wybranym okresie. Wybierz krótszy okres.";

    public static ProblemHttpResult RangeTooLarge() =>
        TypedResults.Problem(
            detail: RangeTooLargeDetail,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?> { ["code"] = RangeTooLargeCode });

    /// <summary>
    /// Raport sprzedaży (SF-19): ponad 10 000 składników sprzedaży albo wydatków w okresie; raport
    /// przepływów (SF-20): ponad 10 000 operacji pieniężnych w okresie albo w okresie bazowym.
    /// </summary>
    public const string ReportRangeTooLargeCode = "report_range_too_large";

    public const string ReportRangeTooLargeDetail = "Za dużo danych w wybranym okresie. Wybierz krótszy okres.";

    public static ProblemHttpResult ReportRangeTooLarge() =>
        TypedResults.Problem(
            detail: ReportRangeTooLargeDetail,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?> { ["code"] = ReportRangeTooLargeCode });
}
