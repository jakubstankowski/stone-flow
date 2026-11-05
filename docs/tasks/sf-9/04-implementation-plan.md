# Implementation Plan — SF-9

Tryb: **fullstack**. Zadanie zmienia storage, API i UI oraz dodaje nowy kontrakt. Nie dzielę go
na części. Backend i frontend powstają równolegle według kontraktu z `02-spec.md`, z korektami
z sekcji 5 tego planu, a na końcu jest krok integracji.

## 1. Scope

Plan obejmuje:

- **backend:**
  - encje `Payment` i `Refund` w module Orders oraz `PaymentSummary`;
  - migrację `PaymentsAndRefunds`;
  - sześć tras zapisu wpisów;
  - rozszerzenie `GET /api/orders/{id}` i `GET /api/orders`;
  - blokadę zamówienia;
  - audyt Payment i Refund z rodzicem Order;
- **frontend:**
  - sekcję „Płatności” w szczegółach zamówienia, z formularzami i usuwaniem;
  - ostrzeżenie w dialogu anulowania;
  - kolumny i filtr na liście.

Kod SF-7 i moduły spoza Orders się nie zmieniają.

## 2. Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Domain | `Orders.Domain/Payments` | `Payment`, `Refund`, słowniki, `PaymentSummary`, reguła „Zapłacono po zmianie ≥ 0” |
| Application | `Orders.Application` (`AddPayment/`, `EditPayment/`, `DeletePayment/`, `AddRefund/`, `EditRefund/`, `DeleteRefund/`, `GetOrder/`, `ListOrders/`, `Abstractions/`) | Przypadki użycia, walidacja, idempotencja, audyt |
| Infrastructure | `Orders.Infrastructure/Persistence` | Tabele, migracja, `OrderLock`, store wpisów, odczyt sum |
| Api | `Orders.Api/Endpoints` | Trasy, kontrakty, kody problemów |
| Tests | `StoneFlow.Orders.Tests`, `Api.IntegrationTests/Orders` | Reguły i API |
| Frontend | `features/orders` | Sekcja płatności, formularze, lista, dialog anulowania |

## 3. Files

### Backend

| File path | Action | Purpose |
|---|---|---|
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Payments/PaymentVocabulary.cs` | create | `PaymentMethod` (Transfer, Blik, Cash, CashOnDelivery), `RefundMethod` (Transfer, Blik, Cash), `RefundKind` (SalesReturn, MoneyReturn), `PaymentState` (Unpaid, PartiallyPaid, Paid) |
| `.../Domain/Payments/Payment.cs`, `Refund.cs` | create | Encje z `OrderId`, fabryką, `Edit` zwracającym listę zmienionych pól i `Snapshot` do audytu `Deleted` |
| `.../Domain/Payments/PaymentSummary.cs` | create | `PaymentSummary.Compute(value, isCancelled, paymentsTotal, refundsTotal)` zwraca `paid`, `state?`, `amountDue?`, `overpayment?`, `toRefund?`; `PaidAfter(...)` służy do sprawdzania reguł |
| `.../Application/Abstractions/Ports.cs` | modify | `IOrderLock.LockAsync(orderId)` zwraca `LockedOrder(Number, Value, IsCancelled, PaymentsTotal, RefundsTotal)?`; `IPaymentStore` i `IRefundStore` (Add, Load, Save z `OriginalValue`, Delete), `FindByCreationRequest` |
| `.../Application/Payments/PaymentInput.cs`, `RefundInput.cs` | create | Walidacja, normalizacja i skrót (z `orderId`, N1) |
| `.../Application/Payments/PaymentAudit.cs` | create | Created, Updated i Deleted z rodzicem Order |
| `.../Application/{AddPayment,EditPayment,DeletePayment,AddRefund,EditRefund,DeleteRefund}/*.cs` | create | Komendy, wyniki, handlery |
| `.../Application/GetOrder/GetOrder.cs` | modify | `payment` (z `PaymentSummary`), `payments[]`, `refunds[]` |
| `.../Application/ListOrders/ListOrders.cs` | modify | Parametr `paymentState`, pola `paid` i `paymentState` w wierszu |
| `.../Infrastructure/Persistence/PaymentConfiguration.cs`, `RefundConfiguration.cs` | create | Mapowanie, check constraints, indeksy, FK, `rowversion` |
| `.../Infrastructure/Persistence/OrderLock.cs` | create | `SELECT … WITH (UPDLOCK, ROWLOCK)` z sumami wpisów (`COALESCE`), w transakcji jednostki pracy |
| `.../Infrastructure/Persistence/PaymentStore.cs`, `RefundStore.cs` | create | Zapis, wersja, tłumaczenie `DbUpdateConcurrencyException` i `2601/2627` |
| `.../Infrastructure/Persistence/OrderReader.cs` | modify | Sumy w projekcji, filtr stanu jako predykat SQL; kolumny wyliczane przez `PaymentSummary` z sum (N5) |
| `.../Infrastructure/Persistence/OrdersDbContext.cs`, `DependencyInjection.cs` | modify | `DbSet`, rejestracja portów |
| `.../Infrastructure/Persistence/Migrations/*_PaymentsAndRefunds.*` | create | Migracja i snapshot |
| `.../Api/Endpoints/PaymentsEndpoints.cs`, `PaymentContracts.cs` | create | Trasy wpłat i zwrotów |
| `.../Api/Endpoints/OrdersEndpoints.cs`, `OrderContracts.cs` | modify | Rozszerzone DTO szczegółów i listy, `paymentState` w query; komentarz „Usuwania nie ma” zawężony do zamówień (N3) |
| `.../Api/OrderProblems.cs` | modify | `entry_not_found`, `entry_conflict`, `paid_would_be_negative`, `refund_exceeds_paid` (+ `available`), `idempotency_key_reused` (+ `paymentId` / `refundId`) |
| `.../Api/OrdersModule.cs` | modify | Mapowanie `PaymentsEndpoints` |
| `api/tests/StoneFlow.Orders.Tests/Domain/PaymentSummaryTests.cs`, `PaymentTests.cs` | create | Progi stanu i reguły |
| `api/tests/StoneFlow.Orders.Tests/Application/PaymentInputTests.cs`, `PaymentAuditTests.cs` | create | Walidacja, skrót, audyt |
| `api/tests/StoneFlow.Api.IntegrationTests/Orders/PaymentsApiTests.cs` | create | API end-to-end |
| `api/tests/StoneFlow.Api.IntegrationTests/Orders/OrdersApiTests.cs` | modify | `Create_does_not_create_money_records_or_payment_state`: zamiast braku pól sprawdza brak wierszy w `orders.Payments` i `orders.Refunds` oraz `payment.paid = "0.00"`, `state = unpaid` (N3) |

### Frontend

| File path | Action | Purpose |
|---|---|---|
| `app/src/app/features/orders/order-models.ts` | modify | Typy `PaymentEntry`, `RefundEntry`, `OrderPaymentSummary`, kody i etykiety metod, rodzajów i stanów; tony stanów (N9) |
| `app/src/app/features/orders/orders-api.ts` | modify | Metody wpisów; `DELETE` z ciałem `{ version }` (N2); `paymentState` w query |
| `app/src/app/features/orders/orders.service.ts` | modify | Przepływy wpisów, filtr stanu |
| `app/src/app/features/orders/payment-form.ts` / `.html` | create | Formularz wpłaty, z polem „Przyjął” tylko przy gotówce |
| `app/src/app/features/orders/refund-form.ts` / `.html` | create | Formularz zwrotu (radio rodzaju bez domyślnej wartości, opisy) |
| `app/src/app/features/orders/order-payments.ts` / `.html` | create | Sekcja „Płatności”: podsumowanie, ostrzeżenia, tabele, akcje, dialog usuwania |
| `app/src/app/features/orders/order-details-page.ts` / `.html` | modify | Osadzenie sekcji, wzajemne wykluczenie edycji zamówienia i formularzy wpisów (N6), ostrzeżenie w dialogu anulowania (N7), komentarz o usuwaniu (N3) |
| `app/src/app/features/orders/orders-list-page.ts` / `.html` | modify | Kolumny Zapłacono i Stan płatności, filtr stanu |
| `app/src/app/features/orders/*.spec.ts`, `app/src/testing/orders-testing.ts` | create/modify | Testy i dane testowe |

## 4. Implementation steps

**Backend**

1. **B1, domena:** słowniki, `Payment`, `Refund`, `PaymentSummary`, testy jednostkowe.
2. **B2, aplikacja:** porty, `PaymentInput` i `RefundInput`, `PaymentAudit`, testy.
3. **B3, handlery** w kolejności sprawdzeń:
   - **Add:** walidacja (`400`), blokada (`404`), klucz idempotencji (repeat albo reused,
     także gdy wpis należy do innego zamówienia), dla zwrotu limit (`422`), zapis, audyt.
   - **Edit i Delete:** walidacja (`400`), blokada (`404 order_not_found`), wpis należący do
     zamówienia (`404 entry_not_found`), `version` (`409`), reguła (`422`), brak zmian, zapis
     albo usunięcie, audyt.
   - `commitWhen` przyjmuje tylko sukces.
4. **B4, infrastruktura:** konfiguracje, `OrderLock`, store wpisów, rozszerzenie
   `OrderReader`, DI, migracja `dotnet ef migrations add PaymentsAndRefunds`.
5. **B5, API:** trasy, kontrakty, problemy, rozszerzone DTO.
6. **B6, testy integracyjne** (sekcja 8) i poprawka testu SF-8.
7. **B7, kontrole:** `dotnet format`, build Release, pełny `dotnet test`.

**Frontend**

1. **F1:** modele i klient API, z testami.
2. **F2:** serwis.
3. **F3:** `payment-form`, `refund-form` i `order-payments`.
4. **F4:** zmiany w szczegółach: osadzenie sekcji, N6, N7.
5. **F5:** kolumny i filtr na liście.
6. **F6, kontrole:** `typecheck`, `lint`, `format:check`, `test`, `build`.

**Integracja**

1. **I1:** porównać kody problemów, klucze walidacji i kształty DTO po obu stronach.

## 5. Data & contracts

Korekty i doprecyzowania względem `02-spec.md`, rozstrzygające uwagi z przeglądu:

- **N1, idempotencja:**
  - skrót obejmuje `orderId`;
  - klucz znaleziony przy wpisie innego zamówienia daje `409 idempotency_key_reused`;
  - klucz sprawdza się przed limitem zwrotu;
  - klucze wpłat i zwrotów są niezależne, bo żyją w osobnych tabelach;
  - po fizycznym usunięciu wpisu spóźnione powtórzenie z tym samym kluczem utworzy go
    ponownie (świadoma granica);
  - brak lub zły nagłówek daje `400 idempotency_key_required`, jak w SF-8.
- **N2, `DELETE` z ciałem:** `version` idzie w ciele JSON `{ version }`, tak jak w `cancel` i
  `restore` z SF-8, a nie w zapytaniu. Unika to problemu z `+`, `/` i `=` w base64 w URL.
  Brak `version` daje `400` pod kluczem `version`. To odstępstwo od spec, zapisane w
  manifeście.
- **Format `version` wpisu:** base64 z 8 bajtów `rowversion`, jak w zamówieniu.
- **`404`:**
  - nieistniejące zamówienie w trasach wpisów daje `order_not_found`;
  - nieistniejący wpis albo wpis innego zamówienia daje `entry_not_found`.
- **Zamówienie anulowane:** dodanie, poprawa i usunięcie wpisów działają bez dodatkowych
  ograniczeń.
- **N4, `IOrderLock`:** jedyna droga do blokady zamówienia przy zapisach wpisów. Zwraca numer
  zamówienia do etykiety audytu oraz sumy wpisów policzone po uzyskaniu blokady. SF-10 musi
  używać tego samego portu przy korektach sprzedaży.
- **N5, lista:**
  - SQL zwraca `PaymentsTotal` i `RefundsTotal` (`COALESCE(SUM(...), 0)`);
  - `paid` i `paymentState` w wierszu wylicza `PaymentSummary` w aplikacji;
  - filtr `paymentState` to predykat SQL odpowiadający regułom `PaymentSummary`;
  - test zgodności obejmuje wszystkie progi i przypadki brzegowe: brak wpisów, wartość 0,
    `P = V`, `P > V`, anulowane z `P = 0` i `P > 0`.
- **N7, ostrzeżenie w dialogu anulowania:** kryterium to „zamówienie ma wpłaty”
  (`payments.length > 0`, bez arytmetyki we frontendzie), zgodnie z PRD. Tekst podaje kwotę
  `payment.paid` z backendu.
- **N9, tony stanów:** Nieopłacone `warning`, Częściowo opłacone `info`, Opłacone `success`.
  Badge zawsze ma tekst.
- **Słowniki:**
  - metody wpłaty: `transfer`, `blik`, `cash`, `cashOnDelivery`;
  - metody zwrotu: `transfer`, `blik`, `cash`;
  - rodzaje zwrotu: `salesReturn`, `moneyReturn`;
  - stany: `unpaid`, `partiallyPaid`, `paid`; w filtrze dodatkowo `none`.

## 6. Edge cases & null safety

| Edge case (spec) | Gdzie obsłużony |
|---|---|
| Progi stanu, 0 zł, nadpłata, anulowane, „Do zwrotu” | `PaymentSummary` i `PaymentSummaryTests`; test zgodności listy ze szczegółami |
| Zwrot ponad Zapłacono, poprawa lub usunięcie dające ujemne Zapłacono | `PaymentSummary.PaidAfter` w handlerach pod blokadą |
| Wyścig zwrotów | `OrderLock` i test równoległy (2 × 600 przy 1000 daje 1 zapis i 1 × `422`) |
| Data z przyszłości, granica doby według Europe/Warsaw | `PaymentInput`/`RefundInput` z `TimeProvider`; test z `FakeTimeProvider` o 23:30 UTC |
| „Przyjął” przy innej metodzie | Walidacja `400`; UI czyści pole przy zmianie metody |
| Wpis innego zamówienia | `404 entry_not_found` |
| Dwie karty, wpis już usunięty | `409 entry_conflict`, `404` |
| Klucz użyty przy innym zamówieniu | `409 idempotency_key_reused` (N1) |
| Utrata niezapisanych zmian zamówienia (N6) | Gdy edycja zamówienia jest otwarta, akcje wpisów są wyłączone z opisem; gdy otwarty jest formularz wpisu, „Edytuj” jest wyłączone |
| Zamówienie ze sklepu | Te same ścieżki co dla ręcznego; test z zamówieniem zasianym SQL-em |

**Null safety:** `receivedBy` i pola podsumowania są nullable. `LockedOrder` jest `null`, gdy
zamówienie nie istnieje, a sumy są zawsze nie-null (`COALESCE`).

## 7. Invariants

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | Wpis nie zmienia `orders.Orders` (wartość, status); pobranie to tylko metoda | `Payment_does_not_change_order_value_or_status`, `Cash_on_delivery_order_without_payment_is_unpaid` |
| 2. Kwoty decimal | `decimal(18,2)`, `PaymentSummary` na `decimal`, frontend bez arytmetyki | `PaymentSummaryTests`, test typów kolumn w migracji |
| 3. Dostęp przez API | Trasy bez `AllowAnonymous`; XSRF dla `POST`, `PUT` i `DELETE` | `Payment_routes_require_session`, `Payment_writes_require_xsrf`; `EndpointSecurityTests` |
| 4. Ponowienia nie duplikują | Unikalny `CreationRequestId` w obu tabelach | `Parallel_adds_with_same_key_create_one_entry` |
| 5. Audyt | `IAuditLog` w każdym zapisie; `Deleted` ze wszystkimi polami | `Entry_lifecycle_writes_created_updated_deleted_audit`, `Audit_failure_rolls_back_entry_changes` |
| 6. Sekrety | Logi tylko z identyfikatorami | Przegląd kodu |
| 7. Granice | Tylko `orders.*`; audyt przez `Shared.Application` | Testy architektury |
| 8. Brak danych identyfikujących | Fikcyjne dane | Przegląd kodu |

## 8. Tests

| Test | Type | What it proves |
|---|---|---|
| `PaymentSummaryTests` | unit | Wszystkie progi i ostrzeżenia, w tym 0 zł, anulowane, nadpłata, `PaidAfter` |
| `PaymentTests` | unit | Edycja wylicza zmiany; zmiana rodzaju zwrotu; `Snapshot` |
| `PaymentInputTests` | unit | Kwota > 0, 2 miejsca po przecinku, zakres; data nie z przyszłości (granica Europe/Warsaw); metoda; „Przyjął” tylko przy gotówce; rodzaj wymagany; powód; skrót z `orderId` |
| `PaymentAuditTests` | unit | Etykieta, rodzic, `IsPersonalData` dla `receivedBy`, `Deleted` ze wszystkimi polami |
| `PaymentsApiTests.State_progression_*` | integration | 300 + 700 przy 1000 daje Nieopłacone, potem Częściowo (Do zapłaty 700), potem Opłacone; nadpłata; zwrot pieniędzy; 0 zł; anulowane i „Do zwrotu” |
| `PaymentsApiTests.Refund_limit_*`, `Edit_delete_negative_paid_*` | integration | `422` z `available` i brak zmian |
| `PaymentsApiTests.Parallel_refunds_respect_limit` | integration | Blokada zamówienia |
| `PaymentsApiTests.Parallel_adds_with_same_key_create_one_entry`, `Key_reused_for_other_order_conflicts` | integration | Idempotencja (N1) |
| `PaymentsApiTests.Conflict_*`, `Entry_of_other_order_is_404`, `Delete_requires_version` | integration | Współbieżność i `404` |
| `PaymentsApiTests.Entry_lifecycle_writes_*_audit`, `Audit_failure_rolls_back_entry_changes` | integration | Niezmiennik 5 |
| `PaymentsApiTests.List_payment_columns_and_filter_match_details` | integration | Zgodność listy ze szczegółami (N5) |
| `PaymentsApiTests.Payment_routes_require_session`, `Payment_writes_require_xsrf` | integration | Niezmiennik 3 |
| `PaymentsApiTests.Payment_does_not_change_order_value_or_status`, `Cash_on_delivery_order_without_payment_is_unpaid` | integration | Niezmiennik 1 |
| `OrdersApiTests.Create_does_not_create_money_records_or_payment_state` (zmieniony) | integration | Utworzenie zamówienia nie tworzy wpisów pieniężnych |
| `order-payments.spec.ts` | component | Podsumowanie, ostrzeżenia, puste stany, dialog usuwania, konflikt, komunikaty `422` |
| `payment-form.spec.ts`, `refund-form.spec.ts` | component | „Przyjął” tylko przy gotówce i czyszczenie; rodzaj bez wartości domyślnej; `max` daty; `busy` i jedno żądanie |
| `order-details-page.spec.ts` (zmieniony) | component | Ostrzeżenie w dialogu anulowania przy wpłatach; wzajemne wykluczenie edycji i formularzy |
| `orders-list-page.spec.ts`, `orders-api.spec.ts` (zmienione) | unit/component | Kolumny, filtr `paymentState` w query, `DELETE` z ciałem |

## 9. Verification

- **Backend:**
  - `dotnet format stone-flow.slnx --verify-no-changes`;
  - `dotnet build stone-flow.slnx -c Release` (0 ostrzeżeń);
  - `dotnet test --solution stone-flow.slnx -c Release`.
- **Frontend**, w `app/`: `npm run typecheck`, `npm run lint`, `npm run format:check`,
  `npm test -- --watch=false`, `npm run build`.
- **Manual:** scenariusz z sekcji Verification w spec.
- **Przegląd człowieka przed merge (ryzyko high):** blokada i limity, fizyczne usuwanie
  z audytem, idempotencja.
- **CI** na PR musi przejść.

## 10. Risks

- **`UPDLOCK` na zamówieniu a edycja zamówienia z SF-8:** edycja aktualizuje ten sam wiersz,
  więc chwilowo czeka na blokadę. Kolejność blokad jest zawsze taka sama (zamówienie, potem
  wpis), więc zakleszczenie nie grozi.
- **Fizyczne usuwanie wpisów:** odtworzenie jest możliwe tylko z audytu.
- **Limit obniżeń sprzedaży (PLAT-05: zwrot sprzedaży 1200 przy wartości 1000 i wpłatach 1200
  jest odrzucany) to SF-10 (N8).** W SF-9 taki zwrot przejdzie, bo mieści się w Zapłacono.
- **`receivedBy` jako dane osobowe:** czy anonimizacja klienta (SF-22) obejmuje to pole,
  rozstrzygnie SF-22.
