# Implementation Plan — SF-8

Tryb: **fullstack**. Zadanie dodaje storage, API i UI oraz nowy kontrakt między warstwami.
Bez podziału na części. Backend i frontend powstają równolegle według kontraktu z `02-spec.md`
(sekcja Data / API), a na końcu jest krok integracji.

## 1. Scope

Plan obejmuje:

- **backend:** moduł Orders (Domain, Application, Infrastructure, Api), migrację
  `OrdersAndNumbering` z tabelami `orders.Orders` i `orders.OrderNumberCounters`, sześć
  endpointów `/api/orders`, zapis audytu przez `IUnitOfWork` i `IAuditLog` z SF-7;
- **testy backendu:** nowy projekt `StoneFlow.Orders.Tests` oraz testy integracyjne;
- **frontend:** `features/orders/` z listą, formularzem dodawania, szczegółami, edycją,
  anulowaniem i przywracaniem, a także parser kwot w `shared/format/`.

Moduły Expenses, Reporting i Integrations ani kod SF-7 się nie zmieniają.

## 2. Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Domain | `Orders.Domain/Orders` | Agregat `Order`, `Customer`, `OperationalData`, słowniki, reguły statusu i własności pól, porównanie zmian |
| Application | `Orders.Application` (`CreateOrder/`, `EditOrder/`, `CancelOrder/`, `RestoreOrder/`, `GetOrder/`, `ListOrders/`, `Abstractions/`) | Przypadki użycia CQRS, walidacja, normalizacja i skrót, budowa wpisów audytu |
| Infrastructure | `Orders.Infrastructure/Persistence` | Konfiguracja EF, migracja, porty zapisu, odczytu i numeracji, tłumaczenie błędów SQL i współbieżności |
| Api | `Orders.Api/Endpoints` | Grupa `/api/orders`, mapowanie wyników na HTTP i Problem Details, kody słowników |
| Tests | `tests/StoneFlow.Orders.Tests` (nowy), `tests/StoneFlow.Api.IntegrationTests/Orders` (nowy folder) | Reguły domeny, handlery, API end-to-end na SQL Server |
| Frontend | `features/orders`, `shared/format`, `app.routes.ts` | Ekrany, klient API, serwis stanu, parser kwot, trasy potomne |

## 3. Files

### Backend

| File path | Action | Purpose |
|---|---|---|
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/OrderVocabulary.cs` | create | Enumy `OrderSource` (Manual, WooCommerce), `OrderChannel` (Shop, Olx, Acquaintance, Phone, Other), `FulfillmentStatus` (InProgress, Completed, Problem, Cancelled), `CustomerKind` (Person, Company) |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/Customer.cs` | create | Value object klienta (rodzaj, nazwa, kontakt, adres, NIP), równość wartości |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/OperationalData.cs` | create | Value object: dostawcy, ZO, faktura, flagi, uwagi; porównanie list z kolejnością |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/Order.cs` | create | Agregat: `CreateManual(...)`, `Edit(OrderChanges)`, `Cancel()`, `Restore()`, `ShopOwnedFields`; `Edit` zwraca listę zmienionych pól (`OrderFieldChange`) albo naruszenie reguły |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/OrderRuleViolation.cs` | create | `FieldOwnedByShop(fields)`, `StatusChangeRequiresAction`, `AlreadyCancelled`, `NotCancelled` |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/Abstractions/Ports.cs` | create | `IOrderStore` (Add/Load/Save z wersją i wynikami `Saved`, `Conflict`, `DuplicateRequest`), `IOrderNumberAllocator`, `IOrderReader` (list, details, `FindByCreationRequest`) |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/Abstractions/FieldErrors.cs` | create | Błędy pól pod ścieżkami JSON (`customer.name`, `operational.suppliers[0]`), na wzór `Access.Application` (bez referencji do Access) |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/OrderInput.cs` | create | Wspólny model wejścia komend, walidacja niezależna od źródła i zależna od źródła, normalizacja, `ComputeRequestHash()` |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/OrderAudit.cs` | create | Budowa `AuditEntry` dla Created/Updated (format wartości, `IsPersonalData`, etykieta = numer) |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/CreateOrder/CreateOrder.cs` | create | `CreateOrderCommand`, `CreateOrderResult` (Created, Repeated, KeyReused, ValidationFailed), handler |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/EditOrder/EditOrder.cs` | create | `EditOrderCommand`, wynik, handler z kolejnością sprawdzeń z sekcji 6 |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/CancelOrder/CancelOrder.cs` | create | Komenda, wynik, handler |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/RestoreOrder/RestoreOrder.cs` | create | Komenda, wynik, handler |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/GetOrder/GetOrder.cs` | create | Zapytanie i DTO `OrderDetailsDto` |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/ListOrders/ListOrders.cs` | create | Zapytanie z filtrami, walidacja parametrów, `OrderListPageDto` |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/StoneFlow.Orders.Application.csproj` | modify | Referencje do `Orders.Domain` i `Shared.Application`, jeśli ich brak |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrdersDbContext.cs` | modify | `DbSet<Order>`, `ApplyConfigurationsFromAssembly` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderConfiguration.cs` | create | Mapowanie kolumn ze spec, `decimal(18,2)`, enumy jako string, JSON list z konwerterem i comparerem, shadow `Version` (`IsRowVersion`), indeksy i check constraints |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderNumberCounterConfiguration.cs` | create | Tabela liczników (encja techniczna w Infrastructure) |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderStore.cs` | create | `IOrderStore`: `OriginalValue` wersji, wyłapanie `DbUpdateConcurrencyException`, `2601/2627`, `DuplicateRequest` z oryginalnym wyjątkiem |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderNumberAllocator.cs` | create | `UPDATE … WITH (UPDLOCK, HOLDLOCK) … OUTPUT`, `INSERT` dla nowego roku, jedno ponowienie po `2627` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderReader.cs` | create | Projekcje `Select` do DTO, filtry, sortowanie, `LIKE` z escapowaniem, `COUNT`, `AsNoTracking` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/Migrations/*_OrdersAndNumbering.cs` (+ Designer, snapshot) | create/modify | `dotnet ef migrations add OrdersAndNumbering` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/DependencyInjection.cs` | modify | Rejestracja portów (scoped) |
| `api/src/Modules/Orders/StoneFlow.Orders.Api/Endpoints/OrdersEndpoints.cs` | create | Sześć tras, odczyt konta z sesji jak w `AuthEndpoints.CurrentAccount`, nagłówek `Idempotency-Key`, mapowanie wyników |
| `api/src/Modules/Orders/StoneFlow.Orders.Api/Endpoints/OrderContracts.cs` | create | Rekordy żądań i odpowiedzi API, kody słowników (`inProgress`, `olx`, `person`…), parsowanie kwoty z tekstu (`InvariantCulture`) |
| `api/src/Modules/Orders/StoneFlow.Orders.Api/OrderProblems.cs` | create | Kody `order_conflict`, `idempotency_key_required`, `idempotency_key_reused` (+ `orderId`, `orderNumber`), `field_owned_by_shop` (+ `fields`), `status_change_requires_action`, `order_already_cancelled`, `order_not_cancelled`, `order_not_found`; polskie `detail` |
| `api/src/Modules/Orders/StoneFlow.Orders.Api/OrdersModule.cs` | modify | `MapOrdersEndpoints` wywołuje `OrdersEndpoints.Map` |
| `api/tests/StoneFlow.Orders.Tests/StoneFlow.Orders.Tests.csproj` (+ `packages.lock.json`) | create | Projekt testów domeny i aplikacji |
| `api/tests/StoneFlow.Orders.Tests/Domain/OrderTests.cs` | create | Reguły agregatu |
| `api/tests/StoneFlow.Orders.Tests/Application/OrderInputTests.cs` | create | Walidacja, normalizacja, skrót |
| `api/tests/StoneFlow.Orders.Tests/Application/OrderAuditTests.cs` | create | Format wpisów audytu |
| `api/tests/StoneFlow.Api.IntegrationTests/Orders/OrdersApiTests.cs` | create | API end-to-end na SQL Server |
| `stone-flow.slnx` | modify | Dodanie `StoneFlow.Orders.Tests` |

### Frontend

| File path | Action | Purpose |
|---|---|---|
| `app/src/app/features/orders/order-models.ts` | create | Typy DTO z kontraktu, kody i polskie etykiety słowników |
| `app/src/app/features/orders/orders-api.ts` | create | Klient `/api/orders` (Promise, relatywne URL-e, nagłówek `Idempotency-Key`) |
| `app/src/app/features/orders/orders.service.ts` | create | Stan listy (sygnały: strona, sort, filtry, wynik), przepływy zapisu, konfliktu i akcji |
| `app/src/app/features/orders/orders.routes.ts` | create | `''` lista, `nowe` dodawanie, `:id` szczegóły |
| `app/src/app/features/orders/orders-list-page.ts` / `.html` | create | Lista z `app-data-table`, filtry `tableFilters`, pusty stan bez filtrów z akcją |
| `app/src/app/features/orders/order-form.ts` / `.html` | create | Wspólny formularz (Signal Forms, `app-field`), podsumowanie błędów, pola tylko do odczytu |
| `app/src/app/features/orders/order-create-page.ts` / `.html` | create | Klucz idempotencji, zapis, `idempotency_key_reused` |
| `app/src/app/features/orders/order-details-page.ts` / `.html` | create | Szczegóły, tryb edycji, konflikt, dialogi anulowania i przywracania |
| `app/src/app/features/orders/*.spec.ts` | create | Testy klienta, serwisu, formularza, listy i szczegółów |
| `app/src/app/features/orders/orders-page.ts` / `.html` | delete | Zaślepka z SF-5, zastąpiona listą |
| `app/src/app/shared/format/pl-format.ts` (+ spec) | modify | `parsePlnAmount(text): string \| null`: polski zapis na `"1234.50"`, tylko na tekście |
| `app/src/app/app.routes.ts` | modify | Sekcja `zamowienia` z `loadChildren` do `orders.routes.ts`; helper `section` przyjmuje `loadChildren` |

## 4. Implementation steps

**Backend (B)**

1. **B1 — Domena:** utworzyć `OrderVocabulary`, `Customer`, `OperationalData`, `Order`
   i `OrderRuleViolation`.
   - `Order.Edit` przyjmuje pełny zestaw pól ERP i opcjonalne pola sklepu i wylicza zmiany.
   - Reguły statusu: w zamówieniu nieanulowanym `Cancelled` jest zakazany; w anulowanym
     wymagany jest `Cancelled`.
   - Własność pól: dla `WooCommerce` pola sklepu są porównywane z zapisanymi, a różnica daje
     `FieldOwnedByShop`.
   - `Cancel` i `Restore` odrzucają akcję niezgodną ze stanem.
   - Testy `OrderTests` powstają w tym samym kroku.
2. **B2 — Kontrakty aplikacji:**
   - `Ports.cs` i `FieldErrors`;
   - `OrderInput` z walidacją dwuetapową (sekcja 6, N1), normalizacją (spec Behavior pkt 4)
     i skrótem;
   - domyślny `fulfillmentStatus = InProgress` jest wstawiany **przed** liczeniem skrótu (N3);
   - `OrderAudit` w formacie ze spec;
   - testy `OrderInputTests` i `OrderAuditTests`.
3. **B3 — Handlery:** `CreateOrder`, `EditOrder`, `CancelOrder`, `RestoreOrder`, `GetOrder`,
   `ListOrders`.
   - Zapisy idą przez `IUnitOfWork.ExecuteInTransactionAsync` z `commitWhen` przyjmującym
     tylko wynik sukcesu.
   - Audyt idzie przez `IAuditLog.RecordAsync` z `AuditAuthor.Account(accountId)`.
4. **B4 — Infrastruktura:**
   - konfiguracje EF i `OrdersDbContext`;
   - `OrderNumberAllocator` z kodem obronnym na `2627` (N4);
   - `OrderStore`: przy `DuplicateRequest` zwraca wynik razem z oryginalnym wyjątkiem;
   - `OrderReader` z escapowaniem `%`, `_`, `[` w `LIKE`;
   - rejestracja w DI;
   - migracja `dotnet ef migrations add OrdersAndNumbering --project
     src/Modules/Orders/StoneFlow.Orders.Infrastructure --startup-project src/StoneFlow.Api
     --context OrdersDbContext`.
5. **B5 — Api:**
   - `OrderContracts`, `OrderProblems`, `OrdersEndpoints`;
   - podpięcie w `OrdersModule.MapOrdersEndpoints`;
   - bez `MapDelete`.
6. **B6 — Testy integracyjne** `OrdersApiTests` (sekcja 8), uruchomione na LocalDB.
7. **B7 — Kontrole:** `stone-flow.slnx`, `dotnet format`, build Release bez ostrzeżeń i pełny
   `dotnet test`.

**Frontend (F)**

1. **F1 — Parser kwot:** `parsePlnAmount` w `shared/format` i testy.
   - Akceptuje `1234,5`, `1 234,50`, `1234.50` i `0`.
   - Odrzuca liczby ujemne, więcej niż 2 miejsca po przecinku, tekst i pusty ciąg.
2. **F2 — Model i klient API:** `order-models.ts` (typy zgodne z kontraktem spec, etykiety)
   i `orders-api.ts`; błędy przez `toApiError()`.
3. **F3 — Serwis:** `orders.service.ts`. Zmiana filtra resetuje stronę do 1. Zapis,
   konflikt i akcje są obsługiwane w serwisie, nie w komponentach.
4. **F4 — Trasy:** `orders.routes.ts` i zmiana `app.routes.ts`; usunięcie zaślepki
   `orders-page`.
5. **F5 — Lista:** `orders-list-page`.
   - Kolumny i filtry ze spec, sortowanie przez `sortChange`.
   - Pusty stan bez filtrów: `app-empty-state` z przyciskiem „Dodaj zamówienie” zamiast
     tabeli.
   - Pusty stan z filtrami i „Wyczyść filtry” zostają w `app-data-table`, bez dodatkowego
     przycisku na stronie.
6. **F6 — Formularz:** `order-form` (sekcje Klient, Zamówienie, Realizacja, Uwagi).
   - Pole statusu bez „Anulowane”; w zamówieniu anulowanym status tylko do odczytu.
   - Pola sklepu tylko do odczytu według `shopOwnedFields`.
   - Błędy z API przypinane po kluczach JSON; podsumowanie błędów z fokusem.
7. **F7 — Dodawanie:** `order-create-page`.
   - UUID z `crypto.randomUUID()` generowany przy otwarciu.
   - `busy` przy zapisie, przejście do szczegółów po `201` i `200`.
   - Przy `409 idempotency_key_reused` komunikat z linkiem i akcja „Dodaj kolejne
     zamówienie”, która generuje nowy klucz.
8. **F8 — Szczegóły:** `order-details-page`.
   - Widok, „Edytuj”, zapis z `version`.
   - Konflikt `409 order_conflict` z akcją „Wczytaj aktualne dane”.
   - „Anuluj zamówienie” (`danger`) i „Przywróć zamówienie” (`secondary`) z
     `app-confirm-dialog`.
   - Komunikaty w regionie `aria-live`.
9. **F9 — Kontrole:** `npm run typecheck`, `npm run lint`, `npm run format:check`,
   `npm test`, `npm run build`.

**Integracja (I)**

1. **I1:** API na LocalDB i `npm start`. Pełny scenariusz ręczny z sekcji 9. Porównać kody
   błędów i nazwy pól między `OrderProblems` i `ValidationProblemDetails` a `order-models.ts`.

## 5. Data & contracts

- **Storage:** zgodnie z `02-spec.md`, sekcja Storage, w migracji `OrdersAndNumbering`.
  `Version` to shadow property `rowversion` skonfigurowane w Infrastructure, więc domena nie zna
  EF. Port zwraca wersję razem z wynikiem jako `byte[]`, a API koduje ją base64.
- **API:** sześć endpointów z tabeli w spec. Kody słowników:
  - `source`: `manual`, `wooCommerce`;
  - `channel`: `shop`, `olx`, `acquaintance`, `phone`, `other`;
  - `fulfillmentStatus`: `inProgress`, `completed`, `problem`, `cancelled`;
  - `customer.kind`: `person`, `company`.
- **Numer:** `ZR-{rok}-{n:D4}`. Powyżej 9999 zamówień w roku numer rośnie do 5 cyfr
  (`ZR-2026-10000`), mieści się w `nvarchar(32)` i nadal jest unikalny (niejasność z
  przeglądu).
- **Typy na granicy:** `OrderInput`, `OrderListItem`, `OrderDetails` i `OrderListPage` jak
  w spec. Kwoty i daty są tekstem. `version` to string base64.

## 6. Edge cases & null safety

**Kolejność sprawdzeń w `EditOrder` (N1):**

1. walidacja niezależna od źródła: `version` obecna i base64, `fulfillmentStatus` z poprawnym
   kodem, dane operacyjne, komentarz; błąd daje `400`;
2. wczytanie zamówienia, brak daje `404`;
3. porównanie `version`, różnica daje `409 order_conflict`;
4. walidacja zależna od źródła:
   - w zamówieniu ręcznym `orderDate`, `channel`, `value` i `customer` są wymagane
     i walidowane (`400`);
   - w zamówieniu ze sklepu te pola nie są walidowane, tylko porównywane, a różnica daje
     `422 field_owned_by_shop`;
5. reguły statusu (`422 status_change_requires_action`);
6. brak zmian: sukces bez zapisu;
7. zapis i audyt.

Dzięki temu `409` ma pierwszeństwo przed `422`.

**Duplikat numeru (N2):** `OrderStore` łapie `2601/2627` tylko po to, żeby zwrócić
`DuplicateRequest` z oryginalnym wyjątkiem. Po wycofaniu handler czyta zamówienie po kluczu.

- Gdy je znajdzie, wynik to `Repeated` albo `KeyReused`.
- Gdy go nie znajdzie (duplikat `Number`), ponownie rzuca **oryginalny** wyjątek
  (`ExceptionDispatchInfo.Throw`). `SecuritySetup` mapuje `DbException` na `503`, zgodnie ze
  Storage i rozbieżnością udokumentowaną w konwencjach.

**Mapa przypadków brzegowych:**

| Edge case (spec) | Gdzie obsłużony |
|---|---|
| Podwójne kliknięcie | F7 (`busy`), B3/B4 (klucz, unikalny indeks), test integracyjny równoległy |
| Utrata odpowiedzi, ponowienie z kluczem | `CreateOrderHandler`: krok szukania po kluczu daje `200` |
| Ten sam klucz z innymi danymi | Porównanie skrótu daje `KeyReused`, a w UI F7 |
| Normalizacja spacji i NIP | `OrderInput.Normalize` + `OrderInputTests` |
| Wartość 0, ujemna, 3 miejsca po przecinku, tekst | `OrderInput`, `parsePlnAmount`, check constraint |
| Równoległa numeracja i nowy rok | `OrderNumberAllocator` (blokada zakresu, obronne ponowienie po `2627`). Test: równoległe tworzenie daje różne numery `0001`/`0002` (N4) |
| Wycofany zapis nie zużywa numeru | Licznik w transakcji jednostki pracy. Test z wymuszonym błędem audytu |
| Edycja anulowanego z innej karty, podwójne anulowanie | Kontrola `version` w `OrderStore` |
| `LIKE` ze znakami specjalnymi | `OrderReader` escapuje znaki i używa `ESCAPE` |
| `dateFrom > dateTo`, `pageSize > 100`, strona poza zakresem | `ListOrders` walidacja (`400`); reader zwraca pustą stronę z `total` |
| Zmiana daty zamówienia ręcznego | `Order.Edit`; data biznesowa audytu to nowa data |
| Pola sklepu i kanał w `PUT` | `Order.Edit` (`FieldOwnedByShop` z listą pól) |
| `Idempotency-Key` brak lub nie-UUID | `OrdersEndpoints` daje `400 idempotency_key_required` |

**Null safety:**

- pola opcjonalne `OrderInput` są nullable i normalizowane do `null`;
- `Customer` i `OperationalData` zawsze istnieją; listy zawsze są nie-null (pusta lista);
- `ExternalId` i `ShopStatus` są nullable;
- konto z sesji brane jest jako `Guid?`; jego brak daje `401`, jak w `AuthEndpoints`.

## 7. Invariants

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sprzedaż ≠ pieniądze | Model bez pól płatności; `CreateOrder` niczego poza zamówieniem i audytem nie zapisuje | `Create_does_not_create_money_records_or_payment_state` (integracyjny: odpowiedź bez pól płatności, brak innych tabel z wierszami poza `orders.*` i `reporting.AuditEntries`) |
| 2. Decimal | `decimal` w domenie, `decimal(18,2)` w konfiguracji EF, kwota jako tekst w API, parser tekstowy na froncie | `OrderInputTests` (parsowanie i zakres), test migracji (typ kolumny), `pl-format.spec` |
| 3. Dostęp przez API | Endpointy bez `AllowAnonymous`; globalna ochrona XSRF | `Anonymous_requests_are_rejected` (5 tras → `401`), `Write_without_xsrf_is_rejected` |
| 4. Bez duplikatów | Unikalne `CreationRequestId`, `(Source, ExternalId)` filtrowany, `Number` | `Parallel_creates_with_same_key_create_one_order`, test indeksów w migracji |
| 5. Audyt | `IAuditLog` w `IUnitOfWork` w każdym zapisie | `Create_edit_cancel_restore_write_audit_entries`, `Audit_failure_rolls_back_order_and_number`, `Edit_without_changes_writes_no_audit` |
| 6. Sekrety | Logi zawierają tylko id i numer | Przegląd kodu (brak logowania `OrderInput`) |
| 7. Granice | Orders nie referencjonuje Reporting; audyt przez `Shared.Application` | Istniejące testy architektury (`ModuleBoundaryTests`, `LayerDependencyTests`, `DbContextSchemaTests`) |
| 8. Brak danych identyfikujących | Fikcyjni klienci w testach i przykładach | Przegląd kodu |

## 8. Tests

| Test | Type | What it proves |
|---|---|---|
| `OrderTests` | unit (domain) | Tworzenie ręcznego (status domyślny, źródło Manual); edycja wylicza tylko zmienione pola, także listy z kolejnością; brak zmian daje pustą listę; `Cancelled` w edycji nieanulowanego jest odrzucany; anulowane wymaga `Cancelled`; pola sklepu i kanał różne od zapisanych dają `FieldOwnedByShop`, równe przechodzą; `Cancel`/`Restore` w złym stanie są odrzucane; `Restore` ustawia `InProgress` |
| `OrderInputTests` | unit | Wymagane pola; wartość ≥ 0, 2 miejsca po przecinku, zakres `decimal(18,2)`; NIP z sumą kontrolną i normalizacją; kod pocztowy; limity list i długości; klucze błędów JSON; skrót stały przy różnicy spacji i NIP oraz przy domyślnym i jawnym `inProgress` (N3), różny przy innej wartości |
| `OrderAuditTests` | unit | Created bez pól `null` i pustych list, z `false`; Updated tylko ze zmianami; formaty kwoty, daty, listy i kodów; `IsPersonalData` dla `customer*` poza `customerKind`; etykieta = numer; data biznesowa = data zamówienia |
| `OrdersApiTests.Create_*` | integration | `201`, numer `ZR-{rok}-0001`, pojawia się na liście; brak klucza daje `400`; powtórka daje `200` z tym samym id; inne dane dają `409` z `orderId`/`orderNumber`; walidacja `400` z kluczami; `cancelled` w POST daje `400`; 0 zł przyjęte |
| `OrdersApiTests.Parallel_creates_with_same_key_create_one_order` | integration | Niezmiennik 4 przy wyścigu |
| `OrdersApiTests.Parallel_creates_get_distinct_numbers` | integration | Numeracja bez duplikatów (N4) |
| `OrdersApiTests.Audit_failure_rolls_back_order_and_number` | integration | Ograniczenie odrzucające wpis audytu (wzorzec z SF-7): brak zamówienia i licznik nie jest zwiększony, a następne zamówienie dostaje ten sam numer |
| `OrdersApiTests.Edit_*` | integration | Zmiana wartości i daty z audytem `Updated`; brak zmian bez audytu i bez zmiany wersji; stara `version` daje `409` bez zmian (także przy rozłącznych polach); anulowane z `cancelled` zapisuje, z innym daje `422`; zamówienie WooCommerce zasiane w bazie: zmiana wartości, daty, klienta i kanału daje `422 field_owned_by_shop` (N5), pola równe przechodzą; `404` |
| `OrdersApiTests.Cancel_restore_*` | integration | Zmiana statusu i audyt; stan niezgodny daje `409`; stara `version` daje `409 order_conflict` |
| `OrdersApiTests.Delete_is_not_possible` | integration | `DELETE` z sesją i XSRF daje `405`, a rekord istnieje |
| `OrdersApiTests.List_*` | integration | Filtry (okres włącznie, kanał, źródło, wiele statusów, wyszukiwanie z `%`/`_`), sortowanie ze stabilnym id, paginacja i `total`; `pageSize > 100` oraz `dateFrom > dateTo` dają `400` |
| `OrdersApiTests.Anonymous_requests_are_rejected`, `Write_without_xsrf_is_rejected` | integration | Niezmiennik 3 |
| `OrdersApiTests.Create_does_not_create_money_records_or_payment_state` | integration | Niezmiennik 1 |
| `pl-format.spec.ts` (`parsePlnAmount`) | unit (frontend) | Polskie zapisy kwot i odrzucenia |
| `orders-api.spec.ts` | unit (frontend) | URL-e, query z powtarzanym `status`, nagłówek `Idempotency-Key`, mapowanie błędów |
| `orders.service.spec.ts` | unit (frontend) | Zmiana filtra resetuje stronę; konflikt i `KeyReused` dają właściwe stany |
| `order-form.spec.ts` | component | Brak „Anulowane” w statusie; status tylko do odczytu dla anulowanego; pola sklepu tylko do odczytu; błędy API przy polach i w podsumowaniu; fokus na podsumowaniu |
| `order-create-page.spec.ts` | component | Jedno żądanie przy podwójnym kliknięciu; ten sam klucz przy ponowieniu; nowy klucz po „Dodaj kolejne zamówienie” |
| `orders-list-page.spec.ts` | component | Stany ładowania, pusty bez filtrów z akcją, błąd; sort i strona wywołują API |
| `order-details-page.spec.ts` | component | Konflikt i „Wczytaj aktualne dane”; dialog przed anulowaniem i przywróceniem; brak akcji „Usuń” |

## 9. Verification

- **build:** `dotnet build stone-flow.slnx -c Release` (0 ostrzeżeń); `npm run build` w `app/`.
- **format:** `dotnet format stone-flow.slnx --verify-no-changes`; `npm run format:check`.
- **tests:** `dotnet test --solution stone-flow.slnx -c Release`; `npm run typecheck`,
  `npm run lint`, `npm test` w `app/`.
- **restore z lockfile:** `dotnet restore stone-flow.slnx --locked-mode`, po dodaniu
  `packages.lock.json` nowego projektu.
- **manual (I1):** API na LocalDB i `npm start`, a potem:
  - dodać zamówienie podwójnym kliknięciem: na liście jest jeden rekord;
  - otworzyć zamówienie w dwóch kartach, zapisać w obu: druga dostaje konflikt;
  - anulować, edytować anulowane, przywrócić: w `reporting.AuditEntries` są wpisy;
  - sprawdzić filtry i sortowanie, widok 375 px i obsługę klawiaturą.
- **Przegląd człowieka przed merge (risk high):** zwrócić uwagę na idempotencję, kolejność
  sprawdzeń w edycji, numerację w transakcji i dane osobowe w audycie.
- **CI:** zielone na PR (backend z kontenerem SQL i uwierzytelnianiem SQL).

## 10. Risks

- **`UPDATE … OUTPUT` przez `Database.SqlQuery` na wspólnym połączeniu SF-7.** Interceptor
  ustawia transakcję w `CommandInitialized`, więc polecenie powinno dołączyć. Weryfikuje to
  test wycofania numeru.
- **Naruszenie unikalności w otwartej transakcji.** Przy `XACT_ABORT OFF` SQL Server zostawia
  transakcję aktywną, ale handler i tak ją wycofuje (`commitWhen = false`) i czyta po kluczu
  dopiero po wycofaniu. Nie należy czytać w tej samej transakcji.
- **Po wycofaniu kontekst Orders czyści tracker**, bo dołączył przez `SaveChanges` (SF-7,
  otwarta uwaga C2). Odczyt po kluczu idzie przez `OrderReader` z `AsNoTracking`.
- **Rozbieżność `DbException → 503`** zostaje. Duplikat `Number` (błąd programu) kończy się
  `503`, a nie `500`; jest to udokumentowane.
- **Pokrycie ścieżki `2627` licznika:** jest tylko kodem obronnym, bez deterministycznego
  testu (N4).
