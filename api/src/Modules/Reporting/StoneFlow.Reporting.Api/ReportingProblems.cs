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
}
