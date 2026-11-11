using System.Globalization;
using StoneFlow.Reporting.Application.Sales;
using StoneFlow.Reporting.Domain.Periods;

namespace StoneFlow.Reporting.Tests.Periods;

public sealed class ComparisonTests
{
    [Fact]
    public void Zero_base_has_no_basis()
    {
        Assert.Equal(Comparison.NoBasis, Comparison.Of(500m, 0m));
        Assert.Null(Comparison.NoBasis.Base);
        Assert.Null(Comparison.NoBasis.Difference);
        Assert.Null(Comparison.NoBasis.Percent);
    }

    [Fact]
    public void Difference_and_percent_against_positive_base()
    {
        var comparison = Comparison.Of(1234.50m, 1000m);

        Assert.Equal(ComparisonStatus.Available, comparison.Status);
        Assert.Equal(1000m, comparison.Base);
        Assert.Equal(234.50m, comparison.Difference);
        Assert.Equal(23.5m, comparison.Percent); // 23,45 → 23,5 (połową od zera)
    }

    [Fact]
    public void Negative_base_divides_by_its_absolute_value()
    {
        var comparison = Comparison.Of(-500m, -1000m);

        Assert.Equal(500m, comparison.Difference);
        Assert.Equal(50.0m, comparison.Percent);
    }

    [Theory]
    [InlineData("10005", "10000", "0.1")] // +0,05% → +0,1
    [InlineData("9995", "10000", "-0.1")] // −0,05% → −0,1
    [InlineData("10004", "10000", "0.0")] // +0,04% → 0,0
    [InlineData("9996", "10000", "0.0")] // −0,04% → 0,0 bez znaku
    [InlineData("10000", "10000", "0.0")]
    public void Percent_rounds_half_away_from_zero_to_one_decimal(string current, string @base, string expected)
    {
        var comparison = Comparison.Of(decimal.Parse(current, CultureInfo.InvariantCulture), decimal.Parse(@base, CultureInfo.InvariantCulture));

        Assert.Equal(expected, SalesReportCodes.FormatPercent(comparison.Percent!.Value));
    }

    [Fact]
    public void Integer_variant_uses_the_same_decimal_rule()
    {
        var comparison = Comparison.Of(12, 8);

        Assert.Equal(8m, comparison.Base);
        Assert.Equal(4m, comparison.Difference);
        Assert.Equal(50.0m, comparison.Percent);
        Assert.Equal(Comparison.NoBasis, Comparison.Of(3, 0));
        Assert.Equal(-33.3m, Comparison.Of(2, 3).Percent);
    }
}
