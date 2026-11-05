# Spec review — SF-10

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa wszystkie punkty zgłoszenia i reguły PRD („Korekta
  sprzedaży”, „Limit obniżeń sprzedaży”, PLAT-05, PLAT-07). Wzory limitu i dostępnej kwoty
  są poprawne dla zwrotów, zmiany rodzaju i obniżenia wartości, a przykłady z PRD dają opisane
  wyniki. Blokada `IOrderLock` w `EditOrder` jest wykonalna na istniejącym kodzie i nie grozi
  zakleszczeniem przy izolacji READ COMMITTED. Żaden niezmiennik nie jest naruszony. Do
  poprawy: reguła poprawy korekty bez zmiany kwoty przy istniejącej nadwyżce jest sprzeczna
  z założeniem o wychodzeniu z nadwyżki, a miejsce blokady w `EditOrder` nie zostało wpisane
  w kolejność sprawdzeń.

## Findings

### Critical Issues

None.

### Non-Critical Issues

- **N1 (średnia): poprawa korekty bez zmiany kwoty przy `R > V`.** W „Poprawa i usunięcie
  korekty” reguła brzmi `R − stara + nowa ≤ V`, a wyjątek dotyczy tylko obniżenia kwoty. Przy
  nadwyżce (stan po SF-12) poprawa samej daty albo powodu korekty daje `R' = R > V`, więc
  według wzoru dostaje `422`. Zwroty mają w specyfikacji regułę „blokowana tylko zmiana, która
  podnosi `R`”, a In scope mówi, że limit sprawdza się „w każdej zmianie, która podnosi `R`”.
  Te zapisy się różnią. Edge Cases wymienia tylko obniżenie, usunięcie i podwyższenie.
  **Poprawka:** zapisać jedną regułę dla korekt i zwrotów: odrzucenie wtedy i tylko wtedy,
  gdy `R' > R` i `R' > V`. Dodać przypadek brzegowy: poprawa daty korekty przy nadwyżce
  przechodzi. Z tą regułą `SalesReductionRules` powinien mieć jedną metodę dla obu rodzajów
  wpisu.
- **N2 (średnia): miejsce blokady w kolejności sprawdzeń `EditOrder`.** Sekcja „Limit przy
  ręcznej korekcie wartości (zmiana SF-8)” mówi, że blokada jest brana „przed zapisem”. Lista
  kolejności (`400 → 404 → 409 → pola sklepu i status → limit → brak zmian → zapis`) nie mówi,
  w którym kroku. W kodzie (`EditOrderHandler.EditAsync`) zmiana wartości jest znana dopiero
  po `Order.Edit`, czyli po wczytaniu zamówienia i sprawdzeniu wersji. Bez tego zapisu plan
  może wziąć blokadę na początku, co przeczy założeniu „tylko przy zmianie wartości”, albo
  policzyć `R` przed blokadą, co otwiera wyścig. **Poprawka:** dopisać krok, na przykład:
  „… → reguły domeny (`Order.Edit`) → jeśli zmienia się `value`, to `IOrderLock` i sumy po
  blokadzie → limit (`422`) → …”. Dopisać też, że blokada nie zmienia `rowversion`. Wtedy
  wersję sprawdzoną przed blokadą pilnuje dalej `SaveAsync` (`409 order_conflict`), a dodanie
  korekty między odczytem a blokadą kończy się `422`, a nie `409`. Analiza zakleszczeń:
  odczyt w `LoadAsync` zwalnia blokadę S po poleceniu (READ COMMITTED, brak RCSI i
  `IsolationLevel` w `UnitOfWork`). Potem `UPDLOCK`, a dalej `UPDATE` tego samego wiersza
  w tej samej transakcji, czyli konwersja U→X. Blokady U się wykluczają, więc nie ma cyklu.
  Zapisy wpisów biorą najpierw ten sam wiersz zamówienia. Ryzyka zakleszczenia nie widać.
- **N3 (niska): kolejność reguł przy poprawie zwrotu.** Kolejność „najpierw
  `refund_exceeds_paid`, potem `sales_reduction_limit_exceeded`” jest zapisana tylko przy
  dodaniu zwrotu. Założenie autora mówi ogólnie „przy zwrocie”. Ta sama poprawka może naruszyć
  obie reguły, na przykład zmiana na `salesReturn` z podniesieniem kwoty. **Poprawka:** w sekcji
  „Poprawa zwrotu” wprost napisać, że kolejność jest taka sama jak przy dodaniu.
- **N4 (niska): przekazanie sum i wartości do reguły we `OrderEntryWriter`.** Dziś `check`
  dostaje tylko `EntryTotals` (wpłaty, zwroty) i zwraca `PaidRuleViolation`. `Rejected` zna
  tylko dwa rodzaje naruszeń, a `EntryWriteResult.Available` opisuje się jako dane dla
  `RefundExceedsPaid`. Limit potrzebuje `V`, `SalesReturnsTotal` i `CorrectionsTotal`. Context
  wspomina tylko rozszerzenie `OrderLock`. **Poprawka:** dopisać do Context, że rozszerza się
  `LockedOrder` i `EntryTotals` (albo reguła dostaje `LockedOrder`). Dopisać też nowy rodzaj
  naruszenia i wyniku (`SalesReductionLimitExceeded` z `Available`) oraz mapowanie na problem
  w `OrderProblems`. Można to zostawić planowi, ale wtedy wpisać jako zadanie planu.
- **N5 (niska): klasyfikacja audytu.** `PaymentAudit.Object` ustala dziś typ wyrażeniem
  `entry is Payment ? Payment : Refund`, a `Label` rzuca wyjątek dla nieznanego typu. Bez
  zmiany korekta trafiłaby do audytu jako `Refund`. Specyfikacja słusznie wymaga
  `AuditObjectType.SalesCorrection`. **Poprawka:** dodać do Verification test audytu
  sprawdzający typ obiektu `SalesCorrection` i etykietę „Korekta sprzedaży do {numer}”.
- **N6 (niska): opis w formularzu zwrotu.** Zgłoszenie wymaga, żeby formularze wyjaśniały
  różnicę między korektą wartości, zwrotem i korektą sprzedaży. Formularz zwrotu zachowuje
  opisy rodzajów z SF-9. Opis „Zwrot sprzedaży: oddany towar, wycofana sprzedaż” nie mówi,
  że gdy pieniądze nie wracają, należy dodać korektę sprzedaży. To najbardziej prawdopodobny
  błąd użytkownika. **Poprawka:** dodać jedno zdanie w formularzu zwrotu, na przykład: „Gdy
  towar wraca, a pieniądze nie, dodaj korektę sprzedaży.”, albo uzasadnić, że opis z formularza
  korekty wystarcza.
- **N7 (niska): test równoległej zmiany wartości.** W Edge Cases jest „dają co najwyżej jedną
  z dwóch zmian”. Przy wartości 1000, nowej wartości 400 i korekcie 500 każda kolejność daje
  dokładnie jeden sukces i jedno `422`. **Poprawka:** zapisać „dokładnie jedna zmiana
  przechodzi, druga dostaje `422`”. Tak sformułowany warunek da się sprawdzić testem.

### Unclear or Ambiguous Sections

- **Ostrzeżenie o nadwyżce / UI:** nie wiadomo, przy którym podsumowaniu ma stać alert. Może
  to być podsumowanie „Płatności” w komponencie `order-payments` (tam są „Nadpłata” i „Do
  zwrotu”) albo nowa sekcja „Korekty sprzedaży”. Do wskazania sekcja i komponent.
- **Types crossing a boundary / `SalesReductions`:** tylko przy `excess` jest napisane, że to
  string albo `null`. Format `returnsTotal`, `correctionsTotal` i `total` nie jest podany
  (zapewne decimal jako string `"1000.00"`, jak w SF-9). Warto zapisać to wprost.
- **Kolejność sprawdzeń `EditOrder`:** lista pomija walidację pól zamówienia ręcznego (`400`),
  która w kodzie jest po `409`. Nie zmienia to logiki, ale lista nie odpowiada kodowi.
- **Założenie „stan powstaje tylko przez synchronizację”:** nadwyżka mogła powstać też
  wcześniej, w danych z SF-9, gdzie zwrot sprzedaży był ograniczony tylko kwotą Zapłacono.
  Ostrzeżenie jest wyliczane dla każdego zamówienia, więc działanie jest poprawne. Do
  poprawienia jest tylko uzasadnienie.

### Invented or Unsupported Requirements

None. Rozwiązania spoza zgłoszenia, takie jak `minimumValue`, blokada tylko przy zmianie
wartości, kolejność reguł i dozwolona korekta do zamówienia anulowanego, są zapisane jako
założenia albo wynikają z PRD i zapisów SF-9.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Ostrzeżenie o nadwyżce | DESIGN-GUID, „Ostrzeżenia w szczegółach zamówienia” | Brak konfliktu co do treści. Design guide wiąże ostrzeżenie z synchronizacją, a specyfikacja liczy je ze stanu dla każdego zamówienia. Te ujęcia są zgodne, bo ostrzeżenie zawsze wynika ze stanu. Tylko dla informacji. |

Brak konfliktów z decyzjami. PLAT-05: zwrot sprzedaży 1200 przy wartości 1000 i wpłatach
1200 daje `422` z `available = 1000.00`, bo reguła Zapłacono przechodzi, a limit nie. PLAT-07:
korekta 500 przy wartości 1000 i zaliczce 300 nie zmienia Zapłacono ani stanu. „Błędny wpis”
(PRD): korekta nie wpływa na Zapłacono, więc reguła „Zapłacono ≥ 0” jej nie dotyczy. Wszystkie
trzy przypadki są zgodne ze specyfikacją.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Korekta bez metody i bez wpływu na Zapłacono, stan płatności, wartość i przepływy. `OrderLock` liczy Zapłacono z wpłat i zwrotów, bez korekt. |
| 2. Kwoty decimal na backendzie | OK | `decimal(18,2)`, limit i nadwyżka w domenie, frontend tylko wyświetla. |
| 3. Dostęp egzekwowany przez API | OK | Sesja i XSRF na nowych trasach, testy `401` i XSRF w AC. |
| 4. Ponowienia nie duplikują | OK | Klucz idempotencji z unikalnym filtrowanym indeksem i hashem, zgodnie z `OrderEntryWriter.AddAsync`. |
| 5. Audyt finansowy | OK | Created, Updated i Deleted z rodzicem Order (dozwolone w `AuditRecord`). Pełne pola przy usunięciu. Zob. N5. |
| 6. Sekrety po stronie serwera | Not applicable | |
| 7. Granice modułów i warstw | OK | Tylko `orders.*`, audyt przez `IAuditLog`, reguły w domenie. |
| 8. Brak danych identyfikujących w dokumentach | OK | Dane fikcyjne. |

## Risk

- Recorded: high
- Assessed: high. Zadanie dotyczy podstawy sprzedaży, fizycznego usuwania wpisów, limitu
  przy współbieżności i zmian w zatwierdzonych ścieżkach SF-8 (`EditOrder`) oraz SF-9 (zwroty).
  Przed merge potrzebny jest przegląd człowieka.

## Assumptions Detected

- Daty korekty jak przy wpłatach: decyzja użytkownika, **zapisane**.
- Limit obowiązuje także dla zamówień anulowanych: decyzja użytkownika, **zapisane**.
- Kolejność „zwrot ≤ Zapłacono” przed limitem: **zapisane** (dla poprawy nie wprost, N3).
- Zmiana obniżająca `R` albo podwyższająca `V` nie jest blokowana przy nadwyżce: **zapisane**.
  Przypadek „bez zmiany `R`” jest niespójny (N1).
- Ostrzeżenie dla każdego zamówienia z `R > V`: **zapisane** (uzasadnienie niepełne, patrz
  Unclear).
- Korekta do zamówienia anulowanego dozwolona: **zapisane**.
- Usunięcie fizyczne, `version` w ciele `DELETE`: **zapisane**.
- `EditOrder` blokuje zamówienie tylko przy zmianie wartości: **zapisane** (miejsce blokady
  w kolejności nie, N2).
- Blokada nie zmienia `rowversion` zamówienia, więc dodanie korekty nie powoduje `409` przy
  edycji zamówienia: **niezapisane** (wynika z kodu, N2).
- Klucz idempotencji jest unikalny tylko w tabeli korekt; ten sam klucz użyty dla wpłaty
  i korekty nie jest wykrywany, jak w SF-9: **niezapisane**, wzorzec przejęty z SF-9.
- Zamówienie o wartości 0 zł nie przyjmuje żadnej korekty ani zwrotu sprzedaży
  (`available = 0.00`): **niezapisane**, wynika ze wzoru.

## Recommendation

- Revise specification. Poprawki N1 i N2 nie blokują `sdd-plan`, ale trzeba je wprowadzić
  w specyfikacji albo rozstrzygnąć wprost w planie przed implementacją. Pozostałe uwagi są
  niskiej wagi.
