using System.Text;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Shared.Application.ShopOrders;

namespace StoneFlow.Integrations.Tests.WooCommerce;

/// <summary>SF-12: podpis i mapowanie treści webhooka (fikcyjne dane).</summary>
public sealed class WooCommercePayloadTests
{
    private const string Secret = "sekret-testowy-1234567890";

    internal const string FullOrder = """
        {
          "id": 1001,
          "status": "processing",
          "currency": "PLN",
          "date_created_gmt": "2026-12-31T23:30:00",
          "date_modified_gmt": "2027-01-01T08:15:00",
          "total": "1234.50",
          "billing": {
            "first_name": "Jan", "last_name": "Testowy", "company": "",
            "address_1": "ul. Przykładowa 1", "address_2": "m. 2",
            "postcode": "00-950", "city": "Warszawa",
            "email": "jan.testowy@example.com", "phone": "500600700"
          },
          "meta_data": [ { "id": 1, "key": "_billing_billing_nip", "value": "PL 123-456-32-18" } ],
          "refunds": [ { "id": 5, "total": "-100.00" }, { "id": 6, "total": "-23.45" } ]
        }
        """;

    private static PayloadReadResult Read(string json, ShopOrderTopic topic = ShopOrderTopic.Updated) =>
        WooCommerceOrderPayload.Read(topic, Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Signature_matches_hmac_of_raw_bytes()
    {
        var body = Encoding.UTF8.GetBytes(FullOrder);

        Assert.True(WebhookSignature.IsValid(body, WebhookSignature.Sign(body, Secret), Secret));
        Assert.False(WebhookSignature.IsValid(body, WebhookSignature.Sign(body, "inny-sekret-1234567"), Secret));
        Assert.False(WebhookSignature.IsValid([.. body, (byte)' '], WebhookSignature.Sign(body, Secret), Secret));
        Assert.False(WebhookSignature.IsValid(body, "nie-base64", Secret));
        Assert.False(WebhookSignature.IsValid(body, null, Secret));
    }

    [Fact]
    public void Full_order_is_mapped()
    {
        var snapshot = Read(FullOrder).Snapshot!;

        Assert.Equal("1001", snapshot.ExternalId);
        Assert.Equal("processing", snapshot.Status);
        Assert.Equal(1234.50m, snapshot.Value);
        Assert.Equal(123.45m, snapshot.RefundedAmount);
        Assert.Equal(new DateTime(2027, 1, 1, 8, 15, 0, DateTimeKind.Utc), snapshot.ModifiedAtUtc);
        Assert.Equal(new DateOnly(2027, 1, 1), snapshot.OrderDate); // 23:30 UTC 31.12 = 00:30 w Warszawie
        var customer = snapshot.Customer!;
        Assert.Equal(ShopCustomerKind.Person, customer.Kind);
        Assert.Equal("Jan Testowy", customer.Name);
        Assert.Equal("ul. Przykładowa 1 m. 2", customer.Street);
        Assert.Equal("1234563218", customer.TaxId);
        Assert.Equal("PLN", Read(FullOrder).Currency);
    }

    [Fact]
    public void Company_comes_from_billing_company() =>
        Assert.Equal(
            ShopCustomerKind.Company,
            Read(FullOrder.Replace("\"company\": \"\"", "\"company\": \"Firma Testowa\"", StringComparison.Ordinal)).Snapshot!.Customer!.Kind);

    [Fact]
    public void Deletion_needs_only_id()
    {
        var snapshot = Read("""{ "id": 77 }""", ShopOrderTopic.Deleted).Snapshot!;

        Assert.Equal("77", snapshot.ExternalId);
        Assert.Equal(ShopOrderTopic.Deleted, snapshot.Topic);
    }

    [Theory]
    [InlineData("nie json")]
    [InlineData("""{ "status": "processing" }""")]
    [InlineData("""{ "id": 1, "status": "checkout-draft", "date_created_gmt": "", "date_modified_gmt": "2026-11-05T10:00:00", "total": "0.00" }""")]
    [InlineData("""{ "id": 1, "status": "processing", "date_created_gmt": "2026-11-05T10:00:00", "date_modified_gmt": "2026-11-05T10:00:00", "total": "10.555" }""")]
    [InlineData("""{ "id": 1, "status": "processing", "date_created_gmt": "2026-11-05T10:00:00", "date_modified_gmt": "2026-11-05T10:00:00", "total": "-5.00" }""")]
    public void Unreadable_payloads_have_no_snapshot(string json)
    {
        var result = Read(json);

        Assert.Null(result.Snapshot);
        Assert.NotNull(result.Problem);
        Assert.DoesNotContain("Testowy", result.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PL1234563218", "1234563218")]
    [InlineData("123 456 32 18", "1234563218")]
    [InlineData("  ", null)]
    public void Tax_id_is_normalized(string input, string? expected) =>
        Assert.Equal(expected, WooCommerceOrderPayload.NormalizeTaxId(input));
}
