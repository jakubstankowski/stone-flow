# Implementation done — SF-22

> Manifest dostawy przed przeglądem kodu: `05-code-review.md` jeszcze nie istnieje (przegląd
> następuje później). Ryzyko `high` — wymagany przegląd człowieka przed merge.

## Summary

- Ręczna, nieodwracalna akcja „Anonimizuj dane klienta” w szczegółach zamówienia:
  `POST /api/orders/{id}/anonymize` i pole `anonymization` w `GET /api/orders/{id}`.
- Okres retencji z konfiguracji `StoneFlow:Anonymization:RetentionMonths` (1–1200). Brak, pusta albo
  biała wartość = funkcja wyłączona (fail-closed, D-09 otwarta); inna wartość zatrzymuje start. Żaden
  plik repozytorium nie ustawia okresu.
- Reguła domeny `CustomerAnonymization` (początek okresu = najpóźniejsza z dat zamówienia, wpisu i
  ostatniej zmiany zamówienia, wpisów lub audytu — także usunięcia wpisu), `Order.AnonymizeCustomer`,
  `Refund/SalesCorrection.AnonymizeReason`, blokada klienta w `ApplyFromShop` (webhook, uzupełnianie,
  „Synchronizuj teraz”, „Ponów”) i w `Edit`.
- Anonimizacja zapisanych wpisów audytu przez nowy port `IAuditRedaction` (`AuditRecord.Redact`,
  jedyny wyjątek od „tylko dodawane”) i odczyt ostatniej aktywności `IAuditActivity`; wpisy operacji
  `Anonymized` dla zamówienia oraz każdego zwrotu i korekty ze zmienionym powodem.
- Migracja `CustomerAnonymization` (expand): `AnonymizedAtUtc`, `AnonymizedByAccountId`,
  `CK_Orders_Anonymization`.
- UI: blok „Dane osobowe klienta” z czterema stanami, dialog nieodwracalności, oznaczenie w
  nagłówku, klient tylko do odczytu w formularzu, operacja „Anonimizacja” w „Historii zmian”.
- Dokumenty: `docs/operations/anonimizacja.md` (nowy), `kopie-i-odtworzenie.md` §7,
  `powrot-do-wersji.md` §1, `deploy/api.env.example` (zakomentowany placeholder),
  `.sdd/context/conventions.md`.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Konfiguracja okresu retencji, walidacja startu, brak wartości domyślnej | Implemented | wiązanie jako `string?` (Deviations); `AnonymizationStartupTests` |
| Reguła początku okresu i dostępności (Behavior 6–9) | Implemented | `CustomerAnonymization`, `AnonymizationRetention`; początek obejmuje też audyt (rozstrzygnięcie 3 planu) |
| `POST /api/orders/{id}/anonymize` z kolejnością kontroli (Behavior 10–12) | Implemented | `disabled` przed `404` (rozstrzygnięcie 7 planu) |
| Anonimizacja zamówienia (Behavior 13) | Implemented | `Order.AnonymizeCustomer` |
| Anonimizacja powodów zwrotów i korekt (Behavior 14) | Implemented | wpłaty bez zmian |
| Anonimizacja wpisów audytu (Behavior 15) | Implemented | `IAuditRedaction`, `AuditRedactor`, `AuditScope` |
| Wpis audytu operacji bez danych osobowych (Behavior 16) | Implemented | plus wpisy zwrotów i korekt (rozstrzygnięcie 4 planu) |
| Brak logowania danych (Behavior 17) | Implemented | handler nie loguje |
| Synchronizacja nie przywraca danych (Behavior 18) | Implemented | domena; testy webhooka i uzupełniania/„Ponów” |
| Edycja po anonimizacji (Behavior 19) | Implemented | `422 order_customer_anonymized`, także dla zmiany rodzaju |
| Operacje finansowe po anonimizacji (Behavior 20) | Implemented | test `After_anonymization_customer_is_locked_but_erp_fields_and_entries_work` |
| Odczyty i raporty bez zmian (Behavior 21) | Implemented | `AnonymizationReportsUnchangedTests` |
| Migracja `CustomerAnonymization` (Storage) | Implemented | expand |
| UI szczegółów, dialog, oznaczenie, formularz (UI 1–5) | Implemented | |
| UI „Historia zmian” | Implemented | |
| `docs/operations/anonimizacja.md`, `kopie-i-odtworzenie.md`, `deploy/api.env.example` | Implemented | plus `powrot-do-wersji.md` (plan D2) |
| Scenariusze testów ręcznych UI | Implemented | sekcja poniżej; nie wykonane w tej sesji |

## Files

### Created

- `api/src/Shared/StoneFlow.Shared.Application/Audit/IAuditRedaction.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Audit/AuditScope.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Audit/AuditRedactor.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Audit/AuditActivity.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/CustomerAnonymization.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Application/AnonymizeCustomer/AnonymizeCustomer.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Application/AnonymizeCustomer/AnonymizationRetention.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Application/OrderAnonymizationAudit.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Options/AnonymizationOptions.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderAnonymizationStore.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/Migrations/20261113152523_CustomerAnonymization.cs` (+ `.Designer.cs`)
- `api/tests/StoneFlow.Orders.Tests/Domain/CustomerAnonymizationTests.cs`, `OrderAnonymizationTests.cs`, `EntryAnonymizationTests.cs`
- `api/tests/StoneFlow.Orders.Tests/Application/FakeAnonymizationPorts.cs`, `AnonymizeCustomerHandlerTests.cs`, `GetOrderHandlerTests.cs`, `OrderAnonymizationAuditTests.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/AnonymizationStartupTests.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Audit/AuditRedactionTests.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Orders/AnonymizationTestSupport.cs`, `CustomerAnonymizationApiTests.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Reporting/AnonymizationReportsUnchangedTests.cs`
- `app/src/app/features/orders/order-anonymization.ts` (+ `.spec.ts`)
- `docs/operations/anonimizacja.md`
- `docs/tasks/sf-22/06-implementation-done.md`

### Modified

- `api/src/Shared/StoneFlow.Shared.Kernel/Audit/AuditVocabulary.cs`, `StoneFlow.Shared.Application/Audit/IAuditLog.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Audit/AuditRecord.cs`, `StoneFlow.Reporting.Application/ChangeHistory/ChangeHistoryCodes.cs`, `StoneFlow.Reporting.Infrastructure/DependencyInjection.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/Order.cs`, `OrderRuleViolation.cs`, `Payments/OrderEntry.cs`, `Payments/Refund.cs`, `Payments/SalesCorrection.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Application/Abstractions/Ports.cs`, `GetOrder/GetOrder.cs`, `EditOrder/EditOrder.cs`, `OrderCodes.cs`, `WarsawTime.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/DependencyInjection.cs`, `Persistence/OrderConfiguration.cs`, `Persistence/OrderReader.cs`, `Persistence/Migrations/OrdersDbContextModelSnapshot.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Api/OrderProblems.cs`, `Endpoints/OrderContracts.cs`, `Endpoints/OrdersEndpoints.cs`
- `api/tests/StoneFlow.Orders.Tests/StoneFlow.Orders.Tests.csproj`, `packages.lock.json`, `Domain/ShopSyncTests.cs`
- `api/tests/StoneFlow.Reporting.Tests/Audit/AuditRecordTests.cs`, `ChangeHistory/ChangeHistoryCodesTests.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Orders/WooCommerceWebhookTests.cs`, `Integrations/WooCommerceBackfillTests.cs`, `Reporting/ChangeHistoryApiTests.cs`
- `app/src/app/features/orders/order-models.ts`, `orders-api.ts`, `orders.service.ts`, `order-details-page.ts/.html`, `order-form.ts/.html` (+ specy `orders-api`, `orders.service`, `order-details-page`, `order-form`)
- `app/src/app/features/change-history/change-history-models.ts`, `change-history-page.ts/.html`, `audit-field-catalog.ts` (+ specy `audit-field-catalog`, `change-history-page`)
- `app/src/testing/orders-testing.ts`
- `docs/operations/kopie-i-odtworzenie.md`, `docs/operations/powrot-do-wersji.md`, `deploy/api.env.example`, `.sdd/context/conventions.md`
- `docs/tasks/sf-22/task.md`

## Planned Units

| Item from the plan | Status |
|---|---|
| S1 `AuditOperation.Anonymized`, komentarz `IAuditLog` | Exists |
| S2 `IAuditRedaction`, `AuditRedaction`, `AuditFieldSelector`, `IAuditActivity` | Exists |
| S3 `AuditRecord.Redact`, `ChangeHistoryCodes`, `AuditScope`, `AuditRedactor`, `AuditActivity`, DI | Exists |
| B1 `CustomerAnonymization`, `AnonymizationStatus`, `AnonymizationAvailability` | Exists |
| B2 `AlreadyAnonymized`, `CustomerAnonymized` | Exists |
| B3 `EraseCreationRequestHash`, `AnonymizeReason` (zwrot, korekta) | Exists |
| B4 `Order` (fakt, `AnonymizeCustomer`, `ApplyFromShop`, `Edit`) | Exists |
| B5 `OrderAnonymizationAudit` | Exists |
| B6 porty, `OrderCodes.Of(AnonymizationStatus)`, `WarsawTime.DateOf` | Exists |
| B7 `AnonymizeCustomer` | Exists |
| B8 `GetOrder`, `EditOrder` | Exists |
| B9 `AnonymizationRetention` | Exists |
| B10 `AnonymizationOptions`, walidator, `ConfiguredAnonymizationSettings`, DI | Exists |
| B11 `OrderConfiguration`, migracja `CustomerAnonymization`, `OrderAnonymizationStore`, `OrderReader` | Exists |
| B12 `OrderProblems`, `OrderAnonymizationResponse`, endpoint `Orders.AnonymizeCustomer`, mapowanie edycji | Exists |
| B13 testy integracyjne | Exists |
| D1 `docs/operations/anonimizacja.md` | Exists |
| D2 `kopie-i-odtworzenie.md`, `powrot-do-wersji.md` | Exists |
| D3 `deploy/api.env.example` | Exists |
| D4 `.sdd/context/conventions.md` | Exists |
| F1 modele, fikstura | Exists |
| F2 `OrdersApi.anonymize` | Exists |
| F3 `OrdersService.anonymize`, mapowanie kodów | Exists |
| F4 `anonymizationText` | Exists |
| F5 szczegóły zamówienia | Exists |
| F6 formularz `customerAnonymized` | Exists |
| F7 „Historia zmian” | Exists |
| Trasy | Bez nowych tras (zgodnie z planem) |

## Database

- `OrdersDbContext` (schemat `orders`): migracja `20261113152523_CustomerAnonymization` — dwie kolumny
  `NULL` (`AnonymizedAtUtc datetime2`, `AnonymizedByAccountId uniqueidentifier`) i
  `CK_Orders_Anonymization`; `Down` odwrotnie; bez zmian danych.
- Pozostałe konteksty: bez migracji. `has-pending-model-changes` dla sześciu kontekstów z
  `deploy/scripts/contexts.sh`: „No changes have been made to the model since the last migration.”

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| CA-1 … CA-8 `CustomerAnonymizationTests` | Exists | Passed |
| OA-1 … OA-10 `OrderAnonymizationTests` | Exists | Passed |
| SS-1 `ShopSyncTests` (po anonimizacji) | Exists | Passed |
| RA-1 … RA-4 `EntryAnonymizationTests` | Exists | Passed |
| AU-1, AU-2 `OrderAnonymizationAuditTests` | Exists | Passed |
| H-AN-1 … H-AN-9 `AnonymizeCustomerHandlerTests` | Exists | Passed |
| H-GO-1 … H-GO-5 `GetOrderHandlerTests` | Exists | Passed |
| AR-1 … AR-5 `AuditRecordTests` | Exists | Passed |
| CHC-1 `ChangeHistoryCodesTests` | Exists | Passed |
| I-ST-1 … I-ST-6 `AnonymizationStartupTests` (I-ST-5 objęty skanem I-ST-6) | Exists | Passed |
| I-RD-1 … I-RD-4 `AuditRedactionTests` | Exists | Passed |
| I-AN-1 … I-AN-11 `CustomerAnonymizationApiTests` | Exists | Passed |
| I-RP-1 `AnonymizationReportsUnchangedTests` | Exists | Passed |
| I-WH-1, I-WH-2 `WooCommerceWebhookTests` | Exists | Passed |
| I-BF-1 `WooCommerceBackfillTests` | Exists | IT3_BACKFILL |
| I-CH-1 `ChangeHistoryApiTests` | Exists | Passed |
| Regresja: `OrdersApiTests`, `PaymentsApiTests`, `SalesCorrectionsApiTests`, `AuditLogTests`, `EndpointSecurityTests`, `OpenApiTests`, `LaunchDateStartupTests`, `AccessStartupTests`, testy raportów, `MvpFinancialScenariosTests`, `StoneFlow.Architecture.Tests` | bez edycji | IT3_REGRESSION; architektura Passed (56) |
| F-A1 `orders-api.spec.ts` | Exists | Passed |
| F-S1 `orders.service.spec.ts` | Exists | Passed |
| F-T1 `order-anonymization.spec.ts` | Exists | Passed |
| F-P1 … F-P6 `order-details-page.spec.ts` | Exists | Passed |
| F-F1 `order-form.spec.ts` | Exists | Passed |
| F-CH1 `audit-field-catalog.spec.ts` | Exists | Passed |
| F-CH2 `change-history-page.spec.ts` | Exists | Passed |

## Verification

Środowisko: Windows, LocalDB (`STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated
Security=true;TrustServerCertificate=true'`), bez Dockera; build i testy w Release.

| Command | Outcome |
|---|---|
| `dotnet restore api/tests/StoneFlow.Orders.Tests --force-evaluate` (aktualizacja `packages.lock.json` po dodaniu pakietu) | Passed |
| `dotnet restore stone-flow.slnx --locked-mode` | Passed |
| `dotnet format stone-flow.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet build` w Release projektów `StoneFlow.Api`, `StoneFlow.Orders.Tests`, `StoneFlow.Reporting.Tests`, `StoneFlow.Architecture.Tests`, `StoneFlow.Api.IntegrationTests` (0 ostrzeżeń, 0 błędów) | Passed |
| `dotnet build stone-flow.slnx -c Release --no-restore` (całe rozwiązanie naraz) | Not run — budowane po projektach (mało pamięci); wszystkie projekty zależne od zmian zbudowane |
| `dotnet ef migrations add CustomerAnonymization … --configuration Release` | Passed |
| `dotnet ef migrations has-pending-model-changes --no-build --configuration Release` × 6 kontekstów | Passed (brak zmian) |
| `dotnet test --project api/tests/StoneFlow.Orders.Tests -c Release --no-build` | Passed (258) |
| `dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release --no-build` | Passed (272) |
| `dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release --no-build` | Passed (56) |
| `StoneFlow.Api.IntegrationTests`, klasa `CustomerAnonymizationApiTests` | Passed (16) |
| `StoneFlow.Api.IntegrationTests`: `AnonymizationStartupTests`, `AuditRedactionTests`, `AnonymizationReportsUnchangedTests`, `WooCommerceWebhookTests`, `WooCommerceBackfillTests`, `ChangeHistoryApiTests` | 76/77 Passed; 1 Failed (nowy test I-BF-1 — znaczniki czasu poza oknem uzupełniania w samym teście), poprawiony i powtórzony w następnym wierszu |
| `StoneFlow.Api.IntegrationTests`: `WooCommerceBackfillTests` (po poprawce) + regresja `OrdersApiTests`, `PaymentsApiTests`, `SalesCorrectionsApiTests`, `AuditLogTests`, `EndpointSecurityTests`, `OpenApiTests`, `LaunchDateStartupTests`, `AccessStartupTests`, `SalesReportApiTests`, `CashFlowReportApiTests`, `OperationsApiTests`, `MvpFinancialScenariosTests` | Passed (218) |
| `dotnet test` pozostałych klas integracyjnych i projektów `StoneFlow.Access.Tests`, `StoneFlow.Expenses.Tests`, `StoneFlow.Integrations.Tests`, `StoneFlow.Shared.Tests` | Not run — kod tych obszarów bez zmian; ograniczenie czasu i pamięci |
| `deploy/scripts/test-migrate.sh` (LocalDB, `SQLCMD='sqlcmd -S (localdb)\MSSQLLocalDB -E'`) | Passed („wszystkie scenariusze zgodne z oczekiwaniami”) |
| Migracja na bazie z danymi (spec Verification) | Not run — brak lokalnej bazy z danymi do zmiany bez zgody właściciela; migracja tylko dodaje kolumny `NULL` |
| `npm run typecheck` | Passed |
| `npm run lint` (w tym `lint:colors`) | Passed |
| `npm run format:check` | Passed |
| `npm run build` | Passed |
| `npx ng test --watch=false` dla 9 plików spec (`order-anonymization`, `orders-api`, `orders.service`, `order-form`, `order-details-page`, `features/change-history/**`) | Passed (184) |
| `npm test -- --watch=false` (wszystkie specy) | Not run — uruchomione tylko specy dotknięte zmianą |
| `npm run test:scripts` | Passed (20) |
| `node scripts/check-bundle-secrets.mjs dist` | Passed |
| `npm ci` | Not run — zależności npm bez zmian |
| E2E (`npx playwright test e2e/orders.e2e.ts e2e/a11y.e2e.ts`) | Not run — opcjonalne w planie; wymaga osobnej bazy `StoneFlowE2E` i uruchomienia API oraz aplikacji |
| Scenariusze ręczne UI z działającym API | Not run — opisane poniżej do wykonania przez właściciela |

## Deviations

- Testy integracyjne retencji i synchronizacji używają okresu **1 miesiąca** i przesunięcia zegara z
  5.11.2026 o około miesiąc (granica 4/5.12.2026 w Europe/Warsaw), a nie okresu 60 miesięcy i
  zamówienia z 2021 r. (plan I-AN-2). Powód: ograniczenie przesunięcia zegara (sesja, klucze Data
  Protection); reguła 60 miesięcy z datą 15.03.2021 jest pokryta testami jednostkowymi (CA-4, H-AN-4,
  H-GO-3).
- I-WH-1: wariant „po usunięciu klucza konfiguracji (nowy host)” nie ma osobnego testu integracyjnego
  (każdy `OrdersTest` tworzy nową bazę). Pokrywają go CA-7 (stan `anonymized` przed `disabled`) i
  domenowa reguła `ApplyFromShop` niezależna od konfiguracji (SS-1).
- I-ST-5 (brak klucza w `appsettings*.json`) jest częścią skanu I-ST-6
  (`No_repository_file_sets_a_retention_period`), bez osobnego testu.
- I-AN-10: wyjątek wstrzyknięty w `IAuditRedaction` albo zapis wpisu `Anonymized` daje `500`; test
  sprawdza ten status.
- Odstępstwa od specyfikacji przyjęte w planie (rozstrzygnięcia 1–7): opcja wiązana jako `string?`;
  pusta wartość = funkcja wyłączona; początek okresu obejmuje czas ostatniego wpisu audytu (usunięcia
  wpisów); własne wpisy `Anonymized` zwrotów i korekt; fakty wpisów z portu
  `IOrderAnonymizationStore`, a nie z pól `OrderDetailsDto`; `anonymizedFields` = `"[]"` dla pustej
  listy; aktualizacja `powrot-do-wersji.md`.
- Spec „Out of scope” uzupełnia `anonimizacja.md` §6: dane w sklepie WooCommerce obsługuje się osobno
  w panelu sklepu (rozstrzygnięcie 5 planu).

## Additional Implementation

Additional implementation not covered by the original documents:

- Właściwość `Order.IsCustomerAnonymized` jest wyłączona z mapowania EF (`Ignore`), jak `IsCancelled`.
- `AuditRedactor` wywołuje `SaveChanges` tylko, gdy zmienił się co najmniej jeden wpis.

## Open Items

1. Human review required before merge (ryzyko `high`): lista pól w `Order.AnonymizeCustomer`,
   `OrderAnonymizationAudit.Fields` i `AnonymizeReason`; `CustomerAnonymization.RetentionStart` i
   `AnonymizationRetention`; zachowanie bez konfiguracji; `AuditRecord.Redact` i `AuditScope`;
   `docs/operations/anonimizacja.md` (krok porównania z D-09).
2. Przegląd kodu (`sdd-review-code SF-22`) — `05-code-review.md` jeszcze nie istnieje.
3. Potwierdzenia właściciela: decyzje wykonawcy ze spec (Assumptions 1–14) i rozstrzygnięcia 1–7 z
   planu (m.in. reguła początku okresu z audytem usunięć, pusta wartość = wyłączona, wpisy
   `Anonymized` zwrotów i korekt).
4. D-09 pozostaje otwarta; włączenie funkcji w produkcji wymaga kroków z `anonimizacja.md` §3.
5. Scenariusze testów ręcznych UI (poniżej) do wykonania.
6. Pełny przebieg `dotnet test --solution`, wszystkich speców frontendu i E2E — do wykonania w CI.

## Scenariusze testów ręcznych (UI)

### Przygotowanie

1. **Włączenie funkcji lokalnie** (tylko zmienna procesu API, nigdy plik w repozytorium). Okres
   1 miesiąc:
   - PowerShell, z katalogu głównego:
     `$env:StoneFlow__Anonymization__RetentionMonths = '1'; dotnet run --project api/src/StoneFlow.Api`
   - Git Bash: `StoneFlow__Anonymization__RetentionMonths=1 dotnet run --project api/src/StoneFlow.Api`
   - Visual Studio: profil uruchomienia → zmienne środowiskowe → `StoneFlow__Anonymization__RetentionMonths=1`
     (zmiany profilu nie zapisuj w `launchSettings.json` w repozytorium).

   Bez zmiennej (albo z pustą wartością) funkcja jest wyłączona. Wartość `0`, `1201` albo `abc`
   zatrzymuje start API z komunikatem o kluczu `StoneFlow:Anonymization:RetentionMonths`.
2. **Frontend:** `cd app && npm start` (proxy `/api` → `http://localhost:5080`), zalogowany właściciel.
3. **Zamówienie testowe** (dane fikcyjne): zamówienie ręczne z pełnymi danymi klienta (np. „Zenon
   Testowy”, telefon `500 000 000`, e-mail `zenon@example.com`, adres, NIP `1234563218`), uwagami do
   realizacji i komentarzem; wpłata gotówką; zwrot sprzedaży z powodem „Zwrot dla Zenona”; korekta
   sprzedaży z powodem „Rabat dla Zenona”. Zanotuj numer zamówienia (`ZR-…`).
4. **Postarzenie zamówienia** (SQL na lokalnej bazie, np. `sqlcmd -S (localdb)\MSSQLLocalDB -E -d
   <baza>` albo SSMS). Daty cofa się o 2 miesiące, więc przy okresie 1 miesiąca akcja jest dostępna, a
   zamówienie zostaje po dacie uruchomienia z `appsettings.Development.json` (2026-01-01). Trzeba
   cofnąć **także `reporting.AuditEntries.OccurredAtUtc`** — czas ostatniego wpisu audytu (także
   usunięcia wpisu) wchodzi do początku okresu retencji:

   ```sql
   DECLARE @id uniqueidentifier = (SELECT Id FROM orders.Orders WHERE Number = N'<numer>');
   DECLARE @day date = DATEADD(month, -2, CAST(SYSUTCDATETIME() AS date));
   DECLARE @at datetime2 = DATEADD(hour, 10, CAST(@day AS datetime2));
   UPDATE orders.Orders SET OrderDate = @day, CreatedAtUtc = @at, UpdatedAtUtc = @at WHERE Id = @id;
   UPDATE orders.Payments SET PaidOn = @day, CreatedAtUtc = @at, UpdatedAtUtc = @at WHERE OrderId = @id;
   UPDATE orders.Refunds SET RefundedOn = @day, CreatedAtUtc = @at, UpdatedAtUtc = @at WHERE OrderId = @id;
   UPDATE orders.SalesCorrections SET CorrectedOn = @day, CreatedAtUtc = @at, UpdatedAtUtc = @at WHERE OrderId = @id;
   UPDATE reporting.AuditEntries SET OccurredAtUtc = @at WHERE ObjectId = @id OR ParentObjectId = @id;
   ```

   Stan „przed upływem retencji” daje to samo zamówienie bez postarzenia (albo drugie, świeże).

### Scenariusze

| # | Kroki | Oczekiwany wynik |
|---|---|---|
| 1 | API **bez** zmiennej `StoneFlow__Anonymization__RetentionMonths`; otwórz szczegóły dowolnego zamówienia (także postarzonego), sekcja „Klient” | Blok „Dane osobowe klienta”: przycisk „Anonimizuj dane klienta” wyłączony, obok widoczny tekst „Funkcja wyłączona do decyzji o okresie retencji.”; brak oznaczenia w nagłówku. |
| 2 | Jak 1: przejdź klawiszem Tab przez sekcję „Klient”; czytnikiem ekranu (NVDA/Narrator) odczytaj przycisk | Przycisk wyłączony nie przyjmuje akcji; czytnik odczytuje opis z `aria-describedby` („Funkcja wyłączona…”). |
| 3 | Jak 1: w narzędziach deweloperskich (konsola) wyślij `POST /api/orders/<id>/anonymize` z aktualną wersją (albo `curl` z ciasteczkiem i `X-XSRF-TOKEN`) | `422`, `code: anonymization_disabled`, „Anonimizacja jest wyłączona do czasu ustalenia okresu retencji danych.”; dane bez zmian. |
| 4 | API z `StoneFlow__Anonymization__RetentionMonths=1`; otwórz szczegóły **świeżego** (niepostarzonego) zamówienia | Przycisk wyłączony; tekst „Dostępne po upływie okresu retencji, od {dzisiaj + 1 miesiąc}. Okres liczy się od {dzisiaj} — najpóźniejszej z dat zamówienia, jego wpisów i ostatniej zmiany.” |
| 5 | Okres 1; otwórz zamówienie **postarzone** skryptem z przygotowania | Przycisk aktywny; tekst „Okres retencji upłynął {data}. Anonimizacja usunie dane klienta, uwagi, komentarz i powody zwrotów i korekt.” |
| 6 | Jak 5: kliknij „Anonimizuj dane klienta”, sprawdź treść dialogu, naciśnij Escape | Dialog „Zanonimizować dane klienta zamówienia {numer}?” z informacją o nieodwracalności, zakresie („[zanonimizowano]”), raportach i synchronizacji; Escape zamyka, fokus wraca na przycisk; nic się nie zmienia (dane klienta nadal widoczne). |
| 7 | Jak 5: otwórz dialog i kliknij „Anuluj” | Dialog zamknięty, brak żądania, dane bez zmian. |
| 8 | Jak 5: otwórz dialog i kliknij „Anonimizuj dane klienta” | Przycisk na chwilę „Anonimizowanie…”, potem komunikat „Dane klienta zanonimizowane.” (region statusu); w nagłówku znacznik „Dane klienta zanonimizowane”; blok bez przycisku z tekstem „Dane klienta zanonimizowano {data, godzina} (Europe/Warsaw). Operacji nie można cofnąć.” |
| 9 | Po 8: sprawdź sekcje szczegółów | Nazwa „[zanonimizowano]”, telefon, e-mail, adres, NIP „Brak danych”; rodzaj klienta bez zmian; uwagi do realizacji i komentarz „Brak danych”; powody zwrotów i korekt „[zanonimizowano]”; wartość, wpłaty, Zapłacono, stan płatności, obniżenia sprzedaży, numer faktury, dostawcy i ZO bez zmian. |
| 10 | Po 8: „Edytuj” | Sekcja „Klient” formularza tylko do odczytu (także rodzaj) z tekstem „Dane klienta zanonimizowano; nie można ich zmienić.”; zmiana statusu na „Zakończone” zapisuje się („Zmiany zapisane.”). |
| 11 | Po 8: dodaj wpłatę i zwrot (z nowym powodem), popraw stary zwrot bez zmiany powodu | Zapisy działają; nowy zwrot ma wpisany powód, stary zostaje „[zanonimizowano]”. |
| 12 | Po 8: „Historia zmian”, okres obejmujący dzisiaj, filtr „Zamówienie” | Wpis „Anonimizacja” (ton ostrzegawczy), autor „Właściciel”, rozwinięcie tylko z kolumną „Jest”: „Zanonimizowane pola” z etykietami (np. „Imię i nazwisko lub nazwa firmy, Telefon, E-mail, …, Uwagi do realizacji, Komentarz”), „Zanonimizowane powody zwrotów i korekt”, „Początek okresu retencji”, „Okres retencji (miesiące)” = 1. |
| 13 | Jak 12: filtr „Zwrot” i „Korekta sprzedaży”; potem okres sprzed postarzenia (2 miesiące wstecz) | Wpisy „Anonimizacja” zwrotu i korekty z „Zanonimizowane pola: Powód”; dawne wpisy (utworzenie, zmiana klienta, usunięcie) pokazują „[zanonimizowano]” zamiast danych klienta i powodów, z nazwami pól, kwotami i datami. |
| 14 | Przed 8 zanotuj wartości Pulpitu (Sprzedaż i Przepływy) i listy „Operacje” za okres obejmujący datę postarzonego zamówienia; po 8 odczytaj je ponownie | Karty, wykresy, zestawienie składników sprzedaży i „Operacje” identyczne przed i po. |
| 15 | Po 8: lista „Zamówienia”, wyszukaj dawną nazwę klienta, potem „[zanonimizowano]” | Dawna nazwa: brak wyników; znacznik: zamówienie z klientem „[zanonimizowano]”. |
| 16 | Synchronizacja nie przywraca danych (wymaga sekretu webhooka w konfiguracji lokalnej): zamówienie ze sklepu (`WC-…`) postarz skryptem, zanonimizuj, potem wyślij podpisany webhook `order.updated` z pełnymi danymi klienta i nowszą datą modyfikacji (albo „Synchronizuj teraz” na `/integracja` przy skonfigurowanym REST) | Klient zostaje „[zanonimizowano]”; status ze sklepu i wartość się aktualizują; w „Historii zmian” wpis „Synchronizacja WooCommerce” bez pól klienta. |
| 17 | Po 8: drugi raz otwórz szczegóły w drugiej karcie sprzed anonimizacji (stara wersja) i kliknij „Anonimizuj dane klienta” → potwierdź | Komunikat „Zamówienie zostało zmienione w międzyczasie.” z „Wczytaj aktualne dane”; jeden wpis „Anonimizacja” w historii. |
| 18 | Telefon (375 px) w trybie urządzenia: szczegóły postarzonego zamówienia, blok i dialog | Blok i dialog bez poziomego przewijania strony; przycisk i teksty czytelne, dialog obsługiwany klawiaturą. |
| 19 | Zatrzymaj API, uruchom z `StoneFlow__Anonymization__RetentionMonths=0` (potem `abc`) | API nie startuje; w konsoli błąd walidacji z kluczem `StoneFlow:Anonymization:RetentionMonths` i zakresem, bez wpisanej wartości. |
| 20 | Uruchom API bez zmiennej i otwórz zamówienie zanonimizowane w 8 | Stan „zanonimizowane” (tekst z czasem, znacznik w nagłówku) mimo wyłączonej funkcji; pozostałe zamówienia „Funkcja wyłączona…”. |
