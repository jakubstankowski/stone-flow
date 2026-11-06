namespace StoneFlow.Integrations.Domain.WooCommerce;

/// <summary>
/// Okno zmian pobieranych przez uzupełnianie (SF-13, „Kursor i przebieg”): zamówienia zmienione po
/// <see cref="FromUtc"/> i przed <see cref="ToUtc"/>. Po sukcesie kursor = <see cref="ToUtc"/>.
/// </summary>
/// <param name="FromUtc">Dolna granica (ostra).</param>
/// <param name="ToUtc">Górna granica (ostra) i nowy kursor po sukcesie.</param>
public sealed record BackfillWindow(DateTime FromUtc, DateTime ToUtc)
{
    /// <summary>
    /// Margines na różnicę zegarów sklepu i serwera: górna granica leży tyle przed startem przebiegu
    /// (zmiana w trakcie przebiegu ma datę po niej, więc nie przesuwa stron), a dolna tyle przed
    /// kursorem (powtórzone zamówienia są idempotentne).
    /// </summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(10);

    /// <summary>Puste okno (np. przebieg tuż po poprzednim): nic do pobrania, kursor bez zmian.</summary>
    public bool IsEmpty => FromUtc >= ToUtc;

    /// <summary>Okno przebiegu startującego w <paramref name="startedAtUtc"/>.</summary>
    /// <param name="cursorUtc">Kursor ostatniego sukcesu; <c>null</c> przed pierwszym sukcesem.</param>
    /// <param name="launchDate">Data uruchomienia: bez kursora okno zaczyna się o północy tego dnia.</param>
    /// <param name="startedAtUtc">Start przebiegu (UTC).</param>
    /// <param name="zone">Strefa daty uruchomienia (Europe/Warsaw).</param>
    public static BackfillWindow For(DateTime? cursorUtc, DateOnly launchDate, DateTime startedAtUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var from = cursorUtc is { } cursor
            ? Utc(cursor) - Margin
            : TimeZoneInfo.ConvertTimeToUtc(launchDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        return new BackfillWindow(Utc(from), Utc(startedAtUtc) - Margin);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
