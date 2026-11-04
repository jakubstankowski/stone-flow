using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Shared.Infrastructure.Persistence;

internal sealed class UnitOfWork(ModuleConnection connection) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        Func<T, bool> commitWhen,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(commitWhen);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var result = await work(cancellationToken);
            if (commitWhen(result))
            {
                await transaction.CommitAsync(cancellationToken);
                committed = true;
            }
            else
            {
                // Wycofanie nie zależy od anulowania: wynik błędu ma wrócić do wywołującego.
                await transaction.RollbackAsync(CancellationToken.None);
            }

            return result;
        }
        finally
        {
            // Bez zatwierdzenia zwolnienie transakcji ją wycofuje.
            connection.EndTransaction(committed);
        }
    }
}
