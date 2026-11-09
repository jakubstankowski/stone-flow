using Microsoft.EntityFrameworkCore;
using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Application.GetOwnerWithdrawal;

namespace StoneFlow.Expenses.Infrastructure.Persistence;

/// <summary>Odczyty wypłat: projekcje do DTO bez śledzenia zmian.</summary>
internal sealed class OwnerWithdrawalReader(ExpensesDbContext context) : IOwnerWithdrawalReader
{
    public async Task<OwnerWithdrawalRecordDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await context.OwnerWithdrawals.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new OwnerWithdrawalRecordDto(
                w.Id,
                w.Person,
                w.Amount,
                w.WithdrawnOn,
                w.Method,
                w.CreatedAtUtc,
                w.UpdatedAtUtc,
                EF.Property<byte[]>(w, OwnerWithdrawalConfiguration.VersionProperty)))
            .SingleOrDefaultAsync(cancellationToken);

        return record is null
            ? null
            : record with
            {
                CreatedAtUtc = DateTime.SpecifyKind(record.CreatedAtUtc, DateTimeKind.Utc),
                UpdatedAtUtc = DateTime.SpecifyKind(record.UpdatedAtUtc, DateTimeKind.Utc),
            };
    }

    public async Task<OwnerWithdrawalCreationMatch?> FindByCreationRequestAsync(Guid creationRequestId, CancellationToken cancellationToken)
    {
        var row = await context.OwnerWithdrawals.AsNoTracking()
            .Where(w => w.CreationRequestId == creationRequestId)
            .Select(w => new { w.Id, w.CreationRequestHash, Version = EF.Property<byte[]>(w, OwnerWithdrawalConfiguration.VersionProperty) })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new OwnerWithdrawalCreationMatch(row.Id, row.CreationRequestHash ?? [], row.Version);
    }

    public async Task<IReadOnlyList<string>> ListPeopleAsync(int max, CancellationToken cancellationToken)
    {
        // W SQL: grupa po osobie bez rozróżniania wielkości liter, zapis z ostatnio zmienionej
        // wypłaty (remis: Id), kolejność alfabetyczna według collation bazy.
        return await context.OwnerWithdrawals
            .GroupBy(w => w.Person.ToUpper())
            .Select(group => group
                .OrderByDescending(w => w.UpdatedAtUtc)
                .ThenByDescending(w => w.Id)
                .Select(w => w.Person)
                .First())
            .OrderBy(person => person)
            .Take(max)
            .ToListAsync(cancellationToken);
    }
}
