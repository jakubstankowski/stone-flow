# Code review — SF-10

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa wszystkie wymagania specyfikacji z rozstrzygnięciami
  z sekcji 5 planu. Reguła N1 (`R' > R` i `R' > V`, `available = max(V − (R − stary wkład), 0)`)
  ma jedno miejsce w domenie i jest użyta przy dodaniu i poprawie korekty, przy dodaniu i
  poprawie zwrotu (ze zmianą rodzaju, po regule Zapłacono, N3). `EditOrder` bierze `IOrderLock`
  tylko przy obniżeniu wartości, po `Order.Edit`, a sumy liczy po blokadzie (N2). Każdy zapis
  obniżenia przechodzi przez `OrderEntryWriter`, który zaczyna od blokady. Audyt ma typ
  `SalesCorrection`, etykietę „Korekta sprzedaży do {numer}” i pełny ślad `Deleted`. Niezmienniki
  nie są naruszone. Uwagi dotyczą drobnych zapachów kodu, fokusu po zapisie korekty i kilku luk
  w testach; żadna nie blokuje merge.
- Risk: high (human review required before merge)

## Project Invariant Violations

None.

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | `SalesCorrection` bez metody; `EntryTotals.Paid` = wpłaty − zwroty, korekty tylko w `Reductions` (`PaymentSummary.cs:66-69`). `OrderDetailsDto.Payment` nie bierze korekt. Test `Correction_does_not_change_paid_or_state` sprawdza Zapłacono, stan, wartość, wersję, `updatedAtUtc` i listę zamówień. |
| 2. Kwoty decimal na backendzie | OK | `decimal(18,2)` z `CHECK (Amount > 0)` (migracja), reguły na `decimal` w domenie. Frontend tylko formatuje (`formatPln`) i parsuje wejście (`parsePlnAmount`), bez arytmetyki. Test typu kolumny rozszerzony (`PaymentsApiTests`). |
| 3. Dostęp egzekwowany przez API | OK | Trasy w grupie z polityką domyślną; `Correction_routes_require_session_and_xsrf` sprawdza `401` i XSRF dla POST/PUT/DELETE. |
| 4. Ponowienia nie duplikują | OK | Unikalny filtrowany indeks `IX_SalesCorrections_CreationRequestId`, skrót z `orderId` (`SalesCorrectionInput.cs:29-33`); test równoległy z 4 żądaniami i test klucza użytego w innym zamówieniu (`salesCorrectionId`). |
| 5. Audyt finansowy | OK | Created/Updated/Deleted w tej samej jednostce pracy, rodzic Order, `Deleted` zachowuje `amount`, `date`, `reason`. `Audit_failure_rolls_back_correction` obejmuje też obniżenie wartości zamówienia. |
| 6. Sekrety po stronie serwera | Not applicable | Bez zmian. |
| 7. Granice modułów i warstw | OK | Tylko tabele `orders.*`, audyt przez `IAuditLog`; reguły w `Orders.Domain`, handler tylko orkiestruje; endpointy tylko mapują wynik. |
| 8. Brak danych identyfikujących w dokumentach | OK | Dane w testach i dokumentach fikcyjne. |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

- **C1 (niska)** `api/src/Modules/Orders/StoneFlow.Orders.Application/Abstractions/Ports.cs:74-75`:
  `LockedOrder` ma domyślne `SalesReturnsTotal = 0` i `CorrectionsTotal = 0`. Jedynym producentem
  jest `OrderLock`, a żaden test nie używa konstruktora, więc wartości domyślne nie mają
  wywołującego (YAGNI). Są też ryzykowne: nowy producent, który pominie te argumenty, dostanie
  `R = 0` i limit przestanie działać po cichu. Lepiej zrobić parametry wymagane.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Korekta: kwota > 0, data nie z przyszłości (Europe/Warsaw), powód 1–500, bez metody | Covered | `SalesCorrectionInput`, `SalesCorrection.Require`, testy jednostkowe i `Conflicts_404_and_validation`. |
| Korekta dla zamówień ręcznych, ze sklepu i anulowanych | Covered | `Cancelled_and_shop_orders_accept_corrections_with_the_same_limit`. |
| Poprawa i usunięcie z `rowversion`, potwierdzeniem i audytem | Covered | `OrderEntryWriter.EditAsync/DeleteAsync`; dialog w `order-payments.html` z nazwą wpisu i skutkiem (`deleteEffect`). |
| Idempotencja dodania (skrót z `orderId`) | Covered | Testy równoległy i powtórzenia po wyczerpaniu limitu (`200`). |
| Limit N1 przy dodaniu/poprawie korekty | Covered | `AddSalesCorrection.cs:105`, `EditSalesCorrection.cs:38`; test zmiany samego powodu przy nadwyżce. |
| Limit przy dodaniu zwrotu sprzedaży, kolejność `refund_exceeds_paid` → limit | Covered | `AddRefund.cs:38`; PLAT-05 (`available = 1000.00`) i przypadek naruszenia obu reguł. |
| Limit przy poprawie zwrotu i zmianie rodzaju (N3) | Covered | `EditRefund.cs:40-43` (stary i nowy wkład); `Refund_kind_change_respects_limit`. |
| Limit przy ręcznym obniżeniu wartości (N2): blokada tylko przy obniżeniu, po `Order.Edit`, `minimumValue = R` | Covered | `EditOrder.cs:124-127, 150-162`; `Edit_order_value_below_reductions_is_rejected`. |
| Ostrzeżenie o nadwyżce `excess = R − V` z backendu | Covered | `SalesReductionRules.Summarize`, `GetOrder.cs` (`SalesReductions`); test stanu zasianego SQL-em; alert w `sales-corrections.html:43`. |
| Zmniejszenia dozwolone przy nadwyżce | Covered | `Excess_warning_from_seeded_state_and_decreases_allowed_in_excess`. |
| Współbieżność: korekta vs zwrot sprzedaży, obniżenie wartości vs korekta (N7) | Covered | Dwa testy z barierą przed zapisem; bez blokady w `EditOrder` drugi test nie przejdzie (obie zmiany zostałyby zapisane). |
| `GET` zwraca `salesCorrections[]` i `salesReductions` (stringi z 2 miejscami, `excess` null) | Covered | `OrderContracts.cs`. |
| Sekcja „Korekty sprzedaży” osobno od wpłat i zwrotów, pusty stan, akcje, formularz z opisem | Covered | `sales-corrections.*`, `sales-correction-form.*`. |
| Opisy w formularzach (korekta wartości, zwrot N6, korekta) | Covered | `VALUE_CORRECTION_HINT` przy edycji, zdanie w `refund-form.html`, opis w formularzu korekty. |
| Komunikaty limitu z kwotą; w formularzu zamówienia przy polu wartości | Covered | `salesReductionLimitMessage`, `valueLimitError` → `fieldErrors.value`. |
| Wzajemne wykluczenie z edycją zamówienia, także dla korekt | Covered | Wspólny stan `openForm`/`operations` w `OrderPayments`; testy w `order-details-page.spec.ts` i `sales-corrections.spec.ts`. |
| Dostępność i kolory jak w SF-9 | Partial | Tokeny kolorów i etykiety `sr-only` są; fokus po zapisie korekty trafia do złej sekcji (A1). |

## Plan Deviations

- Trasy korekt są w `PaymentsEndpoints.cs`, a nie w nowym `SalesCorrectionsEndpoints.cs`. Plan
  dopuszczał oba warianty.
- `PaymentVocabulary.cs` i `entry-form-support.ts` bez zmian (plan: „jeśli potrzebne”).
- Test audytu korekty (N5) jest w `Application/SalesCorrectionInputTests.cs`, a nie
  w rozszerzonym `PaymentAuditTests`.
- `order-form.spec.ts` bez zmian; opis pola wartości i błąd limitu są sprawdzane w
  `order-details-page.spec.ts`.
- Limit w `EditOrder` jest testowany w `SalesCorrectionsApiTests`, a nie w rozszerzeniu
  `OrdersApiTests` (spec, Verification).
- Dodatkowo: wspólna walidacja powodu `EntryInput.ValidateReason` i `entryReasonError`
  (refaktoryzacja `RefundInput` i `refund-form.ts`), pomocnicze żądania testowe przeniesione
  z `PaymentsApiTests` do `OrderEntryRequests.cs`. Żaden test SF-9 nie został usunięty.

## Null Safety Issues

None. `excess` jest nullable w całym łańcuchu (`decimal?` → `string?` → `string | null`).
`MinimumValue!.Value` (`OrdersEndpoints.cs`) i `Available!.Value` (`PaymentsEndpoints.cs`) są
ustawiane zawsze razem z odpowiednim wynikiem. `LockAsync ?? throw` w `EditOrder.cs:159-160`
opiera się na tym, że zamówienie wczytano w tej samej jednostce pracy, a zamówień się nie usuwa.

## Correctness Issues

- **A1 (niska, dostępność)** `app/src/app/features/orders/order-payments.ts:238-251`
  (`closeForm`): po zapisie korekty `closeAndReload` zamyka formularz w trakcie operacji, więc
  przycisk, do którego ma wrócić fokus („Dodaj korektę sprzedaży”, „Popraw”), jest wtedy
  wyłączony. Fokus trafia do zapasowego `heading`, czyli nagłówka „Płatności”
  (`#platnosci-naglowek`), nad sekcją, w której pojawił się komunikat „Korekta sprzedaży
  zapisana.”. Nagłówek `#korekty-naglowek` ma już `tabindex="-1"`
  (`sales-corrections.html:5`), ale nic go nie używa. Użytkownik klawiatury i czytnika ląduje
  w innej sekcji niż ta, w której pracował.

Nie znalazłem błędów we wzorach limitu ani w kolejności sprawdzeń. Sprawdzone ręcznie:
dodanie (stary wkład 0), poprawa kwoty w górę i w dół, zmiana tylko daty lub powodu przy
nadwyżce, `moneyReturn` ↔ `salesReturn` z jednoczesną zmianą kwoty, wartość 0 zł, obniżenie
wartości do `R` (przechodzi) i poniżej (`422`), podwyżka wartości przy nadwyżce (bez blokady,
przechodzi). Blokada nie zmienia `rowversion`, więc `409 order_conflict` dalej pilnuje
`SaveAsync`. Kolejność blokad (najpierw wiersz zamówienia) jest taka sama we wszystkich
ścieżkach, więc zakleszczenia nie widać.

## Test Gaps

- **T1 (niska)** `api/tests/StoneFlow.Api.IntegrationTests/Orders/SalesCorrectionsApiTests.cs:144-169`:
  test nadwyżki nie sprawdza `EditOrder` w stanie `R > V`: ani że dalsze obniżenie wartości
  daje `422` z `minimumValue = R`, ani że częściowa podwyżka nadal poniżej `R` przechodzi bez
  limitu. Reguła ma testy jednostkowe (`SalesReductionRulesTests.cs:46-53`), ale nie ma testu
  na poziomie API.
- **T2 (niska)** `SalesCorrectionsApiTests.cs:96-115`: brak przypadku poprawy zwrotu, która
  narusza obie reguły naraz (np. zmiana na `salesReturn` z podniesieniem kwoty ponad
  Zapłacono). Kolejność N3 przy poprawie jest sprawdzona tylko w kodzie (`EditRefund.cs:40-43`),
  a test kolejności istnieje tylko dla dodania.
- **T3 (niska)** brak testu komponentu sprawdzającego, gdzie trafia fokus po zapisie
  i usunięciu korekty (A1).

## Code Smells

- **S1 (niska)** `api/src/Modules/Orders/StoneFlow.Orders.Domain/Payments/Refund.cs:7` i `:39`:
  ta sama reguła „wkład w obniżenia = kwota zwrotu sprzedaży, inaczej 0” jest zapisana dwa razy
  (`RefundDetails.SalesReduction` i `Refund.SalesReduction`). To powtórzona wiedza biznesowa;
  wystarczy jedna statyczna funkcja domeny dla `(kind, amount)`.
- **S2 (niska)** `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderLock.cs:29-34`:
  pod blokadą idą cztery osobne zapytania agregujące (wpłaty, zwroty, zwroty sprzedaży,
  korekty), także w `EditOrder`, które potrzebuje tylko `R`. Indeks `(OrderId) INCLUDE (Amount)`
  na `Refunds` nie zawiera `Kind`, więc suma zwrotów sprzedaży czyta też wiersze tabeli. Przy
  obecnej skali to bez znaczenia, ale wydłuża czas trzymania blokady.
- **S3 (niska)** `api/src/Modules/Orders/StoneFlow.Orders.Application/EditOrder/EditOrder.cs:153-154`
  oraz `PaymentSummary.cs` (`MinimumValueFor`): warunek „nowa < stara” jest sprawdzany i
  w handlerze (żeby nie brać blokady), i w domenie. Działa poprawnie; dublowanie jest
  uzasadnione tym, że handler decyduje o blokadzie, ale warto to zaznaczyć komentarzem w jednym
  miejscu.

## Recommendation

- Merge as-is. Poprawki C1, A1 i S1 oraz testy T1–T2 są zalecane, ale nie blokują merge.
  Ryzyko high: przed merge wymagany jest przegląd przez człowieka i zielone CI.

Weryfikacja wykonana w tym przeglądzie:
- `dotnet test --project api/tests/StoneFlow.Orders.Tests -c Release`: 157/157 zakończonych
  powodzeniem.
- `npm test -- --watch=false` w `app/`: 38 plików, 374/374 testy zakończone powodzeniem.
- Testów integracyjnych i pełnego przebiegu nie uruchamiałem (ograniczenie zasobów). Wynik
  465/465 podał autor implementacji; ten przegląd go nie potwierdza.
