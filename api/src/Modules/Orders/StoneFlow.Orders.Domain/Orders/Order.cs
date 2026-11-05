using System.Globalization;

namespace StoneFlow.Orders.Domain.Orders;

/// <summary>Treść zamówienia podawana przy utworzeniu.</summary>
public sealed record OrderContent(
    DateOnly OrderDate,
    OrderChannel Channel,
    decimal Value,
    FulfillmentStatus FulfillmentStatus,
    Customer Customer,
    OperationalData Operational,
    string? Comment);

/// <summary>
/// Edycja zamówienia: pełny zestaw pól należących do ERP oraz pola sklepu
/// (<see cref="OrderDate"/>, <see cref="Channel"/>, <see cref="Value"/>, <see cref="Customer"/>).
/// W zamówieniu ręcznym pola sklepu są wymagane; w zamówieniu ze sklepu są opcjonalne i tylko
/// porównywane z zapisanymi.
/// </summary>
public sealed record OrderEdit(
    FulfillmentStatus FulfillmentStatus,
    OperationalData Operational,
    string? Comment,
    DateOnly? OrderDate,
    OrderChannel? Channel,
    decimal? Value,
    Customer? Customer);

/// <summary>Wartość pola objętego audytem (typowana: data, kwota, słownik, tekst, flaga, lista).</summary>
public sealed record OrderFieldValue(OrderField Field, object? Value);

/// <summary>Zmienione pole: wartość stara i nowa.</summary>
public sealed record OrderFieldChange(OrderField Field, object? OldValue, object? NewValue);

/// <summary>Wynik operacji zmieniającej zamówienie: zmienione pola albo naruszenie reguły (bez zmian).</summary>
public sealed record OrderChangeResult(IReadOnlyList<OrderFieldChange> Changes, OrderRuleViolation? Violation)
{
    public bool Succeeded => Violation is null;

    public static OrderChangeResult Rejected(OrderRuleViolation violation) => new([], violation);
}

/// <summary>
/// Zamówienie (agregat). Pilnuje reguł statusu („Anulowane” tylko przez akcje), własności pól
/// zamówienia ze sklepu i wylicza zmienione pola na potrzeby audytu. Nie ma usuwania.
/// </summary>
public sealed class Order
{
    public const int NumberMaxLength = 32;
    public const int ExternalIdMaxLength = 64;
    public const int ShopStatusMaxLength = 32;
    public const int CommentMaxLength = 2000;

    /// <summary>Największa wartość mieszcząca się w <c>decimal(18,2)</c>.</summary>
    public const decimal MaxValue = 9_999_999_999_999_999.99m;

    public static readonly IReadOnlyList<ShopOwnedField> AllShopOwnedFields =
        [ShopOwnedField.OrderDate, ShopOwnedField.Value, ShopOwnedField.Channel, ShopOwnedField.Customer];

    private Order(
        Guid id,
        string number,
        OrderSource source,
        string? externalId,
        string? shopStatus,
        OrderContent content,
        DateTime nowUtc)
    {
        Id = id;
        Number = number;
        Source = source;
        ExternalId = externalId;
        ShopStatus = shopStatus;
        OrderDate = content.OrderDate;
        Channel = content.Channel;
        Value = content.Value;
        FulfillmentStatus = content.FulfillmentStatus;
        Customer = content.Customer;
        Operational = content.Operational;
        Comment = content.Comment;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    // Dla EF Core.
    private Order()
    {
        Number = string.Empty;
        Customer = null!;
        Operational = null!;
    }

    public Guid Id { get; private set; }

    /// <summary>Numer ERP, np. <c>ZR-2026-0001</c>.</summary>
    public string Number { get; private set; }

    public OrderSource Source { get; private set; }

    /// <summary>Identyfikator zamówienia w sklepie; tylko dla <see cref="OrderSource.WooCommerce"/>.</summary>
    public string? ExternalId { get; private set; }

    public OrderChannel Channel { get; private set; }

    /// <summary>Data biznesowa zamówienia (okres sprzedaży).</summary>
    public DateOnly OrderDate { get; private set; }

    /// <summary>Wartość brutto w złotych.</summary>
    public decimal Value { get; private set; }

    public FulfillmentStatus FulfillmentStatus { get; private set; }

    /// <summary>Status zamówienia w sklepie, jeżeli jest.</summary>
    public string? ShopStatus { get; private set; }

    public Customer Customer { get; private set; }

    public OperationalData Operational { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>Klucz idempotencji żądania, które utworzyło zamówienie ręczne.</summary>
    public Guid? CreationRequestId { get; private set; }

    /// <summary>SHA-256 znormalizowanej komendy utworzenia (32 bajty).</summary>
    public byte[]? CreationRequestHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool IsCancelled => FulfillmentStatus == FulfillmentStatus.Cancelled;

    /// <summary>Pola tylko do odczytu w edycji użytkownika (puste dla zamówienia ręcznego).</summary>
    public IReadOnlyList<ShopOwnedField> ShopOwnedFields => OwnedByShop(Source);

    public static IReadOnlyList<ShopOwnedField> OwnedByShop(OrderSource source) =>
        source == OrderSource.WooCommerce ? AllShopOwnedFields : [];

    /// <summary>Numer ERP: <c>ZR-{rok}-{kolejny}</c>, co najmniej 4 cyfry.</summary>
    public static string FormatNumber(int year, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"ZR-{year}-{sequence:D4}");

    /// <summary>Zamówienie wpisane ręcznie, z kluczem idempotencji żądania.</summary>
    /// <exception cref="ArgumentException">Treść narusza reguły, które sprawdza walidacja przed utworzeniem.</exception>
    public static Order CreateManual(
        Guid id,
        string number,
        OrderContent content,
        Guid creationRequestId,
        byte[] creationRequestHash,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(creationRequestHash);
        Require(id, number, content);
        if (content.FulfillmentStatus == FulfillmentStatus.Cancelled)
        {
            throw new ArgumentException("Nowe zamówienie nie może być anulowane.", nameof(content));
        }

        return new Order(id, number, OrderSource.Manual, externalId: null, shopStatus: null, content, nowUtc)
        {
            CreationRequestId = creationRequestId,
            CreationRequestHash = creationRequestHash,
        };
    }

    /// <summary>Zamówienie pobrane ze sklepu (import WooCommerce, SF-12).</summary>
    public static Order CreateFromShop(
        Guid id,
        string number,
        string externalId,
        string? shopStatus,
        OrderContent content,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        Require(id, number, content);
        return new Order(id, number, OrderSource.WooCommerce, externalId, shopStatus, content, nowUtc);
    }

    /// <summary>
    /// Edycja przez użytkownika. Kolejność: własność pól sklepu, reguły statusu, wyliczenie zmian.
    /// Przy naruszeniu reguły zamówienie się nie zmienia; bez zmian lista jest pusta.
    /// </summary>
    /// <exception cref="ArgumentException">Zamówienie ręczne bez pól wymaganych przy edycji.</exception>
    public OrderChangeResult Edit(OrderEdit edit, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Operational, nameof(edit));

        if (Source == OrderSource.WooCommerce)
        {
            var differing = ShopFieldsChangedBy(edit);
            if (differing.Count > 0)
            {
                return OrderChangeResult.Rejected(OrderRuleViolation.FieldOwnedByShop(differing));
            }
        }
        else if (edit.OrderDate is null || edit.Channel is null || edit.Value is null || edit.Customer is null)
        {
            throw new ArgumentException("Zamówienie ręczne wymaga daty, kanału, wartości i klienta.", nameof(edit));
        }

        if (IsCancelled != (edit.FulfillmentStatus == FulfillmentStatus.Cancelled))
        {
            return OrderChangeResult.Rejected(OrderRuleViolation.StatusChangeRequiresAction);
        }

        if (edit.Value is < 0 or > MaxValue)
        {
            throw new ArgumentException("Wartość poza zakresem.", nameof(edit));
        }

        var before = Snapshot();
        if (Source == OrderSource.Manual)
        {
            OrderDate = edit.OrderDate!.Value;
            Channel = edit.Channel!.Value;
            Value = edit.Value!.Value;
            Customer = edit.Customer!;
        }

        FulfillmentStatus = edit.FulfillmentStatus;
        Operational = edit.Operational;
        Comment = edit.Comment;

        return Changed(before, nowUtc);
    }

    /// <summary>Anulowanie: zamówienie wypada ze sprzedaży, ale zostaje (można je przywrócić).</summary>
    public OrderChangeResult Cancel(DateTime nowUtc)
    {
        if (IsCancelled)
        {
            return OrderChangeResult.Rejected(OrderRuleViolation.AlreadyCancelled);
        }

        var before = Snapshot();
        FulfillmentStatus = FulfillmentStatus.Cancelled;
        return Changed(before, nowUtc);
    }

    /// <summary>Przywrócenie anulowanego zamówienia: zawsze „W trakcie”.</summary>
    public OrderChangeResult Restore(DateTime nowUtc)
    {
        if (!IsCancelled)
        {
            return OrderChangeResult.Rejected(OrderRuleViolation.NotCancelled);
        }

        var before = Snapshot();
        FulfillmentStatus = FulfillmentStatus.InProgress;
        return Changed(before, nowUtc);
    }

    /// <summary>Wartości wszystkich pól objętych audytem, w stałej kolejności.</summary>
    public IReadOnlyList<OrderFieldValue> Snapshot() =>
    [
        new(OrderField.OrderDate, OrderDate),
        new(OrderField.Channel, Channel),
        new(OrderField.Value, Value),
        new(OrderField.FulfillmentStatus, FulfillmentStatus),
        new(OrderField.CustomerKind, Customer.Kind),
        new(OrderField.CustomerName, Customer.Name),
        new(OrderField.CustomerPhone, Customer.Phone),
        new(OrderField.CustomerEmail, Customer.Email),
        new(OrderField.CustomerStreet, Customer.Street),
        new(OrderField.CustomerPostalCode, Customer.PostalCode),
        new(OrderField.CustomerCity, Customer.City),
        new(OrderField.CustomerTaxId, Customer.TaxId),
        new(OrderField.Suppliers, Operational.Suppliers),
        new(OrderField.ZoReferences, Operational.ZoReferences),
        new(OrderField.InvoiceNumber, Operational.InvoiceNumber),
        new(OrderField.HasPhotos, Operational.HasPhotos),
        new(OrderField.HasReview, Operational.HasReview),
        new(OrderField.Notes, Operational.Notes),
        new(OrderField.Comment, Comment),
    ];

    private static void Require(Guid id, string number, OrderContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.Customer, nameof(content));
        ArgumentNullException.ThrowIfNull(content.Operational, nameof(content));
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Brak identyfikatora zamówienia.", nameof(id));
        }

        if (content.Value is < 0 or > MaxValue)
        {
            throw new ArgumentException("Wartość poza zakresem.", nameof(content));
        }
    }

    /// <summary>Pola sklepu, które edycja zmieniłaby (podane i różne od zapisanych).</summary>
    public IReadOnlyList<ShopOwnedField> ShopFieldsChangedBy(OrderEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var differing = new List<ShopOwnedField>();
        if (edit.OrderDate is { } orderDate && orderDate != OrderDate)
        {
            differing.Add(ShopOwnedField.OrderDate);
        }

        if (edit.Value is { } value && value != Value)
        {
            differing.Add(ShopOwnedField.Value);
        }

        if (edit.Channel is { } channel && channel != Channel)
        {
            differing.Add(ShopOwnedField.Channel);
        }

        if (edit.Customer is { } customer && customer != Customer)
        {
            differing.Add(ShopOwnedField.Customer);
        }

        return differing;
    }

    private OrderChangeResult Changed(IReadOnlyList<OrderFieldValue> before, DateTime nowUtc)
    {
        var after = Snapshot();
        var changes = new List<OrderFieldChange>();
        for (var index = 0; index < before.Count; index++)
        {
            if (!AreEqual(before[index].Value, after[index].Value))
            {
                changes.Add(new OrderFieldChange(before[index].Field, before[index].Value, after[index].Value));
            }
        }

        if (changes.Count > 0)
        {
            UpdatedAtUtc = nowUtc;
        }

        return new OrderChangeResult(changes, null);
    }

    private static bool AreEqual(object? left, object? right) => (left, right) switch
    {
        (IReadOnlyList<string> leftList, IReadOnlyList<string> rightList) => leftList.SequenceEqual(rightList, StringComparer.Ordinal),
        _ => Equals(left, right),
    };
}
