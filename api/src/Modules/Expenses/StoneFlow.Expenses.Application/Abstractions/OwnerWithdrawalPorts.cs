using StoneFlow.Expenses.Application.GetOwnerWithdrawal;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;

namespace StoneFlow.Expenses.Application.Abstractions;

/// <summary>Wypłata wczytana do zmiany razem z zapisaną wersją (token współbieżności).</summary>
public sealed record StoredOwnerWithdrawal(OwnerWithdrawal Withdrawal, byte[] Version);

/// <summary>
/// Zapis wypłat w transakcji jednostki pracy. Konflikt wersji i duplikat to wyniki
/// (<see cref="StoreSaveResult"/>), nie wyjątki.
/// </summary>
public interface IOwnerWithdrawalStore
{
    /// <summary>Wczytuje wypłatę do zmiany; <c>null</c>, gdy nie istnieje.</summary>
    Task<StoredOwnerWithdrawal?> LoadAsync(Guid id, CancellationToken cancellationToken);

    Task<StoreSaveResult> AddAsync(OwnerWithdrawal withdrawal, CancellationToken cancellationToken);

    /// <summary>Zapisuje zmiany wczytanej wypłaty, o ile wersja w bazie równa się <paramref name="expectedVersion"/>.</summary>
    Task<StoreSaveResult> SaveAsync(OwnerWithdrawal withdrawal, byte[] expectedVersion, CancellationToken cancellationToken);

    /// <summary>Usuwa wypłatę fizycznie, o ile wersja w bazie równa się <paramref name="expectedVersion"/>.</summary>
    Task<StoreSaveResult> DeleteAsync(OwnerWithdrawal withdrawal, byte[] expectedVersion, CancellationToken cancellationToken);
}

/// <summary>Wypłata utworzona żądaniem o danym kluczu idempotencji.</summary>
public sealed record OwnerWithdrawalCreationMatch(Guid Id, byte[] RequestHash, byte[] Version);

/// <summary>Odczyty wypłat jako DTO (bez śledzenia zmian).</summary>
public interface IOwnerWithdrawalReader
{
    /// <summary>Dane wypłaty; <c>null</c>, gdy nie istnieje.</summary>
    Task<OwnerWithdrawalRecordDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OwnerWithdrawalCreationMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken);

    /// <summary>
    /// Osoby z wcześniejszych wypłat: bez duplikatów różniących się wielkością liter (zapis z
    /// ostatnio zmienionej wypłaty), alfabetycznie, najwyżej <paramref name="max"/>.
    /// </summary>
    Task<IReadOnlyList<string>> ListPeopleAsync(int max, CancellationToken cancellationToken);
}
