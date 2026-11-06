# Code review — SF-14

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa specyfikację v4 i rozstrzygnięcia NC1–NC5 z planu. `integrationStateOf` ma kolejność INT-01, ścisłą granicę 25 h i regułę `dataComplete` w `running` zgodną z pkt 19. Żaden timer nie wysyła żądań: odczyt w tle wychodzi tylko w obsłudze `keydown`/`pointerdown`/`visibilitychange` przy widocznej karcie i ścisłym `lastActivityAt > lastReadAt`. Numer sekwencyjny, limity `timeout` i mapowanie kodów (w tym `502`/`503` z kodem czytane z `HttpErrorResponse`) zgadzają się z backendem SF-13. Inwariantów nie naruszono. Zostają cztery ważne uwagi: flaga `aged` zależy wyłącznie od `setTimeout`, regiony wyników akcji powstają razem z treścią, po „Ponów” ginie fokus, a wariant `aged` wygasza też ton „Błąd”. Uruchomione przez recenzenta 8 plików testów SF-14 (114 testów) przechodzi bez błędów.
- Risk: medium

## Project Invariant Violations

None.

| Invariant | Status | Uwagi |
|---|---|---|
| 1. Sales separate from cash | Nie dotyczy | Brak kwot |
| 2. Backend decimal money | Nie dotyczy | Frontend nie liczy kwot |
| 3. API-enforced access | OK | `integration-status.ts:157-169`: wysyłka tylko w obsłudze zdarzenia aktywności, widoczna karta, porównanie ścisłe. `CLOCK_MS` (`:234-237`) i timer `aged` (`:211-225`) nie wysyłają żądań. Żądania nie ustawiają `lastActivityAt`. Przeładowania listy wynikają z odczytu albo akcji użytkownika. Test: `integration-status.spec.ts:81-85, 135-155, 262-271` |
| 4. Retries never duplicate | OK | `busy` na wiersz (`integration-errors.ts:101-105`) i dla synchronizacji (`integration-page.ts:137-141`); duplikatów pilnuje backend |
| 5. Financial audit trail | Nie dotyczy | — |
| 6. Secrets stay server-side | OK | Tylko względne `/api/...`, brak konfiguracji sklepu w bundlu |
| 7. Module and layer boundaries | OK | Logika stanu w czystych funkcjach `integration-state.ts`, tylko API Integrations; link do zamówienia z `orderId` |
| 8. No identifying data in docs | OK | Dane fikcyjne w `src/testing/integration-testing.ts` („1001”, „Z/2026/0001”, „abc123”) |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `app/src/app/features/integration/integration-page.html:84-90` i `:99-105` — regiony wyników akcji (`role="status"`/`role="alert"`) powstają w `@if` razem z treścią, a przy zmianie `alert` element jest podmieniany. Region `role="status"` wstawiony od razu z tekstem często nie jest ogłaszany (NVDA/JAWS + Chrome), a ten sam tekst ustawiony drugi raz z rzędu (np. dwa razy `NETWORK_ERROR_MESSAGE`) nie zmienia DOM-u. W repo regiony są stałe, a zmienia się tylko ich treść (`features/orders/order-payments.html:7-14`, `order-details-page.html:2-9`). Spec (Behavior 5, UI → Dostępność) wymaga ogłaszania wyników akcji.
2. `app/src/app/features/integration/integration-page.ts:137-149` — przepływ „Synchronizuj teraz” (`requestSync` → komunikat → `refresh({ force })`) jest w komponencie, a konwencja mówi: „Flows that orchestrate several calls live in the service, not in components” (`conventions.md`, State and data fetching). Odpowiednik dla „Ponów” jest w serwisie (`integration-errors.ts:101-124`).
3. `.sdd/context/conventions.md:317` — autor dopisał „`formatTime`, `formatReadTime` without pipes”, a tabela UI w specyfikacji ma „`shared/format/pl-format.ts` (+ pipe)”. To odstępstwo jest akceptowalne, bo obie funkcje wywołuje kod TS (`presentationOf`), a nie szablon, i konwencja „tylko przez `shared/format`” jest zachowana. Zapisać w manifeście dostawy.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| `integrationStateOf`: kolejność, `abandoned`/nieznany → „Błąd”, `notConfigured` przed `running`, ścisłe 25 h, nieczytelna/przyszła data | Covered | `integration-state.ts:24-55`; testy `integration-state.spec.ts:31-93` |
| `dataComplete` w `running` tylko przy `succeeded` bez powodów | Covered | `integration-state.ts:29-33`; testy `:95-115` |
| `unknown` z zachowaną odpowiedzią i „(ostatni znany odczyt)” | Covered | `integration-status.ts:60-61, 192-204`; `integration-state.ts:151-154` |
| Reguła wieku odczytu (120 s / 20 s, `dataComplete=false`, „Stan z …”, ogłoszenie, bez żądań) | Partial | Działa przy ciągłym upływie timera; flaga nie wynika z `now − readAt` (Correctness 1) |
| Odczyt w tle tylko przy aktywności, widocznej karcie, ścisłych znacznikach | Covered | `integration-status.ts:157-175`; testy `:81-155` |
| Seria `running` 10 s, nieudany odczyt jej nie kończy | Covered | `:86-87, 187, 206-208`; test `:171-182` |
| Numer sekwencyjny `/status` i listy, `force`, współdzielenie żądania | Covered | `integration-status.ts:138-155`, `integration-errors.ts:51-79`; testy `integration-status.spec.ts:207-236`, `integration-errors.spec.ts:65-75` |
| Limity 15 s / 45 s; timeout `/status` → `unknown` | Covered | `integration-api.ts:94-98, 177-179`; testy `integration-api.spec.ts:119-146` |
| Kody `202`/`409`×2/`400 csrf_invalid`/sieć/timeout dla synchronizacji | Covered | `integration-api.ts:118-137`, `integration-messages.ts:146-169` |
| Kody „Ponów” `200`×2/`404`±kod/`409`×2/`502`/`503`±kod/csrf/sieć/timeout | Covered | `integration-api.ts:139-174`, `integration-messages.ts:172-208`; zgodne z `IntegrationsProblems.cs` |
| Wskaźnik: link, ikona, nazwa od etykiety, bez `title`, `aria-live`, `start`/`stop` | Covered | `integration-indicator.ts/.html`; drobne uwagi do nazwy (Code Smells 2) |
| Ekran: stan, wyjaśnienia, „Ostatnia pełna kontrola”, ostatni przebieg, `aged` | Covered | `integration-page.html:10-68` |
| „Synchronizuj teraz”: `busy`, wyłączenie z powodem i `aria-describedby`, dostępne w `unknown` | Covered | `integration-page.ts:77-91`, `.html:70-83` |
| Lista błędów: filtr przez API, strona 25, link, „ID w sklepie”, cofnięcie pustej strony, przeładowanie po końcu synchronizacji | Covered | `integration-errors.ts`, `integration-page.ts:124-134` |
| Nazwy „Ponów zamówienie {id}” / „Ponawianie zamówienia {id}…” | Covered | `integration-page.html:182-191`; test `integration-page.spec.ts:221-254` |
| Wyniki akcji w `role="status"`/`role="alert"` | Partial | Regiony powstają razem z treścią (Conventions 1) |
| `app-incomplete-data-notice`: pięć wariantów, bez `role="alert"`, bez osadzania | Covered | `incomplete-data-notice.ts`; `loading` nie ma tej samej wysokości (Code Smells 4) |
| Zegar lokalny (granica 25 h „≤ 5 s”) | Partial | `CLOCK_MS = 60_000` (`integration-status.ts:22`) zgodnie z planem NC2; Edge Cases specyfikacji mówią ≤ 5 s |
| `describedBy` w `app-button`, slot ikony w `app-status-badge` | Covered | Testy `button.spec.ts`, `states.spec.ts` |
| Wpis w `conventions.md` (pkt 27) | Covered | `conventions.md:371-376`, dosłownie plus zdanie o `timeout` |
| Brak zmian w `api/`, istniejące testy powłoki przechodzą | Covered | `git status`: brak zmian w `api/` |

## Plan Deviations

- Zegar co 60 s (`integration-status.ts:22`) zamiast 5 s ze specyfikacji. Wynika z planu NC2, ale specyfikacja (Edge Cases: „≤ 5 s”) nie została poprawiona.
- Helper testowy nazywa się `discardIntegrationRequests()` (plan: `discardIntegrationStatus()`) i odrzuca też listę błędów.
- Strona wywołuje `refresh()` w `loading` (`integration-page.ts:119-121`). Spec tego nie wymaga. Przy uruchomionej powłoce odczyt dołącza do żądania w toku, więc skutków nie ma.
- Brak pipe'ów dla `formatTime`/`formatReadTime` (spec UI: „+ pipe”). Zob. Conventions 3.
- Zgłoszone przez autora „`unknown` → `app-error-state` zamiast ostatnich danych” nie jest odstępstwem. Tak stanowi Behavior 5 specyfikacji.
- Zgłoszone „CRLF w dokumentach”: wszystkie pliki `docs/tasks/sf-14/*.md` mają teraz LF, a `.gitattributes` wymusza `eol=lf`. Problemu nie ma.

## Null Safety Issues

None. Pola nullowalne (`lastRun`, `lastSuccessAtUtc`, `finishedAtUtc`, `orderId`, `orderNumber`, `closedAtUtc`, `closedBy`, `message`) są obsłużone w szablonach i w `formatDateTime`. Jedyne `!` są w testach.

## Correctness Issues

1. **Ważne — `app/src/app/features/integration/integration-status.ts:211-225` i `:234-237`.** Flagę `aged` ustawia wyłącznie `setTimeout` (`2 × okres + 1`). Zegar co 60 s (`tick`) aktualizuje `now`, ale nie sprawdza `now − readAt`. Spec (Behavior 2) definiuje regułę przez wartości zegara: „Gdy `now − readAt` przekracza dwa okresy”. Kiedy timer się opóźnia, na przykład przy uśpieniu systemu z widoczną kartą (timery liczą czas monotoniczny, a `visibilitychange` po wybudzeniu nie musi przyjść) albo przy dławieniu timerów, wskaźnik i komunikat pokazują „Aktualne” z `dataComplete = true` dla odczytu sprzed godzin. Trwa to do chwili, gdy timer wreszcie się wykona, albo do pierwszej aktywności. Narusza to kryterium „najpóźniej po dwóch okresach od ostatniego udanego odczytu” i zasadę „nigdy nie zawyżać”. Przegląd specyfikacji v4 (NC5) wskazał ten scenariusz. Wystarczy, że `tick()` (albo `view`) wyliczy `aged` także z `now − readAt`.
2. **Ważne — `app/src/app/features/integration/integration-state.ts:123-130`.** W wariancie `aged` każdy stan traci ton i ikonę, także „Błąd” (`error`, „×”), „Dane niekompletne” i „Nie skonfigurowano”, które dostają `neutral` i zegar. Plan NC1 zdejmuje tylko ton `success` i „✓” („Odznaka traci ton `success` i ikonę „✓””), a spec mówi „etykieta stanu bez zmian”. Po 2 min bezczynności czerwony „Błąd” w nagłówku robi się szary. Tekst zostaje, więc INT-01 nie jest złamane, ale sygnał awarii słabnie w całej aplikacji. Test `integration-state.spec.ts:140-147` sprawdza tylko „Aktualne”.
3. **Ważne (dostępność) — `app/src/app/features/integration/integration-page.html:120` z `:182-191`.** Każde „Ponów” kończy się `errors.load()`, a `app-data-table` w `loading` podmienia całą tabelę na szkielet (`shared/ui/data-table.html:43`). Przycisk z fokusem znika, fokus spada na `body`, a użytkownik klawiatury traci miejsce na liście. Wynik ogłasza tylko region z Conventions 1, który też jest zawodny. Spec wymienia „nawigację klawiaturą do … »Ponów«” w weryfikacji ręcznej. W repo wzorcem jest przeniesienie fokusu na komunikat wyniku (`tabindex="-1"`, np. `features/auth/password-reset-request-page.html:16`).
4. **Drobne — `app/src/app/features/integration/integration-status.ts:138-155`.** `refresh()` nie sprawdza `started`. Spóźniony wynik „Ponów” (do 45 s) albo „Synchronizuj teraz” po zniszczeniu strony lub po `stop()` wywoła `refresh({ force: true })` i wyśle `GET /status`. Gdy to nastąpi po wylogowaniu, zapisze się stan `known` bez timera `aged` (`scheduleAged` wraca przy `!started`, `:213-215`). Skutek jest ograniczony, bo `start()` po zalogowaniu wymusza nowy odczyt.
5. **Drobne — `app/src/app/features/integration/integration-status.ts:240-251`.** Wyjście z `aged` bez zmiany `kind` też jest ogłaszane („Stan integracji: Aktualne”). Spec ogłasza tylko zmianę `kind` i przejścia z i do `unknown`, a plan potwierdza tylko powtarzane wejście w `aged`. Wynika z tego dodatkowe ogłoszenie po każdej przerwie.

## Test Gaps

- Brak testu reguły wieku przy opóźnionym timerze (zmiana `Date.now` bez upływu timerów), czyli Correctness 1.
- Brak testu `presentationOf` dla `aged` przy `error`/`incomplete`/`notConfigured` (Correctness 2).
- `integration-status.spec.ts`: brak testu „długa bezczynność w `running` → brak żądań” (AC: „także w stanie `running`”). Test `:157-169` sprawdza tylko 20 s.
- `integration-status.spec.ts`: brak ogłoszenia przy wyjściu z `unknown` (AC: „przejście do i z »Stan nieznany«”).
- `integration-page.spec.ts`: brak testów „Ponów” wyłączonego w `loading` i dostępnego w `unknown`, dopisku `aged` z podpowiedzią `AGED_HINT`, wyniku synchronizacji `409 integration_not_configured` i limitu 15 s na poziomie ekranu (te przypadki pokrywa tylko `integration-messages.spec.ts`).
- Brak testu, że region wyniku akcji istnieje przed wynikiem (Conventions 1), i testu fokusu po „Ponów” (Correctness 3).

## Code Smells

1. `app/src/app/features/integration/integration-status.ts:165` — `|| this.agedState()` nic nie zmienia, bo `aged` oznacza `now − readAt > 2 × okres`, a `readAt ≥ lastReadAt`, więc `due` jest wtedy już prawdziwe. Warunek `now - this.lastActivityAt <= period` (`:166`) jest zawsze prawdziwy (wartość 0). Oba zostawiono świadomie jako zapis reguły; warto to opisać w komentarzu albo je usunąć.
2. `app/src/app/layout/integration-indicator.html:6` z `integration-indicator.ts:20-24` — biały znak między odznaką a ukrytym tekstem daje nazwę „Aktualne , ostatnia pełna kontrola…”, a w `loading` „Sprawdzanie stanu… . Stan integracji…” (test `integration-indicator.spec.ts:41-49, 61-66` utrwala ten tekst). Spec: „{etykieta}, ostatnia pełna kontrola…”, a w `loading` „sama etykieta”. Czytnik przeczyta zbędną pauzę i kropkę, a w `loading` nazwa ma więcej, niż mówi spec.
3. `app/src/app/features/integration/integration-page.ts:27` — `AGED_HINT` „kliknij lub naciśnij klawisz, aby odświeżyć” zakłada, że każde kliknięcie odświeża. Odświeża zawsze, bo w `aged` odczyt jest należny, więc tekst jest prawdziwy. Działa to jednak tylko dzięki regule z `integration-status.ts`; nie ma testu, który by to wiązał.
4. `app/src/app/features/integration/incomplete-data-notice.ts:21` z `:26-30` — wariant `loading` to sam akapit `text-sm`, a wariant kompletny ma link `min-h-11` (44 px). Spec wymaga „tego samego rozmiaru co wariant kompletny (bez skoku układu)”.

## Recommendation

- Merge as-is: krytycznych uwag nie ma. Przed scaleniem zalecane jest jednak poprawienie ważnych uwag Correctness 1–3 i Conventions 1, bo dotyczą INT-01 (nie zawyżać kompletności) i dostępności wyników akcji. Pozostałe można zostawić albo zapisać jako otwarte w `06-implementation-done.md`.
