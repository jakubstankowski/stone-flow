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
/// porównywane z zapisanymi. <see cref="UnreadableShopFields"/> to pola sklepu podane w postaci,
/// której nie da się odczytać (np. nieznany kod) — nie mogą być równe zapisanym.
/// </summary>
public sealed record OrderEdit(
    FulfillmentStatus FulfillmentStatus,
    OperationalData Operational,
    string? Comment,
    DateOnly? OrderDate,
    OrderChannel? Channel,
    decimal? Value,
    Customer? Customer,
    IReadOnlyCollection<ShopOwnedField>? UnreadableShopFields = null);

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

    /// <summary>Data modyfikacji w sklepie z ostatnio zastosowanej dostawy (kolejność dostaw).</summary>
    public DateTime? ShopModifiedAtUtc { get; private set; }

    /// <summary>Usunięcie zamówienia w sklepie (czas odbioru dostawy usunięcia).</summary>
    public DateTime? ShopDeletedAtUtc { get; private set; }

    /// <summary>Suma zwrotów w sklepie — informacja, nie tworzy wypływu.</summary>
    public decimal ShopRefundedAmount { get; private set; }

    /// <summary>„Zakończone w sklepie”: usunięte, anulowane albo w koszu.</summary>
    public bool IsTerminalInShop => Source == OrderSource.WooCommerce && ShopStatuses.IsTerminal(ShopStatus, ShopDeletedAtUtc);

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Czas anonimizacji danych klienta (SF-22); fakt nieodwracalny.</summary>
    public DateTime? AnonymizedAtUtc { get; private set; }

    /// <summary>Konto, które zanonimizowało dane klienta (SF-22).</summary>
    public Guid? AnonymizedByAccountId { get; private set; }

    /// <summary>Dane klienta zanonimizowano (SF-22): synchronizacja i edycja ich nie przywracają.</summary>
    public bool IsCustomerAnonymized => AnonymizedAtUtc is not null;

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
    /// Zamówienie zaimportowane ze sklepu (SF-12): numer <c>WC-{id}</c>, kanał „sklep”, „Anulowane”,
    /// gdy jest zakończone w sklepie, inaczej „W trakcie”. <paramref name="deletedAtUtc"/> — znacznik
    /// usunięcia odebranego przed utworzeniem.
    /// </summary>
    public static Order ImportFromShop(Guid id, string externalId, ShopOrderData data, DateTime? deletedAtUtc, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(data);
        var terminal = ShopStatuses.IsTerminal(data.Status, deletedAtUtc);
        var content = new OrderContent(
            data.OrderDate,
            OrderChannel.Shop,
            data.Value,
            terminal ? FulfillmentStatus.Cancelled : FulfillmentStatus.InProgress,
            data.Customer,
            OperationalData.Empty,
            Comment: null);
        var order = CreateFromShop(id, ShopNumber(externalId), externalId, data.Status, content, nowUtc);
        order.ShopModifiedAtUtc = data.ModifiedAtUtc;
        order.ShopDeletedAtUtc = deletedAtUtc;
        order.ShopRefundedAmount = data.RefundedAmount;
        return order;
    }

    /// <summary>Numer ERP zamówienia ze sklepu: <c>WC-{id ze sklepu}</c>.</summary>
    public static string ShopNumber(string externalId) => "WC-" + externalId;

    /// <summary>
    /// Zastosowanie dostawy ze sklepu (SF-12, „Zastosowanie snapshotu”). Zmienia tylko pola sklepu,
    /// a status realizacji wyłącznie jednorazowym przejściem na „Anulowane”, gdy zamówienie przechodzi
    /// z aktywnego w sklepie na zakończone w sklepie. Zamówienie z zanonimizowanymi danymi klienta
    /// (SF-22) nie przyjmuje klienta z dostawy, więc synchronizacja nie przywraca jego danych.
    /// </summary>
    /// <param name="delivery">Rodzaj dostawy (temat webhooka).</param>
    /// <param name="data">Dane pełnego zamówienia; <c>null</c> tylko dla <see cref="ShopDelivery.Deleted"/>.</param>
    /// <param name="nowUtc">Bieżący czas UTC (czas odbioru usunięcia).</param>
    public ShopApplyResult ApplyFromShop(ShopDelivery delivery, ShopOrderData? data, DateTime nowUtc)
    {
        if (Source != OrderSource.WooCommerce)
        {
            throw new InvalidOperationException("Dostawa ze sklepu dotyczy wyłącznie zamówień ze sklepu.");
        }

        var wasTerminal = IsTerminalInShop;
        var before = Snapshot();
        var modifiedBefore = ShopModifiedAtUtc;

        if (delivery == ShopDelivery.Deleted)
        {
            ShopDeletedAtUtc ??= nowUtc;
        }
        else
        {
            ArgumentNullException.ThrowIfNull(data);
            if (ShopModifiedAtUtc is { } stored && data.ModifiedAtUtc < stored)
            {
                return ShopApplyResult.Skip(ShopDeliverySkip.Older);
            }

            if (ShopDeletedAtUtc is { } deleted && data.ModifiedAtUtc <= deleted)
            {
                return ShopApplyResult.Skip(ShopDeliverySkip.BeforeDeletion);
            }

            ShopDeletedAtUtc = null;
            ShopModifiedAtUtc = data.ModifiedAtUtc;
            OrderDate = data.OrderDate;
            Value = data.Value;
            if (!IsCustomerAnonymized)
            {
                Customer = data.Customer;
            }

            ShopStatus = data.Status;
            ShopRefundedAmount = data.RefundedAmount;
        }

        if (!wasTerminal && IsTerminalInShop)
        {
            FulfillmentStatus = FulfillmentStatus.Cancelled;
        }

        var result = Changed(before, nowUtc);
        return new ShopApplyResult(result.Changes, result.Changes.Count > 0 || ShopModifiedAtUtc != modifiedBefore, Skipped: null);
    }

    /// <summary>
    /// Edycja przez użytkownika. Kolejność: własność pól sklepu, klient zanonimizowany (SF-22), reguły
    /// statusu, wyliczenie zmian.
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

        if (IsCustomerAnonymized && edit.Customer is { } customer && customer != Customer)
        {
            return OrderChangeResult.Rejected(OrderRuleViolation.CustomerAnonymized);
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

    /// <summary>
    /// Anonimizacja danych klienta (PRD §5, DANE-01, SF-22), nieodwracalna: nazwa klienta → znacznik
    /// <see cref="CustomerAnonymization.Marker"/>, pozostałe dane klienta, uwagi do realizacji i komentarz →
    /// brak, skrót żądania utworzenia → zera (skrót z treścią klienta pozwalałby potwierdzić zgadywane dane).
    /// Rodzaj klienta, kwoty, daty, kanał, status, dostawcy, ZO, faktura, flagi i pola sklepu zostają.
    /// Zwraca pola, które zmieniły wartość (bez starych wartości w audycie — robi to wywołujący).
    /// </summary>
    /// <exception cref="ArgumentException">Brak konta.</exception>
    public OrderChangeResult AnonymizeCustomer(Guid accountId, DateTime nowUtc)
    {
        if (IsCustomerAnonymized)
        {
            return OrderChangeResult.Rejected(OrderRuleViolation.AlreadyAnonymized);
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Brak identyfikatora konta.", nameof(accountId));
        }

        var before = Snapshot();
        Customer = new Customer(Customer.Kind, CustomerAnonymization.Marker, null, null, null, null, null, null);
        Operational = Operational with { Notes = null };
        Comment = null;
        if (CreationRequestHash is not null)
        {
            CreationRequestHash = new byte[32];
        }

        AnonymizedAtUtc = nowUtc;
        AnonymizedByAccountId = accountId;
        var result = Changed(before, nowUtc);
        UpdatedAtUtc = nowUtc;
        return result;
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
        new(OrderField.ShopStatus, ShopStatus),
        new(OrderField.ShopDeleted, Source == OrderSource.WooCommerce ? ShopDeletedAtUtc is not null : null),
        new(OrderField.ShopRefundedAmount, Source == OrderSource.WooCommerce ? ShopRefundedAmount : null),
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

    /// <summary>
    /// Pola sklepu, które edycja zmieniłaby: podane i różne od zapisanych albo nieczytelne,
    /// w kolejności <see cref="AllShopOwnedFields"/>.
    /// </summary>
    private List<ShopOwnedField> ShopFieldsChangedBy(OrderEdit edit)
    {
        var unreadable = edit.UnreadableShopFields ?? [];
        return
        [
            .. AllShopOwnedFields.Where(field => unreadable.Contains(field) || field switch
            {
                ShopOwnedField.OrderDate => edit.OrderDate is { } orderDate && orderDate != OrderDate,
                ShopOwnedField.Value => edit.Value is { } value && value != Value,
                ShopOwnedField.Channel => edit.Channel is { } channel && channel != Channel,
                _ => edit.Customer is { } customer && customer != Customer,
            }),
        ];
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
