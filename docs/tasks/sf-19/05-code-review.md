# Code review — SF-19

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja dostarcza widok sprzedaży, oba odczyty raportu i sumę listy wydatków. W trakcie prac poprawiono zachowanie listy lat po błędzie API i dodano test regresji. Weryfikacja integracji z SQL jest otwarta z powodu niedostępności LocalDB w sesji wykonawczej.
- Risk: high. Przegląd człowieka wymagany przed merge.

## Project Invariant Violations

None.

Sprawdzono wszystkie osiem reguł:

| Inwariant | Sprawdzenie |
|---|---|
| Sales separate from cash | Źródło Orders czyta zamówienia, zwroty sprzedaży i korekty. Źródło Expenses czyta wydatki według daty kosztu. |
| Backend decimal money | Obliczenia w domenie i SQL, formatowanie ciągów w UI, konwersja na liczby do geometrii wykresu. |
| API-enforced access | Trasy raportu pod polityką domyślną hosta. Test obu tras bez sesji. |
| Retries never duplicate | Odczyty raportowe bez operacji zapisu i bez zmian synchronizacji. |
| Financial audit trail | Odczyty i migracja indeksu nie zmieniają rekordów finansowych. |
| Secrets stay server-side | Kontrakty raportowe bez sekretów, Chart.js z zależności npm. |
| Module and layer boundaries | Reporting korzysta z portów współdzielonych, tabele czytają moduły będące ich właścicielami. |
| No identifying data in docs | Składnik raportu zawiera numer zamówienia. Dane scenariuszy są fikcyjne. |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

None.

## Specification Coverage

| Wymaganie | Status | Dowód |
|---|---|---|
| Okresy, granica uruchomienia i porównanie | Covered | `ReportPeriod`, `LaunchCutoff`, `Comparison`. Bieżący rok porównywany z tym samym odcinkiem poprzedniego. |
| Sprzedaż i liczba zamówień | Covered | `SalesEligibility`, `SalesPolicy`, `SalesSummary`. Wpłaty i zwroty pieniędzy poza raportem. Zwroty sprzedaży i korekty według dat operacji. |
| Koszty i wynik przed wypłatami | Covered | `ExpenseCostSource`, `CostSummary`, `SalesResult`. Wydatki według daty kosztu, także planowane, cykliczne i ujemne. |
| Zestawienie i uzgodnienie sum | Covered | `ListSalesComponentsHandler` z filtrami, stabilną kolejnością, stroną i sumą całego filtra. Podział według kanałów i przedziałów w `GetSalesReportHandler`. |
| API, walidacja, limit i sesja | Covered | `SalesReportEndpoints`, `ReportPeriodInput`, `SalesReportSources`. Dwa odczyty pod polityką domyślną, `400` i `422`. |
| Przejścia i suma listy wydatków | Covered | `SalesView`, `SalesComponents`, `ExpenseReader`, `ExpensesService`. `grossTotal` dla całego filtra, także strony poza końcem. |
| Pulpit, aktualność i dostępność | Covered | Karty, wybór okresu, stan integracji, obsługa klawiatury, tekstowe odpowiedniki wykresów i ponowny odczyt przy powrocie. |
| Scenariusze automatyczne i ręczne | Covered | Testy domenowe, integracyjne i komponentowe. Scenariusze ręczne w manifeście. |

## Plan Deviations

- Testy F-7, F-9 i F-12 znajdują się w `app/src/app/features/dashboard/dashboard-page.spec.ts:149` i dalszych przypadkach tego pliku. Plan rozdzielał je na `sales-view.spec.ts` i `sales-components.spec.ts`.

## Null Safety Issues

None.

## Correctness Issues

None.

Zamknięta uwaga: `app/src/app/features/dashboard/dashboard.service.ts:209` zachowuje ostatnią otrzymaną datę uruchomienia, gdy następny odczyt raportu się nie powiedzie. Lista lat i wybrany rok pozostają spójne.

## Test Gaps

None.

Regresję listy lat pokrywa `app/src/app/features/dashboard/dashboard.service.spec.ts:226`. Wykonanie testów wymagających SQL pozostaje otwarte, patrz manifest.

## Code Smells

None.

## Recommendation

- Merge as-is po obowiązkowym przeglądzie człowieka i potwierdzeniu testów SQL.
