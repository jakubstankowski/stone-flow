using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Tests.Domain;

public sealed class SalesEligibilityTests
{
    public static TheoryData<FulfillmentStatus> ActiveStatuses() =>
        [FulfillmentStatus.InProgress, FulfillmentStatus.Completed, FulfillmentStatus.Problem];

    [Theory]
    [InlineData(null)] // zamówienie ręczne
    [InlineData("processing")]
    [InlineData("completed")]
    [InlineData("on-hold")]
    [InlineData("refunded")] // zwrot w sklepie to tylko informacja
    [InlineData("cancelled")] // anulowane w sklepie, przywrócone w ERP: decyduje status realizacji ERP
    [InlineData("trash")]
    [InlineData("custom-status")] // status spoza słownika
    [InlineData("Pending")] // porównanie dokładne, jak IsHiddenByDefault
    public void Counts_when_not_cancelled_in_erp_and_shop_status_is_not_hidden(string? shopStatus)
    {
        foreach (var fulfillment in ActiveStatuses())
        {
            Assert.True(SalesEligibility.CountsTowardSales(fulfillment, shopStatus));
        }
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("failed")]
    [InlineData("checkout-draft")]
    public void Hidden_shop_statuses_do_not_count(string shopStatus)
    {
        foreach (var fulfillment in ActiveStatuses())
        {
            Assert.False(SalesEligibility.CountsTowardSales(fulfillment, shopStatus));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("processing")]
    [InlineData("completed")]
    [InlineData("pending")]
    [InlineData("custom-status")]
    public void Cancelled_order_never_counts(string? shopStatus)
    {
        Assert.False(SalesEligibility.CountsTowardSales(FulfillmentStatus.Cancelled, shopStatus));
    }
}
