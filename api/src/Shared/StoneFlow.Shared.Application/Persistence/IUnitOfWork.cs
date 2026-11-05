namespace StoneFlow.Shared.Application.Persistence;

/// <summary>
/// Granica transakcji przypadku użycia obejmująca konteksty modułów finansowych (wspólne połączenie)
/// i zapis audytu. Wyjątek lub anulowanie w <c>work</c> wycofuje wszystkie zmiany.
/// Jedna granica na przypadek użycia: zagnieżdżone wywołanie kończy się
/// <see cref="InvalidOperationException"/>. Zadanie w tle tworzy nowy zakres DI na każdą jednostkę
/// pracy.
/// </summary>
public interface IUnitOfWork
{
    /// <param name="work">Praca przypadku użycia.</param>
    /// <param name="commitWhen">
    /// Czy zatwierdzić dany wynik. Wymagany: <c>false</c> wycofuje zmiany i zwraca wynik (błąd
    /// przetłumaczony na wynik, np. konflikt współbieżności); przypadek użycia bez wyniku błędu
    /// podaje <c>_ =&gt; true</c>.
    /// </param>
    /// <param name="cancellationToken">Anulowanie wycofuje zmiany.</param>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        Func<T, bool> commitWhen,
        CancellationToken cancellationToken);
}
