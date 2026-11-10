# Implementation done — SF-19

## Summary

Dostarczono widok „Sprzedaż” na Pulpicie, raport i zestawienie składników sprzedaży oraz przejścia do filtrowanej listy wydatków z sumą brutto. Stan selektora lat korzysta z ostatniej daty uruchomienia otrzymanej z API także po błędzie raportu.

## Specification Coverage

| Wymaganie | Status | Uwagi |
|---|---|---|
| Okresy, granica uruchomienia i porównanie | Implemented | `ReportPeriod`, `LaunchCutoff`, `Comparison`. Bieżący rok porównywany z tym samym odcinkiem poprzedniego. |
| Sprzedaż i liczba zamówień | Implemented | `SalesEligibility`, `SalesPolicy`, `SalesSummary`. Wpłaty i zwroty pieniędzy poza raportem. Zwroty sprzedaży i korekty według dat operacji. |
| Koszty i wynik przed wypłatami | Implemented | `ExpenseCostSource`, `CostSummary`, `SalesResult`. Wydatki według daty kosztu, także planowane, cykliczne i ujemne. |
| Zestawienie i uzgodnienie sum | Implemented | `ListSalesComponentsHandler` z filtrami, stabilną kolejnością, stroną i sumą całego filtra. Podział według kanałów i przedziałów w `GetSalesReportHandler`. |
| API, walidacja, limit i sesja | Implemented | `SalesReportEndpoints`, `ReportPeriodInput`, `SalesReportSources`. Dwa odczyty pod polityką domyślną, `400` i `422`. |
| Przejścia i suma listy wydatków | Implemented | `SalesView`, `SalesComponents`, `ExpenseReader`, `ExpensesService`. `grossTotal` dla całego filtra, także strony poza końcem. |
| Pulpit, aktualność i dostępność | Implemented | Karty, wybór okresu, stan integracji, obsługa klawiatury, tekstowe odpowiedniki wykresów i ponowny odczyt przy powrocie. |
| Scenariusze automatyczne i ręczne | Implemented | Testy domenowe, integracyjne i komponentowe. Scenariusze ręczne w manifeście. |

## Files

### Created

- `api/src/Modules/Expenses/StoneFlow.Expenses.Infrastructure/Persistence/ExpenseCostSource.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/SalesEligibility.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/Migrations/20261109164727_SalesCorrectionDateIndex.Designer.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/Migrations/20261109164727_SalesCorrectionDateIndex.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderSalesComponentSource.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Api/Endpoints/SalesReportContracts.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Api/Endpoints/SalesReportEndpoints.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Application/Sales/GetSalesReport.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Application/Sales/ListSalesComponents.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Application/Sales/ReportPeriodInput.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Application/Sales/SalesReportCodes.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Application/Sales/SalesReportSources.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Application/WarsawTime.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Costs/Cost.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Costs/CostSummary.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Periods/Comparison.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Periods/LaunchCutoff.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Periods/ReportPeriod.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Sales/SalesComponent.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Sales/SalesPolicy.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Sales/SalesResult.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Sales/SalesSummary.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Costs/CostRecord.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Costs/ICostSource.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Sales/ISalesComponentSource.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Sales/SalesComponentRecord.cs`
- `api/src/Shared/StoneFlow.Shared.Kernel/Sales/SalesComponentKind.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Reporting/SalesReportApiTests.cs`
- `api/tests/StoneFlow.Orders.Tests/Domain/SalesEligibilityTests.cs`
- `api/tests/StoneFlow.Reporting.Tests/Costs/CostSummaryTests.cs`
- `api/tests/StoneFlow.Reporting.Tests/Periods/ComparisonTests.cs`
- `api/tests/StoneFlow.Reporting.Tests/Periods/LaunchCutoffTests.cs`
- `api/tests/StoneFlow.Reporting.Tests/Periods/ReportPeriodTests.cs`
- `api/tests/StoneFlow.Reporting.Tests/Sales/FakeReportSources.cs`
- `api/tests/StoneFlow.Reporting.Tests/Sales/GetSalesReportHandlerTests.cs`
- `api/tests/StoneFlow.Reporting.Tests/Sales/ListSalesComponentsHandlerTests.cs`
- `api/tests/StoneFlow.Reporting.Tests/Sales/SalesSummaryTests.cs`
- `app/src/app/features/dashboard/chart-canvas.spec.ts`
- `app/src/app/features/dashboard/chart-canvas.ts`
- `app/src/app/features/dashboard/chart-factory.ts`
- `app/src/app/features/dashboard/dashboard-page.spec.ts`
- `app/src/app/features/dashboard/dashboard.service.spec.ts`
- `app/src/app/features/dashboard/dashboard.service.ts`
- `app/src/app/features/dashboard/kpi-card.html`
- `app/src/app/features/dashboard/kpi-card.ts`
- `app/src/app/features/dashboard/period-picker.html`
- `app/src/app/features/dashboard/period-picker.spec.ts`
- `app/src/app/features/dashboard/period-picker.ts`
- `app/src/app/features/dashboard/report-models.ts`
- `app/src/app/features/dashboard/report-text.spec.ts`
- `app/src/app/features/dashboard/report-text.ts`
- `app/src/app/features/dashboard/reports-api.spec.ts`
- `app/src/app/features/dashboard/reports-api.ts`
- `app/src/app/features/dashboard/sales-components.html`
- `app/src/app/features/dashboard/sales-components.ts`
- `app/src/app/features/dashboard/sales-view.html`
- `app/src/app/features/dashboard/sales-view.ts`
- `app/src/testing/dashboard-testing.ts`

### Modified

- `api/src/Modules/Expenses/StoneFlow.Expenses.Api/Endpoints/ExpenseContracts.cs`
- `api/src/Modules/Expenses/StoneFlow.Expenses.Application/ListExpenses/ListExpenses.cs`
- `api/src/Modules/Expenses/StoneFlow.Expenses.Infrastructure/DependencyInjection.cs`
- `api/src/Modules/Expenses/StoneFlow.Expenses.Infrastructure/Persistence/ExpenseReader.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/DependencyInjection.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/Migrations/OrdersDbContextModelSnapshot.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderEntryConfiguration.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Api/ReportingModule.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Api/ReportingProblems.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/CashFlow/CashFlowPolicy.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Expenses/ExpensesApiTests.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Orders/OrderEntryRequests.cs`
- `api/tests/StoneFlow.Reporting.Tests/StoneFlow.Reporting.Tests.csproj`
- `api/tests/StoneFlow.Reporting.Tests/packages.lock.json`
- `app/package-lock.json`
- `app/package.json`
- `app/src/app/app.routes.spec.ts`
- `app/src/app/app.spec.ts`
- `app/src/app/features/dashboard/dashboard-page.html`
- `app/src/app/features/dashboard/dashboard-page.ts`
- `app/src/app/features/expenses/expense-models.ts`
- `app/src/app/features/expenses/expenses-list-page.html`
- `app/src/app/features/expenses/expenses-list-page.spec.ts`
- `app/src/app/features/expenses/expenses-list-page.ts`
- `app/src/app/features/expenses/expenses.service.spec.ts`
- `app/src/app/features/expenses/expenses.service.ts`
- `app/src/app/layout/app-shell.spec.ts`
- `app/src/app/shared/format/pl-format.spec.ts`
- `app/src/app/shared/format/pl-format.ts`

## Planned Units

| Pozycja planu | Jednostka | Status |
|---|---|---|
| B1 | Kontrakty `ISalesComponentSource`, `ICostSource`, rekordy i `SalesComponentKind` | Exists |
| B2 | `SalesEligibility` | Exists |
| B3 | `OrderSalesComponentSource` i rejestracja | Exists |
| B4 | Migracja i indeks `CorrectedOn` | Exists |
| B5 | `ExpenseCostSource`, `grossTotal` i rejestracja | Exists |
| B6 | `LaunchCutoff`, `ReportPeriod`, `DateRange`, przedziały | Exists |
| B7 | `Comparison` | Exists |
| B8 | `SalesComponent`, `SalesPolicy`, `SalesSummary`, `Cost`, `CostSummary`, `SalesResult` | Exists |
| B9 | `WarsawTime`, `SalesReportCodes`, `ReportPeriodInput` | Exists |
| B10 | `SalesReportSources` | Exists |
| B11 | `GetSalesReportHandler` | Exists |
| B12 | `ListSalesComponentsHandler` | Exists |
| B13 | Trasy `/api/reports/sales` i `/api/reports/sales/components` | Exists |
| B14 | `SalesReportContracts`, `report_range_too_large` | Exists |
| B15 | Testy integracyjne sprzedaży i wydatków | Exists |
| F-1 | `chart.js` 4.6.1 | Exists |
| F-2 | `report-models`, `ReportsApi` | Exists |
| F-3 | `formatPercent` | Exists |
| F-4 | `DashboardService` | Exists |
| F-5 | `report-text` | Exists |
| F-6 | `PeriodPicker` | Exists |
| F-7 | `KpiCard` | Exists |
| F-8 | `DashboardPage` i `/pulpit` | Exists |
| F-9 | `SalesComponents` | Exists |
| F-10 | Filtry i suma brutto na liście wydatków | Exists |
| F-11 | `ChartCanvas`, `CHART_FACTORY` | Exists |
| F-12 | `SalesView` | Exists |
| F-13 | Testy tras i powłoki | Exists |

## Database

- SQL Server, `OrdersDbContext`, migracja `20261109164727_SalesCorrectionDateIndex`. `Up` tworzy indeks `orders.SalesCorrections(CorrectedOn)` z `INCLUDE (Amount, OrderId)`, `Down` go usuwa. Oba pliki migracji mają UTF-8 z BOM. Zastosowanie migracji Orders do bazy demo nie zostało zweryfikowane.

## Tests

| Test z planu | Status | Wynik wykonania |
|---|---|---|
| O-1 `SalesEligibilityTests` | Exists | Passed w Orders.Tests |
| LC-1 i RP-1–RP-5, odcięcie, zakresy, bazy, granulacja, przedziały i daty graniczne | Exists | Passed w Reporting.Tests |
| CMP-1, zerowa i ujemna baza, zaokrąglenia | Exists | Passed w Reporting.Tests |
| S-1–S-12, płatności niezależne od sprzedaży, zwroty, korekty, anulowanie, uruchomienie, kanały i przedziały | Exists | Passed w Reporting.Tests |
| C-1, wydatki planowane, ujemne, uruchomienie, kategorie i wynik | Exists | Passed w Reporting.Tests |
| H-1 i H-2, walidacja, źródła, limit, strona, sort i suma | Exists | Passed w Reporting.Tests |
| I-S-1–I-S-15, raport i zestawienie na SQL | Exists | Failed — rozruch LocalDB, bez potwierdzenia asercji SQL |
| I-E-1, `grossTotal` i filtry wydatków | Exists | Failed — pierwsza próba zatrzymała się na komunikacji z runnerem |
| Regresje Operations, SalesCorrections, EndpointSecurity i OpenAPI | Exists | Zob. weryfikacja poniżej |
| F-1–F-6, format, klient, stan i okresy | Exists | Passed w Vitest |
| F-7–F-9, karty, Pulpit i zestawienie | Exists | Passed w Vitest, `dashboard-page.spec.ts` |
| F-10–F-13, wydatki, wykresy, stany i przejścia, trasy i powłoka | Exists | Passed w Vitest |
| Regresja zachowania lat po błędzie raportu | Exists | Passed w Vitest |
| E2E | Odroczone do SF-21 | Not run |

## Verification

| Polecenie | Wynik |
|---|---|
| `dotnet restore stone-flow.slnx` | Passed |
| `dotnet restore stone-flow.slnx --locked-mode` | Passed |
| `dotnet build stone-flow.slnx -c Release -m:1 --no-restore` | Passed, 0 błędów i ostrzeżeń |
| `dotnet format stone-flow.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release --no-build` | Passed, 167 |
| `dotnet test --project api/tests/StoneFlow.Orders.Tests -c Release --no-build` | Passed, 202 |
| `dotnet test --project api/tests/StoneFlow.Expenses.Tests -c Release --no-build` | Passed, 260 |
| `dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release --no-build` | Passed, 56 |
| `dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*SalesReportApiTests"` | Failed — LocalDB 0x89C5010A |
| `dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*ExpensesApiTests"` | Failed — komunikacja z runnerem w pierwszej próbie |
| `dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OperationsApiTests"` | Failed — komunikacja z runnerem w pierwszej próbie |
| `dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*SalesCorrectionsApiTests"` | Failed — komunikacja z runnerem w pierwszej próbie |
| `dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*EndpointSecurityTests"` | Passed, 9 |
| `dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OpenApiTests"` | Passed, 2 |
| `npm run typecheck` | Passed |
| `npm test -- --watch=false` | Passed poza sandboxem, 833 testy w 76 plikach |
| `npm run lint` | Passed |
| `npm run format:check` | Passed |
| `npm run build` | Passed, initial 425,94 kB, Chart.js w dynamicznie ładowanym chunku `DashboardPage` (213,76 kB) |
| `npm install --save-exact chart.js@4.6.1` / `npm ci` | Not run — skorzystano z istniejącej instalacji i przypiętej wersji |
| `dotnet ef migrations add … --configuration Release` | Not run — migracja już istniała, sprawdzono Up/Down i BOM |
| Pełne `dotnet test` i `deploy/scripts/test-migrate.sh` | Not run — zaplanowane w CI |
| `git diff --check` | Passed |

## Deviations

- Przypadki F-7, F-9 i F-12 są w `dashboard-page.spec.ts`. Wspólne dane testowe są w `app/src/testing/dashboard-testing.ts`.
- Powtórzono weryfikację poza sandboxem po błędach plików tymczasowych i nazwanych potoków.

## Additional Implementation

None.

## Open Items

1. Human review required before merge.
2. Powtórzyć testy integracyjne SQL z działającą instancją LocalDB albo w CI i wykonać ręczne scenariusze UI.
3. Potwierdzić decyzję 7 z właścicielem. Czy bieżący rok porównujemy z tym samym odcinkiem poprzedniego roku, a zakończony rok z całym poprzednim rokiem? Odnotować w opisie PR.

## Scenariusze ręczne UI

Warunki: lokalny klient i API, aktywna sesja i dane demo w `StoneFlowDev`. Kwoty poniżej dotyczą samych danych ze skryptu demo, bez innych rekordów i przy dacie uruchomienia nie późniejszej niż początek poprzedniego miesiąca. Do prób zmian użyj fikcyjnych zamówień testowych. Status każdego scenariusza: **Not run**.

| ID | Kroki | Oczekiwany wynik |
|---|---|---|
| M-01 | Na Pulpicie przełącz bieżący i poprzedni miesiąc, bieżący rok i własny zakres. Wprowadź też brak daty, „od” po „do” i koniec ponad rok od dziś. | Miesiące obejmują pełne miesiące, rok bieżący kończy się dziś. Poprawna zmiana zeruje filtry i stronę zestawienia. Błędy są przy polach. Przy porównaniu roku bieżącego baza kończy się w analogicznym dniu poprzedniego roku. |
| M-02 | Wybierz miesiąc importu demo. Kliknij „Sprzedaż”, „Liczba zamówień” i kanał OLX. Porównaj kartę, sumę zestawienia i tabelę wykresu. | Sprzedaż 23 470 zł, 5 zamówień, OLX 3 920 zł. Zestawienie sprzedaży pokazuje wszystkie składniki, karta liczby ustawia rodzaj „Zamówienie”, kanał filtruje do OLX. Suma podziału na kanały równa się sprzedaży. |
| M-03 | W miesiącu importu kliknij kartę „Koszty”, wróć i kliknij kategorię „Zakup materiału u dostawców”. Przejdź na listę wydatków. | Karta i suma filtrowanej listy 6 273 zł. Suma kategorii 3 567 zł. Lista zawiera wydatki opłacone i planowane. Wynik przed wypłatami 17 197 zł. |
| M-04 | Z filtrowanego zestawienia otwórz zamówienie i wróć na Pulpit. | Zachowane widok, okres, filtr, kierunek i strona. Raport pobiera dane ponownie i aktualizuje „Stan na”. |
| M-05 | Na każdym wykresie ustaw fokus klawiszem Tab. Sprawdź strzałki, Home, End i Escape z czytnikiem ekranu. Rozwiń tabelę wykresu czasu. | Punkt i tooltip reagują na klawiaturę. Czytnik ogłasza etykietę i sformatowaną kwotę. Escape chowa tooltip. Tabela zawiera „Razem”, listy kanałów i kategorii są dostępne obok wykresów. |
| M-06 | Otwórz Pulpit na telefonie, tablecie i szerokim ekranie. Włącz ograniczenie ruchu w systemie lub emulacji przeglądarki. | Karty i kontrolki układają się zgodnie z szerokością, a tabela zachowuje dostęp do kolumn. Wykresy i przejścia do zestawienia respektują ograniczenie ruchu. |
| M-07 | W środowisku testowym użyj stanu integracji z niepełnymi danymi, następnie poprawnego i pełnego. | Przy niepełnych danych widnieje ostrzeżenie i przejście do integracji. „Ostatnia pełna kontrola” jest widoczna także przy pełnych danych. |
| M-08 | Wybierz zakres w całości przed datą uruchomienia, potem zakres przecinający tę datę. | W pierwszym przypadku komunikat bez kart i tabeli z zerami. W drugim widoczna data przycięcia, a koszty otwierają listę od efektywnej daty. |
| M-09 | Na fikcyjnym zamówieniu z poprzedniego miesiąca zapisz korektę w bieżącym. Otwórz raport obu miesięcy, potem anuluj i przywróć zamówienie. | Korekta obniża bieżący miesiąc i kanał, a wartość poprzedniego miesiąca jest bez zmian przed anulowaniem. Anulowanie wyłącza zamówienie i korektę, przywrócenie je ponownie uwzględnia. |
| M-10 | Przy konfiguracji z wcześniejszą datą uruchomienia wybierz poprzedni rok. Zasymuluj błąd żądania raportu, wybierz inny rok i przywróć API. | Podczas błędu selektor pokazuje zapamiętany zakres lat i wybraną wartość. Po zmianie roku można pobrać poprawny raport. |
