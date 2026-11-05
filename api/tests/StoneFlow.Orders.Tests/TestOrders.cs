using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Tests;

/// <summary>Fikcyjne dane zamówień do testów (bez danych identyfikujących).</summary>
internal static class TestOrders
{
    public static readonly DateTime CreatedAt = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Later = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    public static Customer Customer(string name = "Jan Testowy") =>
        new(CustomerKind.Person, name, "500 600 700", "jan.testowy@example.com", "ul. Przykładowa 1", "00-950", "Warszawa", "1234563218");

    public static OperationalData Operational(params string[] suppliers) =>
        new(suppliers, ["ZO-1"], null, HasPhotos: false, HasReview: true, Notes: null);

    public static OrderContent Content(decimal value = 1234.50m, FulfillmentStatus status = FulfillmentStatus.InProgress) =>
        new(new DateOnly(2026, 10, 28), OrderChannel.Olx, value, status, Customer(), Operational("Dostawca A", "Dostawca B"), "Pierwszy komentarz");

    public static Order Manual(FulfillmentStatus status = FulfillmentStatus.InProgress) =>
        Order.CreateManual(Guid.NewGuid(), "ZR-2026-0001", Content(status: status), Guid.NewGuid(), new byte[32], CreatedAt);

    public static Order FromShop() =>
        Order.CreateFromShop(Guid.NewGuid(), "ZR-2026-0002", "1001", "processing", Content() with { Channel = OrderChannel.Shop }, CreatedAt);

    /// <summary>Edycja przepisująca obecny stan zamówienia (bez zmian).</summary>
    public static OrderEdit SameAs(Order order) =>
        new(order.FulfillmentStatus, order.Operational, order.Comment, order.OrderDate, order.Channel, order.Value, order.Customer);
}
