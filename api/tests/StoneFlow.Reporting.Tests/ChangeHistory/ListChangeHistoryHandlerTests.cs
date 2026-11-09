using System.Globalization;
using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Reporting.Application.ChangeHistory;
using StoneFlow.Reporting.Domain.Audit;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Reporting.Tests.ChangeHistory;

public sealed class ListChangeHistoryHandlerTests
{
    [Theory]
    [InlineData(null, "2026-11-30", "changedFrom")]
    [InlineData("2026-11-01", null, "changedTo")]
    [InlineData("  ", "2026-11-30", "changedFrom")]
    [InlineData("01.11.2026", "2026-11-30", "changedFrom")]
    [InlineData("2026-11-01", "2026-13-01", "changedTo")]
    [InlineData("2026-11-02", "2026-11-01", "changedFrom")]
    [InlineData("2026-11-01", "9999-12-31", "changedTo")]
    public async Task Change_period_is_required_valid_and_ordered(string? from, string? to, string key)
    {
        var (handler, reader) = Handler();

        var result = await handler.HandleAsync(new ListChangeHistoryQuery(from, to), TestContext.Current.CancellationToken);

        Assert.Null(result.Page);
        Assert.Equal([key], result.Errors!.ToDictionary().Keys);
        Assert.Empty(reader.Calls);
    }

    [Theory]
    [InlineData("2026-10-31", "2026-10-01", "businessFrom")]
    [InlineData("31.10.2026", null, "businessFrom")]
    [InlineData(null, "2026-10-32", "businessTo")]
    public async Task Business_date_range_is_validated(string? from, string? to, string key)
    {
        var (handler, reader) = Handler();

        var result = await handler.HandleAsync(Query(businessFrom: from, businessTo: to), TestContext.Current.CancellationToken);

        Assert.Equal([key], result.Errors!.ToDictionary().Keys);
        Assert.Empty(reader.Calls);
    }

    [Theory]
    [InlineData("2026-10-01", null)]
    [InlineData(null, "2026-10-31")]
    [InlineData("2026-10-15", "2026-10-15")]
    public async Task One_business_date_bound_is_enough(string? from, string? to)
    {
        var (handler, reader) = Handler();

        var result = await handler.HandleAsync(Query(businessFrom: from, businessTo: to), TestContext.Current.CancellationToken);

        Assert.NotNull(result.Page);
        var criteria = Assert.Single(reader.Calls);
        Assert.Equal(from is null ? null : DateOnly.Parse(from, CultureInfo.InvariantCulture), criteria.BusinessFrom);
        Assert.Equal(to is null ? null : DateOnly.Parse(to, CultureInfo.InvariantCulture), criteria.BusinessTo);
    }

    [Fact]
    public async Task Unknown_object_type_and_direction_are_rejected_once_per_key()
    {
        var (handler, reader) = Handler();

        var result = await handler.HandleAsync(
            Query(objectType: ["expense", "cost", "Order"], direction: "up"), TestContext.Current.CancellationToken);

        Assert.Equal(["direction", "objectType"], result.Errors!.ToDictionary().Keys.Order(StringComparer.Ordinal));
        Assert.Single(result.Errors.ToDictionary()["objectType"]);
        Assert.Empty(reader.Calls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task Page_must_be_an_integer_from_one(string page)
    {
        var (handler, _) = Handler();

        var result = await handler.HandleAsync(Query(page: page), TestContext.Current.CancellationToken);

        Assert.Equal(["page"], result.Errors!.ToDictionary().Keys);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("x")]
    public async Task Page_size_is_between_one_and_one_hundred(string pageSize)
    {
        var (handler, _) = Handler();

        var result = await handler.HandleAsync(Query(pageSize: pageSize), TestContext.Current.CancellationToken);

        Assert.Equal(["pageSize"], result.Errors!.ToDictionary().Keys);
    }

    [Fact]
    public async Task Defaults_are_all_types_newest_first_first_page_of_25()
    {
        var (handler, reader) = Handler();

        await handler.HandleAsync(new ListChangeHistoryQuery("2026-11-05", "2026-11-05"), TestContext.Current.CancellationToken);

        var criteria = Assert.Single(reader.Calls);
        Assert.Equal(new DateTime(2026, 11, 4, 23, 0, 0, DateTimeKind.Utc), criteria.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 5, 23, 0, 0, DateTimeKind.Utc), criteria.ToUtcExclusive);
        Assert.Null(criteria.BusinessFrom);
        Assert.Null(criteria.BusinessTo);
        Assert.Empty(criteria.ObjectTypes);
        Assert.True(criteria.Descending);
        Assert.Equal(1, criteria.Page);
        Assert.Equal(25, criteria.PageSize);
    }

    [Fact]
    public async Task Criteria_use_the_warsaw_period_types_without_duplicates_and_paging()
    {
        var (handler, reader) = Handler();

        await handler.HandleAsync(
            new ListChangeHistoryQuery(
                " 2026-10-25 ",
                "2026-10-31",
                "2026-10-01",
                "2026-10-31",
                ["expense", "payment", "expense", " "],
                "asc",
                "3",
                "100"),
            TestContext.Current.CancellationToken);

        var criteria = Assert.Single(reader.Calls);
        Assert.Equal(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc), criteria.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc), criteria.ToUtcExclusive);
        Assert.Equal(new DateOnly(2026, 10, 1), criteria.BusinessFrom);
        Assert.Equal(new DateOnly(2026, 10, 31), criteria.BusinessTo);
        Assert.Equal([AuditObjectType.Payment, AuditObjectType.Expense], criteria.ObjectTypes.Order());
        Assert.False(criteria.Descending);
        Assert.Equal(3, criteria.Page);
        Assert.Equal(100, criteria.PageSize);
    }

    [Fact]
    public async Task Reader_page_is_returned_unchanged()
    {
        var entry = new ChangeHistoryEntryDto(
            7,
            new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc),
            AuditAuthorKind.Account,
            AuditObjectType.Expense,
            Guid.NewGuid(),
            new DateOnly(2026, 10, 28),
            "Wydatek – Cięcie",
            null,
            null,
            true,
            AuditOperation.Deleted,
            [new AuditFieldChange("grossAmount", "1234.50", null, false)]);
        var page = new ChangeHistoryPageDto([entry], 1, 2, 25);
        var (handler, _) = Handler(page);

        var result = await handler.HandleAsync(Query(page: "2"), TestContext.Current.CancellationToken);

        Assert.Same(page, result.Page);
        Assert.Null(result.Errors);
    }

    private static ListChangeHistoryQuery Query(
        string? businessFrom = null,
        string? businessTo = null,
        IReadOnlyList<string>? objectType = null,
        string? direction = null,
        string? page = null,
        string? pageSize = null) =>
        new("2026-11-01", "2026-11-30", businessFrom, businessTo, objectType, direction, page, pageSize);

    private static (ListChangeHistoryHandler Handler, FakeReader Reader) Handler(ChangeHistoryPageDto? page = null)
    {
        var reader = new FakeReader(page);
        return (new ListChangeHistoryHandler(reader), reader);
    }

    private sealed class FakeReader(ChangeHistoryPageDto? page) : IChangeHistoryReader
    {
        public List<ChangeHistoryCriteria> Calls { get; } = [];

        public Task<ChangeHistoryPageDto> ListAsync(ChangeHistoryCriteria criteria, CancellationToken cancellationToken)
        {
            Calls.Add(criteria);
            return Task.FromResult(page ?? new ChangeHistoryPageDto([], 0, criteria.Page, criteria.PageSize));
        }
    }
}
