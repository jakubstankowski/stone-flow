using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Orders.Api;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Kernel.Audit;
using static StoneFlow.Api.IntegrationTests.Orders.AnonymizationTestSupport;
using static StoneFlow.Api.IntegrationTests.Orders.OrderEntryRequests;

namespace StoneFlow.Api.IntegrationTests.Orders;

/// <summary>
/// SF-22: anonimizacja danych klienta przez API na prawdziwej bazie — funkcja wyłączona bez konfiguracji,
/// retencja z granicą dnia w Europe/Warsaw, usunięcie danych z zamówienia, wpisów i audytu, ślad operacji,
/// nieodwracalność, kolejność kontroli, atomowość i dostęp. Dane są fikcyjne. Zegar startuje 5.11.2026
/// 12:00 UTC; okres 1 miesiąc, więc zamówienie z tego dnia jest dostępne od 5.12.2026.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class CustomerAnonymizationApiTests(SqlServerFixture sqlServer)
{
    /// <summary>4.12.2026 23:59:59 w Warszawie (UTC+1).</summary>
    private static readonly DateTimeOffset LastPendingSecond = new(2026, 12, 4, 22, 59, 59, TimeSpan.Zero);

    /// <summary>5.12.2026 00:00 w Warszawie.</summary>
    private static readonly DateTimeOffset FirstAvailableMoment = new(2026, 12, 4, 23, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Later = new(2026, 12, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] PersonalValues =
        ["Zenon", "511 222 333", "zenon.anonimowy", "Ukryta", "11-111", "Testowo", "1234563218", "Dzwonić do", "Komentarz o", "Zwrot dla", "Rabat dla"];

    private static object FullOrder(string name = "Zenon Anonimowy", string kind = "person", string? version = null, string status = "inProgress") => new
    {
        orderDate = "2026-10-28",
        channel = "olx",
        value = "1000.00",
        fulfillmentStatus = version is null ? null : status,
        customer = new
        {
            kind,
            name,
            phone = "511 222 333",
            email = "zenon.anonimowy@example.com",
            street = "ul. Ukryta 7",
            postalCode = "11-111",
            city = "Testowo",
            taxId = "1234563218",
        },
        operational = new
        {
            suppliers = new[] { "Dostawca A" },
            zoReferences = new[] { "ZO-1" },
            invoiceNumber = "FV/1/2026",
            hasPhotos = false,
            hasReview = true,
            notes = "Dzwonić do Zenona po 16",
        },
        comment = "Komentarz o Zenonie",
        version,
    };

    /// <summary>Edycja zanonimizowanego zamówienia z klientem przepisanym z odczytu.</summary>
    private static object EditAfterAnonymization(JsonElement details, string version, string status = "completed", string? name = null, string? kind = null) => new
    {
        orderDate = details.GetProperty("orderDate").GetString(),
        channel = details.GetProperty("channel").GetString(),
        value = details.GetProperty("value").GetString(),
        fulfillmentStatus = status,
        customer = new
        {
            kind = kind ?? details.GetProperty("customer").GetProperty("kind").GetString(),
            name = name ?? details.GetProperty("customer").GetProperty("name").GetString(),
        },
        operational = new { suppliers = new[] { "Dostawca A" }, zoReferences = new[] { "ZO-1" }, invoiceNumber = "FV/1/2026", hasPhotos = false, hasReview = true },
        version,
    };

    [Fact]
    public async Task Without_configuration_get_is_disabled_and_post_is_rejected_even_for_missing_order()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(FullOrder());
        await MoveClockAndLoginAsync(test, Later.AddYears(1));

        var anonymization = await AnonymizationAsync(test, order.Id);
        using var existing = await SendAnonymizeAsync(test, order.Id, await VersionAsync(test, order.Id));
        using var missing = await SendAnonymizeAsync(test, Guid.NewGuid(), order.Version);

        Assert.Equal("disabled", anonymization.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, anonymization.GetProperty("retentionStartDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, anonymization.GetProperty("availableFrom").ValueKind);
        Assert.Equal(JsonValueKind.Null, anonymization.GetProperty("anonymizedAtUtc").ValueKind);
        await AssertProblemAsync(existing, HttpStatusCode.UnprocessableEntity, OrderProblems.AnonymizationDisabledCode);
        await AssertProblemAsync(missing, HttpStatusCode.UnprocessableEntity, OrderProblems.AnonymizationDisabledCode);
        Assert.Equal("Zenon Anonimowy", await test.ScalarAsync<string>($"SELECT CustomerName FROM orders.Orders WHERE Id = '{order.Id}'"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_configuration_value_keeps_the_feature_disabled(string value)
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: new Dictionary<string, string?> { [RetentionKey] = value });
        var order = await test.CreateOrderAsync(FullOrder());

        Assert.Equal("disabled", (await AnonymizationAsync(test, order.Id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Retention_boundary_is_the_warsaw_day_also_through_the_api()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var order = await test.CreateOrderAsync(FullOrder());

        await MoveClockAndLoginAsync(test, LastPendingSecond);
        var pending = await AnonymizationAsync(test, order.Id);
        using var rejected = await SendAnonymizeAsync(test, order.Id, await VersionAsync(test, order.Id));

        Assert.Equal("retentionPending", pending.GetProperty("status").GetString());
        Assert.Equal("2026-11-05", pending.GetProperty("retentionStartDate").GetString());
        Assert.Equal("2026-12-05", pending.GetProperty("availableFrom").GetString());
        await AssertProblemAsync(rejected, HttpStatusCode.UnprocessableEntity, OrderProblems.RetentionNotElapsedCode);
        var problem = await AuthClient.ProblemAsync(rejected);
        Assert.Equal("2026-12-05", problem.GetProperty("availableFrom").GetString());
        Assert.Contains("05.12.2026", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        await MoveClockAndLoginAsync(test, FirstAvailableMoment);
        var available = await AnonymizationAsync(test, order.Id);
        Assert.Equal("available", available.GetProperty("status").GetString());
        Assert.Equal("2026-12-05", available.GetProperty("availableFrom").GetString());
        await AnonymizeAsync(test, order.Id);

        var anonymized = await AnonymizationAsync(test, order.Id);
        Assert.Equal("anonymized", anonymized.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, anonymized.GetProperty("availableFrom").ValueKind);
        Assert.Equal(FirstAvailableMoment, anonymized.GetProperty("anonymizedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task Removes_customer_data_from_order_entries_and_audit_and_leaves_a_trace_without_personal_data()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var order = await test.CreateOrderAsync(FullOrder());
        var other = await test.CreateOrderAsync(Order("500.00"));
        await test.EditAsync(order.Id, FullOrder(name: "Zenon Anonimowy-Nowak", version: order.Version));
        await AddPaymentAsync(test, order.Id, "1000.00");
        await AddRefundAsync(test, order.Id, "100.00", kind: "salesReturn", reason: "Zwrot dla Zenona – pęknięty blat");
        await AddRefundAsync(test, order.Id, "50.00", reason: "Zwrot dla Zenona – nadpłata");
        var deleted = await AddRefundAsync(test, order.Id, "20.00", reason: "Zwrot dla Zenona – pomyłka");
        await DeleteRefundAsync(test, order.Id, deleted.Id, deleted.Version);
        using (var correction = await SendCorrectionAsync(test, order.Id, Correction("30.00", reason: "Rabat dla Zenona")))
        {
            await CreatedAsync(correction);
        }

        Assert.NotEqual(0, await OccurrencesAsync(test, "Zenon"));
        await MoveClockAndLoginAsync(test, Later);
        await AnonymizeAsync(test, order.Id);

        foreach (var value in PersonalValues)
        {
            Assert.True(await OccurrencesAsync(test, value) == 0, $"Wartość „{value}” została w bazie.");
        }

        Assert.True(await OccurrencesAsync(test, "Jan Testowy") > 0, "Dane innego zamówienia nie mogą zniknąć.");
        Assert.Equal(1, await test.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM orders.Orders WHERE Id = '{order.Id}' AND CreationRequestHash = 0x{new string('0', 64)}"));
        Assert.Equal(0, await test.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM orders.Refunds WHERE OrderId = '{order.Id}' AND CreationRequestHash <> 0x{new string('0', 64)}"));
        Assert.Equal(0, await test.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM orders.SalesCorrections WHERE OrderId = '{order.Id}' AND CreationRequestHash <> 0x{new string('0', 64)}"));

        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");
        var customer = details.GetProperty("customer");
        Assert.Equal(Marker, customer.GetProperty("name").GetString());
        Assert.Equal("person", customer.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, customer.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, details.GetProperty("comment").ValueKind);
        Assert.Equal(JsonValueKind.Null, details.GetProperty("operational").GetProperty("notes").ValueKind);
        Assert.Equal("FV/1/2026", details.GetProperty("operational").GetProperty("invoiceNumber").GetString());
        Assert.Equal("1000.00", details.GetProperty("value").GetString());
        Assert.Equal("850.00", details.GetProperty("payment").GetProperty("paid").GetString());
        Assert.All(details.GetProperty("refunds").EnumerateArray(), r => Assert.Equal(Marker, r.GetProperty("reason").GetString()));
        Assert.Equal(Marker, details.GetProperty("salesCorrections")[0].GetProperty("reason").GetString());

        var search = await test.GetJsonAsync("/api/orders?search=Zenon");
        Assert.Equal(0, search.GetProperty("total").GetInt32());
        var list = await test.GetJsonAsync("/api/orders?search=" + Uri.EscapeDataString(Marker));
        Assert.Equal(order.Id, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        var history = await test.GetJsonAsync("/api/change-history?changedFrom=2026-11-01&changedTo=2026-12-31&pageSize=100");
        foreach (var value in PersonalValues)
        {
            Assert.DoesNotContain(value, details.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(value, history.GetRawText(), StringComparison.Ordinal);
        }

        var audit = await AuditAsync(test, order.Id);
        var operation = Assert.Single(audit, row => row.Type == "Order" && row.Operation == "Anonymized");
        Assert.Equal("Account", operation.AuthorKind);
        Assert.Equal(order.Number, operation.Label);
        Assert.Equal(
            [
                ("anonymizedFields", (string?)null, (string?)"""["customerName","customerPhone","customerEmail","customerStreet","customerPostalCode","customerCity","customerTaxId","notes","comment"]""", false),
                ("anonymizedReasons", null, "3", false),
                ("retentionStartDate", null, "2026-11-05", false),
                ("retentionMonths", null, "1", false),
            ],
            Changes(operation.Changes));
        Assert.Equal(3, audit.Count(row => row.Type is "Refund" or "SalesCorrection" && row.Operation == "Anonymized"));
        var nameChange = Assert.Single(
            audit.Where(row => row.Type == "Order" && row.Operation == "Updated").SelectMany(row => Changes(row.Changes)),
            change => change.Field == "customerName");
        Assert.Equal(("customerName", Marker, Marker, true), nameChange);
        var deletion = Assert.Single(audit, row => row.Operation == "Deleted");
        Assert.Contains(("reason", Marker, (string?)null, false), Changes(deletion.Changes));

        var otherDetails = await test.GetJsonAsync($"/api/orders/{other.Id}");
        Assert.Equal("Jan Testowy", otherDetails.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal("available", otherDetails.GetProperty("anonymization").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Order_with_only_a_name_lists_only_the_name()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var order = await test.CreateOrderAsync(Order("100.00"));
        await MoveClockAndLoginAsync(test, Later);

        await AnonymizeAsync(test, order.Id);

        var operation = Assert.Single(await AuditAsync(test, order.Id), row => row.Operation == "Anonymized");
        Assert.Equal(("anonymizedFields", null, """["customerName"]""", false), Changes(operation.Changes)[0]);
        Assert.Equal(("anonymizedReasons", null, "0", false), Changes(operation.Changes)[1]);
    }

    [Fact]
    public async Task Late_entry_and_cancellation_move_the_retention_start()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var withPayment = await test.CreateOrderAsync(Order("100.00"));
        var cancelled = await test.CreateOrderAsync(Order("100.00"));

        await MoveClockAndLoginAsync(test, new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        await AddPaymentAsync(test, withPayment.Id, "100.00", date: "2026-11-05");
        await test.StatusActionAsync(cancelled.Id, "cancel", cancelled.Version);
        await MoveClockAndLoginAsync(test, Later);

        foreach (var id in new[] { withPayment.Id, cancelled.Id })
        {
            var anonymization = await AnonymizationAsync(test, id);
            Assert.Equal("retentionPending", anonymization.GetProperty("status").GetString());
            Assert.Equal("2026-11-20", anonymization.GetProperty("retentionStartDate").GetString());
            Assert.Equal("2026-12-20", anonymization.GetProperty("availableFrom").GetString());
        }
    }

    [Fact]
    public async Task Deleted_entry_moves_the_retention_start_through_the_audit()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var order = await test.CreateOrderAsync(Order("100.00"));
        await AddPaymentAsync(test, order.Id, "100.00");
        var refund = await AddRefundAsync(test, order.Id, "10.00");

        await MoveClockAndLoginAsync(test, new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        await DeleteRefundAsync(test, order.Id, refund.Id, refund.Version);
        await MoveClockAndLoginAsync(test, Later);

        var anonymization = await AnonymizationAsync(test, order.Id);
        using var rejected = await SendAnonymizeAsync(test, order.Id, await VersionAsync(test, order.Id));

        Assert.Equal("retentionPending", anonymization.GetProperty("status").GetString());
        Assert.Equal("2026-11-20", anonymization.GetProperty("retentionStartDate").GetString());
        await AssertProblemAsync(rejected, HttpStatusCode.UnprocessableEntity, OrderProblems.RetentionNotElapsedCode);
    }

    [Fact]
    public async Task Operation_is_irreversible_and_not_repeated()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var order = await test.CreateOrderAsync(FullOrder());
        await MoveClockAndLoginAsync(test, Later);
        var before = await VersionAsync(test, order.Id);

        var after = await AnonymizeAsync(test, order.Id);
        using var stale = await SendAnonymizeAsync(test, order.Id, before);
        using var repeated = await SendAnonymizeAsync(test, order.Id, after);

        await AssertProblemAsync(stale, HttpStatusCode.Conflict, OrderProblems.ConflictCode);
        await AssertProblemAsync(repeated, HttpStatusCode.Conflict, OrderProblems.AlreadyAnonymizedCode);
        Assert.Single(await AuditAsync(test, order.Id), row => row.Operation == "Anonymized");
    }

    [Fact]
    public async Task Stale_version_before_anonymization_is_a_conflict_without_changes()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var order = await test.CreateOrderAsync(FullOrder());
        await MoveClockAndLoginAsync(test, Later);
        await test.EditAsync(order.Id, FullOrder(version: order.Version, status: "completed"));

        using var response = await SendAnonymizeAsync(test, order.Id, order.Version);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, OrderProblems.ConflictCode);
        Assert.Equal("Zenon Anonimowy", await test.ScalarAsync<string>($"SELECT CustomerName FROM orders.Orders WHERE Id = '{order.Id}'"));
    }

    [Fact]
    public async Task After_anonymization_customer_is_locked_but_erp_fields_and_entries_work()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var order = await test.CreateOrderAsync(FullOrder());
        await AddPaymentAsync(test, order.Id, "500.00");
        var refund = await AddRefundAsync(test, order.Id, "50.00", reason: "Zwrot dla Zenona");
        await MoveClockAndLoginAsync(test, Later);
        var version = await AnonymizeAsync(test, order.Id);
        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");

        using var otherName = await test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}", EditAfterAnonymization(details, version, name: "Zenon Anonimowy"));
        using var otherKind = await test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}", EditAfterAnonymization(details, version, kind: "company"));
        await AssertProblemAsync(otherName, HttpStatusCode.UnprocessableEntity, OrderProblems.CustomerAnonymizedCode);
        await AssertProblemAsync(otherKind, HttpStatusCode.UnprocessableEntity, OrderProblems.CustomerAnonymizedCode);

        await test.EditAsync(order.Id, EditAfterAnonymization(details, version, status: "completed"));
        var updated = Assert.Single(await AuditAsync(test, order.Id), row => row.Operation == "Updated" && row.Changes.Contains("fulfillmentStatus", StringComparison.Ordinal));
        Assert.DoesNotContain(Changes(updated.Changes), change => change.Field.StartsWith("customer", StringComparison.Ordinal));

        await AddPaymentAsync(test, order.Id, "100.00", date: "2026-12-10");
        var newRefund = await AddRefundAsync(test, order.Id, "10.00", reason: "Nowy powód po anonimizacji");
        using (var correction = await SendCorrectionAsync(test, order.Id, Correction("5.00", date: "2026-12-10")))
        {
            await CreatedAsync(correction);
        }

        var refundVersion = (await test.GetJsonAsync($"/api/orders/{order.Id}")).GetProperty("refunds").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == refund.Id).GetProperty("version").GetString()!;
        using var sameReason = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{order.Id}/refunds/{refund.Id}", Refund("50.00", reason: Marker, version: refundVersion));
        await VersionAsync(sameReason);

        var refunds = (await test.GetJsonAsync($"/api/orders/{order.Id}")).GetProperty("refunds").EnumerateArray().ToList();
        Assert.Equal("Nowy powód po anonimizacji", refunds.Single(r => r.GetProperty("id").GetGuid() == newRefund.Id).GetProperty("reason").GetString());
        Assert.Equal(Marker, refunds.Single(r => r.GetProperty("id").GetGuid() == refund.Id).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Order_of_checks_disabled_before_not_found_and_validation_first()
    {
        await using var disabled = await OrdersTest.CreateAsync(sqlServer);
        await using var enabled = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        const string AnyVersion = "AAAAAAAAB9E=";

        using var disabledMissing = await SendAnonymizeAsync(disabled, Guid.NewGuid(), AnyVersion);
        using var enabledMissing = await SendAnonymizeAsync(enabled, Guid.NewGuid(), AnyVersion);
        using var disabledInvalid = await SendAnonymizeAsync(disabled, Guid.NewGuid(), "zła");
        using var enabledInvalid = await SendAnonymizeAsync(enabled, Guid.NewGuid(), null);

        await AssertProblemAsync(disabledMissing, HttpStatusCode.UnprocessableEntity, OrderProblems.AnonymizationDisabledCode);
        await AssertProblemAsync(enabledMissing, HttpStatusCode.NotFound, OrderProblems.NotFoundCode);
        Assert.Equal(["version"], await ErrorKeysAsync(disabledInvalid));
        Assert.Equal(["version"], await ErrorKeysAsync(enabledInvalid));
    }

    [Fact]
    public async Task Requires_session_and_csrf()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var order = await test.CreateOrderAsync(FullOrder());
        await MoveClockAndLoginAsync(test, Later);
        var version = await VersionAsync(test, order.Id);

        using var withoutCsrf = await SendAnonymizeAsync(test, order.Id, version, withCsrf: false);
        using var anonymous = test.Factory.CreateClient();
        using var withoutSession = await anonymous.PostAsJsonAsync(
            new Uri($"/api/orders/{order.Id}/anonymize", UriKind.Relative), new { version }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Equal("Zenon Anonimowy", await test.ScalarAsync<string>($"SELECT CustomerName FROM orders.Orders WHERE Id = '{order.Id}'"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failure_in_audit_rolls_back_everything(bool failRedaction)
    {
        await using var test = await OrdersTest.CreateAsync(
            sqlServer,
            configureServices: services =>
            {
                if (failRedaction)
                {
                    services.AddScoped<IAuditRedaction, ThrowingRedaction>();
                }
                else
                {
                    FailingAnonymizedAuditLog.Register(services);
                }
            },
            settings: Retention());
        var order = await test.CreateOrderAsync(FullOrder());
        await AddPaymentAsync(test, order.Id, "100.00");
        await AddRefundAsync(test, order.Id, "10.00", reason: "Zwrot dla Zenona");
        await MoveClockAndLoginAsync(test, Later);
        var auditBefore = await AuditAsync(test, order.Id);

        using var response = await SendAnonymizeAsync(test, order.Id, await VersionAsync(test, order.Id));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Zenon Anonimowy", await test.ScalarAsync<string>($"SELECT CustomerName FROM orders.Orders WHERE Id = '{order.Id}'"));
        Assert.Equal("Zwrot dla Zenona", await test.ScalarAsync<string>($"SELECT Reason FROM orders.Refunds WHERE OrderId = '{order.Id}'"));
        Assert.Equal(0, await test.ScalarAsync<int>($"SELECT COUNT(*) FROM orders.Orders WHERE Id = '{order.Id}' AND AnonymizedAtUtc IS NOT NULL"));
        Assert.Equal(auditBefore, await AuditAsync(test, order.Id));
    }

    [Fact]
    public async Task Repeated_create_request_of_anonymized_order_gets_key_reused_without_personal_data()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer, settings: Retention());
        var key = Guid.NewGuid();
        var order = await test.CreateOrderAsync(FullOrder(), key);
        await MoveClockAndLoginAsync(test, Later);
        await AnonymizeAsync(test, order.Id);

        using var repeated = await test.CreateAsync(FullOrder(), key);

        await AssertProblemAsync(repeated, HttpStatusCode.Conflict, OrderProblems.IdempotencyKeyReusedCode);
        var body = await repeated.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(order.Number, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Zenon", body, StringComparison.Ordinal);
    }

    private sealed class ThrowingRedaction : IAuditRedaction
    {
        public Task<int> RedactAsync(AuditRedaction redaction, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Symulowany błąd anonimizacji audytu.");
    }

    /// <summary>Prawdziwy zapis audytu, który rzuca wyjątek przy wpisie operacji <c>Anonymized</c>.</summary>
    private sealed class FailingAnonymizedAuditLog(IAuditLog inner) : IAuditLog
    {
        public static void Register(IServiceCollection services)
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(IAuditLog));
            var implementation = descriptor.ImplementationType
                ?? throw new InvalidOperationException("Zapis audytu powinien być zarejestrowany typem.");
            services.Remove(descriptor);
            services.AddScoped<IAuditLog>(provider =>
                new FailingAnonymizedAuditLog((IAuditLog)ActivatorUtilities.CreateInstance(provider, implementation)));
        }

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken) =>
            entry.Operation == AuditOperation.Anonymized
                ? throw new InvalidOperationException("Symulowany błąd zapisu wpisu anonimizacji.")
                : inner.RecordAsync(entry, cancellationToken);
    }
}
