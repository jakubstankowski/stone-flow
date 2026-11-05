# Implementation done — SF-9

## Summary

W module Orders dostarczono:

- **Wpłaty i zwroty:** encje `Payment` i `Refund`, migracja `PaymentsAndRefunds`, dodawanie
  z kluczem idempotencji, poprawa i usuwanie z kontrolą `rowversion`.
- **Blokada zamówienia** (`IOrderLock`, `UPDLOCK`) przy każdym zapisie wpisu.
- **Wyliczenia w backendzie** (`PaymentSummary`): Zapłacono, stan płatności, Do zapłaty,
  Nadpłata, Do zwrotu.
- **Audyt** `Created`, `Updated` i `Deleted` z rodzicem `Order`.
- **Rozszerzone odczyty:** szczegóły zamówienia oraz lista z kolumnami i filtrem stanu.

We frontendzie powstała sekcja „Płatności” w szczegółach zamówienia: formularze, usuwanie
z potwierdzeniem, ostrzeżenia, a do tego ostrzeżenie w dialogu anulowania oraz kolumny i filtr
na liście.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Wpłata: kwota, data, metoda, „Przyjął” przy gotówce, wiele wpłat | Implemented | |
| Zwrot z wymaganym rodzajem bez domyślnej wartości, kwotą, datą, metodą i powodem | Implemented | |
| Wyliczanie Zapłacono, stanu, Do zapłaty, Nadpłaty i Do zwrotu w backendzie | Implemented | `PaymentSummary`; lista liczy przez ten sam kod z sum SQL |
| Ostrzeżenia: nadpłata, do zwrotu, wpłata do anulowanego, anulowanie z wpłatami | Implemented | Dialog anulowania reaguje na „ma wpłaty” (plan, N7) |
| Poprawa i usunięcie z potwierdzeniem i audytem | Implemented | Usunięcie fizyczne, `Deleted` zapisuje wszystkie pola |
| Odrzucenie zwrotu ponad Zapłacono i zmian dających ujemne Zapłacono | Implemented | `422` z `available` |
| Daty przyszłe odrzucane (Europe/Warsaw), daty sprzed zamówienia dozwolone | Implemented | |
| Zmiana rodzaju zwrotu z audytem | Implemented | |
| Kolumny i filtr stanu płatności na liście | Implemented | |
| Idempotencja dodania i serializacja zapisów | Implemented | |

## Files

### Created

- **Backend, Domain:** `api/src/Modules/Orders/StoneFlow.Orders.Domain/Payments/`
  (`PaymentVocabulary.cs`, `OrderEntry.cs`, `Payment.cs`, `Refund.cs`, `PaymentSummary.cs`).
- **Backend, Application:** `api/src/Modules/Orders/StoneFlow.Orders.Application/`:
  - `Payments/` (`EntryInput.cs`, `PaymentInput.cs`, `RefundInput.cs`, `PaymentAudit.cs`,
    `OrderEntryWriter.cs`);
  - foldery `AddPayment/`, `EditPayment/`, `DeletePayment/`, `AddRefund/`, `EditRefund/`,
    `DeleteRefund/`;
  - `WarsawTime.cs`.
- **Backend, Infrastructure:** `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/`
  (`OrderEntryConfiguration.cs`, `OrderLock.cs`, `OrderEntryStore.cs`,
  `Migrations/20261105101805_PaymentsAndRefunds.cs` + Designer).
- **Backend, Api:** `api/src/Modules/Orders/StoneFlow.Orders.Api/Endpoints/`
  (`PaymentsEndpoints.cs`, `PaymentContracts.cs`).
- **Testy backendu:**
  - `api/tests/StoneFlow.Orders.Tests/`: `Domain/PaymentSummaryTests.cs`,
    `Domain/PaymentTests.cs`, `Application/PaymentInputTests.cs`,
    `Application/PaymentAuditTests.cs`, `TestPayments.cs`;
  - `api/tests/StoneFlow.Api.IntegrationTests/Orders/`: `PaymentsApiTests.cs`,
    `OrdersTestSupport.cs`.
- **Frontend:** `app/src/app/features/orders/` (`order-payments.*`, `payment-form.*`,
  `refund-form.*`, `entry-form-model.*`, `entry-form-support.ts`).

### Modified

- **Backend:**
  - Application: `Ports.cs`, `OrderInput.cs`, `OrderCodes.cs`, `OrderAudit.cs`, `GetOrder.cs`,
    `ListOrders.cs`, `CreateOrder.cs`, `EditOrder.cs`, `OrderStatusAction.cs`;
  - Infrastructure: `OrderReader.cs`, `OrderStore.cs`, `OrdersDbContext.cs`,
    `DependencyInjection.cs`, snapshot migracji;
  - Api: `OrderProblems.cs`, `OrderContracts.cs`, `OrdersEndpoints.cs`, `OrdersModule.cs`;
  - testy: `OrdersApiTests.cs`.
- **Frontend:** `order-models.ts`, `orders-api.ts`, `orders.service.ts`,
  `order-details-page.*`, `orders-list-page.*`, specyfikacje,
  `app/src/testing/orders-testing.ts`.

## Planned Units

| Item from the plan | Status |
|---|---|
| `Payment`, `Refund`, słowniki, `PaymentSummary` | Exists |
| `IOrderLock` / `OrderLock` | Exists |
| Store wpłat i zwrotów | Exists (jako generyczny `OrderEntryStore<T>`) |
| Handlery Add, Edit i Delete dla wpłat i zwrotów | Exists |
| Rozszerzenie `GetOrder` i `ListOrders` | Exists |
| Migracja `PaymentsAndRefunds` | Exists |
| `PaymentsEndpoints`, `PaymentContracts`, kody problemów | Exists |
| `order-payments`, `payment-form`, `refund-form` | Exists |
| Zmiany szczegółów (N6, N7) i listy | Exists |

## Database

Orders: migracja `20261105101805_PaymentsAndRefunds`, która tworzy:

- `orders.Payments` i `orders.Refunds`;
- w obu tabelach FK do `orders.Orders` z `NO ACTION`, `CHECK (Amount > 0)`, unikalny
  filtrowany `CreationRequestId`, `rowversion` i indeks `(OrderId) INCLUDE (Amount)`;
- w `Payments` dodatkowo `CHECK` dla `ReceivedBy`.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| `PaymentSummaryTests`, `PaymentTests`, `PaymentInputTests`, `PaymentAuditTests` | Exists | `StoneFlow.Orders.Tests`: 129/129 |
| `PaymentsApiTests` (progresja stanu, limity, wyścigi, idempotencja, konflikty, `404`, audyt i jego wycofanie, lista i filtr, autoryzacja, XSRF, niezmiennik 1) | Exists | Pełny przebieg po poprawkach: 422/422 |
| Testy po przeglądzie: `Amount_columns_are_decimal_18_2`, `Parallel_payment_changes_cannot_make_paid_negative` (×2) | Exists | Uruchomione celowo: 10/10 |
| `OrdersApiTests.Create_does_not_create_money_records_or_payment_state` (zmieniony) | Exists | Pass |
| Testy frontendu (`order-payments`, `payment-form`, `refund-form`, `entry-form-model`, szczegóły, lista, API, serwis) | Exists | `npm test`: 36 plików, 355/355 |

**Testy mutacyjne:**
- usunięcie `UPDLOCK` łamie testy wyścigu zwrotów oraz poprawy i usunięcia wpłat;
- przeniesienie klucza po limit, pominięcie ponownego odczytu po duplikacie, połknięcie błędu
  audytu i brak `OriginalValue` łamią odpowiednie testy.

## Verification

| Command | Outcome |
|---|---|
| `dotnet format stone-flow.slnx --verify-no-changes` | Passed |
| `dotnet build stone-flow.slnx -c Release` | Passed, 0 ostrzeżeń |
| `dotnet test --solution stone-flow.slnx -c Release` (commit `2f48ee9`) | Passed, 419/419 |
| `dotnet test --solution stone-flow.slnx -c Release` (po poprawkach z przeglądu) | Passed, 422/422 |
| `npm run typecheck`, `npm run lint`, `npm run format:check`, `npm run build` | Passed |
| `npm test -- --watch=false` | Passed, 355/355 |
| Scenariusz ręczny w przeglądarce | Not run |
| CI na PR | Not run (w toku po otwarciu PR) |

## Deviations

- `version` w `DELETE` jest w ciele JSON `{ version }`, a nie w zapytaniu (plan, N2).
- Skrót idempotencji obejmuje `orderId`. Klucz znaleziony przy wpisie innego zamówienia daje
  `409` (plan, N1).
- Zamiast osobnych store i konfiguracji wpłat i zwrotów jest jeden generyczny
  `OrderEntryStore<T>`, jeden plik `OrderEntryConfiguration.cs` i wspólny
  `OrderEntryWriter<T>`. Reguły limitu są w `PaidRules`/`EntryTotals`.
- Enumy są zapisywane w bazie jako nazwy PascalCase (`Cash`, `SalesReturn`), tak jak w SF-8.
- `OrderLock` to dwa polecenia: blokada, a potem sumy.
- `201 Location` wskazuje zamówienie.
- `OrderSaveStatus` i `OrderSaveResult` przemianowano na `StoreSaveStatus` i
  `StoreSaveResult`.
- Frontend:
  - metody w formularzach nie mają wartości domyślnej;
  - podczas edycji zamówienia sekcja płatności jest widoczna, ale jej akcje są wyłączone;
  - komunikaty `422` i `409` frontend tworzy sam.

## Additional Implementation

Additional implementation not covered by the original documents:

- helpery testowe `EntryStoreHooks`, `HookedEntryStore<T>` i `Barrier()`;
- `WarsawTime.Today`;
- licznik operacji wpisów w `order-payments`, który blokuje „Edytuj”.

## Open Items

- Przed merge wymagany jest przegląd człowieka (ryzyko high).
- CI na PR musi przejść (SQL Server z uwierzytelnianiem SQL).
- Scenariusz ręczny w przeglądarce nie został wykonany.
- Limit obniżeń sprzedaży (PLAT-05: zwrot sprzedaży 1200 przy wartości 1000) należy do SF-10,
  które musi używać `IOrderLock`.
- Czy `receivedBy` podlega anonimizacji, rozstrzygnie SF-22.
- S2 z przeglądu (podwójne liczenie sum przy aktywnym filtrze) pozostaje bez zmian, bo dotyczy
  tylko wydajności.
