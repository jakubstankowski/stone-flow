using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Domain.Orders;

namespace StoneFlow.Orders.Application;

public sealed record CustomerInput(
    string? Kind,
    string? Name,
    string? Phone,
    string? Email,
    string? Street,
    string? PostalCode,
    string? City,
    string? TaxId);

public sealed record OperationalInput(
    IReadOnlyList<string?>? Suppliers,
    IReadOnlyList<string?>? ZoReferences,
    string? InvoiceNumber,
    bool HasPhotos,
    bool HasReview,
    string? Notes);

/// <summary>Pola edytowalne w każdym zamówieniu, niezależnie od źródła.</summary>
public sealed record EditableOrderFields(FulfillmentStatus FulfillmentStatus, OperationalData Operational, string? Comment);

/// <summary>Pola należące w zamówieniu ze sklepu do sklepu; w zamówieniu ręcznym wymagane.</summary>
public sealed record ManualOrderFields(DateOnly OrderDate, OrderChannel Channel, decimal Value, Customer Customer);

/// <summary>
/// Pola sklepu odczytane z żądania bez walidacji (zamówienie ze sklepu): <c>null</c>, gdy ich brak.
/// <see cref="Unreadable"/> to pola podane w postaci, której nie da się porównać (np. nieznany kod);
/// nie mogą być równe zapisanym.
/// </summary>
public sealed record ShopFieldsReading(
    DateOnly? OrderDate,
    OrderChannel? Channel,
    decimal? Value,
    Customer? Customer,
    IReadOnlyList<ShopOwnedField> Unreadable);

/// <summary>
/// Dane zamówienia z formularza (tekst jak w JSON). Walidacja jest dwuetapowa: pola niezależne od
/// źródła (<see cref="ValidateEditable"/>) i pola zależne od źródła (<see cref="ValidateManualFields"/>
/// albo <see cref="ReadShopFields"/>). Normalizacja: teksty przycięte, pusty tekst to <c>null</c>,
/// NIP z samych cyfr, listy bez pustych pozycji w podanej kolejności.
/// </summary>
public sealed partial record OrderInput(
    string? OrderDate,
    string? Channel,
    string? Value,
    string? FulfillmentStatus,
    CustomerInput? Customer,
    OperationalInput? Operational,
    string? Comment)
{
    private static readonly JsonSerializerOptions HashJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Walidacja i normalizacja komendy utworzenia; status domyślnie „W trakcie”.</summary>
    public OrderContent? ValidateForCreate(FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var manual = ValidateManualFields(errors);

        var status = Domain.Orders.FulfillmentStatus.InProgress;
        if (Text(FulfillmentStatus) is { } statusCode)
        {
            if (!OrderCodes.TryParse(statusCode, out status))
            {
                errors.Add(OrderFieldKeys.FulfillmentStatus, OrderMessages.StatusUnknown);
            }
            else if (status == Domain.Orders.FulfillmentStatus.Cancelled)
            {
                errors.Add(OrderFieldKeys.FulfillmentStatus, OrderMessages.StatusCancelledNotAllowed);
            }
        }

        var operational = ValidateOperational(errors);
        var comment = ValidateText(Comment, OrderFieldKeys.Comment, Order.CommentMaxLength, OrderMessages.CommentTooLong, errors);

        return errors.IsEmpty && manual is not null
            ? new OrderContent(manual.OrderDate, manual.Channel, manual.Value, status, manual.Customer, operational, comment)
            : null;
    }

    /// <summary>Walidacja pól niezależnych od źródła (edycja): status wymagany, dane operacyjne, komentarz.</summary>
    public EditableOrderFields? ValidateEditable(FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        FulfillmentStatus? status = null;
        if (Text(FulfillmentStatus) is not { } statusCode)
        {
            errors.Add(OrderFieldKeys.FulfillmentStatus, OrderMessages.StatusRequired);
        }
        else if (OrderCodes.TryParse(statusCode, out FulfillmentStatus parsed))
        {
            status = parsed;
        }
        else
        {
            errors.Add(OrderFieldKeys.FulfillmentStatus, OrderMessages.StatusUnknown);
        }

        var operational = ValidateOperational(errors);
        var comment = ValidateText(Comment, OrderFieldKeys.Comment, Order.CommentMaxLength, OrderMessages.CommentTooLong, errors);

        return errors.IsEmpty && status is { } valid ? new EditableOrderFields(valid, operational, comment) : null;
    }

    /// <summary>Walidacja daty, kanału, wartości i klienta (zamówienie ręczne).</summary>
    public ManualOrderFields? ValidateManualFields(FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.IsEmpty;

        DateOnly? orderDate = null;
        if (Text(OrderDate) is not { } dateText)
        {
            errors.Add(OrderFieldKeys.OrderDate, OrderMessages.OrderDateRequired);
        }
        else if (TryParseDate(dateText, out var date))
        {
            orderDate = date;
        }
        else
        {
            errors.Add(OrderFieldKeys.OrderDate, OrderMessages.OrderDateInvalid);
        }

        OrderChannel? channel = null;
        if (Text(Channel) is not { } channelCode)
        {
            errors.Add(OrderFieldKeys.Channel, OrderMessages.ChannelRequired);
        }
        else if (OrderCodes.TryParse(channelCode, out OrderChannel parsedChannel))
        {
            channel = parsedChannel;
        }
        else
        {
            errors.Add(OrderFieldKeys.Channel, OrderMessages.ChannelUnknown);
        }

        decimal? value = null;
        if (TryParseValue(Value, out var parsedValue, out var valueError))
        {
            value = parsedValue;
        }
        else
        {
            errors.Add(OrderFieldKeys.Value, valueError);
        }

        var customer = ValidateCustomer(errors);

        return before && errors.IsEmpty && orderDate is { } d && channel is { } c && value is { } v && customer is not null
            ? new ManualOrderFields(d, c, v, customer)
            : null;
    }

    /// <summary>Odczyt pól sklepu do porównania z zapisanymi (zamówienie ze sklepu), bez walidacji.</summary>
    public ShopFieldsReading ReadShopFields()
    {
        var unreadable = new List<ShopOwnedField>();

        DateOnly? orderDate = null;
        if (Text(OrderDate) is { } dateText)
        {
            if (TryParseDate(dateText, out var date))
            {
                orderDate = date;
            }
            else
            {
                unreadable.Add(ShopOwnedField.OrderDate);
            }
        }

        decimal? value = null;
        if (Text(Value) is not null)
        {
            if (TryParseValue(Value, out var parsed, out _))
            {
                value = parsed;
            }
            else
            {
                unreadable.Add(ShopOwnedField.Value);
            }
        }

        OrderChannel? channel = null;
        if (Text(Channel) is { } channelCode)
        {
            if (OrderCodes.TryParse(channelCode, out OrderChannel parsedChannel))
            {
                channel = parsedChannel;
            }
            else
            {
                unreadable.Add(ShopOwnedField.Channel);
            }
        }

        Customer? customer = null;
        if (Customer is { } input)
        {
            if (OrderCodes.TryParse(Text(input.Kind), out CustomerKind kind) && Text(input.Name) is { } name)
            {
                customer = new Customer(
                    kind,
                    name,
                    Text(input.Phone),
                    Text(input.Email),
                    Text(input.Street),
                    Text(input.PostalCode),
                    Text(input.City),
                    NormalizeTaxId(input.TaxId));
            }
            else
            {
                unreadable.Add(ShopOwnedField.Customer);
            }
        }

        return new ShopFieldsReading(orderDate, channel, value, customer, unreadable);
    }

    /// <summary>
    /// SHA-256 z JSON znormalizowanej komendy utworzenia (stała kolejność właściwości, wartość
    /// kanoniczna <c>0.00</c>, słowniki jako kody, status domyślny już uzupełniony).
    /// </summary>
    public static byte[] ComputeRequestHash(OrderContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var model = new HashModel(
            content.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            OrderCodes.Of(content.Channel),
            FormatValue(content.Value),
            OrderCodes.Of(content.FulfillmentStatus),
            new HashCustomer(
                OrderCodes.Of(content.Customer.Kind),
                content.Customer.Name,
                content.Customer.Phone,
                content.Customer.Email,
                content.Customer.Street,
                content.Customer.PostalCode,
                content.Customer.City,
                content.Customer.TaxId),
            new HashOperational(
                content.Operational.Suppliers,
                content.Operational.ZoReferences,
                content.Operational.InvoiceNumber,
                content.Operational.HasPhotos,
                content.Operational.HasReview,
                content.Operational.Notes),
            content.Comment);

        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(model, HashJson));
    }

    /// <summary>Kwota w zapisie kanonicznym: kropka dziesiętna, dokładnie dwa miejsca (<c>1234.50</c>).</summary>
    public static string FormatValue(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Kwota z tekstu w kulturze niezmiennej (<c>1234.50</c>): nieujemna, najwyżej dwa miejsca po
    /// przecinku, w zakresie <c>decimal(18,2)</c>.
    /// </summary>
    public static bool TryParseValue(string? text, out decimal value, out string error)
    {
        value = 0;
        error = string.Empty;
        var trimmed = Text(text);
        if (trimmed is null)
        {
            error = OrderMessages.ValueRequired;
            return false;
        }

        var match = AmountPattern().Match(trimmed);
        if (!match.Success)
        {
            error = OrderMessages.ValueInvalid;
            return false;
        }

        if (match.Groups["sign"].Success)
        {
            error = OrderMessages.ValueNegative;
            return false;
        }

        if (match.Groups["fraction"].Value.Length > 2)
        {
            error = OrderMessages.ValueScale;
            return false;
        }

        if (!decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed > Order.MaxValue)
        {
            error = OrderMessages.ValueTooLarge;
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Przycięty tekst albo <c>null</c> dla pustego.</summary>
    internal static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>NIP bez spacji i myślników (walidacja osobno).</summary>
    internal static string? NormalizeTaxId(string? text) =>
        Text(text) is { } trimmed ? string.Concat(trimmed.Where(c => c is not (' ' or '-'))) : null;

    /// <summary>NIP: 10 cyfr z poprawną sumą kontrolną (wagi 6, 5, 7, 2, 3, 4, 5, 6, 7).</summary>
    internal static bool IsValidTaxId(string digits)
    {
        if (digits.Length != 10 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        ReadOnlySpan<int> weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var sum = 0;
        for (var index = 0; index < weights.Length; index++)
        {
            sum += weights[index] * (digits[index] - '0');
        }

        var check = sum % 11;
        return check != 10 && check == digits[9] - '0';
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private Customer? ValidateCustomer(FieldErrors errors)
    {
        if (Customer is not { } input)
        {
            errors.Add(OrderFieldKeys.CustomerKind, OrderMessages.CustomerKindRequired);
            errors.Add(OrderFieldKeys.CustomerName, OrderMessages.CustomerNameRequired);
            return null;
        }

        var before = errors.IsEmpty;

        CustomerKind? kind = null;
        if (Text(input.Kind) is not { } kindCode)
        {
            errors.Add(OrderFieldKeys.CustomerKind, OrderMessages.CustomerKindRequired);
        }
        else if (OrderCodes.TryParse(kindCode, out CustomerKind parsedKind))
        {
            kind = parsedKind;
        }
        else
        {
            errors.Add(OrderFieldKeys.CustomerKind, OrderMessages.CustomerKindUnknown);
        }

        var name = Text(input.Name);
        if (name is null)
        {
            errors.Add(OrderFieldKeys.CustomerName, OrderMessages.CustomerNameRequired);
        }
        else if (name.Length > Domain.Orders.Customer.NameMaxLength)
        {
            errors.Add(OrderFieldKeys.CustomerName, OrderMessages.CustomerNameTooLong);
        }

        var phone = ValidateText(input.Phone, OrderFieldKeys.CustomerPhone, Domain.Orders.Customer.PhoneMaxLength, OrderMessages.PhoneTooLong, errors);
        var street = ValidateText(input.Street, OrderFieldKeys.CustomerStreet, Domain.Orders.Customer.StreetMaxLength, OrderMessages.StreetTooLong, errors);
        var city = ValidateText(input.City, OrderFieldKeys.CustomerCity, Domain.Orders.Customer.CityMaxLength, OrderMessages.CityTooLong, errors);

        var email = Text(input.Email);
        if (email is not null && (email.Length > Domain.Orders.Customer.EmailMaxLength || !EmailPattern().IsMatch(email)))
        {
            errors.Add(OrderFieldKeys.CustomerEmail, OrderMessages.EmailInvalid);
        }

        var postalCode = Text(input.PostalCode);
        if (postalCode is not null && !PostalCodePattern().IsMatch(postalCode))
        {
            errors.Add(OrderFieldKeys.CustomerPostalCode, OrderMessages.PostalCodeInvalid);
        }

        var taxId = NormalizeTaxId(input.TaxId);
        if (taxId is not null && !IsValidTaxId(taxId))
        {
            errors.Add(OrderFieldKeys.CustomerTaxId, OrderMessages.TaxIdInvalid);
        }

        return before && errors.IsEmpty && kind is { } validKind && name is not null
            ? new Customer(validKind, name, phone, email, street, postalCode, city, taxId)
            : null;
    }

    private OperationalData ValidateOperational(FieldErrors errors)
    {
        if (Operational is not { } input)
        {
            return OperationalData.Empty;
        }

        var suppliers = ValidateList(
            input.Suppliers,
            OrderFieldKeys.Suppliers,
            OperationalData.MaxSuppliers,
            OperationalData.SupplierMaxLength,
            OrderMessages.SuppliersTooMany,
            OrderMessages.SupplierTooLong,
            errors);
        var zoReferences = ValidateList(
            input.ZoReferences,
            OrderFieldKeys.ZoReferences,
            OperationalData.MaxZoReferences,
            OperationalData.ZoReferenceMaxLength,
            OrderMessages.ZoReferencesTooMany,
            OrderMessages.ZoReferenceTooLong,
            errors);
        var invoiceNumber = ValidateText(
            input.InvoiceNumber, OrderFieldKeys.InvoiceNumber, OperationalData.InvoiceNumberMaxLength, OrderMessages.InvoiceNumberTooLong, errors);
        var notes = ValidateText(input.Notes, OrderFieldKeys.Notes, OperationalData.NotesMaxLength, OrderMessages.NotesTooLong, errors);

        return new OperationalData(suppliers, zoReferences, invoiceNumber, input.HasPhotos, input.HasReview, notes);
    }

    private static string? ValidateText(string? text, string field, int maxLength, string tooLong, FieldErrors errors)
    {
        var trimmed = Text(text);
        if (trimmed is not null && trimmed.Length > maxLength)
        {
            errors.Add(field, tooLong);
        }

        return trimmed;
    }

    private static List<string> ValidateList(
        IReadOnlyList<string?>? items,
        string field,
        int maxCount,
        int maxLength,
        string tooMany,
        string tooLong,
        FieldErrors errors)
    {
        var result = new List<string>();
        if (items is null)
        {
            return result;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (Text(items[index]) is not { } item)
            {
                continue;
            }

            if (item.Length > maxLength)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"{field}[{index}]"), tooLong);
            }

            result.Add(item);
        }

        if (result.Count > maxCount)
        {
            errors.Add(field, tooMany);
        }

        return result;
    }

    // [0-9], a nie \d: \d w .NET dopasowuje też cyfry spoza ASCII.
    [GeneratedRegex(@"^(?<sign>-)?[0-9]+(\.(?<fraction>[0-9]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[0-9]{2}-[0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex PostalCodePattern();

    private sealed record HashModel(
        string OrderDate,
        string Channel,
        string Value,
        string FulfillmentStatus,
        HashCustomer Customer,
        HashOperational Operational,
        string? Comment);

    private sealed record HashCustomer(
        string Kind,
        string Name,
        string? Phone,
        string? Email,
        string? Street,
        string? PostalCode,
        string? City,
        string? TaxId);

    private sealed record HashOperational(
        IReadOnlyList<string> Suppliers,
        IReadOnlyList<string> ZoReferences,
        string? InvoiceNumber,
        bool HasPhotos,
        bool HasReview,
        string? Notes);
}

/// <summary>Wersja zamówienia na granicy API: <c>rowversion</c> zakodowany base64.</summary>
public static class OrderVersion
{
    private const int Length = 8;

    public static string Format(byte[] version) => Convert.ToBase64String(version);

    /// <summary>Bajty wersji albo <c>null</c>, gdy tekst nie jest wersją.</summary>
    public static byte[]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var buffer = new byte[Length];
        return Convert.TryFromBase64String(text.Trim(), buffer, out var written) && written == Length ? buffer : null;
    }

    /// <summary>Waliduje wersję z żądania; błąd pod kluczem <c>version</c>.</summary>
    public static byte[]? Validate(string? text, FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var version = Parse(text);
        if (version is null)
        {
            errors.Add(OrderFieldKeys.Version, string.IsNullOrWhiteSpace(text) ? OrderMessages.VersionRequired : OrderMessages.VersionInvalid);
        }

        return version;
    }
}

/// <summary>Klucze błędów walidacji: ścieżki JSON pól żądania.</summary>
public static class OrderFieldKeys
{
    public const string OrderDate = "orderDate";
    public const string Channel = "channel";
    public const string Value = "value";
    public const string FulfillmentStatus = "fulfillmentStatus";
    public const string CustomerKind = "customer.kind";
    public const string CustomerName = "customer.name";
    public const string CustomerPhone = "customer.phone";
    public const string CustomerEmail = "customer.email";
    public const string CustomerStreet = "customer.street";
    public const string CustomerPostalCode = "customer.postalCode";
    public const string CustomerCity = "customer.city";
    public const string CustomerTaxId = "customer.taxId";
    public const string Suppliers = "operational.suppliers";
    public const string ZoReferences = "operational.zoReferences";
    public const string InvoiceNumber = "operational.invoiceNumber";
    public const string Notes = "operational.notes";
    public const string Comment = "comment";
    public const string Version = "version";
}

/// <summary>Komunikaty walidacji pól (UI po polsku).</summary>
public static class OrderMessages
{
    public const string OrderDateRequired = "Podaj datę zamówienia.";
    public const string OrderDateInvalid = "Podaj poprawną datę zamówienia.";
    public const string ChannelRequired = "Wybierz kanał sprzedaży.";
    public const string ChannelUnknown = "Nieznany kanał sprzedaży.";
    public const string ValueRequired = "Podaj wartość brutto.";
    public const string ValueInvalid = "Podaj wartość jako liczbę, np. 1234,50.";
    public const string ValueNegative = "Wartość nie może być ujemna.";
    public const string ValueScale = "Wartość może mieć najwyżej dwa miejsca po przecinku.";
    public const string ValueTooLarge = "Wartość jest zbyt duża.";
    public const string StatusRequired = "Wybierz status realizacji.";
    public const string StatusUnknown = "Nieznany status realizacji.";
    public const string StatusCancelledNotAllowed = "Status „Anulowane” ustawia akcja „Anuluj zamówienie”.";
    public const string CustomerKindRequired = "Wybierz rodzaj klienta.";
    public const string CustomerKindUnknown = "Nieznany rodzaj klienta.";
    public const string CustomerNameRequired = "Podaj imię i nazwisko lub nazwę firmy.";
    public const string CustomerNameTooLong = "Nazwa klienta może mieć najwyżej 200 znaków.";
    public const string PhoneTooLong = "Telefon może mieć najwyżej 32 znaki.";
    public const string EmailInvalid = "Podaj poprawny adres e-mail.";
    public const string StreetTooLong = "Ulica może mieć najwyżej 200 znaków.";
    public const string PostalCodeInvalid = "Podaj kod pocztowy w formacie 00-000.";
    public const string CityTooLong = "Miejscowość może mieć najwyżej 100 znaków.";
    public const string TaxIdInvalid = "Podaj poprawny NIP (10 cyfr).";
    public const string SuppliersTooMany = "Można podać najwyżej 10 dostawców.";
    public const string SupplierTooLong = "Nazwa dostawcy może mieć najwyżej 100 znaków.";
    public const string ZoReferencesTooMany = "Można podać najwyżej 10 referencji ZO.";
    public const string ZoReferenceTooLong = "Referencja ZO może mieć najwyżej 64 znaki.";
    public const string InvoiceNumberTooLong = "Numer faktury może mieć najwyżej 64 znaki.";
    public const string NotesTooLong = "Uwagi mogą mieć najwyżej 2000 znaków.";
    public const string CommentTooLong = "Komentarz może mieć najwyżej 2000 znaków.";
    public const string VersionRequired = "Brak wersji zamówienia. Wczytaj zamówienie ponownie.";
    public const string VersionInvalid = "Nieprawidłowa wersja zamówienia. Wczytaj zamówienie ponownie.";
}
