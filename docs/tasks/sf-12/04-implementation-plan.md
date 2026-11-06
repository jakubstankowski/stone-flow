# Implementation Plan — SF-12

Tryb: **fullstack** (nowy kontrakt webhooka, zmiany modelu i API zamówień oraz UI listy i szczegółów).
Bez podziału na części: backend i frontend łączy jeden kontrakt `GET /api/orders` /
`GET /api/orders/{id}`. Frontend można robić równolegle z backendem, według kontraktu z sekcji 5.

## 1. Scope

Plan dostarcza:
- **webhook WooCommerce** w module Integrations: podpis, ping, limit żądań, mapowanie payloadu;
- **kontrakt** `IShopOrderSync` i `ShopOrderSnapshot` w Shared.Application;
- **regułę zastosowania snapshotu** w domenie Orders (`Order.ApplyFromShop`) z obsługą „zakończone w sklepie”, znaczników usunięcia i kolejności dostaw;
- **trwałość:** nowe kolumny, tabelę `orders.ShopDeletions`, blokadę po `ExternalId` i migrację;
- **audyt** z autorem `WooCommerceSync`;
- **API listy i szczegółów:** `shopStatus`, `includeHiddenShopStatuses`, `shopWarnings`;
- **UI:** przełącznik filtra, etykiety statusów sklepu, ostrzeżenia;
- **testy i dokumentację.**

Domyka uwagi z `03-spec-review.md` (v2), oznaczone niżej [N1]–[N14].

## 2. Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Shared.Application | `ShopOrders/` | kontrakt synchronizacji dwóch modułów |
| Orders.Domain | `Orders/Order.cs`, `Orders/ShopStatuses.cs`, `OrderVocabulary.cs` | reguły sklepu, nowe pola audytu |
| Orders.Application | `ShopSync/ApplyShopOrder.cs`, `OrderAudit.cs`, `Abstractions/Ports.cs`, `ListOrders`, `GetOrder` | zastosowanie snapshotu, audyt, lista, ostrzeżenia |
| Orders.Infrastructure | `Persistence/*`, migracja `ShopOrderSync` | kolumny, `ShopDeletions`, blokada, odczyty |
| Orders.Api | `Endpoints/OrderContracts.cs`, `OrdersEndpoints.cs` | parametr filtra, nowe pola odpowiedzi |
| Integrations.* | `Api/WooCommerceWebhookEndpoint.cs`, `Application/WooCommerce/*`, `Infrastructure/WooCommerceOptions.cs` | webhook |
| Frontend | `features/orders/*`, `shared/format` | filtr, etykiety, ostrzeżenia |
| Docs | `docs/wdrozenia.md`, `deploy/api.env.example`, `conventions.md` | konfiguracja, wyjątek trasy |

## 3. Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Shared/StoneFlow.Shared.Application/ShopOrders/ShopOrderSync.cs` | create | `ShopOrderSnapshot`, `ShopCustomer`, `ShopOrderTopic`, `IShopOrderSync`, `ShopOrderSyncResult` |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/ShopStatuses.cs` | create | `IsTerminal`, `IsHiddenByDefault`, `IsKnown`, kody |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/Order.cs` | modify | pola `ShopModifiedAtUtc`, `ShopDeletedAtUtc`, `ShopRefundedAmount`; `CreateFromShop` z nowymi polami; `ApplyFromShop`; `IsTerminalInShop`; `Snapshot` + nowe pola |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/OrderVocabulary.cs` | modify | `OrderField.ShopStatus`, `ShopDeleted`, `ShopRefundedAmount` |
| `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/ShopWarnings.cs` | create | wyliczenie `ShopWarning` dla szczegółów |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/ShopSync/ApplyShopOrder.cs` | create | `ShopOrderSync : IShopOrderSync` (jednostka pracy, blokada, znaczniki, audyt) |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/Abstractions/Ports.cs` | modify | `IShopOrderLock`, `IShopOrderStore` (wyszukanie po ID zewnętrznym, znaczniki usunięcia) |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/OrderAudit.cs` | modify | przeciążenia z `AuditAuthor`; formatowanie nowych pól |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/OrderCodes.cs` | modify | kody `shopStatus`, `shopDeleted`, `shopRefundedAmount` |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/ListOrders/ListOrders.cs` | modify | `IncludeHiddenShopStatuses`; `ShopStatus` w elemencie |
| `api/src/Modules/Orders/StoneFlow.Orders.Application/GetOrder/GetOrder.cs` | modify | nowe pola i `ShopWarnings` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderConfiguration.cs` | modify | kolumny, check constraints |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/ShopDeletionConfiguration.cs` | create | tabela `ShopDeletions` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/ShopOrderStore.cs` | create | `IShopOrderStore`, `IShopOrderLock` (`sp_getapplock`) |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderReader.cs` | modify | filtr ukrytych, `ShopStatus` na liście, nowe pola w szczegółach |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrdersDbContext.cs` | modify | `DbSet<ShopDeletion>` |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/DependencyInjection.cs` | modify | rejestracja `IShopOrderSync`, portów |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/Migrations/*_ShopOrderSync*.cs` | create | migracja |
| `api/src/Modules/Orders/StoneFlow.Orders.Api/Endpoints/OrderContracts.cs`, `OrdersEndpoints.cs` | modify | parametr i pola |
| `api/src/Modules/Integrations/StoneFlow.Integrations.Application/WooCommerce/WebhookSignature.cs` | create | HMAC, porównanie w czasie stałym |
| `api/src/Modules/Integrations/StoneFlow.Integrations.Application/WooCommerce/WooCommerceOrderPayload.cs` | create | parsowanie JSON → `ShopOrderSnapshot` |
| `api/src/Modules/Integrations/StoneFlow.Integrations.Application/WooCommerce/ReceiveWebhook.cs` | create | przyjęcie dostawy: ping, temat, mapowanie, wywołanie synchronizacji z ponowieniem |
| `api/src/Modules/Integrations/StoneFlow.Integrations.Infrastructure/WooCommerce/WooCommerceOptions.cs` | create | `StoneFlow:WooCommerce:WebhookSecret` + walidator [N7] |
| `api/src/Modules/Integrations/StoneFlow.Integrations.Infrastructure/DependencyInjection.cs` | modify | opcje, handler |
| `api/src/Modules/Integrations/StoneFlow.Integrations.Api/WooCommerceWebhookEndpoint.cs` | create | `POST /webhooks/woocommerce/orders` |
| `api/src/Modules/Integrations/StoneFlow.Integrations.Api/IntegrationsModule.cs` | modify | mapowanie endpointu, polityka limitu |
| testy (sekcja 8) | create/modify | |
| `app/src/app/features/orders/order-models.ts`, `orders-api.ts`, `orders.service.ts` | modify | nowe pola, parametr |
| `app/src/app/features/orders/shop-status.ts` (+ spec) | create | etykiety statusów sklepu |
| `app/src/app/features/orders/orders-list-page.{ts,html}` (+ spec) | modify | przełącznik, etykieta statusu sklepu |
| `app/src/app/features/orders/order-details-page.{ts,html}` (+ spec) | modify | ostrzeżenia, dane sklepu |
| `docs/wdrozenia.md`, `deploy/api.env.example`, `.sdd/context/conventions.md` | modify | [N6][N8] |

## 4. Implementation steps

1. **Kontrakt (Shared.Application/ShopOrders)** [N2][N3]
   - `ShopOrderTopic { Created, Updated, Restored, Deleted }`;
   - `ShopOrderSnapshot(ExternalId, Topic, ModifiedAtUtc?, OrderDate?, Status?, Value?, RefundedAmount, ShopCustomer?)`;
   - `IShopOrderSync.ApplyAsync(snapshot, ct)` → `ShopOrderSyncResult { Created, Updated, Unchanged, SkippedOlder, SkippedBeforeLaunch, SkippedDeletedInShop, DeletionRecorded, DuplicateRetry }`. `DuplicateRetry` oznacza naruszenie unikalności po wycofaniu jednostki pracy; Integrations ponawia wtedy raz w nowym zakresie DI.
2. **Domena.**
   - `ShopStatuses`: kody standardowe; `IsTerminal(status, deleted)` = deleted || `cancelled` || `trash`; `IsHiddenByDefault`.
   - `Order`: nowe pola; `CreateFromShop(id, number, snapshot-like content, shopStatus, modifiedAtUtc, deletedAtUtc?, refundedAmount, nowUtc)` ustawia status realizacji „Anulowane”, gdy zamówienie jest zakończone w sklepie.
   - `ApplyFromShop(ShopOrderChange change, nowUtc)` → `OrderChangeResult` lub `Skipped(reason)` według spec pkt 3–6. Ustalenia:
     - **[N1]** `order.restored` podlega tym samym regułom kolejności co `order.updated`: usunięcie czyści tylko dostawa z `ModifiedAtUtc` > `ShopDeletedAtUtc`. Przywrócenie z kosza w WooCommerce zmienia datę modyfikacji, więc nowsza dostawa przejdzie. Dzięki temu stare przywrócenie nie cofnie późniejszego usunięcia. Ponowne trwałe usunięcie po przywróceniu w sklepie to nowe zakończenie, więc zamówienie znów się anuluje (zgodnie z PRD).
     - **Ostrzeżenia [niejasność z przeglądu]** liczone pojęciem „zakończone w sklepie”:
       - `active_in_shop` = ERP „Anulowane” i zamówienie aktywne w sklepie;
       - `cancelled_in_shop` = ERP nie „Anulowane” i zamówienie zakończone w sklepie;
       - `deleted_in_shop` = ustawione `ShopDeletedAtUtc` albo status `trash`.
   - `OrderField` + 3 pola; `Snapshot()` je zwraca. `ShopModifiedAtUtc` nie wchodzi do snapshotu audytu.
3. **Trwałość.**
   - Kolumny `ShopModifiedAtUtc`, `ShopDeletedAtUtc`, `ShopRefundedAmount decimal(18,2) default 0`.
   - Check constraints:
     - pola sklepu tylko dla `Source <> 'Manual'`;
     - `ShopRefundedAmount >= 0` [N11].
   - Encja `ShopDeletion(ExternalId PK nvarchar(64), ReceivedAtUtc)`.
   - Migracja: `dotnet ef migrations add ShopOrderSync -p …Orders.Infrastructure -s api/src/StoneFlow.Api -c OrdersDbContext -o Persistence/Migrations`.
4. **Blokada po ID zewnętrznym [N4].** `IShopOrderLock.LockAsync(externalId)` w jednostce pracy: `sp_getapplock @Resource = 'orders:shop:{externalId}', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000`. Wynik < 0 → wyjątek (→ `503`). Blokada obejmuje też nieistniejący wiersz i znacznik usunięcia, więc równoczesne `created` i `deleted` się szeregują.
5. **`ShopOrderSync` (Orders.Application).** `IUnitOfWork.ExecuteInTransactionAsync`:
   - blokada;
   - wczytanie zamówienia (`IShopOrderStore.FindAsync(externalId)` → `StoredOrder?`) i znacznika;
   - zastosowanie reguł domeny;
   - zapis `Add`/`Save`;
   - `IAuditLog.RecordAsync` z `AuditAuthor.WooCommerceSync`;
   - obsługa znacznika: zapis przy `Deleted` dla nieznanego; usunięcie po utworzeniu zamówienia, zawsze [N13].

   `DuplicateRequest` ze store'a (indeks `Number` albo `Source+ExternalId`, kolizja PK `ShopDeletions`) → wycofanie i wynik `DuplicateRetry`. Zamówienie sprzed uruchomienia: `SkippedBeforeLaunch`, znacznik zostaje (pomijalny przyrost) [N13].
6. **Odczyty.**
   - `ListOrders`: parametr `includeHiddenShopStatuses` (`true`/`false`, inny → błąd pola). Domyślnie warunek `ShopStatus IS NULL OR ShopStatus NOT IN ('pending','failed','checkout-draft')`.
   - `OrderListItemDto.ShopStatus`.
   - `OrderDetailsDto`: `ShopModifiedAtUtc`, `ShopDeletedAtUtc`, `ShopRefundedAmount`, `ShopWarnings` (z domeny, kody camelCase w API).
7. **Integrations.**
   - `WooCommerceOptions` (`StoneFlow:WooCommerce:WebhookSecret`): brak → integracja wyłączona; obecny, ale krótszy niż 16 znaków → błąd startu [N7].
   - `WebhookSignature.Verify(bytes, header, secret)`: `HMACSHA256` + `CryptographicOperations.FixedTimeEquals` na zdekodowanych bajtach; zły Base64 → false.
   - `WooCommerceOrderPayload.TryParse(topic, bytes)` (`System.Text.Json`, `JsonDocument`):
     - pola wymagane;
     - `total` jako `decimal` w `InvariantCulture`, co najwyżej 2 miejsca po przecinku, nieujemny; inaczej treść nieczytelna [N11];
     - zwroty: suma `|refunds[].total|`;
     - data `date_created_gmt` → `DateOnly` w Europe/Warsaw (`WarsawTimeZone` z Shared.Application);
     - klient: pusta nazwa → „Klient sklepu #{id}” [N12];
     - NIP: usunięte spacje, myślniki i prefiks `PL` [N12];
     - obcinanie do limitów `Customer` (stałe z Orders.Domain niedostępne — limity przekazane w kontrakcie Shared: `ShopCustomer.MaxLengths`; albo obcinanie po stronie Orders przy budowie `Customer` — **wybrane: obcinanie w Orders**, Integrations przekazuje surowe wartości);
     - log przy walucie ≠ PLN i statusie nieznanym.
   - `ReceiveWebhookHandler`:
     - ping (`^webhook_id=\d{1,20}$`, brak tematu) → `Ok`;
     - wyłączona → `Disabled`;
     - podpis → `Unauthorized`;
     - temat spoza czterech → `Ok`;
     - parsowanie: błąd → `Ok` + log;
     - `IShopOrderSync.ApplyAsync` w nowym zakresie (`IServiceScopeFactory`); `DuplicateRetry` → jedno ponowienie w kolejnym nowym zakresie;
     - wyjątek bazy → `Unavailable`.
   - Endpoint `POST /webhooks/woocommerce/orders`:
     - `AllowAnonymous`, `DisableAntiforgery`, `RequireRateLimiting("woocommerce-webhook")` (300/min/IP, fixed window, `429`);
     - limit treści 1 MB przez `IHttpMaxRequestBodySizeFeature` / odczyt z limitem → `413`;
     - odczyt nagłówków `X-WC-Webhook-Signature`, `-Topic`, `-Delivery-ID` (obcięte do 64 znaków w logu).
   - Mapowanie wyniku: `Ok` → `200`, `Unauthorized` → `401`, `Disabled` → `404`, `Unavailable` → `503`.
8. **Frontend** (kontrakt z sekcji 5).
   - `order-models.ts`: `shopStatus` w elemencie listy; w szczegółach `shopModifiedAtUtc`, `shopDeletedAtUtc`, `shopRefundedAmount` i `shopWarnings: ('activeInShop'|'deletedInShop'|'cancelledInShop'|'refundedInShop')[]`.
   - `shop-status.ts`: `shopStatusLabel(code)` według spec.
   - Lista:
     - przełącznik (checkbox daisyUI „Pokaż oczekujące, nieudane i szkice ze sklepu”) w panelu filtrów, w URL `hidden=1` (lub nazwa zgodna z istniejącymi filtrami), wysyłany jako `includeHiddenShopStatuses=true`;
     - w kolumnie statusu druga etykieta `badge badge-ghost badge-sm` ze statusem sklepu.
   - Szczegóły: alerty jak w `sales-corrections.html`, w kolejności `deleted_in_shop`, `active_in_shop`, `cancelled_in_shop`, `refunded_in_shop` (kwota `formatPln`); status sklepu z etykietą.
9. **Dokumentacja [N6][N8].**
   - `docs/wdrozenia.md`, nowa sekcja „Webhooki WooCommerce”:
     - cztery webhooki (created, updated, deleted, restored) w sklepie, API v3, ten sam sekret, adres `https://<adres-api>/webhooks/woocommerce/orders`;
     - sekret w `appsettings.Production.json` (`StoneFlow:WooCommerce:WebhookSecret`);
     - kody `401`/`404`/`413`/`429`/`503` liczą się do wyłączenia webhooka w WooCommerce; po wyłączeniu włączyć ponownie w panelu, a luki uzupełni SF-13;
     - integracja wyłączona do próby SF-2.
   - `deploy/api.env.example`: klucz sekretu.
   - `conventions.md`: wyjątek trasy `/webhooks/*` poza `/api` (bez CSRF i ciasteczek, uwierzytelnienie podpisem) [N6].
   - Rozbieżność „Out of scope” vs „Reguły w domenie” [N9]: obowiązuje „Reguły w domenie” (reguła sprzedaży w SF-19).

## 5. Data & contracts

- Storage: `orders.Orders` + `ShopModifiedAtUtc datetime2 null`, `ShopDeletedAtUtc datetime2 null`, `ShopRefundedAmount decimal(18,2) not null default 0`, check `CK_Orders_ShopFields`, check `CK_Orders_ShopRefundedAmount`; nowa `orders.ShopDeletions (ExternalId nvarchar(64) PK, ReceivedAtUtc datetime2)`.
- API:
  - `POST /webhooks/woocommerce/orders` → `200|401|404|413|429|503` bez treści [N14].
  - `GET /api/orders?includeHiddenShopStatuses=true` → element listy `+ "shopStatus": "processing" | null`.
  - `GET /api/orders/{id}` → `+ "shopModifiedAtUtc", "shopDeletedAtUtc", "shopRefundedAmount": "0.00", "shopWarnings": ["activeInShop", …] (kody camelCase jak inne słowniki API)`.
- Typy współdzielone: sekcja 4 krok 1.

## 6. Edge cases & null safety

| Przypadek | Gdzie |
|---|---|
| Duplikat dostawy, równoczesne utworzenie | blokada `sp_getapplock` + unikalność → `DuplicateRetry` w Integrations |
| Starsza dostawa | `Order.ApplyFromShop` (`SkippedOlder`) |
| Dostawa sprzed usunięcia (także `restored`) | `ApplyFromShop` (`SkippedDeletedInShop`), znacznik przy tworzeniu |
| Drugie `order.deleted`, `trash`→`cancelled` | reguła przejścia aktywne→zakończone |
| Usunięcie nieznanego | `ShopDeletions` (`DeletionRecorded`) |
| Przed uruchomieniem | `ShopOrderSync` (`SkippedBeforeLaunch`) |
| Zły podpis / brak sekretu / ping / nieczytelna treść / inny temat | `ReceiveWebhookHandler` |
| >1 MB, >300/min | endpoint |
| `total` z >2 miejscami, ujemne | `WooCommerceOrderPayload` |
| Pusta nazwa, NIP z `PL`, długie teksty | parser / budowa `Customer` w Orders |
| Konflikt z edycją użytkownika | istniejące `409` (`OrderStore.SaveAsync`) |

Null: `ShopStatus`, `ShopModifiedAtUtc`, `ShopDeletedAtUtc` nullowalne; `Deleted` snapshot bez `ModifiedAtUtc`/`Value`/klienta — `ApplyFromShop` obsługuje osobną gałęzią.

## 7. Invariants

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sales separate from cash | `ApplyFromShop` nie dotyka wpisów; brak wywołań wpłat | `Webhook_never_creates_payments` (integration) |
| 2. Backend decimal money | parser `decimal` InvariantCulture, ≤2 miejsca | `PayloadTests.Total_with_three_decimals_is_unreadable` |
| 3. API-enforced access | podpis HMAC, `401` | `Webhook_with_bad_signature_changes_nothing`, `EndpointSecurityTests` (lista anonimowych + `POST /webhooks/woocommerce/orders`) |
| 4. Retries never duplicate | blokada, unikalność, reguły kolejności i przejścia | `Same_delivery_twice_one_record`, `Concurrent_created_one_record`, `Redelivered_status_does_not_change_fulfillment`, `Second_delete_after_erp_restore_does_not_cancel` |
| 5. Financial audit trail | `OrderAudit` z `AuditAuthor.WooCommerceSync` w jednostce pracy | `Sync_changes_are_audited_by_woocommerce_sync` |
| 6. Secrets stay server-side | opcje, logi bez treści i podpisu | `Webhook_logs_contain_no_payload_or_signature` |
| 7. Module boundaries | Integrations → `IShopOrderSync` | istniejące `ModuleBoundaryTests`, `ProjectReferenceTests` |
| 8. No identifying data | fikcyjne payloady | przegląd |

## 8. Tests

| Test | Type | What it proves |
|---|---|---|
| `ShopStatusesTests` | unit (Orders.Tests) | terminal/ukryte/nieznane |
| `OrderApplyFromShopTests` — przejście aktywne→zakończone, ponowny status, `trash`→`cancelled`, drugie usunięcie po przywróceniu w ERP, starsza dostawa, dostawa sprzed usunięcia, `restored` nowsze/starsze, pola ERP nietknięte, ostrzeżenia | unit | reguły domeny |
| `WebhookSignatureTests`, `WooCommerceOrderPayloadTests` (fikcyjne JSON-y: pełne, deleted, szkic bez daty, `total` 3 miejsca, NIP `PL 123-456-78-90`, pusta nazwa, zwroty) | unit (Integrations.Tests — nowy projekt testów lub Shared.Tests) | podpis i mapowanie |
| `WooCommerceWebhookTests` (SQL): utworzenie, duplikat, równoczesne created×2, created+deleted równocześnie, starsza, przed uruchomieniem, usunięcie (z wpłatą), usunięcie nieznanego potem created, przywrócenie, zły podpis, brak sekretu (`404`), ping bez podpisu, nieczytelna treść (`200`, brak zmian), audyt `WooCommerceSync`, brak wpłat, `409` dla użytkownika po zapisie synchronizacji [N14] | integration | AC |
| `OrdersApiTests` — lista bez/z ukrytymi, `shopStatus`, szczegóły z `shopWarnings` | integration | lista i szczegóły |
| `EndpointSecurityTests` | integration | nowy anonimowy endpoint |
| `orders-list-page.spec.ts` (przełącznik, etykieta statusu sklepu) [N14], `order-details-page.spec.ts` (ostrzeżenia), `shop-status.spec.ts` | frontend unit | UI |

## 9. Verification

- `dotnet build stone-flow.slnx -c Release`; `dotnet format stone-flow.slnx --verify-no-changes`
- `dotnet test` dla Orders.Tests, Integrations/Shared.Tests, Architecture.Tests; integracyjne z filtrem `*WooCommerce*`, `*Orders*`, `*Security*` (LocalDB)
- `cd app && npm run lint && npm test && npm run build`
- CI: pełny zestaw i `test-migrate.sh`
- **Przegląd przez człowieka przed merge (ryzyko high).**

## 10. Risks

- **[N5] Ponawianie po `503` niepotwierdzone:** jeśli WooCommerce nie ponawia, usunięcie odebrane przy awarii bazy przepada (REST go nie odtworzy). Do decyzji właściciela po próbie SF-2, np. alert przy `503`. Zapisane w `docs/wdrozenia.md`.
- **[N10] Różnica zegarów:** zegar sklepu, który się spieszy, może sprawić, że spóźniona dostawa sprzed usunięcia wyczyści usunięcie. Do sprawdzenia w próbie SF-2; tolerancji nie wprowadzamy.
- **Dwie dostawy z tą samą datą modyfikacji (sekunda)** — wygrywa późniejsza; ryzyko akceptowane (dopisane do założeń w manifeście).
- **Potwierdzenie „Przywróć zamówienie”** — bez zmian tekstu; ostrzeżenie `cancelled_in_shop` widać w szczegółach przed akcją.
- SF-2 nieukończone: integracja wyłączona do próby; przed merge przegląd człowieka.
