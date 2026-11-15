using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Reporting.Domain.Audit;

/// <summary>
/// Wpis audytu zmiany finansowej (AUD-01): autor, czas UTC, obiekt z datą biznesową, operacja i
/// zakres zmiany. Wpis jest tylko dodawany; zakres usunięcia zawiera ostatnie wartości obiektu.
/// Jedyna dozwolona zmiana zapisanego wpisu to <see cref="Redact"/>: zastąpienie wartości wskazanych
/// pól znacznikiem przy anonimizacji danych klienta (DANE-01, SF-22).
/// </summary>
public sealed class AuditRecord
{
    public const int LabelMaxLength = 200;

    public const int FieldMaxLength = 100;

    private AuditRecord(
        DateTime occurredAtUtc,
        AuditAuthorKind authorKind,
        Guid? authorAccountId,
        AuditObjectType objectType,
        Guid objectId,
        DateOnly objectBusinessDate,
        string objectLabel,
        AuditObjectType? parentObjectType,
        Guid? parentObjectId,
        AuditOperation operation,
        IReadOnlyList<AuditFieldChange> changes)
    {
        OccurredAtUtc = occurredAtUtc;
        AuthorKind = authorKind;
        AuthorAccountId = authorAccountId;
        ObjectType = objectType;
        ObjectId = objectId;
        ObjectBusinessDate = objectBusinessDate;
        ObjectLabel = objectLabel;
        ParentObjectType = parentObjectType;
        ParentObjectId = parentObjectId;
        Operation = operation;
        Changes = changes;
    }

    public long Id { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public AuditAuthorKind AuthorKind { get; private set; }

    /// <summary>Identyfikator konta, wyłącznie dla autora <see cref="AuditAuthorKind.Account"/>.</summary>
    public Guid? AuthorAccountId { get; private set; }

    public AuditObjectType ObjectType { get; private set; }

    public Guid ObjectId { get; private set; }

    public DateOnly ObjectBusinessDate { get; private set; }

    public string ObjectLabel { get; private set; }

    /// <summary>Rodzaj obiektu nadrzędnego (zamówienie, definicja cykliczna) albo <c>null</c>.</summary>
    public AuditObjectType? ParentObjectType { get; private set; }

    public Guid? ParentObjectId { get; private set; }

    public AuditOperation Operation { get; private set; }

    public IReadOnlyList<AuditFieldChange> Changes { get; private set; }

    /// <exception cref="ArgumentException">Wpis jest niekompletny albo niespójny.</exception>
    public static AuditRecord Create(
        DateTimeOffset occurredAt,
        AuditAuthorKind authorKind,
        Guid? authorAccountId,
        AuditObjectType objectType,
        Guid objectId,
        DateOnly objectBusinessDate,
        string objectLabel,
        AuditObjectType? parentObjectType,
        Guid? parentObjectId,
        AuditOperation operation,
        IReadOnlyList<AuditFieldChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        RequireDefined(authorKind, nameof(authorKind));
        RequireDefined(objectType, nameof(objectType));
        RequireDefined(operation, nameof(operation));

        var isAccount = authorKind == AuditAuthorKind.Account;
        if (isAccount != (authorAccountId is { } id && id != Guid.Empty))
        {
            throw new ArgumentException(
                "Autor „konto” wymaga identyfikatora konta, a autor systemowy nie może go mieć.", nameof(authorAccountId));
        }

        if (objectId == Guid.Empty)
        {
            throw new ArgumentException("Brak identyfikatora obiektu.", nameof(objectId));
        }

        if (string.IsNullOrWhiteSpace(objectLabel) || objectLabel.Trim().Length > LabelMaxLength)
        {
            throw new ArgumentException($"Etykieta obiektu jest wymagana i ma najwyżej {LabelMaxLength} znaków.", nameof(objectLabel));
        }

        RequireAllowedParent(objectType, parentObjectType, parentObjectId);

        if (changes.Count == 0)
        {
            throw new ArgumentException("Wpis audytu wymaga zakresu zmiany.", nameof(changes));
        }

        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change, nameof(changes));
            if (string.IsNullOrWhiteSpace(change.Field) || change.Field.Length > FieldMaxLength)
            {
                throw new ArgumentException($"Nazwa pola jest wymagana i ma najwyżej {FieldMaxLength} znaków.", nameof(changes));
            }
        }

        return new AuditRecord(
            occurredAt.UtcDateTime,
            authorKind,
            authorAccountId,
            objectType,
            objectId,
            objectBusinessDate,
            objectLabel.Trim(),
            parentObjectType,
            parentObjectId,
            operation,
            [.. changes]);
    }

    /// <summary>
    /// Anonimizacja (DANE-01, SF-22): w polach z <paramref name="fields"/> (porównanie dokładne) wartość
    /// stara i nowa różna od <c>null</c> zostaje zastąpiona <paramref name="marker"/>, a <c>null</c>
    /// zostaje <c>null</c>. Nazwy pól, flaga danych osobowych, kolejność, pozostałe pola, autor, czas,
    /// obiekt, etykieta, data biznesowa, obiekt nadrzędny i operacja się nie zmieniają.
    /// </summary>
    /// <returns><c>true</c>, gdy zmieniła się co najmniej jedna wartość; inaczej zakres zmiany zostaje bez zmian.</returns>
    /// <exception cref="ArgumentNullException">Brak zbioru pól.</exception>
    /// <exception cref="ArgumentException">Pusty znacznik.</exception>
    public bool Redact(IReadOnlySet<string> fields, string marker)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);

        var changed = false;
        var redacted = new List<AuditFieldChange>(Changes.Count);
        foreach (var change in Changes)
        {
            if (!fields.Contains(change.Field))
            {
                redacted.Add(change);
                continue;
            }

            var result = change with
            {
                OldValue = change.OldValue is null ? null : marker,
                NewValue = change.NewValue is null ? null : marker,
            };
            changed |= result != change;
            redacted.Add(result);
        }

        if (changed)
        {
            Changes = redacted;
        }

        return changed;
    }

    /// <summary>
    /// Wpłata, zwrot i korekta sprzedaży wskazują zamówienie; wydatek może wskazać definicję
    /// cykliczną; pozostałe obiekty nie mają obiektu nadrzędnego.
    /// </summary>
    private static void RequireAllowedParent(AuditObjectType objectType, AuditObjectType? parentType, Guid? parentId)
    {
        if (parentType.HasValue != parentId.HasValue || parentId == Guid.Empty)
        {
            throw new ArgumentException("Obiekt nadrzędny wymaga rodzaju i identyfikatora.", nameof(parentId));
        }

        var valid = objectType switch
        {
            AuditObjectType.Payment or AuditObjectType.Refund or AuditObjectType.SalesCorrection =>
                parentType == AuditObjectType.Order,
            AuditObjectType.Expense => parentType is null or AuditObjectType.RecurringExpenseDefinition,
            _ => parentType is null,
        };

        if (!valid)
        {
            throw new ArgumentException($"Niedozwolony obiekt nadrzędny dla „{objectType}”.", nameof(parentType));
        }
    }

    private static void RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException("Nieznana wartość.", parameterName);
        }
    }
}

/// <summary>Zmiana pola; <see cref="IsPersonalData"/> oznacza dane osobowe do anonimizacji.</summary>
public sealed record AuditFieldChange(string Field, string? OldValue, string? NewValue, bool IsPersonalData);
