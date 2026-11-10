# Task Specification — SF-20

## Source

[SF-20](https://standev.youtrack.cloud/issue/SF-20)

## Goal

Zastąpić zaślepkę „Przepływy pieniężne” na Pulpicie pełnym widokiem rzeczywistych pieniędzy. Widok obejmuje:

- trzy karty KPI z porównaniem: Wpływy, Wypływy, Zmiana środków, prowadzące do listy „Operacje”;
- wykres wpływów i wypływów w czasie;
- podział wypływów według rodzaju operacji (opłacone wydatki, wypłaty właścicieli, zwroty);
- ostatnie operacje z przejściem do pełnej listy „Operacje”.

Raport liczy backend w module Reporting, wyłącznie na istniejących regułach i kontraktach: przepływy z SF-17 (`ICashOperationSource`, `CashFlowPolicy`, `CashFlowTotals`) oraz okresy, przełom uruchomienia i porównanie z SF-19 (`ReportPeriod`, `LaunchCutoff`, `Comparison`). Dzięki temu karty równają się sumom listy „Operacje” przy tym samym okresie (FIN-05). Lista „Operacje” dostaje odczyt parametrów przejścia z Pulpitu (okres i rodzaje).

## Context

- **Backend, moduł Reporting** (schemat `reporting`, bez zmian schematu):
  - `Reporting.Domain/CashFlow/`: istniejące `CashFlowPolicy`, `CashFlowTotals`, `CashMovement` (SF-17) bez zmiany zachowania. Dochodzi reguła podziału i agregacji w czasie (`CashFlowSummary`) oraz stała kolejność rodzajów wypływów w podziale (`CashFlowPolicy.OutflowKinds`).
  - `Reporting.Domain/Periods/`: `ReportPeriod`, `LaunchCutoff`, `Comparison` (SF-19) używane bez zmian.
  - `Reporting.Application/CashFlow/GetCashFlowReport.cs`: nowe zapytanie i handler, wzorzec `Sales/GetSalesReport.cs`. Walidacja okresu przez istniejące `Sales/ReportPeriodInput` (internal, ten sam assembly), „dziś” przez `WarsawTime.Today`.
  - `Reporting.Application/Operations/`: odczyt źródeł z bezpiecznikiem i stała kolejność operacji (dziś wewnątrz `ListOperationsHandler`) wydzielone do modułowej operacji współdzielonej przez `ListOperations` i `GetCashFlowReport` (conventions: „extract a module-local operation”). Zachowanie `GET /api/operations` się nie zmienia.
  - `Reporting.Api/Endpoints/`: `CashFlowReportEndpoints.cs` i `CashFlowReportContracts.cs`, mapowane w `ReportingModule.MapReportingEndpoints`. Element listy ostatnich operacji ma kształt `OperationItemResponse` (SF-17), a okres i nagłówek porównania — kształt `SalesReportPeriodResponse` i `SalesReportComparisonResponse` (SF-19).
- **Kontrakty międzymodułowe:** bez zmian. Reporting czyta Orders (wpłaty, zwroty) i Expenses (opłacone wydatki, wypłaty) przez istniejące `IEnumerable<ICashOperationSource>` (`OrderCashOperationSource`, `ExpenseCashOperationSource`).
- **Data uruchomienia:** `ILaunchDate`; czas wyliczenia z wstrzykniętego `TimeProvider`.
- **Frontend:**
  - `features/dashboard/`:
    - nowy widok `app-cash-flow-view` zamiast `app-empty-state`;
    - wybór okresu i nagłówek aktualności wspólne dla obu widoków;
    - `DashboardService` dostaje stan raportu przepływów;
    - `ReportsApi` — metodę `cashFlowReport`;
    - `KpiCard` — ikony i link do listy „Operacje”.
  - `features/operations/`: `OperationsListPage` czyta parametry przejścia z Pulpitu, a `OperationsService` dostaje operację ustawienia filtra z przejścia (wzór `ExpensesService.filterByCostPeriod`, SF-19).
  - `shared/dictionaries/`: etykiety rodzajów i kierunków operacji pieniężnych przenoszone z `features/operations/operation-models.ts`, bo używają ich teraz dwa obszary (conventions). `operation-models.ts` eksportuje je dalej pod dotychczasowymi nazwami.
  - Adapter wykresu `ChartCanvas` / `CHART_FACTORY` i `chart.js` 4.6.1 (SF-19) bez nowych zależności.

## Scope

### In scope

- `CashFlowSummary` w `Reporting.Domain/CashFlow/` z testami jednostkowymi: sumy okresu (przez `CashFlowTotals`), podział wypływów według rodzaju, wartości w przedziałach wykresu.
- Wydzielenie odczytu źródeł z bezpiecznikiem i kolejności operacji z `ListOperationsHandler` do operacji modułowej, bez zmiany zachowania `GET /api/operations` (istniejące testy przechodzą bez zmian).
- `GET /api/reports/cash-flow`:
  - okres i nagłówek porównania;
  - trzy karty z porównaniem;
  - szereg czasowy;
  - podział wypływów;
  - 10 ostatnich operacji;
  - czas wyliczenia.
- UI Pulpitu:
  - widok „Przepływy pieniężne” z kartami, wykresem w czasie z tabelą danych, podziałem wypływów z listą i ostatnimi operacjami;
  - wspólny wybór okresu, nagłówek i komunikat o niekompletnych danych dla obu widoków;
  - przełącznik zmieniający jednocześnie karty, wykresy i podpisy.
- Lista „Operacje”: odczyt parametrów `od`, `do`, `rodzaj` (powtarzalny) przy wejściu na `/operacje`.
- Przeniesienie słowników rodzajów i kierunków operacji do `shared/dictionaries/`.
- Scenariusze testów ręcznych UI w `06-implementation-done.md`.

### Out of scope

- Saldo rachunku i saldo początkowe (ticket, PRD §4, §8).
- Wpływy niezwiązane z zamówieniem (ticket, PRD §4).
- Zmiany reguł kierunku i sum przepływów (SF-17) oraz reguł okresów i porównania (SF-19).
- Nowe źródła danych, kontrakty międzymodułowe, indeksy i migracje.
- Zmiany widoku „Sprzedaż” poza przeniesieniem wyboru okresu i nagłówka nad przełącznik widoku.
- Eksport raportu, prognozy, „wynik zarządczy”, „pozostało po wypłatach” (PRD §8).
- Zmiana kolumn, sum i filtrów listy „Operacje” poza odczytem parametrów przejścia.

## Behavior

### Okres, porównanie i przełom uruchomienia (bez nowych reguł)

1. Parametry `period`, `year`, `from`, `to` oraz ich walidacja są takie same jak w `GET /api/reports/sales` (SF-19, Behavior pkt 1–7, `ReportPeriodInput`):
   - bieżący miesiąc — cały miesiąc kalendarzowy;
   - poprzedni miesiąc;
   - rok — bieżący do dziś, wcześniejszy w całości;
   - własny zakres — `from` ≤ `to` ≤ dziś + 1 rok;
   - rok nie późniejszy niż bieżący.
2. **Efektywny początek** = `LaunchCutoff.EffectiveFrom(from, data uruchomienia)`. Raport liczy wyłącznie operacje z datą operacji pieniężnej w [efektywny początek, `to`]. Gdy efektywny początek jest późniejszy niż `from`, widok pokazuje „Raport liczony od {data uruchomienia} (data uruchomienia).” (tekst `launchNotice` z SF-19).
3. **Okres w całości przed uruchomieniem** (`ReportPeriod.BeforeLaunch`): handler nie odpytuje źródeł i zwraca `kpis: null` oraz puste listy. Widok pokazuje komunikat `beforeLaunchMessage` z SF-19 zamiast kart, wykresów i listy, bez zer (PRD §8, zabezpieczenie 5).
4. **Porównanie** dotyczy każdej karty osobno i używa `ReportPeriod.Base`, `BaseAvailable` i `Comparison.Of`:
   - baza zaczyna się przed datą uruchomienia → wszystkie trzy karty pokazują „brak podstawy porównania”, a źródła nie są odpytywane dla bazy;
   - w przeciwnym razie wartości bazowe liczą te same reguły (pkt 6–8) dla zakresu bazowego;
   - baza karty równa 0 → „brak podstawy porównania” tylko dla tej karty;
   - różnica = bieżąca − bazowa, procent = różnica / |bazowa| × 100, zaokrąglony do 0,1 połową od zera;
   - opis porównania pod wyborem okresu jest taki sam jak w widoku „Sprzedaż” (`comparisonDescription`).
5. **Agregacja wykresu** — `ReportPeriod.Granularity` i `Buckets()`:
   - miesiąc → dni;
   - rok → miesiące;
   - własny zakres ≤ 62 dni → dni, dłuższy → miesiące.

   Przedziały pokrywają efektywny zakres bez luk.

### Reguły przepływów (istniejące, SF-17)

6. Operacje dostarczają źródła `ICashOperationSource`, wywoływane po kolei dla [efektywny początek, `to`] i wszystkich rodzajów:

   | Rodzaj | Kierunek | Data operacji | Kwota |
   |---|---|---|---|
   | Wpłata (`payment`) | wpływ | data wpłaty | kwota wpłaty |
   | Zwrot (`refund`, zwrot sprzedaży i zwrot pieniędzy) | wypływ | data zwrotu | kwota zwrotu |
   | Opłacony wydatek (`expense`) | wypływ | data zapłaty | kwota brutto ze znakiem |
   | Wypłata właściciela (`withdrawal`) | wypływ | data wypłaty | kwota |

   Skutki reguły:

   - wartość zamówienia, nieopłacone zamówienie, korekta sprzedaży i wydatek bez daty zapłaty (także wystąpienie cykliczne) nie są operacjami i nie zmieniają przepływów;
   - wpłaty i zwroty liczą się niezależnie od stanu zamówienia: anulowanego, pending, failed, szkicu i zamówienia z datą sprzed uruchomienia (URU-01, PRD §5 „Anulowanie i zwrot”);
   - zapłata od daty uruchomienia wydatku z datą kosztu sprzed uruchomienia jest wypływem.
7. **Wpływy** = suma kwot wpływów, **Wypływy** = suma kwot wypływów, **Zmiana środków** = wpływy − wypływy. Liczy je `CashFlowTotals.Of` — ta sama reguła co sumy listy „Operacje”. Opłacony wydatek ujemny pomniejsza wypływy (bez wartości bezwzględnej). Każda suma może być ujemna.
8. **Podział wypływów według rodzaju** (`CashFlowSummary`) — ta sama suma wypływów pogrupowana po rodzaju operacji, zawsze trzy pozycje w kolejności z PRD §5 (`CashFlowPolicy.OutflowKinds`):
   - opłacone wydatki (`expense`);
   - wypłaty właścicieli (`withdrawal`);
   - zwroty (`refund`, oba rodzaje zwrotu razem).

   Rodzaj bez operacji ma `0.00`. Suma pozycji = karta „Wypływy” (test).
9. **Szereg czasowy:** dla każdego przedziału (pkt 5) wpływy, wypływy i zmiana środków liczone przez `CashFlowTotals.Of` na operacjach z datą w przedziale. Suma przedziałów = wartości kart (test).
10. **Kwoty:** sumy zapisanych kwot `decimal(18,2)`, bez ponownego zaokrąglania (KWO-01). Zaokrągla się wyłącznie procent porównania. Backend nie używa typów zmiennoprzecinkowych.
11. **Bezpiecznik** (jak SF-17 i SF-19):
    - operacje okresu są wczytywane łącznie z limitem 10 001 wierszy (SQL `TOP`, limit malejący między źródłami jak w `ListOperationsHandler`);
    - okres bazowy osobno, z tym samym limitem;
    - więcej niż 10 000 operacji w którymkolwiek zakresie → `422 report_range_too_large` („Za dużo danych w wybranym okresie. Wybierz krótszy okres.”).

### Ostatnie operacje

12. Raport zwraca do 10 operacji z [efektywny początek, `to`], najnowsze pierwsze. Kolejność jest ta sama co domyślna kolejność listy „Operacje”: data malejąco, remis rodzaj (`payment`, `refund`, `expense`, `withdrawal`), potem id. Zapewnia to wspólna operacja kolejności (Context). Ostatnie operacje to zatem dokładnie pierwsze pozycje listy „Operacje” otwartej z tym samym okresem.
13. Element ma pola i znaczenie jak pozycja `GET /api/operations` (SF-17): rodzaj, data, kierunek, kwota zapisana bez zmiany znaku przez kierunek, metoda, rodzaj zwrotu, opis, kategoria, typ i id źródła, tekst linku.
14. Pod listą link „Pokaż wszystkie operacje” prowadzi do `/operacje?od={efektywny początek}&do={to}`, bez filtra rodzaju.

### Przejścia z kart i podziału

15. Przejścia prowadzą do listy „Operacje” z okresem [efektywny początek, `to`]. Suma wskazana w ostatniej kolumnie równa się wartości na Pulpicie przy tym samym okresie:

    | Element | Adres | Suma listy równa wartości |
    |---|---|---|
    | Karta „Wpływy” | `/operacje?od=…&do=…&rodzaj=payment` | „Wpływy” |
    | Karta „Wypływy” | `/operacje?od=…&do=…&rodzaj=refund&rodzaj=expense&rodzaj=withdrawal` | „Wypływy” |
    | Karta „Zmiana środków” | `/operacje?od=…&do=…` | „Zmiana środków” (wpływy i wypływy też) |
    | Pozycja podziału „Opłacone wydatki” | `…&rodzaj=expense` | „Wypływy” listy = wartość pozycji |
    | Pozycja „Wypłaty właścicieli” | `…&rodzaj=withdrawal` | jw. |
    | Pozycja „Zwroty” | `…&rodzaj=refund` | jw. |
16. Przy okresie przed uruchomieniem karty i pozycje nie istnieją (pkt 3), więc linków nie ma.

### Lista „Operacje” — parametry przejścia

17. Przy wejściu na `/operacje` lista odczytuje raz (snapshot, jak `kosztOd`/`kosztDo` na `/wydatki` w SF-19) parametry:
    - `od`, `do` — daty `yyyy-MM-dd`;
    - `rodzaj` — powtarzalny kod `payment` | `refund` | `expense` | `withdrawal`.
18. Gdy jest którykolwiek parametr:
    - okres = [`od`, `do`];
    - rodzaje = poprawne kody w stałej kolejności `OPERATION_KINDS`; brak kodów oznacza wszystkie rodzaje;
    - sortowanie wraca do daty malejąco, strona do 1.
19. Niepoprawne parametry są pomijane:
    - brak, niepoprawna lub nieistniejąca data albo `od` > `do` → okres domyślny, czyli bieżący miesiąc;
    - nieznany kod rodzaju jest pomijany.

    Wejście bez parametrów zachowuje dotychczasowy stan serwisu (SF-17, pkt 14).

### Pulpit

20. Przełącznik [Sprzedaż / Przepływy pieniężne] (SF-19) zostaje; domyślnie „Sprzedaż”. Wybór okresu (`app-period-picker`), etykieta okresu, informacja o dacie uruchomienia, opis porównania, „Stan na …”, etykieta podstawy i `app-incomplete-data-notice` są nad treścią widoku, wspólne dla obu widoków. Wybrany okres jest jeden dla obu widoków.
21. **Zmiana podstawy jednocześnie:**
    - nagłówek (etykieta okresu, opis porównania, „Stan na”, etykieta podstawy) pochodzi z raportu aktywnego widoku;
    - w czasie wczytywania raportu aktywnego widoku nagłówek i treść pokazują stan wczytywania. Nigdy nie widać kart lub podpisów jednego widoku obok nagłówka drugiego.
22. **Wczytywanie:**
    - wejście na `/pulpit` (`enter`), zmiana widoku i zmiana okresu wczytują raport aktywnego widoku;
    - dla „Sprzedaży” jak dotąd raport i zestawienie, dla „Przepływów” raport przepływów;
    - wybór widoku i okresu przetrwa przejście do listy „Operacje” lub do źródła operacji i powrót (stan w `DashboardService`);
    - Pulpit nie odpytuje API w tle.
23. **Aktualność:**
    - „Stan na {calculatedAtUtc w Europe/Warsaw}”;
    - `app-incomplete-data-notice` bez zmian: zawsze „Ostatnia pełna kontrola”, a przy niekompletnej integracji ostrzeżenie z przejściem do integracji.
24. Widok „Przepływy pieniężne” nie zawiera słowa „zysk” (w żadnej odmianie) ani „saldo” i nie pokazuje stanu rachunku (PRD §8, ticket).

## Data / API

### Storage

- Brak zmian. Zapytania korzystają z istniejących indeksów dat operacji z SF-15 i SF-17: `IX_Payments_PaidOn`, `IX_Refunds_RefundedOn`, `IX_OwnerWithdrawals_WithdrawnOn` oraz indeks `expenses.Expenses.PaidOn`.

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/reports/cash-flow` | `period` (`currentMonth` \| `previousMonth` \| `year` \| `custom`, domyślnie `currentMonth`), `year` (`yyyy`, przy `year`), `from`, `to` (`yyyy-MM-dd`, wymagane przy `custom`) | `200` raport (niżej); `400` walidacja (jak `/api/reports/sales`); `422 report_range_too_large` | sesja | Reporting |
| GET | `/api/operations` | bez zmian | bez zmian | sesja | Reporting |

Nazwa endpointu: `.WithName("Reporting.GetCashFlowReport")`, tag `Reports`.

**Odpowiedź `GET /api/reports/cash-flow`:**

```
{
  period: { kind, from, to, effectiveFrom, launchDate, beforeLaunch, inProgress, granularity },  // jak w raporcie sprzedaży
  comparison: { from, to, available, reason: "beforeLaunch"|null },                              // jak w raporcie sprzedaży
  kpis: {                                       // null, gdy beforeLaunch
    inflows:  { value, comparison },
    outflows: { value, comparison },
    change:   { value, comparison }
  },
  series:            [ { from, to, inflows, outflows, change } ],      // [] gdy beforeLaunch
  outflowsByKind:    [ { kind: "expense"|"withdrawal"|"refund", amount } ],  // zawsze 3 pozycje w tej kolejności, [] gdy beforeLaunch
  recentOperations:  [ OperationItemResponse ],                         // ≤ 10, [] gdy beforeLaunch
  calculatedAtUtc
}
comparison karty: { status: "available"|"noBasis", base, difference, percent }   // jak AmountComparisonResponse (SF-19)
```

- Kwoty to tekst z kropką i opcjonalnym minusem (`"-1234.50"`), procent z jednym miejscem po kropce (`"12.5"`), daty `yyyy-MM-dd`, `calculatedAtUtc` w ISO 8601 UTC. Kody rodzajów są zgodne z `OperationCodes`.
- `recentOperations[*]` ma dokładnie pola `OperationItemResponse` (SF-17). Jedno mapowanie służy obu endpointom, bez kopii.
- Walidacja i komunikaty `400` są takie same jak w `GET /api/reports/sales` (wspólne `ReportPeriodInput`). Parametry niepotrzebne dla danego `period` są ignorowane.

### Types crossing a boundary

Brak nowych typów w `Shared.Kernel` i `Shared.Application`. Używane bez zmian: `CashOperationKind`, `CashOperation`, `ICashOperationSource`.

W `Reporting.Domain/CashFlow/` (bez I/O, testy jednostkowe):

- `CashFlowEntry(CashOperationKind Kind, DateOnly Date, decimal Amount)` — fakt, na który handler mapuje `CashOperation`. `CashMovement` (SF-17) zostaje bez zmian, a `CashFlowSummary` buduje z wpisów `CashMovement` dla `CashFlowTotals.Of`.
- `CashFlowPolicy.OutflowKinds` — rodzaje wypływów w kolejności podziału: `PaidExpense`, `OwnerWithdrawal`, `Refund`. Każdy z nich ma kierunek `Outflow` według `DirectionOf` (test).
- `CashFlowSummary.Of(entries, period)` zwraca:
  - `Totals` (`CashFlowTotals`) dla [efektywny początek, `to`];
  - `OutflowsByKind` (lista `KindAmount(CashOperationKind, decimal)` w kolejności `OutflowKinds`);
  - `Buckets` (lista `CashFlowTotals` zgodna z `period.Buckets()`).

  Wpisy spoza efektywnego zakresu są pomijane, a przy `BeforeLaunch` wynik jest pusty, jak w `CostSummary`.

W `Reporting.Application`:

- `CashFlow/GetCashFlowReportQuery(Period, Year, From, To)` → `GetCashFlowReportResult(Report?, Errors?, TooManyRows)`.
- `GetCashFlowReportHandler(IEnumerable<ICashOperationSource>, ILaunchDate, TimeProvider)`:
  1. walidacja okresu;
  2. przy `BeforeLaunch` — wynik bez odpytywania;
  3. wczytanie okresu, a przy `BaseAvailable` — bazy (bezpiecznik);
  4. `CashFlowSummary` dla obu;
  5. `Comparison.Of` dla trzech kart;
  6. ostatnie operacje — 10 pierwszych we wspólnej kolejności;
  7. `calculatedAtUtc` z `TimeProvider`.
- `Operations/` — wspólna operacja modułowa, np. `CashOperationReader`:
  - odczyt źródeł po kolei z limitem `MaxOperations + 1` (wynik `null` przy przekroczeniu);
  - stała kolejność operacji: data (malejąco albo rosnąco), rodzaj, id.

  Używają jej `ListOperationsHandler` (bez zmiany wyniku) i `GetCashFlowReportHandler`. Właścicielem bezpiecznika i kolejności jest ta operacja; walidacja zostaje w handlerach.

Łączenie i sumy w pamięci Reporting: odstępstwo od stronicowania w bazie, uzasadnione jak w SF-17 i SF-19 (źródła w różnych modułach, skala D-08). Źródła są wywoływane sekwencyjnie (wspólne `ModuleConnection`, bez `Task.WhenAll`).

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sales separate from cash | Wpływy to wyłącznie wpłaty, wypływy wyłącznie zwroty, opłacone wydatki i wypłaty — według dat operacji pieniężnych. Raport nie czyta wartości zamówień, korekt sprzedaży ani nieopłaconych wydatków. Widok sprzedaży nie czyta wpłat (SF-19), więc wpłata nie tworzy drugiej sprzedaży. Testy: nieopłacone zamówienie → wpływy 0; dodanie wpłaty zmienia wpływy, a raport sprzedaży nie. |
| 2. Backend decimal money | Sumy, podział, przedziały, różnice i procenty liczy backend na `decimal` (`CashFlowTotals`, `CashFlowSummary`, `Comparison`). Frontend tylko formatuje ciągi z API. „Razem” w tabeli wykresu to wartości kart z API, a nie suma w UI. Konwersja tekst → liczba istnieje wyłącznie w adapterze wykresu (`chartNumber`) dla geometrii. |
| 3. API-enforced access | Nowy `GET` pod domyślną polityką sesji, bez `.AllowAnonymous()`. Test integracyjny: `401` bez sesji. |
| 4. Retries never duplicate | Nie dotyczy (tylko odczyty). |
| 5. Financial audit trail | Nie dotyczy (brak zmian danych finansowych). |
| 6. Secrets stay server-side | Brak nowych sekretów i zależności. |
| 7. Module and layer boundaries | Reporting czyta wyłącznie przez `ICashOperationSource` (`Shared.Application`). Nowe reguły leżą w `Reporting.Domain` (zależność tylko od `Shared.Kernel`). Frontend: `features/dashboard` nie importuje `features/operations`; wspólne słowniki przechodzą do `shared/dictionaries`. Testy architektury bez nowych wyjątków. |
| 8. No identifying data in docs | Przykłady w dokumentach fikcyjne. Ostatnie operacje pokazują to samo co lista „Operacje” (numer zamówienia, opis wydatku, osoba wypłaty). Nie dochodzą nowe dane klienta. |

## Risk

**high** — pieniądze: definicje kart przepływów, ich uzgodnienie z listą „Operacje” (FIN-05) i rozdzielenie sprzedaży od pieniędzy (FIN-07). Błąd w zakresie dat, przełomie uruchomienia albo znaku wydatku ujemnego daje błędny wynik bez widocznego błędu. Wymaga przeglądu przez człowieka przed merge.

## UI

**Trasa:** `/pulpit` (istniejąca). Lista „Operacje” `/operacje` (istniejąca) dostaje parametry przejścia.

**Układ Pulpitu** (design guide §4, §9):

1. Nagłówek „Pulpit”, przełącznik „Widok” (bez zmian), pod nim wspólny wybór okresu i wspólny blok aktualności (Behavior pkt 20–21).
2. Etykieta podstawy dla „Przepływów pieniężnych”: „Kwoty brutto w PLN według daty operacji pieniężnej: wpłaty, zwrotu, zapłaty wydatku albo wypłaty. Raporty obejmują dane od {data uruchomienia}.” Dla „Sprzedaży” zostaje dotychczasowa etykieta.
3. **Trzy karty KPI** (`app-kpi-card`):
   - jedna kolumna poniżej 768 px, dwie kolumny w 768–1199 px (trzecia karta na pełną szerokość), trzy w rzędzie od 1200 px;
   - kwoty nie są ucinane;
   - porównanie neutralne (strzałka i słowo, bez zieleni i czerwieni), tekst z `amountComparisonText`.

   | Karta | Opis podstawy | Wskazówka | Cel |
   |---|---|---|---|
   | Wpływy | „Wpłaty za zamówienia według daty wpłaty. Nieopłacone zamówienia ich nie zwiększają.” | „Pokaż wpłaty” | pkt 15 |
   | Wypływy | „Zwroty, opłacone wydatki według daty zapłaty i wypłaty właścicieli. Ujemne opłacone wydatki pomniejszają wypływy.” | „Pokaż wypływy” | pkt 15 |
   | Zmiana środków | „Wpływy minus wypływy w tym samym okresie.” | „Pokaż operacje” | pkt 15 |

   Karty są linkami (`KpiAction` typu `link` z adresem i `queryParams`). Ikony są nowe i mają `aria-hidden`.
4. **Wiersz wykresów** — od 1200 px obok siebie (wykres w czasie szerszy), poniżej jeden pod drugim:
   - **„Wpływy i wypływy w czasie”** (`ChartCanvas`, typ `timeline`):
     - dwie serie słupków: „Wpływy” (`--sf-accent`) i „Wypływy” (`--sf-text`), z legendą i stałą kolejnością;
     - oś Y od zera z obsługą wartości ujemnych, opis osi „zł”;
     - oś X: dni („01.11”) albo miesiące („lis 2026”), etykiety `bucketLabel`;
     - tytuł z okresem;
     - przycisk „Pokaż dane w tabeli” (`aria-expanded`, `aria-controls`) odsłania tabelę Okres / Wpływy / Wypływy / Zmiana środków z wierszem „Razem” z wartości kart.
   - **„Wypływy według rodzaju”** (`ChartCanvas`, typ `horizontalBar`, `--sf-text`):
     - trzy pozycje: „Opłacone wydatki”, „Wypłaty właścicieli”, „Zwroty”;
     - zawsze widoczna lista z kwotami; każda pozycja jest linkiem (pkt 15);
     - etykieta: „Suma pozycji równa się wypływom. Ujemne opłacone wydatki pomniejszają swoją pozycję.”
5. **„Ostatnie operacje”** (nagłówek `h2`):
   - `app-data-table` z `[mobileCards]="true"`, jedna strona (`total` = liczba pozycji), bez sortowania i filtrów;
   - kolumny jak na liście „Operacje” bez „Metody”: Data, Rodzaj (zwrot ze swoim rodzajem), Opis (link do źródła: `/zamowienia/{id}`, `/wydatki/{id}`, `/operacje/wyplaty/{id}`), Kierunek (tekst „Wpływ”/„Wypływ”), Kwota (do prawej, `tabular-nums`, kwota zapisana, ujemna ze znakiem `−` i dopiskiem „Korekta / zwrot od dostawcy”);
   - pod tabelą link „Pokaż wszystkie operacje” (pkt 14).

**Wykresy i dostępność (design guide §7):**

- Reużyty `ChartCanvas`:
  - canvas z `role="img"`, `aria-label` z tytułem, okresem, jednostką i wskazaniem tabeli albo listy;
  - `tabindex="0"`, strzałki, Home/End i Escape;
  - region `aria-live="polite"` z wartością aktywnego punktu;
  - tooltip pokazuje ciągi z API przez `formatPln`.
- Kolory z tokenów (`lint:colors` bez wyjątków), animacje wyłączone przy `prefers-reduced-motion: reduce`.
- Każda wartość jest dostępna tekstowo: karty, tabela danych wykresu, lista podziału i tabela ostatnich operacji.

**Stany widoku „Przepływy pieniężne”:**

- wczytywanie: `app-loading-state` („Wczytywanie raportu…”), bez fikcyjnych zer;
- błąd: `app-error-state` z „Spróbuj ponownie” (ponowny odczyt raportu przepływów);
- `422`: komunikat z API przy wyborze okresu (`rangeError`);
- okres przed uruchomieniem: komunikat z pkt 3 zamiast kart;
- brak operacji w okresie po uruchomieniu: karty `0,00 zł` (rzeczywiste zero), podział z zerami, wykres z zerowymi przedziałami, tabela ostatnich operacji ze stanem pustym „Brak operacji w wybranym okresie”.

**Lista „Operacje”:** filtry z parametrów są widoczne jako aktywne chipy (istniejące `activeFilters`), a sumy listy są równe wartościom z Pulpitu (pkt 15).

**Formaty:** `formatPln`, `formatDate`, `formatMonth`, `formatDateTime`, `formatPercent` z `shared/format/`, bez arytmetyki.

## Edge Cases

Okres i przełom uruchomienia:

- Data uruchomienia w środku bieżącego miesiąca: efektywny początek = data uruchomienia, z informacją pod wyborem okresu. Porównanie → „brak podstawy porównania” (baza sprzed uruchomienia). Linki kart prowadzą do listy od daty uruchomienia.
- Okres w całości przed uruchomieniem (miesiąc, rok, własny zakres): komunikat bez kart i zer. Źródła nie są odpytywane.
- Własny zakres 62 dni → dni, 63 dni → miesiące. Przedział miesięczny przycięty do efektywnego zakresu.
- `year` > bieżący rok, `custom` bez daty, `from` > `to`, `to` > dziś + 1 rok → `400` jak w raporcie sprzedaży.

Przepływy:

- Nieopłacone zamówienie 1000 zł: wpływy 0 zł. Wpłata 300 zł w kolejnym miesiącu: wpływy kolejnego miesiąca 300 zł, sprzedaż (SF-19) nadal 1000 zł w miesiącu zamówienia — bez drugiej sprzedaży.
- Wpłata z dnia uruchomienia (albo późniejsza) do zamówienia z datą sprzed uruchomienia: zwiększa wpływy. Wpłata z dnia przed uruchomieniem: nie ma jej w raporcie (URU-01).
- Zwrot po uruchomieniu do zamówienia sprzed uruchomienia: zwiększa wypływy (pozycja „Zwroty”), sprzedaży nie obniża.
- Zwrot pieniędzy (nadpłata) i zwrot sprzedaży: oba w pozycji „Zwroty” i we wypływach.
- Anulowane zamówienie z wpłatą i zwrotem: obie operacje zostają w przepływach.
- Wydatek bez daty zapłaty, także wystąpienie cykliczne: nie zwiększa wypływów. Po „Oznacz jako opłacony” wchodzi w okresie daty zapłaty. Po „Cofnij oznaczenie zapłaty” wypada.
- Opłacony wydatek −200 zł obok opłaconego wydatku 500 zł: pozycja „Opłacone wydatki” 300 zł, wypływy o 300 zł.
- Same korekty od dostawców w okresie: wypływy ujemne, zmiana środków większa od wpływów. Słupek wypływów poniżej osi.
- Zapłata od uruchomienia wydatku z datą kosztu sprzed uruchomienia: wypływ, ale nie koszt (SF-19).
- Wypłata właściciela: tylko w wypływach i pozycji „Wypłaty właścicieli”; koszty i wynik przed wypłatami się nie zmieniają.
- Korekta sprzedaży: brak w przepływach i ostatnich operacjach.
- Baza karty równa 0 (np. brak wpłat w poprzednim miesiącu, przy opłaconych wydatkach w tym miesiącu): „brak podstawy porównania” tylko dla „Wpływów”, pozostałe karty mają porównanie. Baza zmiany środków ujemna → dzielnik |baza|.
- Wpływy i wypływy równe: zmiana środków `0,00 zł`.

Ostatnie operacje i lista:

- Mniej niż 10 operacji: tyle, ile jest. Brak operacji: stan pusty.
- Kilka operacji z tą samą datą: kolejność rodzaj → id, identyczna z listą „Operacje”.
- Ponad 10 000 operacji w okresie lub w bazie → `422 report_range_too_large`.
- `/operacje?od=2026-11-31&do=2026-11-30`: niepoprawna data → bieżący miesiąc. `od` > `do` → bieżący miesiąc. `rodzaj=xyz` → pominięty.
- Powrót na `/operacje` bez parametrów po wcześniejszym wejściu z parametrami: zachowany ostatni filtr.
- Użytkownik zmienia filtr listy po przejściu z Pulpitu: sumy dotyczą bieżącego filtra i mogą się różnić od karty.

Przełącznik:

- Zmiana okresu w „Przepływach”, przełączenie na „Sprzedaż”: ten sam okres, raport sprzedaży wczytany ponownie, nagłówek z raportu sprzedaży.
- Szybkie przełączanie widoków: wynik starszego żądania nie nadpisuje nowszego (licznik żądań jak w SF-19).

## Dependencies

- SF-17 (`ICashOperationSource`, `CashFlowPolicy`, `CashFlowTotals`, lista „Operacje”, wypłaty) — w main.
- SF-19 (`ReportPeriod`, `LaunchCutoff`, `Comparison`, `ReportPeriodInput`, Pulpit, `ChartCanvas`, `KpiCard`, `DashboardService`) — na gałęzi `sf-19-sales-view`, na której bazuje ta gałąź. Merge SF-20 dopiero po SF-19.
- SF-14 (`app-incomplete-data-notice`) — w main.
- SF-18 (Historia zmian) nie jest wymagane; jest na gałęzi bazowej.

## Assumptions

Zgłoszenie było jednoznaczne co do zakresu, a niejasne punkty rozstrzygnął wykonawca, bez pytań do użytkownika (polecenie pracy autonomicznej). Każda decyzja jest zgodna z PRD i kodem; sprzeczności nie stwierdzono.

- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** reguły okresów, porównania, przycięcia do daty uruchomienia i stan „okres przed uruchomieniem” są takie same jak w SF-19 i pochodzą z tych samych typów (`ReportPeriod`, `LaunchCutoff`, `Comparison`, `ReportPeriodInput`), bez kopii reguł. Zgodne z PRD §8 (jedna reguła porównań dla raportów) i z ticketem („Reguły okresów … z zadań 19 i 14”).
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** porównanie dotyczy każdej z trzech kart osobno, z „brak podstawy porównania” przy bazie 0 albo sprzed uruchomienia (FIN-01–04, jak SF-19).
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** sumy kart liczą istniejące `ICashOperationSource`, `CashFlowPolicy` i `CashFlowTotals` z SF-17. Ich spec przewidział to wprost dla SF-20 („Spójność z SF-20 (FIN-05)”). Bez nowych źródeł, kontraktów i indeksów. Dzięki temu karta = suma listy „Operacje” przy tym samym okresie konstrukcyjnie, nie tylko w testach.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** karty Wpływy, Wypływy i Zmiana środków prowadzą do `/operacje` z efektywnym okresem raportu. Wpływy dostają filtr `payment`, Wypływy — `refund`, `expense`, `withdrawal`, a Zmiana środków jest bez filtra. Lista nie czytała dotąd parametrów, więc dochodzi ich odczyt, jak w SF-19 na liście wydatków. Nazwy parametrów to polskie słowa bez znaków diakrytycznych (`od`, `do`, `rodzaj`, wzór `kosztOd`/`kategoria`). Wartości rodzaju to kody API (wzór `kategoria=materialPurchase`).
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** link przekazuje efektywny początek (nie `from` sprzed przycięcia), jak karta „Koszty” w SF-19. Sumy listy są w obu wariantach takie same, bo lista też przycina do daty uruchomienia.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** wykres w czasie ma dwie serie słupków (Wpływy — złoty brąz, Wypływy — grafit, design guide §7), z przedziałami jak w SF-19. Zmiana środków nie jest trzecią serią wykresu, bo ticket i PRD mówią o „wykresie wpływów i wypływów”. Jest w tabeli danych wykresu (kolumna liczona w backendzie) i na karcie.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** podział wypływów ma trzy pozycje w kolejności z PRD §5: opłacone wydatki, wypłaty właścicieli, zwroty. „Zwroty” łączą zwrot sprzedaży i zwrot pieniędzy, bo PRD wymienia jeden rodzaj operacji „zwroty”, a filtr listy „Zwrot” obejmuje oba. Pozycja może być ujemna (opłacone wydatki ujemne), a suma pozycji = Wypływy.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** „Ostatnie operacje” to 10 najnowszych operacji efektywnego okresu, w kolejności domyślnej listy „Operacje”. PRD nie podaje liczby. 10 daje krótką tabelę, a „Pokaż wszystkie operacje” prowadzi do pełnej listy z okresem. Pozycje zwraca raport przepływów (jedno żądanie, te same dane co karty), w kształcie pozycji `GET /api/operations`. Tabela pomija kolumnę „Metoda” dla zwięzłości; metoda jest na pełnej liście.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** przełącznik zmienia karty, wykresy i podpisy razem. Wybór okresu i nagłówek aktualności są wspólne dla obu widoków, a okres jest jeden. Dane każdego widoku są wczytywane przy wejściu, przełączeniu i zmianie okresu tylko dla aktywnego widoku. Stan żyje w `DashboardService`.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** widok nie używa słów „zysk” i „saldo” i nie pokazuje stanu rachunku (PRD §8). Opis karty „Zmiana środków” nie wyjaśnia przez zaprzeczenie („to nie jest saldo/zysk”), żeby te słowa w ogóle nie padały; test sprawdza ich brak w treści widoku.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** komunikat o niekompletnej integracji i „Stan na” są takie same jak w SF-19 (`app-incomplete-data-notice`, `calculatedAtUtc`), także w widoku przepływów (PRD §5 „Analiza finansów”: niekompletna integracja widoczna obok wyników). Synchronizacja nie tworzy wpłat (PLAT-01), ale brak zamówienia w ERP uniemożliwia zapisanie jego wpłat.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** bezpiecznik 10 000 operacji na zakres (okres i baza osobno) z kodem `report_range_too_large`, jak w raportach SF-19. Lista „Operacje” zachowuje swój kod `operations_range_too_large`.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** odczyt źródeł z limitem i kolejność operacji są wydzielone z `ListOperationsHandler` do wspólnej operacji w `Reporting.Application/Operations/`, zamiast kopiować je do nowego handlera (DRY reguły „ta sama kolejność co lista”). Zachowanie `/api/operations` się nie zmienia, a jego testy zostają bez zmian.
- **Decyzja wykonawcy z 10.11.2026 (do potwierdzenia w PR):** słowniki rodzajów i kierunków operacji przechodzą do `shared/dictionaries/`, bo używają ich teraz `features/operations` i `features/dashboard` (conventions: obszary nie importują się wzajemnie). Mapa linku do źródła (3 trasy) jest w modelu Pulpitu, bo to nie słownik.
- Kolejność rodzajów w podziale (`OutflowKinds`) różni się od kolejności remisu listy (`payment`, `refund`, `expense`, `withdrawal`). To dwie różne reguły prezentacji, każda w jednym miejscu.
- Przepływy pieniężne nie muszą zgadzać się z ruchem na rachunku, bo wpływy niezwiązane z zamówieniem są poza MVP (PRD §4). Etykieta podstawy wymienia, co obejmują.
- Ryzyko pozostaje **high**.

## Acceptance Criteria

Kryteria ze zgłoszenia:

- [ ] Nieopłacone zamówienie nie zwiększa wpływów, a wpłata nie tworzy drugiej sprzedaży. Test integracyjny: zamówienie 1000 zł bez wpłat → wpływy 0; wpłata 300 zł w kolejnym miesiącu → wpływy tego miesiąca 300 zł, sprzedaż (raport SF-19) 1000 zł w miesiącu zamówienia i 0 w miesiącu wpłaty. Test jednostkowy `CashFlowSummary`.
- [ ] Suma wpływów i wypływów równa się liście „Operacje” przy tym samym okresie. Test integracyjny: `kpis` = `totals` z `GET /api/operations?from={effectiveFrom}&to={to}` dla bieżącego miesiąca, roku i własnego zakresu przecinającego datę uruchomienia; Wpływy = `inflows` z `kind=payment`, Wypływy = `outflows` z `kind=refund,expense,withdrawal`, każda pozycja podziału = `outflows` z jej rodzajem. Pozycje `recentOperations` = pierwsze 10 pozycji listy z `pageSize=10`.
- [ ] Wydatek bez daty zapłaty nie zwiększa wypływów. Testy integracyjny i jednostkowy; po oznaczeniu jako opłacony wchodzi w okresie daty zapłaty.
- [ ] Opłacony wydatek ujemny pomniejsza wypływy. −200 zł i 500 zł → „Opłacone wydatki” 300 zł, Wypływy o 300 zł (jednostkowy i integracyjny).
- [ ] Wpłata po uruchomieniu do zamówienia sprzed uruchomienia zwiększa wpływy; zwrot zwiększa wypływy (oba rodzaje zwrotu, w pozycji „Zwroty”). Wpłata sprzed uruchomienia nie wchodzi (jednostkowy i integracyjny).
- [ ] Widok nie używa słowa „zysk” i nie pokazuje salda rachunku. Test komponentu: treść widoku przepływów (karty, opisy, nagłówki, etykiety, tabela) nie zawiera „zysk” ani „saldo” (bez rozróżniania wielkości liter); odpowiedź API nie ma pola salda.
- [ ] Wartości są dostępne również tekstowo. Test komponentu: karty jako tekst, tabela danych wykresu z „Razem” równym kartom, lista podziału z kwotami, tabela ostatnich operacji; canvas z `aria-label` i obsługą klawiatury.

Kryteria wynikające z zakresu zgłoszenia i PRD:

- [ ] Karty prowadzą do listy „Operacje” z okresem i filtrem rodzaju (pkt 15); lista ustawia filtr z parametrów, a jej sumy równają się kartom.
- [ ] Przełączenie widoku zmienia jednocześnie karty, wykresy i podpisy; nie widać elementów jednego widoku obok nagłówka drugiego.
- [ ] Reguły okresów i komunikat o niekompletnych danych jak w SF-19: przycięcie do daty uruchomienia z informacją, komunikat przy okresie przed uruchomieniem (bez zer), „brak podstawy porównania” przy bazie 0 lub sprzed uruchomienia, „Stan na” i „Ostatnia pełna kontrola”.
- [ ] Suma przedziałów wykresu = karty; suma podziału = Wypływy (jednostkowe).
- [ ] `GET /api/reports/cash-flow` bez sesji → `401`; testy architektury przechodzą bez nowych wyjątków; `GET /api/operations` bez zmian zachowania (istniejące testy przechodzą).

## Verification

- build:
  - `dotnet restore stone-flow.slnx --locked-mode`;
  - `dotnet format stone-flow.slnx --verify-no-changes --no-restore`;
  - `dotnet build stone-flow.slnx -c Release --no-restore`;
  - frontend `npm ci`, `npm run build` (Chart.js nadal w leniwym fragmencie Pulpitu, bez wzrostu budżetu `initial`).
- tests:
  - **`StoneFlow.Reporting.Tests` (jednostkowe):**
    - `CashFlow/CashFlowSummaryTests` — scenariusze PRD §11 na wpisach:
      1. niezapłacone zamówienie → brak wpisów, wpływy 0;
      2. wpłata w innym miesiącu → wpływ w miesiącu wpłaty;
      3. zwrot sprzedaży i zwrot nadpłaty → oba w „Zwrotach” i wypływach;
      4. korekta od dostawcy (wydatek ujemny opłacony) → pomniejsza „Opłacone wydatki” i wypływy;
      5. anulowanie opłaconego zamówienia ze zwrotem → wpłata i zwrot zostają;
      6. zamówienie i wydatek otwarte w dniu uruchomienia → wpłata i zapłata od daty uruchomienia liczone, wcześniejsze pominięte;
      7. wydatek planowany → brak wpisu;
      8. wypłata → tylko wypływ;
      9. suma przedziałów = sumy okresu dla dni i miesięcy;
      10. suma podziału = wypływy;
      11. `BeforeLaunch` → wynik pusty;
    - `CashFlow/CashFlowPolicyTests` — `OutflowKinds`: kolejność i kierunek `Outflow`; dotychczasowe przypadki bez zmian;
    - `CashFlow/GetCashFlowReportHandlerTests` (fałszywe źródła):
      - walidacja jak w raporcie sprzedaży;
      - brak odpytywania przy `BeforeLaunch` i dla bazy sprzed uruchomienia;
      - bezpiecznik `422` dla okresu i dla bazy;
      - porównanie per karta (baza 0, baza ujemna);
      - ostatnie operacje: limit 10, kolejność z remisami identyczna z `ListOperationsHandler`;
      - `calculatedAtUtc` z `TimeProvider`;
    - `Operations/ListOperationsHandlerTests` — bez zmian, przechodzą po wydzieleniu wspólnej operacji;
    - testy wspólnej operacji odczytu i kolejności (limit malejący między źródłami, `null` przy przekroczeniu).
  - **`StoneFlow.Api.IntegrationTests/Reporting/CashFlowReportApiTests` (SQL Server/LocalDB):**
    - dane przez API modułów Orders i Expenses;
    - uzgodnienie z `/api/operations` (kryterium 2) dla bieżącego miesiąca, roku i zakresu przecinającego datę uruchomienia;
    - nieopłacone zamówienie i wpłata vs raport sprzedaży;
    - wydatek bez daty zapłaty i po oznaczeniu;
    - wydatek ujemny;
    - wpłata i zwrot do zamówienia sprzed uruchomienia;
    - wypłata;
    - korekta sprzedaży pominięta;
    - `401` bez sesji, `400` walidacja.
  - **`StoneFlow.Architecture.Tests`** — bez nowych wyjątków.
  - **Frontend** (`npm run format:check`, `npm run lint`, `npm run typecheck`, `npm test -- --watch=false`):
    - `reports-api` — `cashFlowReport` z parametrami okresu;
    - `DashboardService`:
      - wczytywanie aktywnego widoku przy `enter`, `setView` i `setSelection`;
      - kolejność żądań przy szybkim przełączaniu;
      - linki do listy „Operacje”;
      - `422` i błąd;
    - `dashboard-page` / `cash-flow-view`:
      - stany;
      - trzy karty z porównaniem i „brak podstawy porównania”;
      - linki kart i podziału;
      - tabela danych z „Razem”;
      - ostatnie operacje i „Pokaż wszystkie operacje”;
      - komunikat przed uruchomieniem;
      - zmiana podstawy przy przełączeniu;
      - brak słów „zysk” i „saldo”;
    - `report-text` — etykieta podstawy przepływów;
    - `operations-list-page`:
      - parametry `od`/`do`/`rodzaj` (poprawne, niepoprawne, `od` > `do`, nieznany rodzaj);
      - wejście bez parametrów zachowuje stan;
    - testy słowników po przeniesieniu do `shared/dictionaries`.
- manual: scenariusze testów ręcznych UI w `06-implementation-done.md`:
  - przełączanie widoków i okresów;
  - przejścia z kart, podziału i „Pokaż wszystkie operacje” z porównaniem sum z listą „Operacje”;
  - klawiatura i czytnik ekranu na wykresach;
  - telefon, tablet i szeroki ekran;
  - niekompletna integracja;
  - okres przed uruchomieniem i przecinający datę uruchomienia;
  - wydatek ujemny i wypłata w okresie.
