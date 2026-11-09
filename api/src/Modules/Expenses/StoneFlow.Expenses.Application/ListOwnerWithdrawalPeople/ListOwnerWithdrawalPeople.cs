using StoneFlow.Expenses.Application.Abstractions;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Expenses.Application.ListOwnerWithdrawalPeople;

/// <summary>Podpowiedzi osób do formularza wypłaty.</summary>
public sealed record ListOwnerWithdrawalPeopleQuery : IQuery<IReadOnlyList<string>>;

/// <summary>Unikalne osoby z wcześniejszych wypłat, alfabetycznie, najwyżej <see cref="MaxPeople"/>.</summary>
public sealed class ListOwnerWithdrawalPeopleHandler(IOwnerWithdrawalReader reader) : IQueryHandler<ListOwnerWithdrawalPeopleQuery, IReadOnlyList<string>>
{
    public const int MaxPeople = 50;

    public Task<IReadOnlyList<string>> HandleAsync(ListOwnerWithdrawalPeopleQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return reader.ListPeopleAsync(MaxPeople, cancellationToken);
    }
}
