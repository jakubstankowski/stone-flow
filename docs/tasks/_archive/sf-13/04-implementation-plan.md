# Implementation Plan — SF-13

Tryb: plan jednowarstwowy (backend; UI to SF-14). Bez podziału na części.

## 1. Scope

Plan dostarcza moduł uzupełniania WooCommerce w Integrations:
- klient REST v3 (Basic auth w nagłówku);
- zadanie harmonogramu `woocommerce-backfill`, kursor sukcesu i przebiegi;
- historia błędów zamówień z zapisem z webhooka, uzupełniania i „Ponów” oraz zamykaniem z warunkiem wersji;
- „Synchronizuj teraz” w tle, „Ponów”;
- odczyt stanu i listy błędów;
- kontrakt `IShopOrderLookup` (Orders);
- migrację schematu `integrations`, testy z fałszywym sklepem i dokumentację (PRD, `wdrozenia.md`).

Domyka uwagi `03-spec-review.md` (v2) — [R1]–[R7] i niejasności [U1]–[U6].

## 2. Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Shared.Application | `ShopOrders/` | `IShopOrderLookup` (Orders → Integrations) |
| Orders.Application/Infrastructure | `ShopSync/ShopOrderLookup` | numer i Id zamówienia po `ExternalId` |
| Integrations.Domain | `WooCommerce/` | reguły zamknięcia błędu, kursora, stabilności stron |
| Integrations.Application | `WooCommerce/Backfill/*`, `Errors/*`, `Status/*`, zmiana `ReceiveWebhook` | przypadki użycia |
| Integrations.Infrastructure | `WooCommerce/Rest/*`, `Persistence/*`, migracja, `BackgroundService` ręcznego uruchomienia, rejestracja zadania | I/O |
| Integrations.Api | `WooCommerceEndpoints.cs` | 4 endpointy |
| Docs | PRD sekcja 5/ZAM-02, `docs/wdrozenia.md` sekcje 11–12, `api.env.example` | ograniczenie, klucze REST [R4] |

## 3. Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Shared/StoneFlow.Shared.Application/ShopOrders/ShopOrderLookup.cs` | create | `IShopOrderLookup`, `ShopOrderRef(Guid Id, string Number)` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/ShopOrderLookup.cs` | create | implementacja (zapytanie bez śledzenia) + rejestracja DI |
| `api/src/Modules/Integrations/StoneFlow.Integrations.Domain/WooCommerce/OrderErrorRules.cs` | create | `CanClose(failedModifiedAt, successModifiedAt, isDeletion)`; kody statusów, źródeł |
| `api/src/Modules/Integrations/StoneFlow.Integrations.Domain/WooCommerce/BackfillWindow.cs` | create | wyliczenie `from`/`to` z kursora i daty uruchomienia |
| `.../Integrations.Application/WooCommerce/WooCommerceOrderPayload.cs` | modify | `PayloadReadResult.ExternalId`, `ModifiedAtUtc` także dla nieczytelnej treści |
| `.../Integrations.Application/WooCommerce/ReceiveWebhook.cs` | modify | zapis/zamknięcie błędu przez `IWooCommerceOrderErrors` |
| `.../Integrations.Application/WooCommerce/Abstractions.cs` | create | `IWooCommerceRestClient`, `RestPage`, `RestOrderResult`, `IWooCommerceOrderErrors`, `IWooCommerceSyncStore`, `IWooCommerceRestSettings`, `IManualSyncQueue` |
| `.../Integrations.Application/WooCommerce/Backfill/RunBackfill.cs` | create | przebieg uzupełniania (wspólny dla harmonogramu i „teraz”) |
| `.../Integrations.Application/WooCommerce/OrderOutcome.cs` | create | wspólne zastosowanie jednego zamówienia + błąd/zamknięcie (webhook, uzupełnianie, ponowienie) |
| `.../Integrations.Application/WooCommerce/Errors/RetryOrderError.cs` | create | komenda „Ponów” |
| `.../Integrations.Application/WooCommerce/Errors/ListOrderErrors.cs` | create | zapytanie listy błędów |
| `.../Integrations.Application/WooCommerce/Status/GetSyncStatus.cs` | create | zapytanie stanu |
| `.../Integrations.Application/WooCommerce/Sync/RequestSync.cs` | create | komenda „Synchronizuj teraz” |
| `.../Integrations.Infrastructure/WooCommerce/Rest/WooCommerceRestClient.cs` | create | HttpClient, Basic auth, paginacja, klasyfikacja 404 |
| `.../Integrations.Infrastructure/WooCommerce/WooCommerceWebhook.cs` | modify | opcje REST (`BaseUrl`, `ConsumerKey`, `ConsumerSecret`, `RequestTimeout`) + walidacja |
| `.../Integrations.Infrastructure/WooCommerce/BackfillJob.cs` | create | `IScheduledJob` `woocommerce-backfill` |
| `.../Integrations.Infrastructure/WooCommerce/ManualSyncService.cs` | create | `BackgroundService` + flaga jednego oczekującego |
| `.../Integrations.Infrastructure/Persistence/IntegrationsDbContext.cs` | modify | encje `SyncState`, `SyncRun`, `OrderError` |
| `.../Integrations.Infrastructure/Persistence/WooCommerceStores.cs` | create | `IWooCommerceSyncStore`, `IWooCommerceOrderErrors` (upsert) |
| `.../Integrations.Infrastructure/Persistence/Migrations/*_WooCommerceBackfill*` | create | migracja |
| `.../Integrations.Infrastructure/DependencyInjection.cs` | modify | rejestracje, `AddHttpClient`, `AddScheduledJob` |
| `.../Integrations.Api/WooCommerceEndpoints.cs`, `IntegrationsProblems.cs`, `IntegrationsModule.cs` | create/modify | endpointy i problemy |
| `api/tests/StoneFlow.Integrations.Tests/WooCommerce/*` | create | reguły domeny, okno, przebieg z fałszywym klientem |
| `api/tests/StoneFlow.Api.IntegrationTests/Integrations/WooCommerceBackfillTests.cs` | create | SQL + fałszywy `HttpMessageHandler` |
| `docs/PRD-stone-flow.md`, `docs/wdrozenia.md`, `deploy/api.env.example` | modify | [R4] |

## 4. Implementation steps

1. **Okno uzupełniania [R1].** `BackfillWindow.For(cursorUtc?, launchDate, startedAtUtc)`:
   - `to = startedAt − 10 min` (górna granica z buforem: zamówienie zmienione w trakcie przebiegu ma datę po `to`, także przy zegarze sklepu spóźnionym do 10 min, więc nie przesuwa stron);
   - `from = cursor − 10 min`, a bez kursora — początek dnia uruchomienia w Europe/Warsaw (UTC);
   - po sukcesie kursor = `to` (nie `startedAt`), co uściśla spec pkt 2.5 i pkt 5;
   - gdy `from >= to` (np. przebieg tuż po poprzednim), przebieg kończy się sukcesem bez pobierania i kursor zostaje.
2. **Zestaw `trash` [R3].** Pobierany **bez** `modified_after`/`modified_before` (`status=trash`, wszystkie strony), bo nie wiadomo, czy przeniesienie do kosza zmienia `date_modified_gmt`. Wolumen kosza jest mały, a zastosowanie idempotentne: SF-12 zwraca `Unchanged`/`SkippedOlder` dla znanych. Kontrola `X-WP-Total` dotyczy obu zestawów.
3. **Klient REST [R7].**
   - `HttpClient` przez `AddHttpClient`, `BaseAddress` = `BaseUrl` (https), `Timeout` = `RequestTimeout`;
   - nagłówek `Authorization: Basic base64(key:secret)`; klucze **nigdy** w query string;
   - `401`/`403` → wynik `Unauthorized`, co oznacza nieudany przebieg z komunikatem „Sklep odrzucił klucze REST (HTTP 401)”; dokumentacja dodaje, że hosting obcinający nagłówek `Authorization` daje ten sam objaw;
   - logowanie `HttpClient` ograniczone do `Warning` (bez adresów z parametrami — i tak bez kluczy);
   - `GetOrderAsync(id)`: 200 → bajty; 404 + JSON `code` ∈ {`woocommerce_rest_shop_order_invalid_id`, `woocommerce_rest_invalid_id`} → `NotFound`; inny 404/kod/timeout → `Failed(kontrolowany komunikat)`.
4. **Przebieg (`RunBackfill`)**, wywoływany przez `BackfillJob.RunAsync` i usługę ręczną z tokenem:
   1. brak konfiguracji REST → log, koniec bez wiersza;
   2. w transakcji: oznacz wszystkie `running` jako `failed` „Przerwany (restart lub awaria)”, wstaw nowy `running` (`StartedAtUtc` = `startedAt` [U1]);
   3. okno; zestaw `any` z oknem, potem zestaw `trash`;
   4. każda pozycja → `OrderOutcome.ApplyAsync(source: backfill, context: runId)`;
   5. sukces: w transakcji kursor = `to` i przebieg `succeeded` z liczbami;
   6. niepowodzenie: przebieg `failed` z komunikatem kontrolowanym i wyjątek `WooCommerceBackfillException(komunikat)` dla SF-11;
   7. anulowanie: `OperationCanceledException` przepuszczony **bez** zapisu.
5. **`OrderOutcome`** (wspólne dla trzech źródeł):
   - `Read` → brak `ExternalId` → licznik `Unidentified`/log;
   - nieczytelne z `ExternalId` → `RecordAsync`;
   - `IShopOrderSyncRunner.ApplyAsync` → wynik sukcesu → `CloseAsync(externalId, modifiedAt, isDeletion, closedBy)` z warunkiem `OrderErrorRules.CanClose`;
   - `RetryRequired` lub wyjątek → `RecordAsync` z „Błąd zapisu zamówienia ({typ})”.

   Zapis błędu, który sam się nie uda, idzie tylko do logu i nie zmienia wyniku dostawy.
6. **Historia błędów (`WooCommerceOrderErrors` store).**
   - `RecordAsync`: transakcja `IntegrationsDbContext` (własne połączenie `UseModuleSqlServer(connectionString, schema)`, nie `ModuleConnection`, jak w SF-11). Odczyt otwartego błędu `WITH (UPDLOCK, HOLDLOCK)` po `ExternalId`, potem update albo insert. Unikalność → jedno ponowienie jako update.
   - `CloseAsync`: warunkowe `ExecuteUpdate` otwartego błędu z warunkiem wersji (SQL: `FailedModifiedAtUtc IS NULL OR @modified >= FailedModifiedAtUtc OR @isDeletion = 1`).
   - Kolumny:
     - `Id uniqueidentifier`, `ExternalId nvarchar(64)`;
     - `Status nvarchar(16)`, `Source nvarchar(16)`, `ClosedBy nvarchar(16) null`;
     - `FirstOccurredAtUtc`, `LastOccurredAtUtc`, `ClosedAtUtc null`, `FailedModifiedAtUtc null` (`datetime2`);
     - `Attempts int`, `Message nvarchar(1000)`, `Context nvarchar(200)` [U6].
7. **Webhook (SF-12)**: `ReceiveWebhookHandler` używa `OrderOutcome` (źródło `webhook`, kontekst `topic + delivery id`). Kody odpowiedzi bez zmian.
8. **„Synchronizuj teraz” [R2].**
   - `ManualSyncService`: flaga `Interlocked` 0/1 (oczekuje lub trwa) i `SemaphoreSlim` sygnału.
   - `RequestSyncHandler`:
     - brak konfiguracji → `NotConfigured`;
     - dzierżawa ważna (`IJobStatusReader`) → `InProgress`;
     - `TrySetPending` (CAS 0→1) nieudane → `InProgress`;
     - inaczej sygnał → `Accepted`.
   - Usługa: `IJobRunner.TryRunNowAsync("woocommerce-backfill", stoppingToken)`, potem flaga = 0 (także przy `AlreadyRunning` i wyjątku).
   - Stan `isRunning` = dzierżawa ważna **albo** flaga = 1 [U3]. Deterministyczne: drugie żądanie do zwolnienia flagi → `409`.
9. **„Ponów”.**
   - `RetryOrderErrorHandler`:
     - błąd nie istnieje → `NotFound`; zamknięty → `Closed`; brak konfiguracji → `NotConfigured`;
     - `GetOrderAsync`:
       - `Found` → `OrderOutcome` (źródło `retry`, kontekst = identyfikator korelacji żądania z `IRequestContext` albo nowy Guid [R6]);
       - `NotFound` → `ShopOrderSnapshot.Deletion` → `OrderOutcome` (`isDeletion`);
       - `Failed` → `RecordAsync` → `ShopFailed`;
     - błąd bazy → `Unavailable`.
   - Wynik zawiera błąd po zmianie. Jeśli warunek wersji nie pozwolił zamknąć, zwraca `200` ze statusem `open` [U5].
10. **Odczyty.**
    - `GetSyncStatus`: `configured`, `lastSuccessAtUtc` (kursor), `lastRun` (z `running` bez dzierżawy → `abandoned` [U2]), `isRunning`, `openErrors`.
    - `ListOrderErrors`: walidacja `status` ∈ {open, closed, all}, `page` ≥ 1, `pageSize` 1–100 (inaczej `400` z polami) [U4]; `IShopOrderLookup` dla numerów.
11. **Endpointy** (`/api/integrations/woocommerce`, sesja; POST z CSRF globalnie) i `IntegrationsProblems` (kody `integration_not_configured`, `sync_in_progress`, `error_not_found`, `error_closed`, `retry_failed`, `retry_unavailable`).
12. **Rejestracja**: `AddScheduledJob<BackfillJob>(new JobDefinition("woocommerce-backfill", DailyAt(23:00), 30 min))` — zawsze (spec); `AddHostedService<ManualSyncService>`.
13. **Migracja** `WooCommerceBackfill` (`IntegrationsDbContext`); wiersz `SyncState` tworzony leniwie.
14. **Dokumentacja [R4][R5].**
    - `docs/wdrozenia.md` sekcja 11:
      - klucz REST „Read”, `StoneFlow:WooCommerce:BaseUrl/ConsumerKey/ConsumerSecret`;
      - ograniczenie trwałego usunięcia;
      - objaw obcinanego nagłówka `Authorization`.
    - Sekcja 12: weryfikacja na produkcji tylko do odczytu (klucz „Read”, pierwsze uzupełnienie w godzinach małego ruchu; ERP zapisuje tylko zamówienia), gdy kopii testowej nie ma.
    - PRD sekcja 5 i ZAM-02: zdanie o znanym ograniczeniu (decyzja właściciela 2026-11-06).
    - `api.env.example`: 4 klucze.

## 5. Data & contracts

- Storage: `integrations.WooCommerceSyncState`, `WooCommerceSyncRuns`, `WooCommerceOrderErrors` (jak spec + typy z kroku 6).
- API:
  - `POST /sync` → `202` | `409` `{code}`;
  - `POST /errors/{id}/retry` → `200 OrderErrorDto` | `404` | `409` | `502` | `503`;
  - `GET /status` → `SyncStatusDto`;
  - `GET /errors?status&page&pageSize` → `{ items: OrderErrorDto[], page, pageSize, total }`;
  - `OrderErrorDto`: `id`, `externalId`, `orderId?`, `orderNumber?`, `status`, `source`, `firstOccurredAtUtc`, `lastOccurredAtUtc`, `attempts`, `message`, `context`, `closedAtUtc?`, `closedBy?`.
- Typy: `IShopOrderLookup`, porty z kroku 3/5/6.

## 6. Edge cases & null safety

Każdy przypadek ze spec → kroki 1–9; puste `ModifiedAtUtc` → zamknięcie tylko gdy błąd bez daty albo usunięcie.

## 7. Invariants

| Invariant | Where | Test |
|---|---|---|
| 3 | endpointy z sesją, CSRF | `Endpoints_require_session_and_csrf` |
| 4 | `IShopOrderSync`, dzierżawa, flaga ręczna, upsert błędu | `Backfill_twice_no_duplicates`, `Sync_now_twice_one_run`, `Concurrent_failures_one_open_error` |
| 6 | Basic auth w nagłówku, komunikaty kontrolowane | `Rest_client_never_puts_keys_in_url`, `Error_messages_have_no_customer_data` |
| 7 | `IShopOrderLookup` w Shared | testy architektury |

## 8. Tests

| Test | Type | What it proves |
|---|---|---|
| `BackfillWindowTests`, `OrderErrorRulesTests` | unit | okno, bufor, zamykanie z wersją |
| `RunBackfillTests` (fałszywy klient REST, pamięciowe porty) — paginacja, zmiana `X-WP-Total` (powtórka, druga zmiana → fail), błąd strony → kursor bez zmian, pozycja nieczytelna → błąd zamówienia, sukces → kursor = `to`, anulowanie bez zapisu | unit | przebieg |
| `WooCommerceRestClientTests` (fałszywy handler) — nagłówek Basic, brak kluczy w URL, 404 z kodem vs bez kodu, 401 | unit | klient |
| `WooCommerceBackfillTests` (SQL + fałszywy sklep): pominięty webhook uzupełniony; przerwa → od kursora; pierwszy od uruchomienia; zamówienie w koszu → anulowane; sklep 503 → `failed`, potem odzysk; błąd zamówienia + „Ponów” zamyka; „Ponów” 404 z kodem → usunięcie; 404 bez kodu → `502`; webhook zamyka błąd; starszy webhook nie zamyka; „Synchronizuj teraz” `202`/`409`; `401` bez sesji | integration | AC |

## 9. Verification

- `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` Integrations.Tests, Architecture.Tests, filtrowane integracyjne `*WooCommerceBackfill*`, `*WooCommerceWebhook*` (LocalDB).
- CI: pełny zestaw i `test-migrate.sh`.
- **Przegląd przez człowieka przed merge (ryzyko high).**

## 10. Risks

- Parametry REST (`status=trash`, `dates_are_gmt`, `orderby=modified`, kody 404) niepotwierdzone na sklepie (SF-2) — weryfikacja tylko do odczytu na produkcji.
- Bufor 10 min opóźnia uzupełnienie o 10 min — bez znaczenia dla przebiegu nocnego.
- Trwałe usunięcie bez webhooka — znane ograniczenie (decyzja właściciela).
