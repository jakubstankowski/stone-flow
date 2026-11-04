using StoneFlow.Reporting.Domain.Audit;
using StoneFlow.Reporting.Infrastructure.Persistence;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Reporting.Infrastructure.Audit;

/// <summary>
/// Zapis audytu do <c>reporting.AuditEntries</c> w transakcji jednostki pracy wywołującego modułu
/// (wspólne połączenie). Poza transakcją zapis jest odrzucany.
/// </summary>
internal sealed class AuditLog(ReportingDbContext context, ModuleConnection connection, TimeProvider timeProvider) : IAuditLog
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (connection.Transaction is null)
        {
            throw new InvalidOperationException(
                "Audyt zapisuje się wyłącznie w transakcji jednostki pracy (IUnitOfWork.ExecuteInTransactionAsync).");
        }

        var record = AuditRecord.Create(
            timeProvider.GetUtcNow(),
            entry.Author.Kind,
            entry.Author.AccountId,
            entry.Object.Type,
            entry.Object.Id,
            entry.Object.BusinessDate,
            entry.Object.Label,
            entry.Object.Parent?.Type,
            entry.Object.Parent?.Id,
            entry.Operation,
            [.. entry.Changes.Select(change => new AuditFieldChange(change.Field, change.OldValue, change.NewValue, change.IsPersonalData))]);

        context.AuditEntries.Add(record);
        await context.SaveChangesAsync(cancellationToken);
    }
}
