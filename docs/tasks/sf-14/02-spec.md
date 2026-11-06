# Specyfikacja zadania — SF-14

> **Druga poprawka redakcyjna wersji 4 (2026-11-06, po przeglądzie kodu `05-code-review.md`).**
> Zegar lokalny opisano zgodnie z planem NC2: zegar 60 s dla progu 25 h i timer progu wieku,
> a wiek odczytu liczy się z `now − readAt` przy każdym wyliczeniu. Przypadek brzegowy „≤ 5 s”
> zmieniono na „≤ 60 s”. Bez zmian zakresu.

> **Poprawka redakcyjna wersji 4 (2026-11-06, po przeglądzie `03-spec-review.md`: PASS WITH
> ISSUES).** Autor usunął pozostałości z NC3: pkt 25 odsyła do znaczników z pkt 34, przypadek
> brzegowy mówi „następna aktywność” zamiast „kolejne tyknięcie”, komunikat ma pięć wariantów,
> Scope odsyła do pkt 27, a kryterium komunikatu wymienia wariant `aged`. Bez zmian merytorycznych.
> Rozstrzygnięcia NC1, NC2, NC4, NC5 i niejasności zapisano w `04-implementation-plan.md`.

> **Wersja 4 (2026-11-06).** Poprawki po przeglądzie `03-spec-review-v3.md` (FAIL):
> - Critical: lokalna reguła wieku odczytu. Odczyt `/status` starszy niż dwa okresy (120 s, a w
>   `running` 20 s) daje wariant ostrożny: `dataComplete = false`, dopisek „Stan z {HH:mm}”,
>   ostrzeżenie „Stan integracji może być nieaktualny…”. Bez żądań.
> - NC1/NC2: odczyt w tle wysyła się tylko w chwili aktywności. Znaczniki `lastActivityAt` i
>   `lastReadAt`, porównanie ścisłe. Sesja wygasa najpóźniej 60 min po ostatniej aktywności,
>   tolerancja z pkt 26 usunięta.
> - NC3: odsyłacz w pkt 8 poprawiony; jedna treść wpisu do `conventions.md` (pkt 27).
> - Niejasności: „Spróbuj ponownie” = `refresh({ force: true })`; limity czasu wszystkich żądań;
>   przekroczenie limitu przy „Ponów”.
>
> Decyzje zapisano w Assumptions (pkt 32–36).

> **Wersja 3 (2026-11-06).** Poprawki po przeglądzie `03-spec-review-v2.md` (FAIL):
> - Critical: usunięty wyjątek odświeżania bez aktywności podczas `running`. Każdy odczyt w tle
>   podlega jednej regule aktywności (okres 10 s w `running`, 60 s poza nim). Bierny obserwator
>   zobaczy wynik przy najbliższej akcji; to znane ograniczenie. Wpis do `conventions.md` jest bez
>   furtki.
> - NC1: klient zwraca Promise (`firstValueFrom`), limit 15 s przez operator `timeout` w
>   `integration-api.ts`. Spóźnione odpowiedzi `/status` i listy błędów są ignorowane według
>   numeru sekwencyjnego odczytu (zamiast anulowania).
> - NC2: migotanie wariantów komunikatu po `202` jako akceptowany przypadek brzegowy.
> - NC3: wskaźnik bez `title`. Dostępna nazwa zaczyna się od widocznej etykiety, a czas jest w
>   ukrytym tekście po niej.
> - NC4: unikalna nazwa „Ponów” także w `busy`, przez `busyLabel`.
> - NC5: poprawione uzasadnienie powodu wyłączenia.
> - Niejasności: nieudany odczyt nie kończy serii `running`; `unknown` zachowuje ostatnią
>   odpowiedź; przewijanie dotykiem a kółko.
>
> Decyzje zapisano w Assumptions (pkt 25–31). Punkty 20 i 22 z wersji 2 są zastąpione.

> **Wersja 2 (2026-11-06).** Poprawki po przeglądzie `03-spec-review-v1.md` (FAIL):
> - C1: komunikat renderuje się zawsze; przy danych kompletnych to zwarta linia „Ostatnia pełna
>   kontrola”. Wskaźnik w nagłówku podaje ten czas w `title` i w tekście dla czytników ekranu.
> - C2: w stanie `running` `dataComplete = true` tylko przy `lastRun.status = succeeded` i braku
>   powodów niekompletności.
> - NC1: podczas `running` odświeżanie co 10 s bez wymogu aktywności, najdłużej 35 min.
> - NC2: `refresh()` po akcji anuluje żądanie w toku i wysyła nowe.
> - NC3: aktywność to tylko `keydown`, `pointerdown` i powrót na widoczną kartę.
> - NC4: limit czasu `GET /status` 15 s.
> - NC5: dzień zmiany czasu jako akceptowany przypadek brzegowy.
> - NC6: ujednolicona obsługa `csrf_invalid`.
> - NC7: dostępna nazwa „Ponów” przez ukryty tekst; `aria-describedby` dla powodu wyłączenia (nowe
>   wejście `describedBy` w `app-button`).
> - NC8: rozszerzone kryteria akceptacji.
> - Dopisane rozstrzygnięcia niejasności: akcje w `loading`/`unknown`, ogłaszanie `unknown`,
>   definicja „ostatniej aktywności”, `404` bez kodu, równoległe „Ponów”.
>
> Rozstrzygnięcia przekazał koordynator; zapisano je w Assumptions (pkt 18–24).

## Source

[SF-14](https://standev.youtrack.cloud/issue/SF-14)

## Goal

Frontend pokazuje stan integracji WooCommerce zgodnie z INT-01 i PRD sekcja 5. Stan wylicza czysta
funkcja z danych `GET /api/integrations/woocommerce/status` (SF-13), w kolejności „Synchronizacja
trwa” → „Błąd” → „Dane niekompletne” → „Aktualne”, z dodatkowym stanem „Nie skonfigurowano”
sprawdzanym przed nimi. Stan jest widoczny w nagłówku powłoki jako tekst z ikoną i link do ekranu
„Integracja”. Ekran zastępuje obecny placeholder i pokazuje „Ostatnią pełną kontrolę”, ostatni
przebieg z komunikatem, akcję „Synchronizuj teraz” oraz historię błędów zamówień z filtrem,
stronicowaniem, akcją „Ponów” i linkiem do zamówienia. Zadanie dostarcza też komponent komunikatu
o niekompletnych danych, gotowy do osadzenia przez SF-19 i SF-20. Backend się nie zmienia.

## Context

- **Frontend (`app/src/app/`)**
  - `features/integration/` — obszar sekcji „Integracja” (trasa `/integracja`, leniwie ładowana,
    `app.routes.ts`). Dziś jest tu tylko placeholder `integration-page` (`app-empty-state` „Ten
    widok powstanie w kolejnych etapach.”).
  - `layout/app-shell` — nagłówek z nazwą produktu, sekcją, `app-main-nav` i `app-user-menu`
    (SF-5). Design guide sekcja 3 wymaga w nagłówku stanu synchronizacji, a dziś go tam nie ma.
  - `shared/ui/` (`app-status-badge`, `app-data-table`, `app-button`, `app-loading-state`,
    `app-empty-state`, `app-error-state`), `shared/format/` (`formatDateTime`/`plDateTime` →
    Europe/Warsaw), `core/http/` (`provideAppHttp`, `toApiError`), `core/auth/` (interceptor
    `401`).
  - Wzorzec klienta, serwisu i testów: `features/orders/` (`orders-api.ts`, `orders.service.ts`,
    `*.spec.ts` obok kodu, `HttpTestingController`).
- **Backend (bez zmian)** — moduł Integrations, `StoneFlow.Integrations.Api/WooCommerceEndpoints.cs`
  (SF-13):
  - `GET /status`, `GET /errors`, `POST /errors/{id}/retry`, `POST /sync`;
  - kody w `IntegrationsProblems.cs`;
  - `GetSyncStatus.cs`: „stany INT-01 wylicza frontend”, a `running` bez ważnej dzierżawy API
    zwraca jako `abandoned`.
- **Zachowanie sesji (Access, SF-6)** — każde uwierzytelnione żądanie przedłuża sesję
  (`SessionCookie.ValidatePrincipalAsync` → `ShouldRenew = true`; 60 min bezczynności, 12 h
  limitu). Od tego zależy projekt odświeżania (punkt 3 w Behavior).

## Scope

### In scope

- Typy DTO i klient `integration-api.ts` dla czterech endpointów SF-13.
- Czysta funkcja `integrationStateOf` (stan, powody niekompletności, `dataComplete`) z etykietami i
  tonami stanów.
- Serwis stanu `IntegrationStatus` (sygnały, odświeżanie zależne od aktywności, ogłaszanie zmian).
- Wskaźnik w nagłówku powłoki (`layout/integration-indicator`).
- Ekran „Integracja”:
  - stan i „Ostatnia pełna kontrola”;
  - ostatni przebieg;
  - „Synchronizuj teraz”;
  - lista błędów z filtrem i stronicowaniem;
  - „Ponów”;
  - link do zamówienia.
- Komponent `app-incomplete-data-notice` dla widoków raportowych, bez osadzania. Zawsze pokazuje
  „Ostatnią pełną kontrolę”, a przy niekompletnych danych także ostrzeżenie.
- Rozszerzenie `app-status-badge` o slot ikony (zgodne wstecz).
- Rozszerzenie `app-button` o opcjonalne wejście `describedBy` (`aria-describedby` na natywnym
  przycisku), zgodne wstecz.
- Testy jednostkowe frontendu (Vitest) dla funkcji, klienta, serwisów i komponentów. Dostosowanie
  istniejących testów powłoki do nowego żądania `GET /status`.
- Wpis w `.sdd/context/conventions.md` (Frontend → „State and data fetching”) o regule odświeżania
  w tle (punkt 3 w Behavior; treść w Assumptions 27).

### Out of scope

- Osadzenie komunikatu w widokach raportowych (SF-19 i SF-20, zgłoszenie: „Out of scope”).
- Zmiany backendu: wyliczanie stanu w API, nowe pola, zmiana przedłużania sesji.
- Konfiguracja kluczy REST z UI. Nie ma na to endpointu, a sekrety zostają po stronie serwera.
- Wyniki zadań harmonogramu (`GET /api/jobs`) i inne zadania niż uzupełnianie WooCommerce.
- Powiadomienia (e-mail, toast) o zmianie stanu.
- Rozwiązanie otwartych pozycji SF-13: C4, C5, S3–S5, T2, T5, T6.

## Behavior

### 1. Wyliczanie stanu — `integrationStateOf(status, nowMs)`

Czysta funkcja w `features/integration/integration-state.ts`, bez Angulara. Wejście to odpowiedź
`/status` i bieżący czas w milisekundach. Wynik:

- `kind`: `notConfigured` | `running` | `error` | `incomplete` | `current`;
- `reasons`: lista z `openErrors`, `stale` i `neverSucceeded`;
- `dataComplete` (bool).

Kroki sprawdzane w podanej kolejności (pierwszy pasujący wygrywa):

1. `configured = false` → `notConfigured`.
2. `isRunning = true` → `running`. Dotyczy także żądania „Synchronizuj teraz” przyjętego i jeszcze
   nierozpoczętego.
3. `lastRun` istnieje i jego `status` jest różny od `succeeded` → `error`. Obejmuje `failed`,
   `abandoned` i każdą nieznaną wartość. `running` przy `isRunning = false` API zamienia na
   `abandoned`, ale funkcja i tak traktuje go jako `error`.
4. Powody niekompletności, wyliczane zawsze (także dla kroków 1–3):
   - `openErrors > 0` → `openErrors`;
   - `lastSuccessAtUtc = null` albo nieczytelna data → `neverSucceeded`;
   - `nowMs − lastSuccessAtUtc > 25 h` (ściśle więcej, „ponad 25 godzin”) → `stale`.

   Jeśli lista nie jest pusta → `incomplete`.
5. W pozostałych przypadkach → `current`. Dotyczy to także `lastRun = null` przy świeżym sukcesie.

Reguła `dataComplete`:

- `current` → `true`;
- `running` → `true` tylko wtedy, gdy `lastRun` istnieje, ma `status = succeeded`, a lista
  powodów jest pusta. W każdym innym przypadku `false`, także przy `lastRun = null`, przy
  `failed`/`abandoned` przed przyjętym „Synchronizuj teraz” i przy `lastRun.status = running`.
  Wynik trwającego przebiegu nie jest jeszcze znany, a ostatni znany wynik musi być sukcesem
  (wariant ostrożny, INT-01);
- `notConfigured`, `error`, `incomplete` → `false`.

Etykiety stanów (tekst widoczny zawsze):

| `kind` | Etykieta | Ton `app-status-badge` | Ikona (inline SVG, `aria-hidden`) |
|---|---|---|---|
| `current` | Aktualne | `success` | znacznik „✓” w kółku |
| `running` | Synchronizacja trwa | `info` | strzałki odświeżania, obrót tylko przy `motion-safe` |
| `error` | Błąd | `error` | „×” w kółku |
| `incomplete` | Dane niekompletne | `warning` | trójkąt z wykrzyknikiem |
| `notConfigured` | Nie skonfigurowano | `warning` | wtyczka/przekreślenie |
| (serwis) `loading` | Sprawdzanie stanu… | `neutral` | brak |
| (serwis) `unknown` | Stan nieznany | `neutral` | znak zapytania |

Stany `loading` i `unknown` nie pochodzą z funkcji. Ustala je serwis: `loading` przed pierwszą
odpowiedzią, `unknown` po nieudanym odczycie. Dla `unknown` `dataComplete = false`, bo stan bez
odczytu nie może sugerować kompletności.

### 2. Serwis stanu — `IntegrationStatus`

Plik `features/integration/integration-status.ts`, `providedIn: 'root'`.

Sygnały:

- `status` — ostatnia udana odpowiedź albo `null`. Nieudany odczyt jej nie czyści, więc w
  `unknown` np. `configured` pochodzi z poprzedniej odpowiedzi;
- `view` — `loading` | `unknown` | wynik funkcji z `now`, z flagą `aged` (reguła wieku odczytu
  niżej) i czasem `readAt`;
- `dataComplete` — `false` także przy `aged = true`;
- `lastFullCheckAtUtc` — z `status`. W `unknown` to ostatni znany czas, wyświetlany z dopiskiem
  „(ostatni znany odczyt)”;
- `announcement` — tekst ogłoszenia dla czytników ekranu.

`refresh({ force })`:

- klient `integration-api.ts` zwraca Promise (`firstValueFrom`, konwencja obszaru). Limity czasu
  daje operator RxJS `timeout` przed `firstValueFrom`, a przekroczenie limitu przy `/status` to
  błąd odczytu (`unknown`):
  - `GET /status` — 15 s;
  - `GET /errors` — 15 s;
  - `POST /sync` — 15 s;
  - `POST /errors/{id}/retry` — 45 s, bo backend woła sklep z limitem 30 s;
- każde wysłanie odczytu (w tle albo wymuszonego) ustawia `lastReadAt` na chwilę wysłania;
- bez `force` (zegar, powrót na kartę, start) wywołanie w trakcie żądania dołącza do niego;
- z `force: true` (po akcjach „Synchronizuj teraz” i „Ponów”, po „Spróbuj ponownie” w każdym
  miejscu ekranu) wysyła nowe żądanie, nawet gdy jakieś jest w toku;
- **numer sekwencyjny odczytu:** każde żądanie dostaje kolejny numer. Odpowiedź (sukces albo błąd)
  zmienia stan tylko wtedy, gdy jej numer jest najnowszy, a starsze są ignorowane. Odczyt sprzed
  akcji nie nadpisuje więc stanu po niej. Ta sama reguła dotyczy listy błędów w
  `integration-errors.ts`;
- sukces zapisuje odpowiedź i aktualizuje `now`;
- błąd sieci, `5xx`, przekroczenie limitu czasu albo inny błąd niż `401` daje `unknown`;
- `401` obsługuje globalny interceptor (wylogowanie).

**Reguła wieku odczytu:**

- Serwis zapamiętuje `readAt`, czyli czas odpowiedzi ostatniego udanego odczytu `/status` według
  zegara klienta.
- Gdy `now − readAt` przekracza **dwa okresy** odświeżania (**120 s**, a gdy wyświetlany stan to
  `running` — **20 s**), wyliczony stan przechodzi w wariant ostrożny (`aged = true`):
  - etykieta stanu bez zmian, z dopiskiem „Stan z {formatTime(readAt)}” (`HH:mm`, Europe/Warsaw)
    we wskaźniku (widocznie i w nazwie dostępnej) oraz w komunikacie;
  - `dataComplete = false`, więc komunikat jest ostrzeżeniem: **„Stan integracji może być
    nieaktualny. Wyniki mogą być niepełne.”**, z linią „Ostatnia pełna kontrola: …”.
- Reguła nie wysyła żadnego żądania. Pierwsza aktywność użytkownika uruchamia natychmiastowy
  odczyt (Behavior 3), a jego udana odpowiedź kończy wariant ostrożny.
- Nie dotyczy `loading` ani `unknown`, bo te stany i tak nie dają kompletności.
- Wejście w `aged` jest ogłaszane raz: „Stan integracji: {etykieta}, stan z {HH:mm}”.

`start()` i `stop()`:

- wywołuje je wskaźnik w nagłówku;
- oba są idempotentne;
- `stop()` czyści timery i nasłuch zdarzeń, a powłoka znika po wylogowaniu.

`announcement` zmienia się przy każdej zmianie wyświetlanego stanu, czyli `kind` albo przejścia z
lub do `unknown`, nigdy przy pierwszym odczycie (wyjście z `loading`). Tekst: „Stan integracji:
{etykieta}”, np. „Stan integracji: Stan nieznany”.

### 3. Odświeżanie

Rozwiązanie wynika z przedłużania sesji przy każdym żądaniu: stałe odpytywanie podtrzymywałoby
sesję otwartej karty bez końca, wbrew DOST-01 (60 min bezczynności).

- **Aktywność użytkownika** to tylko celowe działania: `keydown` i `pointerdown` na dokumencie
  (nasłuch pasywny) oraz powrót na widoczną kartę (`visibilitychange` → `visible`). Ruch kursora i
  kółko się nie liczą, bo pojawiają się także nad widocznym, nieużywanym oknem. Żądania (własne
  `/status` ani inne wywołania API) nie są aktywnością.
- **Dwa znaczniki:**
  - `lastActivityAt` — chwila ostatniego zdarzenia aktywności;
  - `lastReadAt` — chwila wysłania ostatniego odczytu `/status`, w tle albo wymuszonego.
- **Start:** natychmiastowy `refresh()`.
- **Zegar lokalny co 60 s i timer progu** aktualizują `now`, bez żądań. Zegar obsługuje granicę
  25 h. Timer ustawiony na chwilę `readAt + 2 okresy` przyspiesza przejście w wariant ostrożny, a
  wiek odczytu jest liczony z `now − readAt` przy każdym wyliczeniu i przeliczany przy każdym
  tyknięciu oraz przy powrocie na widoczną kartę (opóźniony timer, np. po uśpieniu, nie zawyża
  kompletności).
- **Jedna reguła dla wszystkich odczytów w tle** (bez wyjątków). Okres to **10 s**, gdy ostatni
  wyświetlany stan to `running`, i **60 s** w pozostałych przypadkach. Odczyt w tle jest
  dozwolony, gdy spełnione są wszystkie warunki:
  - karta jest widoczna (`document.visibilityState === 'visible'`);
  - `lastActivityAt > lastReadAt` (porównanie ścisłe);
  - `now − lastActivityAt ≤ okres`;
  - `now − lastReadAt ≥ okres` (odczyt jest należny).

  Odczyt w tle wysyła się wyłącznie w obsłudze zdarzenia aktywności, czyli w chwili
  `lastActivityAt` (`now − lastActivityAt = 0`). Zegar nigdy go sam nie wysyła. Zdarzenie, które
  przychodzi przed upływem okresu, tylko aktualizuje `lastActivityAt`, a odczyt czeka na kolejną
  aktywność po upływie okresu. W wariancie ostrożnym odczyt jest zawsze należny, więc pierwsza
  aktywność odświeża od razu.
- **Skutek dla sesji:** każde żądanie `/status` wychodzi w chwili celowej aktywności, a żądania
  wymuszone zaraz po akcji (która też jest aktywnością). Bezczynna sesja wygasa więc najpóźniej
  60 min po ostatniej aktywności (DOST-01), z dokładnością do czasu trwania żądań wywołanych tą
  aktywnością. Nie ma tolerancji „+ jeden okres”.
- **Okres 10 s w `running`:** obowiązuje, dopóki ostatni wyświetlany stan to `running`. Nieudany
  odczyt (błąd, limit 15 s → `unknown`) nie kończy tej serii, więc następny odczyt po aktywności
  jest należny po 10 s. Seria kończy się przy pierwszym udanym odczycie innym niż `running`.
- **Znane ograniczenie:** przy bezczynnej karcie stan nie odświeża się aż do najbliższej akcji
  użytkownika. Wtedy następuje natychmiastowy odczyt, a `aria-live` ogłasza zmianę dopiero po nim.
  Reguła wieku odczytu gwarantuje, że po dwóch okresach UI przechodzi w wariant ostrożny.
  Zgodność z INT-01 opisuje Assumptions 25 i 32.
- **Powrót na kartę** (`visibilitychange` → `visible`) to aktywność. Gdy odczyt jest należny
  albo trwa wariant ostrożny, następuje natychmiastowy `refresh()`.
- **Po akcjach** (`refresh({ force: true })`):
  - po „Synchronizuj teraz” — `202` albo `409`;
  - po „Ponów” — każdy wynik poza błędem sieci i `400 csrf_invalid`;
  - po `400 csrf_invalid` nie ma odświeżenia (jedna reguła dla obu akcji): stan się nie zmienił,
    a użytkownik dostaje polecenie odświeżenia strony.
- **Koniec synchronizacji:** przejście `isRunning` z `true` na `false` odczytane na ekranie
  integracji przeładowuje bieżącą stronę listy błędów (przebieg mógł dodać błędy).

### 4. Wskaźnik w nagłówku — `layout/integration-indicator`

- Umieszczony w `app-shell.html` między `app-main-nav` a `app-user-menu`, w tym samym wierszu
  flex z zawijaniem (design guide sekcja 3: „stan synchronizacji” w nagłówku).
- Element to `<a routerLink="/integracja">` z `IntegrationStateBadge` (ikona i etykieta),
  `min-h-11` (obszar 44 px).
- Dostępna nazwa wynika z treści linku i zaczyna się od widocznej etykiety (WCAG 2.5.3):
  „{etykieta}” (widoczna odznaka), a po niej tekst wizualnie ukryty „, ostatnia pełna kontrola:
  {formatDateTime(lastFullCheckAtUtc)}{ (ostatni znany odczyt) w `unknown`}. Stan integracji —
  przejdź do ekranu Integracja”. Brak czasu → „Brak danych”, a w `loading` sama etykieta. W
  wariancie `aged` za odznaką widać dopisek „Stan z {HH:mm}”, który wchodzi też do nazwy zaraz po
  etykiecie. Link
  **nie ma** `title`, więc czytnik nie czyta czasu dwa razy. Widoczny czas przy wynikach zapewnia
  `app-incomplete-data-notice` (Assumptions 28).
- Osobny, wizualnie ukryty region `role="status"` (`aria-live="polite"`) wyświetla
  `announcement`.
- Przy wejściu wywołuje `start()`, przy zniszczeniu `stop()` (`DestroyRef`).
- Stan nie zależy od koloru: tekst jest zawsze widoczny, ikona go wzmacnia.

### 5. Ekran „Integracja” — `/integracja`

Nagłówek strony: `<h1>Integracja</h1>`.

**Sekcja „Stan synchronizacji”** (karta, `<h2>`):

- Stan `loading` → `app-loading-state` z etykietą „Wczytywanie stanu integracji…”.
- Stan `unknown` → `app-error-state` z tekstem „Nie udało się wczytać stanu integracji.”;
  „Spróbuj ponownie” wywołuje `refresh({ force: true })`.
- Stan znany:
  - `IntegrationStateBadge` z wyjaśnieniem stanu (powody poniżej w stałej kolejności);
  - **„Ostatnia pełna kontrola: {formatDateTime(lastSuccessAtUtc)}”** (Europe/Warsaw); dla `null`
    — „Brak danych” oraz „Pełna kontrola jeszcze się nie odbyła.”;
  - **„Ostatni przebieg”**:
    - status: Udany / Nieudany / Przerwany / W toku;
    - źródło: Automatyczny (23:00) / Ręczny;
    - początek i koniec (`formatDateTime`; brak końca → „Brak danych”);
    - „Przetworzone zamówienia: {n}”, „Błędy zamówień: {n}”;
    - `message`, jeśli jest (np. „Przerwany (restart lub awaria)”);
    - `lastRun = null` → „Uzupełnianie jeszcze się nie odbyło.”

Wyjaśnienia stanu:

| Warunek | Tekst |
|---|---|
| `notConfigured` | „Integracja ze sklepem nie ma skonfigurowanych kluczy REST. Dzienne uzupełnianie, „Synchronizuj teraz” i „Ponów” nie działają.” |
| `running` | „Trwa uzupełnianie zamówień ze sklepu.” |
| `error` | „Ostatni przebieg uzupełniania nie powiódł się. Wyniki mogą być niepełne.” |
| powód `openErrors` | „Otwarte błędy zamówień: {n}.” |
| powód `stale` | „Od ostatniej pełnej kontroli minęło ponad 25 godzin.” |
| powód `neverSucceeded` | „Pełna kontrola jeszcze się nie odbyła.” |
| `current` | „Ostatnie uzupełnianie się powiodło i nie ma otwartych błędów zamówień.” |

**Akcja „Synchronizuj teraz”** (`app-button variant="primary"`, jedyna główna akcja strony):

- `busy` z etykietą „Uruchamianie…”, co blokuje podwójne kliknięcie.
- Przy `running`, `notConfigured` i `loading` przycisk jest wyłączony, a obok widać powód:
  - „Synchronizacja trwa. Poczekaj na jej zakończenie.”;
  - tekst `notConfigured`;
  - „Trwa sprawdzanie stanu integracji.”

  Wymóg design guide sekcja 2 („wyłączenie kontrolki nie może ukrywać instrukcji”) spełnia ten
  **widoczny tekst powodu obok przycisku**. Dodatkowo element z powodem ma `id`, a przycisk
  wskazuje go przez `aria-describedby` (nowe wejście `describedBy` w `app-button`). Pomaga to
  czytnikowi w trybie przeglądania, bo wyłączony przycisk nie przyjmuje fokusu.
- W `unknown` przycisk jest dostępny, bo o wyniku decyduje API (`202`/`409`).
- Wyniki trafiają do regionu wyniku akcji (`role="status"`; błędy `role="alert"`). Komunikat
  zostaje do następnej akcji, nie znika jak toast:

| Odpowiedź | Komunikat | Dalej |
|---|---|---|
| `202` | „Synchronizacja uruchomiona. Stan odświeży się automatycznie.” | `refresh({ force: true })` |
| `409` `sync_in_progress` | `detail` albo „Synchronizacja już trwa. Poczekaj na jej zakończenie.” | `refresh({ force: true })` |
| `409` `integration_not_configured` | `detail` albo tekst `notConfigured` | `refresh({ force: true })` |
| `400` `csrf_invalid` | „Odśwież stronę i spróbuj ponownie.” | bez odświeżenia |
| sieć / `5xx` | `NETWORK_ERROR_MESSAGE` | — |
| przekroczenie limitu 15 s | „Nie udało się potwierdzić uruchomienia synchronizacji. Sprawdź stan powyżej.” | `refresh({ force: true })` |
| `401` | interceptor (logowanie) | — |

**Sekcja „Błędy zamówień”** (`<h2>`; `app-data-table`, caption „Błędy zamówień”, strona 25,
kolejność z API: od ostatniego wystąpienia):

- Filtr (`tableFilters`): pole wyboru „Status”: Otwarte (domyślnie) / Zamknięte / Wszystkie
  (`open` / `closed` / `all`).
  - Wartość inna niż domyślna daje aktywny filtr „Status: Zamknięte” albo „Status: Wszystkie”.
  - Usunięcie filtra albo „Wyczyść filtry” wraca do „Otwarte”.
  - Zmiana filtra ustawia stronę 1.
- Kolumny (bez sortowania, bo API sortuje na stałe):
  1. **Zamówienie** (szablon):
     - gdy `orderId` istnieje → link `/zamowienia/{orderId}` „Zamówienie {orderNumber}” (bez
       numeru → „Zamówienie w ERP”);
     - pod spodem zawsze „ID w sklepie: {externalId}”.
  2. **Błąd** — `message` i pod nim drobny tekst „Kontekst: {context}”.
  3. **Źródło** — Webhook / Uzupełnianie / Ponowienie (`webhook` / `backfill` / `retry`).
  4. **Próby** — `attempts`.
  5. **Pierwsze wystąpienie** i **Ostatnie wystąpienie** — `formatDateTime`.
  6. **Status** (szablon) — `app-status-badge`: „Otwarty” (`warning`) albo „Zamknięty”
     (`neutral`); dla zamkniętego także „{formatDateTime(closedAtUtc)}, {Webhook | Uzupełnianie |
     Ponów}”.
  7. **Akcja** (szablon) — „Ponów” (`app-button`), tylko dla otwartych.
     Widoczny przy `configured = true` i w stanie `unknown` (decyduje API). Ukryty przy
     `notConfigured` (z tekstem powodu nad listą) i wyłączony w `loading`. Dostępna nazwa
     „Ponów zamówienie {externalId}” powstaje z treści przycisku: widoczne „Ponów” i
     `<span class="sr-only"> zamówienie {externalId}</span>`. Nazwa zaczyna się od widocznej
     etykiety (WCAG 2.5.3). W `busy` `app-button` ukrywa projekcję, więc unikalną nazwę daje
     istniejące wejście `busyLabel` = „Ponawianie zamówienia {externalId}…”, które jest zarazem
     widocznym tekstem i nazwą. `app-button` nie wymaga zmian dla tej kolumny.
- Stany listy przejmuje `app-data-table`:
  - ładowanie;
  - błąd „Nie udało się wczytać błędów synchronizacji.” z ponowieniem;
  - pustka bez filtra: „Brak otwartych błędów zamówień” z opisem „Błędy zapisu zamówień ze sklepu
    pojawią się tutaj.”;
  - pustka z filtrem: „Brak błędów dla wybranego filtra” z „Wyczyść filtry”.
- „Ponów” działa niezależnie od trwającej synchronizacji (SF-13). Każdy wiersz ma własny stan
  `busy`. Ponowne kliknięcie tego samego wiersza w toku nie wysyła żądania, a równoległe „Ponów” w
  różnych wierszach jest dozwolone. Wynik trafia do regionu wyniku sekcji (`role="status"` /
  `role="alert"`), który pokazuje wynik ostatnio zakończonej akcji. Przeładowanie listy po każdym
  wyniku dostaje nowy numer sekwencyjny, a odpowiedzi starszych przeładowań są ignorowane, więc
  wygrywa ostatnie:

| Odpowiedź | Komunikat | Dalej |
|---|---|---|
| `200`, `status = closed` | „Zamówienie {orderNumber ?? externalId} zapisane. Błąd zamknięty.” | przeładowanie strony listy, `refresh({ force: true })` |
| `200`, `status = open` | „Zamówienie {…} zapisane, ale błąd pozostaje otwarty. Spróbuj ponownie później.” | jw. |
| `404` `error_not_found` | `detail` albo „Nie znaleziono błędu synchronizacji.” | jw. |
| `409` `error_closed` | `detail` albo „Ten błąd jest już zamknięty.” | jw. |
| `409` `integration_not_configured` | `detail` albo tekst `notConfigured` | jw. |
| `502` `retry_failed` | `detail` albo „Sklep nie zwrócił zamówienia. Błąd pozostaje otwarty; spróbuj ponownie później.” | jw. |
| `503` `retry_unavailable` | `detail` albo „Nie udało się zapisać zamówienia. Błąd pozostaje otwarty; spróbuj ponownie później.” | jw. |
| `404` bez kodu albo inny `4xx` z nieznanym kodem | `detail` albo „Nie udało się ponowić. Spróbuj ponownie później.” | jw. |
| `503` bez kodu, `5xx`, sieć | `NETWORK_ERROR_MESSAGE` | bez przeładowania i bez odświeżenia |
| przekroczenie limitu 45 s | „Nie udało się ponowić. Spróbuj ponownie później.” | wiersz wychodzi z `busy`, przeładowanie listy, `refresh({ force: true })` |
| `400` `csrf_invalid` | „Odśwież stronę i spróbuj ponownie.” | bez przeładowania i bez odświeżenia |

`toApiError` sprowadza każdy `5xx` do `network` i gubi `code`, więc metoda `retry` w
`integration-api.ts` sama czyta `HttpErrorResponse`. Dla `502` i `503` z kodem
`retry_failed`/`retry_unavailable` w treści zwraca odpowiedni wynik z `detail`, a pozostałe
przypadki przekazuje do `toApiError`. `core/http/api-error.ts` zostaje bez zmian.

Przeładowanie listy, po którym bieżąca strona jest pusta, choć `page > 1` (np. zamknięto ostatni
błąd na stronie), ładuje stronę `page − 1`.

### 6. Komunikat o niekompletnych danych — `app-incomplete-data-notice`

- Plik `features/integration/incomplete-data-notice.ts`. Komponent nie ma wejść. Czyta
  `IntegrationStatus`, więc każdy widok pokazuje ten sam stan co nagłówek i nie powtarza warunku.
- Renderuje się **zawsze**, bo PRD sekcja 5 wymaga, żeby „przy wynikach zawsze” był widoczny
  czas „Ostatniej pełnej kontroli”. Ma pięć wariantów:
  - **dane kompletne** (`dataComplete = true`) — zwarta linia drobnego tekstu w `--sf-muted`, bez
    ramki, ikony i ostrzeżenia: „Ostatnia pełna kontrola: {formatDateTime(lastFullCheckAtUtc)}”
    oraz link „Integracja” (`/integracja`);
  - **dane niekompletne** (`dataComplete = false`, stan znany) — ostrzeżenie w ramce w tonie
    `--sf-warning` z ikoną trójkąta (`aria-hidden`): **„Nie pobrano wszystkich zamówień. Wyniki
    mogą być niepełne.”** (design guide sekcja 8), pod nim ta sama linia „Ostatnia pełna kontrola:
    …” i link „Przejdź do integracji”;
  - **`unknown`** — to samo ostrzeżenie z tekstem **„Nie udało się sprawdzić stanu integracji.
    Wyniki mogą być niepełne.”**;
  - **wariant ostrożny wieku odczytu** (`aged`) — ostrzeżenie z tekstem **„Stan integracji może być
    nieaktualny. Wyniki mogą być niepełne.”**, dopiskiem „Stan z {HH:mm}” i linią „Ostatnia pełna
    kontrola: …”;
  - **`loading`** — sama linia „Ostatnia pełna kontrola: Sprawdzanie…”, bez ostrzeżenia, w tym
    samym rozmiarze co wariant kompletny (bez skoku układu). Limit 15 s ogranicza czas trwania
    tego wariantu.

  Brak czasu → „Brak danych”.
- Żaden wariant nie ma `role="alert"`. Zmianę stanu ogłasza region nagłówka, więc komunikat nie
  powtarza ogłoszenia.
- Nie wywołuje `start()`. Odświeżanie zależy od powłoki, w której zawsze jest wskaźnik.
- W SF-14 nie jest osadzony w żadnym widoku.

## Data / API

### Storage

- None. Backend i baza bez zmian.

### Endpoints

Wykorzystywane bez zmian (SF-13):

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/integrations/woocommerce/status` | — | `200` `SyncStatusResponse` | sesja | jedno konto właściciela |
| GET | `/api/integrations/woocommerce/errors` | `status=open\|closed\|all`, `page`, `pageSize` (25) | `200` `OrderErrorListResponse`; `400` ValidationProblem | sesja | jw. |
| POST | `/api/integrations/woocommerce/errors/{id}/retry` | — (XSRF) | `200` `OrderErrorResponse`; `404` `error_not_found`; `409` `error_closed` / `integration_not_configured`; `502` `retry_failed`; `503` `retry_unavailable` | sesja + CSRF | jw. |
| POST | `/api/integrations/woocommerce/sync` | — (XSRF) | `202`; `409` `sync_in_progress` / `integration_not_configured` | sesja + CSRF | jw. |

Adresy są względne (`/api/...`). Nagłówek `X-XSRF-TOKEN` dokleja `provideAppHttp()` z ciasteczka
`XSRF-TOKEN`, tak jak w zapisach zamówień. Nie trzeba osobnego `GET /api/auth/csrf`, bo token
ustawia sesja.

### Types crossing a boundary

`features/integration/integration-models.ts`, JSON camelCase; znaczniki czasu to ciągi ISO 8601 z
przesunięciem:

- `SyncStatus { configured: boolean; lastSuccessAtUtc: string | null; lastRun: SyncRun | null;
  isRunning: boolean; openErrors: number }`
- `SyncRun { status: 'running' | 'succeeded' | 'failed' | 'abandoned' | string; startedAtUtc:
  string; finishedAtUtc: string | null; source: 'schedule' | 'manual' | string; ordersProcessed:
  number; errors: number; message: string | null }`
- `OrderError { id: string; externalId: string; orderId: string | null; orderNumber: string |
  null; status: 'open' | 'closed'; source: 'backfill' | 'webhook' | 'retry'; firstOccurredAtUtc:
  string; lastOccurredAtUtc: string; attempts: number; message: string; context: string;
  closedAtUtc: string | null; closedBy: 'backfill' | 'webhook' | 'retry' | null }`
- `OrderErrorPage { items: OrderError[]; page: number; pageSize: number; total: number }`
- Wyniki klienta:
  - `SyncRequestOutcome` = `accepted` | `inProgress` | `notConfigured` | błąd `ApiError`;
  - `RetryOutcome` = `done(OrderError)` | `notFound` | `closed` | `notConfigured` | `shopFailed` |
    `unavailable`, każdy z `detail`, albo błąd `ApiError`.

Nieznane wartości słownikowe (`status`, `source`) wyświetlają surowy kod zamiast pustego pola.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sales separate from cash | Nie dotyczy. Brak kwot i raportów. |
| 2. Backend decimal money | Nie dotyczy. Frontend nie liczy kwot. |
| 3. API-enforced access | Wszystkie cztery endpointy wymagają sesji (polityka domyślna hosta), a zapisy CSRF. UI nie zastępuje tej ochrony, a `401` kończy sesję przez istniejący interceptor. Każde odczytanie w tle wymaga widocznej karty i aktywności użytkownika od poprzedniego odczytu, bez wyjątków (Behavior 3). Odczyt w tle wychodzi tylko w chwili zdarzenia aktywności, a wymuszony zaraz po akcji użytkownika. Bezczynna sesja wygasa więc najpóźniej 60 min po ostatniej aktywności (DOST-01), bez tolerancji. |
| 4. Retries never duplicate | „Ponów” i „Synchronizuj teraz” blokują podwójne kliknięcie (`busy`, jedno żądanie na wiersz). Brak duplikatów zapewnia backend (SF-12/SF-13: unikalność, blokada, dzierżawa). |
| 5. Financial audit trail | Nie dotyczy. UI nie zapisuje danych finansowych; audyt zmian z „Ponów” robi backend (SF-13). |
| 6. Secrets stay server-side | UI pokazuje tylko pola z API (komunikaty bez sekretów według SF-13). Brak kluczy, adresu sklepu i konfiguracji w bundlu. Frontend nie loguje odpowiedzi. Całą komunikację z WooCommerce prowadzi backend. |
| 7. Module and layer boundaries | Frontend woła tylko API Integrations. Link do zamówienia używa `orderId` z odpowiedzi, bez wywołań API zamówień. Logika stanu jest w czystej funkcji poza komponentami (`rules.md`: brak logiki biznesowej w komponentach). |
| 8. No identifying data in docs | Dane testowe są fikcyjne (np. `externalId` „1001”, numer „Z/2026/0001”), bez nazwy i domeny sklepu oraz danych klientów. |

## Risk

**medium** (bez zmian względem `task.md`). Wskaźnik w nagłówku i komunikat są wspólne dla całej
aplikacji (powłoka, SF-19 i SF-20), więc błąd wyliczenia może sugerować kompletność danych w
raportach (INT-01). Odświeżanie w tle dotyka zasady wygasania sesji (DOST-01). Zadanie nie zmienia
pieniędzy, danych klientów ani operacji nieodwracalnych.

## UI

- **Trasy:**
  - `/integracja` — istniejąca, leniwa; `integration-page` zastępuje placeholder;
  - link do zamówienia prowadzi do istniejącej `/zamowienia/:id`.
- **Komponenty i pliki:**

| Plik | Rola |
|---|---|
| `features/integration/integration-models.ts` | DTO i wyniki klienta |
| `features/integration/integration-api.ts` | klient czterech endpointów |
| `features/integration/integration-state.ts` | `integrationStateOf`, etykiety, tony, teksty powodów, słowniki źródeł i statusów |
| `features/integration/integration-status.ts` | serwis stanu (root) |
| `features/integration/integration-errors.ts` | stan listy błędów i „Ponów” (instancja na stronę) |
| `features/integration/integration-state-badge.ts` | `app-integration-state-badge`: `app-status-badge` z ikoną |
| `features/integration/incomplete-data-notice.ts` | `app-incomplete-data-notice` |
| `features/integration/integration-page.{ts,html}` | ekran |
| `layout/integration-indicator.{ts,html}` | wskaźnik w nagłówku |
| `layout/app-shell.{ts,html}` | osadzenie wskaźnika |
| `shared/format/pl-format.ts` (+ pipe) | `formatTime` → `HH:mm` w Europe/Warsaw (dopisek „Stan z …”); brak/nieczytelna wartość → „Brak danych” |
| `shared/ui/status-badge.ts` | `<ng-content />` przed etykietą (slot ikony), bez zmiany istniejących użyć |
| `shared/ui/button.{ts,html}` | opcjonalne wejście `describedBy` → `[attr.aria-describedby]` na natywnym `<button>`, bez zmiany istniejących użyć |

- **Stany:**
  - ładowanie: szkielet sekcji stanu, a w nagłówku „Sprawdzanie stanu…” bez fikcyjnego
    „Aktualne”;
  - pustka: lista błędów, z rozróżnieniem pustki z filtrem i bez;
  - błąd: odczyt stanu i odczyt listy, każdy z „Spróbuj ponownie”, oraz wyniki akcji;
  - wczytane: wszystkie powyższe sekcje.
- **Tokeny:**
  - tony `success`, `info`, `warning`, `error`, `neutral` przez `app-status-badge` i tokeny
    `--sf-*`, bez literałów kolorów (`lint:colors`);
  - główna akcja ekranu: „Synchronizuj teraz” (`primary`), a „Ponów” jest drugorzędne
    (`secondary`).
- **Dostępność (WCAG 2.2 AA):**
  - stan zawsze jako tekst, ikona `aria-hidden` (design guide sekcja 2: kolor nie wystarcza);
  - region `aria-live="polite"` w nagłówku dla zmiany stanu;
  - `role="status"` / `role="alert"` dla wyników akcji;
  - obszary kliknięcia co najmniej 44 px;
  - widoczny fokus z istniejących klas;
  - semantyczne `<h1>`/`<h2>` i tabela z `caption`;
  - animacja ikony tylko przy `motion-safe`;
  - przycisk wyłączony z widocznym powodem obok (wymóg), dodatkowo powiązanym przez
    `aria-describedby`;
  - dostępna nazwa „Ponów zamówienie {externalId}” z ukrytego tekstu w przycisku (WCAG 2.5.3);
  - czas „Ostatniej pełnej kontroli” we wskaźniku nagłówka w ukrytym tekście po etykiecie, bez
    `title`;
  - w `busy` „Ponów” ma nazwę „Ponawianie zamówienia {externalId}…”.
- **Responsywność:** nagłówek zawija się jak dziś. Lista błędów korzysta z przewijanego regionu
  `app-data-table`, bez poziomego przewijania strony.

## Edge Cases

- `configured = false` przy `isRunning = true` (chwilowa dzierżawa zadania bez działania) →
  „Nie skonfigurowano”.
- `isRunning = true` przy `lastRun = null` albo `lastRun.status = failed` (przyjęte „Synchronizuj
  teraz”) → „Synchronizacja trwa”.
- `lastRun.status = abandoned` przy świeżym sukcesie i braku błędów → „Błąd” (PRD: ostatni
  przebieg się nie udał).
- `lastRun = succeeded`, `openErrors = 0`, sukces 25 h 0 min temu → „Aktualne”; 25 h 0 min 1 s →
  „Dane niekompletne” (`stale`).
- Granica 25 h mija przy otwartym ekranie bez żądania → stan zmienia się przy najbliższym tyknięciu
  zegara lokalnego (≤ 60 s) i jest ogłaszany.
- `lastSuccessAtUtc` w przyszłości (rozjechany zegar klienta) → nie `stale`.
- `lastSuccessAtUtc` nieczytelny → `neverSucceeded`.
- Pierwsze uruchomienie: `lastSuccessAtUtc = null`, `lastRun = null` → „Dane niekompletne” i
  „Pełna kontrola jeszcze się nie odbyła.”
- Bezczynna, widoczna karta → żadnych żądań w tle. Po 120 s od ostatniego udanego odczytu
  następuje wariant ostrożny („Stan z HH:mm”, ostrzeżenie). Sesja wygasa po 60 min od ostatniej
  aktywności, a interceptor przenosi do logowania przy kolejnym żądaniu.
- **„Aktualne” i bierny czytelnik** (np. raport czytany z kółkiem myszy): awaria lub nowy błąd po
  ostatnim odczycie nie są widoczne do najbliższej aktywności. Najpóźniej po 120 s od odczytu UI
  przestaje jednak pokazywać kompletność (wariant ostrożny), a pierwsza aktywność odświeża stan.
- Karta w tle podczas 23:00 → żądań brak. Po powrocie natychmiastowy odczyt.
- Błąd odczytu stanu po wcześniejszym sukcesie → „Stan nieznany” i komunikat w wariancie
  `unknown` z ostatnim znanym czasem „(ostatni znany odczyt)”. `configured` pochodzi z poprzedniej
  odpowiedzi. Następna aktywność ponawia odczyt, gdy jest należny.
- „Synchronizuj teraz” w trakcie przebiegu o 23:00 → `409 sync_in_progress` i komunikat
  informacyjny, bez stanu błędu.
- Dwa szybkie kliknięcia „Synchronizuj teraz” → jedno żądanie (`busy`).
- „Ponów” błędu zamkniętego w międzyczasie przez webhook → `409 error_closed`, komunikat i
  przeładowanie (wiersz znika z „Otwarte”).
- „Ponów” ostatniego błędu na stronie 3 → po przeładowaniu pusta strona → strona 2.
- Błąd bez zamówienia w ERP (`orderId = null`) → bez linku, z „ID w sklepie”.
- Koniec synchronizacji wykryty na ekranie → lista błędów się przeładowuje.
- Wylogowanie lub `401` → powłoka znika, `stop()` usuwa timery i nasłuchy.
- Nieudany przebieg przy świeżym kursorze i bez błędów, potem „Synchronizuj teraz” → „Synchronizacja
  trwa” z `lastRun.status = failed` → `dataComplete = false`, ostrzeżenie zostaje do udanego końca.
- Każdy przebieg (także o 23:00) → przez czas trwania `dataComplete = false`, bo `lastRun.status =
  running` (wariant ostrożny, Assumptions 19).
- Bezczynna karta, kliknięcie „Synchronizuj teraz” po dłuższej przerwie → `pointerdown` (t₀)
  ustawia `lastActivityAt = t₀`. Odczyt jest należny, więc wysyła się w tle `GET /status` (numer
  n, `lastReadAt = t₀`). Następnie `POST /sync` → `202`, a `refresh({ force: true })` wysyła numer
  n+1 (`lastReadAt = t₁ > t₀`). Odpowiedź n, nawet spóźniona, jest ignorowana, a stan po akcji to
  `running`. Kolejny odczyt w tle wymaga nowej aktywności po t₁ + 10 s, bo `lastActivityAt = t₀`
  nie jest większe od `lastReadAt`.
- **Znane ograniczenie — bierny obserwator:** użytkownik po „Synchronizuj teraz” tylko patrzy →
  poza wymuszonym odczytem po `202` nie ma odczytów (kliknięcie poprzedza `lastReadAt`). Po 20 s
  od ostatniego udanego odczytu (`running`) wskaźnik pokazuje „Synchronizacja trwa · Stan z
  HH:mm”, a komunikat — ostrzeżenie o możliwej nieaktualności. Najbliższa akcja (klawisz,
  kliknięcie, dotyk, powrót na kartę) daje natychmiastowy odczyt, ogłoszenie zmiany i
  przeładowanie listy błędów. Dotyczy też przebiegu o 23:00 przy otwartej, nieużywanej karcie.
  UI nie sugeruje wtedy kompletności (Assumptions 25 i 32).
- Zegar klienta przestawiony do przodu → wariant ostrożny wcześniej (bezpieczne). Cofnięty →
  `now − readAt` ujemne, wariant ostrożny później. Ryzyko jak w Assumptions 5.
- „Ponów” bez odpowiedzi przez 45 s → komunikat ogólny, wiersz wychodzi z `busy`, lista się
  przeładowuje. Spóźniona odpowiedź tego żądania jest ignorowana.
- „Synchronizuj teraz” bez odpowiedzi przez 15 s → komunikat „Nie udało się potwierdzić…” i
  wymuszony odczyt stanu, który pokaże, czy przebieg ruszył.
- **Migotanie wariantów komunikatu po `202`** (akceptowane): przy wcześniejszym sukcesie komunikat
  jest najpierw zwarty (`isRunning = true` z kolejki, `lastRun.status = succeeded`). Po starcie
  przebiegu (`lastRun.status = running`) staje się ostrzeżeniem, a po sukcesie wraca do zwartej
  linii. Każdy wariant jest bezpieczny. Testy SF-19/SF-20 i komponentu nie traktują tego jako
  błędu.
- Nieudany odczyt albo limit 15 s w trakcie `running` → `unknown`, okres 10 s zostaje, a następny
  odczyt po aktywności.
- Przewijanie dotykiem (`pointerdown`) jest aktywnością, a kółkiem myszy nie (świadoma
  niespójność, Assumptions 30).
- `GET /status` wisi → po 15 s „Stan nieznany” i ostrzeżenie w wariancie `unknown`.
- **Dzień zmiany czasu z letniego na zimowy** → przebiegi o 23:00 Europe/Warsaw dzieli 25 h czasu
  rzeczywistego, więc warunek „ponad 25 godzin” od kursora (start − 10 min) zaczyna działać ok.
  22:50 i do startu przebiegu UI pokazuje „Dane niekompletne”. To samo dzieje się, gdy uśpiony
  hosting opóźni start o ponad 50 min. Fałszywy alarm jest bezpieczny i **akceptowany**, bo próg
  25 h pochodzi z PRD.
- „Ponów” w dwóch wierszach naraz → oba wiersze `busy` niezależnie; region wyniku pokazuje wynik
  zakończony jako ostatni, a stan listy ustala najnowsze przeładowanie (numer sekwencyjny).
- „Ponów” → `404` bez kodu → komunikat ogólny i przeładowanie listy.
- Stan `loading` → „Synchronizuj teraz” i „Ponów” wyłączone. Stan `unknown` → obie akcje dostępne.

## Dependencies

- **SF-13** (PR #12, w przeglądzie) — endpointy i kody. Gałąź `sf-14-integration-status` jest
  odbita od `sf-13-woocommerce-backfill`. Zmiana kontraktu API w przeglądzie SF-13 wymaga
  aktualizacji tej specyfikacji.
- **SF-5** (powłoka, `shared/ui`, `shared/format`) i **SF-6** (`provideAppHttp`, interceptor `401`,
  XSRF) — dostępne.
- **SF-8** — trasa `/zamowienia/:id` (link do zamówienia) — dostępna.
- SF-19 i SF-20 zależą od `app-incomplete-data-notice` z tego zadania.

## Assumptions

Właściciel zlecił pracę bez dodatkowych pytań. Poniższe punkty to decyzje autora specyfikacji z
uzasadnieniem.

1. **Gdzie liczony jest stan:** we frontendzie, czystą funkcją. SF-13 tak zdecydował
   (`GetSyncStatus.cs`: „stany INT-01 wylicza frontend”; spec SF-13: „Stany INT-01 wylicza SF-14”),
   a API zwraca wszystkie potrzebne dane. Warunek 25 h zależy od upływu czasu, więc liczony lokalnie
   zmienia się bez żądania. Backend się nie zmienia. Reguła nie dotyczy finansów („frontend nie
   liczy finansów”) i leży poza komponentem (`rules.md`).
2. **Stan „Nie skonfigurowano”** jest sprawdzany przed czterema stanami PRD. API zwraca
   `configured`, a bez kluczy REST nie działa uzupełnianie ani „Ponów”. Pokazanie „Aktualne” byłoby
   sugerowaniem kompletności (INT-01). To stan techniczny, nie nowy status biznesowy, i nigdy nie
   daje `dataComplete = true`.
3. **„Błąd” obejmuje `failed`, `abandoned` i każdą wartość różną od `succeeded`**, gdy nic nie
   trwa. Jest to zgodne z SF-13 („`abandoned` jak nieudany”) i ostrożniejsze przy nieznanej
   wartości.
4. **Próg 25 h liczony od `lastSuccessAtUtc`**, czyli od kursora (start udanego przebiegu − 10 min;
   SF-13 S2). Próg jest więc o 10 min ostrzejszy niż od startu przebiegu. Przy przebiegu o 23:00 i
   limicie 30 min zostaje zwykle 20 min zapasu przed fałszywym „Dane niekompletne”. Wyjątkiem jest
   dzień zmiany czasu i opóźniony start (Edge Cases), kiedy krótki fałszywy alarm jest akceptowany.
   „Ostatnia pełna kontrola” pokazuje tę samą wartość: czas, do którego dane sprawdzono w sklepie.
   Granica jest ścisła („ponad 25 godzin”).
5. **Czas „teraz” pochodzi z zegara klienta.** API nie zwraca czasu serwera, a dodanie go
   rozszerzałoby zakres. Ryzyko rozjechanego zegara jest małe (jeden użytkownik); przyszła data
   sukcesu nie daje `stale`.
6. **`dataComplete` przy „Synchronizacja trwa”**: wersja 1 wyliczała go tylko z powodów
   niekompletności. Wersja 2 zastępuje to regułą z pkt 19.
7. **Brak odczytu stanu daje „Stan nieznany” i `dataComplete = false`.** Awaria nie może sugerować
   kompletności (INT-01). Komunikat ma dla tego przypadku osobny, prawdziwy tekst.
8. **Odświeżanie zależne od aktywności, bez stałego odpytywania.** Sesja przedłuża się przy każdym
   żądaniu (`ShouldRenew = true`), więc stałe odpytywanie łamałoby DOST-01. Zmiana Access („żądanie
   w tle nie przedłuża sesji”) dotyka bezpieczeństwa i wykracza poza zadanie. Okres 60 s,
   wstrzymanie w ukrytej karcie, odczyt po powrocie i po akcjach. Obowiązującą regułę opisują pkt
   25, 27 i 33, a definicję aktywności pkt 21.
9. **`GET /api/jobs` nie jest używany.** `isRunning` w `/status` uwzględnia już dzierżawę zadania i
   oczekujące żądanie ręczne, a `lastRun` niesie szczegóły przebiegu.
10. **Kod błędów `502`/`503` przy „Ponów” czyta klient obszaru**, bo `toApiError` sprowadza `5xx` do
    `network`. Zmiana wspólnego `toApiError` dotknęłaby wszystkich formularzy, a potrzeba istnieje
    tylko tutaj.
11. **Teksty komunikatów:** `detail` z API, gdy jest, a w przeciwnym razie stałe frontendu równe
    tekstom z `IntegrationsProblems.cs`. Decyzje w kodzie zapadają po `code`, nie po tekście
    (konwencje).
12. **Położenie plików:**
    - wszystko, co dotyczy integracji (funkcja, serwis, odznaka, komunikat), leży w
      `features/integration/`. Komunikat niesie wiedzę o integracji (tekst, warunek, link), a
      `shared/ui` trzyma elementy ogólne;
    - `layout/` importuje z `features/integration/`, tak jak `user-menu.ts` importuje z
      `features/auth/`;
    - te małe pliki trafiają do początkowego pakietu, a strona `/integracja` zostaje leniwa.
13. **`app-incomplete-data-notice` nie ma wejść.** Czyta wspólny serwis, więc widoki SF-19/SF-20
    nie powtarzają warunku i nie mogą się rozjechać z nagłówkiem. Brak wejść jest świadomy (YAGNI).
    Renderuje się zawsze (pkt 18).
14. **Filtr listy ma trzy wartości** (Otwarte, Zamknięte, Wszystkie), bo API obsługuje `all`, a
    zgłoszenie mówi o historii błędów. Domyślnie „Otwarte”, jak w API.
15. **„Ponów” bez dialogu potwierdzenia.** Akcja nie jest destrukcyjna: pobiera stan ze sklepu, a
    przypadek usunięcia w sklepie wynika ze stanu sklepu (SF-13). Jest dostępna tylko dla błędów
    otwartych, ukryta przy `notConfigured` i wyłączona w `loading` (pkt 24).
16. **Kontekst błędu jest widoczny w wierszu** („Kontekst: …”), bo PRD wymaga błędu „ze statusem,
    czasem i kontekstem w historii błędów”.
17. **Ryzyko zostaje `medium`.** Brak pieniędzy, danych klientów i operacji nieodwracalnych.
    Uzasadnienie w sekcji Risk.

Rozstrzygnięcia wersji 2. Poniższe decyzje autora przekazał koordynator po przeglądzie
`03-spec-review-v1.md`:

18. **C1 — „Ostatnia pełna kontrola” zawsze przy wynikach.**
    - `app-incomplete-data-notice` renderuje się zawsze: przy danych kompletnych jako zwarta linia
      bez ostrzeżenia, przy niekompletnych jako ostrzeżenie z tą samą linią.
    - Wskaźnik w nagłówku podaje czas w dostępnej nazwie (w wersji 3 bez `title`, pkt 28), więc czas jest dostępny przy
      każdym widoku powłoki.
    - Uzasadnienie: PRD sekcja 5 i design guide sekcja 6 („zawsze »Ostatnia pełna kontrola«”).
      SF-19/SF-20 osadzają tylko ten komponent.
19. **C2 — `dataComplete` w `running`** = `true` tylko przy `lastRun.status = succeeded` i braku
    powodów niekompletności, w każdym innym przypadku `false`.
    - Po przyjęciu „Synchronizuj teraz” `lastRun` to nadal poprzedni przebieg (`queue.IsPending` w
      `GetSyncStatus.cs`), więc nieudany wynik jest widoczny i nie wolno go ukryć.
    - W trakcie przebiegu (`lastRun.status = running`) wynik jest nieznany.
    - Wariant ostrożny (INT-01) oznacza, że ostrzeżenie widać przez cały czas każdego przebiegu, z
      23:00 włącznie.
20. **(Zastąpione w wersji 3 przez pkt 25.)** Wersja 2 dopuszczała serię `running` co 10 s bez
    wymogu aktywności, najdłużej 35 min. Przegląd v2 wykazał sprzeczność z DOST-01 bez decyzji
    właściciela, więc wyjątek usunięto.
21. **NC3 — aktywność** to tylko celowe działania: `keydown`, `pointerdown` i powrót na widoczną
    kartę.
    - Bez `pointermove`, `wheel` i `touchstart`. `touchstart` poprzedza `pointerdown` na ekranach
      dotykowych, więc nie jest potrzebny.
    - „Ostatnia aktywność” to wyłącznie te działania, nigdy żądania (ani `/status`, ani inne
      wywołania API).
    - Treść wpisu do `conventions.md`: wyłącznie pkt 27.
22. **NC2 / NC4 — żądania stanu.** Wersja 3 zastępuje anulowanie mechanizmem z pkt 29.
    - `refresh({ force: true })` po akcjach wysyła nowe żądanie.
    - Odczyty bez `force` współdzielą żądanie w toku.
    - Limit czasu `GET /status` to 15 s, a jego przekroczenie daje „Stan nieznany”.
23. **NC6 — `400 csrf_invalid`** przy obu akcjach: komunikat „Odśwież stronę i spróbuj ponownie.”,
    bez odświeżenia stanu i bez przeładowania listy.
24. **NC7 i niejasności.**
    - Dostępna nazwa „Ponów” powstaje z ukrytego tekstu w treści przycisku, co jest minimalną
      zmianą bez nowego wejścia. W `busy` nazwę daje `busyLabel` (pkt 31).
    - Powód wyłączenia „Synchronizuj teraz” jest powiązany przez `aria-describedby`. Wymaga to
      nowego, opcjonalnego wejścia `describedBy` w `app-button`, bo atrybut na hoście nie trafia do
      natywnego przycisku.
    - W `loading` obie akcje są wyłączone, a w `unknown` dozwolone (decyduje API).
    - Przejścia do i z „Stan nieznany” są ogłaszane przez `aria-live`.
    - `404` bez kodu przy „Ponów” daje komunikat ogólny i przeładowanie listy.
    - Każdy wiersz ma własny stan zajętości, a równoległe „Ponów” w różnych wierszach jest
      dozwolone.

Rozstrzygnięcia wersji 3. Poniższe decyzje autora przekazał koordynator po przeglądzie
`03-spec-review-v2.md`:

25. **Jedna reguła aktywności, bez wyjątku dla `running`.**
    - Każde żądanie w tle wymaga widocznej karty i aktywności użytkownika po poprzednim odczycie,
      w tle albo wymuszonym (znaczniki z pkt 34). Okres to 10 s w `running` i 60 s poza nim, z tym samym wymogiem.
    - Znane ograniczenie: przy bezczynnej karcie „Synchronizacja trwa” i wariant ostrzegawczy
      zostają do najbliższej akcji. Wtedy następuje natychmiastowy odczyt, a `aria-live` ogłasza
      zmianę dopiero po nim.
    - Zgodność z INT-01: INT-01 wymaga, żeby UI pokazywało ostatnią pełną kontrolę, błąd i
      dostępną akcję oraz nie sugerowało kompletności przy awarii. Nie wymaga natychmiastowego
      pokazania wyniku bez udziału użytkownika. W zawieszonym stanie nagłówek mówi „Synchronizacja
      trwa”, a `dataComplete = false` (pkt 19), więc komunikat jest w wariancie ostrzegawczym, a
      „Ostatnia pełna kontrola” pokazuje kursor sprzed przebiegu.
    - Zdanie „UI może najwyżej zaniżać kompletność, nigdy jej zawyżać” jest prawdziwe w każdym
      stanie dzięki regule wieku odczytu (pkt 32). Bez świeżego odczytu żaden stan, także
      „Aktualne”, nie daje `dataComplete = true` dłużej niż dwa okresy. Do tego czasu UI pokazuje
      stan z odczytu sprzed najwyżej 120 s, z dokładnością właściwą dla każdego odpytywania.
26. **(Usunięte w wersji 4.)** Tolerancja „+ jeden okres” nie jest potrzebna, bo odczyt w tle
    wychodzi tylko w chwili aktywności (pkt 33).
27. **Wpis w `conventions.md`** (Frontend → „State and data fetching”) opisuje tylko regułę, bez
    furtki: „Żądania w tle (np. odświeżanie stanu integracji) wysyła się tylko z widocznej karty i
    tylko w chwili celowej aktywności użytkownika (`keydown`, `pointerdown`, powrót na widoczną
    kartę), która nastąpiła po poprzednim odczycie, bo każde żądanie przedłuża sesję (DOST-01).
    Ruch kursora i kółko nie są aktywnością. Brak świeżego odczytu pokazuje się jako stan
    ostrożny, nie jako stan kompletny.”
28. **Wskaźnik bez `title`.** Dostępna nazwa zaczyna się od widocznej etykiety, a czas jest w
    ukrytym tekście po niej (spójnie z regułą WCAG 2.5.3 stosowaną dla „Ponów”). Czytnik nie czyta
    czasu dwa razy. Wymóg PRD „przy wynikach zawsze widać czas” spełnia
    `app-incomplete-data-notice`, który zawsze się renderuje (pkt 18).
29. **Promise i numer sekwencyjny zamiast anulowania.**
    - `integration-api.ts` zwraca Promise (`firstValueFrom`), zgodnie z konwencją obszaru, bez
      odstępstwa.
    - Limit 15 s daje operator `timeout` w `integration-api.ts` przed `firstValueFrom`.
    - Serwisy `integration-status.ts` i `integration-errors.ts` numerują odczyty i stosują tylko
      odpowiedź najnowszego, więc spóźnione odpowiedzi są ignorowane.
30. **Aktywność a przewijanie.**
    - Przewijanie dotykiem zaczyna się od `pointerdown`, więc liczy się jako aktywność. Przewijanie
      kółkiem (`wheel`) się nie liczy.
    - Niespójność jest świadoma: dotyk wymaga kontaktu z ekranem, a zdarzenie kółka przychodzi
      także nad nieużywanym oknem. Najgorszym skutkiem jest odczyt odłożony do kliknięcia albo
      klawisza.
31. **Pozostałe.**
    - W `busy` „Ponów” ma nazwę z `busyLabel` = „Ponawianie zamówienia {externalId}…”, co jest
      minimalną zmianą przez istniejące wejście `app-button`.
    - Wymóg instrukcji przy wyłączonym „Synchronizuj teraz” spełnia widoczny tekst powodu, a
      `aria-describedby` jest dodatkiem.
    - W `unknown` `status` zachowuje poprzednią odpowiedź (z `configured`), a „Ostatnia pełna
      kontrola” pokazuje ostatni znany czas z dopiskiem „(ostatni znany odczyt)”.
    - Nieudany odczyt nie kończy serii `running`.

Rozstrzygnięcia wersji 4. Poniższe decyzje autora przekazał koordynator po przeglądzie
`03-spec-review-v3.md`:

32. **Reguła wieku odczytu.** Odczyt starszy niż dwa okresy (120 s, a w `running` 20 s) daje
    `dataComplete = false`, etykietę z dopiskiem „Stan z {HH:mm}” i ostrzeżenie „Stan integracji
    może być nieaktualny…”, bez żądań.
    - Zamyka to przypadek „Aktualne” u biernego czytelnika, kiedy awaria po ostatnim odczycie
      byłaby niewidoczna.
    - Dwa okresy zamiast jednego dają zapas na czas odpowiedzi i na aktywnego użytkownika, którego
      odczyty idą zwykle co okres.
    - Do formatu `HH:mm` dochodzi `formatTime` w `shared/format`, zgodnie z konwencją: daty i czasy
      tylko przez `shared/format`.
33. **Wysyłka odczytu w tle tylko w chwili aktywności.** Reguła koordynatora brzmi: odczyt
    dozwolony, gdy `lastActivityAt > lastReadAt` i `now − lastActivityAt ≤ okres`, porównanie
    ścisłe. Autor zawęża jej stosowanie do obsługi zdarzenia aktywności (`now = lastActivityAt`).
    - Uzasadnienie: wysyłka z zegara do okresu po aktywności zostawiałaby do 60 s nadwyżki sesji.
      Bez niej sesja wygasa najpóźniej 60 min po ostatniej aktywności, bez tolerancji (pkt 26
      usunięty).
    - Dla użytkownika różnica jest pomijalna: aktywny użytkownik i tak generuje zdarzenia, a
      bierny jest objęty pkt 32.
34. **Znaczniki.**
    - `lastActivityAt` pochodzi tylko ze zdarzeń z reguły aktywności (pkt 21).
    - `lastReadAt` to chwila wysłania ostatniego odczytu `/status`, w tle albo wymuszonego;
      `refresh({ force: true })` ustawia go jak każdy odczyt.
    - `readAt` (pkt 32) to chwila odpowiedzi ostatniego udanego odczytu.
35. **„Spróbuj ponownie”** wszędzie wywołuje `refresh({ force: true })`. Przy liście błędów
    ponowienie przeładowuje listę.
36. **Limity czasu.**
    - `GET /status`, `GET /errors` i `POST /sync` mają 15 s, a `POST /errors/{id}/retry` 45 s,
      bo backend woła sklep z limitem 30 s.
    - Przekroczenie przy „Ponów” daje komunikat ogólny, wyjście wiersza z `busy` i przeładowanie
      listy. Przy „Synchronizuj teraz” daje komunikat i wymuszony odczyt stanu.
    - Spóźnione odpowiedzi są ignorowane (numer sekwencyjny, pkt 29).

## Acceptance Criteria

- [ ] `integrationStateOf` daje stany w kolejności „Nie skonfigurowano” → „Synchronizacja trwa” →
      „Błąd” → „Dane niekompletne” → „Aktualne”. Testy obejmują: `abandoned` → „Błąd”, otwarte
      błędy → „Dane niekompletne”, `> 25 h` → „Dane niekompletne”, równe 25 h → „Aktualne”, brak
      sukcesu → „Dane niekompletne”. Dodatkowo:
      - `configured = false` przy `isRunning = true` → „Nie skonfigurowano”;
      - `isRunning = true` przy `lastRun.status = failed` → „Synchronizacja trwa”;
      - nieznana wartość `lastRun.status` → „Błąd”;
      - `dataComplete` w `running` jest `true` tylko przy `lastRun.status = succeeded` bez powodów,
        a `false` przy `failed`, `abandoned`, `running`, `lastRun = null` i przy każdym powodzie.
- [ ] Nagłówek każdej sekcji powłoki pokazuje stan tekstem z ikoną (nie tylko kolorem) jako link
      do `/integracja` z dostępną nazwą, która zaczyna się od widocznej etykiety, a po niej podaje
      czas „Ostatniej pełnej kontroli” (bez `title`). Zmiana stanu, w tym przejście do i z „Stan nieznany”, jest ogłaszana przez
      `aria-live="polite"`.
- [ ] Ekran „Integracja” pokazuje:
      - stan z wyjaśnieniem;
      - „Ostatnią pełną kontrolę” w formacie `dd.MM.yyyy, HH:mm` Europe/Warsaw;
      - ostatni przebieg ze statusem, źródłem, czasami, liczbami i komunikatem.
- [ ] „Synchronizuj teraz” obsługuje `202`, oba `409` i `400 csrf_invalid`, blokuje podwójne
      kliknięcie i jest wyłączone przy trwającej synchronizacji, braku konfiguracji i w `loading`.
      Obok wyłączonego przycisku widać powód, dodatkowo powiązany przez `aria-describedby`.
- [ ] Lista błędów filtruje Otwarte/Zamknięte/Wszystkie przez API, stronicuje po 25 i linkuje
      zamówienie, gdy istnieje.
- [ ] „Ponów” obsługuje `200` (zamknięty i nadal otwarty), `404` (z kodem i bez), `409` (oba
      kody), `502`, `503`, `400 csrf_invalid` i błąd sieci z komunikatem dla każdego przypadku. Po
      wyniku odświeża listę i stan, z wyjątkiem błędu sieci i `csrf_invalid`. Dostępna nazwa
      przycisku to „Ponów zamówienie {externalId}”, a w `busy` „Ponawianie zamówienia
      {externalId}…”.
- [ ] `app-incomplete-data-notice` zawsze pokazuje „Ostatnią pełną kontrolę”:
      - przy `dataComplete = true` jako zwartą linię bez ostrzeżenia;
      - przy `dataComplete = false` z ostrzeżeniem „Nie pobrano wszystkich zamówień. Wyniki mogą
        być niepełne.” i linkiem do integracji;
      - przy stanie nieznanym w wariancie `unknown`, z ostatnim znanym czasem i dopiskiem
        „(ostatni znany odczyt)”;
      - w `loading` jako linię „Sprawdzanie…”;
      - w wariancie `aged` jako ostrzeżenie „Stan integracji może być nieaktualny…” z dopiskiem
        „Stan z …” (szczegóły w kryterium reguły wieku odczytu).

      Nie jest osadzony w widokach raportowych.
- [ ] Przy odczytanej awarii (przebieg nieudany lub przerwany, błąd odczytu stanu, brak
      konfiguracji) żaden element nie pokazuje „Aktualne”. Przy awarii jeszcze nieodczytanej UI
      przechodzi w wariant ostrożny najpóźniej po dwóch okresach od ostatniego udanego odczytu.
- [ ] Bezczynna albo ukryta karta nie wysyła żadnych żądań `/status` w tle, także w stanie
      `running` (test na sztucznych timerach). Odczyt w tle wychodzi tylko w obsłudze zdarzenia
      aktywności, przy `lastActivityAt > lastReadAt` i upływie okresu. Ruch kursora i kółko nie
      są aktywnością.
- [ ] Reguła wieku odczytu: odczyt starszy niż 120 s (w `running` 20 s) daje `dataComplete =
      false`, dopisek „Stan z {HH:mm}” we wskaźniku i komunikacie oraz ostrzeżenie „Stan
      integracji może być nieaktualny. Wyniki mogą być niepełne.”, także dla stanu „Aktualne”.
      Pierwsza aktywność odświeża stan od razu.
- [ ] Limity czasu: `GET /status`, `GET /errors`, `POST /sync` po 15 s, a `POST …/retry` 45 s.
      Przekroczenie przy „Ponów” zdejmuje `busy` i przeładowuje listę.
- [ ] Spóźniona odpowiedź starszego odczytu (`/status` i lista błędów) nie nadpisuje stanu.
      `GET /status` dłuższe niż 15 s daje „Stan nieznany”.
- [ ] Brak zmian w `api/`. Brak literałów kolorów (`lint:colors`). Istniejące testy powłoki
      przechodzą po obsłużeniu żądania `/status`.
- [ ] `conventions.md` opisuje regułę odświeżania w tle.

## Verification

- build: z `app/`:
  - `npm ci`
  - `npm run format:check`
  - `npm run lint`
  - `npm run typecheck`
  - `npm run build`
- tests: z `app/`: `npm test -- --watch=false`. Zakres (Vitest, pliki obok kodu):
  - `integration-state.spec.ts` — tabela przypadków kolejności, granica 25 h, `dataComplete`,
    nieczytelne i przyszłe daty;
  - `integration-api.spec.ts` (`HttpTestingController`) — adresy, parametry filtra i strony,
    mapowanie `202`/`409`/`200`/`404`/`502`/`503` (z kodem i bez)/`0`;
  - `integration-status.spec.ts` (sztuczne timery):
    - pierwszy odczyt;
    - okres 60 s;
    - okres 10 s w `running` z wymogiem aktywności; nieudany odczyt nie kończy serii;
    - reguła wieku: „Aktualne” bez aktywności → po 120 s `aged`, `dataComplete = false`, dopisek
      „Stan z HH:mm” i ogłoszenie; `running` → po 20 s; pierwsza aktywność → natychmiastowy
      odczyt i koniec `aged`; żadnego żądania wywołanego samym zegarem;
    - znaczniki: aktywność przed upływem okresu nie wysyła odczytu; aktywność po okresie wysyła;
      `lastActivityAt = lastReadAt` nie wysyła (porównanie ścisłe); wymuszony odczyt ustawia
      `lastReadAt`;
    - wstrzymanie bez aktywności i w ukrytej karcie;
    - `pointermove`/`wheel` nie wznawiają odczytów, a `keydown`/`pointerdown` tak;
    - odczyt po powrocie;
    - współdzielenie żądania w toku bez `force`; przy `force` nowe żądanie i ignorowanie spóźnionej
      odpowiedzi starszego (numer sekwencyjny), także w scenariuszu bezczynna karta →
      `pointerdown` → akcja `202`;
    - limit 15 s (`timeout` w kliencie) → `unknown`;
    - limity w `integration-api.spec.ts`: 15 s dla `/status`, `/errors` i `/sync`, 45 s dla
      `retry`;
    - `unknown` po błędzie z zachowaną poprzednią odpowiedzią;
    - ogłoszenie przy zmianie, także do i z `unknown`, bez ogłoszenia przy pierwszym odczycie;
    - `stop()` usuwa timery i nasłuchy;
  - `integration-errors.spec.ts` — filtr → strona 1, stronicowanie, cofnięcie pustej strony,
    `busy` na wiersz, przeładowanie i `refresh()` po „Ponów”, ignorowanie spóźnionego
    przeładowania, przekroczenie 45 s (wyjście z `busy`, komunikat, przeładowanie);
  - `pl-format.spec.ts` — `formatTime` (Europe/Warsaw, w tym zmiana czasu, brak danych);
  - `integration-page.spec.ts`:
    - stany ładowania, błędu i wczytania;
    - format daty Europe/Warsaw;
    - komunikat przebiegu;
    - przycisk i powody wyłączenia (`aria-describedby`), akcje w `loading`/`unknown`;
    - dostępna nazwa „Ponów zamówienie {externalId}” i w `busy` „Ponawianie zamówienia
      {externalId}…”;
    - regiony `status`/`alert`;
    - link do zamówienia i „ID w sklepie”;
    - przeładowanie listy po końcu synchronizacji;
  - `incomplete-data-notice.spec.ts` — wariant dla każdego stanu (kompletne, niekompletne,
    `unknown`, `aged`, `loading`), teksty, czas, link;
  - `integration-indicator.spec.ts` — tekst i ikona, `href="/integracja"`, dostępna nazwa od
    etykiety z czasem po niej, brak `title`, dopisek „Stan z HH:mm” w `aged`, region `aria-live`;
  - `button.spec.ts` — `describedBy` ustawia `aria-describedby`, a brak wejścia nie dodaje
    atrybutu;
  - `app-shell.spec.ts` — zaktualizowany o wskaźnik i obsługę żądania `/status`;
  - `status-badge` — slot ikony nie zmienia istniejących użyć.
- backend: bez zmian. Wystarczają istniejące testy SF-13 w CI (`dotnet test --solution
  stone-flow.slnx -c Release --no-build`).
- manual (lokalnie z API SF-13):
  - pusta baza → „Dane niekompletne”, „Pełna kontrola jeszcze się nie odbyła.”;
  - „Synchronizuj teraz” → „Synchronizacja trwa” w nagłówku, potem „Aktualne” albo „Błąd”;
  - drugie kliknięcie w trakcie → komunikat `409`;
  - wyłączenie sklepu → „Błąd” z komunikatem przebiegu;
  - błąd zamówienia → „Dane niekompletne”, „Ponów” zamyka błąd i stan wraca do „Aktualne”;
  - brak kluczy REST → „Nie skonfigurowano”, przyciski wyłączone z powodem;
  - karta bez aktywności przez kilka minut → w narzędziach sieci brak dalszych żądań `/status`
    (także przy `running`); ruch myszą ich nie wznawia, a kliknięcie tak;
  - „Synchronizuj teraz” bez dalszego ruchu → po ok. 20 s „Synchronizacja trwa · Stan z HH:mm”
    i ostrzeżenie; po kliknięciu pojawia się wynik i ogłoszenie;
  - otwarty raport bez kliknięć przez 2 min → wariant ostrożny w komunikacie i wskaźniku, bez
    żądań w narzędziach sieci;
  - czytnik ekranu ogłasza zmianę stanu;
  - nawigacja klawiaturą do wskaźnika i „Ponów”.
