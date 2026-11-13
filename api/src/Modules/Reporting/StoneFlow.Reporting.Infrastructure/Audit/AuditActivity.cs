using Microsoft.EntityFrameworkCore;
using StoneFlow.Reporting.Infrastructure.Persistence;
using StoneFlow.Shared.Application.Audit;

namespace StoneFlow.Reporting.Infrastructure.Audit;

/// <summary>Czas ostatniego wpisu audytu obiektu i jego obiektów podrzędnych (SF-22, początek okresu retencji).</summary>
internal sealed class AuditActivity(ReportingDbContext context) : IAuditActivity
{
    public async Task<DateTime?> LastRecordedAtUtcAsync(AuditObjectReference root, CancellationToken cancellationToken)
    {
        var last = await context.AuditEntries.AsNoTracking()
            .ForRoot(root)
            .MaxAsync(record => (DateTime?)record.OccurredAtUtc, cancellationToken);
        return last is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null;
    }
}
