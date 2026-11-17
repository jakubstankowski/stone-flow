# Task Specification — SF-13

> Wersja 2 (2026-11-06), po przeglądzie `03-spec-review-v1.md` (FAIL). Zmiany:
> - drugie zapytanie `status=trash` i znane ograniczenie trwałego usunięcia, zaakceptowane przez właściciela (Critical 1);
> - usunięcie przy „Ponów” tylko dla 404 WooCommerce o nieistniejącym zamówieniu (Critical 2);
> - kontrola stabilności paginacji, jedno oczekujące „Synchronizuj teraz”;
> - zamykanie błędu z uwzględnieniem wersji, komunikaty bez danych klienta, `ExternalId` przy nieczytelnej treści;
> - upsert błędu, porzucone przebiegi, format dat, kody `502`/`503`, kontekst z ID dostawy.

## Source

[SF-13](https://standev.youtrack.cloud/issue/SF-13)

## Goal

Dodać dzienne uzupełnianie zamówień WooCommerce przez REST API v3 (zadanie harmonogramu SF-11,
23:00 Europe/Warsaw) od ostatniego sukcesu synchronizacji. Każde pobrane zamówienie trafia do
istniejącego `IShopOrderSync` (SF-12) z tymi samymi regułami co webhook.

- **Błąd pojedynczego zamówienia** (z uzupełniania albo z webhooka) nie zatrzymuje przebiegu. Trafia do historii błędów ze statusem, czasem i kontekstem i jest otwarty do udanego zapisu tego zamówienia.
- **„Ponów”** pobiera jedno zamówienie z REST; zamówienie usunięte w sklepie (404) traktuje jak usunięcie.
- **„Synchronizuj teraz”** uruchamia to samo uzupełnianie od ostatniego sukcesu.
- **Niedostępność sklepu** to nieudany przebieg bez przesunięcia kursora, więc kolejny przebieg odzyska dane.
- **Stan synchronizacji** (ostatni sukces, ostatni przebieg, otwarte błędy) jest zapisany. Prezentacja to SF-14.

## Context

- PRD sekcja 5 „Zamówienie WooCommerce”:
  - uzupełnianie o 23:00 „od ostatniego sukcesu”; pierwsze pobranie od daty uruchomienia;
  - definicja sukcesu: sklep odpowiedział i pobrano wszystkie strony;
  - błąd zamówienia nie zatrzymuje przebiegu i ma status, czas oraz kontekst w historii błędów;
  - „Ponów” i „Synchronizuj teraz” nie tworzą duplikatów;
  - stany synchronizacji INT-01.
- Sekcja 6: ZAM-02, INT-01.
- SF-11:
  - harmonogram `IScheduledJob`, `AddScheduledJob`, `IJobRunner.TryRunNowAsync` (ta sama dzierżawa), `GET /api/jobs`;
  - `JobRunContext.LastSuccessStartedAt` nie jest kursorem biznesowym, więc kursor należy do SF-13;
  - D-08 pkt 2: dowód z hostingu przy pierwszym wdrożeniu zadania (SF-13).
- SF-12:
  - `IShopOrderSync`, `ShopOrderSnapshot`, `WooCommerceOrderPayload.Read` (mapowanie REST v3), `IShopOrderSyncRunner`;
  - webhooki z historią błędów tylko w logu; konfiguracja `StoneFlow:WooCommerce:*`; integracja wyłączona bez sekretu.
- SF-2: REST v3 ma `modified_after`, `modified_before`, paginację (`page`, `per_page`, `X-WP-TotalPages`); niezweryfikowane na sklepie; daty `_gmt` w GMT.
- Moduł **Integrations** (schemat `integrations`, dotąd bez tabel) — właściciel kursora, przebiegów
  synchronizacji i historii błędów; klient REST w Infrastructure; endpointy w Integrations.Api.

## Scope

### In scope

- Konfiguracja REST: `StoneFlow:WooCommerce:BaseUrl`, `ConsumerKey`, `ConsumerSecret` (klucz
  tylko do odczytu); bez nich uzupełnianie i „Ponów” są wyłączone.
- Klient REST (HttpClient, Basic auth przez HTTPS, limit czasu, paginacja).
- Zadanie harmonogramu `woocommerce-backfill` (23:00, `MaxDuration` 30 min).
- Kursor sukcesu, zapis przebiegów, historia błędów zamówień.
- „Synchronizuj teraz” i „Ponów” (API), odczyt stanu i błędów (API) dla SF-14.
- Zapis błędów z webhooków (SF-12) i zamykanie błędów udanym zapisem z dowolnego źródła.
- Testy z udawanym sklepem (fałszywy `HttpMessageHandler`), dokumentacja kluczy REST.

### Out of scope

- UI stanu integracji, przycisków i listy błędów (SF-14).
- Zapis czegokolwiek w sklepie.
- Zmiana reguł zastosowania zamówienia (SF-12).
- Retencja historii.

## Behavior

### Kursor i przebieg uzupełniania

1. `integrations.WooCommerceSyncState` (jeden wiersz): `LastSuccessCursorUtc` — chwila startu
   ostatniego udanego przebiegu; przed pierwszym sukcesem brak.
2. Przebieg (zadanie harmonogramu albo „Synchronizuj teraz”):
   1. zapisuje przebieg `running` w `integrations.WooCommerceSyncRuns` (start UTC, źródło `schedule`/`manual`);
   2. `startedAt` = teraz (UTC); `from` = `LastSuccessCursorUtc − 10 min`. Margines chroni przed
      różnicą zegarów i zmianami w trakcie poprzedniego przebiegu; dzięki idempotencji SF-12 nie
      duplikuje danych. Przed pierwszym sukcesem `from` = początek dnia daty uruchomienia
      w Europe/Warsaw (UTC);
   3. pobiera kolejne strony `GET /wp-json/wc/v3/orders?modified_after={from}&modified_before={startedAt}&dates_are_gmt=true&status={zestaw}&orderby=modified&order=asc&per_page=100&page={n}`
      w dwóch zestawach: najpierw `status=any`, potem `status=trash` (zamówienia w koszu;
      `any` ich nie zwraca). Daty w formacie ISO 8601 UTC `yyyy-MM-ddTHH:mm:ss`; obie granice są
      ostre (po `from`, przed `startedAt`). Strony kolejno do `X-WP-TotalPages` (brak nagłówka →
      koniec, gdy strona ma < 100 pozycji).
      **Stabilność paginacji:** `X-WP-Total` z pierwszej strony zestawu jest porównywane na każdej
      kolejnej; zmiana oznacza, że zbiór się przesunął (np. zmiana zamówienia w trakcie) i pozycja
      z granicy strony mogła zostać pominięta. Zestaw jest wtedy pobierany od strony 1 jeszcze raz
      (zastosowanie jest idempotentne); druga zmiana z rzędu → przebieg nieudany (kursor bez zmian);
   4. każde zamówienie: `WooCommerceOrderPayload.Read` (temat `Updated`; status `trash` daje
      zakończenie w sklepie według SF-12) → `IShopOrderSyncRunner`; wynik inny niż sukces lub
      wyjątek → błąd zamówienia (niżej), przebieg idzie dalej. Pozycja bez czytelnego `id` nie ma
      klucza błędu: jest liczona w `Unidentified` przebiegu i logowana, przebieg idzie dalej;
   5. **sukces** = każda strona dostała `2xx` i poprawny JSON (tablicę). Wtedy w jednej transakcji:
      `LastSuccessCursorUtc` = `startedAt`, przebieg `succeeded` (koniec, liczba zamówień, liczba błędów);
   6. **niepowodzenie przebiegu** = brak odpowiedzi, limit czasu, `4xx`/`5xx`, nieczytelna strona,
      niestabilna paginacja. Przebieg `failed` z opisem złożonym przez kod (np. „Sklep odpowiedział
      HTTP 503 na stronie 3”, „Przekroczono limit czasu”), nigdy z treści odpowiedzi ani komunikatu
      wyjątku; kursor bez zmian; wyjątek z tym samym kontrolowanym komunikatem dla harmonogramu
      (odstęp ponowienia SF-11). Kolejny przebieg startuje od tego samego kursora, więc odzyskuje dane.
   7. **anulowanie** (zamknięcie aplikacji, utrata dzierżawy): przebieg nie pisze nic po anulowaniu
      (kontrakt `IScheduledJob`); wiersz zostaje `running`. Następny przebieg (pod dzierżawą SF-11,
      więc żaden inny nie trwa) oznacza wszystkie `running` jako `failed` „Przerwany (restart lub
      awaria)”. Odczyt stanu pokazuje `running` bez ważnej dzierżawy jako `abandoned` (jak SF-11).
3. Zamówienia sprzed daty uruchomienia: pomijane przez SF-12 bez błędu (bez zmian).
4. **Znane ograniczenie (decyzja właściciela 2026-11-06):** zamówienia trwale usuniętego w sklepie
   REST nie zwraca. Jeśli zginie jego webhook `order.deleted`, ERP się o nim nie dowie i zamówienie
   zostanie w sprzedaży. Przeniesienie do kosza (zwykła droga usunięcia) jest obsłużone przez
   zestaw `status=trash`. Ograniczenie zapisane w `docs/wdrozenia.md` i PRD (sekcja 5); ewentualne
   porównanie list ID — osobne zadanie.
5. „Kursor sukcesu jako początek przebiegu” (zgłoszenie): tak — `startedAt`, nie koniec, więc zmiany
   w trakcie przebiegu wejdą do następnego.

### „Synchronizuj teraz”

- `POST /api/integrations/woocommerce/sync`:
  - wymaga sesji i CSRF; brak konfiguracji REST → `409` z kodem `integration_not_configured`;
  - gdy przebieg trwa (dzierżawa zadania SF-11 ważna: `GET /api/jobs`/`IJobStatusReader`) → `409` z kodem `sync_in_progress`;
  - w przeciwnym razie `202 Accepted`, a przebieg rusza w tle przez `IJobRunner.TryRunNowAsync("woocommerce-backfill")`.
- Ta sama dzierżawa co o 23:00, więc przebiegi się nie nakładają. Gdy dwa żądania przejdą
  sprawdzenie naraz, drugie dostanie od runnera `AlreadyRunning`; przebieg drugiego nie powstanie.
- Wykonanie w tle: jedno miejsce oczekiwania (kanał o pojemności 1) obsługiwane przez
  `BackgroundService` modułu Integrations. Żądanie, gdy przebieg trwa albo inne żądanie już czeka,
  dostaje `409 sync_in_progress` — oba „Synchronizuj teraz” naraz dają jeden przebieg. Oczekujące
  żądanie ginie przy restarcie aplikacji (użytkownik ponawia; przebieg o 23:00 i tak się odbędzie).
  Zamknięcie aplikacji anuluje przebieg (SF-11: `failed` bez odstępu).
- Po awarii procesu dzierżawa SF-11 jest ważna do jej wygaśnięcia (do ok. 32 min); do tego czasu
  „Synchronizuj teraz” dostaje `409 sync_in_progress`, a stan pokazuje `isRunning`.
- Udany przebieg ręczny przesuwa kursor jak każdy inny i ustawia ostatni sukces zadania SF-11.

### Historia błędów zamówień

- `integrations.WooCommerceOrderErrors`: `Id`, `ExternalId`, `Status` (`open`/`closed`), `Source`
  (`backfill`/`webhook`/`retry`), `FirstOccurredAtUtc`, `LastOccurredAtUtc`, `Attempts`,
  `Message` (≤ 1000 znaków, bez danych klienta i sekretów), `Context` (≤ 200 znaków: temat
  webhooka z `X-WC-Webhook-Delivery-ID` albo Id przebiegu), `FailedModifiedAtUtc` (data
  modyfikacji nieudanej dostawy, jeśli była czytelna), `ClosedAtUtc`, `ClosedBy`
  (`webhook`/`backfill`/`retry`).
- `Message` składa kod, nigdy z komunikatu wyjątku ani treści odpowiedzi: „Nieczytelna treść
  zamówienia: {powód z parsera}” (parser nie podaje danych klienta), „Błąd zapisu zamówienia
  ({typ wyjątku})”, „Sklep odpowiedział HTTP {kod}”, „Przekroczono limit czasu”.
- Najwyżej jeden otwarty błąd na `ExternalId` (unikalny indeks filtrowany `Status = 'open'`).
  Kolejne niepowodzenie aktualizuje `LastOccurredAtUtc`, `Attempts`, `Message`, `Context`,
  `FailedModifiedAtUtc` (większa z wartości). Zapis to upsert w transakcji kontekstu Integrations
  (odczyt otwartego błędu z `UPDLOCK, HOLDLOCK`, potem wstawienie albo zmiana); naruszenie
  unikalności przy równoczesnym wstawieniu → jedno ponowienie jako zmiana.
- **Powstaje**, gdy dla konkretnego `ExternalId`:
  - treść zamówienia jest nieczytelna (webhook, strona uzupełniania, „Ponów”);
  - `IShopOrderSync` zgłasza wyjątek albo dwa razy `RetryRequired`.

  Błąd nie powstaje dla dostaw pominiętych (starsza, sprzed usunięcia, sprzed uruchomienia), bo to nie błąd. Webhook z nieczytelnym `id` nie daje błędu (brak klucza), tylko log.
  Kontrakt SF-12 `PayloadReadResult` dostaje `ExternalId` także dla treści nieczytelnej, gdy `id`
  dało się odczytać (oraz `ModifiedAtUtc`, gdy czytelne).
- **Zamyka się**, gdy zapis tego zamówienia się uda — wynik `Created`, `Updated`, `Unchanged`,
  `SkippedOlder`, `SkippedDeletedInShop`, `SkippedBeforeLaunch` lub `DeletionRecorded` — z dowolnego
  źródła (webhook, uzupełnianie, „Ponów”), **i** dostawa nie jest starsza od nieudanej:
  `FailedModifiedAtUtc` puste albo `ModifiedAtUtc` udanej dostawy ≥ `FailedModifiedAtUtc`.
  Usunięcie (`order.deleted`, 404 z „Ponów”) zamyka zawsze. Spóźniony starszy webhook nie zamknie
  więc błędu nowszej wersji.
- Webhook (SF-12) nadal odpowiada `200` dla treści nieczytelnej i `503` dla błędu zapisu; dodatkowo
  zapisuje błąd. Zapis błędu sam w sobie nie może zmienić kodu odpowiedzi (błąd zapisu błędu → log).

### „Ponów”

- `POST /api/integrations/woocommerce/errors/{id}/retry` (sesja, CSRF):
  - błąd nieistniejący → `404`; zamknięty → `409` `error_closed`; brak konfiguracji REST → `409`;
  - `GET /wp-json/wc/v3/orders/{externalId}`:
    - `200` → `Read` (temat `Updated`) → `IShopOrderSyncRunner`; sukces zamyka błąd (`retry`), wynik `200` z błędem po zmianie;
    - `404` z treścią JSON i kodem WooCommerce nieistniejącego zamówienia
      (`woocommerce_rest_shop_order_invalid_id` albo `woocommerce_rest_invalid_id`) → zamówienia
      nie ma w sklepie: `IShopOrderSyncRunner` z `ShopOrderSnapshot.Deletion`; reguły SF-12 anulują
      zamówienie i oznaczą je jako usunięte, a wpłaty zostają; błąd zamknięty (`retry`);
    - każde inne `404` (np. `rest_no_route`, strona HTML, zły `BaseUrl`), inna odpowiedź sklepu,
      limit czasu, nieczytelna treść → błąd zostaje otwarty, `Attempts` + 1, `Message`
      aktualizowane; odpowiedź `502` `retry_failed`;
    - błąd zapisu w ERP (baza) → błąd otwarty, `503` `retry_unavailable`.
- Audyt zmian z „Ponów” ma autora `WooCommerceSync` (zmiana pochodzi ze stanu sklepu; inicjatora
  widać w historii błędu: `ClosedBy = retry`).
- „Ponów” działa niezależnie od dzierżawy uzupełniania. Blokada SF-12 po `ExternalId` szereguje
  zapis z równoległym przebiegiem; duplikat nie powstaje.

### Odczyt stanu (dla SF-14)

- `GET /api/integrations/woocommerce/status` (sesja):
  - `configured` (bool);
  - `lastSuccessAtUtc` (kursor) lub `null`;
  - `lastRun` (`status`, `startedAtUtc`, `finishedAtUtc`, `source`, `ordersProcessed`, `errors`, `message`) lub `null`;
  - `isRunning` (dzierżawa zadania);
  - `openErrors` (liczba).
- `GET /api/integrations/woocommerce/errors?status=open|closed|all&page=&pageSize=` (sesja):
  - lista malejąco po `LastOccurredAtUtc`, z polami błędu i `orderId`/`orderNumber` (wyszukane przez kontrakt Orders po `ExternalId`, jeśli zamówienie istnieje);
  - domyślnie `open`, `pageSize` 25, maks. 100.
- Przed pierwszym sukcesem: `lastSuccessAtUtc = null`. Stany INT-01 wylicza SF-14.

## Data / API

### Storage

Schemat `integrations` (`IntegrationsDbContext`), migracja `WooCommerceBackfill`:

| Tabela | Kolumny |
|---|---|
| `WooCommerceSyncState` | `Id int` PK (zawsze 1), `LastSuccessCursorUtc datetime2 null` |
| `WooCommerceSyncRuns` | `Id uniqueidentifier` PK, `Source nvarchar(16)`, `StartedAtUtc`, `FinishedAtUtc null`, `Status nvarchar(16)` (`running`/`succeeded`/`failed`), `OrdersProcessed int`, `Errors int`, `Unidentified int`, `Message nvarchar(1000) null`; indeks `StartedAtUtc desc` |
| `WooCommerceOrderErrors` | jak wyżej; unikalny indeks (`ExternalId`) `WHERE Status = 'open'`; indeks `LastOccurredAtUtc desc` |

Przebieg `running` po awarii procesu lub anulowaniu: kolejny przebieg oznacza wszystkie przebiegi
`running` jako `failed` „Przerwany (restart lub awaria)” (dzierżawa SF-11 gwarantuje, że żaden nie
trwa). Brak konfiguracji REST: przebieg nie zapisuje wiersza.

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| POST | `/api/integrations/woocommerce/sync` | — | `202` / `409` (`sync_in_progress`, `integration_not_configured`) | sesja + CSRF | integracja |
| POST | `/api/integrations/woocommerce/errors/{id}/retry` | — | `200` błąd (zamknięty) / `404` / `409` / `502` `retry_failed` / `503` `retry_unavailable` | sesja + CSRF | błąd |
| GET | `/api/integrations/woocommerce/status` | — | `200` stan | sesja | integracja |
| GET | `/api/integrations/woocommerce/errors` | `status`, `page`, `pageSize` | `200` strona błędów | sesja | błędy |

### Types crossing a boundary

- Shared.Application: `IShopOrderLookup.FindAsync(IReadOnlyCollection<string> externalIds)` →
  `ExternalId → (OrderId, Number)` (implementacja w Orders) — do listy błędów.
- Integrations.Application: `IWooCommerceRestClient` (`GetOrdersPageAsync(from, to, page)`,
  `GetOrderAsync(externalId)` → `Found(bytes)`/`NotFound`), `IWooCommerceOrderErrors`
  (`RecordAsync`, `CloseAsync`), `IWooCommerceSyncStore`.
- Konfiguracja `StoneFlow:WooCommerce:BaseUrl` (https), `ConsumerKey`, `ConsumerSecret`,
  `RequestTimeout` (domyślnie 30 s). Walidacja: pełna trójka albo brak; `BaseUrl` musi być `https`.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sales separate from cash | Uzupełnianie i „Ponów” idą przez `IShopOrderSync` (SF-12), który nie tworzy wpłat ani wypływów. |
| 2. Backend decimal money | Mapowanie bez zmian (`WooCommerceOrderPayload`, `decimal`). |
| 3. API-enforced access | Nowe endpointy wymagają sesji, zapisy — CSRF; brak nowych endpointów anonimowych. |
| 4. Retries never duplicate | Idempotencja SF-12 (unikalność, blokada, kolejność); dzierżawa SF-11 dla przebiegów; jeden otwarty błąd na zamówienie; margines kursora tylko powtarza zamówienia, nie tworzy nowych. |
| 5. Financial audit trail | Zmiany zamówień audytowane przez SF-12 z autorem `WooCommerceSync`; historia błędów to dane techniczne, nie finansowe. |
| 6. Secrets stay server-side | Klucze REST tylko w konfiguracji serwera; nie trafiają do logów (Basic auth w nagłówku, logi bez adresów z kluczami), odpowiedzi ani historii błędów; komunikacja ze sklepem tylko z backendu. |
| 7. Module and layer boundaries | Integrations ma własne tabele; Orders przez `IShopOrderSync` i `IShopOrderLookup` (Shared.Application). |
| 8. No identifying data in docs | Testy na fikcyjnych zamówieniach; dokumentacja z `<adres-sklepu>`. |

## Risk

`high`: pierwsze wywołania do sklepu z kluczami, automatyczne zmiany zamówień (także anulowanie
przez „Ponów” przy 404), kursor decydujący o kompletności danych. Przed merge wymagany przegląd
przez człowieka.

## UI

Not applicable — UI stanu integracji, przycisków i listy błędów to SF-14.

## Edge Cases

- Pierwszy przebieg → od początku dnia uruchomienia.
- Kilka dni przerwy → jeden przebieg od ostatniego sukcesu (harmonogram SF-11), wszystkie strony.
- Sklep niedostępny / `401` (zły klucz) / limit czasu → przebieg `failed`, kursor bez zmian,
  odstęp SF-11; kolejny przebieg odzyskuje dane.
- Strona 3 z 5 zwraca `500` → przebieg `failed`; zamówienia z poprzednich stron już zapisane
  (idempotentnie), kursor bez zmian.
- Zamówienie nieczytelne na stronie → błąd zamówienia, przebieg sukcesem (wszystkie strony pobrane).
- To samo zamówienie na dwóch stronach (zmiana w trakcie) → idempotentne.
- „Synchronizuj teraz” w trakcie przebiegu o 23:00 → `409 sync_in_progress`.
- Dwa „Synchronizuj teraz” naraz → jeden przebieg.
- „Ponów” zamówienia usuniętego w sklepie (404 z kodem WooCommerce) → anulowanie + oznaczenie
  usunięcia, błąd zamknięty; 404 bez tego kodu (zły adres, wyłączone REST) → `502`, bez zmian.
- Zamówienie w koszu bez webhooka → zestaw `status=trash` → anulowanie w ERP (SF-12).
- Zamówienie trwale usunięte bez webhooka → niewykrywalne (znane ograniczenie).
- `X-WP-Total` zmienia się w trakcie → ponowne pobranie zestawu; drugi raz → przebieg nieudany.
- Spóźniony starszy webhook zamówienia z błędem nowszej wersji → błąd zostaje otwarty.
- Dwa równoczesne niepowodzenia tego samego zamówienia → jeden otwarty błąd, `Attempts` 2.
- „Ponów” przy niedostępnym sklepie → `502`, błąd otwarty, `Attempts` + 1.
- Udany webhook zamówienia z otwartym błędem → błąd zamknięty (`webhook`).
- Ponowny błąd tego samego zamówienia → ten sam otwarty wiersz, `Attempts` + 1.
- Brak konfiguracji REST → zadanie harmonogramu kończy się sukcesem bez działania (log informacyjny),
  `sync` i `retry` → `409 integration_not_configured`. Zadanie rejestrowane zawsze, żeby SF-14
  widział stan.
- Zamknięcie aplikacji w trakcie → przebieg anulowany; kolejny oznacza `running` jako `failed`.

## Dependencies

- SF-11 — harmonogram, `IJobRunner`, `IJobStatusReader` (done).
- SF-12 — `IShopOrderSync`, payload, runner (merged).
- SF-2 — zachowanie `modified_after` i paginacji do potwierdzenia na sklepie; klucz REST do
  utworzenia przez właściciela (tylko odczyt).

## Assumptions

- **Pytanie:** „Ponów”, gdy zamówienia nie ma w sklepie (404)? **Odpowiedź:** potraktować jak
  usunięcie — reguły SF-12 (anulowanie, oznaczenie usunięcia, wpłaty zostają), błąd zamknięty.
- **Pytanie:** co z trwałym usunięciem zamówienia, którego webhook zginął (REST go nie zwraca)?
  **Odpowiedź (2026-11-06):** akceptuję ograniczenie; kosz obsłużony przez `status=trash`;
  porównanie list ID ewentualnie osobnym zadaniem.
- Rozpoznanie „zamówienia nie ma” po kodach `woocommerce_rest_shop_order_invalid_id` /
  `woocommerce_rest_invalid_id` według dokumentacji — do potwierdzenia na sklepie (SF-2).
- **Pytanie:** czy błędy webhooków trafiają do historii? **Odpowiedź:** tak; udany późniejszy zapis
  z dowolnego źródła zamyka błąd.
- Kursor = start udanego przebiegu, zapytanie z marginesem 10 min (wartość techniczna).
- Nakładanie „Synchronizuj teraz” z 23:00 rozstrzyga dzierżawa SF-11 → `409 sync_in_progress`.
- Stan przed pierwszym sukcesem: brak kursora, pobranie od początku dnia daty uruchomienia.
- „Synchronizuj teraz” zwraca `202` i działa w tle, bo przebieg może trwać do 30 min.
- `status=any`, `dates_are_gmt=true`, `orderby=modified` — parametry REST v3 według dokumentacji;
  do potwierdzenia na sklepie (SF-2).
- Klucz REST tylko do odczytu (uprawnienie „Read” w WooCommerce).
- Brak konfiguracji REST nie blokuje startu; wyłącza uzupełnianie i „Ponów”.
- `per_page` 100 (maksimum REST v3).
- Rejestr zadania SF-11 trzyma wynik przebiegu harmonogramu; własne przebiegi w Integrations
  przechowują szczegóły (liczby, komunikat) potrzebne SF-14.

## Acceptance Criteria

- [ ] Pominięty webhook jest uzupełniany przy dziennym odpytaniu (zamówienie zmienione w sklepie
      bez webhooka pojawia się po przebiegu).
- [ ] Po przerwie (nieudane przebiegi, kilka dni) kolejne uzupełnianie startuje od ostatniego
      sukcesu; pierwsze — od daty uruchomienia.
- [ ] Błąd pojedynczego zamówienia nie zatrzymuje przebiegu i trafia do historii jako otwarty.
- [ ] „Ponów” zamyka błąd po udanym zapisie; 404 traktuje jak usunięcie w sklepie.
- [ ] „Synchronizuj teraz” i „Ponów” nie tworzą duplikatów; „Synchronizuj teraz” w trakcie
      przebiegu → `409`.
- [ ] Niedostępność sklepu zapisuje nieudany przebieg, kursor się nie przesuwa, a kolejny przebieg
      odzyskuje dane.
- [ ] Błąd webhooka dla zamówienia trafia do historii; udany zapis zamyka błąd.
- [ ] Klucze REST nie pojawiają się w logach, odpowiedziach ani historii; komunikaty błędów nie
      zawierają danych klienta.
- [ ] Zamówienie przeniesione do kosza bez webhooka jest anulowane po uzupełnieniu.
- [ ] Niestabilna paginacja nie przesuwa kursora z pominiętym zamówieniem.

## Verification

- build, format, testy jednostkowe (Integrations.Tests: przebieg z fałszywym klientem — paginacja,
  sukces, niepowodzenie, kursor, błędy zamówień), testy integracyjne (SQL + fałszywy
  `HttpMessageHandler` sklepu): przebieg harmonogramu, „Synchronizuj teraz” (`202`, `409`), „Ponów”
  (`200`, 404 → usunięcie, `502`), zamykanie błędu webhookiem, `401` bez sesji, CSRF.
- manual: klucz REST w sklepie, pierwsze uzupełnienie na kopii testowej (SF-2), dowód D-08 pkt 2.
- **Przegląd przez człowieka przed merge (ryzyko high).**
