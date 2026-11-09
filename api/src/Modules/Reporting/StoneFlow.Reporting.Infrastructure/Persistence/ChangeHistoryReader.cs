using Microsoft.EntityFrameworkCore;
using StoneFlow.Reporting.Application.Abstractions;
using StoneFlow.Shared.Kernel.Audit;

namespace StoneFlow.Reporting.Infrastructure.Persistence;

/// <summary>
/// Odczyt „Historii zmian” z <c>reporting.AuditEntries</c>: filtry, liczba, sortowanie i strona w
/// SQL. Znacznik usunięcia to skorelowane <c>EXISTS</c> po <c>(ObjectType, ObjectId)</c>, niezależne
/// od filtrów. Zapytania idą po kolei (wspólne połączenie modułu).
/// </summary>
internal sealed class ChangeHistoryReader(ReportingDbContext context) : IChangeHistoryReader
{
    public async Task<ChangeHistoryPageDto> ListAsync(ChangeHistoryCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var entries = context.AuditEntries.AsNoTracking()
            .Where(record => record.OccurredAtUtc >= criteria.FromUtc && record.OccurredAtUtc < criteria.ToUtcExclusive);

        if (criteria.BusinessFrom is { } businessFrom)
        {
            entries = entries.Where(record => record.ObjectBusinessDate >= businessFrom);
        }

        if (criteria.BusinessTo is { } businessTo)
        {
            entries = entries.Where(record => record.ObjectBusinessDate <= businessTo);
        }

        if (criteria.ObjectTypes.Count > 0)
        {
            var types = criteria.ObjectTypes.ToList();
            entries = entries.Where(record => types.Contains(record.ObjectType));
        }

        var total = await entries.CountAsync(cancellationToken);
        var skip = (long)(criteria.Page - 1) * criteria.PageSize;
        if (skip >= total)
        {
            return new ChangeHistoryPageDto([], total, criteria.Page, criteria.PageSize);
        }

        var ordered = criteria.Descending
            ? entries.OrderByDescending(record => record.OccurredAtUtc).ThenByDescending(record => record.Id)
            : entries.OrderBy(record => record.OccurredAtUtc).ThenBy(record => record.Id);

        var rows = await ordered
            .Skip((int)skip)
            .Take(criteria.PageSize)
            .Select(record => new
            {
                record.Id,
                record.OccurredAtUtc,
                record.AuthorKind,
                record.ObjectType,
                record.ObjectId,
                record.ObjectBusinessDate,
                record.ObjectLabel,
                record.ParentObjectType,
                record.ParentObjectId,
                record.Operation,
                record.Changes,
                Deleted = context.AuditEntries.Any(deletion =>
                    deletion.ObjectType == record.ObjectType
                    && deletion.ObjectId == record.ObjectId
                    && deletion.Operation == AuditOperation.Deleted),
            })
            .ToListAsync(cancellationToken);

        List<ChangeHistoryEntryDto> items =
        [
            .. rows.Select(row => new ChangeHistoryEntryDto(
                row.Id,
                DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc),
                row.AuthorKind,
                row.ObjectType,
                row.ObjectId,
                row.ObjectBusinessDate,
                row.ObjectLabel,
                row.ParentObjectType,
                row.ParentObjectId,
                row.Deleted,
                row.Operation,
                row.Changes)),
        ];

        return new ChangeHistoryPageDto(items, total, criteria.Page, criteria.PageSize);
    }
}
