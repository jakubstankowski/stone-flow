# Implementation done — SF-10

## Summary

Moduł Orders dostał korektę sprzedaży. Encja `SalesCorrection` i migracja `SalesCorrections`
obsługują dodawanie z kluczem idempotencji oraz poprawę i usuwanie z kontrolą `rowversion`.
Każda zmiana jest audytowana z rodzicem `Order`.

Limit obniżeń sprzedaży (`SalesReductionRules`) jest sprawdzany pod `IOrderLock`:

- przy dodaniu i poprawie korekty;
- przy zwrocie sprzedaży, także przy zmianie rodzaju zwrotu;
- przy ręcznym obniżeniu wartości zamówienia.

Szczegóły zamówienia zwracają `salesCorrections[]` i `salesReductions` z nadwyżką.

Frontend dostał:

- sekcję „Korekty sprzedaży”, osobną od wpłat i zwrotów;
- alert o nadwyżce;
- opisy różnic w formularzach;
- komunikaty limitu z kwotą.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Korekta: kwota > 0, data (bez przyszłych), powód, bez metody; zamówienia ręczne, ze sklepu i anulowane | Implemented | |
| Poprawa i usunięcie z potwierdzeniem i audytem | Implemented | Usunięcie fizyczne, pełny ślad `Deleted` |
| Limit: korekty, zwroty sprzedaży (także zmiana rodzaju), ręczne obniżenie wartości | Implemented | Reguła N1 z planu; `available` / `minimumValue` |
| Ostrzeżenie o nadwyżce wyliczane ze stanu | Implemented | `salesReductions.excess` |
| Opisy różnic w formularzach | Implemented | Formularz korekty, zwrotu (N6), pole wartości |
| Korekta osobno od wpłat i zwrotów | Implemented | |
| Korekta nie zmienia Zapłacono ani stanu płatności | Implemented | Test PLAT-07 |

## Files

**Backend**

Utworzone:

- `Domain/Payments/SalesCorrection.cs`;
- `Application/Payments/SalesCorrectionInput.cs`;
- foldery `AddSalesCorrection/`, `EditSalesCorrection/`, `DeleteSalesCorrection/`;
- migracja `20261105122504_SalesCorrections` (+ Designer);
- testy: `SalesReductionRulesTests.cs`, `SalesCorrectionInputTests.cs`,
  `SalesCorrectionsApiTests.cs`, `OrderEntryRequests.cs`.

Zmienione:

- `PaymentSummary.cs`, `Refund.cs`;
- `Ports.cs`, `AddRefund`, `EditRefund`, `EditOrder`, `GetOrder`;
- `EntryInput.cs`, `RefundInput.cs`, `OrderEntryWriter.cs`, `PaymentAudit.cs`;
- `OrderEntryConfiguration.cs`, `OrderLock.cs`, `OrderReader.cs`, `OrdersDbContext.cs`,
  `DependencyInjection.cs`, snapshot;
- `OrderProblems.cs`, `OrderContracts.cs`, `OrdersEndpoints.cs`, `PaymentContracts.cs`,
  `PaymentsEndpoints.cs`;
- testy: `TestPayments.cs`, `PaymentSummaryTests.cs`, `OrdersTestSupport.cs`,
  `PaymentsApiTests.cs`.

**Frontend** (`app/src/app/features/orders/`)

Utworzone:

- `sales-correction-form.*`;
- `sales-corrections.*`.

Zmienione:

- `order-models.ts`, `orders-api.ts`, `orders.service.ts`, `entry-form-model.ts`;
- `order-payments.*`, `refund-form.*`, `order-form.*`, `order-details-page.*`;
- specyfikacje testów;
- `app/src/testing/orders-testing.ts`.

## Planned Units

| Item from the plan | Status |
|---|---|
| `SalesCorrection`, `SalesReductionRules`, rozszerzone `EntryTotals` / `LockedOrder` | Exists |
| Handlery korekt; limit w `AddRefund`, `EditRefund`, `EditOrder` | Exists |
| Audyt z typem wpisu (N5) | Exists |
| Migracja `SalesCorrections` | Exists |
| Trasy `/sales-corrections`, problem `sales_reduction_limit_exceeded` | Exists (w `PaymentsEndpoints.cs`) |
| Sekcja i formularz korekt, alert, opisy, komunikaty limitu | Exists |

## Database

Orders, migracja `20261105122504_SalesCorrections`. Tworzy tabelę `orders.SalesCorrections`:

- `Amount decimal(18,2)` z `CHECK > 0`;
- FK do `orders.Orders` z `NO ACTION`;
- unikalny filtrowany `CreationRequestId`;
- `rowversion`;
- indeks `(OrderId) INCLUDE (Amount)`.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| `SalesReductionRulesTests`, `SalesCorrectionInputTests` | Exists | `StoneFlow.Orders.Tests`: 157/157 |
| `SalesCorrectionsApiTests`: 15 testów z implementacji i 2 po przeglądzie (T1, T2) | Exists | 87/87 w przebiegu klas Orders po poprawkach |
| Frontend: `sales-corrections.spec.ts`, `sales-correction-form.spec.ts` i rozszerzone specyfikacje | Exists | `npm test`: 38 plików, 377/377 |

Testy mutacyjne (limit, blokada, limit w `EditOrder`, `AddRefund`, `EditRefund`) łamią
odpowiednie testy.

## Verification

| Command | Outcome |
|---|---|
| `dotnet format stone-flow.slnx --verify-no-changes` | Passed |
| `dotnet build stone-flow.slnx -c Release` | Passed, 0 ostrzeżeń |
| `dotnet test --solution stone-flow.slnx -c Release` (commit `7634f55`) | Passed, 465/465 |
| Testy integracyjne Orders, Payments i SalesCorrections po poprawkach z przeglądu | Passed, 87/87 (OrdersApiTests, PaymentsApiTests, SalesCorrectionsApiTests) |
| `npm run typecheck`, `lint`, `format:check`, `build` | Passed |
| `npm test -- --watch=false` | Passed, 377/377 |
| Scenariusz ręczny w przeglądarce | Not run |
| CI na PR | Not run (w toku po otwarciu PR) |

## Deviations

- Trasy korekt są w `PaymentsEndpoints.cs`, nie w osobnym pliku.
- `PaidRuleViolation` przemianowano na `EntryRuleViolation`, z nowym rodzajem
  `SalesReductionLimitExceeded`.
- `EntryTotals` zachowuje konstruktor `(Payments, Refunds)` i dostał nowe właściwości `init`.
- Helpery testów integracyjnych przeniesiono do `OrderEntryRequests.cs`.
- We frontendzie logika korekt jest we wspólnym stanie `order-payments`. Jeden formularz
  wpisu jest otwarty naraz w obu sekcjach. Sekcja korekt pokazuje też sumy `salesReductions`.

## Additional Implementation

Additional implementation not covered by the original documents:

- test typu kolumny obejmuje `orders.SalesCorrections.Amount`;
- `Refund.SalesReductionOf`;
- `SalesReductionRules.LowersValue`.

## Open Items

- Przed merge wymagany jest przegląd człowieka (ryzyko high).
- CI na PR musi przejść.
- Scenariusz ręczny w przeglądarce nie był wykonany.
- Uwaga S2 z przeglądu: `OrderLock` wysyła 4 zapytania agregujące, a indeks na `Refunds` nie
  zawiera kolumny `Kind`. Dotyczy tylko wydajności i zostaje bez zmian.
- Obniżenie sprzedaży w raportach: SF-19. Ostrzeżenie o nadwyżce po synchronizacji: SF-12.
