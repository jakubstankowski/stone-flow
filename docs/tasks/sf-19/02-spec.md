# Task Specification — SF-19

## Source

[SF-19](https://standev.youtrack.cloud/issue/SF-19)

## Goal

Dostarczyć wspólne reguły okresów raportowych i widok „Sprzedaż” na Pulpicie. Reguły okresów obejmują bieżący miesiąc, poprzedni miesiąc, rok, własny zakres, porównanie z okresem bazowym i wyłączenie danych sprzed uruchomienia. Widok pokazuje:

- cztery karty KPI: Sprzedaż, Koszty, Wynik przed wypłatami, Liczba zamówień, z porównaniem;
- wykres sprzedaży, kosztów i wyniku w czasie;
- podział sprzedaży na kanały i kosztów na kategorie;
- zestawienie składników sprzedaży z przejściem do zamówienia.

Wszystkie kwoty, sumy, porównania i podziały liczy backend w module Reporting, z danych modułów Orders i Expenses czytanych wyłącznie przez nowe jawne kontrakty w `StoneFlow.Shared.Application`. Lista wydatków dostaje sumę brutto dla całego filtra, uzgadnialną z kartą „Koszty”.

## Context

- **Backend, moduł Reporting** (schemat `reporting`, bez zmian schematu):
  - `Reporting.Domain`: nowe reguły bez I/O — okresy (`Periods/`), sprzedaż (`Sales/`), koszty (`Costs/`). Wzorzec: `CashFlow/CashFlowPolicy`, `CashFlowTotals` (SF-17).
  - `Reporting.Application/Sales/`: dwa zapytania (raport i zestawienie składników), wzorzec `Operations/ListOperations.cs`.
  - `Reporting.Api/Endpoints/`: `SalesReportEndpoints.cs` i `SalesReportContracts.cs`, mapowane w `ReportingModule.MapReportingEndpoints`.
  - Reporting nie mapuje tabel `orders` ani `expenses` (`DbContextSchemaTests`).
- **Kontrakty międzymodułowe** (wzorzec: `ICashOperationSource` z SF-17):
  - `StoneFlow.Shared.Kernel/Sales/SalesComponentKind.cs` — słownik rodzajów składników sprzedaży, bo używa go `Reporting.Domain`, który może zależeć tylko od Kernel;
  - `StoneFlow.Shared.Application/Sales/` — `ISalesComponentSource` i rekord `SalesComponentRecord`;
  - `StoneFlow.Shared.Application/Costs/` — `ICostSource` i rekord `CostRecord`.
- **Implementacje kontraktów:**
  - `Orders.Infrastructure/Persistence/OrderSalesComponentSource.cs` — zamówienia, zwroty sprzedaży i korekty sprzedaży;
  - `Expenses.Infrastructure/Persistence/ExpenseCostSource.cs` — wydatki według daty kosztu.
- **Moduł Orders, domena:** reguła „status zamówienia pozwala liczyć je do sprzedaży” (`FulfillmentStatus` ≠ `Cancelled` i status ze sklepu spoza `ShopStatuses.HiddenByDefault`) jako jedna metoda statyczna w `Orders.Domain/Orders/` (np. `SalesEligibility.CountsTowardSales(FulfillmentStatus, string? shopStatus)`). Źródło odtwarza ją w predykacie SQL; zgodność sprawdza test (wzór: `ExpenseReader` i `ExpensePayment.Compute`).
- **Moduł Expenses:** `ListExpenses` (Application), `IExpenseReader.ListAsync` i `ExpenseReader` (Infrastructure), `ExpenseListPageResponse` (Api) dostają sumę brutto całego filtra.
- **Data uruchomienia:** `ILaunchDate` (`Shared.Application/LaunchDate`). „Dziś” w Europe/Warsaw z wstrzykniętego `TimeProvider` i `WarsawTimeZone` (`Shared.Application/Scheduling`).
- **Frontend:**
  - `features/dashboard/` zastępuje zaślepkę Pulpitu przełącznikiem widoku, wyborem okresu i widokiem „Sprzedaż”;
  - `features/expenses/` czyta parametry przejścia z Pulpitu i pokazuje sumę listy;
  - `features/integration/incomplete-data-notice.ts` (`app-incomplete-data-notice`, SF-14) jest osadzany bez zmian;
  - `shared/format/` dostaje formatowanie procentu;
  - nowa zależność `chart.js` w `app/package.json` (dokładna wersja).

## Scope

### In scope

- Reguły okresów w `Reporting.Domain/Periods/`, z testami jednostkowymi:
  - rozwiązanie wyboru okresu (bieżący miesiąc, poprzedni miesiąc, rok, własny zakres) na zakres dat w Europe/Warsaw;
  - efektywny początek = max(początek, data uruchomienia) — jedna reguła, którą wykorzystuje także `CashFlowPolicy.EffectiveFrom` (delegacja, bez zmiany API i testów SF-17);
  - okres bazowy porównania i warunek „brak podstawy porównania”;
  - agregacja w czasie: dni albo miesiące, przedziały wykresu.
- Reguły sprzedaży i kosztów w `Reporting.Domain/Sales/` i `Reporting.Domain/Costs/`, z testami jednostkowymi, także dla scenariuszy z PRD §11.
- Kontrakty `ISalesComponentSource` i `ICostSource` oraz ich implementacje w Orders i Expenses.
- Reguła `CountsTowardSales` w `Orders.Domain` z testem jednostkowym.
- Indeks `IX_SalesCorrections_CorrectedOn` (migracja addytywna `OrdersDbContext`).
- `GET /api/reports/sales` — okres, karty z porównaniem, szereg czasowy, kanały, kategorie, czas wyliczenia.
- `GET /api/reports/sales/components` — zestawienie składników sprzedaży z filtrem kanału i rodzaju, sortowaniem, stronicowaniem i sumą dla całego filtra.
- `GET /api/expenses`: nowe pole `grossTotal` (suma brutto całego filtra), zmiana addytywna.
- UI Pulpitu:
  - przełącznik [Sprzedaż / Przepływy pieniężne] — „Przepływy pieniężne” pokazuje zaślepkę do SF-20;
  - wybór okresu;
  - komunikat o niekompletnej integracji i aktualność danych;
  - karty KPI, wykresy Chart.js z alternatywą tekstową i obsługą klawiatury;
  - zestawienie składników.
- Lista wydatków:
  - odczyt parametrów `kosztOd`, `kosztDo`, `kategoria` z przejścia z Pulpitu;
  - wiersz „Suma brutto” dla całego filtra.
- Scenariusze testów ręcznych UI w `06-implementation-done.md`.

### Out of scope

- Widok „Przepływy pieniężne”: karty, wykres, podział wypływów, ostatnie operacje (SF-20). W tym zadaniu jest tylko zaślepka w przełączniku.
- Saldo rachunku, wynik zarządczy, „pozostało po wypłatach”, liczba unikalnych klientów, prognozy (PRD §8, ticket).
- Eksport raportu i zestawienia.
- Zmiany listy „Operacje” (SF-17) poza delegacją `CashFlowPolicy.EffectiveFrom`.
- Przejście z karty „Liczba zamówień” do listy „Zamówienia”. Karta prowadzi do zestawienia z filtrem rodzaju „Zamówienie” (patrz Assumptions).
- Zamykanie okresów (PRD §4: poza MVP; raporty za minione okresy mogą się zmieniać).

## Behavior

### Reguły okresów (`Reporting.Domain/Periods/`)

1. **Wybór okresu** (parametr `period`):

   | Kod | Etykieta | Zakres [`from`, `to`] | Okres bazowy porównania |
   |---|---|---|---|
   | `currentMonth` | Bieżący miesiąc | cały miesiąc kalendarzowy dzisiejszej daty (Europe/Warsaw), np. 01.11–30.11.2026 | cały poprzedni miesiąc |
   | `previousMonth` | Poprzedni miesiąc | cały miesiąc przed bieżącym | cały miesiąc przed nim |
   | `year` | Rok | rok kalendarzowy `year` (domyślnie bieżący); bieżący rok kończy się **dziś**, wcześniejszy 31.12 | bieżący rok: ten sam odcinek poprzedniego roku (01.01 – ten sam dzień i miesiąc, 29.02 → 28.02); wcześniejszy rok: cały poprzedni rok |
   | `custom` | Własny zakres | [`from`, `to`] podane przez użytkownika, `from` ≤ `to` | tyle samo dni tuż przed efektywnym początkiem |

2. **Efektywny początek** = max(`from`, data uruchomienia). Raport, wykres, podziały i zestawienie obejmują wyłącznie zakres [efektywny początek, `to`]. Gdy efektywny początek jest późniejszy niż `from`, widok pokazuje informację „Raport liczony od {data uruchomienia} (data uruchomienia).” — tak samo jak lista „Operacje”.
3. **Okres w całości przed uruchomieniem** (efektywny początek > `to`): API zwraca `beforeLaunch: true` i nie odpytuje źródeł. Karty, wykresy i zestawienie nie pokazują zer. Widok pokazuje komunikat „Wybrany okres jest w całości przed datą uruchomienia ({data}). Raporty obejmują dane od tej daty.” Brak danych ≠ zero (PRD §8, zabezpieczenie 5).
4. **Porównanie** dotyczy każdej karty osobno.
   - Gdy okres bazowy zaczyna się przed datą uruchomienia (`baseFrom` < data uruchomienia), wszystkie karty pokazują „brak podstawy porównania”, a źródła nie są odpytywane dla okresu bazowego.
   - W przeciwnym razie:
     - różnica = wartość bieżąca − wartość bazowa;
     - zmiana procentowa = różnica / |wartość bazowa| × 100, zaokrąglona do 0,1 połową od zera (`MidpointRounding.AwayFromZero`);
     - przy wartości bazowej równej zero karta pokazuje „brak podstawy porównania” zamiast porównania.
   - Wartości bazowe liczą te same reguły co bieżące (pkt 9–13) dla zakresu bazowego.
5. **Opis porównania:**
   - miesiąc w toku: „Miesiąc w toku — porównanie z całym poprzednim miesiącem ({październik 2026}).”;
   - rok w toku: „Rok w toku (dane do {dziś}) — porównanie z 01.01–{ten sam dzień} {rok−1}.”;
   - pozostałe okresy: „Porównanie z: {etykieta okresu bazowego}” — nazwa miesiąca, rok albo zakres dat.
6. **Agregacja wykresu w czasie** (`granularity`):
   - miesiąc (bieżący i poprzedni) → dni;
   - rok → miesiące;
   - własny zakres → dni, gdy efektywny zakres ma ≤ 62 dni, w przeciwnym razie miesiące.

   Przedziały pokrywają cały efektywny zakres bez luk. Przedział miesięczny jest przycięty do efektywnego zakresu (pierwszy i ostatni mogą być niepełne). Bieżący miesiąc ma przedziały do ostatniego dnia miesiąca, także po dzisiejszym; bieżący rok ma przedziały do bieżącego miesiąca włącznie.
7. Wybór roku w UI: lata od roku daty uruchomienia do bieżącego roku. API odrzuca rok późniejszy niż bieżący (`400`). Rok w całości przed uruchomieniem daje `beforeLaunch`.

### Reguły sprzedaży i kosztów (`Reporting.Domain/Sales/`, `Reporting.Domain/Costs/`)

8. Składniki sprzedaży dostarcza `ISalesComponentSource` (Orders) dla zakresu dat:
   - **zamówienie** — data = data zamówienia, kwota = wartość brutto;
   - **zwrot sprzedaży** — tylko zwroty rodzaju „Zwrot sprzedaży”; data i kwota zwrotu;
   - **korekta sprzedaży** — data i kwota korekty.

   Każdy składnik niesie dane swojego zamówienia: id, numer, datę zamówienia, kanał i flagę `OrderCountsTowardSales`. Flaga wynika z bieżącego stanu zamówienia: nie anulowane i status ze sklepu inny niż pending, failed, checkout-draft. Zwroty pieniędzy nie są składnikami sprzedaży i źródło ich nie zwraca, zgodnie z regułą Orders `Refund.SalesReductionOf`. Wpłaty nie są czytane.
9. **Składnik wchodzi do sprzedaży**, gdy spełnia łącznie (`SalesPolicy.Includes`):
   - `OrderCountsTowardSales` = prawda;
   - data zamówienia ≥ data uruchomienia;
   - data składnika w [efektywny początek, `to`].

   Skutki reguły:
   - anulowane, pending, failed, szkice i zamówienia sprzed uruchomienia nie wnoszą wartości, a ich zwroty sprzedaży i korekty nie obniżają sprzedaży;
   - po anulowaniu wypadają wszystkie składniki zamówienia, także z wcześniejszych okresów;
   - po przywróceniu wracają w okresach swoich dat.
10. **Kwota ze znakiem** (`SalesPolicy.SignedAmount`): zamówienie +wartość; zwrot sprzedaży −kwota; korekta sprzedaży −kwota.
11. **Sprzedaż** = suma kwot ze znakiem składników wchodzących do sprzedaży. **Liczba zamówień** = liczba składników rodzaju „zamówienie” wchodzących do sprzedaży; zamówienie ze zwrotem lub korektą nadal się liczy. **Podział na kanały** = ta sama suma pogrupowana po kanale zamówienia składnika. Zwrot i korekta pomniejszają kanał swojego zamówienia w okresie swojej daty, więc suma kanałów = sprzedaż. Lista kanałów jest pełna, w kolejności słownika (`ISalesComponentSource.Channels`); kanał bez składników ma `0.00`.
12. **Koszty** dostarcza `ICostSource` (Expenses): wszystkie wydatki z datą kosztu w [efektywny początek, `to`], opłacone i nieopłacone (także wystąpienia cykliczne), z kwotą brutto ze znakiem i kategorią. **Koszty** = suma kwot brutto; wydatek ujemny je pomniejsza. **Podział na kategorie** = ta sama suma pogrupowana po kategorii. Lista jest pełna, w kolejności słownika (`ICostSource.Categories`), także VAT i PPE; kategoria bez wydatków ma `0.00`. Wypłaty właścicieli nie są wydatkami i nie są czytane.
13. **Wynik przed wypłatami** = sprzedaż − koszty w tym samym okresie. Wypłaty właścicieli go nie obniżają.
14. **Szereg czasowy:** dla każdego przedziału (pkt 6) sprzedaż, koszty i wynik liczone tymi samymi regułami. Suma przedziałów = wartości kart (test).
15. **Kwoty i zaokrąglenia:**
    - wszystkie sumy są sumami zapisanych kwot `decimal(18,2)`, bez ponownego zaokrąglania (KWO-01);
    - zaokrągla się wyłącznie procent porównania (pkt 4);
    - w backendzie nie ma typów zmiennoprzecinkowych.

### Zestawienie składników sprzedaży

16. Zestawienie pokazuje składniki wchodzące do sprzedaży (pkt 9) w zakresie [`from`, `to`] przekazanym z odpowiedzi raportu (rozwiązany okres). Jeden wiersz to jeden składnik:
    - **Data**;
    - **Rodzaj** — „Zamówienie”, „Zwrot sprzedaży”, „Korekta sprzedaży”;
    - **Zamówienie** — numer, link do `/zamowienia/{orderId}`;
    - **Kanał**;
    - **Kwota** — ze znakiem, np. `−300,00 zł` dla zwrotu.
17. Filtry:
    - **Kanał** — jeden kanał albo wszystkie;
    - **Rodzaj** — jeden rodzaj albo wszystkie.

    **Suma zestawienia** obejmuje cały filtr (nie stronę). Bez filtrów jest równa karcie „Sprzedaż”, z filtrem kanału — wartości kanału, z filtrem rodzaju „Zamówienie” liczba pozycji (`total`) jest równa karcie „Liczba zamówień”.
18. Sortowanie po dacie, domyślnie malejąco, z możliwością rosnącego. Remis rozstrzyga rodzaj (zamówienie, zwrot sprzedaży, korekta sprzedaży), potem id. Stronicowanie: 25 na stronę, najwyżej 100.
19. Etykieta pod sumą: „Kwoty brutto w PLN. Zamówienia według daty zamówienia, zwroty sprzedaży i korekty sprzedaży według ich dat.”

### Przejścia (karty, kanały, kategorie)

20. Karta **„Sprzedaż”** przewija do zestawienia i ustawia na nim fokus, czyszcząc filtry zestawienia.
21. Karta **„Koszty”** prowadzi do `/wydatki?kosztOd={efektywny początek}&kosztDo={to}`. Lista wydatków ustawia filtr daty kosztu, czyści pozostałe filtry, a jej „Suma brutto” jest równa karcie przy tym samym okresie.
22. Wiersz kategorii prowadzi do `/wydatki?kosztOd=…&kosztDo=…&kategoria={kod}`. Suma listy jest równa wartości kategorii.
23. Wiersz kanału ustawia filtr kanału zestawienia na tej samej stronie i przewija do zestawienia. Suma zestawienia jest równa wartości kanału.
24. Karta **„Liczba zamówień”** ustawia filtr rodzaju „Zamówienie” w zestawieniu i przewija do niego.
25. Karta **„Wynik przed wypłatami”** nie jest klikalna: jej opis wymienia składniki.

### Lista wydatków — suma i parametry

26. `GET /api/expenses` zwraca `grossTotal`: sumę `GrossAmount` wszystkich wydatków spełniających filtry (SQL `SUM`, nie strona), `"0.00"` dla pustego wyniku.
27. Lista pokazuje nad tabelą „Suma brutto: {kwota}” z etykietą „Suma kwot brutto wydatków spełniających filtry; korekty i zwroty od dostawców ją pomniejszają.” Ujemna suma ma znak minus.
28. Parametry `kosztOd`, `kosztDo` i `kategoria` (powtarzalny) są odczytywane raz przy wejściu na `/wydatki`, jak `wydatekCykliczny` (SF-16). Ustawiają filtr daty kosztu i kategorii, a pozostałe filtry czyszczą. Niepoprawne wartości są pomijane (lista z domyślnymi filtrami).

### Pulpit

29. `/pulpit` pokazuje przełącznik widoku [Sprzedaż / Przepływy pieniężne], domyślnie „Sprzedaż”. „Przepływy pieniężne” pokazuje stan pusty „Widok przepływów pieniężnych powstanie w kolejnym etapie. Operacje pieniężne są na liście „Operacje”.” z linkiem do `/operacje`.
30. Zmiana okresu wczytuje raport ponownie: karty, wykresy i podziały z jednego żądania; zestawienie z drugiego, z zakresem z odpowiedzi raportu, od strony 1, bez filtrów. Wybór widoku, okresu i stan zestawienia przetrwa przejście do zamówienia lub wydatku i powrót: stan w serwisie z sygnałami, jak `ExpensesService`.
31. **Aktualność danych:**
    - „Stan na {czas wyliczenia, Europe/Warsaw}” z `calculatedAtUtc`;
    - komponent `app-incomplete-data-notice`, który zawsze pokazuje „Ostatnia pełna kontrola”, a przy niekompletnej integracji ostrzeżenie „Nie pobrano wszystkich zamówień. Wyniki mogą być niepełne.” z przejściem do integracji.

    Pulpit nie odpytuje API w tle.

## Data / API

### Storage

- **Orders** (`OrdersDbContext`), migracja addytywna: `IX_SalesCorrections_CorrectedOn` na `CorrectedOn` (INCLUDE `Amount`, `OrderId`). Pozostałe zapytania korzystają z istniejących indeksów `Orders (OrderDate, Id)` i `IX_Refunds_RefundedOn`.
- **Expenses:** brak zmian; `IX_Expenses_CostDate (CostDate, Id)` istnieje.
- **Reporting:** brak zmian schematu.

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/reports/sales` | `period` (`currentMonth` \| `previousMonth` \| `year` \| `custom`, domyślnie `currentMonth`), `year` (`yyyy`, przy `year`; domyślnie bieżący), `from`, `to` (`yyyy-MM-dd`, wymagane przy `custom`) | `200` raport (niżej); `400` walidacja; `422 report_range_too_large` | sesja | Reporting |
| GET | `/api/reports/sales/components` | `from`, `to` (`yyyy-MM-dd`, wymagane), `channel` (kod), `kind` (`order` \| `salesReturn` \| `salesCorrection`), `direction` (`desc` \| `asc`), `page`, `pageSize` | `200 { items, page, pageSize, total, sum, effectiveFrom, launchDate }`; `400`; `422 report_range_too_large` | sesja | Reporting |
| GET | `/api/expenses` (zmiana) | bez zmian | dotychczasowe pola + `grossTotal` | sesja | Expenses |

**Odpowiedź `GET /api/reports/sales`:**

```
{
  period: { kind, from, to, effectiveFrom, launchDate, beforeLaunch, inProgress, granularity: "day"|"month" },
  comparison: { from, to, available, reason: "beforeLaunch"|null },
  kpis: {                                   // null, gdy beforeLaunch
    sales:      { value, comparison },
    costs:      { value, comparison },
    result:     { value, comparison },
    orderCount: { value, comparison }       // value: liczba całkowita
  },
  series:     [ { from, to, sales, costs, result } ],   // [] gdy beforeLaunch
  channels:   [ { channel, amount } ],                  // pełny słownik, [] gdy beforeLaunch
  categories: [ { category, amount } ],                 // pełny słownik, [] gdy beforeLaunch
  calculatedAtUtc
}
comparison karty: { status: "available"|"noBasis", base, difference, percent }   // base/difference/percent null przy noBasis
```

- `inProgress` = prawda dla bieżącego miesiąca i bieżącego roku (opis porównania, pkt 5).
- `percent` jest tekstem z jednym miejscem po kropce (`"12.5"`, `"-3.0"`).
- `base` i `difference` dla `orderCount` są liczbami całkowitymi, dla kwot — tekstem.

**Element `items` zestawienia:** `{ kind, id, date, amount, orderId, orderNumber, channel }`. `amount` jest kwotą ze znakiem (pkt 10). Bez danych klienta.

**Format danych:** kwoty w JSON są tekstem z kropką i opcjonalnym minusem (`"-1234.50"`), daty `yyyy-MM-dd`, czas `calculatedAtUtc` w ISO 8601 UTC, kanały i kategorie jako kody (`shop`, `olx`, …; `materialPurchase`, …) zgodne z `OrderCodes` i `ExpenseCodes`.

**Walidacja** (handler, `FieldErrors` → `400`):

- `period` ze słownika;
- `year`: liczba 4-cyfrowa ≤ bieżący rok (Europe/Warsaw);
- `from`, `to`: poprawne daty, `from` ≤ `to` (błąd przy `from`);
- `channel` i `kind` ze słownika;
- `direction` `asc`/`desc`;
- `page` ≥ 1, `pageSize` 1–100.

Parametry niepotrzebne dla danego `period` są ignorowane.

**Bezpiecznik** (jak SF-17): każde źródło jest wywoływane z limitem 10 001 wierszy (SQL `TOP`). Gdy zakres zwraca więcej niż 10 000 składników sprzedaży albo 10 000 wydatków, endpoint odpowiada `422 report_range_too_large` — „Za dużo danych w wybranym okresie. Wybierz krótszy okres.”

### Types crossing a boundary

W `StoneFlow.Shared.Kernel/Sales/`:

- `SalesComponentKind { Order, SalesReturn, SalesCorrection }` — kolejność deklaracji to stała kolejność remisu przy sortowaniu.

W `StoneFlow.Shared.Application/Sales/`:

- `SalesComponentRecord` (record):
  - `Kind`, `Id`, `Date` (DateOnly);
  - `Amount` (decimal, kwota zapisana, dodatnia; znak nadaje Reporting);
  - `OrderId`, `OrderNumber`, `OrderDate`;
  - `Channel` (string, kod);
  - `OrderCountsTowardSales` (bool).
- `ISalesComponentSource`:
  - `IReadOnlyList<string> Channels` — kody kanałów w kolejności prezentacji;
  - `Task<IReadOnlyList<SalesComponentRecord>> ListAsync(DateOnly from, DateOnly to, int limit, CancellationToken)` — zamówienia z datą zamówienia w zakresie, zwroty sprzedaży i korekty z datą w zakresie; łącznie najwyżej `limit` wierszy, kolejno tabelami jak w `OrderCashOperationSource`. Zwraca także składniki zamówień nieliczonych do sprzedaży (z flagą `false`), żeby regułę wyłączenia stosował i testował Reporting.

W `StoneFlow.Shared.Application/Costs/`:

- `CostRecord(Guid Id, DateOnly CostDate, decimal GrossAmount, string Category)`.
- `ICostSource`:
  - `IReadOnlyList<string> Categories` — kody kategorii w kolejności prezentacji;
  - `Task<IReadOnlyList<CostRecord>> ListAsync(DateOnly from, DateOnly to, int limit, CancellationToken)`.

Rejestracja, projekcja i skala:

- Implementacje są rejestrowane jako pojedyncze usługi scoped w `DependencyInjection.cs` modułów-właścicieli.
- Każde zapytanie jest projekcją w SQL (`AsNoTracking`, `Select`) z filtrem zakresu dat. Encje nie wychodzą poza Infrastructure.
- Reporting wywołuje źródła po kolei (wspólne `ModuleConnection`, bez `Task.WhenAll`): sprzedaż, koszty, a przy dostępnym porównaniu sprzedaż i koszty okresu bazowego.
- Łączenie, filtrowanie, sumy, sortowanie i stronicowanie zestawienia odbywają się w pamięci Reporting. To odstępstwo od reguły „stronicowanie w bazie” (conventions), uzasadnione tak samo jak w SF-17: źródła leżą w różnych modułach, a skala z D-08 to około 20–30 zamówień miesięcznie.

W `Reporting.Domain` (bez I/O, testy jednostkowe):

- `Periods/LaunchCutoff.EffectiveFrom(from, launchDate)`; `CashFlowPolicy.EffectiveFrom` deleguje do niej.
- `Periods/ReportPeriod`:
  - `Resolve(kind, year, from, to, today, launchDate)` → zakres, efektywny początek, `BeforeLaunch`, `InProgress`, `Granularity`, okres bazowy i jego dostępność;
  - `Buckets()` → przedziały wykresu.
- `Periods/Comparison.Of(current, base)` → `Available` (różnica, procent zaokrąglony połową od zera do 0,1) albo `NoBasis` (baza = 0); wariant dla liczby całkowitej.
- `Sales/SalesComponent` — fakt, na który handler mapuje `SalesComponentRecord`.
- `Sales/SalesPolicy.Includes(component, effectiveFrom, to, launchDate)` i `SignedAmount(component)`.
- `Sales/SalesSummary.Of(components, period, channels)` → sprzedaż, liczba zamówień, kanały, wartości w przedziałach.
- `Costs/CostSummary.Of(costs, period, categories)` → koszty, kategorie, wartości w przedziałach.
- `Sales/SalesResult` (wynik przed wypłatami = sprzedaż − koszty) dla karty i przedziałów.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sales separate from cash | Sprzedaż liczy się z wartości zamówień, zwrotów sprzedaży i korekt według ich dat. Wpłaty nie są czytane, zwroty pieniędzy nie są składnikami, a koszty obejmują wydatki według daty kosztu niezależnie od zapłaty. Wypłaty właścicieli nie wchodzą do kosztów ani wyniku. Widok przepływów jest osobny (SF-20). |
| 2. Backend decimal money | Wszystkie sumy, wynik, podziały, porównania i suma listy wydatków liczy backend na `decimal`; procent zaokrągla `MidpointRounding.AwayFromZero`. Frontend wyświetla tekst z API przez `shared/format`. Chart.js dostaje liczby wyłącznie do geometrii słupków i linii: etykiety, tooltipy, tabele i karty pokazują sformatowane ciągi z API, a frontend niczego nie sumuje. |
| 3. API-enforced access | Nowe endpointy są `GET` pod domyślną polityką sesji. Testy integracyjne: `401` bez sesji dla obu endpointów raportu. |
| 4. Retries never duplicate | Nie dotyczy (tylko odczyty). |
| 5. Financial audit trail | Nie dotyczy (brak zmian danych finansowych). |
| 6. Secrets stay server-side | Brak nowych sekretów; `chart.js` z npm, bez CDN. |
| 7. Module and layer boundaries | Reporting czyta Orders i Expenses wyłącznie przez `ISalesComponentSource` i `ICostSource` z `Shared.Application`. Implementacje leżą w Infrastructure modułów-właścicieli, a reguła statusu sprzedaży w `Orders.Domain`. `Reporting.Domain` zależy tylko od `Shared.Kernel`. Testy architektury przechodzą bez nowych wyjątków. |
| 8. No identifying data in docs | Zestawienie i kontrakty niosą numer zamówienia, bez danych klienta; przykłady w dokumentach są fikcyjne. |

## Risk

**high** — pieniądze: definicje KPI sprzedaży, kosztów i wyniku, od których zależy ocena działalności (FIN-01–06). Pomyłka w regule wyłączeń (anulowanie, statusy sklepu, przełom uruchomienia) daje błędny wynik bez widocznego błędu. Wymaga przeglądu przez człowieka przed merge.

## UI

**Trasa:** `/pulpit` (istniejąca, `loadComponent`). Chart.js jest importowany tylko w fragmencie Pulpitu, więc budżet `initial` się nie zmienia.

**Układ** (design guide §4, §9):

1. Nagłówek strony „Pulpit”, przełącznik widoku (grupa `radio` w `fieldset` z legendą „Widok”, z widocznym zaznaczeniem i obsługą strzałek) i wybór okresu:
   - select „Okres”: Bieżący miesiąc, Poprzedni miesiąc, Rok, Własny zakres;
   - przy „Rok” select „Rok”;
   - przy „Własny zakres” pola „Data od” i „Data do” z walidacją `od` ≤ `do` w UI (komunikat przy polu, bez żądania).
2. Pod wyborem: etykieta okresu („listopad 2026, 01.11–30.11.2026”), informacja o dacie uruchomienia (pkt 2), opis porównania (pkt 5), „Stan na …” i `app-incomplete-data-notice`.
3. Ogólna etykieta podstawy: „Kwoty brutto w PLN. Raporty obejmują dane od {data uruchomienia}.”
4. Cztery karty KPI: 4 w rzędzie od 1200 px, 2×2 w zakresie 768–1199 px, jedna kolumna poniżej 768 px. Każda karta ma:
   - ikonę na stonowanym tle (`aria-hidden`);
   - etykietę i dużą wartość (`tabular-nums`);
   - drobny opis podstawy;
   - porównanie: tekst ze strzałką i słowem, np. „+12,5% (+1 234,00 zł) więcej niż w: październik 2026”, albo „brak podstawy porównania”. Kolor neutralny, bez zieleni i czerwieni dla wzrostu/spadku (design guide §4).

   Opisy podstaw:

   | Karta | Opis |
   |---|---|
   | Sprzedaż | „Wartość brutto zamówień według daty zamówienia minus zwroty sprzedaży i korekty sprzedaży według ich dat. Niezależna od wpłat.” |
   | Koszty | „Wydatki brutto, opłacone i nieopłacone, według daty kosztu. Ujemne wydatki pomniejszają koszty.” |
   | Wynik przed wypłatami | „Sprzedaż minus koszty w tym samym okresie. Wypłaty właścicieli go nie obniżają.” |
   | Liczba zamówień | „Zamówienia wchodzące do sprzedaży według daty zamówienia. Zamówienie ze zwrotem lub korektą nadal się liczy.” |

   Karty klikalne (Sprzedaż, Koszty, Liczba zamówień) są linkiem albo przyciskiem z widoczną wskazówką („Pokaż składniki”, „Pokaż wydatki”, „Pokaż zamówienia”). Wynik nie jest klikalny.
5. Wiersz wykresów — od 1200 px obok siebie (wykres w czasie szerszy), poniżej jeden pod drugim:
   - **„Sprzedaż i koszty w czasie”**: słupki „Sprzedaż” (`--sf-accent`) i „Koszty” (`--sf-text`) oraz linia „Wynik przed wypłatami” (przerywana, ze znacznikami, `--sf-info`). Oś Y od zera, z obsługą wartości ujemnych, opis osi „zł”, oś X — dni („01.11”) albo miesiące („sty 2026”). Legenda i tytuł z okresem.
   - **„Sprzedaż według kanałów”**: poziome słupki (`--sf-accent`) i lista kanałów z kwotą. Wiersz jest przyciskiem filtra zestawienia (pkt 23).
   - **„Koszty według kategorii”**: poziome słupki (`--sf-text`) i lista kategorii z kwotą. Wiersz jest linkiem do wydatków (pkt 22).
6. **Zestawienie składników sprzedaży** (`app-data-table`, nagłówek z `id` do przewinięcia i fokusu):
   - filtry „Kanał” i „Rodzaj” jako aktywne chipy z „Wyczyść filtry”;
   - wiersz „Suma zestawienia: {kwota}” z etykietą (pkt 19);
   - kolumny Data, Rodzaj, Zamówienie (link), Kanał, Kwota (do prawej, `tabular-nums`, znak `−` U+2212);
   - sortowanie tylko po dacie.

**Wykresy i dostępność (design guide §7):**

- Biblioteka `chart.js` w dokładnie przypiętej wersji (bez `^`/`~`; najnowsza stabilna starsza niż 2 tygodnie, obecnie `4.6.1`). Rejestrowane są tylko używane kontrolery i elementy (tree-shaking).
- Kolory pochodzą z tokenów CSS odczytanych przez `getComputedStyle` (`--sf-accent`, `--sf-text`, `--sf-info`), bez literałów kolorów w `app/src/app` (`lint:colors`). Serie różnią się także kształtem: słupek albo linia przerywana ze znacznikami.
- Każdy wykres ma tytuł, okres, jednostkę i legendę oraz tekstową alternatywę:
  - wykres w czasie — przycisk „Pokaż dane w tabeli” (`aria-expanded`) odsłania tabelę Okres / Sprzedaż / Koszty / Wynik przed wypłatami z wierszem „Razem” z wartości kart;
  - kanały i kategorie — lista z kwotami zawsze widoczna.
- Canvas ma `role="img"`, `aria-label` z tytułem i wskazaniem tabeli oraz `tabindex="0"`. Strzałki lewo/prawo (góra/dół dla poziomych) przesuwają aktywny punkt i pokazują tooltip, Home/End skaczą na skraj, Escape ukrywa tooltip. Wartość aktywnego punktu jest ogłaszana przez region `aria-live="polite"`. Tooltip pokazuje ciągi z API sformatowane `formatPln`.
- Przy `prefers-reduced-motion: reduce` animacje są wyłączone. Widoczny fokus zgodny z design guide §9.
- Tworzenie wykresu jest zamknięte w małym adapterze (komponent w `features/dashboard/`), aby testy w jsdom (bez canvas) mogły go zastąpić.

**Stany:**

- wczytywanie: `app-loading-state` / szkielet o docelowych rozmiarach, bez fikcyjnych zer;
- błąd raportu: `app-error-state` z „Spróbuj ponownie”;
- błąd zestawienia: osobny stan błędu tabeli;
- `422`: komunikat z API przy wyborze okresu;
- okres przed uruchomieniem: komunikat z pkt 3 zamiast kart;
- puste zestawienie: „Brak składników sprzedaży w wybranym okresie”, a przy filtrze — „Brak składników dla wybranych filtrów”;
- wartości zerowe: `0,00 zł` i `0` (rzeczywiste zero po dacie uruchomienia).

**Formaty:** `formatPln`, `formatDate`, `formatMonth`, `formatDateTime` oraz nowa funkcja `formatPercent` w `shared/format/` (`"12.5"` → `+12,5%`, `"-3.0"` → `−3,0%`), bez arytmetyki.

**Lista wydatków:** wiersz „Suma brutto” nad tabelą (`aria-live="polite"`); filtry z parametrów widoczne jako chipy.

## Edge Cases

Okres i data uruchomienia:

- Data uruchomienia w środku bieżącego miesiąca: efektywny początek = data uruchomienia, z informacją pod wyborem; porównanie — „brak podstawy porównania” (baza zaczyna się przed uruchomieniem).
- Miesiąc po miesiącu uruchomienia: baza (miesiąc uruchomienia) zaczyna się przed datą uruchomienia → „brak podstawy porównania” (PRD §8, dosłownie).
- Rok uruchomienia i rok po nim: baza zaczyna się przed uruchomieniem → „brak podstawy porównania”.
- Własny zakres zaczynający się przed uruchomieniem: przycięty, z informacją; baza → „brak podstawy porównania”.
- Własny zakres w całości przed uruchomieniem albo rok przed uruchomieniem → `beforeLaunch`, komunikat, bez zer.
- Własny zakres 62 dni → dni; 63 dni → miesiące.
- Luty w roku przestępnym; 31-dniowy miesiąc porównywany z 30-dniowym (cały miesiąc z całym miesiącem).
- `year` > bieżący rok → `400`; `custom` bez `to` → `400`; `from` > `to` → `400` przy `from`.

Sprzedaż:

- Zamówienie z października 1000 zł i zwrot sprzedaży 300 zł z listopada: październik 1000, listopad −300 (sprzedaż listopada może być ujemna, wynik i kanał też). Liczba zamówień w październiku 1, w listopadzie 0.
- Korekta sprzedaży do zamówienia z wcześniejszego miesiąca: widoczna w zestawieniu i obniża sprzedaż w okresie daty korekty.
- Zwrot pieniędzy (nadpłata 200 zł przy zamówieniu 1000 zł): sprzedaż 1000 zł, zwrotu brak w zestawieniu.
- Anulowanie zamówienia z wcześniejszego miesiąca z wcześniejszym zwrotem sprzedaży: znikają zamówienie i zwrot, zmieniają się raporty obu okresów (PRD §5). Przywrócenie przywraca oba.
- Zamówienie ze sklepu `pending` / `failed` / `checkout-draft`: poza sprzedażą i liczbą zamówień, jego zwroty i korekty też. Po zmianie statusu w sklepie na `processing` wchodzi w okresie daty zamówienia.
- Status ze sklepu `refunded` lub `on-hold`, zamówienie nieanulowane w ERP: liczy się do sprzedaży (zwrot w sklepie to tylko informacja).
- Zamówienie anulowane w sklepie i przywrócone przez użytkownika w ERP: liczy się (decyduje status realizacji ERP).
- Zamówienie z datą sprzed uruchomienia i zwrotem sprzedaży po uruchomieniu: zwrot nie obniża sprzedaży (URU-01).
- Zamówienie o wartości 0 zł: liczy się do liczby zamówień, wnosi 0,00 zł.
- Korekta wartości zamówienia (ręczna lub z synchronizacji): zmienia sprzedaż w okresie daty zamówienia przy następnym odczycie.
- Obniżenia sprzedaży przekraczające wartość po synchronizacji (ostrzeżenie SF-10): raport liczy zapisane kwoty bez przycinania.

Koszty i wynik:

- Wydatek nieopłacony i wystąpienie cykliczne: w kosztach według daty kosztu.
- Wydatek z datą kosztu w przyszłości, w bieżącym miesiącu: w kosztach bieżącego miesiąca, ale nie w bieżącym roku, dopóki ta data nie minie (rok kończy się dziś — Assumptions).
- Wydatek ujemny −200 zł: koszty i kategoria pomniejszone; suma kategorii może być ujemna.
- Wydatek z datą kosztu sprzed uruchomienia opłacony po uruchomieniu: nie ma go w kosztach (URU-01).
- Wypłata właściciela: nie zmienia kosztów ani wyniku.

Porównanie i zestawienie:

- Wartość bazowa 0 (np. brak kosztów w poprzednim miesiącu) → „brak podstawy porównania” tylko dla tej karty.
- Wynik bazowy ujemny (−1000) i bieżący −500 → +50,0% (dzielnik |baza|), tekst „więcej”.
- Zmiana procentowa `x,x5` → zaokrąglenie od zera (0,05% → 0,1%; −0,05% → −0,1%).
- Strona zestawienia poza zakresem → pusta lista, `total` i `sum` dla całego filtra.
- Więcej niż 10 000 składników lub wydatków w okresie → `422 report_range_too_large`.
- Lista wydatków z parametrami z Pulpitu i dodatkowym filtrem ustawionym później przez użytkownika: suma dotyczy bieżącego filtra (może różnić się od karty).

## Dependencies

- SF-10 (korekty sprzedaży, limit obniżeń) — w main.
- SF-12 (statusy ze sklepu, anulowanie przez synchronizację) — w main.
- SF-14 (stan integracji, `app-incomplete-data-notice`) — w main.
- SF-16 (wystąpienia cykliczne jako wydatki) — w main.
- SF-17 (`ICashOperationSource`, `CashFlowPolicy`, wzorzec Reporting) — w main.
- SF-18 (Historia zmian) nie jest wymagane.

## Assumptions

- **Pytanie 1 (odpowiedź użytkownika 09.11.2026):** karta „Koszty” prowadzi do listy „Wydatki” z filtrem daty kosztu równym efektywnemu okresowi raportu. Lista wydatków dostaje sumę brutto dla całego filtra liczoną w backendzie (nowe pole `grossTotal`), równą karcie przy tym samym okresie.
- **Pytanie 2 (odpowiedź 09.11.2026):** bieżący miesiąc = cały miesiąc kalendarzowy (np. 01–30.11), porównywany z całym poprzednim miesiącem.
- **Asymetria (bez nowego pytania, zgodnie z PRD §8):** rok bieżący obejmuje dane do dziś, a bieżący miesiąc — cały miesiąc. Wpisy z datą w przyszłości (data zamówienia i data kosztu mogą być późniejsze niż dziś) liczą się w bieżącym miesiącu, a w bieżącym roku dopiero od swojej daty.
- **Porównanie roku (decyzja wykonawcy z 09.11.2026, do potwierdzenia przez właściciela w PR):** bieżący rok (do dziś) porównuje się z tym samym odcinkiem poprzedniego roku (01.01 – ten sam dzień), bo porównanie niepełnego roku z pełnym zawsze wychodziłoby na minus; zakończony rok porównuje się z całym poprzednim rokiem. Opis porównania podaje oba zakresy (pkt 5).
- **Pytanie 3 (odpowiedź 09.11.2026):** cztery karty KPI: Sprzedaż, Koszty, Wynik przed wypłatami, Liczba zamówień — 4 w rzędzie na szerokim ekranie, 2×2 na tablecie, 1 kolumna na telefonie.
- **Pytanie 4 (odpowiedź 09.11.2026):** wykresy w Chart.js w dokładnie przypiętej wersji, z obowiązkową alternatywą tekstową lub tabelą i obsługą klawiatury (design guide §7).
- Własny zakres zaczynający się przed datą uruchomienia jest przycinany do daty uruchomienia z informacją: efektywny początek = max(from, data uruchomienia), jak na liście „Operacje” (SF-17). Porównanie z bazą sprzed uruchomienia albo równą zero → „brak podstawy porównania”.
- Okres w całości przed uruchomieniem pokazuje komunikat zamiast zer (brak danych ≠ zero). To odstępstwo od listy „Operacje”, która w tym przypadku pokazuje sumy 0,00 zł.
- Procent porównania: dzielnik |baza| (przy ujemnym wyniku bazowym), zaokrąglenie do 0,1 połową od zera. Karta pokazuje też różnicę w zł albo w sztukach. Przy „brak podstawy porównania” karta nie pokazuje ani różnicy, ani procentu.
- Kategoria kosztów prowadzi do listy „Wydatki” z filtrem kategorii i daty kosztu. Lista ma już filtr kategorii (wielokrotny), więc dochodzi tylko odczyt parametrów `kosztOd`, `kosztDo`, `kategoria`.
- Kanał ustawia filtr zestawienia składników na tej samej stronie; suma po filtrze kanału = wartość kanału.
- Karta „Liczba zamówień” prowadzi do zestawienia z filtrem rodzaju „Zamówienie”. Filtr `kind` to dodatek spoza ticketu, potrzebny do prześledzenia wartości karty (ZAM-07, design guide §4). Lista „Zamówienia” nie byłaby z kartą równoważna (pokazuje anulowane i zamówienia sprzed uruchomienia).
- Zestawienie składników jest stronicowane w API; suma dla całego okresu i filtra pochodzi z backendu i bez filtrów jest równa karcie „Sprzedaż”.
- Agregacja wykresu: miesiąc → dni, rok → miesiące, własny zakres ≤ 62 dni → dni, dłuższy → miesiące.
- Widok żyje na Pulpicie (`/pulpit`) z przełącznikiem [Sprzedaż / Przepływy pieniężne]. „Przepływy pieniężne” w tym zadaniu pokazuje zaślepkę z linkiem do „Operacje” (SF-20 ją zastąpi).
- Komunikat o niekompletnej integracji i „Ostatnia pełna kontrola” pochodzą z istniejącego `app-incomplete-data-notice` (SF-14). Aktualność wyliczenia to `calculatedAtUtc`.
- Reguła „status pozwala liczyć zamówienie do sprzedaży” należy do Orders (zna statusy sklepu). Przełom uruchomienia, okresy, znaki i sumy należą do Reporting. Źródło zwraca także składniki zamówień wyłączonych (z flagą), aby wyłączenia testować jednostkowo w Reporting.
- Klasyfikacja zwrotu (sprzedaży albo pieniędzy) należy do Orders (`Refund.SalesReductionOf`). Źródło zwraca tylko zwroty sprzedaży, a scenariusz „zwrot nadpłaty” pokrywa test integracyjny źródła i endpointu.
- Łączenie i stronicowanie w pamięci Reporting jest uzasadnione skalą (D-08); bezpiecznik 10 000 wierszy na źródło jest egzekwowany limitem SQL, jak w SF-17.
- Kolejność kanałów i kategorii w odpowiedzi to kolejność słowników modułów-właścicieli (`Channels`, `Categories` w kontraktach).
- Ryzyko pozostaje **high**.

## Acceptance Criteria

- [ ] Suma zestawienia składników sprzedaży równa się karcie „Sprzedaż” przy tym samym okresie, niezależnie od strony i sortowania (test integracyjny i jednostkowy).
- [ ] Korekta sprzedaży z listopada do zamówienia z października jest w zestawieniu listopada i obniża sprzedaż listopada; październik się nie zmienia.
- [ ] Suma kanałów równa się sprzedaży; suma zestawienia z filtrem kanału równa się wartości kanału.
- [ ] Zamówienia anulowane, pending, failed, szkice i sprzed uruchomienia nie liczą się do sprzedaży ani do liczby zamówień; ich zwroty sprzedaży i korekty nie obniżają sprzedaży.
- [ ] Zwrot pieniędzy nie obniża sprzedaży: zwrot nadpłaty 200 zł przy zamówieniu 1000 zł pozostawia sprzedaż 1000 zł.
- [ ] Po anulowaniu zwroty sprzedaży i korekty zamówienia nie obniżają sprzedaży, a po „Przywróć zamówienie” znów ją obniżają w okresach swoich dat.
- [ ] Koszty obejmują opłacone i nieopłacone wydatki (także wystąpienia cykliczne) według daty kosztu, a wydatek ujemny je pomniejsza. Suma listy „Wydatki” otwartej z karty „Koszty” równa się karcie.
- [ ] Przy bazie równej zero albo zaczynającej się przed datą uruchomienia karta pokazuje „brak podstawy porównania”.
- [ ] Wypłata właściciela nie zmienia kosztów ani wyniku przed wypłatami.
- [ ] Wynik przed wypłatami = sprzedaż − koszty; suma przedziałów wykresu równa się kartom.
- [ ] Bieżący miesiąc obejmuje cały miesiąc kalendarzowy i porównuje się z całym poprzednim; rok bieżący kończy się dziś; okres przed uruchomieniem pokazuje komunikat, nie zera.
- [ ] Każdy wykres ma tekstową alternatywę (tabela lub lista), a tooltip jest dostępny klawiaturą.
- [ ] Widok pokazuje podstawę kwot, daty przypisania, „Stan na …” i „Ostatnia pełna kontrola”, a przy niekompletnej integracji ostrzeżenie.
- [ ] Reguły okresów, porównania, sprzedaży i kosztów mają testy jednostkowe, także dla scenariuszy PRD §11 (niżej).
- [ ] Endpointy raportu odrzucają żądania bez sesji (`401`); testy architektury przechodzą bez nowych wyjątków.

## Verification

- build:
  - `dotnet restore stone-flow.slnx --locked-mode`;
  - `dotnet format stone-flow.slnx --verify-no-changes --no-restore`;
  - `dotnet build stone-flow.slnx -c Release --no-restore`;
  - frontend `npm ci` (lockfile z `chart.js`), `npm run build`.
- tests:
  - **`StoneFlow.Reporting.Tests` (jednostkowe):**
    - `Periods/ReportPeriodTests` — cztery rodzaje okresu, cały bieżący miesiąc, rok do dziś i rok miniony, efektywny początek, `beforeLaunch`, okres bazowy i jego dostępność (miesiąc uruchomienia, miesiąc po nim, rok po nim), granulacja 62/63 dni, przedziały miesięczne przycięte, luty przestępny;
    - `Periods/ComparisonTests` — baza 0, baza ujemna, zaokrąglenie połową od zera (±0,05% → ±0,1%), liczby całkowite;
    - `CashFlowPolicyTests` — bez zmian (delegacja);
    - `Sales/SalesSummaryTests` — scenariusze PRD §11 na faktach składników:
      1. niezapłacone zamówienie → w sprzedaży;
      2. wpłata w innym miesiącu → sprzedaż tylko w miesiącu daty zamówienia;
      3. korekta wartości → nowa wartość w okresie daty zamówienia;
      4. zwrot sprzedaży w kolejnym miesiącu → obniża ten miesiąc i kanał zamówienia;
      5. zwrot nadpłaty → brak składnika, sprzedaż 1000 zł;
      6. korekta sprzedaży 500 zł przy zamówieniu 1000 zł z zaliczką 300 zł → −500 zł w okresie korekty;
      7. anulowanie opłaconego zamówienia ze zwrotem → 0 zł i 0 zamówień, po przywróceniu znów liczone;
      8. zamówienie otwarte w dniu uruchomienia (data sprzed uruchomienia) i jego zwrot po uruchomieniu → wyłączone;
      9. pending/failed/szkic → wyłączone;
      10. suma kanałów = sprzedaż; zamówienie ze zwrotem nadal liczone;
      11. suma przedziałów = karty;
    - `Costs/CostSummaryTests` — wydatek planowany w kosztach, wydatek ujemny od dostawcy, wydatek sprzed uruchomienia opłacony po nim → poza kosztami, pełna lista kategorii, VAT/PPE jako kategorie;
    - handlery `GetSalesReport` i `ListSalesComponents` z fałszywymi źródłami: walidacja, brak odpytywania przy `beforeLaunch` i przy bazie sprzed uruchomienia, bezpiecznik `422`, filtry, sortowanie, stronicowanie, suma niezależna od strony.
  - **`StoneFlow.Orders.Tests`:** reguła `CountsTowardSales` dla statusów realizacji × statusów sklepu (w tym `null`, `refunded`, `on-hold`).
  - **`StoneFlow.Api.IntegrationTests/Reporting/SalesReportApiTests` (SQL Server/LocalDB):**
    - dane z Orders i Expenses przez API modułów;
    - zgodność predykatu SQL źródła z `CountsTowardSales`;
    - zwrot pieniędzy pominięty;
    - korekta do zamówienia z wcześniejszego miesiąca;
    - anulowanie i przywrócenie;
    - przełom uruchomienia;
    - wydatek ujemny i nieopłacony;
    - wypłata bez wpływu;
    - suma zestawienia = karta, suma kanałów = sprzedaż;
    - `grossTotal` listy wydatków = karta „Koszty” i wartość kategorii;
    - `401` bez sesji, `400` walidacja.
  - **`StoneFlow.Api.IntegrationTests/Expenses`:** `grossTotal` dla filtrów i pustego wyniku.
  - **`StoneFlow.Architecture.Tests`** — bez nowych wyjątków.
  - **Frontend** (`npm run format:check`, `npm run lint`, `npm run typecheck`, `npm test -- --watch=false`):
    - klient API i serwis raportu (stan, zmiana okresu, zachowanie stanu);
    - Pulpit: stany, karty, opis porównania, „brak podstawy porównania”, przejścia;
    - adapter wykresu: klawiatura i `aria-live`, z zastąpionym Chart.js;
    - `formatPercent`;
    - lista wydatków: parametry `kosztOd`/`kosztDo`/`kategoria` i suma brutto.
- manual: scenariusze testów ręcznych UI w `06-implementation-done.md`:
  - przełączanie okresów;
  - przejścia z kart, kanałów i kategorii z porównaniem sum;
  - klawiatura na wykresach;
  - telefon, tablet i szeroki ekran;
  - niekompletna integracja;
  - okres przed uruchomieniem.
