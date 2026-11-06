using Microsoft.Extensions.Logging;
using StoneFlow.Integrations.Application.WooCommerce;

namespace StoneFlow.Integrations.Infrastructure.WooCommerce;

/// <summary>Logi uzupełniania i historii błędów (SF-13): tylko ID zamówień i kontrolowane komunikaty.</summary>
internal sealed partial class WooCommerceSyncEvents(ILogger<WooCommerceSyncEvents> logger) : IWooCommerceSyncEvents
{
    public void NotConfigured() => LogNotConfigured();

    public void RunStarted(Guid runId, string source, DateTime fromUtc, DateTime toUtc) => LogRunStarted(runId, source, fromUtc, toUtc);

    public void RunSucceeded(Guid runId, SyncRunCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        LogRunSucceeded(runId, counts.OrdersProcessed, counts.Errors, counts.Unidentified);
    }

    public void RunFailed(Guid runId, string message) => LogRunFailed(runId, message);

    public void Unidentified(string source, string context, string problem) => LogUnidentified(source, context, problem);

    public void ApplyFailed(Exception? exception, string source, string externalId) => LogApplyFailed(exception, source, externalId);

    public void ErrorRecordFailed(Exception exception, string externalId) => LogErrorRecordFailed(exception, externalId);

    [LoggerMessage(EventId = 7300, Level = LogLevel.Information, Message = "Uzupełnianie WooCommerce: brak konfiguracji REST, przebieg pominięty.")]
    private partial void LogNotConfigured();

    [LoggerMessage(EventId = 7301, Level = LogLevel.Information, Message = "Uzupełnianie WooCommerce: start przebiegu {RunId} ({Source}), okno {FromUtc:o} – {ToUtc:o}.")]
    private partial void LogRunStarted(Guid runId, string source, DateTime fromUtc, DateTime toUtc);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Information, Message = "Uzupełnianie WooCommerce: przebieg {RunId} zakończony; zamówienia {Orders}, błędy {Errors}, bez ID {Unidentified}.")]
    private partial void LogRunSucceeded(Guid runId, int orders, int errors, int unidentified);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Warning, Message = "Uzupełnianie WooCommerce: przebieg {RunId} nieudany: {Message}.")]
    private partial void LogRunFailed(Guid runId, string message);

    [LoggerMessage(EventId = 7304, Level = LogLevel.Warning, Message = "WooCommerce ({Source}, {Context}): pozycja bez czytelnego ID zamówienia: {Problem}.")]
    private partial void LogUnidentified(string source, string context, string problem);

    [LoggerMessage(EventId = 7305, Level = LogLevel.Error, Message = "WooCommerce ({Source}): błąd zapisu zamówienia {ExternalId}.")]
    private partial void LogApplyFailed(Exception? exception, string source, string externalId);

    [LoggerMessage(EventId = 7306, Level = LogLevel.Error, Message = "WooCommerce: nie udało się zapisać historii błędu zamówienia {ExternalId}.")]
    private partial void LogErrorRecordFailed(Exception exception, string externalId);
}
