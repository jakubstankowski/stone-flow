# Implementation done — SF-8

## Summary

Dostarczono:

- **model zamówienia** w module Orders (zamówienie, klient, dane operacyjne, słowniki stałe
  w kodzie, pola pod integrację);
- **migrację** `OrdersAndNumbering` (`orders.Orders`, `orders.OrderNumberCounters`);
- **sześć endpointów** `/api/orders`: lista, szczegóły, dodanie z kluczem idempotencji,
  edycja, anulowanie i przywrócenie;
- **audyt** każdego zapisu przez `IAuditLog` w `IUnitOfWork` (SF-7);
- **frontend** `features/orders` (lista, „Dodaj zamówienie”, szczegóły z edycją, anulowanie
  i przywrócenie z potwierdzeniem) oraz `parsePlnAmount` w `shared/format`.

Endpointu ani akcji usuwania nie ma.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Model zamówienia i klienta, dane operacyjne, słowniki | Implemented | Słowniki stałe w kodzie |
| Dodanie zamówienia ręcznego z ochroną przed ponownym wysłaniem | Implemented | `Idempotency-Key`, skrót SHA-256, unikalny `CreationRequestId`, `busy` w UI |
| Numer `ZR-{rok}-{NNNN}` | Implemented | Rok według Europe/Warsaw, licznik w transakcji |
| Wspólna lista z filtrowaniem, sortowaniem i paginacją w API | Implemented | |
| Szczegóły i edycja pól ERP | Implemented | |
| Kontrola współbieżności | Implemented | `rowversion`, `409 order_conflict`, „Wczytaj aktualne dane” |
| Korekta wartości i zmiana daty zamówienia ręcznego z audytem | Implemented | |
| Pola sklepu tylko do odczytu | Implemented | `422 field_owned_by_shop` |
| Anulowanie i przywrócenie z potwierdzeniem i audytem | Implemented | |
| Brak usuwania | Implemented | `DELETE` → `405` |
| Audyt utworzenia i zmian | Implemented | Dane klienta oznaczone jako osobowe |
| Stany UI, formatowanie, dostępność | Implemented | Fokus w dialogu i widok 375 px nie były sprawdzone ręcznie |

## Files

### Created

- **Backend, Domain:** `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/`
  (`OrderVocabulary.cs`, `Customer.cs`, `OperationalData.cs`, `Order.cs`,
  `OrderRuleViolation.cs`).
- **Backend, Application:** `api/src/Modules/Orders/StoneFlow.Orders.Application/`:
  - `Abstractions/Ports.cs`, `Abstractions/FieldErrors.cs`;
  - `OrderCodes.cs`, `OrderInput.cs`, `OrderAudit.cs`, `OrderStatusAction.cs`;
  - foldery `CreateOrder/`, `EditOrder/`, `CancelOrder/`, `RestoreOrder/`, `GetOrder/`,
    `ListOrders/`.
- **Backend, Infrastructure:**
  `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/`:
  - `OrderConfiguration.cs`, `OrderNumberCounterConfiguration.cs`;
  - `OrderStore.cs`, `OrderNumberAllocator.cs`, `OrderReader.cs`;
  - `Migrations/20261105085705_OrdersAndNumbering.cs` (+ Designer).
- **Backend, Api:** `api/src/Modules/Orders/StoneFlow.Orders.Api/` (`OrderProblems.cs`,
  `Endpoints/OrdersEndpoints.cs`, `Endpoints/OrderContracts.cs`).
- **Testy backendu:**
  - `api/tests/StoneFlow.Orders.Tests/` (csproj, `packages.lock.json`, `TestOrders.cs`,
    `Domain/OrderTests.cs`, `Application/OrderInputTests.cs`,
    `Application/OrderAuditTests.cs`);
  - `api/tests/StoneFlow.Api.IntegrationTests/Orders/OrdersApiTests.cs`.
- **Frontend:** `app/src/app/features/orders/`:
  - `order-models.ts`, `order-form-model.ts`, `orders-api.ts`, `orders.service.ts`,
    `orders.routes.ts`;
  - `orders-list-page.*`, `order-form.*`, `order-create-page.*`, `order-details-page.*`;
  - specyfikacje testów;
  - poza folderem: `app/src/testing/orders-testing.ts`.

### Modified

- **Backend:** `OrdersModule.cs`, Orders `DependencyInjection.cs`, `OrdersDbContext.cs`,
  `OrdersDbContextModelSnapshot.cs`, `stone-flow.slnx`.
- **Frontend:** `app/src/app/app.routes.ts` (+ spec), `app/src/app/shared/format/pl-format.ts`
  (+ spec), `app/src/testing/auth-testing.ts`.
- **Usunięte:** `app/src/app/features/orders/orders-page.ts` / `.html` (zaślepka SF-5).

## Planned Units

| Item from the plan | Status |
|---|---|
| Agregat `Order`, `Customer`, `OperationalData`, słowniki, `OrderRuleViolation` | Exists |
| Porty `IOrderStore`, `IOrderNumberAllocator`, `IOrderReader` | Exists |
| `OrderInput` (walidacja dwuetapowa, normalizacja, skrót), `OrderAudit` | Exists |
| Handlery Create/Edit/Cancel/Restore/Get/List | Exists |
| Konfiguracje EF, `OrderStore`, `OrderNumberAllocator`, `OrderReader` | Exists |
| Migracja `OrdersAndNumbering` | Exists |
| `OrdersEndpoints`, `OrderContracts`, `OrderProblems` | Exists |
| `StoneFlow.Orders.Tests` w `stone-flow.slnx` | Exists |
| `orders-api.ts`, `orders.service.ts`, `orders.routes.ts`, `order-models.ts` | Exists |
| Lista, formularz, dodawanie, szczegóły | Exists |
| `parsePlnAmount` | Exists |
| Trasa `zamowienia` z `loadChildren` | Exists |

## Database

Orders: migracja `20261105085705_OrdersAndNumbering`. Tworzy:

- `orders.Orders`:
  - unikalne `Number` i `CreationRequestId` (filtrowany);
  - filtrowany unikalny `(Source, ExternalId)`;
  - check constraints `Value >= 0` oraz `Source`/`ExternalId`;
  - `rowversion`;
  - indeks `(OrderDate DESC, Id DESC)`;
- `orders.OrderNumberCounters`.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| `OrderTests`, `OrderInputTests`, `OrderAuditTests` | Exists | `StoneFlow.Orders.Tests`: 63/63 |
| `OrdersApiTests` (tworzenie, idempotencja, numeracja, edycja, konflikt, audyt i jego wycofanie, anulowanie, przywrócenie, `DELETE`, lista, autoryzacja, XSRF, niezmiennik 1) | Exists | 39/39 w przebiegu pełnym |
| Testy po przeglądzie kodu: T1 konflikt między wczytaniem a zapisem, T2 duplikat klucza i numeru, T3 wycofanie audytu dla edycji, anulowania i przywrócenia, T4 XSRF dla `restore`, T5 rok według Europe/Warsaw | Exists | Pass |
| `pl-format.spec.ts`, `orders-api.spec.ts`, `orders.service.spec.ts`, `order-form*.spec.ts`, `order-create-page.spec.ts`, `orders-list-page.spec.ts`, `order-details-page.spec.ts` | Exists | `npm test`: 32 pliki, 301/301 |

## Verification

| Command | Outcome |
|---|---|
| `dotnet restore stone-flow.slnx --locked-mode` | Passed (uruchomione przez implementującego backend) |
| `dotnet format stone-flow.slnx --verify-no-changes` | Passed |
| `dotnet build stone-flow.slnx -c Release` | Passed, 0 ostrzeżeń |
| `dotnet test --solution stone-flow.slnx -c Release` (LocalDB) | Passed, 325/325 |
| `npm run typecheck` | Passed |
| `npm run lint` (z `lint:colors`) | Passed |
| `npm run format:check` | Passed |
| `npm test -- --watch=false` | Passed, 301/301 |
| `npm run build` | Passed |
| Scenariusz ręczny I1 (przeglądarka, API, LocalDB) | Not run |
| CI na PR (SQL Server z uwierzytelnianiem SQL) | Not run (w toku po otwarciu PR) |

## Deviations

- Kody słowników są w `Orders.Application/OrderCodes.cs`, a nie w `OrderContracts`.
- Wspólny przebieg anulowania i przywracania jest w `OrderStatusAction`.
- Komunikaty i klucze błędów są w `OrderInput.cs`, a nie w osobnym pliku.
- Domena ma `Order.CreateFromShop`, używane w testach domeny.
- Nieczytelne pole sklepu w `PUT` daje `422 field_owned_by_shop`.
- Lista z `sort` bez `direction` sortuje rosnąco.
- Frontend:
  - ma dodatkowy plik `order-form-model.ts`;
  - dostawcy i ZO to `textarea`, jedna pozycja w wierszu;
  - pola sklepu i status anulowanego zamówienia są tekstem tylko do odczytu i nie są wysyłane
    w `PUT` (poza statusem `cancelled`);
  - filtry listy nie korzystają z Signal Forms;
  - nowe zamówienie ma domyślnie dzisiejszą datę.
- Migracja `OrdersAndNumbering` została wygenerowana ponownie po przeglądzie (zmiana indeksu).
  Nie była wcześniej scalona ani wdrożona.

## Additional Implementation

Additional implementation not covered by the original documents:

- `HookedOrderStore`/`StoreHooks`: dekorator portu, tylko w testach integracyjnych;
- predykat w `nextRequest` (`app/src/testing/auth-testing.ts`).

## Open Items

- Human review required before merge (risk high).
- CI na PR musi przejść (SQL Server z uwierzytelnianiem SQL).
- Scenariusz ręczny I1 nie był wykonany: podwójne kliknięcie, dwie karty, anulowanie
  i przywrócenie, widok 375 px, klawiatura i fokus w dialogu.
- Ponowne rzucenie oryginalnego wyjątku przy duplikacie `Number` (`503`) ma test zachowania,
  ale nie zostało potwierdzone mutacją.
- Ponowienie licznika po `2627` jest kodem obronnym, bez testu deterministycznego.
- SF-9 doda wpłaty, stan płatności, filtr stanu płatności i ostrzeżenie o wpłatach przy
  anulowaniu. SF-10 doda limit obniżeń przy korekcie wartości.
