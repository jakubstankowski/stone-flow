# Implementation done — SF-13

## Summary

Dostarczono dzienne uzupełnianie zamówień WooCommerce w module Integrations:
- **klient REST v3** z Basic auth w nagłówku i klasyfikacją odpowiedzi;
- **zadanie harmonogramu** `woocommerce-backfill` (23:00 Europe/Warsaw):
  - okno od kursora − 10 min do startu − 10 min; kursor = `to`;
  - zestaw `any` z oknem i zestaw `trash` bez filtrów dat;
  - kontrola `X-WP-Total`;
- **historia błędów zamówień** (`integrations.WooCommerceOrderErrors`): upsert, jeden otwarty błąd na zamówienie, zamykanie z warunkiem wersji, zapis z webhooka, uzupełniania i „Ponów”;
- **akcje:** „Synchronizuj teraz” w tle (jedno oczekujące żądanie, `409` przy trwającym przebiegu) i „Ponów” (404 z kodem WooCommerce → usunięcie; inne → `502`; błąd ERP → `503`);
- **odczyty** stanu i listy błędów;
- **kontrakt** `IShopOrderLookup` (Orders);
- **migracja** `WooCommerceBackfill`;
- **dokumentacja:** PRD (znane ograniczenie), `docs/wdrozenia.md`, `api.env.example`, `conventions.md`.

Po przeglądzie kodu (`05-code-review.md`, PASS WITH ISSUES) poprawiono:
- W1: nieudany zapis historii błędu kończy uzupełnianie niepowodzeniem, a kursor się nie przesuwa;
- C2: kursor przesuwa się tylko dla przebiegu `running`;
- C3: jawne sprawdzenie anulowania przed końcowymi zapisami;
- S1: podwójne logowanie;
- sprzeczność w `conventions.md` i pusty plik.

Dodano testy T1, T3 i T4. Ponownego przeglądu kodu nie wykonano.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Uzupełnianie o 23:00 od ostatniego sukcesu; pierwsze od daty uruchomienia | Implemented | kursor = startedAt − 10 min (plan [R1]) |
| Sukces = wszystkie strony; niepowodzenie bez przesunięcia kursora | Implemented | |
| Zestaw `trash` | Implemented | bez filtrów dat (plan [R3]) |
| Stabilność paginacji (`X-WP-Total`) | Implemented | |
| Brak zapisów po anulowaniu; porzucone przebiegi → `failed` | Implemented | |
| Historia błędów: zapis, upsert, zamknięcie z warunkiem wersji | Implemented | |
| Błędy webhooków w historii, kody odpowiedzi bez zmian | Implemented | |
| „Synchronizuj teraz” (`202`/`409`) | Implemented | flaga jednego oczekującego |
| „Ponów” (`200`/`404`/`409`/`502`/`503`), 404 z kodem → usunięcie | Implemented | |
| Stan i lista błędów (API) | Implemented | |
| Klucze tylko w nagłówku; komunikaty kontrolowane | Implemented | |
| Znane ograniczenie trwałego usunięcia w PRD i dokumentacji | Implemented | |

## Files

Utworzone i zmienione pliki — patrz commit `feat(integrations)` SF-13. Główne:
- `Shared.Application/ShopOrders/ShopOrderLookup.cs`, `Orders.Infrastructure/Persistence/ShopOrderLookup.cs`;
- `Integrations.Domain/WooCommerce/{OrderErrorRules,BackfillWindow}.cs`;
- `Integrations.Application/WooCommerce/{Abstractions,OrderOutcome}.cs`, `Backfill/RunBackfill.cs`, `Errors/{RetryOrderError,ListOrderErrors}.cs`, `Status/GetSyncStatus.cs`, `Sync/RequestSync.cs`; zmienione `ReceiveWebhook.cs`, `WooCommerceOrderPayload.cs`;
- `Integrations.Infrastructure/WooCommerce/{Rest/WooCommerceRestClient,BackfillJob,ManualSyncService,WooCommerceSyncEvents}.cs`, `Persistence/WooCommerceStores.cs`, migracja `20261106132510_WooCommerceBackfill`; zmienione `WooCommerceWebhook.cs`, `IntegrationsDbContext.cs`, `DependencyInjection.cs`;
- `Integrations.Api/{WooCommerceEndpoints,IntegrationsProblems}.cs`, `IntegrationsModule.cs`;
- testy w `StoneFlow.Integrations.Tests/WooCommerce/*`, `StoneFlow.Api.IntegrationTests/Integrations/WooCommerceBackfillTests.cs`; zmienione `StoneFlowApiFactory.cs`, `Scheduling/SchedulerHost.cs`, `Scheduling/JobsEndpointTests.cs`;
- `docs/PRD-stone-flow.md`, `docs/wdrozenia.md`, `deploy/api.env.example`, `.sdd/context/conventions.md`.

## Planned Units

Wszystkie jednostki z sekcji 3 planu: Exists. `IRequestContext` z planu nie istnieje w repo; zamiast niego jest `Activity.Current.TraceId`.

## Database

`IntegrationsDbContext`, migracja `20261106132510_WooCommerceBackfill` (addytywna):
- tabele `WooCommerceSyncState`, `WooCommerceSyncRuns`, `WooCommerceOrderErrors`;
- unikalny indeks filtrowany (`ExternalId` WHERE `Status = 'open'`);
- indeksy malejące.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| `BackfillWindowTests`, `OrderErrorRulesTests` (w `WooCommerceRulesTests`) | Exists | Passed |
| `RunBackfillTests` | Exists | Passed |
| `WooCommerceRestClientTests` | Exists | Passed (Integrations.Tests razem 59/59) |
| `WooCommerceBackfillTests` (SQL + fałszywy sklep) | Exists | Passed 16/16 |
| Regresja `WooCommerceWebhookTests`, `EndpointSecurityTests`, `SchedulerTests`, `JobsEndpointTests` | Exists | Passed 17/17, 9/9, 15/15, 3/3 (przed rundą poprawek; webhook ponownie 17/17 po niej) |

## Verification

| Command | Outcome |
|---|---|
| `dotnet build stone-flow.slnx -m:1` | Passed (0 ostrzeżeń, 0 błędów) |
| `dotnet format stone-flow.slnx --verify-no-changes` | Passed |
| `dotnet test` Integrations.Tests / Architecture.Tests | Passed (59 / 56) |
| Integracyjne filtrowane (LocalDB) | Passed (jak wyżej) |
| Pełny `dotnet test`, `test-migrate.sh`, build Release | Not run (CI) |

## Deviations

- Cały `IntegrationsDbContext` ma własne połączenie (plan: tylko historia błędów).
- `StoneFlowApiFactory` domyślnie `Scheduling:Enabled=false`; testy SF-11 usuwają definicje zadań modułów.
- Logowanie `HttpClient` wyłączone (`RemoveAllLoggers`) zamiast filtra poziomu.
- Korelacja „Ponów”: `Activity.Current.TraceId` zamiast `IRequestContext`.
- Zamknięcie błędu: odczyt `UPDLOCK, HOLDLOCK` + reguła w domenie zamiast warunkowego SQL.
- Id przebiegu Integrations = `JobRunContext.RunId`.
- `WooCommerceEndpoints.cs` poza katalogiem `Endpoints/`.

## Additional Implementation

Additional implementation not covered by the original documents:
- bezpiecznik 1000 stron na zestaw;
- limit odpowiedzi 32 MB;
- liczniki przebiegu po unikalnych ID;
- dopisek „(kosz)” w komunikatach zestawu `trash`.

## Open Items

- **Human review required before merge** (ryzyko high).
- Pełny zestaw testów i `test-migrate.sh` — CI.
- Ponowny przegląd kodu po poprawkach nie został wykonany.
- Nierozwiązane z `05-code-review.md`:
  - C4: nieaktualny otwarty błąd przy wyścigu sukcesu i porażki tej samej wersji;
  - C5: sklep ignorujący `dates_are_gmt` — dopisać krok weryfikacji;
  - S2: `lastSuccessAtUtc` w `/status` = kursor (start − 10 min) — SF-14 pokazuje to jako czas, do którego dane są kompletne;
  - S3: `OrdersProcessed` liczy cały kosz;
  - S4: „Przerwany” także dla limitu czasu;
  - S5: mylący komunikat przy odpowiedzi > 32 MB;
  - T2: brak testu `abandoned` w `/status`;
  - T5: brak testu źródła `schedule` / rejestracji 23:00;
  - T6: brak testu anulowania na SQL.
- Parametry REST niepotwierdzone na sklepie — weryfikacja tylko do odczytu na produkcji (`docs/wdrozenia.md` sekcja 12); dowód D-08 pkt 2 przy pierwszym wdrożeniu.
- Znane ograniczenie: trwałe usunięcie bez webhooka niewykrywalne (decyzja właściciela).
