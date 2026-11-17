# Spec review — SF-20

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Definicje kart, kierunków i dat operacji zgadzają się z PRD §4 (przełom uruchomienia), §5 („Rejestracja płatności”, „Anulowanie i zwrot”, „Zawartość widoków”), §8 („Przepływy pieniężne”, zakaz „zysku” i salda) oraz FIN-01–07, URU-01, PLAT-02, PLAT-05 i KWO-01. Sprawdziłem je w kodzie: `OrderCashOperationSource` czyta wpłaty i oba rodzaje zwrotów bez filtra stanu zamówienia i bez korekt, `ExpenseCashOperationSource` czyta tylko wydatki z `PaidOn` (kwota ze znakiem) i wypłaty, a `CashFlowTotals.Of` sumuje bez wartości bezwzględnej. Raport korzysta z tych samych źródeł, tej samej reguły sum, tego samego `LaunchCutoff`, a po wydzieleniu z `ListOperationsHandler` także z tej samej kolejności co lista „Operacje”. Karty, podział i ostatnie operacje uzgadniają się więc z `/api/operations` z samej konstrukcji, a test integracyjny to potwierdza. Uwagi dotyczą frontendu: gdzie leżą współdzielone typy i funkcje operacji, stan Pulpitu zależny od widoku, zapis parametru `kind` w kryterium 2 i odczyt parametrów przy powrocie na `/operacje`. Plan może je rozstrzygnąć bez zgadywania reguł finansowych.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Współdzielone typy i funkcje operacji na froncie (Context → Frontend; UI pkt 5; Assumptions — słowniki).** Spec przenosi do `shared/dictionaries/` tylko „etykiety rodzajów i kierunków”. Tabela „Ostatnie operacje” potrzebuje jednak więcej elementów, które dziś są w `features/operations/operation-models.ts`: typów `OperationItem`, `OperationKind`, `OperationDirection` (pole `recentOperations` w modelu raportu), funkcji `kindLabel` (zwrot pokazuje swój rodzaj), `NEGATIVE_AMOUNT_LABEL` i `isNegativeAmount` (dopisek „Korekta / zwrot od dostawcy”) oraz `sourceLink`. Spec świadomie kopiuje mapę `sourceLink` do modelu Pulpitu. Reszta zostaje nieokreślona, więc plan może skończyć się albo importem `features/operations` z `features/dashboard` (wbrew conventions i Invariant impact 7), albo kopią typu i funkcji etykiety. **Poprawka:** wypisać w spec, co trafia do `shared/` (np. `shared/dictionaries/cash-operations.ts`): kody i typy rodzaju i kierunku, `OperationItem`, `KIND_FILTER_LABELS`, `DIRECTION_LABELS`, `kindLabel`, `NEGATIVE_AMOUNT_LABEL`, `isNegativeAmount`. `operation-models.ts` eksportuje je dalej pod dotychczasowymi nazwami. Dla `sourceLink` trzeba wybrać jedno z dwóch: przenieść go razem z resztą (trzy trasy to wiedza o nawigacji, wspólna dla obu tabel) albo zostawić kopię i dopisać test zgodności tras.
2. **Zapis filtra rodzaju w kryterium 2 (Acceptance Criteria; Verification → integracyjne).** „`outflows` z `kind=refund,expense,withdrawal`” sugeruje jeden parametr z przecinkami. `GET /api/operations` przyjmuje `string[] kind` z powtórzonych parametrów, a `"refund,expense,withdrawal"` jako jeden kod daje `400` („Nieznany rodzaj operacji.”). **Poprawka:** zapisać `kind=refund&kind=expense&kind=withdrawal`, jak w pkt 15 dla `rodzaj`.
3. **Stan Pulpitu zależny od widoku (Behavior pkt 20–22; UI → Stany).** Spec wymaga, żeby nagłówek pochodził z raportu aktywnego widoku. Nie mówi jednak nic o pozostałych sygnałach wspólnego wyboru okresu w `DashboardService`:
   - `rangeError` (dziś `reportRangeError ?? componentsRangeError`): `422` z „Sprzedaży” zostaje widoczny pod wyborem okresu po przełączeniu na „Przepływy”, bo raport sprzedaży nie jest wtedy wczytywany ponownie;
   - `launchDate` dla `yearOptions` jest dziś ustawiane tylko z raportu sprzedaży;
   - nie wiadomo, czy licznik żądań jest wspólny dla obu raportów, czy osobny dla każdego.

   **Poprawka:** dopisać do pkt 21–22, że wybór okresu pokazuje błąd `422` i błędy tylko aktywnego widoku (albo że zmiana widoku je czyści), że `launchDate` ustawia każdy wczytany raport i że odpowiedź raportu nieaktywnego widoku jest odrzucana. Do testów `DashboardService` dodać: `422` w „Sprzedaży” → przełączenie → brak komunikatu w „Przepływach”.
4. **Powrót przyciskiem „Wstecz” na `/operacje?od=…&rodzaj=…` (Behavior pkt 17–19; Edge Cases — „Użytkownik zmienia filtr listy po przejściu z Pulpitu”).** Parametry zostają w adresie. Scenariusz: użytkownik przechodzi z karty na listę, zmienia filtr, otwiera źródło operacji i wraca „Wstecz”. Lista znów odczytuje parametry i nadpisuje zmieniony filtr, choć stan żyje w serwisie (SF-17, pkt 14). **Poprawka:** zapisać wybrane zachowanie. Jedna możliwość: po odczycie parametrów lista usuwa je z adresu (`replaceUrl`). Druga: zostawić je świadomie, jak na `/wydatki` w SF-19. Wybrane zachowanie trzeba dopisać do scenariuszy ręcznych.
5. **Zakres testu „zysk” i „saldo” (Acceptance Criteria — kryterium 6; Behavior pkt 24).** Po przeniesieniu nagłówka, etykiety podstawy, opisu porównania i `app-incomplete-data-notice` nad przełącznik (pkt 20) te teksty nie są częścią `app-cash-flow-view`. Test komponentu samego widoku ich nie obejmie. **Poprawka:** sprawdzać brak „zysk” i „saldo” na całym `dashboard-page` w widoku „Przepływy pieniężne” (nagłówek, etykieta podstawy, karty, wykresy z `aria-label`, lista podziału, tabela), a nie tylko w komponencie widoku.
6. **Wynik `CashFlowSummary` przy `BeforeLaunch` (Data / API → Types crossing a boundary; Verification → test 11).** „Przy `BeforeLaunch` wynik jest pusty, jak w `CostSummary`”. `CostSummary.Of` nie zwraca jednak przy `BeforeLaunch` pustego wyniku: daje sumę 0, pełną listę kategorii z zerami i pustą listę przedziałów. Odwołanie się do niego daje więc dwie różne interpretacje: `OutflowsByKind` z trzema zerami albo pusta lista. Handler i tak nie woła podsumowania przy `BeforeLaunch` (pkt 3), więc API się nie zmienia. **Poprawka:** zapisać wprost wynik domeny przy `BeforeLaunch`, np. `Totals` = 0/0/0, `OutflowsByKind` = trzy pozycje 0, `Buckets` = [] (jak `CostSummary`), i tak samo sformułować test 11.
7. **Widoczność mapowań kontraktów (Data / API → Odpowiedź).** „Jedno mapowanie służy obu endpointom, bez kopii”. Dziś `OperationContracts.ToResponse(OperationItemDto)` jest `private`, a mapowanie okresu i porównania leży w `SalesReportContracts`. Wymaga to zmiany widoczności (internal) albo wydzielenia mapowań, czyli zmiany w plikach SF-17 i SF-19. Nie jest to problem, ale warto to zapisać w Context, żeby plan nie zrobił kopii. Analogicznie `CashFlow/` korzysta z `Sales/ReportPeriodInput` i `SalesReportCodes` (kody okresu, statusu porównania, format procentu). **Poprawka:** jedno zdanie w Context: które istniejące mapowania i kody stają się `internal` i są współdzielone, bez przenoszenia folderów (conventions: „Existing names and structure are not migrated”).

### Unclear or Ambiguous Sections

- **Behavior pkt 19 (Lista „Operacje” — parametry przejścia):** „brak, niepoprawna lub nieistniejąca data albo `od` > `do` → okres domyślny”. Nie wiadomo, czy przy poprawnym `od` i niepoprawnym `do` obie daty wracają do bieżącego miesiąca, czy tylko `do`. Na `/wydatki` (SF-19) każdy parametr jest pomijany osobno, ale lista „Operacje” wymaga obu dat. Prawdopodobnie chodzi o całe okno [od, do], ale trzeba to zapisać, bo sama `do` z bieżącego miesiąca może dać `od` > `do`.
- **Behavior pkt 18 a pkt 19:** „Gdy jest którykolwiek parametr: okres = [`od`, `do`]”. Przy samym `rodzaj` (bez dat) obowiązuje pkt 19, czyli bieżący miesiąc, a nie okres zachowany w serwisie. To prawdopodobnie zamierzone. Warto dodać przykład w Edge Cases.
- **Behavior pkt 22:** „zmiana widoku … wczytuje raport aktywnego widoku; dla Sprzedaży jak dotąd raport i zestawienie”. Nie wiadomo, czy zmiana okresu w „Przepływach” zeruje filtry, sortowanie i stronę zestawienia sprzedaży (dziś robi to `setSelection` → `resetComponents`). Najpewniej tak, bo zmienia się wspólny okres. Warto to zapisać.
- **UI pkt 5 („Ostatnie operacje”):** `app-data-table` z `total` = liczba pozycji. Brakuje informacji, że stronicowanie jest ukryte albo nieaktywne. Przy 10 pozycjach i domyślnym rozmiarze strony wynika to samo z siebie, ale warto podać `pageSize` przekazywany do tabeli.

### Invented or Unsupported Requirements

None. Liczba 10 ostatnich operacji, pominięcie kolumny „Metoda”, nazwy parametrów `od`/`do`/`rodzaj`, osobne porównanie każdej karty i dwie serie wykresu to oznaczone decyzje wykonawcy. Wypełniają luki PRD (§5 nie podaje liczby ostatnich operacji ani kształtu przejścia) i niczemu w nim nie przeczą.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Invariant impact → 7 (frontend) | `.sdd/context/conventions.md` → Frontend → Structure („feature areas never import each other”) i kod `features/dashboard/dashboard-page.ts` | Rozbieżność kodu z dokumentacją z SF-19, niespowodowana przez SF-20: Pulpit importuje `features/integration/incomplete-data-notice`. Spec zapewnia tylko, że `features/dashboard` nie importuje `features/operations`, co jest prawdą. Rozbieżność warto odnotować w `06-implementation-done.md` jako otwarty punkt (przeniesienie komponentu do `shared/ui` albo wyjątek w conventions), bez naprawiania w tym zadaniu. |
| Data / API → Types crossing a boundary (`CashFlowSummary` przy `BeforeLaunch`) | `Reporting.Domain/Costs/CostSummary.cs` | „Pusty, jak w `CostSummary`”, a `CostSummary` przy `BeforeLaunch` zwraca zera i pełny słownik kategorii (Non-Critical 6). |

Spec nie przeczy żadnej zapisanej decyzji z PRD §12 (D-01, D-03, D-04, D-06, D-08) ani decyzjom z SF-17 i SF-19.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Wpływy to tylko wpłaty, wypływy to zwroty (oba rodzaje), opłacone wydatki (`PaidOn != null`) i wypłaty, według dat operacji (kod źródeł potwierdza). Raport nie czyta wartości zamówień, korekt ani nieopłaconych wydatków. Testy obejmują nieopłacone zamówienie (wpływy 0) i wpłatę, która nie zmienia raportu sprzedaży. |
| 2. Backend decimal money | OK | Sumy, podział, przedziały i porównania liczy backend na `decimal` (`CashFlowTotals`, `CashFlowSummary`, `Comparison`). Zaokrągla się tylko procent. „Razem” w tabeli pochodzi z kart w API. Konwersja tekst → liczba jest tylko w `chartNumber`, do geometrii wykresu. |
| 3. API-enforced access | OK | Nowy `GET` pod domyślną polityką sesji, bez `.AllowAnonymous()`, z testem `401`. |
| 4. Retries never duplicate | Not applicable | Same odczyty. |
| 5. Financial audit trail | Not applicable | Brak zmian danych finansowych. |
| 6. Secrets stay server-side | OK | Brak nowych sekretów i zależności. |
| 7. Module and layer boundaries | OK | Odczyt wyłącznie przez `ICashOperationSource` (`Shared.Application`), reguły w `Reporting.Domain` (tylko `Shared.Kernel`), wspólna operacja odczytu w `Reporting.Application`. Na froncie trzeba domknąć miejsce typów i funkcji operacji (Non-Critical 1), żeby nie powstał import `features/operations`. |
| 8. No identifying data in docs | OK | Przykłady fikcyjne. Ostatnie operacje pokazują to samo co lista „Operacje”. |

## Risk

- Recorded: high
- Assessed: high — pieniądze. Definicje i uzgodnienie kart przepływów z listą „Operacje” (FIN-05) oraz rozdzielenie sprzedaży od pieniędzy (FIN-07). Błąd w zakresie dat, przełomie uruchomienia albo znaku wydatku ujemnego daje błędny wynik bez widocznego błędu. Wymagany przegląd przez człowieka przed merge.

## Assumptions Detected

| Założenie | Podane w spec |
|---|---|
| Reguły okresu, porównania, przełomu i stanu „przed uruchomieniem” z SF-19 bez kopii | Tak (decyzja wykonawcy) |
| Porównanie osobno dla każdej karty, „brak podstawy” przy bazie 0 albo sprzed uruchomienia | Tak (decyzja wykonawcy) |
| Sumy z `ICashOperationSource`, `CashFlowPolicy`, `CashFlowTotals` (SF-17) | Tak (decyzja wykonawcy, zgodna z SF-17 spec „Spójność z SF-20”) |
| Linki kart z efektywnym początkiem i kodami API w `rodzaj` | Tak (decyzja wykonawcy) |
| Dwie serie wykresu, zmiana środków tylko w tabeli i na karcie | Tak (decyzja wykonawcy) |
| „Zwroty” łączą zwrot sprzedaży i zwrot pieniędzy | Tak (decyzja wykonawcy, zgodna z PRD §5 i filtrem listy) |
| 10 ostatnich operacji w kolejności listy, bez kolumny „Metoda” | Tak (decyzja wykonawcy) |
| Wspólny okres i nagłówek dla obu widoków, wczytywanie tylko aktywnego widoku | Tak (decyzja wykonawcy) |
| Bezpiecznik 10 000 na zakres, kod `report_range_too_large` | Tak (decyzja wykonawcy) |
| Wydzielenie odczytu źródeł i kolejności z `ListOperationsHandler` | Tak (decyzja wykonawcy) |
| Przeniesienie słowników operacji do `shared/dictionaries` | Częściowo: nie wymienia typów `OperationItem`, `kindLabel`, `NEGATIVE_AMOUNT_LABEL`, `isNegativeAmount` (Non-Critical 1) |
| Błędy i `launchDate` we wspólnym wyborze okresu pochodzą z aktywnego widoku | Nie (Non-Critical 3) |
| Parametry przejścia zostają w adresie listy po odczycie | Nie (Non-Critical 4) |
| Mapowania kontraktów SF-17 i SF-19 stają się `internal` i są współdzielone | Nie wprost (Non-Critical 7) |
| Okres „bieżący miesiąc” obejmuje cały miesiąc (także operacje z datą po dziś), a rok w toku kończy się na dziś — w raporcie i na liście tak samo | Pośrednio (przez odwołanie do SF-19); uzgodnienie nie jest zagrożone, bo link przekazuje te same daty |

## Recommendation

- Proceed as-is. Uwagi niekrytyczne 1–7 i niejasności rozstrzygnąć w `sdd-plan` (albo krótką poprawką spec) bez zmiany reguł finansowych. Decyzje wykonawcy potwierdzić w opisie PR.
