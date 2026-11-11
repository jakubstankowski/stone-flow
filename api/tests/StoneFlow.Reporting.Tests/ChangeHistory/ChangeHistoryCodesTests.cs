using StoneFlow.Reporting.Application.ChangeHistory;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Reporting.Tests.ChangeHistory;

public sealed class ChangeHistoryCodesTests
{
    public static TheoryData<AuditObjectType> ObjectTypes => [.. Enum.GetValues<AuditObjectType>()];

    public static TheoryData<AuditOperation> Operations => [.. Enum.GetValues<AuditOperation>()];

    public static TheoryData<AuditAuthorKind> Authors => [.. Enum.GetValues<AuditAuthorKind>()];

    [Theory]
    [MemberData(nameof(ObjectTypes))]
    public void Every_object_type_has_a_code_that_parses_back(AuditObjectType type)
    {
        var code = ChangeHistoryCodes.Of(type);

        Assert.True(ChangeHistoryCodes.TryParse(code, out var parsed));
        Assert.Equal(type, parsed);
    }

    [Theory]
    [InlineData(AuditObjectType.Order, "order")]
    [InlineData(AuditObjectType.Payment, "payment")]
    [InlineData(AuditObjectType.Refund, "refund")]
    [InlineData(AuditObjectType.SalesCorrection, "salesCorrection")]
    [InlineData(AuditObjectType.Expense, "expense")]
    [InlineData(AuditObjectType.RecurringExpenseDefinition, "recurringExpenseDefinition")]
    [InlineData(AuditObjectType.OwnerWithdrawal, "ownerWithdrawal")]
    public void Object_type_codes_are_camel_case(AuditObjectType type, string code)
    {
        Assert.Equal(code, ChangeHistoryCodes.Of(type));
    }

    [Theory]
    [InlineData("Order")]
    [InlineData("ORDER")]
    [InlineData("salescorrection")]
    [InlineData("cost")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_or_differently_cased_object_type_codes_are_rejected(string? code)
    {
        Assert.False(ChangeHistoryCodes.TryParse(code, out _));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void Every_operation_has_a_code(AuditOperation operation)
    {
        Assert.False(string.IsNullOrEmpty(ChangeHistoryCodes.Of(operation)));
    }

    [Theory]
    [MemberData(nameof(Authors))]
    public void Every_author_kind_has_a_code(AuditAuthorKind author)
    {
        Assert.False(string.IsNullOrEmpty(ChangeHistoryCodes.Of(author)));
    }

    [Fact]
    public void Operation_and_author_codes_match_the_contract()
    {
        Assert.Equal(
            ["created", "updated", "deleted"],
            [ChangeHistoryCodes.Of(AuditOperation.Created), ChangeHistoryCodes.Of(AuditOperation.Updated), ChangeHistoryCodes.Of(AuditOperation.Deleted)]);
        Assert.Equal(
            ["account", "wooCommerceSync", "automatedJob"],
            [ChangeHistoryCodes.Of(AuditAuthorKind.Account), ChangeHistoryCodes.Of(AuditAuthorKind.WooCommerceSync), ChangeHistoryCodes.Of(AuditAuthorKind.AutomatedJob)]);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    public void Timestamp_is_iso_utc_with_z_and_round_trips(DateTimeKind kind)
    {
        var value = new DateTime(2026, 11, 5, 12, 0, 0, kind);

        var text = ChangeHistoryCodes.FormatTimestamp(value);

        Assert.Equal("2026-11-05T12:00:00.0000000Z", text);
        var parsed = DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), parsed);
    }

    [Fact]
    public void Dates_use_the_exact_iso_pattern()
    {
        Assert.Equal("2026-11-05", ChangeHistoryCodes.FormatDate(new DateOnly(2026, 11, 5)));
        Assert.True(ChangeHistoryCodes.TryParseDate("2026-11-05", out var date));
        Assert.Equal(new DateOnly(2026, 11, 5), date);
        Assert.False(ChangeHistoryCodes.TryParseDate("05.11.2026", out _));
        Assert.False(ChangeHistoryCodes.TryParseDate("2026-13-01", out _));
    }
}
