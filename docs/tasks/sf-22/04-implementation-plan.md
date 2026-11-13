# Implementation Plan — SF-22

Tryb: **fullstack**. Zadanie dodaje regułę domeny Orders (retencja, anonimizacja zamówienia, zwrotów i
korekt, blokada synchronizacji i edycji), nowy przypadek użycia z endpointem
`POST /api/orders/{id}/anonymize`, stan akcji w `GET /api/orders/{id}`, dwa nowe porty współdzielone
audytu (zmiana wartości wpisów i odczyt czasu ostatniego wpisu) z implementacją w Reporting, migrację
`orders.Orders`, UI w szczegółach zamówienia, formularzu edycji i „Historii zmian” oraz dokumentację
operacyjną. Plany backendu i frontendu napisał jeden autor po kolei (bez subagentów): najpierw
backend, potem frontend z kontraktem wyprowadzonym ze spec (sekcje „Data / API” i „UI”), nie z planu
backendu. Oba porównano w „Integration Check”.

Zadanie nie jest dzielone na części (podział zaproponowano w „Risks”; bez zgody właściciela powstaje
jeden plan). Migracja jest typu expand, endpoint nowy, pole `anonymization` w odpowiedzi addytywne,
więc stary frontend działa z nowym backendem. Funkcja jest wyłączona bez konfiguracji w każdym
środowisku. Ryzyko `high`: **przegląd człowieka przed merge** (sekcja „Verification”, krok R).

## Scope

- **Shared.Kernel:** `AuditOperation.Anonymized`.
- **Shared.Application/Audit:** porty `IAuditRedaction` (z `AuditRedaction`, `AuditFieldSelector`) i
  `IAuditActivity`; doprecyzowanie komentarza `IAuditLog`.
- **Reporting:** `AuditRecord.Redact` (Domain), `AuditRedactor`, `AuditActivity`, `AuditScope`
  (Infrastructure), kod `anonymized` w `ChangeHistoryCodes` (Application). Bez zmian schematu.
- **Orders.Domain:** `CustomerAnonymization`, `AnonymizationStatus`, `AnonymizationAvailability`;
  `Order` (fakt anonimizacji, `AnonymizeCustomer`, `ApplyFromShop`, `Edit`); `OrderEntry`, `Refund`,
  `SalesCorrection` (`AnonymizeReason`); `OrderRuleViolationKind.AlreadyAnonymized`,
  `CustomerAnonymized`.
- **Orders.Application:** porty `IAnonymizationSettings`, `IOrderAnonymizationStore`; przypadek
  `AnonymizeCustomer/`; `OrderAnonymizationAudit`; `GetOrder` ze stanem akcji; `EditOrder` z wynikiem
  `CustomerAnonymized`; kody w `OrderCodes`.
- **Orders.Infrastructure:** `Options/AnonymizationOptions.cs` (opcje, walidator, ustawienia),
  `Persistence/OrderAnonymizationStore.cs`, `OrderConfiguration`, `OrderReader`, migracja
  `CustomerAnonymization`, DI.
- **Orders.Api:** endpoint, kody problemów, `OrderAnonymizationResponse`, mapowanie edycji.
- **Frontend:** `features/orders/` (modele, klient, serwis, szczegóły, formularz), `features/change-history/`
  (operacja, katalog pól, widok wartości), `src/testing/orders-testing.ts`.
- **Dokumenty i konfiguracja:** `docs/operations/anonimizacja.md` (nowy), `kopie-i-odtworzenie.md`,
  `powrot-do-wersji.md`, `deploy/api.env.example` (tylko zakomentowany placeholder),
  `.sdd/context/conventions.md`.
- Bez zmian: Integrations (kod i tabele), Expenses, Access, Scheduling, `deploy/scripts/contexts.sh`
  (Orders już jest), `appsettings*.json`, `launchSettings.json`, `app/playwright.config.ts`, `app/e2e/`,
  workflowy CI, zależności npm. NuGet: tylko `PackageReference` do istniejącego w
  `Directory.Packages.props` pakietu `Microsoft.Extensions.TimeProvider.Testing` w
  `StoneFlow.Orders.Tests` (+ jego `packages.lock.json`).
- Pliki spoza zadania (`README.md`, `deploy/scripts/demo-data.sql`) nie są dotykane.

### Rozstrzygnięcia uwag z przeglądu (`03-spec-review.md`)

Każde rozstrzygnięcie to **decyzja wykonawcy z 13.11.2026 do potwierdzenia przez właściciela**
(wpisać do `06-implementation-done.md` jako otwarte potwierdzenie).

| # | Uwaga przeglądu | Decyzja | Gdzie w planie |
|---|---|---|---|
| 1 | Włączenie ustawia długość okresu, a nie regułę początku | `docs/operations/anonimizacja.md`, sekcja „Włączenie”, krok 1 (warunek konieczny, przed ustawieniem zmiennej): właściciel z księgowym lub prawnikiem porównuje rozstrzygnięcie D-09 z regułą początku okresu opisaną w dokumencie (najpóźniejsza z dat: zamówienia, wpisu, ostatniej zmiany zamówienia lub wpisów, w tym usunięcia wpisu). Gdy D-09 liczy okres inaczej (np. od końca roku obrotowego), **nie ustawia się wartości**: najpierw osobne zadanie zmienia `CustomerAnonymization.RetentionStart` (jedyne miejsce reguły; testy CA-*). Krok zapisuje w rejestrze właściciela datę i treść potwierdzenia. Reguła jest jedną metodą domeny wołaną przez zapytanie i komendę (`AnonymizationRetention`, B9) — żadna inna warstwa jej nie powtarza (frontend tylko wyświetla daty z API). | D1, B4, B9, CA-* |
| 2 | Pusta wartość, pliki konfiguracji, fabryka testów | **Brak klucza, pusty tekst albo same białe znaki = funkcja wyłączona.** Niepusta wartość nie będąca liczbą całkowitą 1–1200 (`0`, `-1`, `1201`, `abc`, `12.5`, `60 m`) = **błąd startu** (`ValidateOnStart`). Komunikat: tylko klucz `StoneFlow:Anonymization:RetentionMonths` i zakres, bez wartości. Żeby to osiągnąć, opcja jest wiązana jako **`string?`** (nie `int?`, jak w spec pkt 1): wiązanie `int?` z `"abc"` rzuca `InvalidOperationException` z bindera (nie `OptionsValidationException`, test `StartupFailure` by go nie rozpoznał), a `"  "` też by rzuciło, choć ma oznaczać „wyłączona”. Parsowanie w jednym miejscu (`AnonymizationOptions.TryParseRetentionMonths`), używane przez walidator i `ConfiguredAnonymizationSettings`; port `IAnonymizationSettings.RetentionMonths` zostaje `int?`. Wartości nie ma w `appsettings*.json`, `Properties/launchSettings.json`, `app/playwright.config.ts`, `app/e2e/`, `.github/workflows/*`; w `deploy/api.env.example` tylko linia zakomentowana z placeholderem `<…>`. Pilnuje tego test I-ST-6 (skan plików). `StoneFlowApiFactory` **nie** ustawia klucza (domyślnie wyłączona); każdy test, który potrzebuje funkcji, podaje `RetentionMonths` jawnie przez `settings` w `OrdersTest.CreateAsync`, a testy stanu „wyłączona” nie podają go albo podają `""`/`"   "`. | B10, B11, I-ST-*, D3 |
| 3 | Usunięcie wpisu nie przesuwa początku okresu | **Ostatnia zmiana obejmuje też czas ostatniego wpisu audytu zamówienia i jego wpisów** (wszystkie operacje, w tym `Deleted`, od każdego autora). Źródło: nowy port odczytu `IAuditActivity.LastRecordedAtUtcAsync(Order/{id})` w `Shared.Application/Audit`, implementacja w `Reporting.Infrastructure` (właściciel `reporting.AuditEntries`) na wspólnym `ModuleConnection`. Uzasadnienie: (a) audyt ma czas każdego usunięcia, bo `PaymentAudit.Deleted` jest zapisywany w tej samej transakcji co usunięcie (niezmiennik 5) — bez nowej kolumny, migracji ani zmiany ścieżek usuwania; (b) dotykanie `Order.UpdatedAtUtc` przy usunięciu wpisu zmieniałoby `rowversion` zamówienia i dawało `409` otwartej edycji, wbrew kontraktowi `IOrderLock` („Blokada nie zmienia wersji zamówienia”); (c) tabela znaczników usunięć w `orders` to nowy schemat dla jednej daty; (d) ten sam predykat zakresu (obiekt `Order/{id}` albo rodzic `Order/{id}`) co anonimizacja audytu — jedna definicja (`AuditScope`). Reguła liczy maksimum z `Order.UpdatedAtUtc`, `UpdatedAtUtc` wpisów i czasu z audytu, więc brak audytu (np. dane testowe sprzed SF-7) nie skraca okresu poniżej dzisiejszej reguły. Granica modułów: Orders czyta tylko jedną datę przez port, nie tabelę Reporting (niezmiennik 7). Zmienia to zdanie spec (Assumptions pkt 2: „nie korzysta z audytu”) i usuwa wyłączenie z Behavior pkt 6 („Wpisy usunięte nie wchodzą do wyliczenia”). | S2, S3, B9, CA-*, H-AN-6, H-GO-4, I-AN-5 |
| 4 | Zmiana powodu bez wpisu na obiekcie zwrotu/korekty | **Każdy zwrot i korekta z powodem zmienionym przez anonimizację dostaje własny wpis audytu `Anonymized`** (obiekt `Refund`/`SalesCorrection`, rodzic `Order/{id}`, etykieta `PaymentAudit.Label`, data biznesowa = data wpisu, autor = konto) z jednym polem `anonymizedFields` = `["reason"]`, `old` = `null`, `personal` = `false`. Wpis zamówienia zachowuje `anonymizedReasons` (liczba). Filtr „Zwrot”/„Korekta sprzedaży” w „Historii zmian” pokazuje operację. Wpisy powstają po `RedactAsync`. | B5, B8, AU-2, I-AN-3, I-CH-1, F-CH2 |
| 5 | Dane w sklepie WooCommerce | `anonimizacja.md`, sekcja „Czego operacja nie obejmuje”: dane klienta w sklepie WooCommerce (zamówienie, konto klienta, notatki, e-maile) są poza zasięgiem aplikacji; żądanie usunięcia trzeba obsłużyć osobno w panelu sklepu. Uzupełnianie i „Ponów” pobierają te dane ponownie tylko do pamięci, a domena je pomija (pkt 18). Ten sam punkt w „Out of scope” odnotowuje się w `06-implementation-done.md` jako uzupełnienie spec. | D1 |
| 6 | Komentarz `IAuditLog` | Zdanie „Wpisy są tylko dodawane” → „Wpisy są tylko dodawane; jedyna dozwolona zmiana zapisanego wpisu to zastąpienie wartości wskazanych pól znacznikiem przez <see cref="IAuditRedaction"/> (anonimizacja danych klienta, DANE-01, SF-22).” Tak samo `AuditRecord` (XML doc klasy) i `conventions.md` („Financial audit (SF-7)”). | S1, R1, D4 |
| 7 | Kolejność `disabled` przed `404` i drobne niejasności | **Kolejność kontroli `POST`:** brak konta → `401` (endpoint); niepoprawna wersja → `400`; `RetentionMonths` = `null` → `422 anonymization_disabled` **bez dostępu do bazy, także dla nieistniejącego albo już zanonimizowanego zamówienia**; dalej w transakcji: `404 order_not_found` → `409 order_conflict` (wersja) → `409 order_already_anonymized` → `422 retention_not_elapsed`. Tabela endpointu i testy H-AN-1, H-AN-2, I-AN-8 podają to wprost; testy `404` i `409` ustawiają okres. **Zmiana rodzaju klienta** (osoba/firma) po anonimizacji to też zmiana klienta → `422 order_customer_anonymized`; w formularzu pole rodzaju jest tylko do odczytu jak pozostałe pola klienta. **Tekst stanu `available`:** „Okres retencji upłynął {availableFrom}.” (pierwszy dzień dostępności, `dd.MM.yyyy`). | B6, B7, F5, F6, OA-8 |

Niejasności z przeglądu:

| Niejasność | Rozstrzygnięcie | Gdzie |
|---|---|---|
| Rodzaj klienta po anonimizacji | jak w rozstrzygnięciu 7: blokada w domenie (`Customer` porównywany w całości) i w UI | B4, F6 |
| Tekst stanu `available` | data `availableFrom` | F5 |
| Synchronizacja a blokada | Synchronizacja **celowo nie bierze** `IOrderLock`; szereguje ją `sp_getapplock` po ID zewnętrznym. Spójność z anonimizacją daje `rowversion` zamówienia: przestarzały zapis synchronizacji dostaje `Conflict` → `RetryRequired` → wycofanie i ponowienie na świeżym (zanonimizowanym) zamówieniu, a przestarzała anonimizacja dostaje `409 order_conflict`. Zapisane w XML doc `AnonymizeCustomerHandler` i w `anonimizacja.md` (sekcja techniczna). Test I-WH-2 (dostawa po anonimizacji) i istniejące testy wyścigów synchronizacji. | B7, D1 |
| Powód równy już znacznikowi | **Liczy się tylko rzeczywista zmiana pola `reason`** (`EntryFieldChange` z `Changed`): powód już równy `[zanonimizowano]` nie jest liczony w `anonymizedReasons` i nie dostaje własnego wpisu `Anonymized`. Skrót żądania jest zerowany zawsze (to nie pole audytu). Analogicznie nazwa klienta równa znacznikowi nie trafia do `anonymizedFields` (lista = pola zmienione przez operację). | B4, B5, OA-4, RA-3 |

Inne rozstrzygnięcia przyjęte z kodu:

- **Wynik `Order.AnonymizeCustomer`** to istniejący `OrderChangeResult` (zmienione pola albo naruszenie).
  Handler bierze z niego wyłącznie `Field` (kody do `anonymizedFields`); stare wartości nigdy nie trafiają
  do audytu ani logów (test H-AN-7).
- **Fakty retencji wpisów w `GET` i `POST`** pochodzą z tego samego portu
  `IOrderAnonymizationStore.GetRetentionFactsAsync` (zamiast osobnych pól w `OrderDetailsDto`, jak
  sugeruje spec „Types crossing a boundary”): jedno źródło dla obu ścieżek, bez zmian rekordów wpisów
  w DTO. `OrderDetailsDto` dostaje tylko `AnonymizedAtUtc` i wyliczony `Anonymization`.
- **Zapytanie czyta fakty tylko gdy trzeba:** zanonimizowane → bez odczytów; wyłączona → bez odczytów;
  inaczej dwa odczyty (wpisy, audyt). Wynik jest identyczny z regułą domeny (kolejność stanów w
  `CustomerAnonymization.Evaluate`).
- **Zerowanie skrótu wpisu:** `OrderEntry.CreationRequestHash` ma prywatny setter, więc `OrderEntry`
  dostaje `protected void EraseCreationRequestHash()` (32 bajty zerowe, gdy skrót jest). Skrót
  zamówienia ze sklepu jest `null` i zostaje `null`.
- **Testy integracyjne retencji używają zegara, nie SQL:** zamówienie i wpisy tworzy się przy
  `FakeTimeProvider` cofniętym do daty zamówienia, potem zegar przesuwa się do granicy. Po przesunięciu
  o lata sesja wygasa, więc test **loguje się ponownie** (`test.Client.LoginAsync()`). Testy raportów
  podają `StoneFlow:LaunchDate` wcześniejszy od danych (inaczej okres byłby „przed uruchomieniem”).

## Backend Plan

### Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Kernel | `Shared.Kernel/Audit` | operacja `Anonymized` |
| Application (shared) | `Shared.Application/Audit` | porty `IAuditRedaction`, `IAuditActivity`; komentarz `IAuditLog` |
| Domain | `Reporting.Domain/Audit` | `AuditRecord.Redact` — jedyna reguła zmiany zapisanego wpisu |
| Application | `Reporting.Application/ChangeHistory` | kod `anonymized` |
| Infrastructure | `Reporting.Infrastructure/Audit` | implementacje portów na `ReportingDbContext` i `ModuleConnection` |
| Domain | `Orders.Domain/Orders`, `Orders.Domain/Payments` | retencja, anonimizacja, blokady synchronizacji i edycji |
| Application | `Orders.Application` | komenda, zapytanie, porty, audyt operacji, wynik edycji |
| Infrastructure | `Orders.Infrastructure` | opcje, porty, mapowanie, migracja |
| Api | `Orders.Api` | endpoint, problemy, kontrakty |
| Tests | `StoneFlow.Orders.Tests`, `StoneFlow.Reporting.Tests`, `StoneFlow.Api.IntegrationTests` | domena, aplikacja, HTTP, baza, start hosta |

Integrations bez zmian: webhook, uzupełnianie, „Synchronizuj teraz” i „Ponów” idą przez jedyną
implementację `IShopOrderSync` (`ShopOrderSync` → `Order.ApplyFromShop`).

### API contract (backend)

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| POST | `/api/orders/{id:guid}/anonymize` | `{ "version": "<base64>" }` (`OrderVersionRequest`) | `200 { version }`; kolejność błędów: `401` → `400` VPD `errors.version` (także `400` CSRF z middleware) → `422 anonymization_disabled` → `404 order_not_found` → `409 order_conflict` → `409 order_already_anonymized` → `422 retention_not_elapsed` + `availableFrom`; `503` | sesja (fallback), CSRF globalnie | jedno zamówienie, jego zwroty, korekty i ich audyt |
| GET | `/api/orders/{id:guid}` | — | jak dziś + `anonymization { status, retentionStartDate, availableFrom, anonymizedAtUtc }` | sesja | jedno zamówienie |
| PUT | `/api/orders/{id:guid}` | jak dziś | jak dziś + `422 order_customer_anonymized` | sesja, CSRF | jedno zamówienie |
| GET | `/api/change-history` | jak dziś | `operation` może być `anonymized` | sesja | bez zmian |

Szczegóły kształtów — sekcja „API Contract”.

### Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Shared/StoneFlow.Shared.Kernel/Audit/AuditVocabulary.cs` | modify | `AuditOperation.Anonymized` (po `Deleted`) z XML doc |
| `api/src/Shared/StoneFlow.Shared.Application/Audit/IAuditLog.cs` | modify | komentarz „tylko dodawane” z wyjątkiem `IAuditRedaction` (rozstrzygnięcie 6) |
| `api/src/Shared/StoneFlow.Shared.Application/Audit/IAuditRedaction.cs` | create | `IAuditRedaction`, `AuditRedaction`, `AuditFieldSelector`, `IAuditActivity` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Audit/AuditRecord.cs` | modify | `Redact(IReadOnlySet<string> fields, string marker)` → `bool`; XML doc klasy |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Application/ChangeHistory/ChangeHistoryCodes.cs` | modify | `[AuditOperation.Anonymized] = "anonymized"` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Audit/AuditScope.cs` | create | `internal static IQueryable<AuditRecord> ForRoot(this IQueryable<AuditRecord>, AuditObjectReference)` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Audit/AuditRedactor.cs` | create | `IAuditRedaction` w transakcji jednostki pracy |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Audit/AuditActivity.cs` | create | `IAuditActivity` (odczyt `MAX(OccurredAtUtc)`) |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/DependencyInjection.cs` | modify | rejestracja obu portów (scoped) |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/CustomerAnonymization.cs` | create | `Marker`, `RetentionStart`, `AvailableFrom`, `Evaluate`; `AnonymizationStatus`, `AnonymizationAvailability` |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/Order.cs` | modify | `AnonymizedAtUtc`, `AnonymizedByAccountId`, `IsCustomerAnonymized`, `AnonymizeCustomer`; `ApplyFromShop` i `Edit` |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/OrderRuleViolation.cs` | modify | `AlreadyAnonymized`, `CustomerAnonymized` (rodzaje i instancje) |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Payments/OrderEntry.cs` | modify | `protected void EraseCreationRequestHash()` |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Payments/Refund.cs` | modify | `AnonymizeReason(DateTime nowUtc)` → `IReadOnlyList<EntryFieldChange>` |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Payments/SalesCorrection.cs` | modify | jak wyżej |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/Abstractions/Ports.cs` | modify | `IAnonymizationSettings`, `IOrderAnonymizationStore`, `EntryRetentionFacts`, `ReasonEntries` |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/AnonymizeCustomer/AnonymizeCustomer.cs` | create | `AnonymizeCustomerCommand`, `AnonymizeCustomerOutcome`, `AnonymizeCustomerResult`, `AnonymizeCustomerHandler` |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/AnonymizeCustomer/AnonymizationRetention.cs` | create | wspólna operacja zapytania i komendy: fakty → `AnonymizationAvailability` |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/OrderAnonymizationAudit.cs` | create | `Fields` (selektory), `Redaction(order)`, `Anonymized(order, …)`, `EntryAnonymized(entry, …)` |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/GetOrder/GetOrder.cs` | modify | `OrderDetailsDto.AnonymizedAtUtc`, `Anonymization`; handler z ustawieniami i retencją |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/EditOrder/EditOrder.cs` | modify | `EditOrderOutcome.CustomerAnonymized` i gałąź naruszenia |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/OrderCodes.cs` | modify | `Of(AnonymizationStatus)` (`disabled`, `retentionPending`, `available`, `anonymized`) |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/WarsawTime.cs` | modify | `DateOf(DateTime utc)` — data w Europe/Warsaw dla znacznika UTC |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Options/AnonymizationOptions.cs` | create | `AnonymizationOptions`, `AnonymizationOptionsValidator`, `ConfiguredAnonymizationSettings` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderAnonymizationStore.cs` | create | `IOrderAnonymizationStore` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderConfiguration.cs` | modify | dwie kolumny, `CK_Orders_Anonymization` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderReader.cs` | modify | `AnonymizedAtUtc` w projekcji szczegółów |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/Migrations/<ts>_CustomerAnonymization.cs` (+ `.Designer.cs`, snapshot) | create/modify | migracja expand (generowana `dotnet ef`) |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/DependencyInjection.cs` | modify | opcje z walidatorem, porty |
| `api/src/Modules/Orders/StoneFlow.Orders.Api/OrderProblems.cs` | modify | cztery kody i fabryki problemów |
| `api/src/Modules/Orders/StoneFlow.Orders.Api/Endpoints/OrderContracts.cs` | modify | `OrderAnonymizationResponse`, pole w `OrderDetailsResponse`, mapowanie |
| `api/src/Modules/Orders/StoneFlow.Orders.Api/Endpoints/OrdersEndpoints.cs` | modify | `MapPost("/{id:guid}/anonymize", AnonymizeAsync).WithName("Orders.AnonymizeCustomer")`; mapowanie edycji |
| `api/tests/StoneFlow.Orders.Tests/StoneFlow.Orders.Tests.csproj` (+ `packages.lock.json`) | modify | `PackageReference Microsoft.Extensions.TimeProvider.Testing` |
| `api/tests/StoneFlow.Orders.Tests/Domain/CustomerAnonymizationTests.cs` | create | CA-* |
| `api/tests/StoneFlow.Orders.Tests/Domain/OrderAnonymizationTests.cs` | create | OA-* |
| `api/tests/StoneFlow.Orders.Tests/Domain/EntryAnonymizationTests.cs` | create | RA-* (zwrot i korekta) |
| `api/tests/StoneFlow.Orders.Tests/Domain/ShopSyncTests.cs` | modify | SS-1 (`ApplyFromShop` po anonimizacji) |
| `api/tests/StoneFlow.Orders.Tests/Application/FakeAnonymizationPorts.cs` | create | fałszywe porty z dziennikiem wywołań (`CallLog`) |
| `api/tests/StoneFlow.Orders.Tests/Application/AnonymizeCustomerHandlerTests.cs` | create | H-AN-* |
| `api/tests/StoneFlow.Orders.Tests/Application/GetOrderHandlerTests.cs` | create | H-GO-* |
| `api/tests/StoneFlow.Orders.Tests/Application/OrderAnonymizationAuditTests.cs` | create | AU-* |
| `api/tests/StoneFlow.Reporting.Tests/Audit/AuditRecordTests.cs` | modify | AR-* |
| `api/tests/StoneFlow.Reporting.Tests/ChangeHistory/ChangeHistoryCodesTests.cs` | modify | `anonymized` w kontrakcie kodów (CHC-1) |
| `api/tests/StoneFlow.Api.IntegrationTests/Orders/CustomerAnonymizationApiTests.cs` | create | I-AN-* |
| `api/tests/StoneFlow.Api.IntegrationTests/Orders/AnonymizationTestSupport.cs` | create | dane z danymi klienta, przesunięcie zegara i ponowne logowanie, przeszukanie tabel SQL |
| `api/tests/StoneFlow.Api.IntegrationTests/Reporting/AnonymizationReportsUnchangedTests.cs` | create | I-RP-1 |
| `api/tests/StoneFlow.Api.IntegrationTests/Orders/WooCommerceWebhookTests.cs` | modify | I-WH-1, I-WH-2 |
| `api/tests/StoneFlow.Api.IntegrationTests/Integrations/WooCommerceBackfillTests.cs` | modify | I-BF-1 |
| `api/tests/StoneFlow.Api.IntegrationTests/Audit/AuditRedactionTests.cs` | create | I-RD-* |
| `api/tests/StoneFlow.Api.IntegrationTests/AnonymizationStartupTests.cs` | create | I-ST-* |
| `api/tests/StoneFlow.Api.IntegrationTests/Reporting/ChangeHistoryApiTests.cs` | modify | I-CH-1 |

Bez zmian (muszą przejść bez edycji): `EndpointSecurityTests` (bez wpisu w `AllowedAnonymous`),
`StoneFlow.Architecture.Tests`, `OrdersApiTests`, `PaymentsApiTests`, `SalesCorrectionsApiTests`,
`AuditLogTests`, `OpenApiTests`, `MvpFinancialScenariosTests`, testy raportów.

### Implementation steps

Kolejność TDD: każdy krok domeny i aplikacji zaczyna się od czerwonego testu (sekcja „Tests”).

- **S1 `AuditOperation.Anonymized`** (`AuditVocabulary.cs`): nowa wartość po `Deleted`, XML doc
  „Anonimizacja danych klienta zamówienia (SF-22); zakres zmiany bez danych osobowych”. Kolumna
  `Operation` (`nvarchar(20)`, konwersja na tekst) przyjmuje `Anonymized` bez migracji
  (`has-pending-model-changes` dla `ReportingDbContext` — bez zmian). `IAuditLog.cs`: komentarz
  (rozstrzygnięcie 6).
- **S2 Porty współdzielone** (`Shared.Application/Audit/IAuditRedaction.cs`), dokładnie wg spec
  „Types crossing a boundary” plus odczyt:
  - `IAuditRedaction.RedactAsync(AuditRedaction redaction, CancellationToken)` → `Task<int>` (liczba
    zmienionych wpisów); XML doc: tylko w transakcji `IUnitOfWork`, poza nią `InvalidOperationException`;
    obejmuje wpisy z obiektem `Root` albo rodzicem `Root`, wszystkich operacji i autorów; pusta lista
    selektorów albo pusty znacznik → `ArgumentException`.
  - `public sealed record AuditRedaction(AuditObjectReference Root, IReadOnlyCollection<AuditFieldSelector> Fields, string Marker);`
  - `public sealed record AuditFieldSelector(AuditObjectType ObjectType, string Field);`
  - `IAuditActivity.LastRecordedAtUtcAsync(AuditObjectReference root, CancellationToken)` →
    `Task<DateTime?>` — najpóźniejszy czas wpisu (UTC) obiektu `root` i obiektów, których rodzicem jest
    `root`; `null`, gdy wpisów nie ma. Odczyt, dozwolony w transakcji i poza nią. XML doc: „Używany do
    początku okresu retencji (SF-22): obejmuje usunięcia wpisów, których wierszy już nie ma.”
- **S3 Reporting** (AR-*, CHC-1, potem I-RD-*):
  - `AuditRecord.Redact(IReadOnlySet<string> fields, string marker)`: `ArgumentNullException` dla
    `fields`, `ArgumentException.ThrowIfNullOrWhiteSpace(marker)`; nowa lista `Changes`, w której pole z
    `fields` (porównanie `Ordinal`) ma `OldValue`/`NewValue` różne od `null` zastąpione `marker`, a
    `null` zostaje `null`; nazwa, `IsPersonalData`, kolejność i pozostałe pola bez zmian. Zwraca `true`
    i podmienia `Changes` tylko, gdy coś się zmieniło (inaczej `Changes` ta sama instancja). Pozostałe
    właściwości wpisu nie mają setterów publicznych — metoda nie dotyka autora, czasu, obiektu, etykiety,
    daty biznesowej ani operacji. XML doc klasy: wyjątek od „tylko dodawany” (rozstrzygnięcie 6).
  - `ChangeHistoryCodes`: `[AuditOperation.Anonymized] = "anonymized"`; test kontraktu
    (`Operation_and_author_codes_match_the_contract`) dostaje czwarty kod.
  - `AuditScope.ForRoot(root)`: `records.Where(r => (r.ObjectType == root.Type && r.ObjectId == root.Id) || (r.ParentObjectType == root.Type && r.ParentObjectId == root.Id))`
    — jedyna definicja zakresu (korzystają oba porty; indeksy `(ObjectType, ObjectId)` i
    `(ParentObjectType, ParentObjectId)` istnieją).
  - `AuditRedactor(ReportingDbContext context, ModuleConnection connection)`: walidacja argumentów
    (selektory niepuste, znacznik niepusty) → brak `connection.Transaction` → `InvalidOperationException`
    (komunikat jak w `AuditLog`) → `context.AuditEntries.ForRoot(root).ToListAsync(ct)` (śledzone) →
    dla każdego wpisu zbiór pól z selektorów o jego `ObjectType` (pusty → pomiń) → `Redact` → jedno
    `SaveChangesAsync` (UPDATE kolumny `Changes`; `ChangesJson.Comparer` wykrywa zmianę) → liczba
    zmienionych. Bez logowania wartości.
  - `AuditActivity(ReportingDbContext context)`: `context.AuditEntries.AsNoTracking().ForRoot(root).MaxAsync(r => (DateTime?)r.OccurredAtUtc, ct)`.
    Wynik `DateTime` z `Kind = Utc` (`DateTime.SpecifyKind`).
  - DI: `services.AddScoped<IAuditRedaction, AuditRedactor>(); services.AddScoped<IAuditActivity, AuditActivity>();`.
- **B1 Domena — `CustomerAnonymization`** (CA-* najpierw), `Orders.Domain/Orders/CustomerAnonymization.cs`, bez I/O:
  - `public enum AnonymizationStatus { Disabled, RetentionPending, Available, Anonymized }`.
  - `public sealed record AnonymizationAvailability(AnonymizationStatus Status, DateOnly? RetentionStartDate, DateOnly? AvailableFrom);`
  - `public static class CustomerAnonymization`:
    - `public const string Marker = "[zanonimizowano]";` `public const int MinRetentionMonths = 1; public const int MaxRetentionMonths = 1200;`
    - `public static DateOnly RetentionStart(DateOnly orderDate, DateOnly? lastEntryDate, DateOnly lastChangeDate)`
      — najpóźniejsza z dat. XML doc: reguła z Assumptions pkt 2 + rozstrzygnięcie 3; **jedyne miejsce
      reguły; zmiana po D-09 tylko tutaj** (procedura w `docs/operations/anonimizacja.md`).
    - `public static DateOnly AvailableFrom(DateOnly retentionStart, int retentionMonths)` —
      `retentionStart.AddMonths(retentionMonths)`; miesiące spoza 1–1200 → `ArgumentOutOfRangeException`.
    - `public static AnonymizationAvailability Evaluate(bool isAnonymized, int? retentionMonths, DateOnly? retentionStart, DateOnly today)`
      — kolejność: `isAnonymized` → `Anonymized` (daty `null`); `retentionMonths` `null` → `Disabled`
      (daty `null`); `retentionStart` `null` → `ArgumentException` (wywołujący musi podać fakty, gdy
      funkcja włączona); `today < AvailableFrom` → `RetentionPending`, inaczej `Available` (obie daty).
- **B2 Domena — naruszenia** (`OrderRuleViolation.cs`): `AlreadyAnonymized`, `CustomerAnonymized` w enumie
  (z XML doc) i statyczne instancje bez pól.
- **B3 Domena — wpisy** (RA-* najpierw):
  - `OrderEntry.EraseCreationRequestHash()` (protected): `if (CreationRequestHash is not null) CreationRequestHash = new byte[32];`.
  - `Refund.AnonymizeReason(DateTime nowUtc)` / `SalesCorrection.AnonymizeReason(DateTime nowUtc)`:
    `before = Snapshot(); Reason = CustomerAnonymization.Marker; EraseCreationRequestHash(); return Changed(before, nowUtc);`
    — zmiana tylko `reason` (lub żadna), `UpdatedAtUtc` przesuwa `Changed` tylko przy zmianie. XML doc:
    nieodwracalne; kwota, data, rodzaj i metoda bez zmian. `Payment` bez zmian (Assumptions pkt 5).
- **B4 Domena — `Order`** (OA-*, SS-1 najpierw):
  - Właściwości `public DateTime? AnonymizedAtUtc { get; private set; }`,
    `public Guid? AnonymizedByAccountId { get; private set; }`,
    `public bool IsCustomerAnonymized => AnonymizedAtUtc is not null;` (XML doc: fakt nieodwracalny, SF-22).
  - `public OrderChangeResult AnonymizeCustomer(Guid accountId, DateTime nowUtc)`:
    - `IsCustomerAnonymized` → `Rejected(OrderRuleViolation.AlreadyAnonymized)` bez zmian;
      `accountId == Guid.Empty` → `ArgumentException`;
    - `before = Snapshot()`; `Customer = new Customer(Customer.Kind, CustomerAnonymization.Marker, null, null, null, null, null, null)`;
      `Operational = Operational with { Notes = null }`; `Comment = null`;
      `if (CreationRequestHash is not null) CreationRequestHash = new byte[32]`;
      `AnonymizedAtUtc = nowUtc; AnonymizedByAccountId = accountId;`
    - `var result = Changed(before, nowUtc); UpdatedAtUtc = nowUtc; return result;` — lista zmienionych pól
      (tylko pola, które miały inną wartość); `UpdatedAtUtc` ustawiany zawsze (spec pkt 13).
    - Dostawcy, ZO, faktura, flagi, rodzaj klienta, kwoty, daty, kanał, źródło, numer, ID zewnętrzne,
      status ze sklepu, pola sklepu — bez zmian (test OA-2).
  - `ApplyFromShop`: w gałęzi danych `if (!IsCustomerAnonymized) { Customer = data.Customer; }` — reszta bez
    zmian (data, wartość, status, zwroty w sklepie, usunięcie, przejście na „Anulowane”). XML doc metody
    dopisuje regułę.
  - `Edit`: po gałęzi `Source` (dla WooCommerce najpierw `field_owned_by_shop` jak dziś; dla ręcznego po
    sprawdzeniu pól wymaganych) dodać: `if (IsCustomerAnonymized && edit.Customer is { } customer && customer != Customer) return Rejected(OrderRuleViolation.CustomerAnonymized);`
    — przed regułą statusu. Porównanie rekordu obejmuje rodzaj klienta (rozstrzygnięcie 7). Dla zamówienia
    ze sklepu gałąź jest nieosiągalna (klient należy do sklepu i zwraca `FieldOwnedByShop` wcześniej) —
    test OA-10 to dokumentuje.
- **B5 Aplikacja — audyt operacji** (AU-* najpierw), `Orders.Application/OrderAnonymizationAudit.cs`,
  `public static class`:
  - `public static IReadOnlyList<AuditFieldSelector> Fields { get; }` — `order`: `customerName`,
    `customerPhone`, `customerEmail`, `customerStreet`, `customerPostalCode`, `customerCity`,
    `customerTaxId`, `notes`, `comment`; `refund`: `reason`; `salesCorrection`: `reason`. Kody pól budowane
    przez `OrderCodes.Of(OrderField…)` i `OrderCodes.Of(EntryField.Reason)` (bez literałów, test AU-1
    porównuje z oczekiwanymi tekstami).
  - `public static AuditRedaction Redaction(Guid orderId)` → `new(new AuditObjectReference(AuditObjectType.Order, orderId), Fields, CustomerAnonymization.Marker)`.
  - `public static AuditEntry Anonymized(Order order, IReadOnlyList<OrderField> clearedFields, int anonymizedReasons, DateOnly retentionStart, int retentionMonths, Guid accountId)`
    — obiekt jak `OrderAudit` (typ `Order`, data zamówienia, numer); pola w kolejności: `anonymizedFields`
    (tablica JSON kodów przez `OrderAudit.Format(IReadOnlyList<string>)`; pusta lista → `"[]"`, bo wartość
    nie może być `null`), `anonymizedReasons` (`"0"`…), `retentionStartDate` (`yyyy-MM-dd`),
    `retentionMonths` (`"60"`); `old = null`, `personal = false`.
  - `public static AuditEntry EntryAnonymized(OrderEntry entry, string orderNumber, Guid accountId)` —
    obiekt jak `PaymentAudit` (typ z `PaymentAudit.ObjectType`, etykieta `PaymentAudit.Label`, data wpisu,
    rodzic `Order/{entry.OrderId}`), jedno pole `anonymizedFields` = `["reason"]` (rozstrzygnięcie 4).
  - XML doc: wpis bez danych osobowych; zapisywany po `RedactAsync`.
- **B6 Aplikacja — porty i kody** (`Ports.cs`, `OrderCodes.cs`, `WarsawTime.cs`):
  - `public interface IAnonymizationSettings { int? RetentionMonths { get; } }` — XML doc: `null` =
    funkcja wyłączona; brak wartości zastępczej.
  - `public sealed record EntryRetentionFacts(DateOnly? LastEntryDate, DateTime? LastEntryChangeUtc);`
  - `public sealed record ReasonEntries(IReadOnlyList<Refund> Refunds, IReadOnlyList<SalesCorrection> SalesCorrections);`
  - `public interface IOrderAnonymizationStore { Task<EntryRetentionFacts> GetRetentionFactsAsync(Guid orderId, CancellationToken ct); Task<ReasonEntries> LoadReasonEntriesAsync(Guid orderId, CancellationToken ct); }`
    — XML doc: fakty z wpłat, zwrotów i korekt; wpisy do zmiany śledzone, zapisywane przez
    `IOrderStore.SaveAsync` (ten sam kontekst, jedno `SaveChanges`).
  - `OrderCodes.Of(AnonymizationStatus)`, wzorem istniejących `Of(...)`.
  - `WarsawTime.DateOf(DateTime utc)` → `Today(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)))`.
- **B9 Aplikacja — `AnonymizationRetention`** (`AnonymizeCustomer/AnonymizationRetention.cs`), wspólna
  operacja zapytania i komendy (konwencje: „extract a module-local operation”), `public sealed class`
  (jak `OrderStatusAction`) rejestrowana w DI Infrastructure (`AddScoped`):
  - zależności: `IAnonymizationSettings`, `IOrderAnonymizationStore`, `IAuditActivity`, `TimeProvider`;
  - `public int? RetentionMonths => settings.RetentionMonths;`
  - `public async Task<AnonymizationAvailability> EvaluateAsync(Guid orderId, DateOnly orderDate, DateTime orderUpdatedAtUtc, bool isAnonymized, CancellationToken ct)`:
    `isAnonymized` albo `RetentionMonths is null` → `CustomerAnonymization.Evaluate(...)` bez odczytów;
    inaczej `facts = await store.GetRetentionFactsAsync(orderId, ct)`, potem
    `auditAt = await auditActivity.LastRecordedAtUtcAsync(Order/{orderId}, ct)` (po kolei, bez
    `Task.WhenAll` — wspólne połączenie); `lastChange = max(orderUpdatedAtUtc, facts.LastEntryChangeUtc, auditAt)`;
    `start = CustomerAnonymization.RetentionStart(orderDate, facts.LastEntryDate, WarsawTime.DateOf(lastChange))`;
    `Evaluate(false, months, start, WarsawTime.Today(time.GetUtcNow()))`.
  - XML doc: jedyne złożenie faktów; reguła należy do domeny.
- **B7 Aplikacja — `AnonymizeCustomer`** (H-AN-* najpierw), `AnonymizeCustomer/AnonymizeCustomer.cs`:
  - `public sealed record AnonymizeCustomerCommand(Guid AccountId, Guid OrderId, string? Version) : ICommand<AnonymizeCustomerResult>;`
  - `public enum AnonymizeCustomerOutcome { Saved, ValidationFailed, Disabled, NotFound, Conflict, AlreadyAnonymized, RetentionNotElapsed }`
  - `public sealed record AnonymizeCustomerResult(AnonymizeCustomerOutcome Outcome, byte[]? Version = null, FieldErrors? Errors = null, DateOnly? AvailableFrom = null);`
  - `public sealed class AnonymizeCustomerHandler(IUnitOfWork unitOfWork, IAuditLog auditLog, IAuditRedaction auditRedaction, IOrderLock orderLock, IOrderStore store, IOrderAnonymizationStore anonymizationStore, AnonymizationRetention retention, TimeProvider timeProvider)`:
    1. `OrderVersion.Validate(command.Version, errors)` → `ValidationFailed`.
    2. `retention.RetentionMonths is not { } months` → `Disabled` (bez wywołań portów i jednostki pracy).
    3. `unitOfWork.ExecuteInTransactionAsync(work, commitWhen: r => r.Outcome == Saved, ct)`; w `work`:
       1. `orderLock.LockAsync(orderId)` `null` → `NotFound`.
       2. `store.LoadAsync(orderId)` (`null` → `NotFound`, wyścig niemożliwy pod blokadą, ale bez `!`);
          wersja ≠ podanej → `Conflict`.
       3. `stored.Order.IsCustomerAnonymized` → `AlreadyAnonymized`.
       4. `availability = await retention.EvaluateAsync(order.Id, order.OrderDate, order.UpdatedAtUtc, false, ct)`
          (po blokadzie: dodanie, poprawa i usunięcie wpisów czekają); `RetentionPending` →
          `RetentionNotElapsed` z `AvailableFrom`. (Tu `months` z kroku 2 = `availability`, bo ustawienia
          są singletonem od startu.)
       5. `nowUtc = timeProvider.GetUtcNow().UtcDateTime`; `changes = order.AnonymizeCustomer(accountId, nowUtc)`;
          niepowodzenie → `AlreadyAnonymized` (obrona).
       6. `entries = await anonymizationStore.LoadReasonEntriesAsync(orderId)`; dla każdego zwrotu i korekty
          `AnonymizeReason(nowUtc)`; lista wpisów z niepustą zmianą = `changedEntries`.
       7. `store.SaveAsync(order, version)` → `Conflict` (całość wycofana).
       8. `await auditRedaction.RedactAsync(OrderAnonymizationAudit.Redaction(orderId), ct)`.
       9. `await auditLog.RecordAsync(OrderAnonymizationAudit.Anonymized(order, changes.Changes.Select(c => c.Field).ToList(), changedEntries.Count, availability.RetentionStartDate!.Value, months, accountId), ct)`
          (`!` uzasadnione: `Available` zawsze ma datę — kontrakt `Evaluate`, test CA-8), potem dla każdego
          z `changedEntries` `RecordAsync(OrderAnonymizationAudit.EntryAnonymized(entry, order.Number, accountId))`.
       10. `Saved` z `saved.Version`.
    - **Nie** wywołuje `OrderAudit.Updated` ani `PaymentAudit.Updated` (stare wartości to dane osobowe).
      Nie loguje niczego. Wyjątek w dowolnym kroku wycofuje całość (`IUnitOfWork`).
    - XML doc: kolejność kontroli (rozstrzygnięcie 7); blokada szereguje z wpisami; synchronizacja nie bierze
      blokady, spójność z nią daje `rowversion` (niejasność „Synchronizacja a blokada”).
  - Rejestracja przez `AddHandlersFromAssembly` (bez zmian DI handlera).
- **B8 Aplikacja — `GetOrder` i `EditOrder`** (H-GO-* najpierw):
  - `OrderDetailsDto`: `public DateTime? AnonymizedAtUtc { get; init; }`,
    `public AnonymizationAvailability? Anonymization { get; init; }` (XML doc: ustawia zapytanie;
    `null` tylko przed ustawieniem przez handler).
  - `GetOrderHandler(IOrderReader reader, AnonymizationRetention retention)`: `dto = await reader.GetAsync`;
    `null` → `null`; `availability = await retention.EvaluateAsync(dto.Id, dto.OrderDate, dto.UpdatedAtUtc, dto.AnonymizedAtUtc is not null, ct)`;
    `return dto with { Anonymization = availability };`. Zapytanie niczego nie zapisuje.
  - `EditOrderOutcome.CustomerAnonymized`; w `switch` naruszenia: `case OrderRuleViolationKind.CustomerAnonymized: return new EditOrderResult(EditOrderOutcome.CustomerAnonymized);`
    (bez zmian, bez audytu; jednostka pracy wycofana jak przy innych naruszeniach).
- **B10 Infrastruktura — opcje** (`Options/AnonymizationOptions.cs`, wzór `WooCommerceWebhook.cs` i
  `LaunchDateOptionsValidator`):
  - `public sealed class AnonymizationOptions { public const string SectionName = "StoneFlow:Anonymization"; public const string RetentionMonthsKey = SectionName + ":RetentionMonths"; public string? RetentionMonths { get; set; } public static bool TryParseRetentionMonths(string? text, out int? months) }`
    — `null`/pusty/białe znaki → `true`, `months = null`; `int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)` w zakresie
    `CustomerAnonymization.MinRetentionMonths`–`MaxRetentionMonths` → `true`; inaczej `false`.
  - `AnonymizationOptionsValidator : IValidateOptions<AnonymizationOptions>`: `false` →
    `Fail($"'{RetentionMonthsKey}' musi być liczbą całkowitą miesięcy od 1 do 1200 albo pozostać nieustawione (funkcja wyłączona).")`
    — bez wartości z konfiguracji.
  - `internal sealed class ConfiguredAnonymizationSettings(IOptions<AnonymizationOptions> options) : IAnonymizationSettings` —
    parsuje przy każdym odczycie (`IOptions` to singleton z wartością od startu, więc wynik stały).
  - DI (`DependencyInjection.cs`): `services.AddSingleton<IValidateOptions<AnonymizationOptions>, AnonymizationOptionsValidator>(); services.AddOptions<AnonymizationOptions>().BindConfiguration(AnonymizationOptions.SectionName).ValidateOnStart(); services.AddSingleton<IAnonymizationSettings, ConfiguredAnonymizationSettings>(); services.AddScoped<IOrderAnonymizationStore, OrderAnonymizationStore>(); services.AddScoped<AnonymizationRetention>();`.
- **B11 Infrastruktura — trwałość:**
  - `OrderConfiguration`: `public const string AnonymizationConstraint = "CK_Orders_Anonymization";` w
    `ToTable(..., table => …)` `HasCheckConstraint(AnonymizationConstraint, "([AnonymizedAtUtc] IS NULL AND [AnonymizedByAccountId] IS NULL) OR ([AnonymizedAtUtc] IS NOT NULL AND [AnonymizedByAccountId] IS NOT NULL)")`;
    `Property(o => o.AnonymizedAtUtc).HasColumnType("datetime2")`, `Property(o => o.AnonymizedByAccountId)`.
  - Migracja: `dotnet ef migrations add CustomerAnonymization -p api/src/Modules/Orders/StoneFlow.Orders.Infrastructure -s api/src/StoneFlow.Api -c OrdersDbContext -o Persistence/Migrations --configuration Release`.
    Przejrzeć `Up`: dwie kolumny `NULL` i check; `Down`: odwrotnie. Bez wartości domyślnych i bez
    przepisywania danych (expand, stare API ich nie mapuje i wstawia `NULL`).
  - `OrderAnonymizationStore(OrdersDbContext context)`:
    - `GetRetentionFactsAsync`: po kolei trzy zapytania (wpłaty, zwroty, korekty), każde
      `Where(e => e.OrderId == orderId).GroupBy(_ => 1).Select(g => new { Date = g.Max(e => e.Date), Changed = g.Max(e => e.UpdatedAtUtc) }).SingleOrDefaultAsync(ct)`;
      wynik maksimum z trzech (`null`, gdy brak wpisów). `UpdatedAtUtc` → `DateTime.SpecifyKind(..., Utc)`.
    - `LoadReasonEntriesAsync`: `context.Refunds.Where(OrderId).ToListAsync`, potem
      `context.SalesCorrections.Where(OrderId).ToListAsync` (śledzone).
  - `OrderReader.GetAsync`: `o.AnonymizedAtUtc` w projekcji i `AnonymizedAtUtc = row.AnonymizedAtUtc` w
    inicjalizatorze DTO.
- **B12 Api** (`OrderProblems.cs`, `OrderContracts.cs`, `OrdersEndpoints.cs`):
  - kody i teksty dokładnie wg spec: `AnonymizationDisabledCode = "anonymization_disabled"` (422),
    `RetentionNotElapsedCode = "retention_not_elapsed"` (422, `detail` „Anonimizacja będzie dostępna od
    {dd.MM.yyyy}, po upływie okresu retencji.”, rozszerzenie `availableFrom` = `yyyy-MM-dd`),
    `AlreadyAnonymizedCode = "order_already_anonymized"` (409), `CustomerAnonymizedCode = "order_customer_anonymized"` (422);
    fabryki `AnonymizationDisabled()`, `RetentionNotElapsed(DateOnly)`, `AlreadyAnonymized()`, `CustomerAnonymized()`.
  - `public sealed record OrderAnonymizationResponse(string Status, string? RetentionStartDate, string? AvailableFrom, DateTime? AnonymizedAtUtc);`
    `OrderDetailsResponse` + ostatni parametr `OrderAnonymizationResponse Anonymization`; mapowanie: status
    `OrderCodes.Of`, daty `yyyy-MM-dd` (dla `RetentionPending`/`Available`), `AnonymizedAtUtc` tylko dla
    `Anonymized` (z `dto.AnonymizedAtUtc`, UTC). `dto.Anonymization` `null` → `InvalidOperationException`
    (błąd programu: handler zawsze ustawia).
  - Endpoint `AnonymizeAsync(Guid id, OrderVersionRequest? request, IDispatcher dispatcher, HttpContext context, CancellationToken ct)`
    — wzór `EditAsync`: brak konta → `Unauthorized`; `switch` wyniku na `Ok(new OrderVersionResponse(...))`
    i problemy; `ValidationFailed` → `ValidationProblem`. `.WithName("Orders.AnonymizeCustomer")`, bez
    `.AllowAnonymous()`.
  - `EditAsync`: `EditOrderOutcome.CustomerAnonymized => OrderProblems.CustomerAnonymized()`.
- **B13 Testy integracyjne** (sekcja „Tests”, I-*), wzór `OrdersApiTests`/`PaymentsApiTests`
  (`OrdersTest.CreateAsync(sqlServer, now, configureServices, settings)`), dane fikcyjne
  (`Jan Testowy`, `jan@example.com`, `000 000 000`, `00-000 Testowo`, NIP `0000000000`).

### Authorization

`POST /api/orders/{id}/anonymize`: uwierzytelnienie — polityka domyślna hosta (sesja), CSRF — middleware
globalny; konto z `ClaimTypes.NameIdentifier` w endpoincie (brak → `401`); konkretny zasób — handler
(istnienie pod blokadą, wersja, stan, retencja), niezależnie od UI. Jedna firma, jedno konto: brak
zakresu per konto. `GET` bez zmian (sesja). Testy: I-AN-9 (`401`, `400` CSRF), `EndpointSecurityTests`
bez nowego wyjątku.

### Background jobs

None.

### Edge cases & null safety (backend)

| Przypadek | Gdzie | Test |
|---|---|---|
| Brak klucza / `""` / `"   "` | B10 → `null`; B7 krok 2 → `422`; `GET` `disabled` | I-ST-1, I-ST-2, H-AN-2, I-AN-1 |
| `0`, `-1`, `1201`, `abc`, `12.5` | B10 walidator → start przerwany, komunikat bez wartości | I-ST-3 |
| Wyłączona + nieistniejące / zanonimizowane zamówienie | B7 krok 2 przed transakcją → `422 anonymization_disabled`; `GET` zanonimizowanego → `anonymized` | H-AN-2, I-AN-8 |
| Granica dnia i północ Warszawy | B1 `Evaluate` (`today < availableFrom`), B9 `WarsawTime` | CA-4, H-GO-3, I-AN-2 |
| 31.01 + 1 miesiąc | B1 `AddMonths` | CA-5 |
| Wpis z późniejszą datą / dopisany dziś z datą wsteczną | B9 (`LastEntryDate`, `LastEntryChangeUtc`) | CA-2, CA-3, I-AN-4 |
| Usunięty wpis (rozstrzygnięcie 3) | B9 (`IAuditActivity`) | H-GO-4, I-AN-5 |
| Anulowanie/zmiana ze sklepu dziś | `Order.UpdatedAtUtc` | CA-3, I-AN-4 |
| Dostawa `Unchanged` | `ApplyFromShop` nie przesuwa `UpdatedAtUtc` (bez zmian) | SS-1 |
| Data zamówienia w przyszłości | B1 | CA-6 |
| Dwa okna / podwójne kliknięcie | B7: wersja przed stanem → `409 order_conflict`; aktualna wersja → `409 order_already_anonymized`; jeden wpis `Anonymized` | H-AN-3, I-AN-6 |
| Równoczesny zwrot | `IOrderLock` | I-AN-7 (sekwencyjnie: zwrot przed → znacznik; po → powód użytkownika) |
| Równoczesna dostawa ze sklepu | `rowversion` (`RetryRequired`) | I-WH-2 |
| Wyjątek w `RedactAsync` / `RecordAsync` | `IUnitOfWork` wycofuje całość | I-AN-10 |
| Zamówienie bez wpisów audytu | `RedactAsync` → 0, `IAuditActivity` → `null` | H-AN-8, I-RD-3 |
| Zamówienie ze sklepu (skrót `null`) | B4 | OA-5 |
| Nazwa / powód już równe znacznikowi | B4, B3 (tylko rzeczywiste zmiany) | OA-4, RA-3 |
| Zamówienie ręczne bez telefonu itp. | B4 lista tylko pól z wartością | OA-3 |
| `RetentionStartDate` przy `Available` | kontrakt `Evaluate` | CA-8 |
| Zmiana rodzaju klienta po anonimizacji | B4 `Edit` | OA-8 |
| `PUT` zamówienia ze sklepu z innym klientem | `field_owned_by_shop` jak dziś | OA-10 |
| Ponowienie `POST /api/orders` ze starym kluczem | skrót zerowy → `409 idempotency_key_reused` | I-AN-11 |
| Konfiguracja usunięta po anonimizacji | `Evaluate`: `anonymized` przed `disabled`; `ApplyFromShop` nie zależy od konfiguracji | CA-7, I-WH-1 |

### Risks (backend)

- Odczyt audytu w zapytaniu `GET /api/orders/{id}` (gdy funkcja włączona) to jedno dodatkowe zapytanie na
  istniejących indeksach; przy skali D-08 pomijalne.
- `ChangesJson.Comparer` musi wykryć podmianę listy (`SequenceEqual` rekordów) — test I-RD-1 sprawdza zapis
  w bazie, nie tylko w pamięci.
- Przesunięcie `FakeTimeProvider` o lata unieważnia sesję i może wpłynąć na limity Access (reset hasła) —
  testy logują się ponownie po przesunięciu; jeśli logowanie po przesunięciu okaże się niestabilne, dane
  cofa się SQL-em (`OrderDate`, `UpdatedAtUtc`, daty i `UpdatedAtUtc` wpisów oraz `OccurredAtUtc` audytu).

## Frontend Plan

### Step 0 — aplikacja

Jedna aplikacja: `app/` (Angular). Zmiany w `features/orders/` i `features/change-history/` oraz
w fikstur `src/testing/orders-testing.ts`. Obszary się nie importują nawzajem.

### Expected API contract (wyprowadzony ze spec)

| Method | Route | Request shape | Response shape | Used by |
|---|---|---|---|---|
| GET | `/api/orders/{id}` | — | `OrderDetails` + `anonymization: { status: 'disabled' \| 'retentionPending' \| 'available' \| 'anonymized'; retentionStartDate: string \| null; availableFrom: string \| null; anonymizedAtUtc: string \| null }` | `OrdersService.loadOrder` → `OrderDetailsPage` |
| POST | `/api/orders/{id}/anonymize` | `{ version: string }` | `200 { version }`; błędy Problem Details z `code`: `order_conflict` (409), `order_already_anonymized` (409), `anonymization_disabled` (422), `retention_not_elapsed` (422, `availableFrom`), `order_not_found` (404); VPD (400) | `OrdersApi.anonymize` → `OrdersService.anonymize` |
| PUT | `/api/orders/{id}` | jak dziś | jak dziś + `422 order_customer_anonymized` (`detail`) | `OrderForm` (podsumowanie błędów) |
| GET | `/api/change-history` | jak dziś | `operation: 'anonymized'` możliwe; pola `anonymizedFields` (JSON tablica kodów), `anonymizedReasons` (liczba jako tekst), `retentionStartDate` (`yyyy-MM-dd`), `retentionMonths` (liczba jako tekst); dla zwrotu/korekty `anonymizedFields` = `["reason"]` | `ChangeHistoryPage` |

### Routing

| Path | Page | Loaded how | Guard |
|---|---|---|---|
| `/zamowienia/:id` | `OrderDetailsPage` (bez zmian trasy) | istniejący `loadComponent` | istniejący `authGuard` |
| `/historia-zmian` | bez zmian | bez zmian | bez zmian |

### Files

| File path | Action | Purpose |
|---|---|---|
| `app/src/app/features/orders/order-models.ts` | modify | `AnonymizationStatus`, `OrderAnonymization`, `OrderDetails.anonymization`; `ORDER_PROBLEM.alreadyAnonymized`, `anonymizationDisabled`, `retentionNotElapsed`, `customerAnonymized`; `ANONYMIZED_BADGE_LABEL` |
| `app/src/app/features/orders/orders-api.ts` | modify | `anonymize(id, version)` |
| `app/src/app/features/orders/orders-api.spec.ts` | modify | F-A1 |
| `app/src/app/features/orders/orders.service.ts` | modify | `anonymize`; `change()` mapuje trzy nowe kody na `stateChanged` |
| `app/src/app/features/orders/orders.service.spec.ts` | modify | F-S1 |
| `app/src/app/features/orders/order-anonymization.ts` | create | czysta funkcja `anonymizationText(a: OrderAnonymization): string \| null` (teksty stanów z formatowaniem dat) — jedno miejsce tekstów |
| `app/src/app/features/orders/order-anonymization.spec.ts` | create | F-T1 |
| `app/src/app/features/orders/order-details-page.ts` | modify | blok, dialog, akcja `anonymize`, oznaczenie |
| `app/src/app/features/orders/order-details-page.html` | modify | jak wyżej |
| `app/src/app/features/orders/order-details-page.spec.ts` | modify | F-P1 … F-P6 |
| `app/src/app/features/orders/order-form.ts` | modify | wejście `customerAnonymized` |
| `app/src/app/features/orders/order-form.html` | modify | klient tylko do odczytu z opisem |
| `app/src/app/features/orders/order-form.spec.ts` | modify | F-F1 |
| `app/src/testing/orders-testing.ts` | modify | domyślne `anonymization: { status: 'disabled', … null }` w `orderDetails()` |
| `app/src/app/features/change-history/change-history-models.ts` | modify | `'anonymized'` w `AuditOperationCode`, `OPERATION_LABELS`, `OPERATION_TONES`; `showsOldValues(operation)` |
| `app/src/app/features/change-history/change-history-page.html` | modify | warunki `row.operation !== 'created'` → `showsOldValues(row.operation)` |
| `app/src/app/features/change-history/change-history-page.ts` | modify | eksponuje `showsOldValues` |
| `app/src/app/features/change-history/audit-field-catalog.ts` | modify | format `fields`; pola operacji w `order`, `refund`, `salesCorrection` |
| `app/src/app/features/change-history/audit-field-catalog.spec.ts` | modify | F-CH1 |
| `app/src/app/features/change-history/change-history-page.spec.ts` | modify | F-CH2 |

### Implementation steps

- **F1 Modele** (`order-models.ts`): typy wg spec „Frontend (`order-models.ts`)”; `OrderDetails.anonymization`
  (wymagane); kody problemów; `ANONYMIZED_BADGE_LABEL = 'Dane klienta zanonimizowane'`. Fikstura
  `orderDetails()` z domyślnym stanem `disabled` (wszystkie daty `null`), żeby istniejące specy przeszły.
- **F2 Klient** (`orders-api.ts`): `anonymize(id: string, version: string): Promise<OrderVersion>` →
  `POST ${BASE}/${encodeURIComponent(id)}/anonymize` z `{ version }` (jak `cancel`).
- **F3 Serwis** (`orders.service.ts`): `anonymize(id, version)` → `this.change(() => this.api.anonymize(id, version), 'Dane klienta zanonimizowane.')`;
  w `change()` kody `alreadyAnonymized`, `anonymizationDisabled`, `retentionNotElapsed` → `{ kind: 'stateChanged', detail }`
  (obok `alreadyCancelled`/`notCancelled`). `order_customer_anonymized` zostaje w gałęzi `error` (trafia do
  formularza). Komentarz typu `ChangeOrderOutcome.stateChanged` uzupełniony o nowe kody.
- **F4 Teksty stanów** (`order-anonymization.ts`): `anonymizationText(anonymization)`:
  - `available` → „Okres retencji upłynął {formatDate(availableFrom)}. Anonimizacja usunie dane klienta,
    uwagi, komentarz i powody zwrotów i korekt.”;
  - `retentionPending` → „Dostępne po upływie okresu retencji, od {availableFrom}. Okres liczy się od
    {retentionStartDate} — najpóźniejszej z dat zamówienia, jego wpisów i ostatniej zmiany.”;
  - `disabled` → „Funkcja wyłączona do decyzji o okresie retencji.”;
  - `anonymized` → „Dane klienta zanonimizowano {formatDateTime(anonymizedAtUtc)} (Europe/Warsaw). Operacji
    nie można cofnąć.”
  - brak wymaganej daty → tekst bez daty z `NO_DATA` (nie rzuca). Daty wyłącznie przez `shared/format`.
- **F5 Szczegóły** (`order-details-page.ts/html`):
  - `actionBusy` typu `'cancel' | 'restore' | 'anonymize' | null`; `runAction` przyjmuje `'anonymize'`.
  - `anonymization = computed(() => this.order()?.anonymization ?? null)`, `anonymized = computed(() => status === 'anonymized')`,
    `canAnonymize = computed(() => status === 'available' && !this.entryActive() && this.actionBusy() === null)`,
    `anonymizationText = computed(...)` (F4).
  - Nagłówek: przy `anonymized()` trzeci `app-status-badge` `[label]="anonymizedBadge"` `tone="neutral"` `[marker]="true"`.
  - Sekcja „Klient”: pod `<dl>` blok `<div>` z `<h3 id="dane-osobowe-naglowek">Dane osobowe klienta</h3>`,
    `<p id="anonimizacja-opis">{{ anonymizationText() }}</p>` i — poza stanem `anonymized` — `app-button`
    `variant="danger"`, `busyLabel="Anonimizowanie…"`, `[busy]="actionBusy() === 'anonymize'"`,
    `[disabled]="!canAnonymize() && actionBusy() !== 'anonymize'"`, `describedBy="anonimizacja-opis"`,
    `(pressed)="anonymizeCustomer()"`, tekst „Anonimizuj dane klienta”. Blok jest w gałęzi widoku szczegółów
    (podczas edycji widoku nie ma, więc przycisk jest niedostępny w edycji). Przy `entryActive()` opis
    dodatkowo: istniejący akapit o formularzu wpisu wystarcza (bez nowego tekstu).
  - Dialog `<app-confirm-dialog #anonymizeDialog tone="danger" [heading]="'Zanonimizować dane klienta zamówienia ' + order.number + '?'" confirmLabel="Anonimizuj dane klienta">`
    z dwoma akapitami treści ze spec (UI pkt 3).
  - `anonymizeCustomer()`: `order`, `canAnonymize()`, `await dialog.open()` (anulowanie → nic); potem własny
    przebieg (nie `handleChange`, bo wymaga ponownego odczytu przy błędach stanu):
    - `saved` → `handleChange` (ponowny odczyt i komunikat „Dane klienta zanonimizowane.” w `role="status"`);
    - `conflict` → alert `CONFLICT_MESSAGE` z „Wczytaj aktualne dane” (jak dziś);
    - `stateChanged` → `await this.load(id, { keepView: true })`, potem `alert.set({ text: detail ?? STATE_CHANGED_MESSAGE, reload: false })`
      (kolejność: odczyt czyści alert, więc alert po odczycie);
    - `notFound` → `status = 'notFound'`;
    - `error` `network` → alert „Nie udało się zanonimizować danych. Spróbuj ponownie.”; inny `error` →
      `orderErrorMessage`.
  - `[customerAnonymized]="anonymized()"` przekazywane do `app-order-form`.
- **F6 Formularz** (`order-form.ts/html`): `readonly customerAnonymized = input(false)`;
  `customerReadOnly = computed(() => this.ownsCustomer() || this.customerAnonymized())`; szablon używa
  `customerReadOnly()` zamiast `ownsCustomer()` dla bloku klienta (rodzaj też tylko do odczytu); przy
  `customerAnonymized()` akapit „Dane klienta zanonimizowano; nie można ich zmienić.” (`id` powiązany z
  `aria-describedby` fieldsetu). `toOrderInput` bez zmian: zamówienie ręczne wysyła klienta z modelu
  (wartości zapisane, niezmienione), zamówienie ze sklepu go nie wysyła. `422 order_customer_anonymized`
  trafia do podsumowania przez istniejące `orderErrorMessage(error)` (`detail`).
- **F7 Historia zmian:**
  - `change-history-models.ts`: `AuditOperationCode` + `'anonymized'`; `OPERATION_LABELS.anonymized = 'Anonimizacja'`,
    `OPERATION_TONES.anonymized = 'warning'`; `export function showsOldValues(op: AuditOperationCode): boolean { return op !== 'created' && op !== 'anonymized'; }`.
  - `change-history-page.html`: oba warunki kolumny „Przed” na `showsOldValues(row.operation)`.
  - `audit-field-catalog.ts`: `FieldFormat` + `'fields'`; definicja `fieldList(label)`; `formatFieldValue` dla
    `fields`: `JSON.parse` w `try` → tablica tekstów → etykiety `fieldLabel(type, code)` połączone „, ”
    (pusta tablica → `NO_DATA`; błąd parsowania → surowa wartość, funkcja nie rzuca). Katalog:
    `order.anonymizedFields = fieldList('Zanonimizowane pola')`, `order.anonymizedReasons = number('Zanonimizowane powody zwrotów i korekt')`,
    `order.retentionStartDate = date('Początek okresu retencji')`, `order.retentionMonths = number('Okres retencji (miesiące)')`,
    `refund.anonymizedFields` i `salesCorrection.anonymizedFields` = `fieldList('Zanonimizowane pola')`.
    Wartości `[zanonimizowano]` w dawnych wpisach — format `text`, bez zmian.

### State

- Lokalny stan strony: `actionBusy` (rozszerzony), `alert`, `notice` — istniejące sygnały.
- Stan serwera: `order().anonymization` z `GET`; brak osobnego żądania stanu akcji.
- Wartości pochodne: `anonymized`, `canAnonymize`, `anonymizationText` (computed); frontend nie liczy dat
  retencji (niezmiennik 2 — tylko wyświetla daty z API).

### Styling

- Tokeny i prymitywy: `app-button` (`danger` dla jedynej akcji destrukcyjnej sekcji klienta),
  `app-status-badge` (`neutral` + `marker`), `app-confirm-dialog` (`danger`), klasy `text-(--sf-muted)` dla
  opisu; bez literałów kolorów (`lint:colors`).
- Akcja główna widoku pozostaje „Edytuj”; anonimizacja jest w sekcji klienta, nie w wierszu akcji
  zamówienia (spec UI pkt 2).
- Responsywność: blok w siatce sekcji klienta (`flex flex-col gap-3`), przycisk `w-full sm:w-auto`, długi
  tekst z `break-words`; dialog z istniejącymi stylami — bez poziomego przewijania przy 360 px.

### Accessibility

- Przycisk i dialog obsługiwane klawiaturą (natywny `button`, `dialog` z Escape i powrotem fokusu w
  `app-confirm-dialog`).
- Wyłączony przycisk ma `aria-describedby` do widocznego opisu (`describedBy`); opis nie znika.
- Nagłówek bloku `h3` pod `h2` „Klient” (kolejność nagłówków).
- Komunikat sukcesu w istniejącym `role="status"`, błąd w `role="alert"`.
- W formularzu tylko do odczytu: lista `dl` z opisem powiązanym przez `aria-describedby`.

### Edge cases (frontend)

| Przypadek | Obsługa | Test |
|---|---|---|
| Cztery stany | `anonymizationText`, warunki przycisku | F-T1, F-P1 |
| Brak daty mimo stanu | tekst z „Brak danych”, bez wyjątku | F-T1 |
| Aktywny formularz wpisu / inna akcja | `canAnonymize` false | F-P4 |
| Anulowanie dialogu | brak żądania | F-P2 |
| `retention_not_elapsed` po zmianie w innym oknie | ponowny odczyt + `detail` | F-P3 |
| Konflikt | istniejący alert z „Wczytaj aktualne dane” | F-P3 |
| Błąd sieci | komunikat anonimizacji | F-P3 |
| Zamówienie ze sklepu zanonimizowane | klient już tylko do odczytu (`ownsCustomer`), opis anonimizacji dodatkowo | F-F1 |
| Nieznany kod pola w `anonymizedFields` / zły JSON | nazwa kodu / surowa wartość | F-CH1 |

### Risks (frontend)

- Wiele specy buduje `OrderDetails` przez `orderDetails()` — wymagane pole z wartością domyślną w fikstury
  usuwa ryzyko błędów `typecheck`; specy budujące obiekt literałem (jeśli są) trzeba uzupełnić.
- Testy E2E i axe odwiedzają szczegóły zamówienia: nowy blok w stanie `disabled` (E2E bez konfiguracji)
  musi przejść skan axe (wyłączony przycisk z opisem) — sprawdzić `npm run e2e` lokalnie, jeśli dostępny.

## API Contract

| Method | Route | Request | Response | Errors |
|---|---|---|---|---|
| POST | `/api/orders/{id:guid}/anonymize` | `{ "version": "<base64>" }` | `200 { "version": "<base64>" }` | w tej kolejności: `401`; `400` VPD (`errors.version`) lub CSRF; `422 anonymization_disabled` (także gdy zamówienia nie ma); `404 order_not_found`; `409 order_conflict`; `409 order_already_anonymized`; `422 retention_not_elapsed` + `availableFrom` (`yyyy-MM-dd`); `503` |
| GET | `/api/orders/{id:guid}` | — | dotychczasowe pola + `anonymization` | jak dziś |
| PUT | `/api/orders/{id:guid}` | jak dziś | jak dziś | jak dziś + `422 order_customer_anonymized` |
| GET | `/api/change-history` | jak dziś | `operation` ∈ `created`, `updated`, `deleted`, `anonymized` | jak dziś |

`anonymization`:

```json
{
  "status": "disabled | retentionPending | available | anonymized",
  "retentionStartDate": "2021-03-15 | null",
  "availableFrom": "2026-03-15 | null",
  "anonymizedAtUtc": "2026-11-13T10:15:00Z | null"
}
```

Daty retencji tylko dla `retentionPending`/`available`; `anonymizedAtUtc` tylko dla `anonymized`. Konto
wykonawcy nie trafia do odpowiedzi.

Wpis audytu `anonymized` (obiekt `order`): pola `anonymizedFields` (`["customerName","customerEmail","notes"]`),
`anonymizedReasons` (`"2"`), `retentionStartDate` (`"2021-03-15"`), `retentionMonths` (`"60"`); `old` =
`null`, `personal` = `false`. Wpis `anonymized` zwrotu / korekty: `anonymizedFields` = `["reason"]`.

## Integration Check

| Frontend expects | Backend provides | Gap |
|---|---|---|
| `POST /api/orders/{id}/anonymize` z `{ version }` → `{ version }` | `OrderVersionRequest` → `OrderVersionResponse` (B12) | brak |
| kody `order_conflict`, `order_already_anonymized`, `anonymization_disabled`, `retention_not_elapsed`, `order_not_found` | `OrderProblems` (B12), kolejność B7 | brak |
| `order_customer_anonymized` z `detail` przy `PUT` | `EditOrderOutcome.CustomerAnonymized` → `OrderProblems.CustomerAnonymized()` | brak |
| `anonymization` z czterema polami i kodami statusu camelCase | `OrderAnonymizationResponse` (System.Text.Json camelCase), `OrderCodes.Of(AnonymizationStatus)` | brak |
| `anonymizedAtUtc` ISO UTC z „Z” | `DateTime` UTC (jak `updatedAtUtc`) | brak — ten sam format co istniejące pola czasu |
| operacja `anonymized` i pola wpisu | `ChangeHistoryCodes`, `OrderAnonymizationAudit` | brak |
| `anonymizedFields` dla `refund`/`salesCorrection` | `EntryAnonymized` (rozstrzygnięcie 4) | brak — frontend wyprowadził z przeglądu (uwaga 4), backend z rozstrzygnięcia; zgodne |
| Uwierzytelnienie: sesja + XSRF przez `provideAppHttp()` | polityka domyślna + CSRF globalnie | brak |

No integration gaps.

## Invariant Check

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sales separate from cash | B4/B3 zmieniają tylko klienta, uwagi, komentarz, powody i skróty; źródła raportów nie czytają tych pól | I-RP-1 (cztery odczyty identyczne), OA-2 |
| 2. Backend decimal money | brak obliczeń pieniężnych; retencja na `DateOnly` w domenie; frontend tylko wyświetla daty | CA-*, F-T1 (brak arytmetyki dat w UI) |
| 3. API-enforced access | polityka domyślna, CSRF, handler: konfiguracja, istnienie, wersja, stan, retencja | I-AN-1, I-AN-2, I-AN-9, `EndpointSecurityTests` |
| 4. Retries never duplicate | drugi `POST` → `409`, jeden wpis `Anonymized`; dostawa nie nadpisuje klienta; skrót zerowy → `idempotency_key_reused` | I-AN-6, I-WH-1, I-BF-1, I-AN-11 |
| 5. Financial audit trail | wpis `Anonymized` zamówienia i wpisów; `Redact` zmienia tylko wartości wskazanych pól; usunięcia w audycie zostają | AR-*, AU-*, I-AN-3, I-RD-1, I-CH-1 |
| 6. Secrets stay server-side | komunikat walidacji bez wartości; brak logów z danymi; odpowiedź bez danych klienta | I-ST-3 (`forbiddenValue`), I-AN-3 (przeszukanie odpowiedzi i tabel) |
| 7. Module and layer boundaries | Orders zmienia i czyta audyt tylko przez `IAuditRedaction`/`IAuditActivity` (`Shared.Application`); reguły w `Orders.Domain` i `Reporting.Domain`; Integrations przez `IShopOrderSync` | `StoneFlow.Architecture.Tests` bez nowych wyjątków |
| 8. No identifying data in docs | dane fikcyjne w testach i dokumentach, znacznik nieidentyfikujący | przegląd R |

### Wpływ na testy architektury

`IAuditRedaction`, `IAuditActivity` w `Shared.Application` (dozwolone odwołanie każdego modułu), implementacje
`internal` w `Reporting.Infrastructure` — ten sam układ co `IAuditLog`/`AuditLog`. Orders nie odwołuje się do
projektów Reporting. Bez nowych wyjątków.

## Unified Implementation Order

1. **Kontrakty współdzielone:** S1 (`AuditOperation.Anonymized`, komentarz `IAuditLog`), S2 (porty).
2. **Reporting (TDD):** AR-* → `AuditRecord.Redact`; CHC-1 → `ChangeHistoryCodes`; `AuditScope`,
   `AuditRedactor`, `AuditActivity`, DI (S3).
3. **Orders.Domain (TDD):** CA-* → `CustomerAnonymization` (B1); naruszenia (B2); RA-* → wpisy (B3); OA-*,
   SS-1 → `Order` (B4).
4. **Orders.Application (TDD):** pakiet testowy czasu w `StoneFlow.Orders.Tests` (`dotnet restore` z
   aktualizacją lock); AU-* → `OrderAnonymizationAudit` (B5); porty, kody, `WarsawTime` (B6);
   `AnonymizationRetention` (B9); H-AN-* → `AnonymizeCustomer` (B7); H-GO-* → `GetOrder`, `EditOrder` (B8).
5. **Orders.Infrastructure:** opcje i DI (B10), konfiguracja EF, migracja `CustomerAnonymization`, store,
   reader (B11); `has-pending-model-changes` dla sześciu kontekstów.
6. **Orders.Api:** problemy, kontrakty, endpoint, mapowanie edycji (B12).
7. **Testy integracyjne:** I-ST-*, I-RD-*, I-AN-*, I-RP-1, I-WH-*, I-BF-1, I-CH-1; regresja (B13).
8. **Dokumenty i konfiguracja:** D1–D4.
9. **Frontend — fundamenty:** F1 (modele, fikstura), F2 + F-A1, F3 + F-S1, F4 + F-T1; `npm run typecheck`.
10. **Frontend — widoki:** F6 + F-F1 (formularz), F5 + F-P* (szczegóły), F7 + F-CH* (historia zmian).
11. **Weryfikacja** (sekcja „Verification”), sprawdzenie z działającym API, scenariusze ręczne UI i
    potwierdzenia właściciela do `06-implementation-done.md`; przegląd człowieka (R).

### Dokumenty (D)

- **D1 `docs/operations/anonimizacja.md`** (nowy; styl `data-uruchomienia.md`, oznaczenia **[właściciel]**):
  1. Cel i zasada (DANE-01, ręczna, nieodwracalna, pojedyncze zamówienie, raporty bez zmian).
  2. Stan domyślny: wyłączona w każdym środowisku do decyzji D-09 (fail-closed); jak to rozpoznać w UI.
  3. **Włączenie [właściciel]** — kroki w kolejności: (1) D-09 rozstrzygnięta na piśmie z księgowym lub
     prawnikiem; (2) **porównanie reguły początku okresu z D-09** — opis reguły (najpóźniejsza z dat:
     zamówienia, wpłaty/zwrotu/korekty, ostatniej zmiany zamówienia lub wpisów w Europe/Warsaw, w tym
     usunięcia wpisu); gdy D-09 liczy inaczej (np. od końca roku) — **stop**, osobne zadanie zmienia
     `CustomerAnonymization.RetentionStart`; (3) wpis potwierdzenia w rejestrze właściciela; (4) ustawienie
     `StoneFlow__Anonymization__RetentionMonths=<liczba miesięcy>` (1–1200) w panelu hostingu i restart;
     (5) sprawdzenie: zamówienie sprzed okresu pokazuje „dostępne”, nowsze — datę „dostępne od”;
     błędna wartość zatrzymuje start (log z kluczem).
  4. Zmiana i wyłączenie: zmiana wartości od restartu, bez wpływu na zamówienia zanonimizowane; usunięcie
     zmiennej wyłącza akcję, zanonimizowane zostają i synchronizacja dalej nie przywraca danych.
  5. Zakres usuwanych i zachowanych danych (Assumptions pkt 3, 4, 10), w tym historia zmian (znacznik w
     dawnych wpisach, wpis „Anonimizacja”).
  6. **Czego operacja nie obejmuje:** dane w sklepie WooCommerce (osobno w panelu sklepu — rozstrzygnięcie 5),
     wydatki, wypłaty, „Przyjął”, kopie zapasowe, logi hostingu.
  7. Kopie zapasowe: retencja 30 dni; po odtworzeniu kopii — ponowić anonimizacje z rejestru (odsyłacz do
     `kopie-i-odtworzenie.md`).
  8. Logi: brak danych klienta w logach aplikacji.
  9. Rejestr żądań [właściciel]: data żądania, numer zamówienia, data wykonania — poza aplikacją, bez danych
     klienta w repozytorium.
  10. Powrót do wersji: po pierwszym użyciu nie wraca się do wersji sprzed SF-22 (Assumptions pkt 14).
  11. Uwagi techniczne: synchronizacja nie bierze blokady zamówienia, spójność daje `rowversion`.
- **D2 `docs/operations/kopie-i-odtworzenie.md`**, sekcja 7 „Odtworzenie po awarii”: krok po odtworzeniu
  — ponowić anonimizacje wykonane po dacie kopii według rejestru (`anonimizacja.md`, sekcja 9).
  **`docs/operations/powrot-do-wersji.md`**: jedna uwaga — po pierwszej anonimizacji nie cofa się do wersji
  sprzed SF-22 (odsyłacz).
- **D3 `deploy/api.env.example`**: linia
  `# StoneFlow__Anonymization__RetentionMonths=<liczba-miesiecy-po-decyzji-D-09>  # brak = funkcja wyłączona; docs/operations/anonimizacja.md`
  — zakomentowana, bez liczby.
- **D4 `.sdd/context/conventions.md`**: w „Financial audit (SF-7)” wyjątek od „tylko dodawane”
  (`IAuditRedaction`, `AuditRecord.Redact`, wyłącznie DANE-01) i port `IAuditActivity`; w „Repository layout”
  `docs/operations/` + „anonimizacja (SF-22)”. Odwołanie do konfiguracji `StoneFlow:Anonymization` w
  akapicie o konfiguracji wdrożenia nie jest potrzebne (dokument operacyjny).

## Tests

| Test | Type | What it proves |
|---|---|---|
| CA-1 `CustomerAnonymizationTests` — sama data zamówienia | unit (Orders.Tests) | brak wpisów, ostatnia zmiana = data zamówienia → początek = data zamówienia |
| CA-2 wpis późniejszy | unit | zwrot 10.06.2022 przy zamówieniu z 2020 → początek 10.06.2022 |
| CA-3 ostatnia zmiana późniejsza | unit | wpłata z datą 2020 dopisana dziś / anulowanie dziś → początek = dziś |
| CA-4 granica dnia | unit | 15.03.2021 + 60 → `availableFrom` 15.03.2026; dziś 14.03.2026 → `RetentionPending`, 15.03.2026 → `Available` (obie daty wypełnione) |
| CA-5 `AddMonths` | unit | 31.01.2024 + 1 → 29.02.2024; 31.01.2025 + 1 → 28.02.2025 |
| CA-6 przyszła data zamówienia | unit | początek w przyszłości → `RetentionPending` |
| CA-7 kolejność stanów | unit | zanonimizowane + brak konfiguracji → `Anonymized` (daty `null`); brak konfiguracji → `Disabled` (daty `null`) |
| CA-8 kontrakt | unit | włączona bez `retentionStart` → `ArgumentException`; miesiące 0 i 1201 w `AvailableFrom` → `ArgumentOutOfRangeException`; `Available` zawsze z datami |
| OA-1 `OrderAnonymizationTests` — pola wyczyszczone | unit | nazwa = znacznik; telefon, e-mail, ulica, kod, miasto, NIP, uwagi, komentarz = `null` |
| OA-2 pola zachowane | unit | rodzaj klienta, dostawcy, ZO, faktura, flagi, data, kanał, wartość, status, źródło, numer, ID zewnętrzne, status/zwroty/usunięcie ze sklepu bez zmian |
| OA-3 lista zmian | unit | zamówienie tylko z nazwą → zmiany = `[CustomerName]`; pełne → wszystkie dziewięć pól |
| OA-4 nazwa równa znacznikowi | unit | nie ma jej w zmianach; fakt anonimizacji ustawiony |
| OA-5 skrót i fakt | unit | ręczne: skrót 32 bajty zerowe; ze sklepu: skrót `null`; `AnonymizedAtUtc`, `AnonymizedByAccountId`, `UpdatedAtUtc` = teraz |
| OA-6 ponowne wywołanie | unit | `AlreadyAnonymized`, brak zmian (snapshot i `UpdatedAtUtc` bez zmian) |
| OA-7 `Edit` z innym klientem (ręczne) | unit | `CustomerAnonymized`, zamówienie bez zmian |
| OA-8 `Edit` ze zmienionym tylko rodzajem | unit | `CustomerAnonymized` (rozstrzygnięcie 7) |
| OA-9 `Edit` z tym samym klientem | unit | zmiany statusu, uwag, komentarza, wartości i daty zapisane; brak pól klienta w zmianach |
| OA-10 `Edit` zamówienia ze sklepu z innym klientem | unit | `FieldOwnedByShop` (jak dziś) |
| SS-1 `ShopSyncTests` — po anonimizacji | unit | `Created`/`Updated`/`Restored` z pełnymi danymi klienta: klient bez zmian, data/wartość/status/zwroty zaktualizowane, przejście na „Anulowane” jak dotąd, brak pól klienta w zmianach; `Deleted` jak dotąd; dostawa różna tylko klientem → brak zmian, nowsze `ShopModifiedAtUtc` zapisane, `UpdatedAtUtc` bez zmian |
| RA-1 `EntryAnonymizationTests` — zwrot | unit | powód = znacznik; skrót zerowy; kwota, data, rodzaj, metoda bez zmian; zmiany = `[Reason]`; `UpdatedAtUtc` = teraz |
| RA-2 korekta | unit | jak RA-1 dla `SalesCorrection` |
| RA-3 powód równy znacznikowi | unit | zmiany puste, `UpdatedAtUtc` bez zmian, skrót wyzerowany |
| RA-4 poprawa po anonimizacji | unit | `Edit` z powodem-znacznikiem bez zmian → brak zmian; z nowym powodem → zmiana |
| AU-1 `OrderAnonymizationAuditTests` — selektory | unit | dokładnie 9 pól `order` + `reason` dla `refund` i `salesCorrection`; kody równe `OrderCodes.Of(...)` i `OrderAudit` (te same nazwy co w zapisanych wpisach) |
| AU-2 wpisy operacji | unit | zamówienie: obiekt, data biznesowa, etykieta = numer, operacja `Anonymized`, autor konto, cztery pola w kolejności, `old` `null`, `personal` `false`, `anonymizedFields` jako tablica JSON (także `"[]"`); wpis: obiekt `Refund`/`SalesCorrection`, rodzic zamówienie, etykieta „Zwrot do …”, `["reason"]` |
| H-AN-1 `AnonymizeCustomerHandlerTests` — wersja | unit (fałszywe porty, `FakeTimeProvider`) | niepoprawna wersja → `ValidationFailed`, żaden port i jednostka pracy niewołane |
| H-AN-2 wyłączona | unit | `RetentionMonths` `null` → `Disabled` bez wywołania jednostki pracy, blokady, store ani audytu (także gdy fałszywy store „nie ma” zamówienia) |
| H-AN-3 kolejność w transakcji | unit | brak zamówienia → `NotFound`; zła wersja → `Conflict` przed stanem; zanonimizowane → `AlreadyAnonymized`; kolejność wywołań w `CallLog`: blokada → wczytanie → fakty → audyt aktywności → zapis → redakcja → wpisy audytu |
| H-AN-4 retencja | unit | dzień przed → `RetentionNotElapsed` z `AvailableFrom`, brak zapisu; w dniu granicy → `Saved` |
| H-AN-5 konflikt zapisu | unit | `SaveAsync` → `Conflict` → wynik `Conflict`, brak redakcji i wpisów; `commitWhen` `false` |
| H-AN-6 usunięty wpis | unit | `IAuditActivity` zwraca dziś → `RetentionNotElapsed` mimo starych dat zamówienia i wpisów |
| H-AN-7 brak `Updated` | unit | żaden wpis `Updated` ani wartość z danych klienta w zapisanych `AuditEntry` (sprawdzenie wszystkich `OldValue`/`NewValue` i etykiet) |
| H-AN-8 zakres i liczba | unit | dwa zwroty i korekta (jedna z powodem-znacznikiem) → `anonymizedReasons = "2"`, dwa wpisy `EntryAnonymized`; `retentionStartDate`, `retentionMonths` z konfiguracji; redakcja z `Root = Order/{id}`, selektorami i znacznikiem |
| H-AN-9 `commitWhen` | unit | `true` tylko dla `Saved` (wszystkie inne wyniki → `false`) |
| H-GO-1 `GetOrderHandlerTests` — wyłączona | unit | `disabled`, store faktów i audyt aktywności niewołane |
| H-GO-2 zanonimizowane | unit | `anonymized` także bez konfiguracji; bez odczytów faktów |
| H-GO-3 oczekiwanie i dostępność | unit | daty z faktów; granica „dziś” w Europe/Warsaw (22:59:59Z / 23:00:00Z zimą) |
| H-GO-4 usunięcie wpisu | unit | czas z `IAuditActivity` późniejszy od dat → początek = jego data w Warszawie |
| H-GO-5 brak zamówienia | unit | `null`, bez odczytów faktów |
| AR-1 `AuditRecordTests.Redact` — tylko wskazane pola | unit (Reporting.Tests) | stare i nowe wartości wskazanych pól → znacznik; inne pola bez zmian |
| AR-2 `null` | unit | `null` zostaje `null` (`Created` bez starej, `Deleted` bez nowej) |
| AR-3 reszta wpisu | unit | autor, czas, obiekt, etykieta, data biznesowa, rodzic, operacja, nazwy pól, `IsPersonalData`, kolejność bez zmian |
| AR-4 brak zmian | unit | brak pasujących pól albo wartości już równe znacznikowi → `false`, `Changes` ta sama instancja |
| AR-5 argumenty | unit | `null` pól → `ArgumentNullException`; pusty znacznik → `ArgumentException` |
| CHC-1 `ChangeHistoryCodesTests` | unit | `anonymized` w kontrakcie; każda operacja ma kod (istniejący `Theory`) |
| I-ST-1 `AnonymizationStartupTests` — brak klucza | integration | host startuje; `IAnonymizationSettings.RetentionMonths` = `null` |
| I-ST-2 pusta i biała wartość | integration | `""`, `"   "` → start, `null` |
| I-ST-3 wartości błędne | integration | `0`, `-1`, `1201`, `abc`, `12.5` → `StartupFailure.AssertOptionsValidation(factory, "StoneFlow:Anonymization:RetentionMonths", forbiddenValue: value)` (dla `0` bez `forbiddenValue`, bo „0” występuje w „1200” w komunikacie) |
| I-ST-4 wartość poprawna | integration | `"60"` i `" 60 "` → 60; `"1"`, `"1200"` → granice |
| I-ST-5 appsettings | integration | `appsettings.json` i `appsettings.Development.json` bez `StoneFlow:Anonymization` |
| I-ST-6 pliki repozytorium | integration | skan: `appsettings*.json`, `Properties/launchSettings.json`, `app/playwright.config.ts`, `app/e2e/**`, `.github/workflows/*.yml` bez `RetentionMonths`; w `deploy/api.env.example` każda linia z `Anonymization` zaczyna się od `#` i nie zawiera cyfr po `=` |
| I-RD-1 `AuditRedactionTests` — korzeń i podrzędne | integration (SQL) | wpisy `Order/{id}` i wpłat/zwrotów/korekt (także `Deleted`) zmienione w bazie; wynik = liczba zmienionych |
| I-RD-2 inne zamówienia | integration | wpisy drugiego zamówienia i wydatku bez zmian (porównanie `Changes`) |
| I-RD-3 bez wpisów | integration | `RedactAsync` → 0; `LastRecordedAtUtcAsync` → `null` |
| I-RD-4 poza transakcją | integration | `RedactAsync` poza `IUnitOfWork` → `InvalidOperationException`; `LastRecordedAtUtcAsync` działa poza transakcją i zwraca najpóźniejszy czas (także `Deleted` wpisu) |
| I-AN-1 `CustomerAnonymizationApiTests` — wyłączona | integration | bez klucza: `GET` → `disabled`, daty `null`; `POST` → `422 anonymization_disabled` dla zamówienia z 2020 |
| I-AN-2 retencja przez API (kryterium 1) | integration | okres 60, zamówienie z 15.03.2021 (zegar cofnięty, ponowne logowanie po przesunięciu): 14.03.2026 23:59 Warszawa → `GET` `retentionPending` z datami, `POST` → `422 retention_not_elapsed`, `availableFrom = "2026-03-15"`, `detail` z „15.03.2026”; 15.03.2026 00:00 → `available`, `POST` → `200` |
| I-AN-3 pełna anonimizacja (kryterium 2 i 4) | integration | zamówienie ręczne i ze sklepu (webhook) z klientem, uwagami, komentarzem, zwrotem sprzedaży, zwrotem pieniędzy, korektą, wpłatą, edycją klienta i usuniętym zwrotem: po `POST` żadna pierwotna wartość pól z pkt 15 (nazwa, telefon, e-mail, ulica, kod, miasto, NIP, uwagi, komentarz, powody) nie występuje w `orders.Orders`, `orders.Refunds`, `orders.SalesCorrections` ani w `reporting.AuditEntries` (`LIKE` po kolumnach i `Changes`) ani w odpowiedziach `GET /api/orders/{id}`, `/api/orders?search=` i `/api/change-history`; skróty zerowe; jeden wpis `anonymized` zamówienia z autorem „Właściciel” i zakresem pkt 16 bez pierwotnych danych; wpisy `anonymized` zwrotów i korekt; dawne wpisy z `[zanonimizowano]` i nazwami pól |
| I-AN-4 przesunięcie początku | integration | wpłata z datą 2020 dopisana „dziś” → `retentionPending`, początek = dziś; anulowanie dziś → to samo |
| I-AN-5 usunięcie wpisu (rozstrzygnięcie 3) | integration | zamówienie i zwrot z 2020, usunięcie zwrotu „dziś” (zegar) → `GET` `retentionPending`, `retentionStartDate` = dziś; `POST` → `422` |
| I-AN-6 nieodwracalność i powtórzenie | integration | drugi `POST` z nową wersją → `409 order_already_anonymized`; ze starą → `409 order_conflict`; jeden wpis `anonymized` zamówienia |
| I-AN-7 operacje po anonimizacji | integration | `PUT` ręcznego z inną nazwą → `422 order_customer_anonymized`; z innym rodzajem → `422`; z niezmienionym klientem i statusem „Zakończone” → `200` z audytem `Updated` bez pól klienta; wpłata/zwrot/korekta → `201`; poprawa zwrotu ze znacznikiem bez zmiany → `200`; nowy zwrot zachowuje powód użytkownika |
| I-AN-8 kolejność kontroli (rozstrzygnięcie 7) | integration | wyłączona: nieistniejące `id` → `422 anonymization_disabled` (nie `404`); włączona: nieistniejące → `404 order_not_found`; zła wersja → `400 errors.version` (przy obu ustawieniach) |
| I-AN-9 dostęp | integration | bez sesji → `401`; bez nagłówka XSRF → `400` |
| I-AN-10 atomowość | integration | dekorator `IAuditRedaction` rzucający wyjątek (przez `configureServices`) → `500`/`503` bez zmian: klient, wpisy, skróty i audyt jak przed; to samo dla `IAuditLog.RecordAsync` operacji `Anonymized` |
| I-AN-11 idempotencja tworzenia | integration | ponowienie `POST /api/orders` z kluczem zanonimizowanego zamówienia → `409 idempotency_key_reused` z numerem, bez danych osobowych |
| I-RP-1 `AnonymizationReportsUnchangedTests` | integration | `StoneFlow:LaunchDate` sprzed danych; `/api/reports/sales`, `/api/reports/sales/components`, `/api/reports/cash-flow`, `/api/operations` za okres zamówienia i wpisów — identyczne JSON przed i po (bez `calculatedAtUtc`) |
| I-WH-1 `WooCommerceWebhookTests` — po anonimizacji | integration | webhook `order.updated` z pełnymi danymi i nowszą datą modyfikacji → klient = znacznik, wartość/status zaktualizowane, wpis `Updated` synchronizacji bez pól klienta; dostawa różna tylko klientem → brak wpisu audytu; usunięcie i przywrócenie w sklepie → klient bez zmian; także po usunięciu klucza konfiguracji (nowy host) |
| I-WH-2 dostawa w trakcie anonimizacji | integration | dostawa zapisana przed `POST` ze starą wersją → `POST` `409 order_conflict`; anonimizacja, potem dostawa → klient bez zmian (przebieg `RetryRequired` pokrywają istniejące testy wyścigu) |
| I-BF-1 `WooCommerceBackfillTests` | integration (fałszywe REST) | uzupełnianie i „Ponów” dla zanonimizowanego zamówienia → klient bez zmian, pozostałe pola zaktualizowane, błąd zamknięty jak dotąd |
| I-CH-1 `ChangeHistoryApiTests` | integration | wpis `operation = anonymized`, autor `account`, pola zakresu; filtr `refund` zwraca wpis `anonymized` zwrotu |
| Regresja | integration / arch | `OrdersApiTests`, `PaymentsApiTests`, `SalesCorrectionsApiTests`, `AuditLogTests`, `EndpointSecurityTests`, `OpenApiTests`, `MvpFinancialScenariosTests`, testy raportów i `StoneFlow.Architecture.Tests` — bez edycji |
| F-A1 `orders-api.spec.ts` | unit (Vitest) | `anonymize` → `POST /api/orders/{id}/anonymize` z `{ version }`, `id` zakodowane |
| F-S1 `orders.service.spec.ts` | unit | sukces → `saved` i komunikat „Dane klienta zanonimizowane.”; `order_conflict` → `conflict`; `retention_not_elapsed`, `anonymization_disabled`, `order_already_anonymized` → `stateChanged` z `detail`; błąd sieci → `error` `network`; `order_customer_anonymized` przy `update` → `error` (nie `stateChanged`) |
| F-T1 `order-anonymization.spec.ts` | unit | cztery teksty z datami `dd.MM.yyyy` / `dd.MM.yyyy, HH:mm`; brak daty → „Brak danych”; teksty dokładnie wg spec |
| F-P1 `order-details-page.spec.ts` — stany | component | `available`: przycisk aktywny, opis; `retentionPending` i `disabled`: przycisk wyłączony, `aria-describedby` wskazuje widoczny opis z tekstem; `anonymized`: brak przycisku, tekst z czasem, oznaczenie w nagłówku |
| F-P2 dialog | component | nagłówek z numerem, dwa akapity treści (nieodwracalność, raporty), `confirmLabel`; anulowanie → brak żądania |
| F-P3 zapis | component | potwierdzenie → `busy` „Anonimizowanie…”, drugi klik bez żądania; sukces → ponowny odczyt, komunikat w `role="status"`, oznaczenie; `stateChanged` → ponowny odczyt i `detail` w alercie; `conflict` → alert z „Wczytaj aktualne dane”; sieć → „Nie udało się zanonimizować danych. Spróbuj ponownie.” |
| F-P4 blokady | component | aktywny formularz wpisu (`entryActive`) i trwająca akcja anulowania → przycisk wyłączony |
| F-P5 dane po anonimizacji | component | pola klienta: `[zanonimizowano]` i „Brak danych”; istniejące testy bez zmian (fikstura `disabled`) |
| F-P6 przekazanie do formularza | component | po „Edytuj” zanonimizowanego `app-order-form` dostaje `customerAnonymized = true` |
| F-F1 `order-form.spec.ts` | component | `customerAnonymized`: pola klienta (także rodzaj) tylko do odczytu z opisem; zapis wysyła klienta równego `initial`; `422 order_customer_anonymized` → `detail` w podsumowaniu; zamówienie ze sklepu bez zmian zachowania |
| F-CH1 `audit-field-catalog.spec.ts` | unit | `anonymizedFields` → „Imię i nazwisko lub nazwa firmy, E-mail, Uwagi do realizacji”; dla `refund` → „Powód”; `[]` → „Brak danych”; zły JSON i nieznany kod bez wyjątku; etykiety i formaty czterech nowych pól |
| F-CH2 `change-history-page.spec.ts` | component | operacja „Anonimizacja” z tonem `warning`; kolumna „Przed” niewidoczna dla `anonymized` (jak `created`); `[zanonimizowano]` jako tekst w dawnym wpisie |

E2E: bez nowych testów (Assumptions pkt 13). Istniejące E2E działają z funkcją wyłączoną (brak klucza w
konfiguracji E2E). Scenariusze ręczne UI w `06-implementation-done.md`.

## Verification

Środowisko (fakty dla wykonawcy):

- Windows; API uruchomione w Visual Studio może blokować `bin/Debug` — **build i testy w Release**.
  Nie zabijać procesu Visual Studio ani uruchomionego API.
- Testy integracyjne na LocalDB: `STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True'`.
- `dotnet ef` zawsze z `--configuration Release`.
- Mało pamięci: uruchamiać projekty testowe po kolei (`--project`), nie całe rozwiązanie naraz, gdy build
  albo testy padają z braku pamięci.

Backend (z katalogu głównego, Git Bash):

```
dotnet restore stone-flow.slnx --force-evaluate     # tylko po dodaniu pakietu do Orders.Tests (aktualizacja packages.lock.json)
dotnet restore stone-flow.slnx --locked-mode
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet build stone-flow.slnx -c Release --no-restore
dotnet test --project api/tests/StoneFlow.Orders.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release --no-build
STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True' \
  dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build
# pozostałe projekty (Access, Expenses, Integrations, Shared) — regresja, po kolei
```

Migracja i model (sześć kontekstów z `deploy/scripts/contexts.sh`):

```
dotnet ef migrations add CustomerAnonymization -p api/src/Modules/Orders/StoneFlow.Orders.Infrastructure \
  -s api/src/StoneFlow.Api -c OrdersDbContext -o Persistence/Migrations --configuration Release
dotnet build stone-flow.slnx -c Release --no-restore
source deploy/scripts/contexts.sh
for entry in "${STONEFLOW_CONTEXTS[@]}"; do
  dotnet ef migrations has-pending-model-changes --no-build --configuration Release \
    --project "${entry%%|*}" --startup-project "$STONEFLOW_STARTUP_PROJECT" --context "${entry##*|}"
done
STONEFLOW_TEST_SQLSERVER='…' SQLCMD='sqlcmd -S (localdb)\MSSQLLocalDB -E' deploy/scripts/test-migrate.sh
```

Każde polecenie pętli kończy się kodem 0; `Up`/`Down` migracji przejrzane ręcznie (dwie kolumny `NULL`,
check, bez zmian danych).

Frontend (z `app/`):

```
npm ci
npm run format:check
npm run lint
npm run typecheck
npm run build
npm test -- --watch=false
npm run test:scripts
node scripts/check-bundle-secrets.mjs dist
```

Opcjonalnie E2E (funkcja wyłączona; potwierdza, że szczegóły zamówienia przechodzą axe): wg
`conventions.md` „End-to-end tests”, `npx playwright test e2e/orders.e2e.ts e2e/a11y.e2e.ts`.

Sprawdzenie ręczne z działającym API (wynik do `06-implementation-done.md`, scenariusze w tabeli testów
ręcznych UI):

- API bez zmiennej → szczegóły: „Funkcja wyłączona do decyzji o okresie retencji.”, przycisk wyłączony.
- API z `StoneFlow__Anonymization__RetentionMonths=1` (zmienna środowiskowa procesu API, nie plik
  konfiguracji). Przygotowanie zamówienia kwalifikującego się skryptem SQL na lokalnej bazie:
  `UPDATE orders.Orders SET OrderDate = '2020-01-15', UpdatedAtUtc = '2020-01-15T10:00:00' WHERE Number = '<numer>'`;
  to samo dla `Date` i `UpdatedAtUtc` w `orders.Payments`, `orders.Refunds`, `orders.SalesCorrections`
  zamówienia **oraz** `OccurredAtUtc` w `reporting.AuditEntries` z `ObjectId` zamówienia albo
  `ParentObjectId` zamówienia (rozstrzygnięcie 3 — inaczej audyt utrzyma dzisiejszy początek okresu).
- Scenariusze: wyłączona (klawiatura, opis); przed upływem (daty); po upływie (dialog: treść, Escape,
  powrót fokusu, anulowanie bez zmian, potwierdzenie, oznaczenie, pola „[zanonimizowano]”/„Brak danych”,
  uwagi i komentarz puste, powody zastąpione, kwoty i stan płatności bez zmian); edycja po anonimizacji
  (klient tylko do odczytu, zmiana statusu zapisana); wpłata i zwrot po anonimizacji; „Historia zmian”
  (wpis „Anonimizacja” z etykietami pól, filtr „Zwrot” z wpisem zwrotu, dawne wpisy z „[zanonimizowano]”);
  Pulpit (sprzedaż, przepływy) i „Operacje” za okres zamówienia przed i po; lista zamówień (klient
  „[zanonimizowano]”, wyszukiwanie po dawnej nazwie bez wyniku); telefon 360 px (blok i dialog bez
  poziomego przewijania).

**R — przegląd człowieka przed merge (ryzyko `high`):** lista pól w `Order.AnonymizeCustomer`,
`OrderAnonymizationAudit.Fields` i `AnonymizeReason` (pkt 13–15) wobec `OrderAudit.IsPersonalData` i pól
tekstowych zamówienia; `CustomerAnonymization.RetentionStart` i `AnonymizationRetention` (rozstrzygnięcie
3); zachowanie bez konfiguracji (I-ST-*, I-AN-1, I-AN-8); `AuditRecord.Redact` i zakres `AuditScope`;
`docs/operations/anonimizacja.md` (krok porównania z D-09). Potwierdzenia właściciela do zapisania w `06`:
decyzje wykonawcy ze spec (Assumptions) i rozstrzygnięcia 1–7 tego planu.

## Risks

- **D-09 i reguła początku okresu.** Kod nie rozstrzyga długości okresu, ale zawiera regułę początku
  (rozszerzoną o audyt usunięć — rozstrzygnięcie 3). Włączenie bez porównania z D-09 może otworzyć akcję za
  wcześnie, a operacji nie da się cofnąć. Zabezpieczenie: domyślnie wyłączona, krok 2 procedury włączenia,
  jedno miejsce reguły. Potwierdzenie właściciela wymagane przed włączeniem, nie przed merge.
- **Odstępstwa od spec do potwierdzenia przez właściciela:** opcja wiązana jako `string?` (zamiast `int?`);
  pusta wartość = wyłączona; ostatnia zmiana obejmuje audyt (zmienia Assumptions pkt 2 i Behavior pkt 6);
  własne wpisy `Anonymized` zwrotów i korekt; fakty wpisów z portu zamiast pól `OrderDetailsDto`;
  `anonymizedFields` jako `"[]"` dla pustej listy; aktualizacja `powrot-do-wersji.md` (spec wymienia tylko
  dwa dokumenty).
- **Zmiana zapisanych wpisów audytu** to jedyny wyjątek od niezmienności. Błąd zakresu (`AuditScope`) mógłby
  zmienić wpisy innych obiektów — test I-RD-2 i przegląd R.
- **Dane w sklepie WooCommerce** pozostają; operacja nie spełnia samodzielnie żądania usunięcia danych —
  opisane w D1 (rozstrzygnięcie 5).
- **Kopie zapasowe i powrót do wersji** przywracają dane albo nadpisywanie klienta — procedury D1/D2.
- **Testy z przesuwanym zegarem** (sesja, limity) — opisane w „Risks (backend)”; wariant SQL jako
  zastępczy.
- **Rozmiar zmiany.** Zmiana obejmuje ok. 30 plików backendu, 20 frontendu i 5 dokumentów. Możliwy podział
  na części (core „Tasks split into parts”): **a** — backend, migracja, dokumenty (funkcja wyłączona, UI bez
  zmian; nowe pole w `GET` ignorowane przez stary frontend); **b** — frontend (po merge a). Plan zakłada
  jedną część, bo podziału nie zatwierdzono; decyzja należy do właściciela.
