# Spec review — SF-13

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Wersja 2 rozwiązuje oba problemy krytyczne z przeglądu v1. Przebieg pobiera osobno zestaw `status=trash`, a trwałe usunięcie bez webhooka jest zapisane jako znane ograniczenie z decyzją właściciela z 2026-11-06. Gałąź usunięcia w „Ponów” działa tylko dla `404` z kodem WooCommerce nieistniejącego zamówienia. Opisy kodu SF-11 i SF-12 zgadzają się z repozytorium, w tym `TryRunNowAsync`, `JobExecutor`, `CompleteAsync` ustawiający ostatni sukces także dla przebiegu ręcznego, `ShopOrderSyncRunner`, `PayloadReadResult` i `IsTerminal` dla `trash`. Zostały luki, które da się domknąć w planie: kontrola `X-WP-Total` nie wykrywa każdego przesunięcia stron, kanał o pojemności 1 nie gwarantuje deklarowanego „jednego przebiegu”, a zestaw `trash` opiera się na niezapisanym założeniu o dacie modyfikacji przy przeniesieniu do kosza.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Behavior → Kursor i przebieg uzupełniania, pkt 2.3 („Stabilność paginacji”); Acceptance Criteria (ostatnie kryterium).** Porównanie `X-WP-Total` wykrywa tylko zmianę liczności zbioru. Zbiór może się przesunąć przy niezmienionej liczbie pozycji. Wystarczy, że zegar sklepu spóźnia się względem serwera ERP choćby o kilka sekund, a zamówienie zmienia się w trakcie wielostronicowego przebiegu. Nowa data modyfikacji jest wtedy nadal `< startedAt`, więc zamówienie opuszcza swoje miejsce i wraca na koniec zbioru (`orderby=modified asc`). `X-WP-Total` się nie zmienia, a pozycja z granicy strony zostaje pominięta. Kursor przechodzi za nią i margines 10 min jej nie odzyska. Przy 20–30 zamówieniach miesięcznie i `per_page` 100 dotyczy to praktycznie tylko pierwszego przebiegu albo długiej przerwy, ale kryterium „Niestabilna paginacja nie przesuwa kursora z pominiętym zamówieniem” nie jest w tym przypadku spełnione. Dwie proste poprawki: górna granica w przeszłości (`modified_before` = `startedAt − bufor` większy od różnicy zegarów, ten sam bufor w kursorze) albo stronicowanie przez przesuwany `modified_after` od daty ostatniej pozycji. Ewentualnie trzeba zapisać to jako znane ograniczenie.
2. **Behavior → „Synchronizuj teraz” (kanał o pojemności 1); Edge Cases („Dwa »Synchronizuj teraz« naraz → jeden przebieg”).** Konsument `BackgroundService` czeka na kanale, więc odbiera żądanie natychmiast i miejsce oczekiwania od razu pustoszeje. Między odbiorem a przejęciem dzierżawy w `TryRunNowAsync` (`PrepareAsync` i `TryAcquireAsync` to kilka zapytań do bazy) drugie żądanie widzi pusty kanał i brak dzierżawy. Dostaje więc `202`, a po zakończeniu pierwszego przebiegu rusza drugi. Zdanie „drugie dostanie od runnera `AlreadyRunning`; przebieg drugiego nie powstanie” nie pasuje do jednego konsumenta: runner widzi `AlreadyRunning` tylko wtedy, gdy dzierżawę trzyma harmonogram. Duplikatów danych nie ma (niezmiennik 4 zachowany), ale przypadek brzegowy i test z Verification nie są deterministyczne. Pierwszy problem v1 (Non-Critical 2) jest więc rozwiązany częściowo. Trzeba określić znacznik „żądanie przyjęte”, trwający od zapisu do kanału do końca `TryRunNowAsync`, albo złagodzić gwarancję.
3. **Behavior → Kursor i przebieg, pkt 2.3 i 4 (zestaw `status=trash`); Assumptions; Acceptance Criteria („Zamówienie przeniesione do kosza bez webhooka jest anulowane”).** Zestaw `trash` trafi do okna `modified_after`/`modified_before` tylko wtedy, gdy przeniesienie do kosza aktualizuje `date_modified_gmt` zamówienia (sklep ma HPOS, `OrdersTableDataStore`, SF-2), i tylko wtedy, gdy REST v3 honoruje `modified_after` razem ze `status=trash`. Żadnego z tych założeń nie ma w Assumptions. Lista „do potwierdzenia” obejmuje tylko `status=any`, `dates_are_gmt` i `orderby`. Jeśli data się nie zmienia, przebieg kończy się sukcesem, a zamówienie z kosza nadal liczy się do sprzedaży. Trzeba dopisać założenie i krok weryfikacji. Warto też rozważyć pobieranie zestawu `trash` bez `modified_after`: kosz jest mały i WordPress domyślnie opróżnia go po 30 dniach. Byłoby to odporne na to założenie, a przy idempotencji SF-12 (`Unchanged`) nie ma ryzyka duplikatów.
4. **Behavior → Kursor i przebieg, pkt 4 („Ograniczenie zapisane w `docs/wdrozenia.md` i PRD (sekcja 5)”); Scope → In scope.** Decyzja właściciela zmienia regułę PRD sekcja 5 („Usunięcie zamówienia w sklepie lub przeniesienie go do kosza działa tak samo”) i kryterium ZAM-02 w sekcji 6 („pominięty webhook jest uzupełniany”). SF-2 (`01-weryfikacja.md`) wymaga w takim przypadku „aktualizacji PRD i ponownego przeglądu”. PRD jeszcze tego nie zawiera, a `docs/wdrozenia.md` sekcja 11 nadal mówi „do decyzji właściciela po próbie”. In scope nie wymienia tych zmian w dokumentacji. Trzeba je wpisać jako element dostawy: PRD sekcja 5 i wiersz ZAM-02, `docs/wdrozenia.md` sekcje 11 i 12.
5. **Verification („manual: … pierwsze uzupełnienie na kopii testowej (SF-2)”); Dependencies.** Według SF-2 właściciel ma tylko produkcję i kopii testowej nie ma. Odczyty REST na produkcji niczego nie zmieniają w sklepie. Próby kosza, kodu `404` przy nieistniejącym zamówieniu i zachowania `X-WP-Total` wymagają jednak operacji na zamówieniach. Specyfikacja nie mówi, co zrobić, jeśli kopia nie powstanie: syntetyczne zamówienie na produkcji za zgodą właściciela czy włączenie bez próby. Warto to zapisać w Dependencies albo Assumptions.
6. **Behavior → Historia błędów („Context”).** Dla źródła `retry` nie podano wartości `Context`. Wcześniej zgłaszały to Unclear z v1 i Non-Critical 11 z v1. PRD sekcja 7 wymaga identyfikatora korelacji. Host ma `CorrelationIdMiddleware`, więc naturalna wartość to identyfikator korelacji żądania „Ponów”. Specyfikacja powinna to przesądzić.
7. **Scope → In scope („Basic auth przez HTTPS”); Invariant impact, wiersz 6.** Zapis „logi bez adresów z kluczami” sugeruje, że uwierzytelnienie parametrami `consumer_key`/`consumer_secret` w adresie wchodzi w grę. Część hostingów obcina nagłówek `Authorization`, a typowa „naprawa” to klucze w adresie, które trafiają wtedy do logów `HttpClient` i serwera. Warto napisać wprost, że klient używa wyłącznie nagłówka. Obcięty nagłówek daje `401`, czyli nieudany przebieg do wyjaśnienia na sklepie, nigdy przejście na klucze w adresie.

### Unclear or Ambiguous Sections

- **Kursor i przebieg, pkt 2.1–2.2 i 2.7.** Nadal nie wiadomo, czy `startedAt` to ta sama wartość co `StartedAtUtc` wiersza przebiegu, czy osobny odczyt zegara (Unclear z v1). Nie wiadomo też, w którym kroku przebieg oznacza wcześniejsze wiersze `running` jako `failed`: przed wstawieniem własnego czy po nim, w tej samej transakcji czy osobno.
- **Odczyt stanu → `lastRun.status`.** Stan `abandoned` jest wyliczany przy odczycie, ale nie wiadomo, jak odpowiada stanom INT-01 („Błąd”, gdy ostatni przebieg się nie udał). SF-14 będzie musiał zgadywać. Wystarczy jedno zdanie, że `abandoned` liczy się jak nieudany przebieg.
- **„Synchronizuj teraz” → `isRunning`.** Między `202` a przejęciem dzierżawy oraz przy żądaniu czekającym w kanale stan pokazuje `isRunning = false` (Unclear z v1). Nie wiadomo, czy SF-14 dostaje sygnał „przyjęte”.
- **Odczyt stanu → lista błędów.** Brak zachowania przy niepoprawnym `status`, `page` < 1 lub `pageSize` > 100 (`400` czy przycięcie) oraz sortowania dla `status=all` (Unclear z v1).
- **„Ponów”, gałąź `200`.** Nie określono odpowiedzi, gdy zapis się uda, ale warunek zamknięcia (`ModifiedAtUtc ≥ FailedModifiedAtUtc`) nie jest spełniony, np. przy cofniętym zegarze sklepu. Wynik `200` „z błędem po zmianie” obiecuje błąd zamknięty.
- **Data / API → Storage, `WooCommerceOrderErrors`.** Tabela ma tylko „jak wyżej”, bez typów i długości (`ExternalId`, `Source`, `Status`, `ClosedBy`). Pozostałe tabele mają je podane.

### Invented or Unsupported Requirements

None.

Kontrola stabilności paginacji, kanał o pojemności 1, zestaw `trash` i warunek wersji przy zamykaniu błędu wynikają z definicji sukcesu w PRD sekcja 5, z kryteriów zgłoszenia („nie tworzą duplikatów”) i z przeglądu v1. Nie są nowymi wymaganiami.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Kursor i przebieg, pkt 4 (znane ograniczenie) | PRD sekcja 5 („Usunięcie … lub przeniesienie do kosza działa tak samo”), PRD sekcja 6 ZAM-02 („pominięty webhook jest uzupełniany”) | Decyzja właściciela z 2026-11-06 zawęża regułę PRD. PRD i `docs/wdrozenia.md` sekcja 11 nie są jeszcze zaktualizowane, a ich aktualizacja nie jest w In scope (Non-Critical 4). |
| Verification (kopia testowa) | SF-2 `01-weryfikacja.md` („Właściciel potwierdził, że ma tylko produkcję”) | Weryfikacja zakłada środowisko, którego nie ma (Non-Critical 5). |
| „Synchronizuj teraz” (drugie żądanie → `AlreadyRunning`) | `JobScheduler.TryRunNowAsync`: konsument kanału wywołuje runner po kolei | Drugi przebieg może powstać po pierwszym, zamiast dostać `AlreadyRunning` (Non-Critical 2). |

Kod SF-11 i SF-12 nie przeczy pozostałym twierdzeniom specyfikacji:

- uwaga „bez dalszych zapisów” w `IScheduledJob` jest teraz zachowana (pkt 2.7);
- `JobStatusReader` wylicza `abandoned` tak samo, jak specyfikacja tego oczekuje;
- `LeaseUntil` = `MaxDuration` + 2 min daje opisane „ok. 32 min”.

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | OK | Uzupełnianie i „Ponów” idą przez `IShopOrderSync`. Ani `ShopOrderSync`, ani `Order.ApplyFromShop` nie tworzą wpłat; przy usunięciu i koszu wpłaty zostają. |
| 2. Backend decimal money | OK | Mapowanie `WooCommerceOrderPayload` bez zmian (`decimal`, najwyżej 2 miejsca). |
| 3. API-enforced access | OK | Nowe endpointy pod `/api` wymagają sesji. `CsrfValidationMiddleware` obejmuje każdy `POST` pod `/api`. Nie ma nowego endpointu anonimowego. |
| 4. Retries never duplicate | OK | Unikalność źródła i ID, blokada po ID w SF-12, dzierżawa SF-11, jeden otwarty błąd na ID. Dodatkowy przebieg z Non-Critical 2 nie tworzy duplikatów. |
| 5. Financial audit trail | OK | Audyt SF-12 z autorem systemowym `WooCommerceSync`, zgodnie z PRD sekcja 3 (D-05). Inicjator „Ponów” jest widoczny w historii błędu (`ClosedBy = retry`); system ma jedno konto. |
| 6. Secrets stay server-side | OK | Klucze REST tylko w konfiguracji serwera, Basic auth przez `https`. Komunikaty przebiegów i błędów składa kod, a nie `exception.Message`. Doprecyzowanie zakazu kluczy w adresie: Non-Critical 7. |
| 7. Module and layer boundaries | OK | Tabele w schemacie `integrations`. Orders dostępne przez `IShopOrderSync` i nowy `IShopOrderLookup` w Shared.Application. `PayloadReadResult` zmienia się wewnątrz Integrations.Application. |
| 8. No identifying data in docs | OK | `<adres-sklepu>` i fikcyjne zamówienia. Komunikaty błędów zawierają najwyżej ID zamówienia ze sklepu (powody z parsera). |

## Risk

- Recorded: high
- Assessed: high. Pierwsze wywołania do sklepu z kluczami REST. Zamówienia zmieniają się automatycznie, w tym anulowanie z kosza i z `404` w „Ponów”, co wpływa na sprzedaż. Kursor decyduje o kompletności danych. Przed merge wymagany jest przegląd przez człowieka.

## Assumptions Detected

- „Ponów” przy `404` = usunięcie, tylko dla kodów `woocommerce_rest_shop_order_invalid_id` / `woocommerce_rest_invalid_id`: podane, do potwierdzenia na sklepie.
- Trwałe usunięcie bez webhooka jest niewykrywalne: podane (decyzja właściciela z 2026-11-06).
- Błędy webhooków trafiają do historii i zamyka je udany zapis nie starszy od nieudanej wersji: podane.
- Kursor = start udanego przebiegu, margines 10 min, granice ostre, format ISO 8601 UTC: podane.
- Nakładanie przebiegów rozstrzyga dzierżawa SF-11, plus kanał o pojemności 1: podane. Gwarancja „jeden przebieg” nie wynika jednak z projektu (Non-Critical 2).
- Pierwsze pobranie od początku dnia daty uruchomienia w Europe/Warsaw: podane.
- `202` i praca w tle; żądanie z kanału ginie przy restarcie: podane.
- Parametry `status=any`, `dates_are_gmt=true`, `orderby=modified` do potwierdzenia: podane.
- `status=any` nie zwraca zamówień z kosza: podane w Behavior jako fakt, nie jako założenie do potwierdzenia (bez skutków ubocznych, bo podwójne pobranie jest idempotentne).
- Klucz tylko do odczytu, `per_page` 100, brak konfiguracji nie blokuje startu: podane.
- Ukryte: przeniesienie do kosza aktualizuje `date_modified_gmt`, a `status=trash` działa z `modified_after`. Nie podane (Non-Critical 3).
- Ukryte: zegary sklepu i serwera różnią się mniej niż czas, po którym zmiana w trakcie przebiegu wypada z okna. Bez tego `X-WP-Total` nie wykryje przesunięcia. Nie podane (Non-Critical 1). Różnica zegarów do 10 min jest podana pośrednio jako uzasadnienie marginesu.
- Ukryte: istnieje kopia testowa sklepu do prób ręcznych. Nie podane i sprzeczne z SF-2 (Non-Critical 5).
- Ukryte: hosting przekazuje nagłówek `Authorization` do WordPressa. Nie podane (Non-Critical 7).

## Recommendation

- Proceed as-is. Non-Critical 1–4 rozstrzygnąć w planie (`sdd-plan`) albo krótką poprawką specyfikacji przed planem: górna granica okna, znacznik przyjętego żądania, założenie i weryfikacja zestawu `trash`, zmiany PRD i `docs/wdrozenia.md` jako element dostawy.
