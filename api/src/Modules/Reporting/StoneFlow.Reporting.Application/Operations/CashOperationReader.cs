using StoneFlow.Shared.Application.Operations;
using StoneFlow.Shared.Kernel.Operations;

namespace StoneFlow.Reporting.Application.Operations;

/// <summary>
/// Odczyt operacji pieniężnych ze źródeł modułów-właścicieli i ich stała kolejność, wspólne dla
/// listy „Operacje” (SF-17) i raportu przepływów (SF-20) — dzięki temu karty Pulpitu równają się
/// sumom listy, a ostatnie operacje jej pierwszej stronie (FIN-05). Właściciel bezpiecznika
/// <see cref="MaxOperations"/> i reguły kolejności. Walidację, przełom uruchomienia, sumy i
/// stronicowanie mają handlery. Same odczyty: bez zapisu i bez ponawiania.
/// </summary>
public static class CashOperationReader
{
    /// <summary>Najwięcej operacji w jednym odczycie; więcej to <c>null</c> (422 w handlerze).</summary>
    public const int MaxOperations = 10_000;

    /// <summary>Wszystkie rodzaje operacji pieniężnych.</summary>
    public static IReadOnlySet<CashOperationKind> AllKinds { get; } = new HashSet<CashOperationKind>(Enum.GetValues<CashOperationKind>());

    /// <summary>
    /// Operacje żądanych rodzajów z datą w [<paramref name="from"/>, <paramref name="to"/>]. Źródła
    /// wywoływane po kolei (wspólne połączenie, bez równoległych zapytań); każde dostaje tylko swoje
    /// żądane rodzaje (źródło bez nich nie jest wołane) i limit malejący <c>MaxOperations + 1 −
    /// wczytane</c> (SQL <c>TOP</c>). Wynik <c>null</c>, gdy operacji jest więcej niż
    /// <see cref="MaxOperations"/>.
    /// </summary>
    /// <param name="sources">Źródła w kolejności rejestracji.</param>
    /// <param name="from">Efektywny początek (już po uwzględnieniu daty uruchomienia).</param>
    /// <param name="to">Koniec zakresu (włącznie).</param>
    /// <param name="kinds">Żądane rodzaje.</param>
    /// <param name="cancellationToken">Anulowanie zapytań.</param>
    public static async Task<IReadOnlyList<CashOperation>?> ReadAsync(
        IEnumerable<ICashOperationSource> sources,
        DateOnly from,
        DateOnly to,
        IReadOnlySet<CashOperationKind> kinds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(kinds);

        var loaded = new List<CashOperation>();
        foreach (var source in sources)
        {
            var requested = new HashSet<CashOperationKind>(kinds.Where(source.Kinds.Contains));
            if (requested.Count == 0)
            {
                continue;
            }

            var remaining = MaxOperations + 1 - loaded.Count;
            if (remaining <= 0)
            {
                break;
            }

            loaded.AddRange(await source.ListAsync(from, to, requested, remaining, cancellationToken));
        }

        return loaded.Count > MaxOperations ? null : loaded;
    }

    /// <summary>
    /// Stała kolejność operacji: data malejąco albo rosnąco, przy remisie rodzaj w kolejności
    /// deklaracji <see cref="CashOperationKind"/> (wpłata, zwrot, opłacony wydatek, wypłata), potem
    /// identyfikator. Jedyne miejsce tej reguły.
    /// </summary>
    public static IOrderedEnumerable<CashOperation> Order(IEnumerable<CashOperation> operations, bool descending)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var byDate = descending
            ? operations.OrderByDescending(operation => operation.Date)
            : operations.OrderBy(operation => operation.Date);
        return byDate.ThenBy(operation => operation.Kind).ThenBy(operation => operation.Id);
    }
}
