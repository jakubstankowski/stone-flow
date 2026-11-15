using System.Net;
using System.Text.Json;
using StoneFlow.Api.IntegrationTests.Infrastructure;

namespace StoneFlow.Api.IntegrationTests.Orders;

/// <summary>
/// Wsparcie testów anonimizacji danych klienta (SF-22): konfiguracja okresu, przesunięcie zegara z ponownym
/// logowaniem (sesja wygasa), wywołanie akcji i przeszukanie tabel pod kątem pierwotnych wartości.
/// Funkcję włącza się w testach jawnie — <see cref="StoneFlowApiFactory"/> nie ustawia klucza.
/// </summary>
internal static class AnonymizationTestSupport
{
    public const string RetentionKey = "StoneFlow:Anonymization:RetentionMonths";

    public const string Marker = "[zanonimizowano]";

    /// <summary>Okres retencji i dodatkowe ustawienia testu.</summary>
    public static Dictionary<string, string?> Retention(int months = 1, IReadOnlyDictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>(extra ?? new Dictionary<string, string?>())
        {
            [RetentionKey] = months.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return settings;
    }

    /// <summary>Przesuwa zegar aplikacji do <paramref name="to"/> i loguje właściciela ponownie.</summary>
    public static async Task MoveClockAndLoginAsync(OrdersTest test, DateTimeOffset to)
    {
        test.Time.SetUtcNow(to);
        using var login = await test.Client.LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    public static async Task<string> VersionAsync(OrdersTest test, Guid orderId) =>
        (await test.GetJsonAsync($"/api/orders/{orderId}")).GetProperty("version").GetString()!;

    public static async Task<JsonElement> AnonymizationAsync(OrdersTest test, Guid orderId) =>
        (await test.GetJsonAsync($"/api/orders/{orderId}")).GetProperty("anonymization");

    public static Task<HttpResponseMessage> SendAnonymizeAsync(OrdersTest test, Guid orderId, string? version, bool withCsrf = true) =>
        test.SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/anonymize", new { version }, withCsrf: withCsrf);

    /// <summary>Udana anonimizacja z aktualną wersją; zwraca nową wersję.</summary>
    public static async Task<string> AnonymizeAsync(OrdersTest test, Guid orderId)
    {
        using var response = await SendAnonymizeAsync(test, orderId, await VersionAsync(test, orderId));
        return await OrderEntryRequests.VersionAsync(response);
    }

    /// <summary>
    /// Liczba trafień tekstu we wszystkich kolumnach tekstowych tabel schematów <c>orders</c> i
    /// <c>reporting</c> (w tym JSON <c>Changes</c> audytu); porównanie bez rozróżnienia wielkości liter.
    /// </summary>
    public static async Task<int> OccurrencesAsync(OrdersTest test, string value)
    {
        var columns = await test.RowsAsync(
            "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_SCHEMA IN (N'orders', N'reporting') AND DATA_TYPE IN (N'nvarchar', N'varchar', N'nchar', N'char') "
            + "AND TABLE_NAME <> N'__EFMigrationsHistory'",
            reader => (Schema: reader.GetString(0), Table: reader.GetString(1), Column: reader.GetString(2)));
        var sql = string.Join(
            " + ",
            columns.Select(c => $"(SELECT COUNT(*) FROM [{c.Schema}].[{c.Table}] WHERE [{c.Column}] LIKE @pattern)"));
        var rows = await test.RowsAsync(
            $"DECLARE @pattern nvarchar(400) = N'%{value.Replace("'", "''", StringComparison.Ordinal).Replace("[", "[[]", StringComparison.Ordinal)}%'; SELECT {sql}",
            reader => reader.GetInt32(0));
        return rows.Single();
    }

    /// <summary>Wiersze audytu zamówienia i jego wpisów (obiekt albo rodzic), w kolejności zapisu.</summary>
    public static Task<IReadOnlyList<(string Type, Guid Id, string Operation, string AuthorKind, string Label, string Changes)>> AuditAsync(OrdersTest test, Guid orderId) =>
        test.RowsAsync(
            "SELECT ObjectType, ObjectId, Operation, AuthorKind, ObjectLabel, Changes FROM reporting.AuditEntries "
            + $"WHERE ObjectId = '{orderId}' OR ParentObjectId = '{orderId}' ORDER BY Id",
            reader => (reader.GetString(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));

    public static IReadOnlyList<(string Field, string? Old, string? New, bool Personal)> Changes(string json) =>
    [
        .. JsonDocument.Parse(json).RootElement.EnumerateArray().Select(c => (
            c.GetProperty("field").GetString()!,
            c.GetProperty("old").GetString(),
            c.GetProperty("new").GetString(),
            c.GetProperty("personal").GetBoolean())),
    ];
}
