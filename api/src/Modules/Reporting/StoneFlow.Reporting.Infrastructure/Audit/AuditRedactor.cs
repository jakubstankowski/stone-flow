using Microsoft.EntityFrameworkCore;
using StoneFlow.Reporting.Infrastructure.Persistence;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Reporting.Infrastructure.Audit;

/// <summary>
/// Anonimizacja wpisów <c>reporting.AuditEntries</c> (SF-22) w transakcji jednostki pracy wywołującego
/// modułu (wspólne połączenie, jak <see cref="AuditLog"/>). Reguła zmiany wartości należy do
/// <see cref="Domain.Audit.AuditRecord.Redact"/>. Wartości nie są logowane.
/// </summary>
internal sealed class AuditRedactor(ReportingDbContext context, ModuleConnection connection) : IAuditRedaction
{
    public async Task<int> RedactAsync(AuditRedaction redaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redaction);
        ArgumentNullException.ThrowIfNull(redaction.Root, nameof(redaction));
        ArgumentNullException.ThrowIfNull(redaction.Fields, nameof(redaction));
        if (redaction.Fields.Count == 0)
        {
            throw new ArgumentException("Anonimizacja wymaga co najmniej jednego pola.", nameof(redaction));
        }

        if (string.IsNullOrWhiteSpace(redaction.Marker))
        {
            throw new ArgumentException("Anonimizacja wymaga znacznika.", nameof(redaction));
        }

        if (connection.Transaction is null)
        {
            throw new InvalidOperationException(
                "Anonimizacja audytu działa wyłącznie w transakcji jednostki pracy (IUnitOfWork.ExecuteInTransactionAsync).");
        }

        var fieldsByType = redaction.Fields
            .GroupBy(selector => selector.ObjectType)
            .ToDictionary(group => group.Key, group => (IReadOnlySet<string>)group.Select(selector => selector.Field).ToHashSet(StringComparer.Ordinal));

        var records = await context.AuditEntries.ForRoot(redaction.Root).ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var record in records)
        {
            if (fieldsByType.TryGetValue(record.ObjectType, out var fields) && record.Redact(fields, redaction.Marker))
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }
}
