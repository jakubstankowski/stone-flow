using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Tests.Domain;

/// <summary>SF-22: początek okresu retencji i dostępność akcji anonimizacji.</summary>
public sealed class CustomerAnonymizationTests
{
    [Fact]
    public void Retention_starts_at_order_date_without_entries_and_later_changes()
    {
        var orderDate = new DateOnly(2021, 3, 15);

        Assert.Equal(orderDate, CustomerAnonymization.RetentionStart(orderDate, null, orderDate));
    }

    [Fact]
    public void Later_entry_date_moves_the_start()
    {
        var start = CustomerAnonymization.RetentionStart(new DateOnly(2020, 5, 1), new DateOnly(2022, 6, 10), new DateOnly(2020, 5, 1));

        Assert.Equal(new DateOnly(2022, 6, 10), start);
    }

    [Fact]
    public void Later_change_moves_the_start_even_when_entry_date_is_old()
    {
        var today = new DateOnly(2026, 11, 13);

        var start = CustomerAnonymization.RetentionStart(new DateOnly(2020, 5, 1), new DateOnly(2020, 5, 2), today);

        Assert.Equal(today, start);
    }

    [Fact]
    public void Day_boundary_of_availability()
    {
        var start = new DateOnly(2021, 3, 15);

        var before = CustomerAnonymization.Evaluate(false, 60, start, new DateOnly(2026, 3, 14));
        var onDay = CustomerAnonymization.Evaluate(false, 60, start, new DateOnly(2026, 3, 15));

        Assert.Equal(new AnonymizationAvailability(AnonymizationStatus.RetentionPending, start, new DateOnly(2026, 3, 15)), before);
        Assert.Equal(new AnonymizationAvailability(AnonymizationStatus.Available, start, new DateOnly(2026, 3, 15)), onDay);
    }

    [Theory]
    [InlineData(2024, 29)]
    [InlineData(2025, 28)]
    public void Months_are_calendar_months(int year, int lastDayOfFebruary)
    {
        Assert.Equal(new DateOnly(year, 2, lastDayOfFebruary), CustomerAnonymization.AvailableFrom(new DateOnly(year, 1, 31), 1));
    }

    [Fact]
    public void Future_order_date_is_pending()
    {
        var today = new DateOnly(2026, 11, 13);
        var start = CustomerAnonymization.RetentionStart(new DateOnly(2026, 12, 1), null, today);

        var availability = CustomerAnonymization.Evaluate(false, 1, start, today);

        Assert.Equal(new DateOnly(2026, 12, 1), start);
        Assert.Equal(AnonymizationStatus.RetentionPending, availability.Status);
    }

    [Fact]
    public void Anonymized_comes_before_disabled_and_disabled_has_no_dates()
    {
        var today = new DateOnly(2026, 11, 13);

        Assert.Equal(
            new AnonymizationAvailability(AnonymizationStatus.Anonymized, null, null),
            CustomerAnonymization.Evaluate(true, null, null, today));
        Assert.Equal(
            new AnonymizationAvailability(AnonymizationStatus.Anonymized, null, null),
            CustomerAnonymization.Evaluate(true, 60, new DateOnly(2000, 1, 1), today));
        Assert.Equal(
            new AnonymizationAvailability(AnonymizationStatus.Disabled, null, null),
            CustomerAnonymization.Evaluate(false, null, new DateOnly(2000, 1, 1), today));
    }

    [Fact]
    public void Enabled_evaluation_requires_start_and_valid_months()
    {
        var today = new DateOnly(2026, 11, 13);

        Assert.Throws<ArgumentException>(() => CustomerAnonymization.Evaluate(false, 60, null, today));
        Assert.Throws<ArgumentOutOfRangeException>(() => CustomerAnonymization.AvailableFrom(today, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CustomerAnonymization.AvailableFrom(today, 1201));
        Assert.Equal(today.AddMonths(1200), CustomerAnonymization.AvailableFrom(today, 1200));

        var available = CustomerAnonymization.Evaluate(false, 1, new DateOnly(2000, 1, 1), today);
        Assert.Equal(AnonymizationStatus.Available, available.Status);
        Assert.NotNull(available.RetentionStartDate);
        Assert.NotNull(available.AvailableFrom);
    }
}
