# Code review — SF-12

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja realizuje specyfikację v2 z rozstrzygnięciami planu [N1]–[N14]. Podpis
  jest liczony na surowych bajtach i porównywany w czasie stałym, ping jest jedyną ścieżką bez podpisu
  i niczego nie zmienia. `sp_getapplock` działa w transakcji jednostki pracy na wspólnym
  `ModuleConnection` i szereguje dostawy jednego `ExternalId`, także `created` i `deleted` dla
  nieistniejącego wiersza. Reguły domeny (zakończone w sklepie, jednorazowe przejście, usunięcie,
  znacznik `ShopDeletions`, kolejność dostaw) są zgodne ze spec i planem, a audyt zamówień ręcznych
  ma ten sam kształt co przed zmianą. Nie znalazłem naruszenia niezmienników. Zostały: nieopisane
  odstępstwo przy konflikcie wersji z użytkownikiem, brak części testów z planu (w tym kryterium
  akceptacji o ostrzeżeniu obniżeń i testu `409`) oraz kilka drobnych luk wobec spec.
- Risk: high (wymagany przegląd przez człowieka przed merge)

## Project Invariant Violations

None.

Sprawdzenie po kolei:

| Invariant | Wynik | Uzasadnienie |
|---|---|---|
| 1. Sales separate from cash | OK | `Order.ApplyFromShop` (`Order.cs:232`) nie dotyka wpisów; `ShopOrderSync` nie woła portów wpłat ani zwrotów; zwrot w sklepie to tylko `ShopRefundedAmount`. Testy: `WooCommerceWebhookTests.cs:97`, `:254`. |
| 2. Backend decimal money | OK | `WooCommerceOrderPayload.ReadAmount` (`WooCommerceOrderPayload.cs:124-143`) parsuje `decimal` w `InvariantCulture`, odrzuca więcej niż 2 miejsca; kolumna `decimal(18,2)`. |
| 3. API-enforced access | OK | `ReceiveWebhook.cs:86` — `401` przed jakimkolwiek przetwarzaniem; ping (`:81`) bez skutków; endpoint w `EndpointSecurityTests.AllowedAnonymous`. Odczyty `/api/orders` bez zmian w autoryzacji. |
| 4. Retries never duplicate | OK | Blokada `ShopOrderStore.cs:20-34`, unikalność (`Source`, `ExternalId`) i `Number`, reguły kolejności `Order.cs:250-258`, przejście tylko aktywne → zakończone (`Order.cs:269`). Pola ERP nie są nadpisywane. |
| 5. Financial audit trail | OK | `ApplyShopOrder.cs:72`, `:107` — audyt w tej samej transakcji, autor `WooCommerceSync`; `IsPersonalData` dla pól klienta bez zmian (`OrderAudit.cs:82`). |
| 6. Secrets stay server-side | OK | Sekret tylko z opcji (`WooCommerceWebhook.cs:10-35`); logi (`WooCommerceWebhook.cs:71-90`) bez treści, bez podpisu i bez danych klienta; komunikat walidatora podaje nazwę klucza, nie wartość. |
| 7. Module and layer boundaries | OK | Integrations woła wyłącznie `IShopOrderSync` z Shared.Application (`WooCommerceWebhook.cs:46-50`); Orders.Domain bez zależności od EF/HTTP. Testy architektury przechodzą według autora (56/56). |
| 8. No identifying data in docs | OK | `docs/wdrozenia.md` sekcja 11 bez nazwy i domeny sklepu; dane testowe fikcyjne („Jan Testowy”, `example.com`). |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `api/src/Modules/Integrations/StoneFlow.Integrations.Application/WooCommerce/ReceiveWebhook.cs:66-67`
   — lista standardowych statusów WooCommerce powiela `ShopStatuses.Known`
   (`api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/ShopStatuses.cs:44-45`). To wiedza
   biznesowa, więc reguła DRY z `rules.md` („DRY applies to business knowledge and rules”) dotyczy jej
   wprost. Integrations nie może sięgać do Orders.Domain, więc lista należy do kontraktu w
   Shared.Application albo zostaje tylko po jednej stronie (np. log nieznanego statusu w Orders).
2. `api/src/Modules/Integrations/StoneFlow.Integrations.Api/WooCommerceWebhookEndpoint.cs:24-28`
   — kod `429` i treść odpowiedzi webhooka zależą od konfiguracji modułu Access
   (`AccessModule.cs:38-39`: `RejectionStatusCode = 429` i globalny `OnRejected`). Domyślny kod
   odrzucenia w ASP.NET Core to `503`. Webhook dostaje więc treść Problem Details logowania
   („Zbyt wiele prób. Spróbuj ponownie za chwilę.”), choć plan [N14] mówi o odpowiedziach bez treści.
   To ukryte powiązanie dwóch modułów, nieopisane w `conventions.md`.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Endpoint `POST /webhooks/woocommerce/orders` anonimowy, poza `/api`, bez CSRF | Covered | `WooCommerceWebhookEndpoint.cs:30-35`; wyjątek opisany w `conventions.md`. |
| Brak sekretu → `404`; sekret < 16 znaków → błąd startu | Covered | `ReceiveWebhook.cs:72-75`; `WooCommerceWebhook.cs:20-30` + `ValidateOnStart`. Brak testu walidatora (Test Gaps). |
| Podpis HMAC-SHA256 Base64 na surowych bajtach, porównanie w czasie stałym | Covered | `WebhookSignature.cs:12-29` (`FixedTimeEquals`, zły Base64 → false). |
| `401` z logiem ID dostawy (≤ 64 znaki), bez treści i podpisu | Covered | `ReceiveWebhook.cs:77`, `:139-144`; `WooCommerceWebhook.cs:71-72`. |
| Limit 1 MB → `413` (także przy chunked) | Covered | `WooCommerceWebhookEndpoint.cs:39-62` — limit funkcji Kestrel, `Content-Length` i odczyt z przerwaniem po przekroczeniu. Brak testu. |
| Limit 300/min/IP → `429`, polityka w Integrations | Covered | `WooCommerceWebhookEndpoint.cs:24-28`; zob. Conventions 2. Brak testu. |
| Ping bez podpisu → `200` bez działania | Covered | `ReceiveWebhook.cs:81-84`, `:137`, `:146`; test `WooCommerceWebhookTests.cs:220`. |
| Tematy created/updated/restored/deleted; inny → `200` + log | Covered | `ReceiveWebhook.cs:92-96`, `:128-135`. |
| Nieczytelna treść / brak pól wymaganych → `200` + log bez danych klienta | Covered | `WooCommerceOrderPayload.cs:23-71`. |
| Błąd bazy → `503` | Covered | `ReceiveWebhook.cs:115-125`. Brak testu. |
| Mapowanie payloadu (data Warszawa, wartość, zwroty, klient, NIP, „Klient sklepu #{id}”) | Covered | `WooCommerceOrderPayload.cs:67`, `:146-191`; `ApplyShopOrder.cs:138-150`. |
| Obcinanie tekstów z logiem informacyjnym o obcięciu | Partial | Obcinanie jest (`ApplyShopOrder.cs:152-161`), logu o obcięciu brak. |
| Waluta ≠ PLN i nieznany status → log | Covered | `ReceiveWebhook.cs:105-113`. |
| Blokada po `ExternalId` w jednostce pracy | Covered | `ShopOrderStore.cs:20-34` (`sp_getapplock`, `LockOwner = 'Transaction'`), polecenie dołączane do transakcji przez `ModuleTransactionInterceptor`. |
| Usunięcie nieznanego → znacznik `ShopDeletions` (ponowienie nie zmienia czasu) | Covered | `ApplyShopOrder.cs:45-50`; `ShopOrderStore.cs:46-63`. |
| Zamówienie sprzed uruchomienia → brak rekordu i błędu | Covered | `ApplyShopOrder.cs:52-55`; test `:126`. |
| Utworzenie `WC-{id}`, kanał „sklep”, „Anulowane” gdy zakończone w sklepie, znacznik → zamówienie usunięte | Covered | `Order.cs:202-219`; `ApplyShopOrder.cs:57-65` (`restored` też podlega porównaniu dat — zgodnie z [N1]). |
| Równoczesne utworzenie → ponowienie raz w nowym zakresie DI | Covered | `WooCommerceWebhook.cs:38-51`; test `WooCommerceWebhookTests.cs:101`. |
| Kolejność dostaw: starsza, sprzed usunięcia, równa data, `order.deleted` | Covered | `Order.cs:243-258`. |
| `ShopModifiedAtUtc` zapisywane zawsze, bez audytu | Covered | `Order.cs:261`, `:275`; `ApplyShopOrder.cs:89-105`. |
| Jednorazowe przejście na „Anulowane” | Covered | `Order.cs:239`, `:269-272`; testy jednostkowe `ShopSyncTests.cs:96-138`. |
| Przywrócenie w sklepie nie zmienia statusu realizacji | Covered | `ShopSyncTests.cs:156`. Brak testu integracyjnego `order.restored`. |
| Audyt `Created`/`Updated` z autorem `WooCommerceSync`, nowe `OrderField`, `IsPersonalData` | Covered | `OrderVocabulary.cs:84-91`, `Order.cs:375-377`, `OrderAudit.cs:27-57`. Kształt audytu zamówień ręcznych bez zmian (nowe pola mają `null` i są pomijane; test `ShopSyncTests.cs:199`). |
| Zapis równoczesny z użytkownikiem: `409` dla użytkownika, synchronizacja nie ponawia | Partial | Synchronizacja ponawia przy konflikcie wersji (Correctness 1). |
| Lista: domyślnie ukryte pending/failed/szkic, `includeHiddenShopStatuses`, `shopStatus` | Covered | `ListOrders.cs:222-235`, `OrderReader.cs:163-167` (warunek `ShopStatus IS NULL OR NOT IN`), `OrderContracts.cs:59`. |
| UI listy: przełącznik, druga etykieta statusu sklepu, polskie etykiety | Covered | `orders-list-page.html:136-146`, `:169-176`; `shop-status.ts`. Stan filtra nie jest w adresie strony, ale inne filtry też nie są (spec mówi „jak inne filtry”). |
| Szczegóły: `shopDeletedAtUtc`, `shopRefundedAmount`, `shopModifiedAtUtc`, `shopWarnings` | Covered | `GetOrder.cs:72-83`, `OrderContracts.cs:103-106`, `:253-256`, `ShopStatuses.cs:59-95`. |
| UI szczegółów: ostrzeżenia `role="status"`, wzorzec SF-10, status sklepu z etykietą | Covered | `order-details-page.html:53-64`, `:173`. Kolejność według planu (Plan Deviations 3). |
| Obniżenie wartości poniżej sumy obniżeń → istniejące ostrzeżenie SF-10 | Covered (bez testu) | Ostrzeżenie liczone przy odczycie (`SalesReductionRules`), a synchronizacja zmienia `Value`. Test, który miał to sprawdzać, tego nie robi (Test Gaps 1). |
| Migracja addytywna `ShopOrderSync`, check constraints, `ShopDeletions` | Covered | `20261106103530_ShopOrderSync.cs` — tylko dodanie kolumn (domyślnie `0`), tabeli i ograniczeń; istniejące wiersze je spełniają. |
| Dokumentacja konfiguracji webhooka | Covered | `docs/wdrozenia.md` sekcje 11–12, `deploy/api.env.example`, `conventions.md`. Brak zalecenia sekretu alfanumerycznego (przegląd spec, uwaga 8). |

## Plan Deviations

1. Pliki `ShopWarnings.cs` (Orders.Domain) i `WooCommerceOptions.cs` (Integrations.Infrastructure) z
   sekcji 3 planu nie powstały. `ShopWarning` jest w `ShopStatuses.cs:13-26`, a `WooCommerceOptions`
   z walidatorem w `WooCommerceWebhook.cs:10-30`.
2. `IShopOrderLock` i `IShopOrderStore` z planu (krok 4–5, Files) połączono w jeden port
   `IShopOrderStore` z `LockAsync` (`Ports.cs:129-144`).
3. Kolejność ostrzeżeń: plan (krok 8) — `deletedInShop`, `activeInShop`, `cancelledInShop`,
   `refundedInShop`; tabela w spec („w kolejności tabeli”) — `active_in_shop` pierwsze. Kod idzie za
   planem (`ShopStatuses.cs:74-92`, `order-details-page.ts:34-39`). To rozbieżność spec i planu,
   nie kodu z planem.
4. Konflikt wersji z zapisem użytkownika jest mapowany na `DuplicateRetry` i ponawiany
   (`ApplyShopOrder.cs:94-100`). Plan (sekcja 6: „istniejące `409`”) i spec (pkt 9: „Synchronizacja nie
   ponawia przy konflikcie wersji”) tego nie przewidują.
5. `OrdersApiTests` nie zostały rozszerzone o listę z ukrytymi i `shopWarnings` (plan sekcja 8).
   Część tych przypadków sprawdzają `WooCommerceWebhookTests`.
6. Brak testów nazwanych w planie: `Webhook_logs_contain_no_payload_or_signature`, równoczesne
   `created` + `deleted`, `409` dla użytkownika po zapisie synchronizacji [N14] (Test Gaps).

## Null Safety Issues

None. Nullowalne pola sklepu są obsłużone (`Order.cs:243-258`, `OrderReader.cs:148`); `ToData`
(`ApplyShopOrder.cs:120-135`) sprawdza pola wymagane przed `!`; `FindAsync` zwraca `null` dla
nieistniejącego zamówienia.

## Correctness Issues

1. **Konflikt wersji z użytkownikiem jest ponawiany, a nazwa wyniku temu przeczy** —
   `api/src/Modules/Orders/StoneFlow.Orders.Application/ShopSync/ApplyShopOrder.cs:94-100`. Blokada
   aplikacyjna `orders:shop:{id}` nie blokuje wiersza dla edycji użytkownika (ta używa wersji
   i `IOrderLock`), więc zapis użytkownika między `FindAsync` a `SaveAsync` daje `Conflict`. Kod zwraca
   wtedy `DuplicateRetry` (opisany w kontrakcie jako „naruszenie unikalności”,
   `ShopOrderSync.cs:69-73`), a runner ponawia w nowym zakresie. Drugi konflikt z rzędu kończy się
   `503`. Zachowanie jest bezpieczne (nic nie zostaje nadpisane, ponowienie widzi świeże dane), ale:
   (a) przeczy spec pkt 9 i nie jest zapisane w planie ani w Assumptions; (b) nazwa i dokumentacja
   `DuplicateRetry` wprowadzają w błąd; (c) ścieżka nie ma testu. Trzeba albo dopisać odstępstwo do
   spec/manifestu i wprowadzić osobny wynik (np. `ConflictRetry`), albo zrobić to, co mówi spec.
2. **Wartość ponad zakres `decimal(18,2)` daje `503` w pętli zamiast `200` + log** —
   `api/src/Modules/Integrations/StoneFlow.Integrations.Application/WooCommerce/WooCommerceOrderPayload.cs:124-143`
   nie sprawdza górnej granicy (`Order.MaxValue`), a `Order.ApplyFromShop`
   (`api/src/Modules/Orders/StoneFlow.Orders.Domain/Orders/Order.cs:263`) przypisuje `Value` bez
   kontroli zakresu, którą robią `Require` (`Order.cs:391`) i `Edit` (`Order.cs:306`). Przy imporcie
   poleci `ArgumentException`, przy aktualizacji przepełnienie SQL. W obu przypadkach handler zwraca
   `503` (`ReceiveWebhook.cs:121-125`), a kolejne dostawy tego zamówienia mogą wyłączyć webhook w
   WooCommerce. Spec (Edge Cases: „nieczytelne `total` → treść odrzucona jako nieczytelna”) wymaga
   `200` + log. To samo dotyczy sumy `refunds[].total`. Przypadek mało realny, ale domena dopuszcza
   stan, który odrzuca przy innych operacjach.
3. **Limit 300/min/IP obejmuje sam sklep** —
   `api/src/Modules/Integrations/StoneFlow.Integrations.Api/WooCommerceWebhookEndpoint.cs:26-28`.
   Wszystkie prawdziwe dostawy przychodzą z jednego adresu sklepu. W `Program.cs` nie ma
   `UseForwardedHeaders`, więc za proxy partycja jest jedna dla wszystkich (komentarz w
   `AccessModule.cs:53-55`). Masowa zmiana statusów w panelu sklepu może przekroczyć limit, a `429`
   liczy się do wyłączenia webhooka. Limit pochodzi ze spec, więc to ryzyko, nie błąd kodu, ale warto
   je dopisać do sekcji 11 `docs/wdrozenia.md` obok opisu `429`.
4. Drobne:
   - `WooCommerceWebhook.cs:34` — sekret złożony z samych spacji (≥ 16) przechodzi walidację i
     włącza integrację (`IsNullOrEmpty` zamiast `IsNullOrWhiteSpace`).
   - `WooCommerceWebhookEndpoint.cs:44-62` — przy wyłączonej integracji duża treść dostaje `413`
     zamiast `404` (sprawdzenie rozmiaru przed sprawdzeniem sekretu). Bez wpływu na bezpieczeństwo.

## Test Gaps

1. `api/tests/StoneFlow.Api.IntegrationTests/Orders/WooCommerceWebhookTests.cs:243` —
   `Refund_in_shop_is_information_and_value_drop_keeps_reduction_warning_logic` nie zmienia wartości
   poniżej sumy obniżeń ani nie sprawdza `salesReductions.excess`. Kryterium akceptacji „Obniżenie
   wartości poniżej sumy obniżeń sprzedaży pokazuje ostrzeżenie” nie ma testu, a nazwa testu sugeruje
   coś innego.
2. Brak testu `409` dla użytkownika, gdy synchronizacja zapisze zamówienie w trakcie edycji (spec
   Edge Cases, plan [N14], priorytet testów 3 „concurrent edits”). Brak testu ponowienia przy
   konflikcie wersji (Correctness 1).
3. Brak testu równoczesnych `order.created` i `order.deleted` dla tego samego ID (plan sekcja 8,
   [N4]). Właśnie tę ścieżkę wprowadzono `sp_getapplock`.
4. Brak testu integracyjnego `order.restored` (spec Verification: „przywrócenie”) i usunięcia
   zamówienia z wpłatą („wpłaty pozostają”, „Do zwrotu”).
5. Logi: `WooCommerceWebhookTests.cs:207` sprawdza tylko brak „Testowy” przy złym podpisie. Nie ma
   sprawdzenia, że w logach z udanej dostawy nie ma danych klienta ani wartości nagłówka podpisu
   (plan: `Webhook_logs_contain_no_payload_or_signature`, niezmiennik 6).
6. Brak testów `413` (w tym bez `Content-Length`), `429` i `503` (np. przez `StoreHooks` albo
   podmieniony `IShopOrderSync`).
7. Brak testu walidatora sekretu (< 16 znaków → błąd startu) i testu `includeHiddenShopStatuses`
   z niepoprawną wartością (`400`, `ListOrders.cs:231-233`).
8. Audyt: testy liczą tylko wpisy `WooCommerceSync` (`WooCommerceWebhookTests.cs:74-75`). Nic nie
   sprawdza, że zmiana statusu ze sklepu daje pole `shopStatus` w audycie (kryterium „także zmiana
   statusu ze sklepu”) ani flag `IsPersonalData` pól klienta w imporcie.
9. Brak testów jednostkowych `ReceiveWooCommerceWebhookHandler` (kolejność: wyłączona → ping →
   podpis → temat → treść) i `ShopOrderSyncRunner` (dokładnie jedno ponowienie). Część pokrywają
   testy integracyjne.
10. Regresja: testy integracyjne Orders/Audit nie zostały ukończone przez autora (brak pamięci).
    W tym przeglądzie uruchomiłem tylko trzy (`Shop_order_rejects_changes_of_shop_owned_fields`,
    `List_filters_sorts_and_pages_in_the_database`, `Audit_failure_rolls_back_edit_cancel_and_restore`
    — 3/3 OK, `--no-build`). Pełny przebieg tych przestrzeni (np. w CI) jest warunkiem merge.

## Code Smells

1. `ShopOrderSyncResult.DuplicateRetry` ma dwa znaczenia: naruszenie unikalności i konflikt wersji
   (`ApplyShopOrder.cs:97-100`); zob. Correctness 1.
2. `ShopStatuses.cs` zawiera poza statusami także `ShopDelivery`, `ShopWarning`, `ShopOrderData`,
   `ShopDeliverySkip` i `ShopApplyResult`. Nazwa pliku nie mówi, co w nim jest; plan przewidywał
   osobny `ShopWarnings.cs`.
3. `WooCommerceWebhook.cs` łączy opcje, walidator, ustawienia, runner i logi (5 typów). Podobnie jak
   wyżej — różne odpowiedzialności w jednym pliku.
4. Powielona lista statusów standardowych (Conventions 1).

## Recommendation

- Fix critical issues before merge — krytycznych nie ma; przed merge warto: (1) rozstrzygnąć
  Correctness 1 (zapis odstępstwa albo zmiana zachowania i nazwy wyniku), (2) dodać testy z Test Gaps
  1–4, (3) uruchomić pełne testy integracyjne Orders/Audit. Pozostałe uwagi można przenieść do
  manifestu jako otwarte.
- **Ryzyko high: przed merge wymagany przegląd przez człowieka**, niezależnie od wyniku. Integracja
  pozostaje wyłączona (brak sekretu) do próby na sklepie z SF-2.
