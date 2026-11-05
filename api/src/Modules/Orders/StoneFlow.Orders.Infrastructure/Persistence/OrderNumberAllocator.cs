using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoneFlow.Orders.Application.Abstractions;

namespace StoneFlow.Orders.Infrastructure.Persistence;

/// <summary>
/// Numeracja w transakcji jednostki pracy (interceptor SF-7 dołącza polecenia do transakcji):
/// <c>UPDATE … WITH (UPDLOCK, HOLDLOCK) … OUTPUT</c> blokuje licznik roku (także zakres, gdy
/// wiersza jeszcze nie ma) do końca transakcji, a wycofanie cofa licznik, więc numeracja nie ma luk.
/// </summary>
internal sealed class OrderNumberAllocator(OrdersDbContext context) : IOrderNumberAllocator
{
    private const int UniqueConstraintViolation = 2627;

    public async Task<int> NextAsync(int year, CancellationToken cancellationToken)
    {
        if (await IncrementAsync(year, cancellationToken) is { } next)
        {
            return next;
        }

        try
        {
            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO [orders].[OrderNumberCounters] ([Year], [LastNumber]) VALUES ({year}, 1)",
                cancellationToken);
            return 1;
        }
        catch (SqlException exception) when (exception.Number == UniqueConstraintViolation)
        {
            // Kod obronny: równoległe pierwsze zamówienie roku wstawiło już licznik.
        }

        return await IncrementAsync(year, cancellationToken)
            ?? throw new InvalidOperationException("Nie udało się nadać numeru zamówienia.");
    }

    private async Task<int?> IncrementAsync(int year, CancellationToken cancellationToken)
    {
        var numbers = await context.Database
            .SqlQuery<int>(
                $"UPDATE [orders].[OrderNumberCounters] WITH (UPDLOCK, HOLDLOCK) SET [LastNumber] = [LastNumber] + 1 OUTPUT inserted.[LastNumber] AS [Value] WHERE [Year] = {year}")
            .ToListAsync(cancellationToken);
        return numbers.Count == 0 ? null : numbers[0];
    }
}
