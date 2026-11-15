using System.Text.Json.Nodes;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Api.IntegrationTests.Orders;
using static StoneFlow.Api.IntegrationTests.Orders.AnonymizationTestSupport;
using static StoneFlow.Api.IntegrationTests.Orders.OrderEntryRequests;

namespace StoneFlow.Api.IntegrationTests.Reporting;

/// <summary>
/// SF-22: anonimizacja nie zmienia raportów — raport sprzedaży, zestawienie składników, raport przepływów
/// i lista „Operacje” za okres zamówienia i jego wpisów są identyczne przed i po (bez czasu wyliczenia).
/// Data uruchomienia przed danymi, żeby okres nie był „przed uruchomieniem”. Dane fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AnonymizationReportsUnchangedTests(SqlServerFixture sqlServer)
{
    private const string Period = "period=custom&from=2026-10-01&to=2026-11-30";

    private static readonly string[] Reads =
    [
        $"/api/reports/sales?{Period}",
        $"/api/reports/sales/components?{Period}&pageSize=100",
        $"/api/reports/cash-flow?{Period}",
        "/api/operations?from=2026-10-01&to=2026-11-30&pageSize=100",
    ];

    [Fact]
    public async Task Reports_and_operations_are_identical_before_and_after()
    {
        await using var test = await OrdersTest.CreateAsync(
            sqlServer, settings: Retention(extra: new Dictionary<string, string?> { ["StoneFlow:LaunchDate"] = "2026-10-01" }));
        var order = await test.CreateOrderAsync(Order("1000.00", comment: "Komentarz o Zenonie"));
        await AddPaymentAsync(test, order.Id, "600.00", date: "2026-10-30");
        await AddPaymentAsync(test, order.Id, "400.00");
        await AddRefundAsync(test, order.Id, "100.00", kind: "salesReturn", reason: "Zwrot dla Zenona");
        using (var correction = await SendCorrectionAsync(test, order.Id, Correction("50.00", reason: "Rabat dla Zenona")))
        {
            await CreatedAsync(correction);
        }

        await MoveClockAndLoginAsync(test, new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        var before = await ReadAllAsync(test);

        await AnonymizeAsync(test, order.Id);

        var after = await ReadAllAsync(test);
        Assert.Equal(before, after);
        Assert.Contains(order.Number, before[1], StringComparison.Ordinal);
    }

    private static async Task<string[]> ReadAllAsync(OrdersTest test)
    {
        var results = new List<string>();
        foreach (var path in Reads)
        {
            var node = JsonNode.Parse((await test.GetJsonAsync(path)).GetRawText())!;
            RemoveCalculatedAt(node);
            results.Add(node.ToJsonString());
        }

        return [.. results];
    }

    private static void RemoveCalculatedAt(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("calculatedAtUtc");
                foreach (var (_, child) in obj.ToList())
                {
                    RemoveCalculatedAt(child);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    RemoveCalculatedAt(child);
                }

                break;
        }
    }
}
