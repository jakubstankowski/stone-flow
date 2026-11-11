# Spec review — SF-19

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Reguły sprzedaży, kosztów, liczby zamówień, przełomu uruchomienia oraz anulowania i przywrócenia zgadzają się z PRD §4, §5 („Anulowanie i zwrot”, „Analiza finansów”, „Podział sprzedaży na kanały”) i §8. Sprawdziłem to także w kodzie: `ShopStatuses.HiddenByDefault`, `FulfillmentStatus.Cancelled`, `Refund.SalesReductionOf`, `SalesCorrection`, `ExpenseReader`, `ListOperations` i `CashFlowPolicy`. Sumy uzgadniają się konstrukcyjnie: zestawienie = karta „Sprzedaż”, kanały = sprzedaż, `grossTotal` = karta „Koszty”, przedziały = karty. Granice modułów, `decimal` i jedno miejsce zaokrąglenia (procent, połową od zera) są opisane, a każde kryterium zgłoszenia ma zachowanie i test. Zostały uwagi niekrytyczne: zdublowana reguła statusu w SQL, brak górnej granicy własnego zakresu, kilka niedookreśleń endpointu zestawienia i powrotu na Pulpit. Plan może je rozstrzygnąć bez zgadywania reguł biznesowych.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Reguła `CountsTowardSales` zdublowana w predykacie SQL (Context → Moduł Orders; Data / API → Types crossing a boundary).** Źródło zwraca wszystkie składniki z flagą i niczego po niej nie filtruje (`ISalesComponentSource.ListAsync`). Flagę można więc policzyć w pamięci, w `OrderSalesComponentSource`: projekcja SQL pobiera `FulfillmentStatus` i `ShopStatus`, a potem źródło woła `SalesEligibility.CountsTowardSales` z `Orders.Domain`. Reguła istniałaby wtedy w jednym miejscu (DRY wiedzy biznesowej, rules.md), bez testu zgodności SQL z domeną. Jeżeli predykat zostaje w SQL, test integracyjny zgodności musi wprost objąć zamówienie ręczne (`ShopStatus = NULL`: `NOT IN` w SQL daje dla `NULL` wynik nieznany), status nieznany słownikowi i `on-hold`/`refunded`. **Poprawka:** liczyć flagę metodą domeny po projekcji albo dopisać te trzy przypadki do testu `SalesReportApiTests`.
2. **Brak górnej granicy własnego zakresu (Behavior pkt 1, 6; Data / API → Walidacja).** `custom` przyjmuje dowolne `to` (np. `9999-12-31`). Bezpiecznik 10 000 liczy wiersze ze źródeł, a nie przedziały, więc taki zakres daje dziesiątki tysięcy pustych przedziałów miesięcznych w `series` i na wykresie. **Poprawka:** dopisać regułę, np. `to` nie później niż 31.12 bieżącego roku albo limit liczby przedziałów, z błędem `400` przy polu `to` i komunikatem.
3. **Endpoint zestawienia: początek zakresu i przełom uruchomienia (Behavior pkt 16; Data / API → Endpoints).** Pkt 16 mówi o zakresie „przekazanym z odpowiedzi raportu”, ale nie mówi, które pole przekazuje frontend (`period.from` czy `period.effectiveFrom`). Nie wynika też jasno, że `GET /api/reports/sales/components` sam stosuje `LaunchCutoff.EffectiveFrom` i co zwraca, gdy efektywny początek > `to` (pusta lista i `sum: "0.00"` jak w `ListOperations` czy flaga `beforeLaunch`). **Poprawka:** dopisać, że endpoint sam przycina `from` do daty uruchomienia (zwraca `effectiveFrom`), że frontend przekazuje `period.from` i `period.to`, oraz jaka jest odpowiedź dla zakresu w całości przed uruchomieniem.
4. **Powrót na Pulpit a aktualność (Behavior pkt 30–31).** Stan widoku „przetrwa przejście do zamówienia lub wydatku i powrót”. Nie wiadomo jednak, czy po powrocie raport i zestawienie są pobierane ponownie z zachowanym okresem, filtrami i stroną, czy pokazywany jest wynik z pamięci z dawnym „Stan na”. Użytkownik często wraca po poprawieniu zamówienia (PRD §4: raporty minionych okresów mogą się zmieniać). **Poprawka:** zapisać, że wejście na `/pulpit` ponownie wczytuje raport i bieżącą stronę zestawienia z zachowanym stanem wyboru (jak `ExpensesService`), a „nie odpytuje w tle” dotyczy tylko odświeżania cyklicznego.
5. **`SalesComponentRecord.Amount` „dodatnia” (Data / API → Types crossing a boundary).** Zamówienie może mieć wartość 0 zł (Edge Cases: „Zamówienie o wartości 0 zł… wnosi 0,00 zł”; PRD §5, PLAT-04). **Poprawka:** „nieujemna (zamówienie może mieć 0 zł); zwrot i korekta > 0”.
6. **Wskazówka „Pokaż zamówienia” na karcie „Liczba zamówień” (UI pkt 4).** Karta prowadzi do zestawienia z filtrem rodzaju, a nie do listy „Zamówienia” (Out of scope). Napis sugeruje przejście do listy. **Poprawka:** np. „Pokaż w zestawieniu”.
7. **Rok bieżący porównywany z tym samym odcinkiem poprzedniego roku (Behavior pkt 1, 5; Assumptions).** To decyzja wykonawcy do potwierdzenia przez właściciela. PRD §8 mówi tylko „rok z poprzednim rokiem”, więc to interpretacja, a nie sprzeczność blokująca. **Poprawka:** wpisać to w `06-implementation-done.md` jako otwarty punkt i w opisie PR jako pytanie do właściciela. Reguła ma być zamknięta w `ReportPeriod.Resolve`, żeby ewentualna zmiana na „cały poprzedni rok” dotyczyła jednej metody i jej testów.

### Unclear or Ambiguous Sections

- **Behavior pkt 21 / „Lista wydatków — suma i parametry” pkt 28:** „pozostałe filtry czyszczą”. Nie wiadomo, czy sortowanie i strona wracają do domyślnych (data kosztu malejąco, strona 1). Najpewniej tak, jak przy `wydatekCykliczny`, ale warto to zapisać.
- **Behavior pkt 4:** „wariant dla liczby całkowitej” w `Comparison.Of`. Procent liczby zamówień liczony jest na `decimal` z tym samym zaokrągleniem. Warto to powiedzieć wprost, żeby nie powstał wariant z `double`.
- **Behavior pkt 17:** suma zestawienia z filtrem rodzaju „Zwrot sprzedaży” albo „Korekta sprzedaży” jest ujemna. To wynika z pkt 10, ale etykieta pod sumą (pkt 19) tego nie wyjaśnia. Wystarczy przykład w Edge Cases.
- **UI → Wykresy:** Chart.js dostaje liczby z ciągów API. Specyfikacja dopuszcza to tylko dla geometrii (Invariant impact 2). Warto dodać, że konwersja tekst → liczba jest w adapterze wykresu i nigdzie poza nim.

### Invented or Unsupported Requirements

- Filtr `kind` zestawienia i przejście karty „Liczba zamówień” do zestawienia. Spoza ticketu, ale uzasadnione w Assumptions (ZAM-07, FIN-01 „liczba zamówień obejmuje te same zamówienia co sprzedaż”, design guide §4 „klikalność prowadzi do danych składających się na wartość”). Akceptowalne.
- Granulacja dzienna dla miesiąca i krótkiego własnego zakresu. PRD §8 wymienia „agregację miesięczną”, a dzienna jest dodatkiem. Ma podstawę w FIN-01–04 („zmiany w czasie”): miesiąc z jednym przedziałem nie pokazywałby zmian. Akceptowalne.
- Indeks `IX_SalesCorrections_CorrectedOn`: techniczny, addytywny, uzasadniony zapytaniem po dacie korekty (kolumna `CorrectedOn` istnieje w `SalesCorrectionConfiguration`).

Poza tym nic nowego.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior pkt 1 (rok bieżący → ten sam odcinek poprzedniego roku) | PRD §8 „rok z poprzednim rokiem” | Interpretacja, nie sprzeczność: PRD nie mówi, czy niepełny rok porównuje się z pełnym. Decyzja wykonawcy do potwierdzenia w PR (NC 7). |
| Behavior pkt 1 (bieżący miesiąc = cały miesiąc) | PRD §8 (rok bieżący „do dziś”) | Brak konfliktu. Asymetrię zdecydował użytkownik 09.11.2026 i zapisano ją w Assumptions. |
| Data / API → „Łączenie… w pamięci Reporting” | conventions (stronicowanie w bazie) | Świadome odstępstwo, takie samo jak w SF-17, uzasadnione skalą D-08 i bezpiecznikiem SQL `TOP`. Akceptowalne. |
| Behavior pkt 3 (okres przed uruchomieniem → komunikat, nie zera) | SF-17 lista „Operacje” (sumy 0,00 zł) | Różnica opisana w Assumptions, zgodna z PRD §8 (zabezpieczenie 5). Brak konfliktu z PRD. |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Sprzedaż z wartości zamówień, zwrotów sprzedaży i korekt według ich dat. Wpłaty nie są czytane, zwroty pieniędzy wykluczone przez `Refund.SalesReductionOf`, koszty według daty kosztu niezależnie od zapłaty, wypłaty poza kosztami i wynikiem. |
| 2. Backend decimal money | OK | Sumy, wynik, podziały, procent i `grossTotal` w backendzie na `decimal`. Procent zaokrąglany `AwayFromZero`, poza tym bez zaokrągleń (KWO-01). Frontend nie sumuje („Razem” z kart), Chart.js tylko do geometrii. |
| 3. API-enforced access | OK | Nowe `GET` pod domyślną polityką sesji, testy `401` dla obu endpointów. `/api/expenses` już chronione. |
| 4. Retries never duplicate | Not applicable | Tylko odczyty. |
| 5. Financial audit trail | Not applicable | Brak zmian danych finansowych. Migracja to wyłącznie indeks. |
| 6. Secrets stay server-side | OK | Brak sekretów. `chart.js` z npm w przypiętej wersji `4.6.1` (wydana 22.10.2026, starsza niż 2 tygodnie). |
| 7. Module and layer boundaries | OK | Reporting czyta przez `ISalesComponentSource`/`ICostSource` (`Shared.Application`), `SalesComponentKind` w `Shared.Kernel` (wzór `CashOperationKind`). Implementacje w Infrastructure właścicieli, reguła statusu w `Orders.Domain`, `Reporting.Domain` tylko → Kernel (zgodnie z csproj). Bez mapowania tabel `orders`/`expenses`. |
| 8. No identifying data in docs | OK | Zestawienie bez danych klienta, tylko numer zamówienia. Przykłady fikcyjne. |

## Risk

- Recorded: high
- Assessed: high. Definicje KPI sprzedaży, kosztów i wyniku (pieniądze, FIN-01–06). Błąd w regule wyłączeń daje zły wynik bez widocznego błędu. Wymagany przegląd człowieka przed merge.

## Assumptions Detected

| Założenie | Czy zapisane w spec |
|---|---|
| Karta „Koszty” → lista wydatków z `grossTotal` (decyzja użytkownika 09.11.2026) | Tak |
| Bieżący miesiąc = cały miesiąc vs cały poprzedni (decyzja użytkownika) | Tak |
| Rok bieżący do dziś vs ten sam odcinek roku poprzedniego (decyzja wykonawcy, do potwierdzenia) | Tak |
| Cztery karty KPI, Chart.js (decyzje użytkownika) | Tak |
| Efektywny początek = max(from, data uruchomienia), jak w SF-17 | Tak |
| Okres przed uruchomieniem → komunikat zamiast zer | Tak |
| Dzielnik procentu = \|baza\| | Tak |
| Flaga statusu liczona z bieżącego stanu zamówienia (anulowanie/przywrócenie działa wstecz) | Tak (pkt 8–9) |
| Dane kanału i daty zamówienia dla zwrotu/korekty brane z bieżącego stanu zamówienia (zmiana kanału przenosi też obniżenia) | Nie wprost. Wynika z kontraktu (`Channel` zamówienia) i z PRD („kanał swojego zamówienia”). Warto dopisać jako przypadek brzegowy. |
| Zakres zestawienia przekazywany przez frontend z odpowiedzi raportu | Częściowo (NC 3) |
| Wydatki nie mają miękkiego usuwania, więc `ICostSource` i `ExpenseReader` filtrują ten sam zbiór | Nie wprost. Zgodne z kodem (`ExpenseReader` nie ma filtra usunięcia), uzgodnienie pokrywa test integracyjny `grossTotal` = karta. |
| Brak górnej granicy własnego zakresu | Nie zapisane (NC 2) |
| Ponowne wczytanie danych po powrocie na Pulpit | Nie zapisane (NC 4) |

## Recommendation

- Proceed as-is. Uwagi niekrytyczne 1–7 i niejasności powinny zostać rozstrzygnięte w `04-implementation-plan.md`. Najważniejsze są NC 1 (jedno miejsce reguły statusu) i NC 2 (górna granica zakresu).
