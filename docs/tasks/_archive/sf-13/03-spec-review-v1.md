# Spec review — SF-13

## Summary

- Overall assessment: FAIL
- Justification: Specyfikacja pokrywa zgłoszenie, a większość opisu zgadza się z kodem SF-11 i SF-12: dzierżawa, `TryRunNowAsync` bez odstępu dla przebiegu ręcznego, anulowanie przy zamykaniu jako błąd bez odstępu, `IShopOrderSyncRunner` z jednym ponowieniem i `ShopOrderSnapshot.Deletion`. Kursor (start przebiegu, margines 10 min, `modified_before`) jest poprawny. Dwie luki dotyczą jednak automatycznych zmian w zamówieniach, które usuwają je ze sprzedaży. Uzupełnianie nie określa, co robi z zamówieniami przeniesionymi do kosza lub trwale usuniętymi, a kryterium „pominięty webhook jest uzupełniany” obejmuje też `order.deleted`. „Ponów” traktuje każdą odpowiedź `404` jako usunięcie w sklepie, także `404` wynikający ze złej konfiguracji. Przy ryzyku high trzeba to rozstrzygnąć przed planem.

## Findings

### Critical Issues

1. **Behavior → Kursor i przebieg uzupełniania, pkt 2.3; Acceptance Criteria (pierwsze kryterium).** Zapytanie `status=any` według semantyki WordPress/WooCommerce nie zwraca zamówień w koszu (`trash` jest wykluczony z `any`). Trwale usuniętych zamówień REST nie zwraca wcale. `docs/wdrozenia.md` sekcja 11 mówi o tym wprost: „REST nie zwraca trwale usuniętych zamówień — do decyzji właściciela po próbie”. SF-2 również wymienia kosz jako niesprawdzony. Specyfikacja tego nie rozstrzyga. Skutek: przy pominiętym `order.deleted` (kosz albo trwałe usunięcie) zamówienie zostaje w ERP aktywne i liczone do sprzedaży bez ograniczenia czasu. Przebieg kończy się sukcesem, więc SF-14 pokaże „Aktualne”. Jest to sprzeczne z PRD sekcja 5 (usunięcie lub przeniesienie do kosza działa jak anulowanie), ZAM-02 i INT-01 („nie sugeruje kompletności”). Specyfikacja musi określić, czy przebieg pobiera także `status=trash` (osobny przebieg stron albo jawna lista statusów). Musi też zapisać trwałe usunięcie jako znane ograniczenie, z decyzją właściciela w Assumptions i punktem do potwierdzenia na sklepie (SF-2).
2. **Behavior → „Ponów”, gałąź `404`.** Każdy `404` z `GET /wp-json/wc/v3/orders/{externalId}` jest traktowany jak usunięcie zamówienia. Wywołuje to jednorazowe przejście na „Anulowane” i oznaczenie usunięcia. WordPress zwraca jednak `404` także przy złym `BaseUrl` lub ścieżce, wyłączonym REST API albo braku trasy (`rest_no_route`). Tak samo potrafią odpowiadać wtyczki bezpieczeństwa i strony błędu hostingu (HTML). Przy błędnej konfiguracji jedno kliknięcie „Ponów” anuluje w ERP zamówienie, które w sklepie istnieje, i usuwa je ze sprzedaży. Specyfikacja musi zawęzić gałąź usunięcia do odpowiedzi `404` z treścią JSON i kodem błędu WooCommerce dla nieistniejącego zamówienia (wg dokumentacji `woocommerce_rest_shop_order_invalid_id`, do potwierdzenia na sklepie). Każdy inny `404` ma dawać `502 retry_failed`, a błąd ma zostać otwarty. Potrzebny jest też przypadek brzegowy i test.

### Non-Critical Issues

1. **Behavior → Kursor i przebieg, pkt 2.3; Edge Cases („To samo zamówienie na dwóch stronach”).** Paginacja przez `page` jest niestabilna. Gdy w trakcie przebiegu zbiór wyników się zmienia (zamówienie zmienione po `startedAt` wypada przez `modified_before`, a bez tego ograniczenia przesuwa się na koniec), kolejne pozycje przesuwają się o jedną w przód. Jedno zamówienie z granicy strony nie zostanie wtedy pobrane. Przebieg kończy się sukcesem, kursor przechodzi za to zamówienie i następny przebieg go nie odzyska. Specyfikacja opisuje tylko duplikat, nie pominięcie. Przy obecnym wolumenie (20–30 zamówień miesięcznie, `per_page` 100) zdarzy się to rzadko, głównie przy pierwszym przebiegu lub po długiej przerwie. Warto jednak dodać prosty warunek, np. porównanie `X-WP-Total` z pierwszej i ostatniej strony (różnica → ponowienie od strony 1 albo przebieg nieudany) albo stronicowanie przez przesuwany `modified_after`.
2. **Behavior → „Synchronizuj teraz”; Edge Cases („Dwa »Synchronizuj teraz« naraz → jeden przebieg”).** Przy kolejce `Channel` z jednym konsumentem oba żądania przejdą sprawdzenie dzierżawy (lease nie jest jeszcze przejęty) i oba dostaną `202`. Konsument wykona je po kolei, więc drugi przebieg ruszy po zakończeniu pierwszego i nie dostanie `AlreadyRunning`. Twierdzenie „przebieg drugiego nie powstanie” jest sprzeczne z opisanym projektem. Duplikatów danych nie będzie (idempotencja SF-12), ale test z Verification będzie niejednoznaczny. Trzeba określić pojemność i zachowanie kolejki (np. pojemność 1 ze scalaniem żądań albo `409` dla oczekującego żądania). Warto też wspomnieć, że żądanie z kolejki ginie przy restarcie.
3. **Behavior → Historia błędów, „Zamyka się”.** Błąd zamyka każdy udany zapis z dowolnego źródła, niezależnie od wersji zamówienia. Przykład: przebieg nie zapisał wersji v2 (błąd otwarty, przebieg sukcesem, kursor za v2), po czym przychodzi spóźniony webhook v1 i daje `Updated` lub `SkippedOlder`. Błąd się zamyka, a v2 nie trafia do ERP, dopóki zamówienie znów się nie zmieni. Właściciel zdecydował, że zamyka „udany późniejszy zapis”, więc warto doprecyzować „późniejszy”: zapisać `ModifiedAtUtc` nieudanej dostawy i zamykać błąd tylko wtedy, gdy zapisane dane są co najmniej tak nowe. Bez daty, jak przy treści nieczytelnej, wystarczy dowolny sukces.
4. **Behavior → Historia błędów (`Message`); Kursor i przebieg, pkt 2.6.** Specyfikacja wymaga komunikatu „bez danych klienta i sekretów”, ale nie mówi, skąd go brać. Surowy `exception.Message` może zawierać dane klienta, np. `SqlException` przy obcięciu tekstu w SQL Server 2019+ podaje obciętą wartość. Wyjątek przebiegu trafia też do historii SF-11 przez `JobDue.DescribeError` (`Type: Message`) i do `GET /api/jobs`. Trzeba określić, że komunikat powstaje z typu wyjątku i stałego tekstu albo kodu HTTP, a nie z treści wyjątku ani odpowiedzi sklepu.
5. **Behavior → Historia błędów, „Powstaje” (treść nieczytelna z webhooka).** `WooCommerceOrderPayload.Read` dla treści nieczytelnej zwraca `PayloadReadResult` bez `ExternalId` (`Snapshot = null`), więc webhook nie ma klucza do zapisania błędu. Specyfikacja nie wymienia potrzebnej zmiany kontraktu `PayloadReadResult`. Nie określa też, co zrobić z pozycją na stronie uzupełniania bez czytelnego `id`: pominąć z logiem czy uznać stronę za nieczytelną, czyli przebieg nieudany.
6. **Behavior → Kursor i przebieg, pkt 2.6; Data / API → Storage („Przebieg running po awarii”).** Uwaga w `IScheduledJob` każe po anulowaniu przerwać pracę „bez dalszych zapisów”, a specyfikacja zapisuje wtedy przebieg `failed`. Trzeba doprecyzować, że ten zapis idzie z osobnym, krótkim tokenem i mieści się w zapasie `JobExecutor.Grace` (2 min). Do tego `GET .../status` po awarii procesu pokaże `lastRun.status = running` przy `isRunning = false` aż do następnego przebiegu. SF-11 w tym samym przypadku pokazuje `abandoned`. Dzierżawa po awarii jest ważna do 32 min (`MaxDuration` + `Grace`), więc przez ten czas „Synchronizuj teraz” dostaje `409 sync_in_progress`. SF-14 potrzebuje jednoznacznej reguły dla porzuconego przebiegu (stan „Błąd” vs „Synchronizacja trwa”).
7. **Behavior → Historia błędów („Najwyżej jeden otwarty błąd”).** Brak opisu współbieżności `RecordAsync`. Dwa równoczesne niepowodzenia tego samego zamówienia (webhook i przebieg) naruszą unikalny indeks filtrowany. Trzeba określić upsert albo ponowienie po naruszeniu unikalności oraz zachowanie przy wyścigu zapisu z zamykaniem.
8. **Behavior → Kursor i przebieg, pkt 2.3.** Brakuje formatu `modified_after` i `modified_before` (ISO 8601 bez przesunięcia przy `dates_are_gmt=true`, sekundy). Brakuje też informacji, że granice są ostre. Margines to pokrywa, ale klient REST i testy z fałszywym `HttpMessageHandler` potrzebują konkretnego formatu.
9. **Behavior → „Ponów”.** Błąd zapisu w ERP (baza) daje `502 retry_failed`, a to kod błędu bramy. Rozważyć `503` albo osobny kod. Przy anulowaniu wywołanym kliknięciem „Ponów” audyt SF-12 ma autora `WooCommerceSync`, a historia błędów nie zapisuje konta. Niezmiennik 5 jest zachowany, ale warto rozważyć zapis konta przy zamknięciu przez `retry`.
10. **Edge Cases („Brak konfiguracji REST”).** Nie określono, czy przebieg bez konfiguracji tworzy wiersz `WooCommerceSyncRuns`. Od tego zależy `lastRun` w stanie dla SF-14.
11. **Data / API → Storage.** PRD sekcja 7 („Synchronizacja”) wymienia identyfikator korelacji. `Context` zawiera temat webhooka albo Id przebiegu, ale nie `X-WC-Webhook-Delivery-ID` ani identyfikator dla „Ponów”. Warto to ujednolicić.

### Unclear or Ambiguous Sections

- **Kursor i przebieg, pkt 2.1–2.2:** nie wiadomo, czy `startedAt` to ta sama chwila co `StartedAtUtc` przebiegu w `WooCommerceSyncRuns`, czy osobny odczyt zegara.
- **Historia błędów, `Context`:** brak wartości dla źródła `retry`.
- **„Synchronizuj teraz”:** `isRunning` w okresie między `202` a przejęciem dzierżawy pokazuje `false`. Nie wiadomo, czy SF-14 ma to uwzględnić.
- **Odczyt stanu, lista błędów:** brak opisu `status=all` (sortowanie, filtr) i zachowania przy niepoprawnym `status` lub `pageSize` (`400`?).

### Invented or Unsupported Requirements

None.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Kursor i przebieg, pkt 2.3 (`status=any`) | `docs/wdrozenia.md` sekcja 11; PRD sekcja 5 (kosz = anulowanie); SF-2 `01-weryfikacja.md` (kosz niesprawdzony) | Otwarta decyzja o usunięciach niewidocznych w REST nie jest rozstrzygnięta ani odnotowana jako ograniczenie |
| „Ponów”, gałąź `404` | PRD sekcja 5 (anulowanie tylko przy usunięciu w sklepie) | Każdy `404`, także konfiguracyjny, uruchamia anulowanie |
| Kursor i przebieg, pkt 2.6 | `IScheduledJob` (uwaga „bez dalszych zapisów” po anulowaniu) | Zapis `failed` po anulowaniu wymaga doprecyzowania |
| Data / API → Storage | PRD sekcja 7 („identyfikator korelacji”) | Częściowe pokrycie przez `Context` |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Zapis wyłącznie przez `IShopOrderSync`; wpłaty zostają także przy usunięciu. |
| 2. Backend decimal money | OK | Mapowanie bez zmian (`decimal`). |
| 3. API-enforced access | OK | Sesja na wszystkich nowych endpointach, CSRF globalnie dla zapisów pod `/api` (`CsrfValidationMiddleware`). |
| 4. Retries never duplicate | OK | Idempotencja SF-12 i dzierżawa SF-11; dodatkowy przebieg z Non-Critical 2 nie tworzy duplikatów. |
| 5. Financial audit trail | OK | Audyt SF-12 z autorem `WooCommerceSync`; uwaga o koncie przy „Ponów” w Non-Critical 9. |
| 6. Secrets stay server-side | OK | Basic auth w nagłówku, `https`, klucze tylko w konfiguracji; źródło komunikatów do doprecyzowania (Non-Critical 4). |
| 7. Module and layer boundaries | OK | Własne tabele Integrations; Orders przez `IShopOrderSync` i nowy `IShopOrderLookup` w Shared.Application. |
| 8. No identifying data in docs | OK | `<adres-sklepu>`, fikcyjne zamówienia w testach. |

## Risk

- Recorded: high
- Assessed: high. Pierwsze wywołania do sklepu z kluczami, automatyczne anulowanie zamówień (wpływ na sprzedaż) i kursor decydujący o kompletności danych. Wymagany przegląd człowieka przed merge.

## Assumptions Detected

- „Ponów” przy `404` = usunięcie: podane (odpowiedź właściciela). Nie odróżnia jednak `404` konfiguracyjnego (Critical 2).
- Błędy webhooków w historii, zamykane udanym zapisem z dowolnego źródła: podane. Nie uwzględnia wersji zamówienia (Non-Critical 3).
- Kursor = start przebiegu, margines 10 min: podane.
- Nakładanie przebiegów rozstrzyga dzierżawa SF-11: podane. Nie obejmuje kolejki żądań (Non-Critical 2).
- Pierwsze pobranie od początku dnia daty uruchomienia: podane.
- `202` i praca w tle: podane.
- Parametry REST (`status=any`, `dates_are_gmt`, `orderby=modified`) do potwierdzenia: podane. Nie obejmuje skutku `any` bez kosza (Critical 1).
- Klucz tylko do odczytu, `per_page` 100, brak konfiguracji nie blokuje startu: podane.
- Rejestr SF-11 i własne przebiegi Integrations obok siebie: podane.
- Ukryte: trwale usunięte i przeniesione do kosza zamówienia nie wymagają uzupełniania. Nie podane.
- Ukryte: paginacja przez `page` jest stabilna w trakcie przebiegu. Nie podane.
- Ukryte: `WooCommerceOrderPayload.Read` daje `ExternalId` dla treści nieczytelnej. Nie podane i nieprawdziwe w obecnym kodzie.
- Ukryte: komunikaty wyjątków nie zawierają danych klienta. Nie podane.
- Ukryte: shop odpowiada `404` wyłącznie dla nieistniejącego zamówienia. Nie podane.

## Recommendation

- Revise specification
