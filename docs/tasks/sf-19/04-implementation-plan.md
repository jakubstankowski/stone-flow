# Implementation Plan — SF-19

Tryb: **fullstack**. Zadanie dodaje nowe kontrakty międzymodułowe (`ISalesComponentSource`,
`ICostSource`), reguły domeny Reporting, dwa endpointy raportu, pole `grossTotal` listy wydatków i UI
Pulpitu z wykresami. Plany backendu i frontendu napisał jeden autor po kolei (bez subagentów):
najpierw backend, potem frontend z kontraktem wyprowadzonym ze spec i z rozstrzygnięć poniżej (nie z
planu backendu). Oba porównano w „Integration Check”.

Zadanie nie jest dzielone na części. Migracja to jeden indeks (addytywna), `grossTotal` jest polem
dodatkowym, backend wdraża się przed frontendem, a Pulpit do tej pory był zaślepką, więc nie ma
starego zachowania do zachowania. Ryzyko `high`: **przegląd człowieka przed merge**.

## Scope

- **Shared:** `SalesComponentKind` (Kernel), `SalesComponentRecord` + `ISalesComponentSource`,
  `CostRecord` + `ICostSource` (Application).
- **Orders:** reguła `SalesEligibility.CountsTowardSales` (Domain), źródło
  `OrderSalesComponentSource` (Infrastructure), indeks `IX_SalesCorrections_CorrectedOn` z migracją.
- **Expenses:** źródło `ExpenseCostSource`, pole `grossTotal` w `GET /api/expenses`
  (`ExpenseReader`, `ListExpenses`, `ExpenseContracts`).
- **Reporting:** reguły okresów (`Periods/`), sprzedaży (`Sales/`), kosztów (`Costs/`), delegacja
  `CashFlowPolicy.EffectiveFrom`; przypadki `GetSalesReport` i `ListSalesComponents`; endpointy
  `GET /api/reports/sales` i `GET /api/reports/sales/components`.
- **Frontend:** `features/dashboard/` (przełącznik widoku, wybór okresu, karty, wykresy Chart.js z
  alternatywą tekstową, podziały, zestawienie), `features/expenses/` (parametry z Pulpitu, „Suma
  brutto”), `shared/format/formatPercent`, zależność `chart.js@4.6.1`.

### Rozstrzygnięcia uwag z przeglądu (`03-spec-review.md`)

| # | Uwaga przeglądu | Decyzja | Gdzie w planie |
|---|---|---|---|
| 1 | Reguła `CountsTowardSales` zdublowana w predykacie SQL | **Bez predykatu SQL.** Projekcja w `OrderSalesComponentSource` pobiera `FulfillmentStatus` i `ShopStatus` zamówienia, a flagę `OrderCountsTowardSales` liczy po materializacji `SalesEligibility.CountsTowardSales(status, shopStatus)` z `Orders.Domain`. Reguła istnieje w jednym miejscu; test zgodności SQL ↔ domena przestaje być potrzebny. Testy: jednostkowy pełnej macierzy (w tym `null`, nieznany status, `on-hold`, `refunded`) i integracyjny na prawdziwej bazie: zamówienie ręczne (`ShopStatus = NULL`), status spoza słownika (`custom-status`), `on-hold`, `refunded`, `pending`, `failed`, `checkout-draft`, anulowane. | B3, B6, testy O-1, I-S-4 |
| 2 | Brak górnej granicy własnego zakresu | **`to` ≤ dziś + 1 rok** (Europe/Warsaw, `DateOnly.AddYears(1)`), inaczej `400` przy `to`: „Data końcowa nie może być późniejsza niż {dd.MM.yyyy} (rok od dziś).”. Uzasadnienie: (a) dolny koniec ogranicza już data uruchomienia (efektywny początek), więc liczba przedziałów rośnie tylko z wiekiem systemu (np. 10 lat → ok. 130 przedziałów miesięcznych), a nie z wpisanej daty; (b) wpisy z datą w przyszłości (data kosztu, data zamówienia) są dozwolone, więc rok zapasu nie odcina realnych danych; (c) wyklucza `9999-12-31` i przepełnienie `DateOnly.AddMonths` przy przedziałach; (d) limit długości (np. 5 lat) i tak wymagałby osobnego zabezpieczenia daty końcowej. Granica jest w jednym miejscu: `ReportPeriod.LatestEnd(today)` (Domain), sprawdza ją `ReportPeriodInput` (oba endpointy), a UI ustawia `max` pola i ten sam komunikat. | B9, B11, testy RP-5, H-1, F-3 |
| 3 | Endpoint zestawienia: zakres i przełom uruchomienia | `GET /api/reports/sales/components` przyjmuje **te same parametry okresu** co `/api/reports/sales` (`period`, `year`, `from`, `to`) i sam je rozwiązuje przez `ReportPeriod.Resolve` (efektywny początek = max(`from`, data uruchomienia)). Frontend nie przepisuje dat z odpowiedzi raportu, tylko wysyła ten sam wybór okresu. Odpowiedź zawiera ten sam obiekt `period` co raport. Okres w całości przed uruchomieniem: `200` z `period.beforeLaunch = true`, `items = []`, `total = 0`, `sum = null` (brak danych ≠ zero, jak `kpis = null` w raporcie); źródła nie są odpytywane. Zmienia to kontrakt ze spec (`from`/`to` wymagane, pola `effectiveFrom`/`launchDate` na górnym poziomie) — patrz Risks. | B9, B12, B14, F-2, F-4, testy H-2, I-S-9 |
| 4 | Powrót na Pulpit a aktualność | Stan Pulpitu w `DashboardService` (`providedIn: 'root'`, wzór `OperationsService`): widok, wybór okresu, filtry, kierunek i strona zestawienia. Każde wejście na `/pulpit` (konstruktor strony) wywołuje `enter()`: ponowny odczyt raportu i bieżącej strony zestawienia z zachowanym stanem, więc „Stan na …” jest świeży. „Nie odpytuje API w tle” dotyczy wyłącznie cyklicznego odświeżania (go nie ma). | F-4, F-8, testy F-4, F-8 |
| 5 | `SalesComponentRecord.Amount` „dodatnia” | Dokumentacja kontraktu: kwota zapisana **nieujemna** (zamówienie może mieć 0 zł; zwrot i korekta > 0 z `CHECK` w bazie). Znak nadaje Reporting (`SalesPolicy.SignedAmount`). | B1, test S-12 |
| 6 | Wskazówka „Pokaż zamówienia” | Karta „Liczba zamówień” ma wskazówkę **„Pokaż w zestawieniu”**. | F-7, test F-7 |
| 7 | Rok bieżący vs ten sam odcinek poprzedniego roku | Reguła zamknięta w jednym miejscu: `ReportPeriod.Resolve` (gałąź `Year` z `InProgress`). Zmiana na „cały poprzedni rok” dotyczy jednej gałęzi i testów `RP-2`. Frontend nie liczy okresu bazowego — opis porównania składa z `comparison.from/to` z API. **Otwarty punkt dla właściciela:** wpisać w `06-implementation-done.md` (sekcja otwartych punktów) i jako pytanie w opisie PR. | B8, F-6, Verification |

Niejasności z przeglądu:

| Niejasność | Rozstrzygnięcie | Gdzie |
|---|---|---|
| „Pozostałe filtry czyszczą” przy wejściu na `/wydatki` z Pulpitu | Wejście z parametrami `kosztOd`/`kosztDo`/`kategoria` ustawia filtry `EMPTY_FILTERS` + daty kosztu i kategorie, **sortowanie domyślne** (data kosztu malejąco) i **stronę 1**. Każdy niepoprawny parametr jest pomijany osobno (data nie `yyyy-MM-dd`, `kosztOd` > `kosztDo` → obie daty pominięte, nieznana kategoria); gdy żaden nie przeszedł, lista i tak startuje od filtrów i sortowania domyślnego. | F-10, test F-10 |
| „Wariant dla liczby całkowitej” w `Comparison.Of` | `Comparison.Of(int current, int base)` zamienia wartości na `decimal` i woła tę samą metodę co dla kwot (jedno zaokrąglenie `MidpointRounding.AwayFromZero`, zero `double`). Wynik `-0.0` normalizowany do `0.0`. | B8, test CMP-1 |
| Suma zestawienia z filtrem „Zwrot sprzedaży”/„Korekta sprzedaży” jest ujemna | Zgodne z pkt 10; etykieta bez zmian. Przypadek w testach H-2 i F-9 (suma `-300.00` → „-300,00 zł”). | B14, F-9 |
| Konwersja tekst → liczba dla Chart.js | Jedynie w `chart-canvas.ts` (adapter), w jednej funkcji `chartNumber(text)`; tooltip, aria-live, tabela i listy pokazują ciągi z API przez `formatPln`. | F-11, test F-11 |
| Kanał i data zamówienia zwrotu/korekty z bieżącego stanu zamówienia (zmiana kanału przenosi obniżenia) | Źródło łączy wpis z bieżącym wierszem zamówienia; przypadek dopisany do Edge cases i testu I-S-6. | B6, I-S-6 |

Inne przyjęte rozstrzygnięcia (z kodu):

- **Klasyfikacja zwrotu:** źródło czyta tylko `Refunds` z `Kind == RefundKind.SalesReturn` (filtr po
  samym polu rodzaju; `Refund.SalesReductionOf` jest zdefiniowana właśnie przez ten rodzaj).
  Zwroty pieniędzy nie zużywają limitu wierszy.
- **Kody 422:** nowy kod `report_range_too_large` obok istniejącego `operations_range_too_large`
  (SF-17 bez zmian).
- **Znak minus kwot:** `formatPln` daje `-` (U+002D), jak na listach SF-15/SF-17 (decyzja SF-17,
  `06-implementation-done.md`). Spec wspomina U+2212 w kolumnie „Kwota” — formater bez zmian, zob.
  Risks. `formatPercent` daje `−` (U+2212), bo spec podaje to wprost w jego przykładzie (`"-3.0"` →
  `−3,0%`).

## Backend Plan

### Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Kernel | `StoneFlow.Shared.Kernel/Sales/` | słownik `SalesComponentKind` (używa go `Reporting.Domain`) |
| Shared Application | `StoneFlow.Shared.Application/Sales/`, `/Costs/` | kontrakty źródeł sprzedaży i kosztów |
| Domain | `Orders.Domain/Orders/` | reguła `SalesEligibility.CountsTowardSales` |
| Infrastructure | `Orders.Infrastructure/Persistence` | `OrderSalesComponentSource`, indeks `CorrectedOn`, migracja, DI |
| Application / Infrastructure / Api | `Expenses` | `grossTotal` listy; `ExpenseCostSource`, DI |
| Domain | `Reporting.Domain/Periods/`, `/Sales/`, `/Costs/`, `/CashFlow/` | okresy, porównanie, sprzedaż, koszty, wynik; delegacja `EffectiveFrom` |
| Application | `Reporting.Application/Sales/` | walidacja okresu, odczyt źródeł z bezpiecznikiem, dwa zapytania |
| Api | `Reporting.Api` | `SalesReportEndpoints`, kontrakty, `report_range_too_large` |
| Tests | `StoneFlow.Reporting.Tests`, `StoneFlow.Orders.Tests`, `StoneFlow.Api.IntegrationTests` | domena, aplikacja, HTTP i baza |

Reporting nie dostaje zmian schematu ani DbContextu (`DbContextSchemaTests` bez zmian).

### API contract (backend)

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/reports/sales` | `period`, `year`, `from`, `to` | `200 SalesReportResponse`; `400`; `422 report_range_too_large` | sesja (fallback policy) | Reporting, jedna firma |
| GET | `/api/reports/sales/components` | `period`, `year`, `from`, `to`, `channel`, `kind`, `direction`, `page`, `pageSize` | `200 SalesComponentsPageResponse`; `400`; `422 report_range_too_large` | sesja | Reporting |
| GET | `/api/expenses` (zmiana addytywna) | bez zmian | dotychczasowe pola + `grossTotal` | sesja | Expenses |

Szczegóły kształtów — sekcja „API Contract”.

### Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Shared/StoneFlow.Shared.Kernel/Sales/SalesComponentKind.cs` | create | `enum SalesComponentKind { Order, SalesReturn, SalesCorrection }` — kolejność deklaracji = remis sortowania |
| `api/src/Shared/StoneFlow.Shared.Application/Sales/SalesComponentRecord.cs` | create | `record SalesComponentRecord(Kind, Id, Date, Amount, OrderId, OrderNumber, OrderDate, Channel, OrderCountsTowardSales)`; `Amount` nieujemna (decyzja 5) |
| `api/src/Shared/StoneFlow.Shared.Application/Sales/ISalesComponentSource.cs` | create | `Channels`, `ListAsync(from, to, limit, ct)` |
| `api/src/Shared/StoneFlow.Shared.Application/Costs/CostRecord.cs` | create | `record CostRecord(Guid Id, DateOnly CostDate, decimal GrossAmount, string Category)` |
| `api/src/Shared/StoneFlow.Shared.Application/Costs/ICostSource.cs` | create | `Categories`, `ListAsync(from, to, limit, ct)` |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/SalesEligibility.cs` | create | `CountsTowardSales(FulfillmentStatus, string? shopStatus)` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderSalesComponentSource.cs` | create | `ISalesComponentSource` (zamówienia, zwroty sprzedaży, korekty) |
| `…/Orders.Infrastructure/Persistence/OrderEntryConfiguration.cs` | modify | `SalesCorrectionConfiguration`: `IX_SalesCorrections_CorrectedOn (CorrectedOn) INCLUDE (Amount, OrderId)` |
| `…/Orders.Infrastructure/Persistence/Migrations/<ts>_SalesCorrectionDateIndex.cs` (+ `.Designer.cs`, `OrdersDbContextModelSnapshot.cs`) | create/modify | `CreateIndex` / `DropIndex` |
| `…/Orders.Infrastructure/DependencyInjection.cs` | modify | `AddScoped<ISalesComponentSource, OrderSalesComponentSource>()` |
| `api/src/Modules/Expenses/StoneFlow.Expenses.Application/ListExpenses/ListExpenses.cs` | modify | `ExpenseRowsPage.GrossTotal`, `ExpenseListPageDto.GrossTotal`, przekazanie w handlerze |
| `…/Expenses.Infrastructure/Persistence/ExpenseReader.cs` | modify | `SumAsync(GrossAmount)` na tym samym `Filter(...)` (także przy stronie za końcem) |
| `…/Expenses.Infrastructure/Persistence/ExpenseCostSource.cs` | create | `ICostSource` |
| `…/Expenses.Infrastructure/DependencyInjection.cs` | modify | `AddScoped<ICostSource, ExpenseCostSource>()` |
| `…/Expenses.Api/Endpoints/ExpenseContracts.cs` | modify | `ExpenseListPageResponse(..., string GrossTotal)` przez `ExpenseInput.FormatAmount` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Periods/LaunchCutoff.cs` | create | `EffectiveFrom(from, launchDate)` = max |
| `…/Reporting.Domain/Periods/ReportPeriod.cs` | create | `PeriodKind`, `Granularity`, `DateRange`, `ReportPeriod` + `Resolve`, `Buckets()`, `LatestEnd(today)` |
| `…/Reporting.Domain/Periods/Comparison.cs` | create | `ComparisonStatus`, `Comparison` + `Of(decimal, decimal)`, `Of(int, int)` |
| `…/Reporting.Domain/Sales/SalesComponent.cs` | create | fakt składnika (Kind, Id, Date, Amount, OrderId, OrderNumber, OrderDate, Channel, OrderCountsTowardSales) |
| `…/Reporting.Domain/Sales/SalesPolicy.cs` | create | `Includes(component, effectiveFrom, to, launchDate)`, `SignedAmount(component)`, `SumOf(components)` |
| `…/Reporting.Domain/Sales/SalesSummary.cs` | create | `SalesSummary.Of(components, period, channels)` → sprzedaż, liczba zamówień, kanały, przedziały |
| `…/Reporting.Domain/Sales/SalesResult.cs` | create | wynik przed wypłatami dla karty i przedziałów |
| `…/Reporting.Domain/Costs/Cost.cs` | create | fakt kosztu (Id, CostDate, GrossAmount, Category) |
| `…/Reporting.Domain/Costs/CostSummary.cs` | create | `CostSummary.Of(costs, period, categories)` |
| `…/Reporting.Domain/CashFlow/CashFlowPolicy.cs` | modify | `EffectiveFrom` deleguje do `LaunchCutoff.EffectiveFrom` (bez zmiany API) |
| `…/Reporting.Application/WarsawTime.cs` | create | `internal static Today(TimeProvider)` na `WarsawTimeZone.Zone` (wzór Expenses) |
| `…/Reporting.Application/Sales/SalesReportCodes.cs` | create | kody okresów, granulacji, rodzajów składników, statusu porównania; `FormatPercent`; daty i kwoty przez `OperationCodes` |
| `…/Reporting.Application/Sales/ReportPeriodInput.cs` | create | walidacja `period`/`year`/`from`/`to` → `ReportPeriod?` (wspólna dla obu zapytań) |
| `…/Reporting.Application/Sales/SalesReportSources.cs` | create | odczyt źródeł z limitem 10 001 i mapowanie na fakty; `null` = bezpiecznik |
| `…/Reporting.Application/Sales/GetSalesReport.cs` | create | `GetSalesReportQuery`, DTO, wynik, `GetSalesReportHandler` |
| `…/Reporting.Application/Sales/ListSalesComponents.cs` | create | `ListSalesComponentsQuery`, DTO, wynik, `ListSalesComponentsHandler` |
| `…/Reporting.Api/Endpoints/SalesReportEndpoints.cs` | create | grupa `/api/reports/sales`, dwie trasy |
| `…/Reporting.Api/Endpoints/SalesReportContracts.cs` | create | odpowiedzi i mapowanie DTO → JSON |
| `…/Reporting.Api/ReportingProblems.cs` | modify | `ReportRangeTooLarge()` (`report_range_too_large`, „Za dużo danych w wybranym okresie. Wybierz krótszy okres.”) |
| `…/Reporting.Api/ReportingModule.cs` | modify | `SalesReportEndpoints.Map(endpoints)`; komentarz |
| `api/tests/StoneFlow.Reporting.Tests/Periods/LaunchCutoffTests.cs` | create | LC-1 |
| `api/tests/StoneFlow.Reporting.Tests/Periods/ReportPeriodTests.cs` | create | RP-1 … RP-5 |
| `api/tests/StoneFlow.Reporting.Tests/Periods/ComparisonTests.cs` | create | CMP-1 |
| `api/tests/StoneFlow.Reporting.Tests/Sales/SalesSummaryTests.cs` | create | S-1 … S-12 (PRD §11) |
| `api/tests/StoneFlow.Reporting.Tests/Costs/CostSummaryTests.cs` | create | C-1 |
| `api/tests/StoneFlow.Reporting.Tests/Sales/FakeReportSources.cs` | create | fałszywe `ISalesComponentSource`/`ICostSource` z zapisem wywołań, `ILaunchDate`, `FakeTimeProvider` |
| `api/tests/StoneFlow.Reporting.Tests/Sales/GetSalesReportHandlerTests.cs` | create | H-1 |
| `api/tests/StoneFlow.Reporting.Tests/Sales/ListSalesComponentsHandlerTests.cs` | create | H-2 |
| `api/tests/StoneFlow.Reporting.Tests/StoneFlow.Reporting.Tests.csproj` (+ `packages.lock.json`) | modify | `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />` (wersja centralna 10.11.0 w `Directory.Packages.props`, jak `StoneFlow.Access.Tests`); `dotnet restore` odświeża lock |
| `api/tests/StoneFlow.Orders.Tests/Domain/SalesEligibilityTests.cs` | create | O-1 |
| `api/tests/StoneFlow.Api.IntegrationTests/Orders/OrderEntryRequests.cs` | modify | `Order(value, …, orderDate = "2026-10-28", channel = "olx")` — opcjonalne parametry, domyślne bez zmian |
| `api/tests/StoneFlow.Api.IntegrationTests/Reporting/SalesReportApiTests.cs` | create | I-S-1 … I-S-14 |
| `api/tests/StoneFlow.Api.IntegrationTests/Expenses/ExpensesApiTests.cs` | modify | I-E-1 (`grossTotal`) |

Bez zmian: `stone-flow.slnx`, `.csproj` w `api/src` (referencje już są: Orders/Expenses.Infrastructure
→ Application → `Shared.Application` → `Shared.Kernel`; `Reporting.Domain` → Kernel;
`Reporting.Application` → Domain + `Shared.Application`), `deploy/scripts/contexts.sh`,
`EndpointSecurityTests.AllowedAnonymous`, `StoneFlow.Architecture.Tests`.

### Implementation steps

Kolejność TDD: każdy krok domeny i aplikacji zaczyna się od czerwonego testu (sekcja „Tests”).

- **B1 Kontrakty współdzielone.**
  - `SalesComponentKind { Order, SalesReturn, SalesCorrection }` (Kernel, `namespace StoneFlow.Shared.Kernel.Sales`).
  - `SalesComponentRecord(SalesComponentKind Kind, Guid Id, DateOnly Date, decimal Amount, Guid OrderId, string OrderNumber, DateOnly OrderDate, string Channel, bool OrderCountsTowardSales)`.
    XML doc: `Amount` — kwota zapisana, **nieujemna** (zamówienie może mieć 0 zł; zwrot i korekta
    > 0), znak nadaje Reporting; `Date` — data zamówienia / zwrotu / korekty; `Channel` — kod kanału
    zamówienia z bieżącego stanu; flaga z bieżącego stanu zamówienia.
  - `ISalesComponentSource`: `IReadOnlyList<string> Channels { get; }` (kody w kolejności
    prezentacji); `Task<IReadOnlyList<SalesComponentRecord>> ListAsync(DateOnly from, DateOnly to, int limit, CancellationToken)`
    — zamówienia z datą zamówienia w zakresie, zwroty sprzedaży i korekty z datą w zakresie, także
    zamówień nieliczonych (flaga `false`); łącznie ≤ `limit` (SQL `TOP`), tabelami po kolei.
  - `CostRecord(Guid Id, DateOnly CostDate, decimal GrossAmount, string Category)`;
    `ICostSource`: `Categories`, `ListAsync(from, to, limit, ct)` — wszystkie wydatki z datą kosztu
    w zakresie (opłacone i nie), kwota brutto ze znakiem, ≤ `limit`.
- **B2 `SalesEligibility`** (O-1 najpierw), `Orders.Domain/Orders/SalesEligibility.cs`:
  `public static bool CountsTowardSales(FulfillmentStatus fulfillment, string? shopStatus) => fulfillment != FulfillmentStatus.Cancelled && !ShopStatuses.IsHiddenByDefault(shopStatus);`
  XML doc: PRD §8 „Sprzedaż”; `null` (zamówienie ręczne) i statusy spoza `HiddenByDefault` (w tym
  nieznane, `on-hold`, `refunded`, `cancelled` w sklepie przy zamówieniu przywróconym w ERP) liczą
  się; decyduje status realizacji ERP. `OrderReader` (filtr listy) bez zmian — to osobna reguła
  widoczności listy.
- **B3 `OrderSalesComponentSource(OrdersDbContext)`** (wzór `OrderCashOperationSource`; `AsNoTracking`,
  projekcja do typu anonimowego, potem mapowanie w pamięci):
  - `Channels` = `Enum.GetValues<OrderChannel>().Select(OrderCodes.Of)` (kolejność słownika).
  - Zamówienia: `Orders.Where(o => o.OrderDate >= from && o.OrderDate <= to).OrderBy(OrderDate).ThenBy(Id).Take(limit)`,
    kolumny `Id, Number, OrderDate, Channel, Value, FulfillmentStatus, ShopStatus` →
    `new SalesComponentRecord(Order, o.Id, o.OrderDate, o.Value, o.Id, o.Number, o.OrderDate, OrderCodes.Of(o.Channel), SalesEligibility.CountsTowardSales(o.FulfillmentStatus, o.ShopStatus))`.
  - `remaining = limit − wczytane`; przy 0 kolejne tabele nie są odpytywane (bez `TOP 0`).
  - Zwroty sprzedaży: `Refunds.Where(r => r.Kind == RefundKind.SalesReturn && r.Date >= from && r.Date <= to)`
    `.Join(Orders, …)` z kolumnami wpisu i zamówienia (`Number, OrderDate, Channel, FulfillmentStatus, ShopStatus`),
    `OrderBy(Date).ThenBy(Id).Take(remaining)`.
  - Korekty: `SalesCorrections.Where(Date in range).Join(Orders, …)`, jak wyżej.
  - **Bez filtra statusu, anulowania ani daty uruchomienia w SQL** (decyzja 1; przełom uruchomienia
    stosuje Reporting). Zwroty pieniędzy i wpłaty nie są czytane.
  - Zapytania sekwencyjnie (wspólne `ModuleConnection`).
- **B4 Indeks i migracja Orders.** W `SalesCorrectionConfiguration`:
  `builder.HasIndex(c => c.Date).IncludeProperties(c => new { c.Amount, c.OrderId }).HasDatabaseName("IX_SalesCorrections_CorrectedOn");`
  Migracja (z katalogu repozytorium):
  `dotnet ef migrations add SalesCorrectionDateIndex -p api/src/Modules/Orders/StoneFlow.Orders.Infrastructure -s api/src/StoneFlow.Api -c OrdersDbContext -o Persistence/Migrations --configuration Release`.
  Tylko `CreateIndex` (`Down`: `DropIndex`). Pliki migracji w UTF-8 z BOM (jak istniejące).
  Rejestracja źródła w `Orders.Infrastructure/DependencyInjection.cs` (scoped).
- **B5 Expenses — `grossTotal` i źródło kosztów.**
  - `ExpenseRowsPage(Rows, Total, decimal GrossTotal)`; `ExpenseListPageDto(Items, Page, PageSize, Total, decimal GrossTotal)`;
    `ListExpensesHandler` przepisuje wartość.
  - `ExpenseReader.ListAsync`: po `CountAsync` — `var grossTotal = await expenses.SumAsync(e => e.GrossAmount, ct);`
    na tym samym `Filter(...)` (wszystkie filtry, także stan „po terminie” i wyszukiwanie);
    gałąź `skip >= total` też zwraca `grossTotal`. Pusty wynik → `0` (EF tłumaczy `SUM` z
    `COALESCE`); dwa zapytania po kolei.
  - `ExpenseListPageResponse(Items, Page, PageSize, Total, string GrossTotal)` przez
    `ExpenseInput.FormatAmount` (`"0.00"` dla pustego wyniku). Zmiana addytywna — stary frontend ją
    ignoruje.
  - `ExpenseCostSource(ExpensesDbContext)`: `Categories` = `Enum.GetValues<ExpenseCategory>().Select(ExpenseCodes.Of)`;
    `ListAsync`: `Expenses.Where(CostDate in [from, to]).OrderBy(CostDate).ThenBy(Id).Take(limit)`
    → `new CostRecord(e.Id, e.CostDate, e.GrossAmount, ExpenseCodes.Of(e.Category))`. Bez filtra
    zapłaty (opłacone i nieopłacone, wystąpienia cykliczne to zwykłe wydatki). Wypłaty
    (`OwnerWithdrawals`) nie są czytane. Ten sam zbiór co `ExpenseReader` z filtrem daty kosztu
    (brak miękkiego usuwania) — uzgodnienie potwierdza I-S-10.
  - Rejestracja w `Expenses.Infrastructure/DependencyInjection.cs` (scoped).
- **B6 Reporting.Domain — okresy** (LC-1, RP-* najpierw; bez I/O):
  - `LaunchCutoff.EffectiveFrom(DateOnly from, DateOnly launchDate)` = max;
    `CashFlowPolicy.EffectiveFrom` → `LaunchCutoff.EffectiveFrom` (testy SF-17 bez zmian).
  - `enum PeriodKind { CurrentMonth, PreviousMonth, Year, Custom }`, `enum Granularity { Day, Month }`,
    `record DateRange(DateOnly From, DateOnly To)` (`Contains(date)`, `Days`).
  - `ReportPeriod` (record): `Kind`, `Range` [`from`, `to`], `EffectiveFrom`, `LaunchDate`,
    `BeforeLaunch` (`EffectiveFrom > To`), `InProgress`, `Granularity`, `Base` (`DateRange`),
    `BaseAvailable` (`Base.From >= LaunchDate`), `Effective` (`DateRange(EffectiveFrom, To)`; nie
    używać przy `BeforeLaunch`).
  - `ReportPeriod.LatestEnd(DateOnly today)` = `today.AddYears(1)` (decyzja 2).
  - `ReportPeriod.Resolve(PeriodKind kind, int? year, DateOnly? from, DateOnly? to, DateOnly today, DateOnly launchDate)`:
    - `CurrentMonth`: [1. dzień miesiąca `today`, ostatni dzień]; baza = cały poprzedni miesiąc;
      `InProgress = true`.
    - `PreviousMonth`: cały miesiąc przed bieżącym; baza = cały miesiąc przed nim.
    - `Year` (`year ?? today.Year`; > `today.Year` → `ArgumentOutOfRangeException`, walidacja jest
      wcześniej): bieżący → [01.01, `today`], baza [01.01 (y−1), `today.AddYears(-1)`] (29.02 →
      28.02 przez `DateOnly.AddYears`), `InProgress = true`; wcześniejszy → [01.01, 31.12], baza =
      cały rok y−1 (**decyzja 7: jedyne miejsce tej reguły**).
    - `Custom`: [`from`, `to`] (wymagane, `from ≤ to`, `to ≤ LatestEnd`); baza = tyle samo dni
      co **efektywny** zakres tuż przed efektywnym początkiem
      (`[EffectiveFrom − n, EffectiveFrom − 1]`, `n = To.DayNumber − EffectiveFrom.DayNumber + 1`);
      przy `BeforeLaunch` baza = `n` dni przed `from` (i tak `BaseAvailable = false`).
    - `EffectiveFrom = LaunchCutoff.EffectiveFrom(from, launchDate)`.
    - `Granularity`: miesiące (bieżący, poprzedni) → `Day`; `Year` → `Month`; `Custom` → `Day`
      dla efektywnego zakresu ≤ 62 dni (`CustomDailyMaxDays = 62`), inaczej `Month`.
  - `Buckets()` → `IReadOnlyList<DateRange>` pokrywające [`EffectiveFrom`, `To`] bez luk: dni albo
    miesiące kalendarzowe przycięte do zakresu (pierwszy i ostatni mogą być niepełne); bieżący
    miesiąc — do ostatniego dnia miesiąca (także po dziś); bieżący rok — do bieżącego miesiąca
    włącznie (ostatni przedział kończy się dziś). `BeforeLaunch` → pusta lista.
- **B7 Reporting.Domain — porównanie** (CMP-1 najpierw): `enum ComparisonStatus { Available, NoBasis }`;
  `record Comparison(ComparisonStatus Status, decimal? Base, decimal? Difference, decimal? Percent)`;
  `Of(decimal current, decimal @base)`: baza 0 → `NoBasis` (bez różnicy i procentu); inaczej
  różnica = current − base, procent = `Math.Round(difference / Math.Abs(base) * 100m, 1, MidpointRounding.AwayFromZero)`,
  zero normalizowane (`-0.0` → `0.0`); `Of(int, int)` → `Of((decimal)…, (decimal)…)`;
  `static Comparison NoBasis` dla bazy sprzed uruchomienia.
- **B8 Reporting.Domain — sprzedaż, koszty, wynik** (S-*, C-* najpierw):
  - `SalesComponent` (record, pola jak `SalesComponentRecord`, `Channel` jako kod tekstowy).
  - `SalesPolicy.Includes(c, effectiveFrom, to, launchDate)` = `c.OrderCountsTowardSales && c.OrderDate >= launchDate && c.Date >= effectiveFrom && c.Date <= to`.
  - `SalesPolicy.SignedAmount(c)`: `Order` → `+Amount`; `SalesReturn`, `SalesCorrection` → `−Amount`.
  - `SalesPolicy.SumOf(IEnumerable<SalesComponent>)` = Σ `SignedAmount` (jedna reguła sumy dla
    karty, kanałów, przedziałów i zestawienia).
  - `SalesSummary.Of(components, ReportPeriod period, IReadOnlyList<string> channels)` →
    `record SalesSummary(decimal Sales, int OrderCount, IReadOnlyList<ChannelAmount> Channels, IReadOnlyList<decimal> BucketSales)`:
    filtr `Includes` z `period.EffectiveFrom`, `period.Range.To`, `period.LaunchDate`;
    `OrderCount` = liczba włączonych rodzaju `Order`; kanały w kolejności `channels`, brak →
    `0`; składnik z kanałem spoza listy → `ArgumentException` (błąd programu, test);
    przedziały z `period.Buckets()`.
  - `Cost` (record) i `CostSummary.Of(costs, period, categories)` →
    `record CostSummary(decimal Costs, IReadOnlyList<CategoryAmount> Categories, IReadOnlyList<decimal> BucketCosts)`:
    filtr `CostDate ∈ [EffectiveFrom, To]` (wydatki sprzed uruchomienia odpadają niezależnie od daty
    zapłaty), suma kwot ze znakiem, pełna lista kategorii.
  - `SalesResult.Of(decimal sales, decimal costs)` = sales − costs; `SalesResult.Buckets(salesBuckets, costBuckets)`.
  - Wszystkie sumy na `decimal` bez zaokrąglania (KWO-01).
- **B9 Reporting.Application — kody i walidacja okresu** (H-1 walidacja najpierw):
  - `WarsawTime.Today(TimeProvider)` (internal, `WarsawTimeZone.Zone`).
  - `SalesReportCodes`: okresy `currentMonth|previousMonth|year|custom`, granulacja `day|month`,
    rodzaje `order|salesReturn|salesCorrection`, status `available|noBasis`, powód `beforeLaunch`;
    `TryParse…` dokładne; `FormatPercent(decimal)` = `"0.0"` w kulturze niezmiennej. Kwoty i daty
    przez `OperationCodes.FormatAmount/FormatDate/TryParseDate` (ten sam moduł, bez kopii).
  - `ReportPeriodInput.Validate(string? period, string? year, string? from, string? to, DateOnly today, DateOnly launchDate, FieldErrors errors)` → `ReportPeriod?`:
    - `period`: brak → `currentMonth`; spoza słownika → `period` „Nieznany okres.”;
    - `year` (tylko przy `year`): brak → bieżący; nie 4 cyfry → „Podaj rok jako liczbę
      czterocyfrową.”; > bieżący → „Rok nie może być późniejszy niż bieżący.”;
    - `from`/`to` (tylko przy `custom`): wymagane („Podaj datę początkową.” / „Podaj datę
      końcową.”), format („Podaj datę w formacie RRRR-MM-DD.”), `from > to` → przy `from` („Data
      początkowa nie może być późniejsza niż końcowa.”), `to > LatestEnd(today)` → przy `to`
      (decyzja 2);
    - parametry niepotrzebne dla danego `period` są ignorowane.
- **B10 `SalesReportSources`** (internal static, Application): `MaxRows = 10_000`;
  `SalesAsync(ISalesComponentSource, DateRange, ct)` → `IReadOnlyList<SalesComponent>?` (woła
  `ListAsync(from, to, MaxRows + 1)`; `> MaxRows` → `null`; mapowanie rekord → fakt);
  `CostsAsync(ICostSource, DateRange, ct)` analogicznie. Odstępstwo od „stronicowania w bazie” jak
  w SF-17 (spec, D-08).
- **B11 `GetSalesReportHandler(ISalesComponentSource sales, ICostSource costs, ILaunchDate launchDate, TimeProvider time)`** (H-1 najpierw):
  1. `today = WarsawTime.Today(time)`; walidacja `ReportPeriodInput` → błędy (400).
  2. `BeforeLaunch` → raport bez wywołań źródeł: `Kpis = null`, puste `Series`, `Channels`,
     `Categories`, `Comparison(Base, Available = false, Reason = BeforeLaunch)`.
  3. Po kolei (bez `Task.WhenAll`): sprzedaż i koszty dla [`EffectiveFrom`, `To`]; `null` → wynik
     `TooManyRows` (422).
  4. `BaseAvailable` → sprzedaż i koszty dla `Base` (też z bezpiecznikiem 422); inaczej źródła nie
     są wołane dla bazy, a każda karta ma `Comparison.NoBasis`, nagłówek porównania
     `Available = false, Reason = BeforeLaunch`.
  5. `SalesSummary`/`CostSummary` dla okresu bieżącego i — przy dostępnej bazie — dla okresu
     bazowego (ten sam `ReportPeriod` przesunięty na `Base`: pomocnik `period.ForBase()` w domenie,
     z `EffectiveFrom = Base.From`). Karty: sprzedaż, koszty, wynik (`SalesResult.Of`), liczba
     zamówień; `Comparison.Of` dla każdej osobno.
  6. Szereg: przedziały `Buckets()` z sprzedażą, kosztami i wynikiem.
  7. `CalculatedAtUtc = time.GetUtcNow()`.
  Wynik: `GetSalesReportResult(SalesReportDto? Report, FieldErrors? Errors = null, bool TooManyRows = false)`.
- **B12 `ListSalesComponentsHandler(ISalesComponentSource sales, ILaunchDate launchDate, TimeProvider time)`** (H-2 najpierw):
  1. Walidacja: okres przez `ReportPeriodInput` (decyzja 3) + `channel` (kod z `sales.Channels`,
     „Nieznany kanał.”), `kind` (`SalesReportCodes`, „Nieznany rodzaj składnika.”), `direction`
     (`asc|desc`, domyślnie `desc`), `page` ≥ 1, `pageSize` 1–100 (domyślnie 25) — teksty jak w
     `ListOperationsHandler`.
  2. `BeforeLaunch` → `items = []`, `total = 0`, `sum = null`, bez wywołań źródła.
  3. `SalesReportSources.SalesAsync` (422 przy `null`) → `Where(SalesPolicy.Includes)` → filtr
     kanału i rodzaju → `sum = SalesPolicy.SumOf(filtered)`, `total = filtered.Count`.
  4. Sortowanie w pamięci: `Date` według kierunku, potem `Kind` rosnąco (kolejność enum), potem `Id`
     rosnąco; `Skip/Take`; strona za końcem → puste `items` z prawdziwymi `total` i `sum`.
  5. Pozycja: `kind`, `id`, `date`, `amount = SignedAmount`, `orderId`, `orderNumber`, `channel`.
- **B13 Reporting.Api**: `SalesReportEndpoints` — `MapGroup("/api/reports/sales").WithTags("Reports")`;
  `MapGet("", …).WithName("Reporting.GetSalesReport")` z `[FromQuery] string? period, year, from, to`;
  `MapGet("/components", …).WithName("Reporting.ListSalesComponents")` z dodatkowymi `channel`, `kind`,
  `direction`, `page`, `pageSize`. Wynik → `200` / `ValidationProblem` /
  `ReportingProblems.ReportRangeTooLarge()`. `ReportingModule.MapReportingEndpoints` mapuje obie grupy.
- **B14 Kontrakty JSON** (`SalesReportContracts`): kształty z sekcji „API Contract”; kwoty
  `OperationCodes.FormatAmount`, procent `SalesReportCodes.FormatPercent`, liczby całkowite dla
  `orderCount`, `calculatedAtUtc` jako `DateTimeOffset` UTC.
- **B15 Testy integracyjne** — sekcja „Tests”.

### Authorization

Oba nowe endpointy to `GET` pod fallback policy hosta (sesja → `401`), bez `AllowAnonymous`; jedna
firma i jeden właściciel, więc brak dodatkowego zakresu zasobu. `EndpointSecurityTests` obejmuje
nowe trasy automatycznie; I-S-13 sprawdza `401` wprost.

### Background jobs

None.

### Edge cases & null safety (backend)

| Edge case (spec) | Gdzie |
|---|---|
| data uruchomienia w środku miesiąca / miesiąc po miesiącu uruchomienia / rok uruchomienia i następny | `ReportPeriod.Resolve` (`EffectiveFrom`, `BaseAvailable`) — RP-1, RP-2 |
| własny zakres przed uruchomieniem (przycięty) / w całości przed (`beforeLaunch`) | `Resolve`, B11 krok 2, B12 krok 2 |
| 62 / 63 dni; luty przestępny; 31 vs 30 dni | `Resolve`, `Buckets` — RP-3, RP-4 |
| `year` > bieżący, `custom` bez `to`, `from > to`, `to` > dziś + 1 rok | `ReportPeriodInput` — H-1 |
| zwrot sprzedaży w kolejnym miesiącu (sprzedaż ujemna) | `SalesPolicy.SignedAmount`, `SalesSummary` — S-4 |
| korekta do zamówienia z poprzedniego miesiąca | `Includes` po dacie składnika — S-6, I-S-3 |
| zwrot pieniędzy | źródło czyta tylko `SalesReturn` (B3) — I-S-5 |
| anulowanie / przywrócenie (także wstecz) | flaga z bieżącego stanu (B2, B3) — S-7, I-S-7 |
| pending / failed / szkic; `refunded`/`on-hold`; `NULL`; status nieznany; anulowane w sklepie i przywrócone w ERP | `SalesEligibility` (B2) — O-1, I-S-4 |
| zamówienie sprzed uruchomienia ze zwrotem po uruchomieniu | `Includes` (`OrderDate >= launch`) — S-8, I-S-8 |
| zamówienie 0 zł | liczy się do liczby, wnosi 0 — S-12 |
| zmiana kanału zamówienia przenosi jego zwroty i korekty | kanał z bieżącego wiersza zamówienia (B3) — I-S-6 |
| obniżenia przekraczające wartość | sumy zapisanych kwot bez przycinania (B8) |
| wydatek nieopłacony, cykliczny, ujemny, z datą w przyszłości, sprzed uruchomienia | `ExpenseCostSource` bez filtra zapłaty, `CostSummary` — C-1, I-S-10 |
| wypłata właściciela | nie jest czytana (B5) — I-S-11 |
| baza 0, baza ujemna, ±0,05% | `Comparison.Of` — CMP-1 |
| > 10 000 składników albo wydatków (także w bazie) | `SalesReportSources` → 422 — H-1, H-2, I-S-12 |
| strona poza zakresem | B12 krok 4 |
| `ShopStatus` nullowalny | `IsHiddenByDefault(null)` = `false` → liczy się |
| `Number` zamówienia | nie-null w modelu (`IsRequired`) |
| `sum` przy `beforeLaunch` | `null`, nie `"0.00"` (decyzja 3) |

## Frontend Plan

### Step 0 — aplikacja

Jedna aplikacja: `app/`. Praca w `app/src/app/features/dashboard/` (zastępuje zaślepkę) i
`features/expenses/`; `shared/format/` dostaje `formatPercent`.

### Expected API contract (wyprowadzony ze spec i rozstrzygnięć)

| Method | Route | Request shape | Response shape | Used by |
|---|---|---|---|---|
| GET | `/api/reports/sales` | `period`, `year?`, `from?`, `to?` | `SalesReport` (`period`, `comparison`, `kpis \| null`, `series`, `channels`, `categories`, `calculatedAtUtc`); `422 report_range_too_large`; `400` | `ReportsApi.salesReport` |
| GET | `/api/reports/sales/components` | te same parametry okresu + `channel?`, `kind?`, `direction`, `page`, `pageSize` | `{ period, items, page, pageSize, total, sum: string \| null }`; `422`; `400` | `ReportsApi.salesComponents` |
| GET | `/api/expenses` | jak dotąd | dotychczasowe pola + `grossTotal: string` | `ExpensesApi.list` |

### Routing

| Path | Page | Loaded how | Guard |
|---|---|---|---|
| `/pulpit` | `DashboardPage` | istniejące `section('pulpit', { loadComponent })` | guard powłoki (sesja) |
| `/wydatki?kosztOd=&kosztDo=&kategoria=` | `ExpensesListPage` | bez zmian trasy; parametry odczytane raz w konstruktorze | jw. |
| `/zamowienia/{id}` | istniejące szczegóły | link z zestawienia | jw. |

`app.routes.ts` i `nav-items.ts` bez zmian.

### Files

| File path | Action | Purpose |
|---|---|---|
| `app/package.json`, `app/package-lock.json` | modify | `chart.js` `4.6.1` (dokładnie, `npm install --save-exact chart.js@4.6.1` w `app/`) |
| `app/src/app/shared/format/pl-format.ts` (+ `pl-format.spec.ts`) | modify | `formatPercent(text)` |
| `app/src/app/features/dashboard/report-models.ts` | create | typy kontraktu, kody, etykiety (okresy, rodzaje, kanały, kategorie — kopie słowników jak w SF-17), `REPORT_PROBLEM`, teksty stałe (opisy kart, komunikaty) |
| `app/src/app/features/dashboard/reports-api.ts` (+ `.spec.ts`) | create | klient `/api/reports/sales` i `/components` |
| `app/src/app/features/dashboard/report-text.ts` (+ `.spec.ts`) | create | czyste funkcje tekstów: etykieta okresu, opis porównania, tekst porównania karty, etykiety przedziałów, informacja o dacie uruchomienia; bez arytmetyki kwot |
| `app/src/app/features/dashboard/dashboard.service.ts` (+ `.spec.ts`) | create | stan Pulpitu na sygnałach (`providedIn: 'root'`) |
| `app/src/app/features/dashboard/dashboard-page.ts`, `.html` (+ `.spec.ts`) | modify/create | nagłówek, przełącznik widoku, wybór okresu, aktualność, zaślepka przepływów, `app-sales-view` |
| `app/src/app/features/dashboard/period-picker.ts`, `.html` (+ `.spec.ts`) | create | select „Okres”, „Rok”, pola dat z walidacją i komunikatem `422` |
| `app/src/app/features/dashboard/kpi-card.ts`, `.html` | create | karta KPI (ikona, etykieta, wartość, opis, porównanie, akcja) |
| `app/src/app/features/dashboard/sales-view.ts`, `.html` (+ `.spec.ts`) | create | karty, wykresy, kanały, kategorie, zestawienie; stany |
| `app/src/app/features/dashboard/chart-factory.ts` | create | jedyny import `chart.js`: rejestracja używanych kontrolerów/elementów, token `CHART_FACTORY` |
| `app/src/app/features/dashboard/chart-canvas.ts` (+ `.spec.ts`) | create | adapter wykresu: canvas, kolory z tokenów, klawiatura, `aria-live`, `chartNumber` |
| `app/src/app/features/dashboard/sales-components.ts`, `.html` (+ `.spec.ts`) | create | zestawienie składników (`app-data-table`, chipy, suma) |
| `app/src/app/features/expenses/expense-models.ts` | modify | `ExpenseListPage.grossTotal` |
| `app/src/app/features/expenses/expenses.service.ts` (+ `.spec.ts`) | modify | `grossTotal` sygnał; `filterByCostPeriod(params)` |
| `app/src/app/features/expenses/expenses-list-page.ts`, `.html` (+ `.spec.ts`) | modify | odczyt `kosztOd`/`kosztDo`/`kategoria`; wiersz „Suma brutto” |
| `app/src/app/app.routes.spec.ts` | modify | `/pulpit` poza listą zaślepek; osobny test z odpowiedziami `/api/reports/sales*` |
| `app/src/app/layout/app-shell.spec.ts`, `app/src/app/app.spec.ts` | modify (jeśli testy padną) | nawigacje „bez zapytań” na `/historia-zmian` albo obsłużenie żądań raportu (Pulpit teraz pobiera dane) |

### Implementation steps

- **F-1 Zależność:** w `app/` `npm install --save-exact chart.js@4.6.1` (sprawdzić, że w
  `package.json` jest `"chart.js": "4.6.1"` bez `^`/`~`, a lockfile się zmienił). Najpierw
  instalacja, potem kod (rules.md).
- **F-2 Modele i klient:**
  - `report-models.ts`: `PeriodKind = 'currentMonth'|'previousMonth'|'year'|'custom'`,
    `PeriodSelection { kind, year: string, from: string, to: string }`, `ReportPeriod`,
    `ReportComparisonHeader`, `AmountComparison { status: 'available'|'noBasis', base, difference, percent }`,
    `CountComparison` (liczby całkowite), `SalesKpis`, `SeriesPoint`, `ChannelAmount`, `CategoryAmount`,
    `SalesReport`, `SalesComponentKind = 'order'|'salesReturn'|'salesCorrection'`, `SalesComponentItem`,
    `SalesComponentsPage`; `PERIOD_LABELS`, `COMPONENT_KIND_LABELS` („Zamówienie”, „Zwrot sprzedaży”,
    „Korekta sprzedaży”), `CHANNEL_LABELS`, `CATEGORY_LABELS` (kopie, bez importów między feature'ami
    — precedens SF-17); `KPI_DESCRIPTIONS` (4 teksty ze spec, UI pkt 4); `REPORT_PROBLEM.rangeTooLarge = 'report_range_too_large'`.
  - `ReportsApi.salesReport(selection)` i `salesComponents(selection, filters, page)` — parametry
    okresu wysyłane tylko potrzebne dla `kind` (`year` przy roku, `from`/`to` przy własnym zakresie);
    `firstValueFrom` jak `OperationsApi`.
- **F-3 `formatPercent`** w `shared/format/pl-format.ts`: `"12.5"` → `+12,5%`, `"-3.0"` → `−3,0%`
  (U+2212), `"0.0"` → `0,0%`, niepoprawny / `null` → `NO_DATA`; operacja na tekście (znak, zamiana
  kropki), bez `Number`.
- **F-4 `DashboardService`** (`providedIn: 'root'`):
  - sygnały: `view: 'sales'|'cashFlow'` (domyślnie `sales`); `selection` (domyślnie
    `currentMonth`, rok = bieżący z `todayInWarsaw()`, daty puste); `report`, `reportLoading`,
    `reportError`, `rangeError` (komunikat `422` przy wyborze okresu); zestawienie: `channel`,
    `kind`, `direction` (`desc`), `page`, `componentsPage`, `componentsLoading`, `componentsError`;
  - `selectionErrors` (computed, UI pkt 1): przy `custom` puste „od”/„do” („Podaj datę.”), „od” >
    „do” (komunikat przy „Data od”: „Data „od” nie może być późniejsza niż data „do”.”), „do” > dziś
    + 1 rok (ten sam tekst co API, decyzja 2; porównanie tekstu `yyyy-MM-dd`, data graniczna z
    `todayInWarsaw()` przez zmianę roku w tekście, 29.02 → 28.02) → bez żądania;
  - `enter()` (decyzja 4): `loadReport()` i `loadComponents()` z zachowanym stanem;
  - `setSelection(change)`: zapis wyboru, reset zestawienia (strona 1, bez filtrów, `desc`) i oba
    odczyty (spec pkt 30);
  - `loadReport()` / `loadComponents()`: osobne liczniki sekwencji (wzór `OperationsService.load`);
    `422` → `rangeError = detail` z API, dane ukryte; inny błąd → `reportError` „Nie udało się wczytać
    raportu.” / `componentsError` „Nie udało się wczytać zestawienia.”; dwa żądania mogą iść
    równolegle (osobne żądania HTTP; backend obsługuje każde w osobnym zakresie DI);
  - akcje przejść (spec 20, 23, 24): `showAllComponents()` (czyści filtry), `showChannel(code)`
    (kanał, bez rodzaju), `showOrders()` (rodzaj `order`, bez kanału) — strona 1 i odczyt;
    przewinięcie i fokus robi komponent;
  - `yearOptions` (computed): od roku `report.period.launchDate` (gdy znany; inaczej tylko bieżący)
    do bieżącego roku, malejąco;
  - `costsLink` / `categoryLink(code)`: `['/wydatki']` + `queryParams { kosztOd: period.effectiveFrom, kosztDo: period.to, kategoria? }`;
  - **bez obliczeń pieniędzy:** wszystkie kwoty, sumy, procenty i liczby z API.
- **F-5 `report-text.ts`** (czyste funkcje, testowane osobno):
  - `periodLabel(period)`: miesiąc → „listopad 2026, 01.11–30.11.2026” (`formatMonth` + daty);
    rok → „2026, 01.01–09.11.2026”; własny → „01.09.2026–15.11.2026”;
  - `launchNotice(period)`: gdy `effectiveFrom > from` i nie `beforeLaunch` → „Raport liczony od
    {data} (data uruchomienia).”; `beforeLaunchMessage(period)` (spec pkt 3);
  - `comparisonDescription(period, comparison)` (spec pkt 5): bieżący miesiąc → „Miesiąc w toku —
    porównanie z całym poprzednim miesiącem ({formatMonth(comparison.from)}).”; bieżący rok →
    „Rok w toku (dane do {dziś}) — porównanie z 01.01–{dd.MM} {rok bazy}.” (rok z tekstu
    `comparison.from`); pozostałe → „Porównanie z: {etykieta bazy}” (miesiąc, rok albo zakres);
  - `baseLabel(period, comparison)` do tekstu karty;
  - `cardComparisonText(comparison, baseLabel, unit)` → `{ arrow: '↑'|'↓'|'→', text }`:
    „+12,5% (+1 234,00 zł) więcej niż w: październik 2026”, „−3,0% (-50,00 zł) mniej niż w: …”,
    „0,0% (0,00 zł) bez zmian względem: …”, albo „brak podstawy porównania”; kierunek z pierwszego
    znaku tekstu `difference` (`-` → mniej, `0`/`0.00` → bez zmian), `+` dopisywany do tekstu
    dodatniej różnicy; dla liczby zamówień różnica w sztukach („+3 zam.”);
  - `bucketLabel(point, granularity)`: dzień „01.11”, miesiąc „sty 2026” (`Intl.DateTimeFormat('pl-PL', { month: 'short', year: 'numeric', timeZone: 'UTC' })`).
- **F-6 `PeriodPicker`** (wejście: `selection`, `errors`, `rangeError`, `yearOptions`, `maxDate`;
  wyjście: `selectionChange`): `app-field` „Okres” (select z `PERIOD_LABELS`), przy „Rok”
  `app-field` „Rok” (select), przy „Własny zakres” „Data od” i „Data do” (`type="date"`, `max` =
  dziś + 1 rok); komunikaty przy polach; `rangeError` w `role="alert"` pod wyborem. Zmiana pola dat
  wysyła wybór dopiero, gdy oba pola są poprawne (serwis i tak blokuje żądanie przy błędzie).
- **F-7 `KpiCard`** (wejścia: `label`, `value` (sformatowany tekst), `description`, `comparison`
  (`{ arrow, text }`), `icon`, `action: { kind: 'link', link, queryParams } | { kind: 'button' } | null`,
  `hint`; wyjście `activate`): ikona w `span aria-hidden` na stonowanym tle (`bg-base-200`),
  wartość `text-3xl tabular-nums`, porównanie w kolorze neutralnym (`text-(--sf-muted)`, bez
  zieleni/czerwieni). Klikalne karty: cały nagłówek karty to `<a>` (Koszty) albo `<button>`
  (Sprzedaż, Liczba zamówień) z widoczną wskazówką: „Pokaż składniki”, „Pokaż wydatki”, **„Pokaż w
  zestawieniu”** (decyzja 6). Wynik bez akcji.
- **F-8 `DashboardPage`**:
  - konstruktor: `dashboard.enter()` (decyzja 4);
  - nagłówek „Pulpit”; przełącznik widoku: `fieldset` z `legend` „Widok”, dwa `input type="radio"`
    (natywne strzałki, widoczne zaznaczenie przez styl etykiety `has-[:checked]`); „Przepływy
    pieniężne” → `app-empty-state heading="Widok przepływów pieniężnych powstanie w kolejnym etapie."`
    z tekstem i linkiem `/operacje` (spec pkt 29); wybór okresu ukryty w widoku przepływów;
  - pod wyborem: `periodLabel`, `launchNotice`, `comparisonDescription`, „Stan na
    {formatDateTime(calculatedAtUtc)}”, `<app-incomplete-data-notice />` (bez zmian, SF-14), ogólna
    etykieta „Kwoty brutto w PLN. Raporty obejmują dane od {formatDate(launchDate)}.”;
  - `@if (view === 'sales') <app-sales-view />`.
- **F-9 `SalesComponents`** (zestawienie): nagłówek `h2 id="zestawienie-skladnikow" tabindex="-1"`
  „Zestawienie składników sprzedaży”; `app-data-table` z `filterable`, `[filters]` = chipy
  („Kanał: OLX”, „Rodzaj: Zamówienie”), „Wyczyść filtry”; w `tableFilters` dwa selecty „Kanał”
  (wszystkie + `report.channels` w kolejności API) i „Rodzaj”; wiersz „Suma zestawienia:
  {formatPln(sum)}” w `aria-live="polite"` z etykietą spec pkt 19; kolumny: Data (`formatDate`,
  sortowalna — jedyna), Rodzaj, Zamówienie (szablon `appTableCell`: `routerLink` `['/zamowienia', orderId]`
  z numerem), Kanał, Kwota (`formatPln(amount)`, `align: 'end'`, `tabular-nums`); `emptyTitle`
  „Brak składników sprzedaży w wybranym okresie”, `filteredEmptyTitle` „Brak składników dla
  wybranych filtrów”; błąd → `error` z „Spróbuj ponownie”; `beforeLaunch` → sekcja ukryta.
- **F-10 Lista wydatków:**
  - `ExpenseListPage.grossTotal: string`; `ExpensesService.grossTotal` (sygnał, `null` w trakcie
    odczytu zmienionego filtra i przy błędzie);
  - `ExpensesService.filterByCostPeriod({ from, to, categories })`: `EMPTY_FILTERS` + daty kosztu +
    kategorie, `sortState = DEFAULT_SORT`, `pageState = 1`, odczyt (niejasność „pozostałe filtry”);
  - `ExpensesListPage` konstruktor: gdy w `queryParamMap` jest `kosztOd`, `kosztDo` albo
    `kategoria` → walidacja każdej wartości (`/^\d{4}-\d{2}-\d{2}$/` + poprawna data kalendarzowa,
    `kosztOd ≤ kosztDo`, kategoria z `CATEGORIES`), niepoprawne pominięte → `filterByCostPeriod`;
    w przeciwnym razie dotychczasowa logika (`wydatekCykliczny` albo `load()`);
  - nad tabelą `<p aria-live="polite">Suma brutto: <span class="tabular-nums">{formatPln(grossTotal)}</span></p>`
    z etykietą „Suma kwot brutto wydatków spełniających filtry; korekty i zwroty od dostawców ją
    pomniejszają.”; ukryta przy błędzie i w trakcie pierwszego odczytu (bez fikcyjnego zera); filtry
    z parametrów widoczne jako istniejące chipy.
- **F-11 Wykresy:**
  - `chart-factory.ts`: `Chart.register(BarController, BarElement, LineController, LineElement, PointElement, CategoryScale, LinearScale, Tooltip, Legend)`;
    `export const CHART_FACTORY = new InjectionToken<ChartFactory>(…, { factory: () => (canvas, config) => new Chart(canvas, config) })`.
    Importowany tylko przez `chart-canvas.ts` → trafia do fragmentu Pulpitu, budżet `initial` bez
    zmian (sprawdzić wynik `npm run build`).
  - `ChartCanvas` (`app-chart-canvas`), wejścia: `spec: ChartSpec` (`type: 'timeline'|'horizontalBar'`,
    `labels: string[]`, `series: { label, values: string[] (ciągi z API), token: '--sf-accent'|'--sf-text'|'--sf-info', shape: 'bar'|'dashedLine' }[]`,
    `ariaLabel`), wyjście brak. Odpowiedzialności:
    - `chartNumber(text)` = `Number(text)` — **jedyna konwersja tekst → liczba w aplikacji**, tylko
      do geometrii (niejasność z przeglądu);
    - kolory z `getComputedStyle(document.documentElement).getPropertyValue(token)` (bez literałów,
      `lint:colors`); seria wyniku: linia przerywana (`borderDash`) ze znacznikami;
    - oś Y od zera (`beginAtZero`, obsługa ujemnych), tytuł osi „zł”; poziome słupki
      `indexAxis: 'y'`;
    - tooltip i region `aria-live="polite"`: ciągi `formatPln(values[i])` i etykieta przedziału, nie
      liczby;
    - canvas `role="img"`, `aria-label` (tytuł + „Dane w tabeli poniżej.”/„Wartości na liście
      obok.”), `tabindex="0"`; klawiatura: ←/→ (↑/↓ dla poziomych) przesuwają aktywny indeks
      (`setActiveElements` + `tooltip.setActiveElements`, `update()`), Home/End skrajne, Escape
      chowa tooltip i czyści `aria-live`;
    - `animation: false` przy `matchMedia('(prefers-reduced-motion: reduce)').matches`;
    - `effect` na zmianę `spec` → `chart.data = …; chart.update()`; `DestroyRef` → `chart.destroy()`.
  - `SalesView`: wiersz wykresów (`grid xl:grid-cols-[2fr_1fr]` od 1200 px):
    - „Sprzedaż i koszty w czasie” (`h2` + okres) → `app-chart-canvas` (`Sprzedaż` słupki
      `--sf-accent`, `Koszty` słupki `--sf-text`, `Wynik przed wypłatami` linia przerywana
      `--sf-info`), przycisk „Pokaż dane w tabeli” (`aria-expanded`, `aria-controls`) → tabela
      Okres / Sprzedaż / Koszty / Wynik przed wypłatami z wierszem „Razem” z **wartości kart** (nie
      sumowane w UI);
    - „Sprzedaż według kanałów”: poziome słupki + lista `<ul>`; wiersz to `<button>` „{kanał}:
      {kwota}” → `dashboard.showChannel(code)`, przewinięcie i fokus nagłówka zestawienia;
    - „Koszty według kategorii”: poziome słupki (`--sf-text`) + lista; wiersz to `<a routerLink="/wydatki" [queryParams]="categoryLink">`.
- **F-12 `SalesView` — karty i stany:** siatka kart `grid gap-4 md:grid-cols-2 xl:grid-cols-4`
  (1 kolumna < 768 px, 2×2 768–1199 px, 4 od 1200 px); stany: ładowanie `app-loading-state` (bez
  zer), błąd raportu `app-error-state` z „Spróbuj ponownie” (`loadReport()`), `422` → komunikat w
  wyborze okresu i ukryte dane, `beforeLaunch` → komunikat spec pkt 3 (`role="status"`) zamiast
  kart, wykresów i zestawienia; zera z API pokazywane jako `0,00 zł` / `0`. Klik kart: Sprzedaż →
  `showAllComponents()` + przewinięcie (`scrollIntoView`, z `behavior: 'auto'` przy reduced motion)
  i `focus()` nagłówka; Liczba zamówień → `showOrders()` + to samo; Koszty → link.
- **F-13 Specyfikacje tras i powłoki:** `app.routes.spec.ts` — `/pulpit` usunięte z `SECTIONS`
  (zaślepek), osobny test z `HttpTestingController` odpowiadającym na oba żądania raportu;
  `app-shell.spec.ts` i `app.spec.ts` — gdzie `/pulpit` służy jako „strona bez zapytań”, zamienić na
  `/historia-zmian` albo obsłużyć żądania (jak przy `/operacje` w SF-17).

### State

- Lokalny w komponencie: rozwinięcie tabeli danych wykresu, aktywny indeks wykresu, tekst
  `aria-live`.
- Serwis (root, `DashboardService`): widok, wybór okresu, filtry/kierunek/strona zestawienia, wynik
  raportu i strony — przetrwa przejście do zamówienia/wydatku (decyzja 4).
- Stan serwera: raport, strona zestawienia, `grossTotal` — zawsze z API, odświeżane przy wejściu.
- Pochodne: `selectionErrors`, `yearOptions`, linki do wydatków, teksty z `report-text.ts` — bez
  obliczeń pieniężnych.

### Styling

- Tokeny przez daisyUI i `text-(--sf-muted)`; kolory wykresu wyłącznie z tokenów CSS (`--sf-accent`,
  `--sf-text`, `--sf-info`); zero literałów (`lint:colors`).
- Porównanie w kolorze neutralnym (design guide §4), kierunek słowem i strzałką.
- Brak jednej „akcji głównej” na Pulpicie (widok analityczny, bez `app-button primary`).
- Kwoty `tabular-nums`, do prawej w tabelach. Style w klasach Tailwind (budżet `anyComponentStyle`).
- Progi responsywne jak design guide §9; brak poziomego przewijania strony (tabele w swoich
  kontenerach).

### Accessibility

- Przełącznik widoku: natywne radio w `fieldset`/`legend` (strzałki, fokus widoczny).
- Pola okresu w `app-field`; błędy przy polach (`aria-describedby`), `422` w `role="alert"`.
- Wykresy: `role="img"`, `aria-label`, `tabindex="0"`, klawiatura i `aria-live` (F-11); tabela
  danych i listy zawsze dostępne jako alternatywa.
- Przejścia z kart/kanałów: fokus na nagłówku zestawienia (`tabindex="-1"`).
- „Suma zestawienia” i „Suma brutto” w `aria-live="polite"`; stany ładowania z
  `app-loading-state` (komunikat dla AT).

### Edge cases (frontend)

| Edge case | Gdzie |
|---|---|
| `kpis = null` / `beforeLaunch` | komunikat zamiast kart, wykresów i zestawienia (F-12) |
| `noBasis` na jednej karcie | `cardComparisonText` → „brak podstawy porównania” (F-5) |
| sprzedaż / wynik / kanał / suma zestawienia ujemne | `formatPln` bez zmiany znaku; oś Y z ujemnymi |
| `422` raportu albo zestawienia | `rangeError` przy wyborze okresu |
| własny zakres niepoprawny / „do” > dziś + 1 rok | `selectionErrors`, bez żądania |
| zera po dacie uruchomienia | `0,00 zł` / `0` z API |
| powrót z zamówienia lub wydatku | `enter()` — ponowny odczyt z zachowanym stanem |
| niepoprawne `kosztOd`/`kosztDo`/`kategoria` | pominięte, lista z filtrami domyślnymi (F-10) |
| `sum = null` | wiersz sumy ukryty |
| brak `launchDate` przed pierwszą odpowiedzią | `yearOptions` = bieżący rok |

## API Contract

| Method | Route | Request | Response |
|---|---|---|---|
| GET | `/api/reports/sales` | `period` (`currentMonth` domyślnie \| `previousMonth` \| `year` \| `custom`); `year` (`yyyy`, przy `year`, domyślnie bieżący, ≤ bieżący); `from`, `to` (`yyyy-MM-dd`, wymagane przy `custom`, `from ≤ to`, `to ≤ dziś + 1 rok`) | `200 SalesReportResponse`; `400` (klucze `period`, `year`, `from`, `to`); `422 report_range_too_large` |
| GET | `/api/reports/sales/components` | parametry okresu jak wyżej (decyzja 3); `channel` (kod kanału), `kind` (`order` \| `salesReturn` \| `salesCorrection`), `direction` (`desc` domyślnie \| `asc`), `page` (≥ 1, domyślnie 1), `pageSize` (1–100, domyślnie 25) | `200 { period, items, page, pageSize, total, sum }`; `400` (klucze jak wyżej + `channel`, `kind`, `direction`, `page`, `pageSize`); `422 report_range_too_large` |
| GET | `/api/expenses` | bez zmian | dotychczasowe pola + `grossTotal` (`"0.00"` dla pustego wyniku) |

```
SalesReportResponse {
  period: { kind, from, to, effectiveFrom, launchDate, beforeLaunch, inProgress, granularity: "day"|"month" },
  comparison: { from, to, available, reason: "beforeLaunch" | null },
  kpis: null | {
    sales:      { value: "1234.50", comparison: AmountComparison },
    costs:      { value, comparison: AmountComparison },
    result:     { value, comparison: AmountComparison },
    orderCount: { value: 12, comparison: CountComparison }
  },
  series:     [ { from, to, sales, costs, result } ],   // [] przy beforeLaunch
  channels:   [ { channel, amount } ],                  // pełny słownik w kolejności Orders; [] przy beforeLaunch
  categories: [ { category, amount } ],                 // pełny słownik w kolejności Expenses; [] przy beforeLaunch
  calculatedAtUtc: "2026-11-09T10:15:00+00:00"
}
AmountComparison { status: "available"|"noBasis", base: string|null, difference: string|null, percent: string|null }
CountComparison  { status, base: int|null, difference: int|null, percent: string|null }
SalesComponentsPage {
  period: (jak wyżej),
  items: [ { kind, id, date, amount /* ze znakiem */, orderId, orderNumber, channel } ],
  page, pageSize, total,
  sum: string | null      // null przy beforeLaunch
}
```

- Kwoty jako tekst z kropką i opcjonalnym minusem; `percent` z jednym miejscem po kropce; daty
  `yyyy-MM-dd`; kody kanałów/kategorii zgodne z `OrderCodes`/`ExpenseCodes`.
- Obie trasy wymagają sesji; `GET`, więc bez XSRF.

## Integration Check

| Frontend expects | Backend provides | Gap |
|---|---|---|
| `GET /api/reports/sales` z parametrami okresu | B13 `[FromQuery] period, year, from, to` | — |
| `period.effectiveFrom/launchDate/beforeLaunch/inProgress/granularity` | B11 z `ReportPeriod` | — |
| `comparison.from/to` do opisu porównania (decyzja 7) | B11 `ReportPeriod.Base` | — |
| `kpis = null` przy `beforeLaunch` | B11 krok 2 | — |
| `orderCount.value` i `CountComparison.base/difference` jako liczby całkowite | B14 | — |
| `percent` jako tekst `"12.5"` | `SalesReportCodes.FormatPercent` | — |
| pełne listy `channels`/`categories` w stałej kolejności | `Channels`/`Categories` źródeł (B3, B5) | — |
| `GET /components` z tymi samymi parametrami okresu (decyzja 3) | B12 krok 1 (`ReportPeriodInput`) | — |
| `period` w odpowiedzi zestawienia, `sum = null` przy `beforeLaunch` | B12 krok 2, B14 | — |
| kody `kind`: `order`/`salesReturn`/`salesCorrection` | `SalesReportCodes` | — |
| `422` z `code = report_range_too_large` i `detail` | `ReportingProblems.ReportRangeTooLarge()` | — |
| `400` z kluczem `to` dla „do” > dziś + 1 rok, ten sam tekst | B9 / F-4 (tekst skopiowany) | — |
| `grossTotal` w `/api/expenses` | B5 | — |
| sesja | fallback policy hosta | — |

No integration gaps.

## Invariant Check

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sales separate from cash | Źródło sprzedaży czyta wartości zamówień, zwroty sprzedaży i korekty (bez wpłat i zwrotów pieniędzy); koszty według daty kosztu bez względu na zapłatę; wypłaty nie są czytane | S-1, S-2, S-5; I-S-5 (zwrot nadpłaty), I-S-10 (nieopłacony w kosztach), I-S-11 (wypłata bez wpływu), I-S-2 (wpłata w innym miesiącu nie zmienia sprzedaży) |
| 2. Backend decimal money | `decimal` w domenie i źródłach; jedno zaokrąglenie (`Comparison`, `AwayFromZero`); `grossTotal` przez SQL `SUM`; frontend formatuje ciągi, `chartNumber` tylko do geometrii, „Razem” z kart | CMP-1, S-11 (przedziały = karty), I-S-1, I-S-10; F-5, F-11, F-12 (brak sumowania w UI) |
| 3. API-enforced access | fallback policy, brak `AllowAnonymous` | `EndpointSecurityTests`; I-S-13 (`401` dla obu tras) |
| 4. Retries never duplicate | n/d (odczyty) | n/d |
| 5. Financial audit trail | n/d (bez zmian danych; migracja to indeks) | n/d |
| 6. Secrets stay server-side | brak sekretów; `chart.js` z npm, bez CDN | przegląd `package.json` |
| 7. Module and layer boundaries | kontrakty w `Shared.Application`, słownik w Kernel, implementacje w Infrastructure właścicieli, reguła statusu w `Orders.Domain`, Reporting bez mapowania `orders`/`expenses`, wywołania sekwencyjne | `StoneFlow.Architecture.Tests` bez nowych wyjątków; I-S-14 (rejestracja: po jednym źródle) |
| 8. No identifying data in docs | numer zamówienia bez danych klienta; fikcyjne dane testowe | przegląd |

Brak blokerów.

### Wpływ na testy architektury

Bez zmian w `StoneFlow.Architecture.Tests`: `SalesComponentKind` to czysty enum (Kernel);
`ISalesComponentSource`/`ICostSource` bez EF/HTTP (`Shared.Application`); `Reporting.Domain` używa
tylko Kernel; `SalesEligibility` w `Orders.Domain` bez EF; źródła w Infrastructure odwołują się do
`Shared.Application` i własnego modułu; brak zmian `.csproj` w `api/src`; `DbContextSchemaTests` —
nowy indeks w schemacie `orders`, Reporting bez encji.

## Unified Implementation Order

1. **Kernel i kontrakty:** `SalesComponentKind`, `SalesComponentRecord`, `ISalesComponentSource`,
   `CostRecord`, `ICostSource` (B1).
2. **Domena Orders (TDD):** O-1 → `SalesEligibility` (B2).
3. **Domena Reporting — okresy (TDD):** LC-1, RP-1 … RP-5 → `LaunchCutoff`, delegacja
   `CashFlowPolicy`, `ReportPeriod` (B6); `CashFlowPolicyTests` bez zmian zielone.
4. **Domena Reporting — porównanie, sprzedaż, koszty (TDD):** CMP-1, S-*, C-1 → `Comparison`,
   `SalesComponent`, `SalesPolicy`, `SalesSummary`, `SalesResult`, `Cost`, `CostSummary` (B7, B8).
5. **Aplikacja Reporting (TDD):** H-1, H-2 → `WarsawTime`, `SalesReportCodes`, `ReportPeriodInput`,
   `SalesReportSources`, `GetSalesReport`, `ListSalesComponents` (B9–B12).
6. **Infrastructure:** `OrderSalesComponentSource`, indeks + migracja `SalesCorrectionDateIndex`
   (`--configuration Release`, UTF-8 z BOM, przegląd `Up`/`Down`), DI Orders (B3, B4);
   `ExpenseReader` `grossTotal`, `ListExpenses`, `ExpenseCostSource`, DI Expenses (B5).
7. **Api:** `ExpenseContracts.grossTotal`; `SalesReportEndpoints`, `SalesReportContracts`,
   `ReportingProblems`, `ReportingModule` (B13, B14).
8. **Testy integracyjne:** I-S-*, I-E-1; regresja `OperationsApiTests`, `ExpensesApiTests`,
   `SalesCorrectionsApiTests`, `EndpointSecurityTests`, `OpenApiTests`.
9. **Frontend — zależność:** `npm install --save-exact chart.js@4.6.1` (F-1).
10. **Frontend — fundamenty:** `formatPercent` + spec (F-3), modele i klient + spec (F-2),
    `report-text.ts` + spec (F-5), `DashboardService` + spec (F-4).
11. **Frontend — komponenty:** `chart-factory.ts`, `ChartCanvas` + spec (F-11), `KpiCard` (F-7),
    `PeriodPicker` + spec (F-6), `SalesComponents` + spec (F-9), `SalesView` + spec (F-11, F-12),
    `DashboardPage` + spec (F-8).
12. **Frontend — wydatki:** model, serwis, strona listy + specy (F-10).
13. **Frontend — specy tras i powłoki** (F-13).
14. **Weryfikacja** (sekcja „Verification”), sprawdzenie z działającym API (przejścia, sumy), ręczne
    scenariusze do `06-implementation-done.md` z otwartym punktem decyzji 7.

## Tests

| Test | Type | What it proves |
|---|---|---|
| O-1 `SalesEligibilityTests` | unit (Orders.Tests) | macierz `FulfillmentStatus` × `shopStatus`: `null`, `processing`, `completed`, `on-hold`, `refunded`, `cancelled` (sklep), `trash`, `custom-status` (nieznany) → liczy się, gdy ERP ≠ `Cancelled`; `pending`, `failed`, `checkout-draft` → nie; `Cancelled` → nigdy; wielkość liter (`Pending`) — liczy się (porównanie dokładne, jak `IsHiddenByDefault`) |
| LC-1 `LaunchCutoffTests` | unit (Reporting.Tests) | max(from, launch) dla przed / w dniu / po; `CashFlowPolicyTests` bez zmian |
| RP-1 `ReportPeriodTests` — zakresy | unit | bieżący miesiąc = cały miesiąc (01–30.11), poprzedni, rok bieżący do dziś, rok miniony do 31.12, własny; `EffectiveFrom`; `BeforeLaunch` dla roku i zakresu przed uruchomieniem; `InProgress` |
| RP-2 okres bazowy | unit | miesiąc → cały poprzedni (31 vs 30 dni); rok bieżący → 01.01–ten sam dzień y−1, 29.02 → 28.02; rok miniony → cały y−1 (decyzja 7); własny → tyle samo dni przed efektywnym początkiem; `BaseAvailable` false dla miesiąca uruchomienia, miesiąca po nim, roku uruchomienia i roku po nim, zakresu przyciętego |
| RP-3 granulacja | unit | miesiąc → dni, rok → miesiące, własny efektywny 62 dni → dni, 63 → miesiące (także przycięty datą uruchomienia) |
| RP-4 `Buckets` | unit | pokrycie bez luk; przedziały miesięczne przycięte; bieżący miesiąc do końca miesiąca; bieżący rok do bieżącego miesiąca (ostatni kończy się dziś); luty przestępny; `BeforeLaunch` → pusto |
| RP-5 `LatestEnd` | unit | dziś + 1 rok; 29.02 → 28.02 (decyzja 2) |
| CMP-1 `ComparisonTests` | unit | baza 0 → `NoBasis`; baza −1000, bieżący −500 → +50,0; 0,05% → 0,1; −0,05% → −0,1; −0,04% → 0,0 (bez `-0.0`); różnica; wariant `int` (12 vs 8 → +50,0, 3 vs 0 → `NoBasis`) |
| S-1 … S-11 `SalesSummaryTests` | unit | scenariusze PRD §11 ze spec Verification 1–11 (niezapłacone zamówienie; wpłata w innym miesiącu; korekta wartości; zwrot sprzedaży w kolejnym miesiącu → ujemny miesiąc i kanał; zwrot nadpłaty → brak składnika, 1000; korekta 500 przy 1000/300 → −500 w okresie korekty; anulowanie → 0 i 0 zamówień, po przywróceniu znów; zamówienie sprzed uruchomienia i jego zwrot → wyłączone; pending/failed/szkic → wyłączone; suma kanałów = sprzedaż, zamówienie ze zwrotem liczone; suma przedziałów = karty) |
| S-12 `SalesSummaryTests` — kwoty brzegowe | unit | zamówienie 0 zł: liczba +1, sprzedaż +0,00; pełna lista kanałów z `0`; kanał spoza słownika → wyjątek; kwoty bez zaokrąglania |
| C-1 `CostSummaryTests` | unit | nieopłacony w kosztach; wydatek −200 obniża koszty i kategorię (kategoria ujemna); sprzed uruchomienia (opłacony po nim) poza kosztami; pełna lista kategorii z VAT i PPE; przedziały = karta; wynik = sprzedaż − koszty (`SalesResult`) |
| H-1 `GetSalesReportHandlerTests` | unit (fałszywe źródła, `ILaunchDate`, zegar) | walidacja (nieznany `period`; `year` 3 cyfry, > bieżący; `custom` bez `to`; `from > to` przy `from`; `to` > dziś + 1 rok przy `to`; parametry spoza okresu ignorowane); `beforeLaunch` → żadne źródło nie jest wołane, `Kpis = null`; baza sprzed uruchomienia → źródła wołane tylko dla okresu bieżącego, wszystkie karty `NoBasis`; dostępna baza → 4 wywołania po kolei z poprawnymi zakresami; bezpiecznik: 10 001 składników albo wydatków (także w bazie) → `TooManyRows`, dokładnie 10 000 → raport; `CalculatedAtUtc` z zegara; przedziały = karty |
| H-2 `ListSalesComponentsHandlerTests` | unit | te same parametry okresu (decyzja 3); `beforeLaunch` → puste, `sum = null`, źródło nie jest wołane; wyłączone składniki pominięte; filtr kanału (suma = kanał), filtr rodzaju (`order`: `total` = liczba zamówień; `salesReturn`: suma ujemna); sortowanie data/rodzaj/id w obu kierunkach; strona za końcem; `pageSize` 0/101, `page` 0, nieznany `channel`/`kind`/`direction` → `400`; suma niezależna od strony; bezpiecznik → 422 |
| I-S-1 `SalesReportApiTests` — karty i zestawienie | integration (SQL, LocalDB/kontener) | zamówienia, zwrot sprzedaży, korekta, wydatki przez API modułów; karty z dokładnymi ciągami; suma zestawienia = karta „Sprzedaż” dla każdej strony i kierunku; suma kanałów = sprzedaż; `kind=order` → `total` = liczba zamówień |
| I-S-2 niezależność od wpłat | integration | wpłata w innym miesiącu nie zmienia sprzedaży żadnego okresu |
| I-S-3 korekta do wcześniejszego zamówienia | integration | (zegar grudzień, launch wcześniej) korekta z listopada do zamówienia z października: w zestawieniu listopada, obniża listopad, październik bez zmian |
| I-S-4 statusy (decyzja 1) | integration | zamówienie ręczne (`ShopStatus NULL`) liczone; zamówienia ze sklepu (`SeedShopOrderAsync` + `UPDATE ShopStatus`) `custom-status`, `on-hold`, `refunded` liczone; `pending`, `failed`, `checkout-draft` i anulowane w ERP — poza sprzedażą i liczbą, ich zwroty i korekty też; po `UPDATE` na `processing` wchodzi w okresie daty zamówienia |
| I-S-5 zwrot pieniędzy | integration | zwrot nadpłaty 200 przy zamówieniu 1000 → sprzedaż `1000.00`, brak pozycji w zestawieniu |
| I-S-6 kanał z bieżącego stanu | integration | zmiana kanału zamówienia (edycja) przenosi jego zwrot sprzedaży do nowego kanału |
| I-S-7 anulowanie i przywrócenie | integration | anulowanie zamówienia ze zwrotem sprzedaży z kolejnego miesiąca → oba okresy się zmieniają; „Przywróć zamówienie” → znów liczone w okresach swoich dat |
| I-S-8 przełom uruchomienia | integration | zamówienie z 2026-10-28 i jego zwrot sprzedaży 2026-11-04 → nie obniża sprzedaży; `period.effectiveFrom` = 2026-11-03 przy bieżącym miesiącu; `comparison.available = false` |
| I-S-9 okres przed uruchomieniem | integration | `custom` w październiku 2026 → `beforeLaunch`, `kpis = null`, puste listy; `/components` → `period.beforeLaunch`, `items = []`, `sum = null` (decyzja 3) |
| I-S-10 koszty i `grossTotal` | integration | wydatki opłacony, nieopłacony, ujemny, wystąpienie cykliczne; karta „Koszty” = `grossTotal` z `/api/expenses?costDateFrom={effectiveFrom}&costDateTo={to}`; wartość kategorii = `grossTotal` z filtrem kategorii; wydatek sprzed uruchomienia poza kosztami |
| I-S-11 wypłata | integration | wypłata właściciela nie zmienia kosztów ani wyniku |
| I-S-12 bezpiecznik i walidacja | integration | 10 001 wydatków wstawionych SQL-em (`INSERT … SELECT`) → `422 report_range_too_large` z `detail` (oba endpointy dla zamówień); `from > to` → `400` przy `from`; `to` > dziś + 1 rok → `400` przy `to`; `year=2027` przy zegarze 2026 → `400` |
| I-S-13 dostęp | integration | `401` bez sesji dla `/api/reports/sales` i `/components` |
| I-S-14 rejestracja źródeł | integration | w zakresie hosta dokładnie jedno `ISalesComponentSource` i jedno `ICostSource` |
| I-S-15 porównanie | integration | (ustawienie `StoneFlow:LaunchDate` wcześniej, zegar w grudniu) karty z `available`, `base`, `difference`, `percent`; karta z bazą 0 → `noBasis` |
| I-E-1 `ExpensesApiTests` — `grossTotal` | integration | suma całego filtra (nie strony), z ujemnym wydatkiem; filtr kategorii i stanu; pusty wynik → `"0.00"`; strona za końcem → prawdziwa suma |
| Regresja | integration / arch | `OperationsApiTests`, `ExpensesApiTests`, `SalesCorrectionsApiTests` (migracja), `EndpointSecurityTests`, `OpenApiTests`, `StoneFlow.Architecture.Tests` |
| F-1 `pl-format.spec.ts` — `formatPercent` | unit (Vitest) | `"12.5"` → `+12,5%`, `"-3.0"` → `−3,0%`, `"0.0"` → `0,0%`, `null`/śmieci → „Brak danych” |
| F-2 `reports-api.spec.ts` | unit | adresy; parametry okresu tylko potrzebne dla rodzaju; filtry i strona zestawienia |
| F-3 `dashboard.service.spec.ts` — wybór | unit | domyślnie bieżący miesiąc; błędy własnego zakresu (puste, „od” > „do”, „do” > dziś + 1 rok) bez żądania; zmiana okresu → strona 1, bez filtrów, oba odczyty |
| F-4 `dashboard.service.spec.ts` — stan | unit | `enter()` ponownie pobiera raport i bieżącą stronę z zachowanym stanem (decyzja 4); `422` → `rangeError`; błędy osobno; stare odpowiedzi ignorowane (sekwencja); `showChannel`/`showOrders`/`showAllComponents`; `yearOptions` z `launchDate`; linki do wydatków z `effectiveFrom`/`to`; brak arytmetyki (wartości przepisane z API) |
| F-5 `report-text.spec.ts` | unit | etykiety okresów; `launchNotice`; trzy warianty opisu porównania (w tym rok w toku); tekst porównania karty (więcej/mniej/bez zmian, `+`, „brak podstawy porównania”, liczba zamówień); etykiety przedziałów |
| F-6 `period-picker.spec.ts` | component | pola zależne od rodzaju okresu; `max` dat; komunikaty przy polach; `422` w `role="alert"` |
| F-7 `sales-view.spec.ts` — karty | component | 4 karty z wartościami, opisami i porównaniem; wskazówki „Pokaż składniki”, „Pokaż wydatki”, „Pokaż w zestawieniu” (decyzja 6); Wynik bez akcji; link „Koszty” z `kosztOd`/`kosztDo` |
| F-8 `dashboard-page.spec.ts` | component | przełącznik widoku (radio, legenda), zaślepka przepływów z linkiem `/operacje`; „Stan na …”, `app-incomplete-data-notice`, ogólna etykieta podstawy; `enter()` przy każdym utworzeniu strony |
| F-9 `sales-components.spec.ts` | component | kolumny, link do zamówienia, kwota ze znakiem, suma z etykietą (także ujemna przy filtrze zwrotu), chipy i „Wyczyść filtry”, puste i filtrowane puste, błąd z ponowieniem, ukrycie przy `beforeLaunch` |
| F-10 `expenses-list-page.spec.ts`, `expenses.service.spec.ts` | component / unit | `kosztOd`/`kosztDo`/`kategoria` → filtry, inne filtry wyczyszczone, sortowanie domyślne, strona 1; niepoprawne wartości pominięte; „Suma brutto” z `grossTotal` (ujemna z minusem), ukryta przy błędzie; `wydatekCykliczny` bez zmian |
| F-11 `chart-canvas.spec.ts` | component (fałszywe `CHART_FACTORY`) | konfiguracja z tokenów (bez literałów), linia przerywana wyniku, `beginAtZero`; klawiatura ←/→/↑/↓/Home/End/Escape zmienia aktywny punkt i tekst `aria-live` ze sformatowanym ciągiem API; `role="img"`, `aria-label`, `tabindex`; `animation: false` przy reduced motion; `destroy()` przy zniszczeniu |
| F-12 `sales-view.spec.ts` — stany i przejścia | component | ładowanie bez zer; błąd z „Spróbuj ponownie”; `beforeLaunch` → komunikat zamiast kart; tabela danych wykresu za przyciskiem (`aria-expanded`) z „Razem” = wartości kart; listy kanałów i kategorii zawsze widoczne; klik kanału/karty → filtr i fokus nagłówka zestawienia |
| F-13 `app.routes.spec.ts`, `app-shell.spec.ts`, `app.spec.ts` | component | `/pulpit` ładuje Pulpit z obsłużonymi żądaniami; powłoka bez nieobsłużonych żądań |

E2E: odroczone do SF-21 (brak infrastruktury E2E, jak w SF-15/SF-17). Ręczne scenariusze UI w
`06-implementation-done.md`.

## Verification

Środowisko (fakty dla wykonawcy):

- Build i testy **w Release** — `bin/Debug` może być zablokowany przez API uruchomione w Visual
  Studio. `dotnet ef` także z `--configuration Release`.
- Pliki migracji zapisywać w **UTF-8 z BOM** (jak istniejące; inaczej `dotnet format`/diff pokaże
  zmianę kodowania).
- Testy integracyjne na LocalDB:
  `export STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true'`.
- Pamięć ograniczona: `-m:1`, polecenia kolejno (nie uruchamiać testów .NET i `npm run build`
  naraz).

Backend, z katalogu repozytorium (Git Bash):

```
dotnet restore stone-flow.slnx
dotnet build stone-flow.slnx -c Release -m:1 --no-restore
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Orders.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Expenses.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release --no-build
export STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true'
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*SalesReportApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*ExpensesApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OperationsApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*SalesCorrectionsApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*EndpointSecurityTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OpenApiTests"
```

Po `dotnet ef migrations add … --configuration Release` przejrzeć `Up`/`Down` (tylko
`CreateIndex`/`DropIndex`) i kodowanie plików. Pełny `dotnet test` i
`deploy/scripts/test-migrate.sh` uruchamia CI; `dotnet restore … --locked-mode` w CI wymaga
aktualnych `packages.lock.json` (gdy zmienia się `StoneFlow.Reporting.Tests.csproj`).

Frontend, z `app/`, kolejno:

```
npm install --save-exact chart.js@4.6.1     # jednorazowo (F-1); potem npm ci w CI
npm run typecheck
npm test -- --watch=false
npm run lint
npm run format:check
npm run build                                # sprawdzić, że budżet initial się nie zmienił, a chart.js jest w chunku Pulpitu
```

Manual (ryzyko `high` — **przegląd człowieka przed merge**):

- scenariusze ręczne UI w `06-implementation-done.md` (tabela): przełączanie okresów (miesiąc,
  poprzedni, rok, własny, przed uruchomieniem); przejścia z kart „Sprzedaż”, „Koszty”, „Liczba
  zamówień”, kanałów i kategorii z porównaniem sum (zestawienie = karta, lista wydatków = karta /
  kategoria); powrót z zamówienia z zachowanym stanem i nowym „Stan na”; klawiatura na trzech
  wykresach i czytnik ekranu (`aria-live`); tabela danych wykresu; telefon, tablet, szeroki ekran;
  niekompletna integracja; `prefers-reduced-motion`;
- przegląd kodu: `SalesEligibility` i jej użycie w źródle (brak predykatu SQL), `SalesPolicy`,
  `ReportPeriod.Resolve` (szczególnie rok, decyzja 7), `Comparison` (zaokrąglenie), bezpiecznik
  10 001, `grossTotal` na tym samym filtrze, brak arytmetyki pieniędzy w UI poza `chartNumber`;
- **otwarty punkt dla właściciela (decyzja 7):** „Bieżący rok porównujemy z tym samym odcinkiem
  poprzedniego roku (01.01 – ten sam dzień), a zakończony rok z całym poprzednim. Czy tak?” —
  wpisać w `06-implementation-done.md` i w opisie PR.

## Risks

- **Ryzyko `high`** (definicje KPI, FIN-01–06): błąd reguły wyłączeń daje zły wynik bez widocznego
  błędu. Łagodzą: jedna reguła statusu w `Orders.Domain` bez kopii w SQL (decyzja 1), testy
  PRD §11 w domenie, uzgodnienia sum w testach integracyjnych; obowiązkowy przegląd człowieka.
- **Decyzja 3 zmienia kontrakt zestawienia ze spec** (parametry okresu zamiast wymaganych
  `from`/`to`; obiekt `period` zamiast `effectiveFrom`/`launchDate`; `sum = null` przy
  `beforeLaunch`). Zachowanie biznesowe bez zmian; `06-implementation-done.md` powinien to
  odnotować jako odstępstwo z decyzji planu.
- **Decyzja 2 dodaje regułę walidacji** spoza spec (`to` ≤ dziś + 1 rok). Wpis z datą później niż
  rok od dziś nie da się pokazać własnym zakresem do czasu, aż ta data się zbliży — w praktyce
  bez znaczenia, ale do odnotowania.
- **Decyzja 7** czeka na potwierdzenie właściciela; zmiana dotyczy jednej gałęzi `Resolve` i RP-2.
- **Znak minus:** kwoty przez `formatPln` mają `-` (U+002D, decyzja SF-17), `formatPercent` ma `−`
  (U+2212, wprost ze spec). Niespójność wizualna na karcie („−3,0% (-50,00 zł)”). Ujednolicenie
  formatera w całej aplikacji to osobne zadanie.
- **Chart.js w jsdom:** testy komponentów muszą podmieniać `CHART_FACTORY`; prawdziwe renderowanie
  sprawdza tylko scenariusz ręczny. `getComputedStyle` w jsdom zwraca puste tokeny — test sprawdza
  nazwy tokenów, nie kolory.
- **Rozmiar paczki:** `chart.js` (~70 kB gzip) w chunku Pulpitu; import poza `chart-factory.ts`
  wciągnąłby go do `initial` — sprawdzić wynik `npm run build`.
- **Dwa równoległe żądania z Pulpitu** (raport i zestawienie) przy każdym wejściu i zmianie okresu;
  każde czyta źródła osobno (zestawienie ponownie wczytuje składniki). Przy skali D-08 pomijalne;
  bezpiecznik `422` ujawni wzrost.
- **Łączenie i stronicowanie w pamięci** (odstępstwo od konwencji) — jak w SF-17, uzasadnione w spec.
- **Wydajność I-S-12** (10 001 wierszy na LocalDB): wstawienie zbiorcze jednym `INSERT … SELECT`, nie
  przez API.
- **Lista wydatków a karta „Koszty”:** suma listy równa karcie tylko przy filtrze z Pulpitu; po
  dodaniu innego filtra przez użytkownika suma dotyczy bieżącego filtra (zgodne ze spec Edge
  Cases).
- **Oś Y z wartościami ujemnymi** (zwroty większe od sprzedaży w miesiącu): `beginAtZero` w
  Chart.js obsługuje zakres ujemny; potwierdzić w scenariuszu ręcznym.
