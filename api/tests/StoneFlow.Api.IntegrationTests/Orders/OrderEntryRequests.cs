using System.Net;
using System.Text.Json;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests.Orders;

/// <summary>Wspólne żądania i asercje testów wpisów zamówienia (wpłaty, zwroty, korekty sprzedaży).</summary>
internal static class OrderEntryRequests
{
    /// <summary>„Dzisiaj” według zegara testu (<see cref="OrdersTest.DefaultNow"/>).</summary>
    internal const string Today = "2026-11-05";

    /// <summary>Akcja dekoratora: czeka, aż dwa żądania dojdą do zapisu, najwyżej 2 s.</summary>
    internal static Func<Task> Barrier()
    {
        var arrived = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return async () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                both.TrySetResult();
            }

            await Task.WhenAny(both.Task, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        };
    }

    internal static object Order(string value, string? comment = null, string? version = null, string orderDate = "2026-10-28", string channel = "olx") => new
    {
        orderDate,
        channel,
        value,
        fulfillmentStatus = version is null ? null : "inProgress",
        customer = new { kind = "person", name = "Jan Testowy" },
        operational = new { suppliers = Array.Empty<string>(), zoReferences = Array.Empty<string>(), hasPhotos = false, hasReview = false },
        comment,
        version,
    };

    internal static object Payment(string amount, string date = Today, string method = "transfer", string? receivedBy = null, string? version = null) =>
        new { amount, date, method, receivedBy, version };

    internal static object Refund(
        string amount,
        string kind = "moneyReturn",
        string date = Today,
        string method = "transfer",
        string reason = "Zwrot pieniędzy testowy",
        string? version = null) =>
        new { kind, amount, date, method, reason, version };

    internal static Task<HttpResponseMessage> SendPaymentAsync(OrdersTest test, Guid orderId, object body, Guid? key = null) =>
        test.SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/payments", body, (key ?? Guid.NewGuid()).ToString());

    internal static Task<HttpResponseMessage> SendRefundAsync(OrdersTest test, Guid orderId, object body, Guid? key = null) =>
        test.SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/refunds", body, (key ?? Guid.NewGuid()).ToString());

    internal static async Task<CreatedEntry> AddPaymentAsync(
        OrdersTest test, Guid orderId, string amount, string date = Today, string method = "transfer", string? receivedBy = null, Guid? key = null)
    {
        using var response = await SendPaymentAsync(test, orderId, Payment(amount, date, method, receivedBy), key);
        return await CreatedAsync(response);
    }

    internal static async Task<CreatedEntry> AddRefundAsync(
        OrdersTest test, Guid orderId, string amount, string kind = "moneyReturn", string method = "transfer", string reason = "Zwrot pieniędzy testowy")
    {
        using var response = await SendRefundAsync(test, orderId, Refund(amount, kind, Today, method, reason));
        return await CreatedAsync(response);
    }

    internal static async Task<string> EditPaymentAsync(
        OrdersTest test, Guid orderId, Guid entryId, string version, string amount, string date = Today, string method = "transfer", string? receivedBy = null)
    {
        using var response = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{orderId}/payments/{entryId}", Payment(amount, date, method, receivedBy, version));
        return await VersionAsync(response);
    }

    internal static async Task<string> EditRefundAsync(OrdersTest test, Guid orderId, Guid entryId, string version, string amount, string kind = "moneyReturn")
    {
        using var response = await test.SendAsync(HttpMethod.Put, $"/api/orders/{orderId}/refunds/{entryId}", Refund(amount, kind, version: version));
        return await VersionAsync(response);
    }

    internal static async Task DeletePaymentAsync(OrdersTest test, Guid orderId, Guid entryId, string version)
    {
        using var response = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}/payments/{entryId}", new { version });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    internal static object Correction(string amount, string date = Today, string reason = "Oddany kamień testowy", string? version = null) =>
        new { amount, date, reason, version };

    internal static Task<HttpResponseMessage> SendCorrectionAsync(OrdersTest test, Guid orderId, object body, Guid? key = null) =>
        test.SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/sales-corrections", body, (key ?? Guid.NewGuid()).ToString());

    internal static async Task<CreatedEntry> AddCorrectionAsync(OrdersTest test, Guid orderId, string amount, string date = Today, Guid? key = null)
    {
        using var response = await SendCorrectionAsync(test, orderId, Correction(amount, date), key);
        return await CreatedAsync(response);
    }

    internal static Task<HttpResponseMessage> SendCorrectionEditAsync(OrdersTest test, Guid orderId, Guid entryId, object body) =>
        test.SendAsync(HttpMethod.Put, $"/api/orders/{orderId}/sales-corrections/{entryId}", body);

    internal static async Task<string> EditCorrectionAsync(
        OrdersTest test, Guid orderId, Guid entryId, string version, string amount, string date = Today, string reason = "Oddany kamień testowy")
    {
        using var response = await SendCorrectionEditAsync(test, orderId, entryId, Correction(amount, date, reason, version));
        return await VersionAsync(response);
    }

    internal static async Task DeleteCorrectionAsync(OrdersTest test, Guid orderId, Guid entryId, string version)
    {
        using var response = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}/sales-corrections/{entryId}", new { version });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    internal static async Task<(string? ReturnsTotal, string? CorrectionsTotal, string? Total, string? Excess)> ReductionsAsync(OrdersTest test, Guid orderId)
    {
        var reductions = (await test.GetJsonAsync($"/api/orders/{orderId}")).GetProperty("salesReductions");
        return (
            reductions.GetProperty("returnsTotal").GetString(),
            reductions.GetProperty("correctionsTotal").GetString(),
            reductions.GetProperty("total").GetString(),
            reductions.GetProperty("excess").GetString());
    }

    internal static async Task DeleteRefundAsync(OrdersTest test, Guid orderId, Guid entryId, string version)
    {
        using var response = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}/refunds/{entryId}", new { version });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    internal static async Task<CreatedEntry> CreatedAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var json = await Json(response);
        return new CreatedEntry(json.GetProperty("id").GetGuid(), json.GetProperty("version").GetString()!);
    }

    internal static async Task<string> VersionAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await Json(response)).GetProperty("version").GetString()!;
    }

    internal static async Task<JsonElement> PaymentAsync(OrdersTest test, Guid orderId) =>
        (await test.GetJsonAsync($"/api/orders/{orderId}")).GetProperty("payment");

    internal static (string? Paid, string? State, string? AmountDue, string? Overpayment, string? ToRefund) Summary(JsonElement payment) =>
    (
        payment.GetProperty("paid").GetString(),
        payment.GetProperty("state").GetString(),
        payment.GetProperty("amountDue").GetString(),
        payment.GetProperty("overpayment").GetString(),
        payment.GetProperty("toRefund").GetString());

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
    }

    internal static async Task<string[]> ErrorKeysAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        return [.. (await Json(response)).GetProperty("errors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];
    }

    internal static async Task<JsonElement> Json(HttpResponseMessage response) => await AuthClient.ProblemAsync(response);

    internal static Task<IReadOnlyList<EntryAuditRow>> EntryAuditRowsAsync(OrdersTest test, Guid orderId) =>
        test.RowsAsync(
            "SELECT ObjectType, ObjectId, Operation, AuthorKind, ObjectLabel, ObjectBusinessDate, ParentObjectType, ParentObjectId, Changes "
            + $"FROM reporting.AuditEntries WHERE ParentObjectId = '{orderId}' AND ObjectType IN (N'Payment', N'Refund', N'SalesCorrection') ORDER BY Id",
            reader => new EntryAuditRow(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDateTime(5),
                reader.GetString(6),
                reader.GetGuid(7),
                reader.GetString(8)));

    internal static IReadOnlyList<(string Field, string? Old, string? New, bool Personal)> Changes(EntryAuditRow row) =>
    [
        .. JsonDocument.Parse(row.Changes).RootElement.EnumerateArray().Select(c => (
            c.GetProperty("field").GetString()!,
            c.GetProperty("old").GetString(),
            c.GetProperty("new").GetString(),
            c.GetProperty("personal").GetBoolean())),
    ];
}

internal sealed record CreatedEntry(Guid Id, string Version);

internal sealed record EntryAuditRow(
    string Type,
    Guid Id,
    string Operation,
    string AuthorKind,
    string Label,
    DateTime BusinessDate,
    string ParentType,
    Guid ParentId,
    string Changes);
