# Implementation Plan — SF-20

Tryb: **fullstack**. Zadanie dodaje regułę domeny Reporting (`CashFlowSummary`), wydziela wspólną
operację odczytu operacji pieniężnych z `ListOperationsHandler`, dodaje endpoint
`GET /api/reports/cash-flow` oraz UI Pulpitu (widok „Przepływy pieniężne”) i odczyt parametrów
przejścia na liście „Operacje”. Plany backendu i frontendu napisał jeden autor po kolei (bez
subagentów): najpierw backend, potem frontend z kontraktem wyprowadzonym ze spec i z rozstrzygnięć
poniżej (nie z planu backendu). Oba porównano w „Integration Check”.

Zadanie nie jest dzielone na części. Nie ma migracji ani nowych kontraktów międzymodułowych, endpoint
jest nowy (addytywny), a zaślepka „Przepływy pieniężne” nie ma zachowania do zachowania. Backend wdraża
się przed frontendem, więc stary frontend działa bez zmian. Gałąź bazuje na `sf-19-sales-view`; merge
dopiero po SF-19. Ryzyko `high`: **przegląd człowieka przed merge**.

## Scope

- **Reporting.Domain:** `CashFlowPolicy.OutflowKinds`; nowy `CashFlowSummary` (z `CashFlowEntry` i
  `KindAmount`) w `CashFlow/`. `CashMovement`, `CashFlowTotals`, `ReportPeriod`, `LaunchCutoff`,
  `Comparison` bez zmian.
- **Reporting.Application:** wspólna operacja `Operations/CashOperationReader` (odczyt źródeł z
  bezpiecznikiem i stała kolejność), `ListOperationsHandler` przepięty na nią bez zmiany wyniku; nowy
  przypadek `CashFlow/GetCashFlowReport`.
- **Reporting.Api:** `CashFlowReportEndpoints`, `CashFlowReportContracts`; mapowania z
  `OperationContracts` i `SalesReportContracts` stają się `internal` i współdzielone; rejestracja w
  `ReportingModule`.
- **Frontend:** `shared/dictionaries/operations.ts` (typy i funkcje operacji przeniesione z
  `features/operations/operation-models.ts`, który je dalej eksportuje); `features/dashboard/`
  (wspólny wybór okresu i nagłówek, stan przepływów w `DashboardService`, `cashFlowReport` w
  `ReportsApi`, nowy `app-cash-flow-view`, ikony i linki `KpiCard`); `features/operations/`
  (parametry `od`/`do`/`rodzaj` z usunięciem z adresu, `OperationsService.filterFromDashboard`).
- Bez zmian: schemat bazy, migracje, `Shared.Kernel`, `Shared.Application`, źródła w Orders i
  Expenses, `deploy/`, zależności npm i NuGet.

### Rozstrzygnięcia uwag z przeglądu (`03-spec-review.md`)

| # | Uwaga przeglądu | Decyzja | Gdzie w planie |
|---|---|---|---|
| 1 | Współdzielone typy i funkcje operacji na froncie | **Przeniesienie do `app/src/app/shared/dictionaries/operations.ts`** (bez kopii i bez importów między obszarami): typy `OperationKind`, `OperationDirection`, `OperationSourceType`, `OperationRefundKind`, `OperationMethod`, `OperationCategory`, `OperationItem`; stałe `OPERATION_KINDS`, `KIND_FILTER_LABELS`, `DIRECTION_LABELS`, `NEGATIVE_AMOUNT_LABEL`; funkcje `kindLabel`, `isNegativeAmount`, `sourceLink`. **`sourceLink` też się przenosi** (trzy trasy to wiedza nawigacyjna wspólna dla obu tabel) — to zmienia założenie spec („mapa linku … w modelu Pulpitu”) na wariant 1 z przeglądu; kopii nie ma, więc test zgodności tras nie jest potrzebny. `features/operations/operation-models.ts` eksportuje wszystko dalej pod dotychczasowymi nazwami; `methodLabel`, `NO_METHOD`, `OperationTotals`, `OperationListPage`, `OperationListQuery`, `OPERATIONS_PROBLEM` zostają w obszarze operacji. `features/dashboard` importuje wyłącznie z `shared/dictionaries/operations`. | F1, F2, test F-D1 |
| 2 | Zapis filtra rodzaju w kryterium 2 | Test integracyjny używa **powtórzonych parametrów** `kind=refund&kind=expense&kind=withdrawal` (tak przyjmuje `string[] kind` w `OperationsEndpoints`); w linkach UI analogicznie `rodzaj=refund&rodzaj=expense&rodzaj=withdrawal` (tablica w `queryParams`). | I-CF-2, F6, test F-S4 |
| 3 | Stan Pulpitu zależny od widoku | W `DashboardService`: (a) **`rangeError` tylko aktywnego widoku** — „Sprzedaż”: `reportRangeError ?? componentsRangeError`, „Przepływy”: `cashFlowRangeError`; (b) **zmiana widoku czyści błędy (`error`, `rangeError`) opuszczanego widoku** i unieważnia jego żądania w toku (`++seq`, `loading = false`), po czym wczytuje aktywny widok; (c) **`launchDate` dla `yearOptions` ustawia każdy wczytany raport** (oba niosą `period.launchDate`) — wygrywa ostatnio wczytany; (d) **osobny licznik żądań na widok** (`reportSeq`, `componentsSeq` — jak dziś — i nowy `cashFlowSeq`); odpowiedź opuszczonego widoku jest odrzucana. Testy: `422` w „Sprzedaży” → przełączenie → brak komunikatu w „Przepływach”; odpowiedź raportu sprzedaży po przełączeniu na „Przepływy” nie zmienia stanu; `yearOptions` z `launchDate` raportu przepływów. | F7, testy F-S2, F-S3, F-S5 |
| 4 | Powrót „Wstecz” na `/operacje?od=…&rodzaj=…` | **Lista czyta parametry raz (snapshot), ustawia filtr przez serwis, a potem usuwa je z adresu** `router.navigate([], { relativeTo: route, queryParams: {}, replaceUrl: true })`. Wpis historii z parametrami zostaje zastąpiony adresem `/operacje`, więc „Wstecz” ze źródła operacji wraca na `/operacje` bez parametrów i serwis zachowuje zmieniony filtr (SF-17, pkt 14). Różni się to świadomie od `/wydatki` (SF-19, parametry zostają). Scenariusz ręczny w `06`. | F10, test F-O2, scenariusz ręczny |
| 5 | Zakres testu „zysk” i „saldo” | Test renderuje **cały `DashboardPage` w widoku „Przepływy pieniężne”** (po wczytaniu raportu) i sprawdza bez rozróżniania wielkości liter, że `textContent` hosta oraz wartości atrybutów `aria-label` i `title` wszystkich elementów (w tym canvasów `ChartCanvas`) nie zawierają `zysk` ani `saldo`. Osobny przypadek po otwarciu tabeli danych wykresu. Test API sprawdza, że odpowiedź nie ma pola zawierającego `balance`/`saldo`. | F-CF7, I-CF-1 |
| 6 | Wynik `CashFlowSummary` przy `BeforeLaunch` | **Spójnie z `CostSummary`:** `Totals` = 0/0/0, `OutflowsByKind` = trzy pozycje w kolejności `OutflowKinds` z `0`, `Buckets` = `[]` (bo `ReportPeriod.Buckets()` jest pusta). Wpisy są ignorowane. API pokazuje okres przed uruchomieniem jak SF-19: handler nie woła źródeł ani `CashFlowSummary`, zwraca `kpis: null`, `series: []`, `outflowsByKind: []`, `recentOperations: []`, a `period.beforeLaunch = true`. Test domeny CF-11 sformułowany w ten sposób. | B2, B4, testy CF-11, H-CF-3 |
| 7 | Widoczność mapowań kontraktów | Bez przenoszenia folderów (conventions: „Existing names and structure are not migrated”). Stają się `internal` i współdzielone: `OperationContracts.ToResponse(OperationItemDto)` (z `private`), `SalesReportContracts.ToResponse(ReportPeriod)` i `SalesReportContracts.ToResponse(AmountKpiDto)` (z `private`) oraz nowa `internal static SalesReportComparisonResponse SalesReportContracts.ComparisonOf(ReportPeriod period, bool available)` wydzielona z `ToResponse(SalesReportDto)`. W Application `CashFlow/` używa bez zmian: `Sales/ReportPeriodInput` (internal, ten sam assembly), `SalesReportCodes` (kody okresu, granulacji, statusu porównania, `FormatPercent`, `BeforeLaunchReason`), `Sales.AmountKpiDto`, `WarsawTime`, `OperationCodes` (kody rodzajów, kwoty, daty). | B5, B6, test I-CF-1 |

Niejasności z przeglądu:

| Niejasność | Rozstrzygnięcie | Gdzie |
|---|---|---|
| Pkt 19: poprawne `od`, niepoprawne `do` | Okres to **całe okno**: brak, niepoprawna lub nieistniejąca którakolwiek data albo `od` > `do` → **obie daty** z bieżącego miesiąca (`currentMonth()`). Nigdy nie powstaje `od` > `do`. | F9, test F-O1 |
| Pkt 18 a 19: sam `rodzaj` bez dat | Wejście z samym `rodzaj` (np. `/operacje?rodzaj=payment`) → bieżący miesiąc **z tym rodzajem**, sortowanie data malejąco, strona 1 (a nie okres zachowany w serwisie). Parametry są „obecne”, gdy w adresie jest którykolwiek z `od`, `do`, `rodzaj` (także z niepoprawną wartością). | F9, test F-O1 |
| Pkt 22: zmiana okresu w „Przepływach” a zestawienie sprzedaży | Okres jest jeden dla obu widoków. **`setSelection` zawsze zeruje filtry, sortowanie i stronę zestawienia sprzedaży** (`resetComponents`, jak SF-19) i wczytuje tylko aktywny widok. Po przełączeniu na „Sprzedaż” raport i zestawienie wczytują się z tym okresem od strony 1, bez filtrów. | F7, test F-S1 |
| UI pkt 5: stronicowanie „Ostatnich operacji” | Raport zwraca **dokładnie do 10 pozycji** (`RecentOperationsCount = 10`, bez parametru `pageSize` w API). Tabela dostaje `[total]` = liczba pozycji, `[page]="1"`, `[pageSize]="10"`, bez `sort` i `filterable`. `app-data-table` zawsze rysuje pasek stronicowania; przy tych wartościach „Poprzednia” i „Następna” są nieaktywne, a tekst zakresu to „1–N z N” (bez zmian w `shared/ui`). | B4, F12, test F-CF5 |

Konflikt dokumentacji z przeglądu (`Docs & Decision Conflicts`): `features/dashboard/dashboard-page.ts`
importuje `features/integration/incomplete-data-notice` (stan z SF-19). **Nie jest to trywialne do
naprawy**: komponent czyta `IntegrationStatus` z `features/integration/integration-status.ts`, którego
używają też `layout/integration-indicator.ts` i strona integracji, więc przeniesienie do `shared/ui`
pociąga przeniesienie serwisu stanu integracji (albo `shared/` importowałby obszar). Plan **nie
zmienia** tego importu; SF-20 nie dodaje nowego importu między obszarami. Otwarty punkt do
`06-implementation-done.md`: przeniesienie `IncompleteDataNotice` + `IntegrationStatus` do `shared/`
albo wyjątek w conventions.

Inne rozstrzygnięcia przyjęte z kodu:

- **Kolejność remisu** w obu miejscach to kolejność deklaracji `CashOperationKind` (`Payment`,
  `Refund`, `PaidExpense`, `OwnerWithdrawal`), a potem `Id` — dokładnie jak dziś w
  `ListOperationsHandler` (`ThenBy(Kind).ThenBy(Id)`). Właścicielem staje się `CashOperationReader.Order`.
- **`MaxOperations`:** stała przechodzi do `CashOperationReader.MaxOperations = 10_000`;
  `ListOperationsHandler.MaxOperations` zostaje jako `public const int MaxOperations = CashOperationReader.MaxOperations;`
  (używa jej `ListOperationsHandlerTests`, który ma zostać bez zmian).
- **Kod 422:** raport przepływów zwraca `report_range_too_large` (jak raporty SF-19), lista
  „Operacje” dalej `operations_range_too_large`.
- **Progi szerokości:** spec podaje 768 px i 1200 px; Tailwind ma `md` = 768 px i `xl` = 1280 px, a
  widok „Sprzedaż” (SF-19) używa `md`/`xl`. Widok przepływów używa **tych samych klas `md`/`xl`**,
  żeby oba widoki jednej strony przełączały układ w tym samym miejscu. Rozbieżność 1200/1280 → Risks.

## Backend Plan

### Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Domain | `Reporting.Domain/CashFlow/` | `OutflowKinds`; `CashFlowSummary` (sumy okresu, podział wypływów, przedziały) |
| Application | `Reporting.Application/Operations/` | wspólna operacja odczytu i kolejności (`CashOperationReader`); przepięcie `ListOperationsHandler` |
| Application | `Reporting.Application/CashFlow/` | zapytanie i handler raportu przepływów |
| Api | `Reporting.Api/Endpoints/`, `ReportingModule` | endpoint, kontrakty, współdzielone mapowania |
| Tests | `StoneFlow.Reporting.Tests`, `StoneFlow.Api.IntegrationTests` | domena, aplikacja, HTTP i baza |

Reporting nie dostaje zmian schematu ani DbContextu. Orders i Expenses bez zmian (istniejące
`OrderCashOperationSource`, `ExpenseCashOperationSource`).

### API contract (backend)

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/reports/cash-flow` | `period`, `year`, `from`, `to` (jak `/api/reports/sales`) | `200 CashFlowReportResponse`; `400 ValidationProblem` (klucze `period`, `year`, `from`, `to`); `422 report_range_too_large` | sesja (fallback policy) | Reporting, jedna firma |
| GET | `/api/operations` | bez zmian | bez zmian | sesja | Reporting |

Szczegóły kształtów — sekcja „API Contract”.

### Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/CashFlow/CashFlowPolicy.cs` | modify | `public static IReadOnlyList<CashOperationKind> OutflowKinds { get; } = [PaidExpense, OwnerWithdrawal, Refund]` z XML doc (kolejność PRD §5) |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/CashFlow/CashFlowSummary.cs` | create | `CashFlowEntry`, `KindAmount`, `CashFlowSummary` + `Of(entries, period)` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Application/Operations/CashOperationReader.cs` | create | `public static class CashOperationReader`: `MaxOperations`, `AllKinds`, `ReadAsync(...)`, `Order(...)` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Application/Operations/ListOperations.cs` | modify | odczyt i kolejność przez `CashOperationReader`; `MaxOperations` deleguje do stałej readera; reszta bez zmian |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Application/CashFlow/GetCashFlowReport.cs` | create | `GetCashFlowReportQuery`, `CashFlowKpisDto`, `CashFlowSeriesPointDto`, `CashFlowReportDto`, `GetCashFlowReportResult`, `GetCashFlowReportHandler` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Api/Endpoints/CashFlowReportEndpoints.cs` | create | grupa `/api/reports/cash-flow`, tag `Reports`, `.WithName("Reporting.GetCashFlowReport")` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Api/Endpoints/CashFlowReportContracts.cs` | create | odpowiedzi i mapowanie DTO → JSON |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Api/Endpoints/OperationContracts.cs` | modify | `ToResponse(OperationItemDto)` `private` → `internal` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Api/Endpoints/SalesReportContracts.cs` | modify | `ToResponse(ReportPeriod)`, `ToResponse(AmountKpiDto)` → `internal`; wydzielona `internal ComparisonOf(ReportPeriod, bool)`; `ToResponse(SalesReportDto)` z niej korzysta (wynik bez zmian) |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Api/ReportingModule.cs` | modify | `CashFlowReportEndpoints.Map(endpoints)`; XML doc z `/api/reports/cash-flow` (SF-20) |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Api/ReportingProblems.cs` | modify | tylko komentarz przy `ReportRangeTooLarge`: także raport przepływów (SF-20) |
| `api/tests/StoneFlow.Reporting.Tests/CashFlow/CashFlowPolicyTests.cs` | modify | CP-1 (`OutflowKinds`); dotychczasowe przypadki bez zmian |
| `api/tests/StoneFlow.Reporting.Tests/CashFlow/CashFlowSummaryTests.cs` | create | CF-1 … CF-12 |
| `api/tests/StoneFlow.Reporting.Tests/CashFlow/FakeCashSources.cs` | create | `internal` fałszywe `ICashOperationSource` z dziennikiem wywołań (wspólny `SourceLog`-podobny dziennik kolejności), fabryka `CashOp(...)`; korzysta z `Sales.FixedLaunchDate` i `ReportFacts.Clock` |
| `api/tests/StoneFlow.Reporting.Tests/Operations/CashOperationReaderTests.cs` | create | RD-1 … RD-4 |
| `api/tests/StoneFlow.Reporting.Tests/CashFlow/GetCashFlowReportHandlerTests.cs` | create | H-CF-1 … H-CF-9 |
| `api/tests/StoneFlow.Api.IntegrationTests/Reporting/CashFlowReportApiTests.cs` | create | I-CF-1 … I-CF-11 |

Bez zmian: `ListOperationsHandlerTests.cs`, `OperationsApiTests.cs`, `SalesReportApiTests.cs` (muszą
przejść bez edycji), `.csproj`/`packages.lock.json`, `stone-flow.slnx`, `EndpointSecurityTests`
(`AllowedAnonymous` bez wpisu), `StoneFlow.Architecture.Tests`, `deploy/scripts/contexts.sh`.

### Implementation steps

Kolejność TDD: każdy krok domeny i aplikacji zaczyna się od czerwonego testu (sekcja „Tests”).

- **B1 `CashFlowPolicy.OutflowKinds`** (CP-1 najpierw). Lista tylko do odczytu w kolejności
  `PaidExpense`, `OwnerWithdrawal`, `Refund`. XML doc: kolejność podziału wypływów (PRD §5), inna niż
  kolejność remisu listy — dwie reguły prezentacji, każda w jednym miejscu. Każda pozycja ma
  `DirectionOf == Outflow` (test), a zbiór `OutflowKinds` = wszystkie rodzaje z kierunkiem `Outflow`
  (test — nowy rodzaj wypływu bez wpisu w liście czerwieni test).
- **B2 `CashFlowSummary`** (CF-1 … CF-12 najpierw), plik `CashFlow/CashFlowSummary.cs`, bez I/O:
  - `public sealed record CashFlowEntry(CashOperationKind Kind, DateOnly Date, decimal Amount);` —
    fakt operacji (rodzaj, data operacji pieniężnej, kwota zapisana ze znakiem dla wydatku).
  - `public sealed record KindAmount(CashOperationKind Kind, decimal Amount);`
  - `public sealed record CashFlowSummary(CashFlowTotals Totals, IReadOnlyList<KindAmount> OutflowsByKind, IReadOnlyList<CashFlowTotals> Buckets)`.
  - `public static CashFlowSummary Of(IEnumerable<CashFlowEntry> entries, ReportPeriod period)`:
    - `ArgumentNullException.ThrowIfNull` dla obu parametrów;
    - przy `period.BeforeLaunch` → `Totals = CashFlowTotals.Of([])` (0/0/0), `OutflowsByKind` = trzy
      pozycje `OutflowKinds` z `0m`, `Buckets = []` (rozstrzygnięcie 6);
    - inaczej: wpisy z `period.Effective.Contains(entry.Date)` (pozostałe pomijane);
      `Totals = CashFlowTotals.Of(wpisy → new CashMovement(Kind, Amount))` — **ta sama reguła co
      sumy listy**, bez kopii kierunku;
    - `OutflowsByKind`: dla każdego rodzaju z `OutflowKinds` suma `Amount` wpisów tego rodzaju (bez
      wartości bezwzględnej, `0m` dla braku). Wpłaty nie trafiają do podziału;
    - `Buckets`: dla każdego przedziału z `period.Buckets()` `CashFlowTotals.Of` wpisów z
      `period.BucketIndexOf(entry.Date) == index` (przedział bez wpisów → 0/0/0). Liczba elementów =
      `period.Buckets().Count`.
  - XML doc: PRD §8 „Przepływy pieniężne”; suma `OutflowsByKind` = `Totals.Outflows`, suma
    przedziałów = `Totals` (testy); kwoty bez zaokrąglania (KWO-01).
- **B3 `CashOperationReader`** (RD-1 … RD-4 najpierw), `Operations/CashOperationReader.cs`,
  `public static class` (publiczna, żeby testować ją wprost; testy nie mają `InternalsVisibleTo`):
  - `public const int MaxOperations = 10_000;`
  - `public static IReadOnlySet<CashOperationKind> AllKinds { get; }` — wszystkie wartości enuma.
  - `public static async Task<IReadOnlyList<CashOperation>?> ReadAsync(IEnumerable<ICashOperationSource> sources, DateOnly from, DateOnly to, IReadOnlySet<CashOperationKind> kinds, CancellationToken cancellationToken)`
    — **przeniesiona pętla z `ListOperationsHandler` bez zmiany zachowania**: źródła po kolei
    (sekwencyjnie, bez `Task.WhenAll`), każde dostaje tylko swoje żądane rodzaje (źródło bez
    żądanych rodzajów nie jest wołane), limit malejący `MaxOperations + 1 − wczytane`, przy
    `remaining <= 0` przerwanie; wynik `null`, gdy wczytano więcej niż `MaxOperations`. `from` to już
    efektywny początek (walidacja i przełom uruchomienia zostają w handlerach).
  - `public static IOrderedEnumerable<CashOperation> Order(IEnumerable<CashOperation> operations, bool descending)`
    — data malejąco albo rosnąco, potem `Kind` (kolejność deklaracji), potem `Id`. Jedyne miejsce tej
    reguły.
  - XML doc: właściciel bezpiecznika i kolejności; walidację, przełom uruchomienia, sumy i
    stronicowanie mają handlery; operacja nie zapisuje i nie ponawia (same odczyty).
- **B4 `ListOperationsHandler`** — przepięcie bez zmiany wyniku:
  - `public const int MaxOperations = CashOperationReader.MaxOperations;` (zachowana nazwa).
  - Pętla zastąpiona `var loaded = await CashOperationReader.ReadAsync(_sources, effectiveFrom, criteria.To, criteria.Kinds, cancellationToken);`
    `if (loaded is null) return new ListOperationsResult(null, TooManyOperations: true);`
  - Sortowanie `CashOperationReader.Order(loaded, criteria.Descending).Skip(...).Take(...)` — gałąź
    `skip >= Count` i reszta bez zmian. Walidacja, gałąź „przed uruchomieniem”, sumy bez zmian.
  - `ListOperationsHandlerTests` i `OperationsApiTests` przechodzą bez edycji (regresja).
- **B5 `GetCashFlowReport`** (H-CF-* najpierw), `CashFlow/GetCashFlowReport.cs`, wzór
  `Sales/GetSalesReport.cs`:
  - `public sealed record GetCashFlowReportQuery(string? Period = null, string? Year = null, string? From = null, string? To = null) : IQuery<GetCashFlowReportResult>;`
  - `public sealed record CashFlowKpisDto(AmountKpiDto Inflows, AmountKpiDto Outflows, AmountKpiDto Change);`
    (`AmountKpiDto` z `Sales/GetSalesReport.cs`, bez kopii).
  - `public sealed record CashFlowSeriesPointDto(DateRange Range, CashFlowTotals Totals);`
  - `public sealed record CashFlowReportDto(ReportPeriod Period, bool ComparisonAvailable, CashFlowKpisDto? Kpis, IReadOnlyList<CashFlowSeriesPointDto> Series, IReadOnlyList<KindAmount> OutflowsByKind, IReadOnlyList<OperationItemDto> RecentOperations, DateTimeOffset CalculatedAtUtc);`
    (`OperationItemDto` z `Operations/ListOperations.cs` — jeden typ pozycji dla obu przypadków).
  - `public sealed record GetCashFlowReportResult(CashFlowReportDto? Report, FieldErrors? Errors = null, bool TooManyRows = false);`
  - `public sealed class GetCashFlowReportHandler(IEnumerable<ICashOperationSource> sources, ILaunchDate launchDate, TimeProvider time)`;
    `public const int RecentOperationsCount = 10;` `_sources = [.. sources]`.
    1. `ReportPeriodInput.Validate(query.Period, query.Year, query.From, query.To, WarsawTime.Today(time), launchDate.Value, errors)`;
       `null` → `new GetCashFlowReportResult(null, errors)`.
    2. `period.BeforeLaunch` → `new CashFlowReportDto(period, false, null, [], [], [], time.GetUtcNow())`
       bez odpytywania źródeł.
    3. `current = await CashOperationReader.ReadAsync(_sources, period.EffectiveFrom, period.Range.To, CashOperationReader.AllKinds, ct)`;
       `null` → `TooManyRows: true`.
    4. Przy `period.BaseAvailable`: `var basePeriod = period.ForBase();`
       `baseline = await CashOperationReader.ReadAsync(_sources, basePeriod.EffectiveFrom, basePeriod.Range.To, AllKinds, ct)`;
       `null` → `TooManyRows: true`. Bez `BaseAvailable` baza nie jest czytana.
    5. Mapowanie `CashOperation` → `CashFlowEntry(op.Kind, op.Date, op.Amount)` (prywatna metoda
       handlera); `summary = CashFlowSummary.Of(currentEntries, period)`, przy bazie
       `baseSummary = CashFlowSummary.Of(baseEntries, basePeriod)`.
    6. Karty: z bazą `new AmountKpiDto(summary.Totals.Inflows, Comparison.Of(summary.Totals.Inflows, baseSummary.Totals.Inflows))`
       i analogicznie `Outflows`, `Change`; bez bazy `Comparison.NoBasis` na każdej karcie. Baza karty
       równa 0 → `NoBasis` tylko tej karty (reguła w `Comparison.Of`).
    7. `Series` = `period.Buckets()` sparowane po indeksie z `summary.Buckets`.
    8. `RecentOperations` = `CashOperationReader.Order(current, descending: true).Take(RecentOperationsCount)`
       → `new OperationItemDto(op, CashFlowPolicy.DirectionOf(op.Kind))`.
    9. `CalculatedAtUtc = time.GetUtcNow()`.
  - XML doc klasy: SF-20; te same źródła, reguła sum (`CashFlowTotals`), przełom uruchomienia i
    kolejność co lista „Operacje”, więc karty = sumy listy, a ostatnie operacje = jej pierwsza strona
    (FIN-05); wywołania sekwencyjne (wspólne `ModuleConnection`); łączenie w pamięci uzasadnia skala
    (D-08), bezpiecznik osobno dla okresu i bazy.
  - Rejestracja: automatyczna przez `AddHandlersFromAssembly` (bez zmian DI).
- **B6 Kontrakty HTTP** (`CashFlowReportContracts.cs`), komentarz nagłówkowy jak w
  `SalesReportContracts.cs` (kwoty jako tekst, procent `"12.5"`, daty `yyyy-MM-dd`, kody rodzajów z
  `OperationCodes`):
  - `public sealed record CashFlowKpisResponse(AmountKpiResponse Inflows, AmountKpiResponse Outflows, AmountKpiResponse Change);`
  - `public sealed record CashFlowSeriesPointResponse(string From, string To, string Inflows, string Outflows, string Change);`
  - `public sealed record OutflowKindAmountResponse(string Kind, string Amount);`
  - `public sealed record CashFlowReportResponse(SalesReportPeriodResponse Period, SalesReportComparisonResponse Comparison, CashFlowKpisResponse? Kpis, IReadOnlyList<CashFlowSeriesPointResponse> Series, IReadOnlyList<OutflowKindAmountResponse> OutflowsByKind, IReadOnlyList<OperationItemResponse> RecentOperations, DateTimeOffset CalculatedAtUtc);`
  - `internal static class CashFlowReportContracts { public static CashFlowReportResponse ToResponse(CashFlowReportDto report) }`:
    okres przez `SalesReportContracts.ToResponse(report.Period)`, nagłówek porównania przez
    `SalesReportContracts.ComparisonOf(report.Period, report.ComparisonAvailable)`, karty przez
    `SalesReportContracts.ToResponse(AmountKpiDto)`, pozycje przez
    `OperationContracts.ToResponse(OperationItemDto)`, kwoty `OperationCodes.FormatAmount`, rodzaj
    podziału `OperationCodes.Of(kind)` (`expense`, `withdrawal`, `refund`), `CalculatedAtUtc.ToUniversalTime()`.
  - `SalesReportContracts`: wydzielenie `ComparisonOf` (`new SalesReportComparisonResponse(FormatDate(Base.From), FormatDate(Base.To), available, available ? null : SalesReportCodes.BeforeLaunchReason)`),
    zmiana widoczności dwóch metod. `OperationContracts`: zmiana widoczności. Odpowiedzi
    `/api/reports/sales` i `/api/operations` bez zmian (regresja testów integracyjnych).
- **B7 Endpoint** (`CashFlowReportEndpoints.cs`), wzór `SalesReportEndpoints.GetAsync`:
  `MapGroup("/api/reports/cash-flow").WithTags("Reports")`, `MapGet(string.Empty, GetAsync).WithName("Reporting.GetCashFlowReport")`;
  `[FromQuery] string? period, year, from, to`; `TooManyRows` → `ReportingProblems.ReportRangeTooLarge()`;
  raport → `TypedResults.Ok(CashFlowReportContracts.ToResponse(report))`; inaczej
  `TypedResults.ValidationProblem(result.Errors?.ToDictionary() ?? new Dictionary<string, string[]>())`.
  Bez `.AllowAnonymous()`. `ReportingModule.MapReportingEndpoints` dopisuje
  `CashFlowReportEndpoints.Map(endpoints)` (po `SalesReportEndpoints`).
- **B8 Testy integracyjne** `CashFlowReportApiTests` (sekcja „Tests”, I-CF-*), wzór
  `SalesReportApiTests` (`OrdersTest.CreateAsync`, zegar i `StoneFlow:LaunchDate`, dane przez API
  modułów, prywatne pomocnicze `AddRefundAsync`/`CreateExpenseAsync`/`AddWithdrawalAsync` jak w
  `OperationsApiTests`). Dane fikcyjne.

### Authorization

`GET /api/reports/cash-flow`: uwierzytelnienie → polityka domyślna hosta (fallback, sesja); brak
zasobu per konto (jedna firma, PRD) — handler nie autoryzuje konkretnego obiektu; odczyt tylko
przez `ICashOperationSource`. `GET`, więc bez XSRF. Test: I-CF-11 (`401` bez sesji),
`EndpointSecurityTests` bez wpisu w `AllowedAnonymous`.

### Background jobs

None.

### Edge cases & null safety (backend)

| Przypadek | Gdzie | Test |
|---|---|---|
| Okres w całości przed uruchomieniem | B5 krok 2: bez źródeł, `Kpis = null`, puste listy; domena: zera i trzy pozycje (B2) | H-CF-3, CF-11, I-CF-8 |
| Baza sprzed uruchomienia (miesiąc uruchomienia) | B5 krok 4: brak odczytu bazy, `NoBasis` na trzech kartach | H-CF-4, I-CF-7 |
| Baza karty = 0, baza ujemna | `Comparison.Of` (bez zmian) | H-CF-5 |
| > 10 000 operacji w okresie albo w bazie | B3 (`null`), B5 kroki 3–4 → `422` | RD-2, H-CF-6, I-CF-9 |
| Dokładnie 10 000 | B3 (`Count > MaxOperations`) | RD-2 |
| Opłacony wydatek ujemny | `CashFlowTotals.Of` i suma w podziale bez `Math.Abs` | CF-4, I-CF-4 |
| Wpis spoza efektywnego zakresu (np. źródło zwróci datę spoza) | B2 filtr `Effective.Contains` | CF-12 |
| Mniej niż 10 operacji / brak operacji | B5 krok 8 (`Take`) — lista krótsza / pusta; karty `0.00`, podział z zerami, przedziały zerowe | H-CF-7, I-CF-10 |
| Remisy daty | `CashOperationReader.Order` (rodzaj, id) — ta sama co lista | RD-3, H-CF-7, I-CF-2 |
| `Comparison.Base/Difference/Percent` `null` przy `NoBasis` | mapowanie `SalesReportContracts.ToResponse(AmountKpiDto)` (bez zmian) | I-CF-7 |
| Pola `string?` pozycji (`Method`, `RefundKind`, `Description`, `Category`) | `OperationContracts.ToResponse` (bez zmian) | I-CF-2 |
| Parametry niepotrzebne dla `period` | `ReportPeriodInput` (ignorowane) | H-CF-1 |

### Risks (backend)

- Wydzielenie `CashOperationReader` dotyka zachowania `/api/operations` (FIN-05). Łagodzi: pętla
  przenoszona 1:1, `ListOperationsHandlerTests` i `OperationsApiTests` bez edycji, test RD-4
  porównujący kolejność readera z wynikiem `ListOperationsHandler` na tych samych danych.
- Okres i baza są czytane osobno (dwa odczyty pełnego zbioru rodzajów). Przy skali D-08 bez
  znaczenia; bezpiecznik 10 001 na zakres.

## Frontend Plan

### Step 0 — aplikacja

Jedna aplikacja: `app/` (Angular). Zmiany w `app/src/app/shared/dictionaries/`,
`app/src/app/features/dashboard/`, `app/src/app/features/operations/` i
`app/src/testing/dashboard-testing.ts`.

### Expected API contract (wyprowadzony ze spec i rozstrzygnięć)

| Method | Route | Request shape | Response shape | Used by |
|---|---|---|---|---|
| GET | `/api/reports/cash-flow` | `period`; `year` tylko przy `year`; `from`, `to` tylko przy `custom` (te same `periodParams` co raport sprzedaży) | `CashFlowReport { period, comparison, kpis: { inflows, outflows, change } \| null, series: [{ from, to, inflows, outflows, change }], outflowsByKind: [{ kind: 'expense'\|'withdrawal'\|'refund', amount }], recentOperations: OperationItem[], calculatedAtUtc }` | `ReportsApi.cashFlowReport` → `DashboardService.loadCashFlow` |
| GET | `/api/operations` | bez zmian; lista przyjmuje `kinds` z odczytanego `rodzaj` | bez zmian | `OperationsService` |

`period` i `comparison` mają kształt `ReportPeriod` / `ReportComparisonHeader` z SF-19, karta —
`AmountKpi`, pozycja — `OperationItem` (SF-17). `422` z `code = report_range_too_large`.

### Routing

| Path | Page | Loaded how | Guard |
|---|---|---|---|
| `/pulpit` | `DashboardPage` (bez zmian trasy) | `loadComponent` (istniejące) | istniejący guard sesji powłoki |
| `/operacje` | `OperationsListPage` (bez zmian trasy); parametry `od`, `do`, `rodzaj` (powtarzalny) czytane raz i usuwane z adresu | `loadChildren` (istniejące) | istniejący guard sesji |

Bez nowych tras. API egzekwuje dostęp (`401` → interceptor).

### Files

| File path | Action | Purpose |
|---|---|---|
| `app/src/app/shared/dictionaries/operations.ts` | create | typy, stałe i funkcje operacji pieniężnych (rozstrzygnięcie 1) |
| `app/src/app/shared/dictionaries/operations.spec.ts` | create | F-D1 |
| `app/src/app/features/operations/operation-models.ts` | modify | eksport dalej z `shared/dictionaries/operations` pod dotychczasowymi nazwami; zostają typy listy, `methodLabel`, `NO_METHOD`, `OPERATIONS_PROBLEM` |
| `app/src/app/features/operations/operations.service.ts` | modify | `filterFromDashboard(filter: OperationFilters): Promise<void>` |
| `app/src/app/features/operations/operations.service.spec.ts` | modify | F-O3 |
| `app/src/app/features/operations/operations-list-page.ts` | modify | eksportowana `dashboardFilterFromParams(params: ParamMap)`; konstruktor: snapshot → serwis → `replaceUrl` bez parametrów |
| `app/src/app/features/operations/operations-list-page.spec.ts` | modify | F-O1, F-O2 |
| `app/src/app/features/dashboard/report-models.ts` | modify | `ReportHeader`, `CashFlowKpis`, `CashFlowSeriesPoint`, `OutflowKind`, `OutflowKindAmount`, `CashFlowReport`, `OUTFLOW_KINDS`, `OUTFLOW_KIND_LABELS`, `CASH_FLOW_KPI_DESCRIPTIONS`, `OUTFLOWS_BREAKDOWN_LABEL`; `SalesReport extends ReportHeader` |
| `app/src/app/features/dashboard/reports-api.ts` | modify | `cashFlowReport(selection)` |
| `app/src/app/features/dashboard/reports-api.spec.ts` | modify | F-A1 |
| `app/src/app/features/dashboard/report-text.ts` | modify | `cashFlowBasisLabel(launchDate)` |
| `app/src/app/features/dashboard/report-text.spec.ts` | modify | F-T1 |
| `app/src/app/features/dashboard/dashboard.service.ts` | modify | stan przepływów, stan zależny od widoku (rozstrzygnięcie 3), `activeReport`, linki do „Operacji”; `ExpensesLink` → `ListLink` z tablicami w `queryParams` |
| `app/src/app/features/dashboard/dashboard.service.spec.ts` | modify | F-S1 … F-S6 |
| `app/src/app/features/dashboard/kpi-card.ts` | modify | `KpiIcon` + `'inflows' \| 'outflows' \| 'change'`; `KpiAction` z `ListLink` |
| `app/src/app/features/dashboard/kpi-card.html` | modify | trzy nowe ikony SVG (`aria-hidden` na kontenerze jak dotąd) |
| `app/src/app/features/dashboard/sales-view.ts` | modify | tylko typ `ListLink` zamiast `ExpensesLink` (zachowanie bez zmian) |
| `app/src/app/features/dashboard/cash-flow-view.ts` | create | `app-cash-flow-view` |
| `app/src/app/features/dashboard/cash-flow-view.html` | create | szablon widoku przepływów |
| `app/src/app/features/dashboard/cash-flow-view.spec.ts` | create | F-CF1 … F-CF8 (renderuje cały `DashboardPage` w widoku przepływów) |
| `app/src/app/features/dashboard/dashboard-page.ts` | modify | nagłówek z `activeReport`, etykieta podstawy wg widoku, import `CashFlowView`, bez `EmptyState`; doc komentarz |
| `app/src/app/features/dashboard/dashboard-page.html` | modify | wybór okresu i blok aktualności nad treścią obu widoków; `app-cash-flow-view` zamiast zaślepki |
| `app/src/app/features/dashboard/dashboard-page.spec.ts` | modify | F-P1 (zaślepka → widok, wspólny nagłówek); istniejące testy „Sprzedaży” bez zmian zachowania |
| `app/src/testing/dashboard-testing.ts` | modify | `cashFlowReport(overrides)`, `beforeLaunchCashFlowReport()`; `discardReportRequests` bez zmian (prefiks `/api/reports/`) |

Bez zmian: `shared/ui/*`, `shared/format/*`, `chart-canvas.ts`, `chart-factory.ts`,
`period-picker.*`, `sales-components.*`, `app/src/testing/operations-testing.ts` (import przez
re-eksport działa), `package.json`.

### Implementation steps

- **F1 `shared/dictionaries/operations.ts`** (F-D1 najpierw). Przeniesienie 1:1 z
  `operation-models.ts` (te same nazwy, typy i teksty): `OperationKind`, `OperationDirection`,
  `OperationRefundKind = RefundKind`, `OperationMethod = PaymentMethodCode`, `OperationSourceType`,
  `OperationCategory = ExpenseCategory`, `OperationItem`, `OPERATION_KINDS`, `KIND_FILTER_LABELS`,
  `kindLabel`, `DIRECTION_LABELS`, `NEGATIVE_AMOUNT_LABEL`, `isNegativeAmount`, `sourceLink`. Importy
  tylko z `shared/dictionaries/*` (bez `features/`). Komentarz pliku: słowniki operacji pieniężnych
  wspólne dla „Operacji” (SF-17) i Pulpitu (SF-20).
- **F2 `operation-models.ts`:** usunięcie przeniesionych definicji, `export { … } from '../../shared/dictionaries/operations';`
  oraz `export type { … }` dla typów — pod dotychczasowymi nazwami, więc `operations-list-page.ts`,
  `operations.service.ts`, `operations-api.ts`, specy i `testing/operations-testing.ts` kompilują się
  bez zmian. `methodLabel` importuje `PAYMENT_METHOD_LABELS` jak dotąd.
- **F3 Modele raportu** (`report-models.ts`):
  - `export interface ReportHeader { readonly period: ReportPeriod; readonly comparison: ReportComparisonHeader; readonly calculatedAtUtc: string; }`;
    `SalesReport extends ReportHeader` (pola bez zmian).
  - `CashFlowKpis { inflows: AmountKpi; outflows: AmountKpi; change: AmountKpi }`;
    `CashFlowSeriesPoint { from; to; inflows; outflows; change }` (ciągi);
    `OutflowKind = Exclude<OperationKind, 'payment'>` (typ z `shared/dictionaries/operations`);
    `OutflowKindAmount { kind: OutflowKind; amount: string }`;
    `CashFlowReport extends ReportHeader { kpis: CashFlowKpis | null; series; outflowsByKind; recentOperations: readonly OperationItem[] }`.
  - `OUTFLOW_KINDS: readonly OutflowKind[] = ['expense', 'withdrawal', 'refund']` (kolejność z API,
    tylko do testów i typów — UI renderuje kolejność odpowiedzi).
  - `OUTFLOW_KIND_LABELS`: `expense` „Opłacone wydatki”, `withdrawal` „Wypłaty właścicieli”, `refund` „Zwroty”.
  - `CASH_FLOW_KPI_DESCRIPTIONS`: teksty z tabeli spec UI pkt 3 (Wpływy, Wypływy, Zmiana środków) dosłownie.
  - `OUTFLOWS_BREAKDOWN_LABEL` = „Suma pozycji równa się wypływom. Ujemne opłacone wydatki pomniejszają swoją pozycję.”
- **F4 `ReportsApi.cashFlowReport(selection: PeriodSelection): Promise<CashFlowReport>`** —
  `GET /api/reports/cash-flow` z `periodParams(selection)` (ta sama prywatna funkcja). Doc komentarz
  klasy dopisuje SF-20.
- **F5 `report-text.ts`:** `cashFlowBasisLabel(launchDate)` = „Kwoty brutto w PLN według daty
  operacji pieniężnej: wpłaty, zwrotu, zapłaty wydatku albo wypłaty. Raporty obejmują dane od
  {formatDate(launchDate)}.” Pozostałe funkcje (`periodLabel`, `launchNotice`,
  `beforeLaunchMessage`, `comparisonDescription`, `baseLabel`, `amountComparisonText`,
  `bucketLabel`, `bucketRange`) używane bez zmian; komentarz pliku: teksty obu widoków.
- **F6 Linki do „Operacji”** (`dashboard.service.ts`):
  - typ `ListLink { readonly link: readonly string[]; readonly queryParams: Readonly<Record<string, string | readonly string[]>> }`
    zastępuje `ExpensesLink` (zmiana nazwy w `kpi-card.ts`, `sales-view.ts`; `costsLink` i
    `categoryLink` zwracają ten sam kształt, zachowanie bez zmian).
  - `cashFlowLinks = computed<CashFlowLinks | null>()` z `cashFlowState().period` (null przy braku
    raportu albo `beforeLaunch`): baza `{ od: period.effectiveFrom, do: period.to }`;
    `inflows` = baza + `rodzaj: 'payment'`; `outflows` = baza + `rodzaj: ['refund', 'expense', 'withdrawal']`
    (kolejność `OPERATION_KINDS`); `change` i `all` = sama baza. Wszystkie `link: ['/operacje']`.
  - `outflowKindLink(kind: OutflowKind): ListLink | null` = baza + `rodzaj: kind`.
- **F7 `DashboardService` — stan przepływów i stan zależny od widoku** (F-S* najpierw):
  - Nowe sygnały: `cashFlowState<CashFlowReport | null>`, `cashFlowLoadingState`,
    `cashFlowErrorState`, `cashFlowRangeError`, licznik `cashFlowSeq`. Publiczne: `cashFlowReport`,
    `cashFlowLoading`, `cashFlowError`. Stała `CASH_FLOW_ERROR_MESSAGE` = `REPORT_ERROR_MESSAGE`
    („Nie udało się wczytać raportu.”).
  - `rangeError = computed(() => view() === 'sales' ? reportRangeError() ?? componentsRangeError() : cashFlowRangeError())`.
  - `activeReport = computed<ReportHeader | null>()` — raport aktywnego widoku, `null` w trakcie jego
    wczytywania albo bez raportu (nagłówek pokazuje stan wczytywania).
  - `enter()` → `loadActive()`: „Sprzedaż” — `loadReport()` + `loadComponents()` (jak dotąd);
    „Przepływy” — `loadCashFlow()`.
  - `setView(view)`: ten sam widok → nic; inaczej `leave(previous)` (dla „Sprzedaży”: `++reportSeq`,
    `++componentsSeq`, oba `loading = false`, `reportError`, `reportRangeError`, `componentsError`,
    `componentsRangeError` = `null`; dla „Przepływów”: `++cashFlowSeq`, `cashFlowLoading = false`,
    `cashFlowError`, `cashFlowRangeError` = `null`), ustawienie widoku i `loadActive()`.
  - `setSelection(change)`: aktualizacja okresu, `resetComponents()` (zawsze), `loadActive()`.
  - `loadCashFlow()` — wzór `loadReport()`: `++cashFlowSeq`, wyczyszczenie błędów; przy
    `selectionInvalid()` `cashFlowState = null` bez żądania; inaczej `cashFlowLoading = true`, odpowiedź
    tylko przy aktualnym `seq` → stan i `launchDate.set(report.period.launchDate)`; błąd: `422
    report_range_too_large` → `cashFlowRangeError` (`detail` albo `RANGE_TOO_LARGE_MESSAGE`), inny →
    `cashFlowError`; `finally` zdejmuje `loading` przy aktualnym `seq`.
  - `loadReport()` bez zmian poza tym, że `launchDate` ustawia się jak dotąd (wspólny sygnał).
  - Doc komentarz klasy: SF-20, stan obu widoków, wczytywany tylko aktywny widok, bez odpytywania w tle.
- **F8 `OperationsService.filterFromDashboard(filter: OperationFilters)`** (wzór
  `ExpensesService.filterByCostPeriod`): `filtersState.set({ from, to, kinds: OPERATION_KINDS.filter(k => filter.kinds.includes(k)) })`,
  `sortState.set(DEFAULT_SORT)`, `pageState.set(1)`, `return this.load()`.
- **F9 `dashboardFilterFromParams(params: ParamMap): OperationFilters | null`** (eksport z
  `operations-list-page.ts`, obok strony jak `costPeriodFromParams`):
  - `null`, gdy w adresie nie ma żadnego z `od`, `do`, `rodzaj`;
  - daty: obie poprawne (`/^\d{4}-\d{2}-\d{2}$/` i `formatDate(value) !== NO_DATA`, jak `validDate`
    w `expenses-list-page.ts`) i `od` ≤ `do` → `{ from: od, to: do }`; inaczej obie z `currentMonth()`
    (`operations.service.ts`) — rozstrzygnięcie niejasności 1;
  - rodzaje: `OPERATION_KINDS.filter(kind => params.getAll('rodzaj').includes(kind))` (nieznane
    pominięte, puste → wszystkie rodzaje).
- **F10 `OperationsListPage` konstruktor:** `const route = inject(ActivatedRoute); const filter = dashboardFilterFromParams(route.snapshot.queryParamMap);`
  przy filtrze `void this.operations.filterFromDashboard(filter)` i
  `void this.router.navigate([], { relativeTo: route, queryParams: {}, replaceUrl: true })`
  (rozstrzygnięcie 4); bez filtra `void this.operations.load()` jak dotąd. Doc komentarz strony:
  parametry przejścia z Pulpitu (SF-20).
- **F11 `KpiCard`:** `KpiIcon` += `'inflows' | 'outflows' | 'change'`; w `kpi-card.html` trzy
  `@case` z prostymi ścieżkami SVG (np. strzałka do środka / na zewnątrz / dwie strzałki), w
  istniejącym kontenerze z `aria-hidden="true"`, kolor `text-(--sf-accent-text)` jak dotąd. Szablon
  linku (`[routerLink]` + `[queryParams]`) bez zmian — tablice w `queryParams` Angular zapisuje jako
  powtórzony parametr.
- **F12 `CashFlowView`** (`cash-flow-view.ts`/`.html`, selektor `app-cash-flow-view`, `host: { class: 'flex flex-col gap-6' }`),
  wzór `SalesView`; importy: `ChartCanvas`, `DataTable`, `TableCell`, `ErrorState`, `LoadingState`,
  `KpiCard`, `RouterLink`; słowniki z `../../shared/dictionaries/operations`. Computed:
  - `report = dashboard.cashFlowReport`; `beforeLaunch` → `beforeLaunchMessage(period)`;
    `periodText` → `periodLabel`.
  - `cards`: trzy karty — `inflows` „Wpływy” / hint „Pokaż wpłaty”, `outflows` „Wypływy” / „Pokaż
    wypływy”, `change` „Zmiana środków” / „Pokaż operacje”; wartość `formatPln(kpi.value)`, opis
    `CASH_FLOW_KPI_DESCRIPTIONS`, porównanie `amountComparisonText(kpi.comparison, baseLabel(period, comparison))`,
    akcja `{ kind: 'link', target: cashFlowLinks()[key] }`.
  - `timeline: ChartSpec` — `type: 'timeline'`, etykiety `bucketLabel(point, granularity)`, serie
    „Wpływy” (`--sf-accent`, `bar`) i „Wypływy” (`--sf-text`, `bar`), `ariaLabel` „Wpływy i wypływy w
    czasie, {okres}, w zł. Dane w tabeli poniżej.”
  - `outflowsChart: ChartSpec | null` — `horizontalBar`, etykiety `OUTFLOW_KIND_LABELS`, seria
    „Wypływy” (`--sf-text`), `ariaLabel` „Wypływy według rodzaju w zł. Wartości na liście obok.”
  - `columns: DataTableColumn<OperationItem>[]`: Data (`formatDate`), Rodzaj (`kindLabel`), Opis
    (szablon `appTableCell="description"`: link `sourceLink(row)` z tekstem `sourceLabel`), Kierunek
    (`DIRECTION_LABELS`), Kwota (`align: 'end'`, szablon: `formatPln(row.amount)` + przy
    `isNegativeAmount` dopisek `NEGATIVE_AMOUNT_LABEL`) — jak na liście „Operacje”, bez „Metody”;
    `rowKey = row => \`${row.kind}:${row.id}\``.
  - Szablon (kolejność): `@if cashFlowLoading` → `app-loading-state` („Wczytywanie raportu…”) ·
    `@else if cashFlowError` → `app-error-state` (`retry` → `dashboard.loadCashFlow()`) ·
    `@else if beforeLaunch` → `<p role="status">` · `@else if report()?.kpis`:
    1. karty `grid gap-4 md:grid-cols-2 xl:grid-cols-3`, trzecia karta `md:col-span-2 xl:col-span-1`
       (`data-testid="karty"`);
    2. wiersz wykresów `grid gap-4 xl:grid-cols-[2fr_1fr]`: sekcja „Wpływy i wypływy w czasie”
       (`h2`, podtytuł `{okres}, kwoty w zł`, `app-chart-canvas`, przycisk „Pokaż dane w tabeli” /
       „Ukryj dane w tabeli” z `aria-expanded`/`aria-controls="pulpit-przeplywy-dane-wykresu"`, tabela
       Okres / Wpływy / Wypływy / Zmiana środków z `tfoot` „Razem” = `kpis.*.value`,
       `data-testid="razem"`); sekcja „Wypływy według rodzaju” (`h2`, wykres, zawsze widoczna lista
       `data-testid="wyplywy-rodzaje"` z linkami `outflowKindLink(kind)` i kwotami `formatPln`, pod nią
       `OUTFLOWS_BREAKDOWN_LABEL`);
    3. sekcja „Ostatnie operacje” (`h2 id`), `app-data-table` z `caption="Ostatnie operacje"`,
       `[rows]="report.recentOperations"`, `[total]="report.recentOperations.length"`, `[page]="1"`,
       `[pageSize]="10"`, `[mobileCards]="true"`, `emptyTitle="Brak operacji w wybranym okresie"`;
       pod tabelą link „Pokaż wszystkie operacje” (`cashFlowLinks().all`).
  - Żadnej arytmetyki: „Razem” to wartości kart, kwoty to ciągi z API przez `formatPln`.
- **F13 `DashboardPage`:**
  - `header = computed(...)` czyta `dashboard.activeReport()` (null → brak tekstów, jak dziś przy
    wczytywaniu); `basis` = `view() === 'sales' ? basisLabel(launch) : cashFlowBasisLabel(launch)`;
    pozostałe teksty bez zmian.
  - `dashboard-page.html`: po przełączniku `app-period-picker` i blok `data-testid="aktualnosc"`
    (z `app-incomplete-data-notice`) **poza** `@if` widoku; potem `@if (view === 'sales') <app-sales-view /> @else <app-cash-flow-view />`.
    Zaślepka `app-empty-state` i import `EmptyState` usunięte. Import `IncompleteDataNotice` bez
    zmian (otwarty punkt).
  - Doc komentarz: SF-20 — oba widoki, wspólny wybór okresu i nagłówek.
- **F14 Fikstury testów** (`app/src/testing/dashboard-testing.ts`): `cashFlowReport(overrides)` z
  fikcyjnymi kwotami (np. wpływy `1500.00`, wypływy `700.00`, zmiana `800.00`, podział
  `expense 300.00`, `withdrawal 250.00`, `refund 150.00`, dwie pozycje `recentOperations` przez
  typ `OperationItem` z `shared/dictionaries/operations`, w tym opłacony wydatek `-50.00`) i
  `beforeLaunchCashFlowReport()`.

### State

- Lokalny w komponencie: `tableOpen` (tabela danych wykresu) w `CashFlowView`.
- W serwisie (`DashboardService`, `providedIn: 'root'`): widok, wybór okresu, raport sprzedaży,
  zestawienie, raport przepływów, flagi wczytywania i błędy per widok, liczniki żądań per widok,
  `launchDate`. Przetrwa przejście do „Operacji” / źródła i powrót.
- W serwisie (`OperationsService`): filtry z przejścia, sortowanie, strona — jak dotąd.
- Stan serwera: raporty z API, bez pamięci podręcznej; każde `enter()`, `setView`, `setSelection`
  wczytuje aktywny widok.
- Pochodne: `rangeError`, `activeReport`, `cashFlowLinks`, `yearOptions`, `header` (strona), karty i
  specyfikacje wykresów (widok) — tylko na danych z API, bez liczenia kwot.

### Styling

- Tokeny: `--sf-accent` (seria „Wpływy”), `--sf-text` (seria „Wypływy”, podział), `--sf-secondary`
  (obramowania sekcji), `--sf-muted` (podpisy), `--sf-info` (komunikat przed uruchomieniem),
  `--sf-accent-text` (ikony, wskazówki); `lint:colors` bez wyjątków.
- Reużyte: `app-kpi-card`, `app-chart-canvas`, `app-data-table`, `app-loading-state`,
  `app-error-state`, `app-period-picker`, `app-incomplete-data-notice`.
- Główna akcja: widok raportowy nie ma przycisku `primary`; działaniami są linki kart (jak „Sprzedaż”).
- Responsywność: karty 1 / 2 (trzecia na pełną szerokość) / 3 kolumny (`md`, `xl`); wykresy obok
  siebie od `xl`, wyżej jeden pod drugim; tabela ostatnich operacji jako karty poniżej `md`
  (`mobileCards`); kwoty `tabular-nums`, `whitespace-nowrap` w tabeli wykresu, bez ucinania.
- Animacje wyłączone przy `prefers-reduced-motion` (już w `ChartCanvas`).

### Accessibility

- Kolejność fokusu: przełącznik widoku → wybór okresu → linki aktualności → karty (linki) → canvas
  wykresu w czasie (`tabindex="0"`, strzałki, Home/End, Escape, `aria-live`) → przycisk tabeli →
  canvas podziału → linki podziału → tabela ostatnich operacji (linki źródeł) → „Pokaż wszystkie
  operacje”.
- `role="img"` + `aria-label` canvasów (`ChartCanvas`), `aria-expanded`/`aria-controls` przycisku
  tabeli, `caption` tabel, nagłówki `h2` sekcji, ikony kart `aria-hidden`.
- Komunikat przed uruchomieniem `role="status"`; `422` w wyborze okresu (`role="alert"` w
  `PeriodPicker`, bez zmian). Przełączenie widoku nie przenosi fokusu (zostaje na radiu); stan
  wczytywania ogłasza `app-loading-state`.

### Edge cases (frontend)

| Przypadek | Gdzie | Test |
|---|---|---|
| Wczytywanie aktywnego widoku | nagłówek pusty (`activeReport = null`), treść `app-loading-state`, bez zer | F-CF1, F-P1 |
| Błąd raportu przepływów | `app-error-state`, ponowienie `loadCashFlow` | F-CF1 |
| `422` | komunikat przy wyborze okresu tylko aktywnego widoku | F-S2, F-CF1 |
| `beforeLaunch` | komunikat bez kart, wykresów i tabeli; brak linków | F-CF6 |
| Brak operacji po uruchomieniu | karty `0,00 zł`, podział z zerami, tabela ze stanem pustym | F-CF5 |
| Opłacony wydatek ujemny w ostatnich operacjach | `-50,00 zł` + „Korekta / zwrot od dostawcy” | F-CF5 |
| Szybkie przełączanie widoków / okresów | osobne liczniki, unieważnienie przy opuszczeniu | F-S3 |
| `/operacje` z niepoprawną datą / `od` > `do` / samym `rodzaj` / `rodzaj=xyz` | F9 | F-O1 |
| Powrót „Wstecz” po zmianie filtra | parametry usunięte (`replaceUrl`) | F-O2, scenariusz ręczny |
| Wejście na `/operacje` bez parametrów | `load()` z zachowanym stanem | F-O2 |

### Risks (frontend)

- Zmiana `DashboardService` dotyczy też widoku „Sprzedaż” (`setView` teraz wczytuje dane, nagłówek
  przez `activeReport`). Łagodzą istniejące testy `dashboard.service.spec.ts` i
  `dashboard-page.spec.ts` (muszą przejść; zmiany tylko tam, gdzie test opisywał zaślepkę albo
  brak odczytu przy przełączeniu).
- Pasek stronicowania `app-data-table` przy „Ostatnich operacjach” jest widoczny z nieaktywnymi
  przyciskami (rozstrzygnięcie niejasności 4); `shared/ui` bez zmian.

## API Contract

| Method | Route | Request | Response |
|---|---|---|---|
| GET | `/api/reports/cash-flow` | `period` (`currentMonth` domyślnie \| `previousMonth` \| `year` \| `custom`); `year` (`yyyy`, przy `year`, domyślnie bieżący, ≤ bieżący); `from`, `to` (`yyyy-MM-dd`, wymagane przy `custom`, `from ≤ to ≤ dziś + 1 rok`); parametry niepotrzebne dla `period` ignorowane | `200 CashFlowReportResponse`; `400` (klucze `period`, `year`, `from`, `to`, te same komunikaty co `/api/reports/sales`); `422` `code = report_range_too_large`, `detail` „Za dużo danych w wybranym okresie. Wybierz krótszy okres.” |
| GET | `/api/operations` | bez zmian (`from`, `to`, powtarzalny `kind`, `direction`, `page`, `pageSize`) | bez zmian |

```
CashFlowReportResponse {
  period:     { kind, from, to, effectiveFrom, launchDate, beforeLaunch, inProgress, granularity },   // SalesReportPeriodResponse
  comparison: { from, to, available, reason: "beforeLaunch" | null },                                 // SalesReportComparisonResponse
  kpis: null | {                                                                                      // null przy beforeLaunch
    inflows:  { value: "1500.00", comparison: AmountComparison },
    outflows: { value, comparison: AmountComparison },
    change:   { value, comparison: AmountComparison }
  },
  series:           [ { from, to, inflows, outflows, change } ],            // [] przy beforeLaunch
  outflowsByKind:   [ { kind: "expense"|"withdrawal"|"refund", amount } ],  // zawsze 3 w tej kolejności; [] przy beforeLaunch
  recentOperations: [ OperationItemResponse ],                              // ≤ 10, kolejność listy „Operacje”; [] przy beforeLaunch
  calculatedAtUtc:  "2026-11-10T10:15:00+00:00"
}
AmountComparison      { status: "available"|"noBasis", base: string|null, difference: string|null, percent: string|null }
OperationItemResponse { kind, id, date, direction: "inflow"|"outflow", amount /* zapisana */, method, refundKind, description, category, sourceType: "order"|"expense"|"withdrawal", sourceId, sourceLabel }
```

- Kwoty jako tekst z kropką i opcjonalnym minusem; `percent` z jednym miejscem po kropce; daty
  `yyyy-MM-dd`; kody rodzajów z `OperationCodes`. Brak pola salda.
- Sesja wymagana; `GET`, więc bez XSRF.

## Integration Check

| Frontend expects | Backend provides | Gap |
|---|---|---|
| `GET /api/reports/cash-flow` z `periodParams` | B7 `[FromQuery] period, year, from, to` → `ReportPeriodInput` | — |
| `period` jak `ReportPeriod` (SF-19) | B6 `SalesReportContracts.ToResponse(ReportPeriod)` | — |
| `comparison` jak `ReportComparisonHeader` | B6 `SalesReportContracts.ComparisonOf` | — |
| `kpis.inflows/outflows/change` jako `AmountKpi` | B5 krok 6, B6 `ToResponse(AmountKpiDto)` | — |
| `kpis = null`, puste listy przy `beforeLaunch` | B5 krok 2 | — |
| `series[*].inflows/outflows/change` | B5 krok 7, B6 | — |
| `outflowsByKind` 3 pozycje, kody `expense`/`withdrawal`/`refund` | B1, B2, B6 `OperationCodes.Of` | — |
| `recentOperations` jako `OperationItem` (≤ 10) | B5 krok 8, B6 `OperationContracts.ToResponse(OperationItemDto)` | — |
| `period.effectiveFrom`/`to` do linków `od`/`do` | B6 | — |
| `period.launchDate` do `yearOptions` i etykiety podstawy | B6 | — |
| `422` `code = report_range_too_large` | B7 `ReportingProblems.ReportRangeTooLarge()` | — |
| Lista „Operacje” z kodami `payment`/`refund`/`expense`/`withdrawal` z `rodzaj` | `ListOperationsQuery.Kind` (bez zmian, `OperationCodes.TryParse`) | — |
| Sumy listy = karty przy [`effectiveFrom`, `to`] | te same źródła, `CashFlowTotals`, `LaunchCutoff` (B3, B4, B5) | — |
| sesja | fallback policy hosta | — |

No integration gaps.

## Invariant Check

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sales separate from cash | Raport czyta wyłącznie `ICashOperationSource` (wpłaty, zwroty, opłacone wydatki, wypłaty według dat operacji); bez wartości zamówień, korekt i nieopłaconych wydatków; raport sprzedaży bez zmian | CF-1, CF-2, CF-7; I-CF-3 (nieopłacone zamówienie → wpływy 0; wpłata 300 w kolejnym miesiącu → wpływy 300, sprzedaż SF-19 bez zmian), I-CF-5 (wydatek bez daty zapłaty), I-CF-6 (korekta sprzedaży pominięta) |
| 2. Backend decimal money | `decimal` w `CashFlowSummary`, `CashFlowTotals`, `Comparison` (jedyne zaokrąglenie); UI formatuje ciągi, `chartNumber` tylko w adapterze wykresu, „Razem” z kart | CF-9, CF-10, H-CF-5, I-CF-2; F-CF3 (Razem = karty), F-S6 (wartości przepisane z API) |
| 3. API-enforced access | fallback policy, brak `AllowAnonymous` | I-CF-11 (`401`), `EndpointSecurityTests` |
| 4. Retries never duplicate | n/d (odczyty) | n/d |
| 5. Financial audit trail | n/d (bez zmian danych) | n/d |
| 6. Secrets stay server-side | brak sekretów i zależności | przegląd |
| 7. Module and layer boundaries | odczyt przez `ICashOperationSource` (`Shared.Application`); reguły w `Reporting.Domain` (tylko Kernel); `CashOperationReader` w `Reporting.Application`; frontend: `features/dashboard` importuje `shared/dictionaries/operations`, nie `features/operations` | `StoneFlow.Architecture.Tests` bez nowych wyjątków; przegląd importów (`grep "features/operations" app/src/app/features/dashboard` → brak); F-D1 |
| 8. No identifying data in docs | fikcyjne dane testowe i przykłady; ostatnie operacje pokazują te same pola co lista | przegląd |

Brak blokerów. Istniejący import `features/integration` w `dashboard-page.ts` (SF-19) → otwarty
punkt w `06`.

### Wpływ na testy architektury

Bez zmian w `StoneFlow.Architecture.Tests`: `CashFlowSummary` używa `Shared.Kernel` i własnej
domeny; `CashOperationReader` używa `Shared.Application.Operations`; brak nowych referencji
projektów i encji.

## Unified Implementation Order

1. **Domena Reporting (TDD):** CP-1 → `CashFlowPolicy.OutflowKinds` (B1); CF-1 … CF-12 →
   `CashFlowSummary` (B2).
2. **Aplikacja — wspólna operacja (TDD):** RD-1 … RD-4 → `CashOperationReader` (B3); przepięcie
   `ListOperationsHandler` (B4); `ListOperationsHandlerTests` zielone bez edycji.
3. **Aplikacja — raport (TDD):** H-CF-1 … H-CF-9 → `GetCashFlowReport` (B5).
4. **Api:** widoczność mapowań i `ComparisonOf` (B6), `CashFlowReportContracts`,
   `CashFlowReportEndpoints`, `ReportingModule` (B6, B7).
5. **Testy integracyjne:** I-CF-1 … I-CF-11; regresja `OperationsApiTests`, `SalesReportApiTests`,
   `EndpointSecurityTests`, `OpenApiTests` (B8).
6. **Frontend — słowniki:** `shared/dictionaries/operations.ts` + spec, re-eksport w
   `operation-models.ts` (F1, F2); `npm run typecheck`.
7. **Frontend — operacje:** `filterFromDashboard` + spec (F8), `dashboardFilterFromParams` i
   konstruktor strony + spec (F9, F10).
8. **Frontend — fundamenty Pulpitu:** modele (F3), `ReportsApi` + spec (F4), `report-text` + spec
   (F5), `DashboardService` + spec (F6, F7), fikstury (F14).
9. **Frontend — komponenty:** `KpiCard` (F11), `CashFlowView` (F12), `DashboardPage` (F13) + specy
   (`cash-flow-view.spec.ts`, `dashboard-page.spec.ts`).
10. **Weryfikacja** (sekcja „Verification”), sprawdzenie z działającym API (sumy kart = lista
    „Operacje”), scenariusze ręczne i otwarte punkty do `06-implementation-done.md`.

## Tests

| Test | Type | What it proves |
|---|---|---|
| CP-1 `CashFlowPolicyTests` — `OutflowKinds` | unit (Reporting.Tests) | kolejność `PaidExpense`, `OwnerWithdrawal`, `Refund`; każda pozycja `Outflow`; zbiór = wszystkie rodzaje z kierunkiem `Outflow`; dotychczasowe przypadki bez zmian |
| CF-1 `CashFlowSummaryTests` — niezapłacone zamówienie | unit | brak wpisów → wpływy 0, wypływy 0, zmiana 0, podział trzy zera, przedziały zerowe |
| CF-2 wpłata w innym miesiącu | unit | wpłata 300 z 2026-12-05 przy okresie grudnia → wpływy 300 w przedziale 5.12; w okresie listopada → 0 |
| CF-3 zwrot sprzedaży i zwrot nadpłaty | unit | oba (`Refund`) w pozycji „Zwroty” i we wypływach |
| CF-4 korekta od dostawcy | unit | opłacone wydatki −200 i 500 → pozycja `PaidExpense` 300, wypływy 300 |
| CF-5 anulowanie opłaconego zamówienia ze zwrotem | unit | wpłata i zwrot zostają (wpływy i wypływy) |
| CF-6 przełom uruchomienia | unit | wpisy z dnia uruchomienia i po nim liczone, z dnia przed — pominięte (okres przycięty) |
| CF-7 wydatek planowany | unit | brak wpisu → brak wypływu (dokumentuje regułę źródła) |
| CF-8 wypłata | unit | tylko wypływ i pozycja `OwnerWithdrawal`; wpływy bez zmian |
| CF-9 przedziały = sumy okresu | unit | dla dni (miesiąc) i miesięcy (rok, przycięte przedziały): suma `Buckets` = `Totals` w każdej z trzech wartości; liczba przedziałów = `period.Buckets().Count` |
| CF-10 podział = wypływy | unit | suma `OutflowsByKind` = `Totals.Outflows`; zawsze trzy pozycje w kolejności `OutflowKinds`; brak rodzaju → `0` |
| CF-11 `BeforeLaunch` | unit | `Totals` 0/0/0, trzy pozycje 0, `Buckets` puste, wpisy zignorowane (rozstrzygnięcie 6) |
| CF-12 wpisy spoza zakresu | unit | wpis przed efektywnym początkiem i po `to` pominięty; kwoty bez zaokrąglania (`0.01`) |
| RD-1 `CashOperationReaderTests` — źródła i rodzaje | unit (fałszywe źródła) | źródła wołane po kolei; źródło bez żądanych rodzajów pominięte; każde dostaje tylko swoje rodzaje i zakres |
| RD-2 bezpiecznik | unit | limit malejący `10 001 − wczytane`; dokładnie 10 000 → lista; 10 001 → `null`; przy wyczerpaniu kolejne źródło nie jest wołane |
| RD-3 kolejność | unit | data malejąco / rosnąco, remis `Payment` < `Refund` < `PaidExpense` < `OwnerWithdrawal`, potem `Id` |
| RD-4 zgodność z listą | unit | na tych samych danych z remisami `Order(..., true).Take(10)` = `items` z `ListOperationsHandler` (`pageSize=10`) — FIN-05 ostatnich operacji |
| H-CF-1 `GetCashFlowReportHandlerTests` — walidacja | unit (fałszywe źródła, `FixedLaunchDate`, `ReportFacts.Clock`) | nieznany `period`; `year` 3 cyfry i > bieżący; `custom` bez `to`; `from > to` przy `from`; `to` > dziś + 1 rok przy `to`; parametry spoza okresu ignorowane; źródła nie są wołane przy błędzie |
| H-CF-2 odczyty | unit | dostępna baza → okres, potem baza (zakresy `EffectiveFrom`–`To` i `Base`), wszystkie rodzaje |
| H-CF-3 przed uruchomieniem | unit | brak wywołań źródeł; `Kpis = null`; `Series`, `OutflowsByKind`, `RecentOperations` puste; `Period.BeforeLaunch` |
| H-CF-4 baza sprzed uruchomienia | unit | tylko odczyt okresu; `ComparisonAvailable = false`; trzy karty `NoBasis` |
| H-CF-5 porównanie per karta | unit | baza wpływów 0 → `NoBasis` tylko „Wpływy”; baza zmiany ujemna (−500, bieżąca −250) → +50,0 (dzielnik \|baza\|); różnice na `decimal` |
| H-CF-6 bezpiecznik | unit | 10 001 operacji w okresie → `TooManyRows`; w bazie → `TooManyRows`; 10 000 → raport |
| H-CF-7 ostatnie operacje | unit | 12 operacji → 10; kolejność z remisami identyczna z `ListOperationsHandler` (te same dane); kierunek z `CashFlowPolicy`; < 10 → wszystkie; brak → pusto przy kartach `0` |
| H-CF-8 zegar | unit | `CalculatedAtUtc` z `TimeProvider`; „dziś” w Europe/Warsaw (23:30 UTC) dla bieżącego miesiąca |
| H-CF-9 przedziały i podział | unit | `Series` = przedziały okresu z sumami; suma `Series` = karty; `OutflowsByKind` = wypływy |
| I-CF-1 `CashFlowReportApiTests` — kształt | integration (SQL, LocalDB/kontener) | pola `period`, `comparison`, `kpis` (trzy karty z `comparison`), `series`, `outflowsByKind` (3 w kolejności), `recentOperations`, `calculatedAtUtc`; brak pola salda (żadna nazwa właściwości nie zawiera `balance`/`saldo`) |
| I-CF-2 uzgodnienie z „Operacjami” (kryterium 2) | integration | dla bieżącego miesiąca (zegar 5.11, launch 3.11 — okres przecina uruchomienie), roku (zegar 10.12, launch 1.09) i własnego zakresu przecinającego uruchomienie: `kpis` = `totals` z `/api/operations?from={effectiveFrom}&to={to}`; Wpływy = `inflows` z `&kind=payment`; Wypływy = `outflows` z `&kind=refund&kind=expense&kind=withdrawal`; każda pozycja podziału = `outflows` z jej `kind`; `recentOperations` = `items` z `&pageSize=10` (pełne obiekty, z remisami dat) |
| I-CF-3 sprzedaż a pieniądze (kryterium 1) | integration | zamówienie 1000 zł bez wpłat → wpływy `0.00`; wpłata 300 zł w kolejnym miesiącu → wpływy tego miesiąca `300.00`; `/api/reports/sales` → sprzedaż 1000 w miesiącu zamówienia i 0 w miesiącu wpłaty |
| I-CF-4 wydatek ujemny (kryterium 4) | integration | opłacone −200 i 500 → `outflowsByKind.expense = "300.00"`, wypływy o 300; w ostatnich operacjach `amount = "-200.00"` |
| I-CF-5 wydatek bez daty zapłaty (kryterium 3) | integration | nieopłacony (także wystąpienie cykliczne, jeśli tworzone przez API) → wypływy bez zmian; `mark-paid` → wchodzi w okresie daty zapłaty; `unmark-paid` → wypada |
| I-CF-6 korekta sprzedaży | integration | korekta nie zmienia kart i nie ma jej w `recentOperations` |
| I-CF-7 przełom uruchomienia (kryterium 5) | integration | wpłata z dnia uruchomienia do zamówienia sprzed uruchomienia → wpływy; wpłata z dnia przed → poza raportem; zwrot sprzedaży i zwrot pieniędzy po uruchomieniu → „Zwroty” i wypływy; `effectiveFrom` = data uruchomienia; `comparison.available = false`, karty `noBasis` |
| I-CF-8 okres przed uruchomieniem | integration | `custom` w październiku 2026 przy launch 3.11 → `period.beforeLaunch = true`, `kpis = null`, listy puste |
| I-CF-9 bezpiecznik i walidacja | integration | 10 001 wypłat wstawionych SQL-em (`INSERT … SELECT TOP`) → `422 report_range_too_large` z `detail`; `from > to` → `400` przy `from`; `year=2027` przy zegarze 2026 → `400` |
| I-CF-10 wypłata i brak operacji | integration | wypłata → tylko wypływy i „Wypłaty właścicieli”, raport sprzedaży bez zmian; okres bez operacji → karty `0.00`, trzy zera, `recentOperations = []` |
| I-CF-11 dostęp | integration | `401` bez sesji |
| Regresja | integration / arch | `OperationsApiTests`, `SalesReportApiTests`, `EndpointSecurityTests`, `OpenApiTests`, `StoneFlow.Architecture.Tests`, `ListOperationsHandlerTests`, `GetSalesReportHandlerTests` — bez edycji |
| F-D1 `shared/dictionaries/operations.spec.ts` | unit (Vitest) | `kindLabel` (zwrot pokazuje swój rodzaj, inne — etykieta filtra), `DIRECTION_LABELS`, `isNegativeAmount` (`-50.00`, `0.00`, `null`), `sourceLink` (trzy trasy), `OPERATION_KINDS` w stałej kolejności; `operation-models` eksportuje te same obiekty (`toBe`) |
| F-A1 `reports-api.spec.ts` | unit | `cashFlowReport` → `GET /api/reports/cash-flow`, parametry okresu tylko potrzebne dla rodzaju |
| F-T1 `report-text.spec.ts` | unit | `cashFlowBasisLabel` z datą uruchomienia; brak słów „zysk”/„saldo” |
| F-S1 `dashboard.service.spec.ts` — wczytywanie | unit | `enter()` w „Przepływach” → tylko raport przepływów; `setView('cashFlow')` → raport przepływów, `setView('sales')` → raport i zestawienie; `setSelection` w „Przepływach” → tylko przepływy, zestawienie sprzedaży zresetowane (strona 1, bez filtrów, data malejąco) i wczytane po przełączeniu; ten sam widok → brak żądania |
| F-S2 błędy per widok (uwaga 3) | unit | `422` w „Sprzedaży” → przełączenie → `rangeError` `null` w „Przepływach”; `422` w „Przepływach” → przełączenie → `null` w „Sprzedaży”; błąd raportu przepływów osobno od błędów sprzedaży |
| F-S3 liczniki żądań | unit | stara odpowiedź przepływów (dwie zmiany okresu) ignorowana; odpowiedź raportu sprzedaży po przełączeniu na „Przepływy” nie zmienia stanu ani `loading`; szybkie A→B→A |
| F-S4 linki | unit | `cashFlowLinks`: `od = effectiveFrom`, `do = to`; Wpływy `rodzaj: 'payment'`; Wypływy `rodzaj: ['refund','expense','withdrawal']`; Zmiana i „wszystkie” bez `rodzaj`; `outflowKindLink`; `null` przy `beforeLaunch` i bez raportu; `costsLink`/`categoryLink` bez zmian |
| F-S5 `launchDate` | unit | `yearOptions` od roku `launchDate` raportu przepływów (bez wczytanej sprzedaży); ostatnio wczytany raport wygrywa |
| F-S6 brak arytmetyki | unit | `cashFlowReport()` zawiera ciągi dokładnie z API |
| F-O1 `operations-list-page.spec.ts` — parametry | component | `?od=2026-11-01&do=2026-11-15&rodzaj=refund&rodzaj=expense` → żądanie `from`/`to`/`kind` powtórzone w kolejności `OPERATION_KINDS`, sort `desc`, strona 1, chipy aktywnych filtrów; `od=2026-11-31` → bieżący miesiąc (obie daty); `od` > `do` → bieżący miesiąc; poprawne `od` bez `do` → bieżący miesiąc; sam `rodzaj=payment` → bieżący miesiąc z `payment`; `rodzaj=xyz` → wszystkie rodzaje |
| F-O2 adres i powrót | component | po odczycie parametrów `Router.navigate` z `queryParams: {}` i `replaceUrl: true`; ponowne utworzenie strony bez parametrów → `load()` z zachowanym (zmienionym) filtrem |
| F-O3 `operations.service.spec.ts` | unit | `filterFromDashboard` ustawia filtr, sort domyślny, stronę 1 i wczytuje; sumy z API bez zmian |
| F-CF1 `cash-flow-view.spec.ts` — stany | component (`DashboardPage`, fałszywe `CHART_FACTORY`, `IntegrationStatusStub`) | wczytywanie bez zer i bez nagłówka; błąd z „Spróbuj ponownie” (ponowne żądanie `/api/reports/cash-flow`); `422` przy wyborze okresu |
| F-CF2 karty | component | trzy karty z wartościami, opisami, porównaniem (także „brak podstawy porównania”), wskazówkami „Pokaż wpłaty”/„Pokaż wypływy”/„Pokaż operacje”; linki z `od`, `do` i powtórzonym `rodzaj` (`href`) |
| F-CF3 wykres i tabela | component | spec wykresu z dwiema seriami (tokeny, kolejność), `aria-label` z okresem i wskazaniem tabeli; przycisk z `aria-expanded`; tabela Okres/Wpływy/Wypływy/Zmiana środków; „Razem” = wartości kart (nie suma) |
| F-CF4 podział | component | lista trzech pozycji z kwotami i linkami `rodzaj=expense`/`withdrawal`/`refund`; etykieta sumy pozycji; ujemna pozycja z minusem |
| F-CF5 ostatnie operacje | component | kolumny Data/Rodzaj/Opis/Kierunek/Kwota (bez „Metody”); link opisu do źródła; kierunek tekstem; kwota ujemna z dopiskiem; stan pusty „Brak operacji w wybranym okresie”; link „Pokaż wszystkie operacje” z `od`/`do` bez `rodzaj` |
| F-CF6 przed uruchomieniem | component | komunikat `beforeLaunchMessage` zamiast kart, wykresów i tabeli; brak linków do „Operacji” |
| F-CF7 „zysk” i „saldo” (uwaga 5) | component | cały `DashboardPage` w widoku przepływów (nagłówek, etykieta podstawy, karty, `aria-label` canvasów, lista, tabela; także po otwarciu tabeli danych) — `textContent` i atrybuty `aria-label`/`title` bez `zysk` i `saldo` (bez rozróżniania wielkości liter) |
| F-CF8 wartości tekstowe (kryterium 7) | component | karty jako tekst, tabela danych wykresu, lista podziału, tabela ostatnich operacji; canvas z `role="img"`, `tabindex="0"`, `aria-label` |
| F-P1 `dashboard-page.spec.ts` — wspólny nagłówek | component | wybór okresu i blok aktualności widoczne w obu widokach; przełączenie zmienia jednocześnie etykietę podstawy, karty i wykresy; w trakcie wczytywania nowego widoku nie ma nagłówka poprzedniego; `app-incomplete-data-notice` w obu; zaślepka usunięta; dotychczasowe testy „Sprzedaży” zielone |

E2E: brak infrastruktury E2E (jak SF-17, SF-19). Scenariusze ręczne UI w `06-implementation-done.md`.

## Verification

Środowisko (fakty dla wykonawcy):

- Build i testy **w Release** — `bin/Debug` może być zablokowany przez API uruchomione w Visual
  Studio. **Nigdy nie zabijać procesu Visual Studio ani uruchomionego API.**
- Migracji nie ma. Gdyby jednak była potrzebna: `dotnet ef … --configuration Release`.
- Testy integracyjne na LocalDB:
  `export STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true'`.
- Mało pamięci: `-m:1`, polecenia kolejno (nie uruchamiać testów .NET i `npm run build` naraz),
  testy integracyjne **małymi partiami** — jedna klasa na polecenie.

Backend, z katalogu repozytorium (Git Bash):

```
dotnet restore stone-flow.slnx --locked-mode
dotnet build stone-flow.slnx -c Release -m:1 --no-restore
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release --no-build
export STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true'
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*CashFlowReportApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OperationsApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*SalesReportApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*EndpointSecurityTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OpenApiTests"
```

Pełny `dotnet test` całego rozwiązania uruchamia CI.

Frontend, z `app/`, kolejno:

```
npm ci
npm run typecheck
npm test -- --watch=false
npm run lint
npm run format:check
npm run build        # Chart.js nadal w leniwym fragmencie Pulpitu, budżet initial bez wzrostu
```

Sprawdzenie granic frontu: `grep -rn "features/operations" app/src/app/features/dashboard` → brak
wyników.

Manual (ryzyko `high` — **przegląd człowieka przed merge**):

- scenariusze ręczne UI w `06-implementation-done.md` (tabela): przełączanie widoków i okresów
  (miesiąc, poprzedni, rok, własny, przed uruchomieniem, przecinający uruchomienie) z jednoczesną
  zmianą nagłówka, kart, wykresów i podpisów; `422` w jednym widoku nie przechodzi do drugiego;
  przejścia z trzech kart, trzech pozycji podziału i „Pokaż wszystkie operacje” z porównaniem sum
  listy „Operacje” z Pulpitem; po przejściu adres `/operacje` bez parametrów, zmiana filtra, wejście
  w źródło i „Wstecz” → zmieniony filtr zostaje (rozstrzygnięcie 4); powrót na Pulpit z zachowanym
  widokiem i okresem oraz nowym „Stan na”; klawiatura i czytnik ekranu na dwóch wykresach; tabela
  danych wykresu; telefon, tablet, szeroki ekran; niekompletna integracja; wydatek ujemny i wypłata
  w okresie; `prefers-reduced-motion`;
- przegląd kodu: `CashFlowSummary` (przedziały, podział, brak `Math.Abs`), `CashOperationReader`
  (przeniesienie 1:1, kolejność), `GetCashFlowReportHandler` (zakresy okresu i bazy, przełom
  uruchomienia), brak arytmetyki pieniędzy w UI poza `chartNumber`;
- otwarte punkty do `06-implementation-done.md`: import `features/integration/incomplete-data-notice`
  w `dashboard-page.ts` (SF-19, niezmieniony); progi 1200 px vs `xl` 1280 px; decyzje wykonawcy ze
  spec i zmiana dotycząca `sourceLink` do potwierdzenia w opisie PR.

## Risks

- **Ryzyko `high`** (FIN-05, FIN-07, URU-01): błąd zakresu dat, przełomu uruchomienia albo znaku
  wydatku ujemnego daje zły wynik bez widocznego błędu. Łagodzą: te same źródła, `CashFlowTotals` i
  `LaunchCutoff` co lista „Operacje”; wspólna kolejność w `CashOperationReader`; uzgodnienie kart,
  podziału i ostatnich operacji z `/api/operations` w I-CF-2 dla trzech rodzajów okresu;
  obowiązkowy przegląd człowieka.
- **Refaktoryzacja `ListOperationsHandler`** dotyka działającej listy SF-17. Pętla przenoszona 1:1,
  istniejące testy bez edycji, RD-4.
- **Odstępstwo od założenia spec:** `sourceLink` przeniesiony do `shared/dictionaries/operations.ts`
  zamiast kopii w modelu Pulpitu (rozstrzygnięcie 1, wariant z przeglądu). Do odnotowania w `06`.
- **Odstępstwo od `/wydatki`:** lista „Operacje” usuwa parametry przejścia z adresu (`replaceUrl`),
  a lista wydatków je zostawia. Świadoma różnica (rozstrzygnięcie 4); po odświeżeniu strony filtr
  pochodzi ze stanu serwisu (w nowej karcie przeglądarki — domyślny).
- **Progi szerokości:** spec mówi 1200 px, kod SF-19 i ten plan używają `xl` (1280 px) dla spójności
  obu widoków. Zmiana na `min-[1200px]:` w obu widokach to osobna decyzja.
- **Zmiana `DashboardService` wpływa na „Sprzedaż”:** `setView` wczytuje dane, nagłówek z
  `activeReport`. Istniejące testy SF-19 muszą przejść; zmieniane są tylko przypadki opisujące
  zaślepkę.
- **Pasek stronicowania** przy 10 ostatnich operacjach widoczny z nieaktywnymi przyciskami
  (`app-data-table` bez opcji ukrycia; `shared/ui` poza zakresem).
- **Otwarty punkt z SF-19:** import `features/integration` w `dashboard-page.ts` — nie naprawiany
  (wymaga przeniesienia `IntegrationStatus`), wpis do `06`.
