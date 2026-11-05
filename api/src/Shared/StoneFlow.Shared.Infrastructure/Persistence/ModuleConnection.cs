using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace StoneFlow.Shared.Infrastructure.Persistence;

/// <summary>
/// Jedno połączenie SQL na zakres DI (żądanie) dla kontekstów modułów finansowych. Dzięki temu
/// zmiana danych modułu i zapis audytu biegną w jednej lokalnej transakcji, bez transakcji
/// rozproszonej. Transakcję otwiera wyłącznie <see cref="UnitOfWork"/>.
/// </summary>
public sealed class ModuleConnection(IConfiguration configuration) : IDisposable, IAsyncDisposable
{
    private readonly List<DbContext> _enlisted = [];
    private SqlConnection? _connection;

    public DbConnection Connection =>
        _connection ??= new SqlConnection(configuration.GetConnectionString(ModuleDbContextOptions.ConnectionStringName));

    /// <summary>Aktywna transakcja jednostki pracy albo <c>null</c>.</summary>
    public DbTransaction? Transaction { get; private set; }

    internal async Task<DbTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (Transaction is not null)
        {
            throw new InvalidOperationException("Transakcja jednostki pracy jest już aktywna; zagnieżdżanie nie jest dozwolone.");
        }

        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }

        Transaction = await Connection.BeginTransactionAsync(cancellationToken);
        return Transaction;
    }

    /// <summary>Dołącza kontekst do aktywnej transakcji (wywoływane przez interceptor przed zapisem).</summary>
    internal void Enlist(DbContext context)
    {
        if (Transaction is null || context.Database.CurrentTransaction is not null)
        {
            return;
        }

        context.Database.UseTransaction(Transaction);
        _enlisted.Add(context);
    }

    /// <summary>
    /// Kończy transakcję jednostki pracy: konteksty przestają jej używać. Po wycofaniu dołączone
    /// konteksty porzucają śledzone encje, które nie odpowiadają już stanowi bazy.
    /// </summary>
    internal void EndTransaction(bool committed)
    {
        foreach (var context in _enlisted)
        {
            try
            {
                context.Database.UseTransaction(null);
                if (!committed)
                {
                    context.ChangeTracker.Clear();
                }
            }
            catch (ObjectDisposedException)
            {
                // Kontekst zwolniony przed końcem jednostki pracy nie wymaga odłączenia.
            }
        }

        _enlisted.Clear();
        Transaction = null;
    }

    public void Dispose() => _connection?.Dispose();

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
