using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Application.Abstractions;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>
/// Blokada wiersza definicji w transakcji jednostki pracy (interceptor SF-7 dołącza polecenie do
/// transakcji), jak <c>OrderLock</c>: <c>SELECT … WITH (UPDLOCK, ROWLOCK)</c> trzyma blokadę
/// aktualizacji do końca transakcji przy izolacji READ COMMITTED, więc zmiana użytkownika i generowanie
/// tej samej definicji wykonują się po kolei. Dni i wyłączenia należą do definicji, więc blokada chroni
/// też je. Odczyt nie zmienia <c>rowversion</c>.
/// </summary>
internal sealed class RecurringDefinitionLock(ExpensesDbContext context) : IRecurringDefinitionLock
{
    public async Task<bool> LockAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var rows = await context.Database
            .SqlQuery<Guid>($"SELECT [Id] AS [Value] FROM [expenses].[RecurringExpenseDefinitions] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {definitionId}")
            .ToListAsync(cancellationToken);
        return rows.Count > 0;
    }
}
