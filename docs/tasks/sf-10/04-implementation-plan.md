# Implementation Plan — SF-10

Tryb **fullstack**, bez podziału na części. Backend i frontend powstają równolegle według
`02-spec.md`, z rozstrzygnięciami z sekcji 5. Na koniec jest krok integracji.

## 1. Scope

**Backend:**
- encja `SalesCorrection` i reguły limitu obniżeń;
- migracja `SalesCorrections`;
- trasy `/api/orders/{orderId}/sales-corrections`;
- sprawdzanie limitu przy dodaniu i poprawie zwrotu (SF-9) oraz przy ręcznej zmianie wartości
  (SF-8);
- `salesCorrections[]` i `salesReductions` w szczegółach zamówienia.

**Frontend:**
- sekcja „Korekty sprzedaży”;
- alert o nadwyżce;
- opisy w formularzach;
- obsługa `sales_reduction_limit_exceeded` w formularzach korekty, zwrotu i zamówienia.

## 2. Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Domain | `Orders.Domain/Payments` | `SalesCorrection`, `SalesReductionRules`, rozszerzone `EntryTotals` |
| Application | `Orders.Application` (`AddSalesCorrection/`, `EditSalesCorrection/`, `DeleteSalesCorrection/`, `Payments/OrderEntryWriter.cs`, `Payments/RefundInput.cs` ścieżki zwrotu, `EditOrder/`, `GetOrder/`, `Payments/PaymentAudit.cs`) | Przypadki użycia, limit, audyt |
| Infrastructure | `Persistence` (`OrderEntryConfiguration.cs`, `OrderLock.cs`, `OrderReader.cs`, `OrdersDbContext.cs`, DI, migracja) | Tabela, sumy pod blokadą |
| Api | `Endpoints/PaymentsEndpoints.cs` (lub nowy `SalesCorrectionsEndpoints.cs`), `PaymentContracts.cs`, `OrderContracts.cs`, `OrderProblems.cs` | Trasy i kody |
| Tests | `StoneFlow.Orders.Tests`, `Api.IntegrationTests/Orders` | Reguły i API |
| Frontend | `features/orders` (`order-payments`, nowy `sales-corrections` i `sales-correction-form`, `refund-form`, `order-form`, `order-details-page`, `order-models`, `orders-api`, `orders.service`, `entry-form-model`) | UI |

## 3. Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Payments/SalesCorrection.cs` | create | Encja (dziedziczy `OrderEntry`; pola: kwota, data, powód) |
| `.../Domain/Payments/PaymentSummary.cs` | modify | `EntryTotals` z `Value`, `SalesReturnsTotal` i `CorrectionsTotal`; `SalesReductionRules.Check(totals, oldContribution, newContribution)` i `Excess(totals)`; rodzaj naruszenia `SalesReductionLimitExceeded(available)` |
| `.../Domain/Payments/PaymentVocabulary.cs` | modify | Pola wpisu korekty, jeśli potrzebne (`reason` jest) |
| `.../Application/Abstractions/Ports.cs` | modify | `LockedOrder` z `SalesReturnsTotal` i `CorrectionsTotal`; wynik naruszenia limitu w `EntryWriteResult` |
| `.../Application/Payments/SalesCorrectionInput.cs` | create | Walidacja i skrót (z `orderId`) |
| `.../Application/Payments/OrderEntryWriter.cs` | modify | `check` dostaje pełne `EntryTotals`; nowy wynik `SalesReductionLimitExceeded` |
| `.../Application/AddRefund/`, `EditRefund/` | modify | Po regule Zapłacono sprawdzany jest limit dla wkładu `salesReturn` |
| `.../Application/{AddSalesCorrection,EditSalesCorrection,DeleteSalesCorrection}/*.cs` | create | Handlery przez `OrderEntryWriter<SalesCorrection>` |
| `.../Application/EditOrder/EditOrder.cs` | modify | Blokada i limit przy zmianie wartości (sekcja 5, N2) |
| `.../Application/GetOrder/GetOrder.cs` | modify | `salesCorrections[]`, `salesReductions` |
| `.../Application/Payments/PaymentAudit.cs` | modify | Typ obiektu z rodzaju wpisu (`SalesCorrection`); etykieta „Korekta sprzedaży do {numer}” (N5) |
| `.../Infrastructure/Persistence/OrderEntryConfiguration.cs` | modify | `SalesCorrectionConfiguration` |
| `.../Infrastructure/Persistence/OrderLock.cs` | modify | Sumy zwrotów sprzedaży i korekt po blokadzie |
| `.../Infrastructure/Persistence/OrderReader.cs`, `OrdersDbContext.cs`, `DependencyInjection.cs` | modify | Odczyt korekt, `DbSet`, rejestracja |
| `.../Infrastructure/Persistence/Migrations/*_SalesCorrections.*` | create | Migracja |
| `.../Api/Endpoints/SalesCorrectionsEndpoints.cs` (+ kontrakty) | create | Trasy |
| `.../Api/OrderProblems.cs`, `Endpoints/OrderContracts.cs`, `OrdersModule.cs` | modify | `sales_reduction_limit_exceeded` (+ `available` / `minimumValue`), `idempotency_key_reused` (+ `salesCorrectionId`), DTO |
| `api/tests/StoneFlow.Orders.Tests/Domain/SalesReductionRulesTests.cs` (+ testy wejścia i audytu) | create | Reguły |
| `api/tests/StoneFlow.Api.IntegrationTests/Orders/SalesCorrectionsApiTests.cs` | create | API, limit w zwrotach i w `EditOrder`, wyścigi |
| `app/src/app/features/orders/sales-corrections.ts` / `.html` / `.spec.ts` | create | Sekcja „Korekty sprzedaży” |
| `app/src/app/features/orders/sales-correction-form.ts` / `.html` / `.spec.ts` | create | Formularz |
| `app/src/app/features/orders/order-models.ts`, `orders-api.ts`, `orders.service.ts`, `entry-form-model.ts`, `entry-form-support.ts` | modify | Typy, klient, przepływy, komunikat limitu |
| `app/src/app/features/orders/order-payments.*` | modify | Alert nadwyżki przy podsumowaniu (lub w nowej sekcji) |
| `app/src/app/features/orders/refund-form.*`, `order-form.*`, `order-details-page.*` | modify | Opisy (N6), błąd limitu przy wartości, osadzenie sekcji, wykluczenie z edycją |

## 4. Implementation steps

**Backend**

1. **B1, domena:** `SalesCorrection`, rozszerzone `EntryTotals`, `SalesReductionRules` z testami.
2. **B2, aplikacja:** `LockedOrder`, `OrderEntryWriter` (nowy wynik), `SalesCorrectionInput`,
   `PaymentAudit` (N5).
3. **B3, handlery korekt** oraz limit w `AddRefund`, `EditRefund` i `EditOrder`.
4. **B4, infrastruktura:** konfiguracja, `OrderLock` (sumy), `OrderReader` (korekty i obniżenia),
   DI, migracja `dotnet ef migrations add SalesCorrections`.
5. **B5, API:** trasy, problemy, DTO.
6. **B6, testy integracyjne** (sekcja 8), sprawdzone mutacją dla limitu i blokady.
7. **B7, kontrole:** format, build Release, testy Orders i integracyjne z filtrem, na koniec
   jeden pełny przebieg.

**Frontend**

1. **F1:** modele, klient i serwis.
2. **F2:** formularz i sekcja korekt, osadzone w szczegółach z wykluczeniem z edycją zamówienia.
3. **F3:** alert nadwyżki, opisy w formularzach (N6), komunikaty limitu w formularzu zwrotu,
   korekty i zamówienia.
4. **F4, kontrole:** `typecheck`, `lint`, `format:check`, `test`, `build`.

**Integracja**

1. **I1:** zgodność kodów, rozszerzeń (`available`, `minimumValue`, `salesCorrectionId`)
   i kształtów DTO.

## 5. Data & contracts (rozstrzygnięcia przeglądu)

- **N1, jedna reguła limitu dla korekt i zwrotów.** Niech `R` oznacza sumę obniżeń przed
  zmianą, `R'` po zmianie, a `V` wartość zamówienia. Zmiana jest odrzucana wtedy i tylko
  wtedy, gdy `R' > R` i `R' > V`.
  - Poprawa samej daty lub powodu (`R' = R`) i każde obniżenie zawsze przechodzą, także przy
    istniejącej nadwyżce.
  - `available = max(V − (R − stary wkład), 0)` to maksymalny wkład tego wpisu.
  - Dodanie traktuje stary wkład jako 0.
  - Przypadek brzegowy dla testu: zmiana powodu korekty przy nadwyżce przechodzi.
- **N2, `EditOrder`.** Kolejność sprawdzeń:
  1. `400` dla pól niezależnych od źródła;
  2. `404`;
  3. `409 order_conflict` (wersja klienta);
  4. `400` dla pól zamówienia ręcznego oraz `422` dla pól sklepu i statusu;
  5. `Order.Edit` liczy zmiany. Gdy wśród zmian jest `value` i nowa wartość jest niższa,
     handler bierze `IOrderLock`, a sumy liczy po blokadzie. `V' < R` daje
     `422 sales_reduction_limit_exceeded` z `minimumValue = R`;
  6. brak zmian;
  7. zapis i audyt.

  Blokada nie zmienia `rowversion`, więc wersji nadal pilnuje `SaveAsync` z `OriginalValue`
  (`409`). Zakleszczenia nie ma: wszystkie ścieżki blokują najpierw wiersz zamówienia.
- **N3, poprawa zwrotu.** Kolejność jest taka sama jak przy dodaniu: najpierw reguła Zapłacono
  (`refund_exceeds_paid`), potem limit obniżeń.
- **N4.**
  - `LockedOrder` dostaje `SalesReturnsTotal` i `CorrectionsTotal`, a `EntryTotals` także
    `Value`.
  - `check` w `OrderEntryWriter` zwraca uogólnione naruszenie (`PaidRuleViolation` albo
    `SalesReductionLimitExceeded`).
  - `OrderProblems` mapuje nowe naruszenie na `422`.
- **N5, audyt.** Typ obiektu audytu wynika z rodzaju wpisu (`Payment`, `Refund`,
  `SalesCorrection`), a test to sprawdza. Etykieta korekty: „Korekta sprzedaży do {numer}”.
- **N6, formularz zwrotu.** Dochodzi opis: „Jeżeli towar wrócił, a pieniądze nie, dodaj
  korektę sprzedaży zamiast zwrotu.”
- **N7, test równoległy.** Dokładnie jedna zmiana przechodzi, a druga dostaje `422`.
- **Niejasności:**
  - alert o nadwyżce stoi w sekcji „Korekty sprzedaży”, nad tabelą;
  - `returnsTotal`, `correctionsTotal`, `total` i `excess` to stringi z 2 miejscami po
    przecinku (`excess` może być `null`);
  - nadwyżka może istnieć także w danych z czasów SF-9, sprzed wprowadzenia limitu.
    Ostrzeżenie pokazuje się wtedy tak samo, a reguła N1 pozwala z niej wyjść.

## 6. Edge cases & null safety

Każdy przypadek z Edge Cases w spec ma test integracyjny w `SalesCorrectionsApiTests` albo
rozszerzeniu testów zwrotów i `EditOrder`. Dochodzą przypadki z N1 i N7.

Null safety:
- `excess` jest nullable;
- sumy są zawsze nie-null (`COALESCE`);
- `minimumValue` i `available` są obecne tylko w `422`.

## 7. Invariants

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sprzedaż ≠ pieniądze | Korekta nie wchodzi do `EntryTotals.Paid` ani `PaymentSummary`; nie ma metody | `Correction_does_not_change_paid_or_state` (przykład z AC) |
| 2. Decimal | `decimal(18,2)`, reguły na `decimal` | `SalesReductionRulesTests`, test typu kolumny |
| 3. Dostęp | Sesja i XSRF | `Correction_routes_require_session`, `Correction_writes_require_xsrf` |
| 4. Ponowienia | Unikalny `CreationRequestId` | `Parallel_adds_with_same_key_create_one_correction` |
| 5. Audyt | `IAuditLog` w jednostce pracy, typ `SalesCorrection` | `Correction_lifecycle_writes_audit`, `Audit_failure_rolls_back_correction` |
| 6. Sekrety | Bez zmian | Przegląd kodu |
| 7. Granice | `orders.*`, `IAuditLog` | Testy architektury |
| 8. Brak danych identyfikujących | Fikcyjne dane | Przegląd kodu |

## 8. Tests

| Test | Type | What it proves |
|---|---|---|
| `SalesReductionRulesTests` | unit | Reguła N1 (dodanie, podwyżka, obniżka, zmiana bez zmiany wkładu przy nadwyżce), `available`, `Excess` |
| `SalesCorrectionInputTests`, `PaymentAuditTests` (rozszerzony) | unit | Walidacja; typ i etykieta audytu (N5) |
| `SalesCorrectionsApiTests.Correction_does_not_change_paid_or_state` | integration | AC1 i niezmiennik 1 |
| `…Limit_on_correction_add_and_edit`, `…Limit_on_sales_return`, `…Money_return_is_not_limited`, `…Refund_kind_change_respects_limit`, `…Edit_order_value_below_reductions_is_rejected` | integration | AC2, AC3, `available` i `minimumValue` |
| `…Excess_warning_from_seeded_state`, `…Decreases_allowed_in_excess` | integration | AC4 i N1 |
| `…Parallel_correction_and_sales_return_respect_limit`, `…Parallel_value_edit_and_correction_respect_limit` | integration | Blokada (N7); mutacja: bez `UPDLOCK` test nie przechodzi |
| `…Correction_lifecycle_writes_audit`, `…Audit_failure_rolls_back_correction`, `…Conflict_*`, `…Parallel_adds_with_same_key…`, `…routes_require_session`, `…writes_require_xsrf`, `Amount column decimal(18,2)` | integration | Niezmienniki 3–5 i współbieżność |
| `sales-corrections.spec.ts`, `sales-correction-form.spec.ts` | component | Sekcja, pusty stan, dialog usuwania, alert nadwyżki, komunikat limitu z kwotą, `busy` |
| `refund-form.spec.ts`, `order-form.spec.ts`, `order-details-page.spec.ts` (rozszerzone) | component | Opisy (N6), błąd limitu przy wartości, wykluczenie z edycją |

## 9. Verification

- **Backend:** `dotnet format stone-flow.slnx --verify-no-changes`,
  `dotnet build stone-flow.slnx -c Release`, `dotnet test --solution stone-flow.slnx -c Release`.
- **Frontend:** w `app/` `typecheck`, `lint`, `format:check`, `test -- --watch=false`, `build`.
- **Przed merge:** przegląd człowieka (ryzyko high) i zielone CI.
- **Ręcznie:** scenariusze z Edge Cases w spec.

## 10. Risks

- Zmiana w zatwierdzonej ścieżce `EditOrder` (SF-8): testy SF-8 muszą dalej przechodzić.
- Wszystkie zapisy obniżeń przechodzą przez `IOrderLock`. Nowa ścieżka, która pominie blokadę,
  może przekroczyć limit przy współbieżności. Pilnują tego testy równoległe.
