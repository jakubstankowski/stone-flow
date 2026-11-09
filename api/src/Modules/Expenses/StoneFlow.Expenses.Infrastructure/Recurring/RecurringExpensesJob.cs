using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoneFlow.Expenses.Application;
using StoneFlow.Expenses.Application.GenerateRecurringOccurrences;
using StoneFlow.Expenses.Application.ListRecurringGenerationCandidates;
using StoneFlow.Shared.Application.Cqrs;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Expenses.Infrastructure.Recurring;

/// <summary>
/// Zadanie harmonogramu <c>recurring-expenses</c> (00:10 Europe/Warsaw, SF-16): tworzy wystąpienia
/// należne na dzień startu przebiegu, także zaległe po przerwie w działaniu. Faza odczytu w zakresie
/// przebiegu, potem jedna jednostka pracy na definicję w nowym zakresie DI. Kolizja rejestru albo
/// zakleszczenie (1205) — jedno ponowienie w nowym zakresie (po wycofaniu zakres niczego już nie
/// zapisuje); inny błąd definicji jest logowany (tylko id) i liczony, a kolejne definicje biegną dalej.
/// Przed każdą definicją sprawdzane jest anulowanie. Przebieg z błędami kończy się wyjątkiem z samą
/// liczbą (historia przebiegów, ponowienie SF-11).
/// </summary>
internal sealed partial class RecurringExpensesJob(
    IDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RecurringExpensesJob> logger) : IScheduledJob
{
    public const string JobKey = "recurring-expenses";

    public static readonly JobDefinition Definition =
        new(JobKey, JobSchedule.DailyAt(new TimeOnly(0, 10)), TimeSpan.FromMinutes(30));

    private const int DeadlockVictim = 1205;

    public async Task RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var today = WarsawTime.Today(timeProvider);
        var candidates = await dispatcher.QueryAsync(new ListRecurringGenerationCandidatesQuery(today), cancellationToken);

        var created = 0;
        var failures = 0;
        foreach (var definitionId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                created += await GenerateWithRetryAsync(definitionId, today, cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                failures++;
                LogDefinitionFailed(definitionId, exception);
            }
        }

        LogFinished(context.RunId, candidates.Count, created, failures);
        if (failures > 0)
        {
            throw new RecurringExpensesJobException($"Nie utworzono wystąpień dla {failures} wydatków cyklicznych.");
        }
    }

    /// <summary>Numer 1205 w łańcuchu wyjątku (SqlClient zgłasza go bezpośrednio albo przez <c>DbUpdateException</c>).</summary>
    internal static bool IsDeadlock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockVictim })
            {
                return true;
            }
        }

        return false;
    }

    private async Task<int> GenerateWithRetryAsync(Guid definitionId, DateOnly today, CancellationToken cancellationToken)
    {
        try
        {
            var first = await GenerateInNewScopeAsync(definitionId, today, cancellationToken);
            if (first.Outcome != GenerateRecurringOccurrencesOutcome.DuplicatePeriod)
            {
                return first.Created;
            }
        }
        catch (Exception exception) when (IsDeadlock(exception))
        {
            LogRetryAfterDeadlock(definitionId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var second = await GenerateInNewScopeAsync(definitionId, today, cancellationToken);
        return second.Outcome == GenerateRecurringOccurrencesOutcome.DuplicatePeriod
            ? throw new InvalidOperationException("Ponowna kolizja rejestru okresów wydatku cyklicznego.")
            : second.Created;
    }

    private async Task<GenerateRecurringOccurrencesResult> GenerateInNewScopeAsync(Guid definitionId, DateOnly today, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .SendAsync(new GenerateRecurringOccurrencesCommand(definitionId, today), cancellationToken);
    }

    [LoggerMessage(EventId = 7400, Level = LogLevel.Error, Message = "Zadanie recurring-expenses: błąd tworzenia wystąpień definicji {DefinitionId}.")]
    private partial void LogDefinitionFailed(Guid definitionId, Exception exception);

    [LoggerMessage(EventId = 7401, Level = LogLevel.Warning, Message = "Zadanie recurring-expenses: zakleszczenie przy definicji {DefinitionId}, ponowienie.")]
    private partial void LogRetryAfterDeadlock(Guid definitionId);

    [LoggerMessage(
        EventId = 7402,
        Level = LogLevel.Information,
        Message = "Zadanie recurring-expenses {RunId}: sprawdzono {Checked} definicji, utworzono {Created} wystąpień, błędy: {Failures}.")]
    private partial void LogFinished(Guid runId, int @checked, int created, int failures);
}

/// <summary>Przebieg zadania <c>recurring-expenses</c> z błędami definicji; komunikat zawiera tylko liczbę.</summary>
public sealed class RecurringExpensesJobException : Exception
{
    public RecurringExpensesJobException()
    {
    }

    public RecurringExpensesJobException(string message)
        : base(message)
    {
    }

    public RecurringExpensesJobException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
