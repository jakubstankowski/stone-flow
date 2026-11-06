# Implementation done — SF-12

## Summary

Dostarczono import zamówień WooCommerce przez webhooki:
- **endpoint** `POST /webhooks/woocommerce/orders` (moduł Integrations): podpis HMAC, ping, limit 1 MB, limit 300/min/IP, wyłączenie bez sekretu;
- **mapowanie** treści na `ShopOrderSnapshot` (Shared.Application);
- **zastosowanie dostaw** w module Orders:
  - blokada `sp_getapplock` po ID zewnętrznym;
  - reguły „zakończone w sklepie”, jednorazowe przejście na „Anulowane”, usunięcie, znacznik `orders.ShopDeletions`, kolejność dostaw;
  - audyt `WooCommerceSync`;
- **migracja** `ShopOrderSync`;
- **API:** filtr `includeHiddenShopStatuses`, pole `shopStatus` na liście, w szczegółach `shopWarnings` i pola sklepu;
- **UI:** przełącznik na liście, polskie etykiety statusów sklepu, ostrzeżenia w szczegółach;
- **testy i dokumentacja.**

Po przeglądzie kodu (`05-code-review.md`, PASS WITH ISSUES) poprawiono:
- wynik `RetryRequired` (konflikt wersji i unikalności);
- górną granicę kwot;
- sekret z samych spacji;
- dokumentację limitu żądań i sekretu.

Dodano testy dla ważnych 2–3 (część). Ponownego przeglądu kodu nie wykonano.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Webhook z podpisem, ping, `401`/`404`/`413`/`429`/`503` | Implemented | `413`/`429`/`503` bez testów |
| Mapowanie payloadu (billing, NIP z meta, `total`, data Warszawa, zwroty) | Implemented | |
| Pola wymagane, nieczytelna treść → `200` + log | Implemented | |
| Unikalność i równoczesne dostawy | Implemented | blokada + `RetryRequired` |
| Własność pól, pola ERP nietknięte | Implemented | |
| Kolejność dostaw, dostawy sprzed usunięcia, `restored` | Implemented | |
| Jednorazowe przejście na „Anulowane”, usunięcie, znacznik usunięcia nieznanego | Implemented | |
| Przed datą uruchomienia — bez rekordu i błędu | Implemented | |
| Status przy imporcie („W trakcie”/„Anulowane”) | Implemented | |
| Brak wpłat i wypływów; zwrot w sklepie jako informacja | Implemented | |
| Ostrzeżenie o obniżeniach poniżej sumy | Implemented | istniejąca reguła SF-10, test dodany |
| Audyt `WooCommerceSync` z polami `shopStatus`, `shopDeleted`, `shopRefundedAmount` | Implemented | |
| Lista: ukryte statusy, filtr, `shopStatus` z etykietą | Implemented | |
| Szczegóły: `shopWarnings`, pola sklepu, etykiety | Implemented | |
| Log przy obcięciu tekstu | Not implemented | teksty są obcinane bez logu |
| Dokumentacja (`wdrozenia.md`, `api.env.example`, `conventions.md`) | Implemented | |

## Files

### Created

- `api/src/Shared/StoneFlow.Shared.Application/ShopOrders/ShopOrderSync.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/ShopStatuses.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Application/ShopSync/ApplyShopOrder.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/ShopOrderStore.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/ShopDeletionConfiguration.cs`
- `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/Migrations/20261106103530_ShopOrderSync.cs` (+ `.Designer.cs`)
- `api/src/Modules/Integrations/StoneFlow.Integrations.Application/WooCommerce/WebhookSignature.cs`
- `api/src/Modules/Integrations/StoneFlow.Integrations.Application/WooCommerce/WooCommerceOrderPayload.cs`
- `api/src/Modules/Integrations/StoneFlow.Integrations.Application/WooCommerce/ReceiveWebhook.cs`
- `api/src/Modules/Integrations/StoneFlow.Integrations.Infrastructure/WooCommerce/WooCommerceWebhook.cs`
- `api/src/Modules/Integrations/StoneFlow.Integrations.Api/WooCommerceWebhookEndpoint.cs`
- `api/tests/StoneFlow.Integrations.Tests/` (projekt, `WooCommerce/WooCommercePayloadTests.cs`)
- `api/tests/StoneFlow.Orders.Tests/Domain/ShopSyncTests.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Orders/WooCommerceWebhookTests.cs`
- `app/src/app/features/orders/shop-status.ts`, `shop-status.spec.ts`

### Modified

- `Order.cs`, `OrderVocabulary.cs`, `Ports.cs`, `OrderAudit.cs`, `OrderCodes.cs`, `ListOrders.cs`, `GetOrder.cs` (Orders.Domain/Application)
- `OrderConfiguration.cs`, `OrderReader.cs`, `OrdersDbContext.cs`, `DependencyInjection.cs`, `OrdersDbContextModelSnapshot.cs` (Orders.Infrastructure)
- `OrderContracts.cs`, `OrdersEndpoints.cs` (Orders.Api)
- `IntegrationsModule.cs`, Integrations `DependencyInjection.cs`
- `OrdersTestSupport.cs`, `EndpointSecurityTests.cs`
- frontend: `order-models.ts`, `orders-api.ts`, `orders.service.ts`, `orders-list-page.{ts,html,spec.ts}`, `order-details-page.{ts,html,spec.ts}`, `orders-api.spec.ts`, `testing/orders-testing.ts`
- `stone-flow.slnx`, `deploy/api.env.example`, `docs/wdrozenia.md`, `.sdd/context/conventions.md`, `docs/tasks/sf-11/06-implementation-done.md` (odwołanie do sekcji)

## Planned Units

| Item from the plan | Status |
|---|---|
| Kontrakt Shared.Application/ShopOrders | Exists |
| `ShopStatuses`, `Order.ImportFromShop`/`ApplyFromShop`, `OrderField` + 3 | Exists |
| `ShopWarnings.cs` | Exists jako część `ShopStatuses.cs` |
| `ShopOrderSync` | Exists |
| `IShopOrderLock` + `IShopOrderStore` | Exists jako jeden port `IShopOrderStore` |
| Kolumny, check constraints, `ShopDeletions`, migracja | Exists |
| Odczyty listy i szczegółów | Exists |
| `WooCommerceOptions` + walidator | Exists w `WooCommerceWebhook.cs` |
| Podpis, parser, handler, endpoint, limit | Exists |
| Frontend: modele, etykiety, lista, szczegóły | Exists |
| Dokumentacja | Exists |

## Database

SQL Server, `OrdersDbContext`, migracja `20261106103530_ShopOrderSync`:
- kolumny `ShopModifiedAtUtc`, `ShopDeletedAtUtc`, `ShopRefundedAmount` (domyślnie 0);
- tabela `orders.ShopDeletions`;
- check constraints `CK_Orders_ShopFields`, `CK_Orders_ShopRefundedAmount`.

Zmiana addytywna.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| `ShopStatusesTests`, `OrderApplyFromShopTests` | Exists jako `ShopSyncTests` | Passed (Orders.Tests 185/185) |
| `WebhookSignatureTests`, `WooCommerceOrderPayloadTests` | Exists jako `WooCommercePayloadTests` | Passed (12/12) |
| `WooCommerceWebhookTests` (utworzenie, duplikat, równoczesne, starsza, przed uruchomieniem, usunięcie, usunięcie nieznanego, równoczesne created+deleted, przywrócenie, zły podpis, brak sekretu, ping, nieczytelna, audyt, brak wpłat, `409` użytkownika, obniżenie wartości, zakres kwoty, ukryte statusy, zwrot) | Exists | 17/17 Passed; we wcześniejszym przebiegu 16/17 (1 niepowodzenie, test nieustalony), 3 kolejne przebiegi testów współbieżnych 7/7 |
| Usunięcie z wpłatą, logi bez danych przy udanej dostawie, `413`/`429`/`503`, walidator sekretu, `shopStatus` w audycie | Missing | — |
| `OrdersApiTests` rozszerzone | Missing (pokryte w `WooCommerceWebhookTests`) | — |
| `EndpointSecurityTests` | Exists (zmieniony) | Passed |
| frontend `shop-status.spec.ts`, `orders-list-page.spec.ts`, `order-details-page.spec.ts`, `orders-api.spec.ts` | Exists | Passed (398 testów, raport agenta frontendu) |

## Verification

| Command | Outcome |
|---|---|
| `dotnet build stone-flow.slnx` | Passed |
| `dotnet format stone-flow.slnx --verify-no-changes` | Passed |
| `dotnet test` Orders.Tests / Integrations.Tests / Architecture.Tests | Passed (185 / 12 / 56) |
| Testy integracyjne `WooCommerceWebhookTests` (LocalDB) | Passed 17/17 (wcześniej 1 niestabilne niepowodzenie) |
| Testy integracyjne `EndpointSecurityTests` | Passed |
| Testy integracyjne Orders/Audit (regresja) | Not run — przerwane przez system (brak pamięci); recenzent uruchomił 3 wybrane: Passed |
| `npm run lint`, `npm test -- --watch=false`, `npm run build` | Passed (raport agenta frontendu) |
| `dotnet build -c Release`, pełny `dotnet test`, `test-migrate.sh` | Not run (CI) |

## Deviations

- Przełącznik ukrytych statusów trzyma stan w filtrach `OrdersService` (jak pozostałe filtry listy), nie w URL.
- Porty `IShopOrderLock` i `IShopOrderStore` połączono w `IShopOrderStore`; `ShopWarning` jest w `ShopStatuses.cs`; opcje i walidator w `WooCommerceWebhook.cs`.
- `ShopOrderSyncResult.DuplicateRetry` z planu nazywa się `RetryRequired`. Wynik obejmuje naruszenie unikalności i konflikt wersji z zapisem użytkownika; synchronizacja ponawia raz w nowym zakresie (spec pkt 9 mówi „nie ponawia przy konflikcie wersji”).
- Konfiguracja sekretu: `StoneFlow:WooCommerce:WebhookSecret` (plan [N7]) zamiast `Integrations:WooCommerce:WebhookSecret` ze spec.
- Ostrzeżenia w UI w kolejności z planu (najpierw „usunięte w sklepie”).

## Additional Implementation

Additional implementation not covered by the original documents:
- `WooCommerceOrderPayload.MaxAmount`: kwota lub suma zwrotów powyżej zakresu `decimal(18,2)` daje treść nieczytelną (`200` + log).
- Sekret z samych białych znaków traktowany jako brak sekretu.
- Dokumentacja: zalecenie sekretu alfanumerycznego i limitu 300/min przy masowych zmianach w sklepie.

## Open Items

- **Human review required before merge** (ryzyko high).
- Pełna regresja testów integracyjnych (Orders, Audit) i `test-migrate.sh` — do potwierdzenia w CI.
- Jedno niepowtarzalne niepowodzenie w `WooCommerceWebhookTests` (test nieustalony; podejrzenie: limit 15 s blokady `sp_getapplock` pod obciążeniem LocalDB).
- Ponowny przegląd kodu po poprawkach nie został wykonany.
- Nierozwiązane z `05-code-review.md`:
  - brakujące testy: usunięcie z wpłatą, logi przy udanej dostawie, `413`/`429`/`503`, walidator sekretu, `shopStatus` w audycie;
  - kod `429` i treść odpowiedzi zależą od globalnego `OnRejected` z `AccessModule`;
  - `413` przy wyłączonej integracji zamiast `404`;
  - brak logu przy obcięciu tekstu;
  - lista statusów standardowych powielona w Integrations (granica modułów).
- Integracja wyłączona do próby na kopii sklepu (SF-2; `docs/wdrozenia.md` sekcja 12).
