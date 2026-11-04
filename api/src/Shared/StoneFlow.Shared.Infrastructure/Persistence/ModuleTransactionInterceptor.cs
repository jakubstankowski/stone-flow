using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StoneFlow.Shared.Infrastructure.Persistence;

/// <summary>
/// Gdy jednostka pracy ma aktywną transakcję: kontekst dołącza do niej przed <c>SaveChanges</c>
/// (zamiast otwierać własną), a każde polecenie (zapytanie, <c>ExecuteUpdate</c>) wykonuje się w niej.
/// </summary>
internal sealed class ModuleTransactionInterceptor(ModuleConnection connection) : ISaveChangesInterceptor, IDbCommandInterceptor
{
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enlist(eventData);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Enlist(eventData);
        return ValueTask.FromResult(result);
    }

    public DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (connection.Transaction is { } transaction && result.Transaction is null)
        {
            result.Transaction = transaction;
        }

        return result;
    }

    private void Enlist(DbContextEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            connection.Enlist(context);
        }
    }
}
