using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Expenses.Domain.OwnerWithdrawals;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.GetOwnerWithdrawal;

/// <summary>Dane wypłaty z odczytu (znaczniki w UTC).</summary>
public sealed record OwnerWithdrawalRecordDto(
    Guid Id,
    string Person,
    decimal Amount,
    DateOnly WithdrawnOn,
    OwnerWithdrawalMethod Method,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] Version);

public sealed record GetOwnerWithdrawalQuery(Guid Id) : IQuery<OwnerWithdrawalRecordDto?>;

/// <summary>Szczegóły wypłaty; <c>null</c>, gdy nie istnieje.</summary>
public sealed class GetOwnerWithdrawalHandler(IOwnerWithdrawalReader reader) : IQueryHandler<GetOwnerWithdrawalQuery, OwnerWithdrawalRecordDto?>
{
    public Task<OwnerWithdrawalRecordDto?> HandleAsync(GetOwnerWithdrawalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return reader.GetAsync(query.Id, cancellationToken);
    }
}
