using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Access.Api;
using StoneFlow.Api.IntegrationTests.Infrastructure;
using StoneFlow.Orders.Api;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Api.IntegrationTests.Orders;

/// <summary>
/// SF-9: wpłaty i zwroty przez API na prawdziwej bazie — stan płatności, limity pod blokadą
/// zamówienia, idempotencja, kontrola wersji, fizyczne usuwanie z audytem, lista i filtr oraz
/// ochrona dostępu. Dane są fikcyjne.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PaymentsApiTests(SqlServerFixture sqlServer)
{
    private const string Today = "2026-11-05";

    [Fact]
    public async Task State_progression_from_unpaid_through_partially_paid_to_paid()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));

        var start = await PaymentAsync(test, order.Id);
        await AddPaymentAsync(test, order.Id, "300.00");
        var partial = await PaymentAsync(test, order.Id);
        await AddPaymentAsync(test, order.Id, "700.00");
        var paid = await PaymentAsync(test, order.Id);

        Assert.Equal(("0.00", "unpaid", "1000.00", null, null), Summary(start));
        Assert.Equal(("300.00", "partiallyPaid", "700.00", null, null), Summary(partial));
        Assert.Equal(("1000.00", "paid", null, null, null), Summary(paid));
        var item = (await test.GetJsonAsync("/api/orders")).GetProperty("items")[0];
        Assert.Equal("1000.00", item.GetProperty("paid").GetString());
        Assert.Equal("paid", item.GetProperty("paymentState").GetString());
    }

    [Fact]
    public async Task State_progression_with_overpayment_and_money_refund()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));

        await AddPaymentAsync(test, order.Id, "1200.00");
        var overpaid = await PaymentAsync(test, order.Id);
        await AddRefundAsync(test, order.Id, "200.00");
        var settled = await PaymentAsync(test, order.Id);

        Assert.Equal(("1200.00", "paid", null, "200.00", null), Summary(overpaid));
        Assert.Equal(("1000.00", "paid", null, null, null), Summary(settled));
    }

    [Fact]
    public async Task State_progression_for_zero_value_order()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("0"));

        var start = await PaymentAsync(test, order.Id);
        await AddPaymentAsync(test, order.Id, "50.00");
        var overpaid = await PaymentAsync(test, order.Id);
        await AddRefundAsync(test, order.Id, "50.00");
        var refunded = await PaymentAsync(test, order.Id);

        Assert.Equal(("0.00", "paid", null, null, null), Summary(start));
        Assert.Equal(("50.00", "paid", null, "50.00", null), Summary(overpaid));
        Assert.Equal(("0.00", "paid", null, null, null), Summary(refunded));
    }

    [Fact]
    public async Task State_progression_for_cancelled_order_shows_amount_to_refund()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "300.00");

        var cancelled = await test.StatusActionAsync(order.Id, "cancel", order.Version);
        // Wpłata do anulowanego zamówienia jest przyjmowana (ostrzeżenie pokazuje UI).
        await AddPaymentAsync(test, order.Id, "200.00");
        var toRefund = await PaymentAsync(test, order.Id);
        await AddRefundAsync(test, order.Id, "500.00");
        var refunded = await PaymentAsync(test, order.Id);
        await test.StatusActionAsync(order.Id, "restore", cancelled);
        var restored = await PaymentAsync(test, order.Id);

        Assert.Equal(("500.00", null, null, null, "500.00"), Summary(toRefund));
        Assert.Equal(("0.00", null, null, null, null), Summary(refunded));
        Assert.Equal(("0.00", "unpaid", "1000.00", null, null), Summary(restored));
    }

    [Fact]
    public async Task Payment_does_not_change_order_value_or_status()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var before = await test.GetJsonAsync($"/api/orders/{order.Id}");

        var payment = await AddPaymentAsync(test, order.Id, "1000.00", method: "cashOnDelivery");
        await EditPaymentAsync(test, order.Id, payment.Id, payment.Version, "900.00", method: "cashOnDelivery");
        await AddRefundAsync(test, order.Id, "100.00");

        var after = await test.GetJsonAsync($"/api/orders/{order.Id}");
        foreach (var property in new[] { "value", "fulfillmentStatus", "version", "updatedAtUtc" })
        {
            Assert.Equal(before.GetProperty(property).GetString(), after.GetProperty(property).GetString());
        }

        // Blokada zamówienia nie zmienia jego wersji: otwarta edycja zamówienia zapisze się bez konfliktu.
        Assert.Single(await test.AuditRowsAsync(order.Id));
        await test.EditAsync(order.Id, Order("1000.00", comment: "Po wpłacie", version: order.Version));
    }

    [Fact]
    public async Task Cash_on_delivery_order_without_payment_is_unpaid()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("250.00", comment: "Za pobraniem"));

        var unpaid = await PaymentAsync(test, order.Id);
        await AddPaymentAsync(test, order.Id, "250.00", method: "cashOnDelivery");
        var paid = await PaymentAsync(test, order.Id);

        Assert.Equal(("0.00", "unpaid", "250.00", null, null), Summary(unpaid));
        Assert.Equal(("250.00", "paid", null, null, null), Summary(paid));
    }

    [Fact]
    public async Task Details_list_entries_sorted_by_date_then_creation()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var later = await AddPaymentAsync(test, order.Id, "100.00", date: "2026-11-04", method: "cash", receivedBy: " Anna Przykładowa ");
        var earlier = await AddPaymentAsync(test, order.Id, "200.00", date: "2026-10-30");
        test.Time.Advance(TimeSpan.FromMinutes(1));
        var sameDay = await AddPaymentAsync(test, order.Id, "300.00", date: "2026-10-30", method: "blik");
        var refund = await AddRefundAsync(test, order.Id, "50.00", kind: "salesReturn", method: "cash", reason: " Oddany towar ");

        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");

        var payments = details.GetProperty("payments").EnumerateArray().ToArray();
        Assert.Equal([earlier.Id, sameDay.Id, later.Id], payments.Select(p => p.GetProperty("id").GetGuid()));
        var cash = payments[2];
        Assert.Equal("100.00", cash.GetProperty("amount").GetString());
        Assert.Equal("2026-11-04", cash.GetProperty("date").GetString());
        Assert.Equal("cash", cash.GetProperty("method").GetString());
        Assert.Equal("Anna Przykładowa", cash.GetProperty("receivedBy").GetString());
        Assert.Equal(later.Version, cash.GetProperty("version").GetString());
        Assert.EndsWith("Z", cash.GetProperty("createdAtUtc").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, payments[0].GetProperty("receivedBy").ValueKind);
        var refundJson = Assert.Single(details.GetProperty("refunds").EnumerateArray());
        Assert.Equal(refund.Id, refundJson.GetProperty("id").GetGuid());
        Assert.Equal("salesReturn", refundJson.GetProperty("kind").GetString());
        Assert.Equal("50.00", refundJson.GetProperty("amount").GetString());
        Assert.Equal(Today, refundJson.GetProperty("date").GetString());
        Assert.Equal("cash", refundJson.GetProperty("method").GetString());
        Assert.Equal("Oddany towar", refundJson.GetProperty("reason").GetString());
        Assert.Equal(("550.00", "partiallyPaid", "450.00", null, null), Summary(details.GetProperty("payment")));
    }

    [Fact]
    public async Task Shop_order_accepts_payments_and_refunds()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var shop = await test.SeedShopOrderAsync();

        await AddPaymentAsync(test, shop, "250.00");
        var refund = await AddRefundAsync(test, shop, "50.00");
        await DeleteRefundAsync(test, shop, refund.Id, refund.Version);

        Assert.Equal(("250.00", "paid", null, null, null), Summary(await PaymentAsync(test, shop)));
        var rows = await EntryAuditRowsAsync(test, shop);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.EndsWith(" do ZR-2026-9001", row.Label, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refund_limit_returns_422_with_available_and_changes_nothing()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "1000.00");

        using var tooMuch = await SendRefundAsync(test, order.Id, Refund("1200.00"));
        await AddRefundAsync(test, order.Id, "1000.00");
        using var nothingLeft = await SendRefundAsync(test, order.Id, Refund("0.01"));

        await AssertProblemAsync(tooMuch, HttpStatusCode.UnprocessableEntity, OrderProblems.RefundExceedsPaidCode);
        var problem = await Json(tooMuch);
        Assert.Equal("1000.00", problem.GetProperty("available").GetString());
        Assert.StartsWith("Zwrot nie może przekroczyć kwoty Zapłacono (", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        await AssertProblemAsync(nothingLeft, HttpStatusCode.UnprocessableEntity, OrderProblems.RefundExceedsPaidCode);
        Assert.Equal("0.00", (await Json(nothingLeft)).GetProperty("available").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Refunds"));
        Assert.Equal(2, (await EntryAuditRowsAsync(test, order.Id)).Count);
    }

    [Fact]
    public async Task Repeated_refund_that_exhausted_paid_returns_200_not_422()
    {
        // Klucz idempotencji jest sprawdzany przed limitem (N1).
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "1000.00");
        var key = Guid.NewGuid();

        using var first = await SendRefundAsync(test, order.Id, Refund("1000.00"), key);
        using var repeated = await SendRefundAsync(test, order.Id, Refund(" 1000 ", reason: "  Zwrot pieniędzy testowy "), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal((await Json(first)).GetProperty("id").GetGuid(), (await Json(repeated)).GetProperty("id").GetGuid());
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Refunds"));
    }

    [Fact]
    public async Task Edit_delete_negative_paid_are_rejected_and_change_nothing()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "1000.00");
        var refund = await AddRefundAsync(test, order.Id, "300.00");

        using var lowerPayment = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{order.Id}/payments/{payment.Id}", Payment("100.00", version: payment.Version));
        using var deletePayment = await test.SendAsync(
            HttpMethod.Delete, $"/api/orders/{order.Id}/payments/{payment.Id}", new { version = payment.Version });
        using var raiseRefund = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{order.Id}/refunds/{refund.Id}", Refund("1200.00", version: refund.Version));

        await AssertProblemAsync(lowerPayment, HttpStatusCode.UnprocessableEntity, OrderProblems.PaidWouldBeNegativeCode);
        await AssertProblemAsync(deletePayment, HttpStatusCode.UnprocessableEntity, OrderProblems.PaidWouldBeNegativeCode);
        await AssertProblemAsync(raiseRefund, HttpStatusCode.UnprocessableEntity, OrderProblems.RefundExceedsPaidCode);
        Assert.Equal("1000.00", (await Json(raiseRefund)).GetProperty("available").GetString());
        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");
        Assert.Equal(payment.Version, details.GetProperty("payments")[0].GetProperty("version").GetString());
        Assert.Equal("1000.00", details.GetProperty("payments")[0].GetProperty("amount").GetString());
        Assert.Equal("300.00", details.GetProperty("refunds")[0].GetProperty("amount").GetString());
        Assert.Equal(2, (await EntryAuditRowsAsync(test, order.Id)).Count);

        // Raising the refund to exactly the available amount and removing it are allowed.
        await EditRefundAsync(test, order.Id, refund.Id, refund.Version, "1000.00");
        var afterEdit = await test.GetJsonAsync($"/api/orders/{order.Id}");
        await DeleteRefundAsync(test, order.Id, refund.Id, afterEdit.GetProperty("refunds")[0].GetProperty("version").GetString()!);
        await DeletePaymentAsync(test, order.Id, payment.Id, payment.Version);
        Assert.Equal(("0.00", "unpaid", "1000.00", null, null), Summary(await PaymentAsync(test, order.Id)));
    }

    [Fact]
    public async Task Parallel_refunds_respect_limit()
    {
        // Oba zwroty po 600 przy Zapłacono 1000. Dekorator portu czeka przed zapisem, aż drugi zwrot
        // też dojdzie do zapisu (najwyżej 2 s): bez blokady zamówienia oba przeszłyby sprawdzenie
        // limitu; z blokadą drugi czeka na pierwszy i widzi już jego zwrot.
        var hooks = new EntryStoreHooks();
        await using var test = await OrdersTest.CreateAsync(sqlServer, configureServices: services => hooks.Register(services));
        var order = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, order.Id, "1000.00");
        var arrived = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hooks.BeforeAdd = async _ =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                both.TrySetResult();
            }

            await Task.WhenAny(both.Task, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        };

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => SendRefundAsync(test, order.Id, Refund("600.00"))));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            var rejected = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
            var problem = await Json(rejected);
            Assert.Equal(OrderProblems.RefundExceedsPaidCode, problem.GetProperty("code").GetString());
            Assert.Equal("400.00", problem.GetProperty("available").GetString());
            Assert.Equal(600m, await test.ScalarAsync<decimal>("SELECT SUM(Amount) FROM orders.Refunds"));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Parallel_adds_with_same_key_create_one_entry()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SendPaymentAsync(test, order.Id, Payment("300.00"), key)));
        try
        {
            Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            var ids = new HashSet<Guid>();
            foreach (var response in responses)
            {
                ids.Add((await Json(response)).GetProperty("id").GetGuid());
            }

            Assert.Single(ids);
            Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));
            Assert.Single(await EntryAuditRowsAsync(test, order.Id));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Key_reused_for_other_order_or_other_data_conflicts()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var first = await test.CreateOrderAsync(Order("1000.00"));
        var second = await test.CreateOrderAsync(Order("1000.00"));
        var key = Guid.NewGuid();
        var payment = await AddPaymentAsync(test, first.Id, "300.00", key: key);

        using var otherOrder = await SendPaymentAsync(test, second.Id, Payment("300.00"), key);
        using var otherData = await SendPaymentAsync(test, first.Id, Payment("301.00"), key);
        // Klucze wpłat i zwrotów są niezależne (osobne tabele).
        using var refundWithSameKey = await SendRefundAsync(test, first.Id, Refund("100.00"), key);

        await AssertProblemAsync(otherOrder, HttpStatusCode.Conflict, OrderProblems.IdempotencyKeyReusedCode);
        Assert.Equal(payment.Id, (await Json(otherOrder)).GetProperty("paymentId").GetGuid());
        await AssertProblemAsync(otherData, HttpStatusCode.Conflict, OrderProblems.IdempotencyKeyReusedCode);
        Assert.Equal(HttpStatusCode.Created, refundWithSameKey.StatusCode);
        Assert.Equal(0, (await test.GetJsonAsync($"/api/orders/{second.Id}")).GetProperty("payments").GetArrayLength());
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));

        using var refundReused = await SendRefundAsync(test, second.Id, Refund("100.00"), key);
        await AssertProblemAsync(refundReused, HttpStatusCode.Conflict, OrderProblems.IdempotencyKeyReusedCode);
        Assert.Equal((await Json(refundWithSameKey)).GetProperty("id").GetGuid(), (await Json(refundReused)).GetProperty("refundId").GetGuid());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicate_key_on_insert_is_resolved_after_rollback(bool sameData)
    {
        // Wpis z tym samym kluczem powstaje „równolegle” (np. w innym zamówieniu, bez wspólnej
        // blokady) już po kroku szukania po kluczu: zapis trafia na unikalny indeks.
        var hooks = new EntryStoreHooks();
        await using var test = await OrdersTest.CreateAsync(sqlServer, configureServices: services => hooks.Register(services));
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var key = Guid.NewGuid();
        var racing = Guid.NewGuid();
        hooks.BeforeAdd = entry => test.ExecuteSqlAsync(
            "INSERT INTO orders.Payments (Id, OrderId, Amount, PaidOn, Method, CreationRequestId, CreationRequestHash, CreatedAtUtc, UpdatedAtUtc) "
            + $"VALUES ('{racing}', '{entry.OrderId}', 300.00, '{Today}', N'Transfer', '{key}', "
            + (sameData ? "0x" + Convert.ToHexString(entry.CreationRequestHash!) : "CAST(0x01 AS binary(32))")
            + ", SYSUTCDATETIME(), SYSUTCDATETIME());");

        using var response = await SendPaymentAsync(test, order.Id, Payment("300.00"), key);

        var body = await Json(response);
        if (sameData)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(racing, body.GetProperty("id").GetGuid());
            Assert.False(string.IsNullOrEmpty(body.GetProperty("version").GetString()));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(OrderProblems.IdempotencyKeyReusedCode, body.GetProperty("code").GetString());
            Assert.Equal(racing, body.GetProperty("paymentId").GetGuid());
        }

        // Wycofanie objęło audyt: zostaje tylko wiersz wstawiony „równolegle”.
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));
        Assert.Empty(await EntryAuditRowsAsync(test, order.Id));
    }

    [Fact]
    public async Task Conflict_with_stale_version_returns_409_and_deleted_entry_returns_404()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "300.00");

        var edited = await EditPaymentAsync(test, order.Id, payment.Id, payment.Version, "350.00");
        using var staleEdit = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{order.Id}/payments/{payment.Id}", Payment("400.00", version: payment.Version));
        using var staleDelete = await test.SendAsync(
            HttpMethod.Delete, $"/api/orders/{order.Id}/payments/{payment.Id}", new { version = payment.Version });
        await DeletePaymentAsync(test, order.Id, payment.Id, edited);
        using var deletedAgain = await test.SendAsync(
            HttpMethod.Delete, $"/api/orders/{order.Id}/payments/{payment.Id}", new { version = edited });

        await AssertProblemAsync(staleEdit, HttpStatusCode.Conflict, OrderProblems.EntryConflictCode);
        await AssertProblemAsync(staleDelete, HttpStatusCode.Conflict, OrderProblems.EntryConflictCode);
        await AssertProblemAsync(deletedAgain, HttpStatusCode.NotFound, OrderProblems.EntryNotFoundCode);
        Assert.Equal(3, (await EntryAuditRowsAsync(test, order.Id)).Count);
    }

    [Fact]
    public async Task Conflict_between_load_and_save_returns_409_entry_conflict()
    {
        // Wiersz wpisu zmieniony innym połączeniem już po wczytaniu w jednostce pracy: UPDATE/DELETE
        // z wersją klienta (OriginalValue) nie trafia w wiersz, port zwraca konflikt.
        var hooks = new EntryStoreHooks();
        await using var test = await OrdersTest.CreateAsync(sqlServer, configureServices: services => hooks.Register(services));
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "300.00");
        await AddPaymentAsync(test, order.Id, "500.00", date: "2026-11-04");
        var refund = await AddRefundAsync(test, order.Id, "100.00");
        hooks.BeforeSave = entry => test.ExecuteSqlAsync($"UPDATE orders.Payments SET PaidOn = '2026-11-01' WHERE Id = '{entry.Id}';");
        hooks.BeforeDelete = entry => test.ExecuteSqlAsync(
            $"UPDATE orders.{(entry is Payment ? "Payments" : "Refunds")} SET Amount = Amount WHERE Id = '{entry.Id}';");

        using var edit = await test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}/payments/{payment.Id}", Payment("350.00", version: payment.Version));
        // Po zmianie „z innej karty” wpłata ma datę 1.11, więc jest pierwsza na liście.
        var current = (await test.GetJsonAsync($"/api/orders/{order.Id}")).GetProperty("payments")[0].GetProperty("version").GetString();
        using var delete = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{order.Id}/payments/{payment.Id}", new { version = current });
        using var deleteRefund = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{order.Id}/refunds/{refund.Id}", new { version = refund.Version });

        await AssertProblemAsync(edit, HttpStatusCode.Conflict, OrderProblems.EntryConflictCode);
        await AssertProblemAsync(delete, HttpStatusCode.Conflict, OrderProblems.EntryConflictCode);
        await AssertProblemAsync(deleteRefund, HttpStatusCode.Conflict, OrderProblems.EntryConflictCode);
        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");
        Assert.Equal("300.00", details.GetProperty("payments")[0].GetProperty("amount").GetString());
        Assert.Equal("2026-11-01", details.GetProperty("payments")[0].GetProperty("date").GetString());
        Assert.Equal(1, details.GetProperty("refunds").GetArrayLength());
        Assert.Equal(3, (await EntryAuditRowsAsync(test, order.Id)).Count);
    }

    [Fact]
    public async Task Store_compares_with_the_client_version_not_the_loaded_one()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "300.00");
        var current = await EditPaymentAsync(test, order.Id, payment.Id, payment.Version, "350.00");

        // Wczytana jest aktualna wersja, ale klient przysłał starszą: zapis i usunięcie nie mogą przejść.
        using var scope = test.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var results = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async cancellationToken =>
            {
                var store = services.GetRequiredService<IOrderEntryStore<Payment>>();
                var stored = await store.LoadAsync(payment.Id, cancellationToken);
                Assert.Equal(current, Convert.ToBase64String(stored!.Version));
                stored.Entry.Edit(new PaymentDetails(1m, new DateOnly(2026, 11, 5), PaymentMethod.Transfer, null), DateTime.UtcNow);
                var saved = await store.SaveAsync(stored.Entry, Convert.FromBase64String(payment.Version), cancellationToken);
                var deleted = await store.DeleteAsync(stored.Entry, Convert.FromBase64String(payment.Version), cancellationToken);
                return (saved.Status, deleted.Status);
            },
            commitWhen: _ => false,
            TestContext.Current.CancellationToken);

        Assert.Equal((OrderSaveStatus.Conflict, OrderSaveStatus.Conflict), results);
        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");
        Assert.Equal(("350.00", current), (details.GetProperty("payments")[0].GetProperty("amount").GetString(), details.GetProperty("payments")[0].GetProperty("version").GetString()));
    }

    [Fact]
    public async Task Entry_of_other_order_or_unknown_ids_return_404()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var first = await test.CreateOrderAsync(Order("1000.00"));
        var second = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, first.Id, "300.00");
        var refund = await AddRefundAsync(test, first.Id, "100.00");

        using var editOther = await test.SendAsync(HttpMethod.Put, $"/api/orders/{second.Id}/payments/{payment.Id}", Payment("1.00", version: payment.Version));
        using var deleteOther = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{second.Id}/refunds/{refund.Id}", new { version = refund.Version });
        using var paymentAsRefund = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{first.Id}/refunds/{payment.Id}", new { version = payment.Version });
        using var unknownEntry = await test.SendAsync(HttpMethod.Put, $"/api/orders/{first.Id}/refunds/{Guid.NewGuid()}", Refund("1.00", version: refund.Version));
        using var unknownOrder = await test.SendAsync(HttpMethod.Put, $"/api/orders/{Guid.NewGuid()}/payments/{payment.Id}", Payment("1.00", version: payment.Version));
        using var addToUnknown = await SendPaymentAsync(test, Guid.NewGuid(), Payment("1.00"));
        using var refundToUnknown = await SendRefundAsync(test, Guid.NewGuid(), Refund("1.00"));

        await AssertProblemAsync(editOther, HttpStatusCode.NotFound, OrderProblems.EntryNotFoundCode);
        await AssertProblemAsync(deleteOther, HttpStatusCode.NotFound, OrderProblems.EntryNotFoundCode);
        await AssertProblemAsync(paymentAsRefund, HttpStatusCode.NotFound, OrderProblems.EntryNotFoundCode);
        await AssertProblemAsync(unknownEntry, HttpStatusCode.NotFound, OrderProblems.EntryNotFoundCode);
        await AssertProblemAsync(unknownOrder, HttpStatusCode.NotFound, OrderProblems.NotFoundCode);
        await AssertProblemAsync(addToUnknown, HttpStatusCode.NotFound, OrderProblems.NotFoundCode);
        await AssertProblemAsync(refundToUnknown, HttpStatusCode.NotFound, OrderProblems.NotFoundCode);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Refunds"));
    }

    [Fact]
    public async Task Delete_requires_version_in_body()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "300.00");
        var refund = await AddRefundAsync(test, order.Id, "100.00");

        using var withoutBody = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{order.Id}/payments/{payment.Id}");
        using var withoutVersion = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{order.Id}/refunds/{refund.Id}", new { });
        using var invalidVersion = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{order.Id}/payments/{payment.Id}", new { version = "a+b/c==" });

        foreach (var response in new[] { withoutBody, withoutVersion, invalidVersion })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True((await Json(response)).GetProperty("errors").TryGetProperty("version", out _));
        }

        // Wersja base64 z „+” i „/” w ciele JSON nie wymaga kodowania jak w adresie.
        Assert.Matches("^[A-Za-z0-9+/=]+$", refund.Version);
        await DeleteRefundAsync(test, order.Id, refund.Id, refund.Version);
        await DeletePaymentAsync(test, order.Id, payment.Id, payment.Version);
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));
    }

    [Fact]
    public async Task Invalid_entries_return_400_with_json_path_keys()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "300.00");

        using var invalidPayment = await SendPaymentAsync(test, order.Id, new { amount = "0", date = "2026-11-06", method = "transfer", receivedBy = "Anna" });
        using var negative = await SendPaymentAsync(test, order.Id, Payment("-5.00"));
        using var invalidRefund = await SendRefundAsync(test, order.Id, new { amount = "1.234", date = Today, method = "cashOnDelivery", reason = " " });
        using var invalidEdit = await test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}/payments/{payment.Id}", Payment("abc"));
        using var withoutKey = await test.SendAsync(HttpMethod.Post, $"/api/orders/{order.Id}/payments", Payment("1.00"));
        using var emptyBody = await test.SendAsync(HttpMethod.Post, $"/api/orders/{order.Id}/refunds", null, Guid.NewGuid().ToString());

        Assert.Equal(["amount", "date", "receivedBy"], await ErrorKeysAsync(invalidPayment));
        Assert.Equal(["amount"], await ErrorKeysAsync(negative));
        Assert.Equal(["amount", "kind", "method", "reason"], await ErrorKeysAsync(invalidRefund));
        Assert.Equal(["amount", "version"], await ErrorKeysAsync(invalidEdit));
        await AssertProblemAsync(withoutKey, HttpStatusCode.BadRequest, OrderProblems.IdempotencyKeyRequiredCode);
        Assert.Equal(["amount", "date", "kind", "method", "reason"], await ErrorKeysAsync(emptyBody));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Refunds"));
    }

    [Fact]
    public async Task Today_follows_Europe_Warsaw_time()
    {
        // 23:30 UTC 5.11 to już 6.11 w Warszawie; data sprzed daty zamówienia też jest przyjmowana.
        await using var test = await OrdersTest.CreateAsync(sqlServer, now: new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero));
        var order = await test.CreateOrderAsync(Order("1000.00"));

        await AddPaymentAsync(test, order.Id, "100.00", date: "2026-11-06");
        await AddPaymentAsync(test, order.Id, "100.00", date: "2026-01-02");
        using var tomorrow = await SendPaymentAsync(test, order.Id, Payment("100.00", date: "2026-11-07"));

        Assert.Equal(["date"], await ErrorKeysAsync(tomorrow));
        Assert.Equal(2, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));
    }

    [Fact]
    public async Task Entry_lifecycle_writes_created_updated_deleted_audit()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "300.00", date: "2026-11-02", method: "cash", receivedBy: "Anna Przykładowa");
        var unchanged = await EditPaymentAsync(test, order.Id, payment.Id, payment.Version, " 300 ", date: "2026-11-02", method: "cash", receivedBy: " Anna Przykładowa ");
        var edited = await EditPaymentAsync(test, order.Id, payment.Id, payment.Version, "350.00", date: "2026-11-03", method: "transfer");
        var refund = await AddRefundAsync(test, order.Id, "50.00", kind: "moneyReturn");
        var refundEdited = await EditRefundAsync(test, order.Id, refund.Id, refund.Version, "50.00", kind: "salesReturn");
        await DeleteRefundAsync(test, order.Id, refund.Id, refundEdited);
        await DeletePaymentAsync(test, order.Id, payment.Id, edited);

        Assert.Equal(payment.Version, unchanged);
        var rows = await EntryAuditRowsAsync(test, order.Id);
        Assert.Equal(
            [("Payment", "Created"), ("Payment", "Updated"), ("Refund", "Created"), ("Refund", "Updated"), ("Refund", "Deleted"), ("Payment", "Deleted")],
            rows.Select(r => (r.Type, r.Operation)));
        Assert.All(rows, r => Assert.Equal("Account", r.AuthorKind));
        Assert.All(rows, r => Assert.Equal(("Order", order.Id), (r.ParentType, r.ParentId)));
        Assert.All(rows.Where(r => r.Type == "Payment"), r => Assert.Equal((payment.Id, $"Wpłata do {order.Number}"), (r.Id, r.Label)));
        Assert.All(rows.Where(r => r.Type == "Refund"), r => Assert.Equal((refund.Id, $"Zwrot do {order.Number}"), (r.Id, r.Label)));
        Assert.Equal(new DateTime(2026, 11, 2), rows[0].BusinessDate);
        Assert.Equal(new DateTime(2026, 11, 3), rows[1].BusinessDate);

        Assert.Equal(
            [("amount", null, "300.00", false), ("date", null, "2026-11-02", false), ("method", null, "cash", false), ("receivedBy", null, "Anna Przykładowa", true)],
            Changes(rows[0]));
        Assert.Equal(
            [("amount", "300.00", "350.00", false), ("date", "2026-11-02", "2026-11-03", false), ("method", "cash", "transfer", false), ("receivedBy", "Anna Przykładowa", null, true)],
            Changes(rows[1]));
        Assert.Equal([("kind", "moneyReturn", "salesReturn", false)], Changes(rows[3]));
        Assert.Equal(
            [("kind", "salesReturn", null, false), ("amount", "50.00", null, false), ("date", Today, null, false), ("method", "transfer", null, false), ("reason", "Zwrot pieniędzy testowy", null, false)],
            Changes(rows[4]));
        Assert.Equal(
            [("amount", "350.00", null, false), ("date", "2026-11-03", null, false), ("method", "transfer", null, false)],
            Changes(rows[5]));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Refunds"));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_entry_changes()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "1000.00");
        var extra = await AddPaymentAsync(test, order.Id, "200.00");
        var refund = await AddRefundAsync(test, order.Id, "100.00");
        // WITH NOCHECK: istniejące wpisy zostają, odrzucany jest każdy nowy.
        await test.ExecuteSqlAsync("ALTER TABLE reporting.AuditEntries WITH NOCHECK ADD CONSTRAINT CK_Test_RejectAll CHECK (ObjectLabel = N'nigdy');");

        using var addPayment = await SendPaymentAsync(test, order.Id, Payment("10.00"));
        using var addRefund = await SendRefundAsync(test, order.Id, Refund("10.00"));
        using var editPayment = await test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}/payments/{payment.Id}", Payment("900.00", version: payment.Version));
        using var editRefund = await test.SendAsync(HttpMethod.Put, $"/api/orders/{order.Id}/refunds/{refund.Id}", Refund("200.00", version: refund.Version));
        using var deletePayment = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{order.Id}/payments/{extra.Id}", new { version = extra.Version });
        using var deleteRefund = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{order.Id}/refunds/{refund.Id}", new { version = refund.Version });

        foreach (var response in new[] { addPayment, addRefund, editPayment, editRefund, deletePayment, deleteRefund })
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        var details = await test.GetJsonAsync($"/api/orders/{order.Id}");
        Assert.Equal(
            [("1000.00", payment.Version), ("200.00", extra.Version)],
            details.GetProperty("payments").EnumerateArray()
                .Select(p => (p.GetProperty("amount").GetString(), p.GetProperty("version").GetString()))
                .OrderByDescending(p => p.Item1!.Length));
        var refundJson = Assert.Single(details.GetProperty("refunds").EnumerateArray());
        Assert.Equal(("100.00", refund.Version), (refundJson.GetProperty("amount").GetString(), refundJson.GetProperty("version").GetString()));
        Assert.Equal(3, (await EntryAuditRowsAsync(test, order.Id)).Count);
    }

    [Fact]
    public async Task List_payment_columns_and_filter_match_details()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        // Wszystkie progi i przypadki brzegowe (N5).
        var noEntries = await test.CreateOrderAsync(Order("1000.00"));
        var zeroValue = await test.CreateOrderAsync(Order("0"));
        var zeroValuePaid = await test.CreateOrderAsync(Order("0"));
        await AddPaymentAsync(test, zeroValuePaid.Id, "10.00");
        var partial = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, partial.Id, "999.99");
        var exact = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, exact.Id, "1000.00");
        var over = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, over.Id, "1500.00");
        var refundedToZero = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, refundedToZero.Id, "400.00");
        await AddRefundAsync(test, refundedToZero.Id, "400.00");
        var paidAfterRefund = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, paidAfterRefund.Id, "1200.00");
        await AddRefundAsync(test, paidAfterRefund.Id, "200.00");
        var cancelledEmpty = await test.CreateOrderAsync(Order("1000.00"));
        await test.StatusActionAsync(cancelledEmpty.Id, "cancel", cancelledEmpty.Version);
        var cancelledPaid = await test.CreateOrderAsync(Order("1000.00"));
        await AddPaymentAsync(test, cancelledPaid.Id, "500.00");
        await test.StatusActionAsync(cancelledPaid.Id, "cancel", cancelledPaid.Version);
        var shop = await test.SeedShopOrderAsync();

        var expected = new Dictionary<Guid, string?>
        {
            [noEntries.Id] = "unpaid",
            [zeroValue.Id] = "paid",
            [zeroValuePaid.Id] = "paid",
            [partial.Id] = "partiallyPaid",
            [exact.Id] = "paid",
            [over.Id] = "paid",
            [refundedToZero.Id] = "unpaid",
            [paidAfterRefund.Id] = "paid",
            [cancelledEmpty.Id] = null,
            [cancelledPaid.Id] = null,
            [shop] = "unpaid",
        };

        var list = (await test.GetJsonAsync("/api/orders?pageSize=100")).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(expected.Count, list.Length);
        foreach (var item in list)
        {
            var id = item.GetProperty("id").GetGuid();
            var details = (await test.GetJsonAsync($"/api/orders/{id}")).GetProperty("payment");
            Assert.Equal(details.GetProperty("paid").GetString(), item.GetProperty("paid").GetString());
            Assert.Equal(details.GetProperty("state").GetString(), item.GetProperty("paymentState").GetString());
            Assert.Equal(expected[id], item.GetProperty("paymentState").GetString());
        }

        foreach (var state in new[] { "unpaid", "partiallyPaid", "paid" })
        {
            Assert.Equal(
                expected.Where(e => e.Value == state).Select(e => e.Key).ToHashSet(),
                (await test.IdsAsync($"/api/orders?pageSize=100&paymentState={state}")).ToHashSet());
        }

        Assert.Equal(
            new HashSet<Guid> { cancelledEmpty.Id, cancelledPaid.Id },
            (await test.IdsAsync("/api/orders?paymentState=none")).ToHashSet());
        Assert.Equal(
            expected.Where(e => e.Value is null or "partiallyPaid").Select(e => e.Key).ToHashSet(),
            (await test.IdsAsync("/api/orders?paymentState=partiallyPaid&paymentState=none")).ToHashSet());
        Assert.Equal(
            [partial.Id],
            await test.IdsAsync("/api/orders?paymentState=partiallyPaid&source=manual&dateFrom=2026-10-28&search=Testowy&status=inProgress"));
        Assert.Equal("999.99", list.Single(i => i.GetProperty("id").GetGuid() == partial.Id).GetProperty("paid").GetString());

        using var invalid = await test.SendAsync(HttpMethod.Get, "/api/orders?paymentState=overpaid");
        Assert.Equal(["paymentState"], await ErrorKeysAsync(invalid));
    }

    [Fact]
    public async Task Payment_routes_require_session()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "300.00");
        var refund = await AddRefundAsync(test, order.Id, "100.00");
        using var anonymous = AuthClient.Create(test.Factory);
        (await anonymous.GetCsrfAsync()).Dispose();

        foreach (var (method, path, body) in Routes(order.Id, payment, refund))
        {
            using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-XSRF-TOKEN", anonymous.XsrfToken);
            request.Headers.Add(OrdersTest.IdempotencyKeyHeader, Guid.NewGuid().ToString());
            using var response = await anonymous.Http.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} → {response.StatusCode}");
        }

        Assert.Equal(2, (await EntryAuditRowsAsync(test, order.Id)).Count);
    }

    [Fact]
    public async Task Payment_writes_require_xsrf()
    {
        await using var test = await OrdersTest.CreateAsync(sqlServer);
        var order = await test.CreateOrderAsync(Order("1000.00"));
        var payment = await AddPaymentAsync(test, order.Id, "300.00");
        var refund = await AddRefundAsync(test, order.Id, "100.00");

        foreach (var (method, path, body) in Routes(order.Id, payment, refund))
        {
            using var response = await test.SendAsync(method, path, body, Guid.NewGuid().ToString(), withCsrf: false);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(AuthProblems.CsrfInvalidCode, (await Json(response)).GetProperty("code").GetString());
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Payments"));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM orders.Refunds"));
        Assert.Equal(2, (await EntryAuditRowsAsync(test, order.Id)).Count);
    }

    private static (HttpMethod Method, string Path, object Body)[] Routes(Guid orderId, CreatedEntry payment, CreatedEntry refund) =>
    [
        (HttpMethod.Post, $"/api/orders/{orderId}/payments", Payment("1.00")),
        (HttpMethod.Put, $"/api/orders/{orderId}/payments/{payment.Id}", Payment("2.00", version: payment.Version)),
        (HttpMethod.Delete, $"/api/orders/{orderId}/payments/{payment.Id}", new { version = payment.Version }),
        (HttpMethod.Post, $"/api/orders/{orderId}/refunds", Refund("1.00")),
        (HttpMethod.Put, $"/api/orders/{orderId}/refunds/{refund.Id}", Refund("2.00", version: refund.Version)),
        (HttpMethod.Delete, $"/api/orders/{orderId}/refunds/{refund.Id}", new { version = refund.Version }),
    ];

    private static object Order(string value, string? comment = null, string? version = null) => new
    {
        orderDate = "2026-10-28",
        channel = "olx",
        value,
        fulfillmentStatus = version is null ? null : "inProgress",
        customer = new { kind = "person", name = "Jan Testowy" },
        operational = new { suppliers = Array.Empty<string>(), zoReferences = Array.Empty<string>(), hasPhotos = false, hasReview = false },
        comment,
        version,
    };

    private static object Payment(string amount, string date = Today, string method = "transfer", string? receivedBy = null, string? version = null) =>
        new { amount, date, method, receivedBy, version };

    private static object Refund(
        string amount,
        string kind = "moneyReturn",
        string date = Today,
        string method = "transfer",
        string reason = "Zwrot pieniędzy testowy",
        string? version = null) =>
        new { kind, amount, date, method, reason, version };

    private static Task<HttpResponseMessage> SendPaymentAsync(OrdersTest test, Guid orderId, object body, Guid? key = null) =>
        test.SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/payments", body, (key ?? Guid.NewGuid()).ToString());

    private static Task<HttpResponseMessage> SendRefundAsync(OrdersTest test, Guid orderId, object body, Guid? key = null) =>
        test.SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/refunds", body, (key ?? Guid.NewGuid()).ToString());

    private static async Task<CreatedEntry> AddPaymentAsync(
        OrdersTest test, Guid orderId, string amount, string date = Today, string method = "transfer", string? receivedBy = null, Guid? key = null)
    {
        using var response = await SendPaymentAsync(test, orderId, Payment(amount, date, method, receivedBy), key);
        return await CreatedAsync(response);
    }

    private static async Task<CreatedEntry> AddRefundAsync(
        OrdersTest test, Guid orderId, string amount, string kind = "moneyReturn", string method = "transfer", string reason = "Zwrot pieniędzy testowy")
    {
        using var response = await SendRefundAsync(test, orderId, Refund(amount, kind, Today, method, reason));
        return await CreatedAsync(response);
    }

    private static async Task<string> EditPaymentAsync(
        OrdersTest test, Guid orderId, Guid entryId, string version, string amount, string date = Today, string method = "transfer", string? receivedBy = null)
    {
        using var response = await test.SendAsync(
            HttpMethod.Put, $"/api/orders/{orderId}/payments/{entryId}", Payment(amount, date, method, receivedBy, version));
        return await VersionAsync(response);
    }

    private static async Task<string> EditRefundAsync(OrdersTest test, Guid orderId, Guid entryId, string version, string amount, string kind = "moneyReturn")
    {
        using var response = await test.SendAsync(HttpMethod.Put, $"/api/orders/{orderId}/refunds/{entryId}", Refund(amount, kind, version: version));
        return await VersionAsync(response);
    }

    private static async Task DeletePaymentAsync(OrdersTest test, Guid orderId, Guid entryId, string version)
    {
        using var response = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}/payments/{entryId}", new { version });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task DeleteRefundAsync(OrdersTest test, Guid orderId, Guid entryId, string version)
    {
        using var response = await test.SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}/refunds/{entryId}", new { version });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<CreatedEntry> CreatedAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var json = await Json(response);
        return new CreatedEntry(json.GetProperty("id").GetGuid(), json.GetProperty("version").GetString()!);
    }

    private static async Task<string> VersionAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await Json(response)).GetProperty("version").GetString()!;
    }

    private static async Task<JsonElement> PaymentAsync(OrdersTest test, Guid orderId) =>
        (await test.GetJsonAsync($"/api/orders/{orderId}")).GetProperty("payment");

    private static (string? Paid, string? State, string? AmountDue, string? Overpayment, string? ToRefund) Summary(JsonElement payment) =>
    (
        payment.GetProperty("paid").GetString(),
        payment.GetProperty("state").GetString(),
        payment.GetProperty("amountDue").GetString(),
        payment.GetProperty("overpayment").GetString(),
        payment.GetProperty("toRefund").GetString());

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
    }

    private static async Task<string[]> ErrorKeysAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        return [.. (await Json(response)).GetProperty("errors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await AuthClient.ProblemAsync(response);

    private static Task<IReadOnlyList<EntryAuditRow>> EntryAuditRowsAsync(OrdersTest test, Guid orderId) =>
        test.RowsAsync(
            "SELECT ObjectType, ObjectId, Operation, AuthorKind, ObjectLabel, ObjectBusinessDate, ParentObjectType, ParentObjectId, Changes "
            + $"FROM reporting.AuditEntries WHERE ParentObjectId = '{orderId}' AND ObjectType IN (N'Payment', N'Refund') ORDER BY Id",
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

    private static IReadOnlyList<(string Field, string? Old, string? New, bool Personal)> Changes(EntryAuditRow row) =>
    [
        .. JsonDocument.Parse(row.Changes).RootElement.EnumerateArray().Select(c => (
            c.GetProperty("field").GetString()!,
            c.GetProperty("old").GetString(),
            c.GetProperty("new").GetString(),
            c.GetProperty("personal").GetBoolean())),
    ];

    private sealed record CreatedEntry(Guid Id, string Version);

    private sealed record EntryAuditRow(
        string Type,
        Guid Id,
        string Operation,
        string AuthorKind,
        string Label,
        DateTime BusinessDate,
        string ParentType,
        Guid ParentId,
        string Changes);
}
