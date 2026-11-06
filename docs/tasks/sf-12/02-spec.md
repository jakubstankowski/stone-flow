# Task Specification — SF-12

> Wersja 2 (2026-11-06), po przeglądzie `03-spec-review-v1.md` (FAIL). Zmiany:
> - stan „zakończone w sklepie” i jednorazowe przejście liczone z `ShopStatus` oraz `ShopDeletedAtUtc` (Critical 1);
> - kolejność dostaw przy usunięciu i znacznik usunięcia nieznanego zamówienia (Critical 2);
> - ping bez podpisu, ponowienie w nowym zakresie DI, status sklepu na liście z polskimi etykietami;
> - zapis `ShopModifiedAtUtc` zawsze; rezygnacja z `cancelled_with_payments` na rzecz istniejącego „Do zwrotu”;
> - limit żądań, pola wymagane, logi.

## Source

[SF-12](https://standev.youtrack.cloud/issue/SF-12)

## Goal

Przyjmować webhooki zamówień WooCommerce i tworzyć lub aktualizować zamówienia ERP ze źródłem
`WooCommerce`.

- **Autentyczność:** backend weryfikuje podpis HMAC-SHA256 (Base64) liczony na surowych bajtach treści. Sekret jest tylko w konfiguracji serwera.
- **Identyfikacja:** zamówienie identyfikuje para źródło + zewnętrzne ID, z istniejącym ograniczeniem unikalności. Wielokrotne dostarczenie daje jeden rekord.
- **Własność pól:** synchronizacja zmienia tylko pola należące do sklepu (data, wartość, dane klienta, status ze sklepu, oznaczenie usunięcia, informacja o zwrocie). Pól ERP nie zmienia, z jednym wyjątkiem: jednorazowe przejście na „Anulowane”.
- **Pomijanie:** dostawy starsze od zapisanych danych (według daty modyfikacji w sklepie) i zamówienia sprzed daty uruchomienia są pomijane.
- **Statusy pending, failed i szkice:** są oznaczone, domyślnie ukryte na liście i dostępne filtrem.
- **Ostrzeżenia w szczegółach zamówienia:** „Zamówienie aktywne w sklepie”, usunięcie w sklepie, zwrot w sklepie oraz wpłaty przy anulowanym zamówieniu.
- **Audyt:** każda zmiana ma autora „Synchronizacja WooCommerce”.

Integracja nie tworzy wpłat ani wypływów i niczego nie zapisuje w sklepie.

## Context

- PRD sekcja 5 „Zamówienie WooCommerce” (własność pól, jednorazowe przejście na „Anulowane”,
  usunięcie, przywrócenie, ostrzeżenie „Zamówienie aktywne w sklepie”, status przy imporcie, zwrot
  w sklepie jako informacja, tylko PLN), sekcja 6: ZAM-02, ZAM-06, ZAM-08; sekcja 10 „Integracje”.
- SF-2 (`docs/tasks/sf-2/01-weryfikacja.md`): weryfikacja niezakończona; potwierdzone w panelu:
  WooCommerce 11.0.1, HPOS, strefa Warszawa, PLN, pole NIP z meta `_billing_billing_nip`
  (nie potwierdzono w REST). Podpis według dokumentacji: nagłówek `X-WC-Webhook-Signature`,
  Base64 HMAC-SHA256 payloadu. Niepotwierdzone: payload usunięcia, kolejność zdarzeń, statusy
  niestandardowe, zachowanie przy błędach odbiorcy.
- SF-8: encja `Order` ma już `Source`, `ExternalId`, `ShopStatus`, `Order.CreateFromShop`, regułę
  własności pól sklepu w edycji użytkownika i unikalny indeks (`Source`, `ExternalId`).
- SF-10: ostrzeżenie „Obniżenia sprzedaży przekraczają wartość o X zł” (`SalesReductionRules`) jest
  liczone przy odczycie szczegółów, więc obejmuje też obniżenie wartości przez synchronizację.
- SF-7: `IAuditLog` z autorem `AuditAuthor` `WooCommerceSync`.
- Warstwy i moduły:
  - **Integrations** (`StoneFlow.Integrations.*`) — endpoint webhooka (Api), weryfikacja podpisu,
    odczyt i mapowanie payloadu na snapshot zamówienia (Application/Infrastructure), opcje i sekret
    (Infrastructure). Moduł nie dotyka tabel zamówień.
  - **Shared.Application** — kontrakt `IShopOrderSync` i typ `ShopOrderSnapshot` (kontrakt używany
    przez dwa moduły).
  - **Orders** — implementacja `IShopOrderSync` (Application): reguły zastosowania snapshotu
    (Domain: `Order.ApplyFromShop`), nowe pola encji i migracja (Infrastructure), filtr listy
    i ostrzeżenia w szczegółach (Application/Api).
  - **Frontend** `app/src/app/features/orders` — filtr na liście, ostrzeżenia i informacje ze sklepu
    w szczegółach.

## Scope

### In scope

- `POST /webhooks/woocommerce/orders` — anonimowy, poza `/api` (bez CSRF i ciasteczek), weryfikacja
  podpisu, obsługa tematów `order.created`, `order.updated`, `order.deleted`, `order.restored`
  i pingu przy tworzeniu webhooka.
- Mapowanie payloadu na `ShopOrderSnapshot`.
- Tworzenie i aktualizacja zamówienia `WooCommerce` z regułami własności pól, kolejności dostaw,
  daty uruchomienia, statusu przy imporcie, jednorazowego przejścia na „Anulowane”, usunięcia
  i przywrócenia w sklepie.
- Nowe pola zamówienia: data modyfikacji w sklepie, oznaczenie usunięcia w sklepie, kwota zwrotów
  w sklepie (informacja).
- Audyt zmian z autorem „Synchronizacja WooCommerce”.
- Lista: statusy pending, failed i szkic domyślnie ukryte, filtr je pokazuje.
- Szczegóły: ostrzeżenia i informacje ze sklepu (API i UI).
- Numer ERP zamówienia ze sklepu.
- Testy jednostkowe, integracyjne i frontendu; dokumentacja konfiguracji webhooka.

### Out of scope

- Dzienne uzupełnianie przez REST API, „Synchronizuj teraz”, „Ponów” i historia błędów (SF-13).
- Stan integracji w UI (SF-14) i reguły raportowe sprzedaży (SF-19) — SF-12 tylko zapisuje status
  ze sklepu i udostępnia regułę „liczy się do sprzedaży” w domenie.
- Zapis czegokolwiek w sklepie; klucze REST API.
- Waluty inne niż PLN (sklep sprzedaje wyłącznie w PLN — potwierdzone przez właściciela).
- Anonimizacja danych klienta (SF-22).
- Próby na prawdziwym sklepie (SF-2) — ręczny krok przed włączeniem integracji.

## Behavior

### Przyjęcie webhooka

1. Integracja jest włączona, gdy w konfiguracji jest `Integrations:WooCommerce:WebhookSecret`
   (co najmniej 16 znaków). Bez sekretu endpoint odpowiada `404` i niczego nie przetwarza; start
   aplikacji nie jest blokowany.
2. Backend czyta surowe bajty treści (limit 1 MB; większa treść → `413`) i porównuje w czasie
   stałym `X-WC-Webhook-Signature` z Base64(HMAC-SHA256(sekret, bajty)). Brak lub zły podpis →
   `401` bez treści szczegółów; log ostrzeżenia z identyfikatorem dostawy
   (`X-WC-Webhook-Delivery-ID`, obcięty do 64 znaków), bez treści payloadu i bez nagłówka podpisu.
   Limit żądań na adres IP: 300 na minutę (`429` ponad limit), polityka rejestrowana w module
   Integrations. Sekret obecny, ale krótszy niż 16 znaków → start aplikacji zatrzymany (błąd
   konfiguracji).
3. Ping przy tworzeniu webhooka: treść dokładnie `webhook_id=<1–20 cyfr>` i brak nagłówka tematu →
   `200` bez działania, także bez podpisu (WooCommerce może wysyłać ping niepodpisany). Ping niczego
   nie zmienia, więc nie wymaga uwierzytelnienia. Każda inna treść wymaga poprawnego podpisu
   (pkt 2).
4. Temat z nagłówka `X-WC-Webhook-Topic`:
   - `order.created`, `order.updated`, `order.restored` → pełne zamówienie w treści JSON;
   - `order.deleted` → treść z `id` (pozostałe pola mogą nie wystąpić);
   - inny temat → `200` bez działania, log informacyjny.
5. Treść, której nie da się odczytać jako zamówienia (zły JSON, brak `id`, brak lub pusta wartość
   któregokolwiek z pól wymaganych dla tematów z pełnym zamówieniem: `id`, `status`,
   `date_created_gmt`, `date_modified_gmt`, `total`; np. szkic `checkout-draft` bez daty) → `200` bez zmian i log ostrzeżenia z tematem, ID dostawy
   i ID zamówienia, jeśli jest (bez danych klienta). Odpowiedź `200`, a nie `4xx`, bo WooCommerce
   wyłącza webhook po kilku kolejnych niepowodzeniach; brakujące zmiany uzupełni SF-13.
6. Błąd bazy przy zapisie → `503`; WooCommerce może ponowić dostawę, ponowienie jest idempotentne.
7. Poprawnie przetworzona dostawa (utworzona, zmieniona, pominięta jako starsza lub sprzed
   uruchomienia, bez zmian) → `200`.

### Mapowanie payloadu (`ShopOrderSnapshot`)

| Pole snapshotu | Źródło w payloadzie |
|---|---|
| `ExternalId` | `id` (liczba całkowita dodatnia, zapisywana jako tekst) |
| `ModifiedAtUtc` | `date_modified_gmt` (UTC) |
| `OrderDate` | `date_created_gmt` → data w Europe/Warsaw |
| `Status` | `status` (tekst, np. `processing`, `pending`, `cancelled`, `trash`, `checkout-draft`) |
| `Value` | `total` (decimal, kropka dziesiętna, 2 miejsca) |
| `RefundedAmount` | suma wartości bezwzględnych `refunds[].total`; brak → 0 |
| Klient — rodzaj | `Company`, gdy `billing.company` niepuste, inaczej `Person` |
| Klient — nazwa | `billing.company` dla firmy, inaczej `billing.first_name` + `billing.last_name` |
| Klient — telefon, e-mail | `billing.phone`, `billing.email` |
| Klient — adres | `billing.address_1` (+ `address_2`), `billing.postcode`, `billing.city` |
| Klient — NIP | `meta_data[]` o kluczu `_billing_billing_nip`, jeśli jest i niepusty |

- Puste napisy → brak wartości. Teksty są obcinane do długości kolumn ERP (log informacyjny
  o obcięciu bez treści).
- `currency` nie jest interpretowane (wyłącznie PLN); wartość inna niż `PLN` daje tylko log
  ostrzeżenia (bezpiecznik diagnostyczny), import przebiega normalnie.
- Status spoza standardowych (`pending`, `processing`, `on-hold`, `completed`, `cancelled`,
  `refunded`, `failed`, `trash`, `checkout-draft`) → log informacyjny (D-02: sklep może używać
  statusów niestandardowych).
- NIP z meta: usunięte spacje i myślniki; bez walidacji sumy kontrolnej (dane sklepu przyjmuje się
  jak są).
- `order.deleted` daje snapshot z samym `ExternalId` i znacznikiem usunięcia.

### Zastosowanie snapshotu (Orders, jedna jednostka pracy)

Pojęcia:
- **zakończone w sklepie** = `ShopDeletedAtUtc` ustawione **albo** `ShopStatus` ∈ {`cancelled`, `trash`};
- **aktywne w sklepie** = nie zakończone w sklepie.

1. Zamówienie wyszukiwane po (`WooCommerce`, `ExternalId`) i blokowane do końca jednostki pracy
   (`UPDLOCK`), tak jak wpisy w SF-9.
2. **Brak zamówienia:**
   - `order.deleted` → zapis znacznika usunięcia `orders.ShopDeletions` (`ExternalId`,
     `ReceivedAtUtc`; jeden wiersz na ID, ponowienie nie zmienia czasu); brak zamówienia i błędu;
   - `OrderDate` < data uruchomienia (`ILaunchDate`) → nic (brak rekordu, brak błędu, log
     informacyjny bez danych klienta);
   - w przeciwnym razie nowe zamówienie (`Order.CreateFromShop`): numer `WC-{ExternalId}`,
     kanał „sklep”, `ShopModifiedAtUtc` z dostawy. Jeśli istnieje znacznik usunięcia,
     a dostawa nie jest `order.restored` i jej `ModifiedAtUtc` ≤ `ReceivedAtUtc` znacznika, zamówienie
     powstaje jako usunięte (`ShopDeletedAtUtc` = `ReceivedAtUtc`). W przeciwnym razie znacznik
     jest usuwany. Status realizacji to „Anulowane”, gdy zamówienie jest zakończone w sklepie,
     inaczej „W trakcie”. Audyt `Created` z autorem `WooCommerceSync`.
   - Równoczesne utworzenie przez dwie dostawy: naruszenie unikalności (`Source`, `ExternalId`
     albo `Number`) → jednostka pracy wycofana; moduł Integrations ponawia zastosowanie snapshotu
     **jeden raz w nowym zakresie DI** (nowa jednostka pracy), gdzie trafia na istniejące zamówienie.
3. **Istniejące zamówienie** — kolejność dostaw:
   - dostawa z `ModifiedAtUtc` < zapisane `ShopModifiedAtUtc` → nic (starsza);
   - zamówienie usunięte (`ShopDeletedAtUtc` ustawione) i dostawa `order.created`/`order.updated`
     z `ModifiedAtUtc` ≤ `ShopDeletedAtUtc` → nic (spóźniona dostawa sprzed usunięcia). Dostawa
     z późniejszą datą modyfikacji albo `order.restored` jest stosowana i czyści `ShopDeletedAtUtc`;
   - `order.deleted` (bez daty modyfikacji): ustawia `ShopDeletedAtUtc` = teraz, jeśli puste;
     ponowna dostawa nic nie zmienia; nie zmienia innych pól ani `ShopModifiedAtUtc`;
   - dostawa równa zapisanej dacie → stosowana (idempotentna; brak zmian → brak audytu).
4. **Zastosowanie:** pola sklepu (data, wartość, klient), `ShopStatus`, `ShopRefundedAmount`
   i `ShopModifiedAtUtc` są nadpisywane wartościami snapshotu. `ShopModifiedAtUtc` jest
   zapisywane zawsze, gdy rośnie, także bez innych zmian. Nie jest audytowane, bo to znacznik
   techniczny. Taki zapis zmienia `rowversion`.
5. **Jednorazowe przejście na „Anulowane”:** gdy przed dostawą zamówienie było aktywne w sklepie,
   a po niej jest zakończone w sklepie, status realizacji ERP staje się „Anulowane” (jeśli jeszcze
   nie jest). Inne przypadki nie zmieniają statusu realizacji:
   - ponowna dostawa tego samego statusu;
   - drugie `order.deleted`;
   - `cancelled` po `trash`;
   - przywrócenie w sklepie;
   - zmiana między statusami aktywnymi.
6. **Przywrócenie w sklepie** (`order.restored` albo nowsza dostawa ze statusem aktywnym) zamówienia
   anulowanego w ERP aktualizuje pola sklepu, ale nie zmienia statusu realizacji.
7. Wpłaty, zwroty, korekty, dane operacyjne, komentarz i status realizacji (poza pkt 5) nie są
   zmieniane. Integracja nie tworzy wpłat ani wypływów; zwrot w sklepie zapisuje tylko kwotę
   informacyjną.
8. Audyt `Updated` z autorem `WooCommerceSync`. Zmienione pola zamówienia (`OrderField`)
   rozszerzone o `ShopStatus`, `ShopDeleted` (tak/nie) i `ShopRefundedAmount`. Dane klienta mają
   `IsPersonalData`. Brak zmian audytowanych → brak audytu, nawet jeśli zapisano
   `ShopModifiedAtUtc`.
9. **Zapis równoczesny z użytkownikiem:** synchronizacja blokuje wiersz i zapisuje nową wersję
   (`rowversion`). Zapis użytkownika z wcześniejszą wersją dostaje istniejący konflikt `409`
   i użytkownik ponawia na świeżych danych; zdarza się to tylko przy dostawie w trakcie edycji.
   Synchronizacja nie ponawia przy konflikcie wersji, bo trzyma blokadę.

### Reguły w domenie

- `ShopStatuses` (Orders.Domain): `IsTerminal(status, deleted)` (zakończone w sklepie),
  `IsHiddenByDefault(status)` = `pending`, `failed`, `checkout-draft`; nieznany status traktowany
  jak aktywny i nieukryty.
- `Order.ApplyFromShop(snapshot, nowUtc)` zwraca listę zmian (jak `Edit`) albo wynik pominięcia
  i wykonuje reguły z pkt 3–8.
- Edycja użytkownika (`Edit`) nie zmienia nowych pól sklepu.
- Reguła „liczy się do sprzedaży” należy do SF-19 (nie w SF-12).

### Lista zamówień

- Domyślnie lista nie pokazuje zamówień ze sklepu ze statusem pending, failed lub
  `checkout-draft`. Parametr `includeHiddenShopStatuses=true` je pokazuje.
- UI: przełącznik „Pokaż oczekujące, nieudane i szkice ze sklepu” w filtrach listy; stan w adresie
  jak inne filtry. Pusta lista przy wyłączonym przełączniku pokazuje istniejący stan pustki bez
  zmian.
- `OrderListItemDto` dostaje pole `shopStatus` (addytywnie). Kolumna statusu realizacji pokazuje
  pod nim drugą, mniejszą etykietę ze statusem sklepu dla zamówień `WooCommerce`.
- Polskie etykiety statusów sklepu (frontend): `pending` „Oczekuje na płatność”, `processing`
  „W realizacji”, `on-hold` „Wstrzymane”, `completed` „Zrealizowane”, `cancelled` „Anulowane
  w sklepie”, `refunded` „Zwrócone”, `failed` „Nieudane”, `trash` „W koszu”, `checkout-draft`
  „Szkic”; nieznany → surowy kod. Te same etykiety w szczegółach.

### Szczegóły zamówienia

Nowe pola w `OrderDetailsDto` i odpowiedzi API: `shopDeletedAtUtc`, `shopRefundedAmount`,
`shopModifiedAtUtc` oraz wyliczone `shopWarnings` (lista kodów):

| Kod | Warunek | Tekst w UI |
|---|---|---|
| `active_in_shop` | źródło `WooCommerce`, ERP „Anulowane”, status sklepu nie anulowany, nieusunięte | „Zamówienie aktywne w sklepie” — ERP nie zmienia niczego w sklepie |
| `deleted_in_shop` | `ShopDeletedAtUtc` ustawione | „Zamówienie usunięte w sklepie” |
| `cancelled_in_shop` | ERP nie „Anulowane”, a status sklepu anulowany lub usunięty (np. po przywróceniu w ERP) | „Zamówienie anulowane w sklepie” |
| `refunded_in_shop` | `ShopRefundedAmount` > 0 | „Zwrot w sklepie: X zł — informacja, nie tworzy wypływu” |

- Ostrzeżenia mają ten sam wzorzec wizualny co istniejące ostrzeżenie o obniżeniach sprzedaży
  (SF-10, `sales-corrections.html`), nad sekcją danych, w kolejności tabeli; `role="status"`.
- Wpłaty przy anulowanym zamówieniu pokazuje już istniejąca informacja „Do zwrotu X zł” w sekcji
  wpłat (DESIGN-GUID, SF-9); SF-12 jej nie dubluje.
- Istniejące ostrzeżenie o obniżeniach sprzedaży (SF-10) pozostaje bez zmian.
- Anulowanie i przywrócenie w ERP działają jak dotąd (SF-8); po akcji szczegóły odświeżają
  ostrzeżenia. ERP niczego nie wysyła do sklepu.

## Data / API

### Storage

Orders (`orders.Orders`), migracja addytywna `ShopOrderSync`:

| Kolumna | Typ | Uwagi |
|---|---|---|
| `ShopModifiedAtUtc` | `datetime2` null | data modyfikacji w sklepie z ostatnio zastosowanej dostawy |
| `ShopDeletedAtUtc` | `datetime2` null | oznaczenie usunięcia w sklepie |
| `ShopRefundedAmount` | `decimal(18,2)` not null default 0 | suma zwrotów w sklepie, informacja |

Nowa tabela `orders.ShopDeletions`: `ExternalId nvarchar(64)` PK, `ReceivedAtUtc datetime2` —
znacznik usunięcia zamówienia, którego ERP jeszcze nie ma.

Ograniczenie: `ShopModifiedAtUtc`, `ShopDeletedAtUtc` i `ShopRefundedAmount <> 0` dozwolone tylko
dla `Source = WooCommerce` (check constraint). Istniejący unikalny indeks (`Source`, `ExternalId`)
bez zmian. Integrations: brak tabel w SF-12. Rozszerzenie `OrderField` o `ShopStatus`,
`ShopDeleted`, `ShopRefundedAmount` (kody audytu `shopStatus`, `shopDeleted`, `shopRefundedAmount`).

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| POST | `/webhooks/woocommerce/orders` | surowa treść JSON (lub ping), nagłówki `X-WC-Webhook-Signature`, `-Topic`, `-Delivery-ID` | `200` / `401` / `404` (wyłączone) / `413` / `503` | anonimowy, podpis HMAC; poza `/api`, bez CSRF | jedno zamówienie |
| GET | `/api/orders` | + `includeHiddenShopStatuses` (bool, domyślnie `false`) | + `shopStatus` w elemencie listy (addytywnie) | sesja | lista |
| GET | `/api/orders/{id}` | — | + `shopDeletedAtUtc`, `shopRefundedAmount`, `shopModifiedAtUtc`, `shopWarnings[]` | sesja | zamówienie |

### Types crossing a boundary

- `ShopOrderSnapshot` (Shared.Application): `ExternalId`, `IsDeletion`, `ModifiedAtUtc?`,
  `OrderDate?`, `Status?`, `Value?`, `RefundedAmount`, `ShopCustomer?` (rodzaj, nazwa, telefon,
  e-mail, ulica, kod, miasto, NIP).
- `IShopOrderSync.ApplyAsync(ShopOrderSnapshot, CancellationToken)` → `ShopOrderSyncResult`
  (`Created`, `Updated`, `Unchanged`, `SkippedOlder`, `SkippedBeforeLaunch`, `IgnoredDeletion`).
- Frontend `OrderDetails`: nowe pola i `shopWarnings: ShopWarning[]` (unia kodów z tabeli).

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sales separate from cash | Integracja nie tworzy wpłat ani wypływów; zwrot w sklepie to tylko kwota informacyjna; status opłacenia w sklepie nie jest wpłatą. |
| 2. Backend decimal money | `total` i zwroty parsowane jako `decimal` (invariant culture), zapis `decimal(18,2)`; bez `double`. |
| 3. API-enforced access | Webhook bez sesji, ale z obowiązkowym podpisem HMAC; zły podpis → `401` bez przetwarzania. Endpoint dopisany do listy anonimowych w `EndpointSecurityTests`. Odczyty `/api/orders` jak dotąd z sesją. |
| 4. Retries never duplicate | Unikalność (`Source`, `ExternalId`) i `Number`, blokada wiersza, pomijanie starszych dostaw i dostaw sprzed usunięcia, znacznik usunięcia nieznanego zamówienia, równoczesne utworzenie → ponowienie w nowym zakresie jako aktualizacja; przejście na „Anulowane” tylko przy zmianie aktywne → zakończone, więc ponowienia (także drugie `order.deleted`) nie anulują ponownie przywróconego zamówienia; pola ERP nie są nadpisywane. |
| 5. Financial audit trail | `Created` i `Updated` z autorem `WooCommerceSync` w tej samej jednostce pracy; dane osobowe klienta oznaczone `IsPersonalData`. |
| 6. Secrets stay server-side | Sekret webhooka tylko w `appsettings.Production.json` na serwerze; nie trafia do logów ani odpowiedzi; komunikacja ze sklepem tylko przez backend (SF-12 niczego do sklepu nie wysyła). |
| 7. Module and layer boundaries | Integrations nie dotyka tabel Orders — wywołuje `IShopOrderSync` z Shared.Application; reguły w `Order.ApplyFromShop` (Domain). |
| 8. No identifying data in docs | Testy i przykłady na fikcyjnych danych („Jan Testowy”), bez nazwy i domeny sklepu. |

## Risk

`high`: webhooki zmieniają zamówienia (wartość sprzedaży, status anulowania) i zapisują dane
klientów; błąd duplikuje sprzedaż albo nadpisuje dane ERP. Przed merge wymagany przegląd przez
człowieka. Dodatkowe ryzyko: SF-2 nie potwierdziło payloadów na sklepie — integracja pozostaje
wyłączona (brak sekretu) do ręcznej próby.

## UI

- **Lista** (`orders-list-page`): przełącznik „Pokaż oczekujące, nieudane i szkice ze sklepu”
  (checkbox daisyUI w panelu filtrów); stany listy bez zmian (ładowanie, pustka, błąd).
- **Szczegóły** (`order-details-page`): sekcja alertów ze sklepu nad danymi zamówienia (tabela
  w „Szczegóły zamówienia”); data usunięcia w sklepie w formacie `plDateTime`; kwota zwrotu
  `plnAmount`. Pola sklepu pozostają tylko do odczytu (SF-8).
- Dostępność: alerty z `role="status"`, tekst, nie tylko kolor; przełącznik z etykietą.

## Edge Cases

- Ta sama dostawa dwa razy → jeden rekord, druga bez zmian i bez audytu.
- `order.updated` przed `order.created` → pierwsza tworzy rekord, druga starsza/równa → bez zmian.
- Dwie dostawy tej samej daty modyfikacji, różne dane → druga stosowana (równa data).
- `order.deleted` dla nieznanego zamówienia → `200`, znacznik usunięcia; późniejsze `order.created`
  z datą modyfikacji sprzed znacznika tworzy zamówienie usunięte i „Anulowane”, a `order.restored`
  — aktywne.
- `order.deleted` dla zamówienia z wpłatami → „Anulowane”, oznaczenie usunięcia, wpłaty pozostają;
  sekcja wpłat pokazuje „Do zwrotu X zł”.
- Usunięcie → przywrócenie w ERP przez użytkownika → ponowne `order.deleted` → bez zmian statusu
  realizacji (zamówienie już zakończone w sklepie).
- Usunięcie, a potem spóźnione `order.updated` sprzed usunięcia → bez zmian, zamówienie zostaje
  usunięte.
- `trash` → `cancelled` → bez ponownego przejścia.
- Dostawa z samą nowszą datą modyfikacji → zapis `ShopModifiedAtUtc`, brak audytu; równoczesna
  edycja użytkownika dostaje `409`.
- Ping bez podpisu → `200`, nic; inna treść bez podpisu → `401`.
- Ponad 300 żądań na minutę z jednego IP → `429`.
- Anulowanie w sklepie → przywrócenie w ERP przez użytkownika → kolejna dostawa ze statusem
  `cancelled` nie anuluje ponownie (status sklepu się nie zmienił); ostrzeżenie `cancelled_in_shop`.
- Przywrócenie w sklepie zamówienia anulowanego w ERP → `active_in_shop`, status realizacji bez zmian.
- Anulowanie w ERP zamówienia aktywnego w sklepie → `active_in_shop`; kolejne dostawy nadal
  aktualizują pola sklepu.
- Import jako `cancelled` → „Anulowane”; import jako `trash` → „Anulowane” + usunięte.
- Zamówienie sprzed uruchomienia zmienione po starcie → nadal pomijane (brak rekordu).
- Zamówienie z datą w dniu uruchomienia → importowane.
- `date_created_gmt` 23:30 UTC 31 grudnia → data zamówienia 1 stycznia (Warszawa).
- Obniżenie wartości poniżej sumy obniżeń sprzedaży → zapis i istniejące ostrzeżenie SF-10.
- Brak NIP w meta → brak NIP; firma bez NIP jest dozwolona (Assumptions).
- Brak `billing` → klient bez danych (nazwa „Klient sklepu #{id}”).
- Zbyt długie teksty → obcięte do limitów kolumn.
- Ujemne lub nieczytelne `total` → treść odrzucona jako nieczytelna (`200` + log).
- Zmiana statusu z `pending` na `processing` → zamówienie pojawia się na liście domyślnej; audyt
  `shopStatus`.
- Brak sekretu → `404`, żadnych zmian.
- Zły podpis przy poprawnej treści → `401`, żadnych zmian.

## Dependencies

- SF-2 — wnioski z dokumentacji przyjęte; próby na sklepie przed włączeniem integracji.
- SF-4 — wdrożenia i migracje (status `done`).
- SF-10 — limit obniżeń i ostrzeżenie (status `done`).
- SF-8, SF-7, SF-9 — encja zamówienia, audyt, blokada zamówienia (status `done`).

## Assumptions

- **Pytanie:** SF-2 niezakończone — co z SF-12? **Odpowiedź:** budowa według dokumentacji
  WooCommerce; integracja wyłączona bez sekretu; próba na sklepie to ręczny krok przed włączeniem.
- **Pytanie:** źródło danych po webhooku? **Odpowiedź:** podpisany payload webhooka; klucze REST
  dopiero w SF-13.
- **Pytanie:** mapowanie klienta i wartości? **Odpowiedź:** `billing`, firma gdy
  `billing.company` niepuste, NIP z meta `_billing_billing_nip`, wartość `total`, data
  z `date_created_gmt` w Europe/Warsaw.
- **Pytanie:** waluty inne niż PLN i nieznane statusy? **Odpowiedź:** sklep ma tylko PLN — bez
  obsługi walut. Nieznany status sklepu: traktowany jak aktywny (decyzja autora specyfikacji, brak
  odpowiedzi wprost).
- Numer ERP zamówienia ze sklepu: `WC-{ExternalId}` — deterministyczny, bez licznika, odporny na
  ponowienia; mieści się w `NumberMaxLength` 32 dla ID do 29 cyfr.
- Endpoint poza `/api` (`/webhooks/...`): WooCommerce wywołuje backend bezpośrednio, bez ciasteczek
  i bez CSRF; Vercel nie przepisuje tej ścieżki.
- Nieczytelna treść → `200` + log (nie `4xx`), żeby WooCommerce nie wyłączył webhooka; historia
  błędów w SF-13.
- Reguła firmy bez NIP: zamówienie ze sklepu nie podlega walidacji formularza ręcznego (NIP
  opcjonalny); `Order.CreateFromShop` przyjmuje klienta bez NIP i bez części danych.
- Status `refunded` w sklepie nie anuluje zamówienia w ERP (PRD: zwrot w sklepie to informacja);
  kwota zwrotu z `refunds[]`.
- Znacznik usunięcia nieznanego zamówienia (`orders.ShopDeletions`) zamiast ignorowania: SF-2
  wskazało, że REST nie odtworzy trwale usuniętych zamówień, więc bez znacznika usunięcie, które
  dotrze przed utworzeniem, zostałoby utracone.
- Czas usunięcia to czas odbioru dostawy (`order.deleted` nie ma daty modyfikacji); różnica zegarów
  sklepu i serwera może sprawić, że modyfikacja tuż po usunięciu zostanie uznana za wcześniejszą —
  do sprawdzenia w próbie SF-2.
- Ping bez podpisu przyjmowany (`200`, bez działania) — założenie z kodu WooCommerce, do
  potwierdzenia w próbie SF-2.
- Limit żądań 300/min/IP — wartość techniczna przyjęta w specyfikacji.
- SF-22 (anonimizacja) doda regułę, że synchronizacja nie przywraca danych zanonimizowanego klienta;
  SF-12 jej nie implementuje.
- Ostrzeżenie `cancelled_in_shop` po przywróceniu w ERP zamówienia anulowanego w sklepie —
  rozstrzygnięcie punktu „do rozstrzygnięcia” ze zgłoszenia: pokazujemy informację, przywrócenie
  jest dozwolone.
- Przy równoczesnym zapisie użytkownika i synchronizacji obowiązuje istniejąca kontrola wersji
  (`409` dla użytkownika); synchronizacja trzyma blokadę wiersza.
- Reguła „liczy się do sprzedaży” jest poza SF-12 (SF-19).
- Daty `date_*_gmt` traktowane jako UTC (SF-2: test granic dat niewykonany — do potwierdzenia
  w próbie na sklepie).

## Acceptance Criteria

- [ ] Wielokrotne dostarczenie tego samego ID (w tym równoczesne) daje jeden rekord.
- [ ] Synchronizacja nie zmienia pól ERP (dane operacyjne, komentarz, wpisy, status realizacji)
      poza jednorazowym przejściem na „Anulowane”.
- [ ] Ponowne dostarczenie tego samego statusu nie zmienia statusu realizacji.
- [ ] Dostawa starsza od zapisanych danych niczego nie zmienia.
- [ ] Zamówienie z datą sprzed uruchomienia nie tworzy rekordu ani błędu.
- [ ] Przywrócenie w sklepie i anulowanie w ERP zamówienia aktywnego w sklepie pokazują
      ostrzeżenie „Zamówienie aktywne w sklepie” i niczego nie zmieniają w sklepie.
- [ ] Nowe zamówienie ma status „W trakcie”, a zaimportowane jako anulowane — „Anulowane”.
- [ ] Usunięcie w sklepie anuluje zamówienie w ERP, oznacza je jako usunięte, a wpłaty pozostają;
      ponowne usunięcie i spóźniona dostawa sprzed usunięcia niczego nie zmieniają; usunięcie
      nieznanego zamówienia jest zapamiętane.
- [ ] Integracja nie tworzy wpłat ani wypływów; zwrot w sklepie jest tylko informacją.
- [ ] Obniżenie wartości poniżej sumy obniżeń sprzedaży pokazuje ostrzeżenie.
- [ ] Zły lub brakujący podpis → `401` bez zmian; brak sekretu → `404`.
- [ ] Zmiany mają audyt z autorem „Synchronizacja WooCommerce”, także zmiana statusu ze sklepu.
- [ ] Lista domyślnie ukrywa pending, failed i szkice ze sklepu; przełącznik je pokazuje; lista
      i szczegóły pokazują status ze sklepu z polską etykietą.

## Verification

- build: `dotnet build stone-flow.slnx`; `npm run build` w `app/`.
- tests:
  - jednostkowe Orders.Tests: `Order.ApplyFromShop` (przejścia statusów, usunięcie, przywrócenie,
    własność pól), `ShopStatuses`; Integrations: podpis, mapowanie payloadu (fikcyjne JSON-y);
  - integracyjne: webhook z podpisem (utworzenie, aktualizacja, starsza dostawa, przed
    uruchomieniem, duplikat równoczesny, usunięcie, przywrócenie, zły podpis, brak sekretu, ping),
    audyt `WooCommerceSync`, lista z filtrem, szczegóły z ostrzeżeniami, brak wpłat;
  - frontend: przełącznik filtra, ostrzeżenia w szczegółach;
  - `EndpointSecurityTests` z nowym endpointem anonimowym.
- manual (przed włączeniem integracji, poza SF-12): webhook testowy w sklepie, podpis na prawdziwym
  payloadzie, kosz/przywrócenie/usunięcie (SF-2).
- **Przegląd przez człowieka przed merge (ryzyko high).**
