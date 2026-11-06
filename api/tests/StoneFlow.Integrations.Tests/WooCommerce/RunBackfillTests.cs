using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Integrations.Application.WooCommerce.Backfill;
using StoneFlow.Integrations.Domain.WooCommerce;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Tests.WooCommerce;

/// <summary>SF-13: przebieg uzupełniania z fałszywym klientem REST i portami w pamięci.</summary>
public sealed class RunBackfillTests
{
    private static readonly DateTime WindowEnd = BackfillHarness.Now.UtcDateTime - BackfillWindow.Margin;

    [Fact]
    public async Task All_pages_of_both_sets_are_applied_and_cursor_moves_to_window_end()
    {
        var harness = new BackfillHarness();
        harness.Client
            .Page(BackfillSet.Any, 3, 2, FakeRestClient.Json(1), FakeRestClient.Json(2))
            .Page(BackfillSet.Any, 3, 2, FakeRestClient.Json(3))
            .Page(BackfillSet.Trash, 1, 1, FakeRestClient.Json(4, status: "trash"));

        await harness.RunAsync();

        Assert.Equal(["1", "2", "3", "4"], harness.Sync.Applied.Select(s => s.ExternalId));
        Assert.All(harness.Sync.Applied, s => Assert.Equal(ShopOrderTopic.Updated, s.Topic));
        Assert.Equal(WindowEnd, harness.Store.Cursor);
        Assert.Equal(SyncRunStatus.Succeeded, harness.Run.Status);
        Assert.Equal(4, harness.Run.OrdersProcessed);
        Assert.Equal(0, harness.Run.Errors);

        var any = harness.Client.Requests.Where(r => r.Set == BackfillSet.Any).ToList();
        Assert.Equal([1, 2], any.Select(r => r.Page));
        Assert.All(any, r => Assert.Equal(WindowEnd, r.ModifiedBeforeUtc));
        Assert.All(any, r => Assert.Equal(new DateTime(2026, 11, 2, 23, 0, 0, DateTimeKind.Utc), r.ModifiedAfterUtc));
        var trash = Assert.Single(harness.Client.Requests, r => r.Set == BackfillSet.Trash);
        Assert.Null(trash.ModifiedAfterUtc);
        Assert.Null(trash.ModifiedBeforeUtc);
    }

    [Fact]
    public async Task Without_page_headers_a_short_page_ends_the_set()
    {
        var harness = new BackfillHarness();
        harness.Client.Page(BackfillSet.Any, null, null, FakeRestClient.Json(1));

        await harness.RunAsync();

        Assert.Single(harness.Client.Requests, r => r.Set == BackfillSet.Any);
        Assert.Equal(SyncRunStatus.Succeeded, harness.Run.Status);
    }

    [Fact]
    public async Task Run_starts_ten_minutes_before_cursor()
    {
        var harness = new BackfillHarness();
        harness.Store.Cursor = new DateTime(2026, 11, 5, 21, 50, 0, DateTimeKind.Utc);

        await harness.RunAsync();

        Assert.Equal(new DateTime(2026, 11, 5, 21, 40, 0, DateTimeKind.Utc), harness.Client.Requests[0].ModifiedAfterUtc);
    }

    [Fact]
    public async Task Changed_total_refetches_the_set_once()
    {
        var harness = new BackfillHarness();
        harness.Client
            .Page(BackfillSet.Any, 101, 2, FakeRestClient.Json(1))
            .Page(BackfillSet.Any, 102, 2, FakeRestClient.Json(2))
            .Page(BackfillSet.Any, 102, 2, FakeRestClient.Json(1))
            .Page(BackfillSet.Any, 102, 2, FakeRestClient.Json(2), FakeRestClient.Json(3));

        await harness.RunAsync();

        Assert.Equal(SyncRunStatus.Succeeded, harness.Run.Status);
        Assert.Equal(WindowEnd, harness.Store.Cursor);
        Assert.Equal(3, harness.Run.OrdersProcessed);
    }

    [Fact]
    public async Task Second_change_of_total_fails_the_run_and_keeps_cursor()
    {
        var harness = new BackfillHarness();
        var cursor = new DateTime(2026, 11, 5, 21, 50, 0, DateTimeKind.Utc);
        harness.Store.Cursor = cursor;
        harness.Client
            .Page(BackfillSet.Any, 101, 2, FakeRestClient.Json(1))
            .Page(BackfillSet.Any, 102, 2, FakeRestClient.Json(2))
            .Page(BackfillSet.Any, 102, 2, FakeRestClient.Json(1))
            .Page(BackfillSet.Any, 103, 2, FakeRestClient.Json(2));

        var exception = await Assert.ThrowsAsync<WooCommerceBackfillException>(() => harness.RunAsync());

        Assert.Equal("Lista zamówień zmieniała się w trakcie pobierania", exception.Message);
        Assert.Equal(SyncRunStatus.Failed, harness.Run.Status);
        Assert.Equal(exception.Message, harness.Run.Message);
        Assert.Equal(cursor, harness.Store.Cursor);
    }

    [Fact]
    public async Task Failed_page_fails_the_run_keeps_cursor_and_earlier_pages_stay_applied()
    {
        var harness = new BackfillHarness();
        harness.Client
            .Page(BackfillSet.Any, 300, 3, FakeRestClient.Json(1))
            .Page(BackfillSet.Any, 300, 3, FakeRestClient.Json(2))
            .Fail(BackfillSet.Any, "Sklep odpowiedział HTTP 500 na stronie 3");

        var exception = await Assert.ThrowsAsync<WooCommerceBackfillException>(() => harness.RunAsync());

        Assert.Equal("Sklep odpowiedział HTTP 500 na stronie 3", exception.Message);
        Assert.Null(harness.Store.Cursor);
        Assert.Equal(SyncRunStatus.Failed, harness.Run.Status);
        Assert.Equal(2, harness.Run.OrdersProcessed);
        Assert.Equal(["1", "2"], harness.Sync.Applied.Select(s => s.ExternalId));
        Assert.DoesNotContain(harness.Client.Requests, r => r.Set == BackfillSet.Trash);
    }

    [Fact]
    public async Task Unreadable_order_becomes_order_error_and_run_succeeds()
    {
        var harness = new BackfillHarness();
        harness.Client.Page(
            BackfillSet.Any,
            3,
            1,
            """{ "id": 7, "status": "processing", "date_modified_gmt": "2026-11-05T09:00:00" }""",
            """{ "status": "processing" }""",
            FakeRestClient.Json(8));

        await harness.RunAsync();

        Assert.Equal(SyncRunStatus.Succeeded, harness.Run.Status);
        Assert.Equal(1, harness.Run.Errors);
        Assert.Equal(1, harness.Run.Unidentified);
        var (error, failedModified) = Assert.Single(harness.Errors.Rows);
        Assert.Equal("7", error.ExternalId);
        Assert.Equal(OrderErrorSource.Backfill, error.Source);
        Assert.Equal(harness.RunId.ToString(), error.Context);
        Assert.Equal("Nieczytelna treść zamówienia: brak wymaganych pól zamówienia 7", error.Message);
        Assert.Equal(new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc), failedModified);
        Assert.Equal(["8"], harness.Sync.Applied.Select(s => s.ExternalId));
    }

    [Fact]
    public async Task Save_failure_is_recorded_without_exception_text_and_success_later_closes_it()
    {
        var harness = new BackfillHarness();
        harness.Sync.Result = _ => throw new InvalidOperationException("Jan Testowy, ul. Przykładowa 1");
        harness.Client.Page(BackfillSet.Any, 1, 1, FakeRestClient.Json(5));

        await harness.RunAsync();

        var error = Assert.Single(harness.Errors.Rows).View;
        Assert.Equal("Błąd zapisu zamówienia (InvalidOperationException)", error.Message);
        Assert.Equal(1, harness.Run.Errors);

        var next = new BackfillHarness();
        next.Errors.Rows.AddRange(harness.Errors.Rows);
        next.Client.Page(BackfillSet.Any, 1, 1, FakeRestClient.Json(5));
        await next.RunAsync();

        var closed = Assert.Single(next.Errors.Rows).View;
        Assert.Equal(OrderErrorStatus.Closed, closed.Status);
        Assert.Equal(OrderErrorSource.Backfill, closed.ClosedBy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Order_error_that_cannot_be_recorded_fails_the_run_and_keeps_cursor(bool saveFails)
    {
        var harness = new BackfillHarness();
        var cursor = new DateTime(2026, 11, 5, 21, 50, 0, DateTimeKind.Utc);
        harness.Store.Cursor = cursor;
        harness.Errors.FailRecording = true;
        if (saveFails)
        {
            harness.Sync.Result = _ => throw new InvalidOperationException("Zakleszczenie");
        }

        harness.Client.Page(
            BackfillSet.Any,
            2,
            1,
            saveFails ? FakeRestClient.Json(5) : """{ "id": 5, "status": "processing" }""",
            FakeRestClient.Json(6));

        var exception = await Assert.ThrowsAsync<WooCommerceBackfillException>(() => harness.RunAsync());

        Assert.Equal("Nie udało się zapisać historii błędu zamówienia 5", exception.Message);
        Assert.Equal(SyncRunStatus.Failed, harness.Run.Status);
        Assert.Equal(exception.Message, harness.Run.Message);
        Assert.Equal(cursor, harness.Store.Cursor);
        Assert.DoesNotContain(harness.Client.Requests, r => r.Set == BackfillSet.Trash);
    }

    [Fact]
    public async Task Run_marked_as_interrupted_before_success_does_not_move_cursor()
    {
        var harness = new BackfillHarness();
        harness.Client.Respond(BackfillSet.Any, (_, _) =>
        {
            // Kolejny przebieg (po utracie dzierżawy) oznaczył ten jako przerwany.
            harness.Store.Runs[harness.RunId] = harness.Run with { Status = SyncRunStatus.Failed };
            return new RestPageResult.Loaded([], 0, 0);
        });

        var exception = await Assert.ThrowsAsync<WooCommerceBackfillException>(() => harness.RunAsync());

        Assert.Equal(WooCommerceBackfill.NotRunningMessage, exception.Message);
        Assert.Null(harness.Store.Cursor);
    }

    [Fact]
    public async Task Retry_required_twice_is_an_order_error()
    {
        var harness = new BackfillHarness();
        harness.Sync.Result = _ => ShopOrderSyncResult.RetryRequired;
        harness.Client.Page(BackfillSet.Any, 1, 1, FakeRestClient.Json(5));

        await harness.RunAsync();

        Assert.Equal(OrderErrorStatus.Open, Assert.Single(harness.Errors.Rows).View.Status);
        Assert.Equal(SyncRunStatus.Succeeded, harness.Run.Status);
    }

    [Fact]
    public async Task Cancellation_writes_nothing_after_it()
    {
        var harness = new BackfillHarness();
        using var cancellation = new CancellationTokenSource();
        harness.Client.Respond(BackfillSet.Any, (_, _) =>
        {
            cancellation.Cancel();
            return new RestPageResult.Loaded([], 0, 0);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.RunAsync(cancellation.Token));

        Assert.Equal(SyncRunStatus.Running, harness.Run.Status);
        Assert.Null(harness.Store.Cursor);
    }

    [Fact]
    public async Task Next_run_marks_interrupted_run_as_failed()
    {
        var harness = new BackfillHarness();
        var interrupted = Guid.NewGuid();
        harness.Store.Runs[interrupted] = new SyncRunView(interrupted, SyncRunSource.Manual, SyncRunStatus.Running, DateTime.UtcNow, null, 0, 0, 0, null);

        await harness.RunAsync();

        Assert.Equal(SyncRunStatus.Failed, harness.Store.Runs[interrupted].Status);
        Assert.Equal(WooCommerceBackfill.AbandonedMessage, harness.Store.Runs[interrupted].Message);
    }

    [Fact]
    public async Task Without_rest_configuration_nothing_is_recorded()
    {
        var harness = new BackfillHarness(configured: false);

        await harness.RunAsync();

        Assert.Empty(harness.Store.Runs);
        Assert.Empty(harness.Client.Requests);
        Assert.Equal(1, harness.Events.NotConfiguredCount);
    }

    [Fact]
    public async Task Empty_window_succeeds_without_requests_and_keeps_cursor()
    {
        var harness = new BackfillHarness();
        harness.Store.Cursor = WindowEnd + BackfillWindow.Margin;

        await harness.RunAsync();

        Assert.Empty(harness.Client.Requests);
        Assert.Equal(SyncRunStatus.Succeeded, harness.Run.Status);
        Assert.Equal(WindowEnd + BackfillWindow.Margin, harness.Store.Cursor);
    }
}
