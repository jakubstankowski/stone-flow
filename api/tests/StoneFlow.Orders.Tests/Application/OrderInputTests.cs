using StoneFlow.Orders.Application;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Tests.Application;

public sealed class OrderInputTests
{
    private static OrderInput Valid() => new(
        "2026-10-28",
        "olx",
        "1234.50",
        null,
        new CustomerInput("person", "Jan Testowy", "500 600 700", "jan.testowy@example.com", "ul. Przykładowa 1", "00-950", "Warszawa", "1234563218"),
        new OperationalInput(["Dostawca A", "Dostawca B"], ["ZO-1"], "FV/1/2026", true, false, "Uwagi"),
        "Komentarz");

    private static (OrderContent? Content, IDictionary<string, string[]> Errors) Create(OrderInput input)
    {
        var errors = new FieldErrors();
        var content = input.ValidateForCreate(errors);
        return (content, errors.ToDictionary());
    }

    [Fact]
    public void Valid_input_is_normalized()
    {
        var input = Valid() with
        {
            Value = " 0 ",
            Customer = new CustomerInput(" company ", "  Firma Testowa  ", " ", null, "", null, null, "123-456-32 18"),
            Operational = new OperationalInput([" Dostawca A ", "", null, "Dostawca B"], null, "  ", false, false, null),
            Comment = "   ",
        };

        var (content, errors) = Create(input);

        Assert.Empty(errors);
        Assert.NotNull(content);
        Assert.Equal(0m, content.Value);
        Assert.Equal(FulfillmentStatus.InProgress, content.FulfillmentStatus);
        Assert.Equal(new Customer(CustomerKind.Company, "Firma Testowa", null, null, null, null, null, "1234563218"), content.Customer);
        Assert.Equal(["Dostawca A", "Dostawca B"], content.Operational.Suppliers);
        Assert.Empty(content.Operational.ZoReferences);
        Assert.Null(content.Operational.InvoiceNumber);
        Assert.Null(content.Comment);
    }

    [Fact]
    public void Missing_required_fields_are_reported_under_json_paths()
    {
        var (content, errors) = Create(new OrderInput(null, null, null, null, null, null, null));

        Assert.Null(content);
        Assert.Equal(
            ["channel", "customer.kind", "customer.name", "orderDate", "value"],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("1234.50")]
    [InlineData("9999999999999999.99")]
    public void Accepted_values(string value)
    {
        var (content, errors) = Create(Valid() with { Value = value });

        Assert.Empty(errors);
        Assert.Equal(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), content!.Value);
    }

    [Theory]
    [InlineData("", OrderMessages.ValueRequired)]
    [InlineData("-1", OrderMessages.ValueNegative)]
    [InlineData("-0.01", OrderMessages.ValueNegative)]
    [InlineData("1.234", OrderMessages.ValueScale)]
    [InlineData("1,23", OrderMessages.ValueInvalid)]
    [InlineData("abc", OrderMessages.ValueInvalid)]
    [InlineData("1e3", OrderMessages.ValueInvalid)]
    [InlineData("\u0661\u0662\u0663", OrderMessages.ValueInvalid)]
    [InlineData("12.\u0665\u0660", OrderMessages.ValueInvalid)]
    [InlineData("10000000000000000.00", OrderMessages.ValueTooLarge)]
    public void Rejected_values(string value, string message)
    {
        var (_, errors) = Create(Valid() with { Value = value });

        Assert.Equal([message], errors["value"]);
    }

    [Theory]
    [InlineData("1234563219")]
    [InlineData("123456321")]
    [InlineData("12345632AB")]
    public void Invalid_tax_id_is_rejected(string taxId)
    {
        var (_, errors) = Create(Valid() with { Customer = Valid().Customer! with { TaxId = taxId } });

        Assert.Equal([OrderMessages.TaxIdInvalid], errors["customer.taxId"]);
    }

    [Theory]
    [InlineData("postalCode", "00950", "customer.postalCode")]
    [InlineData("postalCode", "\u0660\u0660-\u0669\u0665\u0660", "customer.postalCode")]
    [InlineData("email", "jan.testowy", "customer.email")]
    [InlineData("name", "   ", "customer.name")]
    [InlineData("kind", "osoba", "customer.kind")]
    public void Invalid_customer_fields_are_rejected(string field, string value, string key)
    {
        var customer = Valid().Customer!;
        customer = field switch
        {
            "postalCode" => customer with { PostalCode = value },
            "email" => customer with { Email = value },
            "name" => customer with { Name = value },
            _ => customer with { Kind = value },
        };

        var (_, errors) = Create(Valid() with { Customer = customer });

        Assert.Equal([key], errors.Keys);
    }

    [Fact]
    public void Text_length_limits_are_checked()
    {
        var input = Valid() with
        {
            Customer = Valid().Customer! with { Name = new string('a', 201), Phone = new string('1', 33), City = new string('c', 101), Street = new string('s', 201) },
            Operational = new OperationalInput([], [], new string('f', 65), false, false, new string('n', 2001)),
            Comment = new string('k', 2001),
        };

        var (_, errors) = Create(input);

        Assert.Equal(
            ["comment", "customer.city", "customer.name", "customer.phone", "customer.street", "operational.invoiceNumber", "operational.notes"],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void List_limits_use_item_index_keys()
    {
        var suppliers = Enumerable.Range(1, 11).Select(i => (string?)$"Dostawca {i}").ToList();
        suppliers[1] = new string('d', 101);
        var input = Valid() with
        {
            Operational = new OperationalInput(suppliers, ["ZO-1", new string('z', 65)], null, false, false, null),
        };

        var (_, errors) = Create(input);

        Assert.Equal(
            ["operational.suppliers", "operational.suppliers[1]", "operational.zoReferences[1]"],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("channel", "allegro", "channel")]
    [InlineData("status", "done", "fulfillmentStatus")]
    [InlineData("status", "cancelled", "fulfillmentStatus")]
    [InlineData("status", "InProgress", "fulfillmentStatus")]
    public void Unknown_codes_and_cancelled_status_are_rejected_on_create(string field, string value, string key)
    {
        var input = field == "channel" ? Valid() with { Channel = value } : Valid() with { FulfillmentStatus = value };

        var (_, errors) = Create(input);

        Assert.Equal([key], errors.Keys);
    }

    [Fact]
    public void Hash_ignores_whitespace_tax_id_format_and_explicit_default_status()
    {
        var baseline = Create(Valid()).Content!;
        var spaced = Create(Valid() with
        {
            Value = "1234.5",
            FulfillmentStatus = "inProgress",
            Customer = Valid().Customer! with { Name = "  Jan Testowy ", TaxId = "123-456-32-18" },
            Operational = Valid().Operational! with { Suppliers = [" Dostawca A", "", "Dostawca B "] },
        }).Content!;
        var different = Create(Valid() with { Value = "1234.51" }).Content!;
        var reordered = Create(Valid() with { Operational = Valid().Operational! with { Suppliers = ["Dostawca B", "Dostawca A"] } }).Content!;

        var hash = OrderInput.ComputeRequestHash(baseline);

        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, OrderInput.ComputeRequestHash(spaced));
        Assert.NotEqual(hash, OrderInput.ComputeRequestHash(different));
        Assert.NotEqual(hash, OrderInput.ComputeRequestHash(reordered));
    }

    [Fact]
    public void Editable_fields_require_a_known_status_and_accept_cancelled()
    {
        var missing = new FieldErrors();
        var cancelled = new FieldErrors();

        Assert.Null((Valid() with { FulfillmentStatus = null }).ValidateEditable(missing));
        var fields = (Valid() with { FulfillmentStatus = "cancelled" }).ValidateEditable(cancelled);

        Assert.Equal([OrderMessages.StatusRequired], missing.ToDictionary()["fulfillmentStatus"]);
        Assert.True(cancelled.IsEmpty);
        Assert.Equal(FulfillmentStatus.Cancelled, fields!.FulfillmentStatus);
    }

    [Fact]
    public void Shop_fields_are_read_without_validation_and_unreadable_ones_are_listed()
    {
        var reading = (Valid() with { Value = "-5", Channel = "allegro", OrderDate = null }).ReadShopFields();

        Assert.Null(reading.OrderDate);
        Assert.Null(reading.Value);
        Assert.Null(reading.Channel);
        Assert.Equal("1234563218", reading.Customer?.TaxId);
        Assert.Equal([ShopOwnedField.Value, ShopOwnedField.Channel], reading.Unreadable);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("nie-base64", false)]
    [InlineData("AAAAAAAAB9E=", true)]
    [InlineData("AAAAAAAAAAAAB9E=", false)]
    public void Version_is_eight_bytes_in_base64(string? text, bool valid) =>
        Assert.Equal(valid, OrderVersion.Parse(text) is not null);
}
