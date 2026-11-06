# Spec review — SF-14

## Summary

- Overall assessment: FAIL
- Justification: Wersja 3 rozwiązuje problem krytyczny z `03-spec-review-v2.md`. Wyjątek odświeżania bez aktywności w `running` zniknął, a wpis do `conventions.md` nie zostawia furtki. Rozwiązane są też wszystkie uwagi niekrytyczne i niejasności v2: Promise z numerem sekwencyjnym i operatorem `timeout`, migotanie po `202`, wskaźnik bez `title`, `busyLabel`, uzasadnienie powodu wyłączenia, `unknown` z ostatnią odpowiedzią. Kod (`SessionCookie`, `app-button`, `app-status-badge`, `app-shell`, konwencja `firstValueFrom`) zgadza się z opisem. Blokuje jeden nowy problem. Uzasadnienie zgodności z INT-01 (Assumptions 25: „UI może najwyżej zaniżać kompletność, nigdy jej zawyżać”) jest nieprawdziwe. Przy bezczynnej, widocznej karcie w stanie „Aktualne” (także gdy użytkownik czyta raport i przewija kółkiem) awaria przebiegu albo nowy błąd zamówienia nie zmienia wskaźnika ani komunikatu do najbliższego kliknięcia lub klawisza. Czas tego opóźnienia nie ma górnej granicy. To sprzeczne z kryterium zgłoszenia „UI nie sugeruje kompletności przy awarii” i z własnym kryterium akceptacji specyfikacji.

## Findings

### Critical Issues

1. **Behavior → 3. Odświeżanie („Znane ograniczenie”); Assumptions, pkt 25 i 30; Edge Cases („Znane ograniczenie — bierny obserwator”); Acceptance Criteria („Przy awarii … żaden element nie pokazuje »Aktualne«”).**
   - **Ograniczenie opisano tylko dla `running`.** Ta sama reguła aktywności dotyczy stanu „Aktualne”. Przykład: o 22:30 użytkownik otwiera raport (po SF-19/SF-20 z `app-incomplete-data-notice`) i tylko go czyta, przewijając kółkiem (Assumptions 30: `wheel` nie jest aktywnością). O 23:00 przebieg kończy się błędem albo webhook zapisuje błąd zamówienia. Bez kliknięcia i klawisza nie ma odczytu `/status`, więc nagłówek dalej pokazuje „Aktualne”, a komunikat ma wariant zwarty (`dataComplete = true`). Lokalny zegar co 60 s łapie tylko granicę 25 h, nie awarię ani nowe błędy.
   - **Zdanie z Assumptions 25 jest fałszywe.** „UI może najwyżej zaniżać kompletność, nigdy jej zawyżać” jest prawdą tylko dla stanu zawieszonego w `running`. Na tym zdaniu stoi argument zgodności z INT-01 („nie sugeruje kompletności przy awarii”) i z celem zgłoszenia („Użytkownik nie może sądzić, że dane są kompletne, gdy nie są”).
   - **Kryterium akceptacji nie da się spełnić w tym scenariuszu.** Specyfikacja nie ogranicza go do odczytanego stanu, a test na sztucznych timerach (bezczynna karta, potem awaria w API) by go nie przeszedł.
   - **Do rozstrzygnięcia przed planem.** Specyfikacja musi przyznać ten przypadek i zapewnić, że UI nie zawyża kompletności, bez łamania DOST-01. Przykładowe kierunki do wyboru przez autora:
     - lokalna reguła wieku odczytu: gdy ostatni udany odczyt jest starszy niż np. dwa okresy, stan i komunikat przechodzą w wariant ostrożny („stan sprzed {czas}”, `dataComplete = false`). Działa na istniejącym zegarze, bez żądań;
     - uznanie `wheel`/przewijania za aktywność. Skraca to okno, ale go nie zamyka;
     - jawne przyjęcie ograniczenia decyzją właściciela, ze zmianą kryterium akceptacji. Wtedy trzeba usunąć zdanie „nigdy jej zawyżać”.

### Non-Critical Issues

1. **Assumptions, pkt 26 (tolerancja „+ jeden okres”); Invariant impact, wiersz 3.** Tolerancję potwierdził koordynator, a nie właściciel produktu. Wstęp do Assumptions mówi, że to decyzje autora. To nie jest decyzja zmieniająca DOST-01 w rozumieniu core sekcji 8. Nie blokuje, bo nadwyżka jest ograniczona (≤ 60 s) i przenosi na serwer rzeczywistą aktywność sprzed najwyżej jednego okresu, a nie podtrzymuje bezczynnej sesji. Trzeba jednak zapisać to uczciwie jako interpretację autora („bezczynność liczona od celowej aktywności, z dokładnością do jednego okresu”), bez sugerowania zatwierdzenia przez właściciela. Najlepiej potwierdzić u właściciela przed przeglądem kodu albo usunąć nadwyżkę (np. nie wysyłać należnego odczytu, gdy aktywność, która go uprawnia, jest starsza niż okres).
2. **Behavior → 3 („od ostatniego żądania `/status` wystąpiła aktywność”) a Assumptions 25 i 27 („od poprzedniego żądania w tle”); Edge Cases („bierny obserwator: jeden odczyt po 10 s”, „Bezczynna karta, kliknięcie »Synchronizuj teraz«”).** Punkt odniesienia dla wymogu aktywności jest niejednoznaczny. Jeśli liczy się każde żądanie `/status`, także `refresh({ force: true })` po `202`, to kliknięcie poprzedza żądanie n+1 i odczyt po 10 s nie nastąpi, wbrew Edge Case. Jeśli liczy się tylko żądanie w tle, odczyt nastąpi. W scenariuszu z bezczynną kartą `pointerdown` i żądanie n mają ten sam moment, więc wynik zależy od `>` albo `≥`. Specyfikacja musi wskazać, czy żądania wymuszone zerują znacznik aktywności i od którego żądania liczy się okres. Od tego zależy test „bezczynna karta → `pointerdown` → akcja `202`”.
3. **Assumptions, pkt 8 i 21.** Pkt 8 odsyła do „serii `running` co 10 s … w pkt 20”, a pkt 20 jest zastąpiony. Pkt 21 przytacza starą treść wpisu do `conventions.md` („od poprzedniego żądania”, bez zdania o kursorze i kółku), a pkt 27 nową. Dwie wersje wiążącego tekstu sprzyjają skopiowaniu złej. Należy zostawić jedną.

### Unclear or Ambiguous Sections

- **Behavior → 5 („»Spróbuj ponownie« wywołuje `refresh()`”) a Behavior → 2 („z `force: true` … po »Spróbuj ponownie«”).** Nie wiadomo, czy ponowienie z `app-error-state` wymusza nowe żądanie, czy dołącza do żądania w toku.
- **Behavior → 2 (limit 15 s) a Behavior → 5 (lista błędów).** Limit czasu opisano tylko dla `GET /status`. Nie wiadomo, czy `GET /errors` i `POST …/retry` mają limit. Zawieszone „Ponów” blokuje wiersz (`busy`) bez końca.

### Invented or Unsupported Requirements

None.

Elementy spoza dosłownego zgłoszenia mają oparcie w PRD, design guide albo API SF-13 i są jawnie opisane w Assumptions: stany techniczne „Nie skonfigurowano”, „Sprawdzanie stanu…” i „Stan nieznany” (2, 7), filtr trzech wartości (14), limit 15 s (22, 29), wejście `describedBy` (24), wpis w `conventions.md` (27).

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → 3; Assumptions 25 i 30 | PRD INT-01 („nie sugeruje kompletności przy awarii”); zgłoszenie, kryterium „UI nie sugeruje kompletności przy awarii” | Przy bezczynnej karcie „Aktualne” i zwarty komunikat zostają po awarii bez ograniczenia czasu (Critical 1) |
| Assumptions 26; Invariant impact, wiersz 3 | PRD DOST-01 i sekcja 3 („Sesja wygasa po 60 minutach bezczynności”) | Nadwyżka ≤ 60 s potwierdzona przez koordynatora, nie przez właściciela. Pomijalna, ale trzeba ją opisać jako interpretację (Non-Critical 1) |
| Behavior → 6 (komunikat zawsze) | Design guide sekcja 4 („Komunikat o niekompletnych danych, jeżeli występuje”) | Brak rzeczywistej sprzeczności: wariant kompletny to linia czasu, a PRD sekcja 5 („przy wynikach zawsze”) ma pierwszeństwo |
| Assumptions 4 („Ostatnia pełna kontrola” = kursor) | PRD sekcja 5 („czas ostatniego sukcesu”) | Kursor (start − 10 min), wariant ostrożniejszy, zgodny z SF-13 S2 i zapisany jawnie |
| Behavior → 1 (`notConfigured`) | PRD sekcja 5 (cztery stany) | Stan techniczny wynikający z INT-01, jawny w Assumptions 2 |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Brak kwot i raportów finansowych |
| 2. Backend decimal money | Not applicable | Frontend nie liczy kwot |
| 3. API-enforced access | OK | Endpointy SF-13 wymagają sesji, zapisy XSRF, `401` obsługuje interceptor. Odświeżanie wymaga aktywności bez wyjątków. Tolerancja ≤ 60 s dotyczy DOST-01, nie inwariantu (Non-Critical 1) |
| 4. Retries never duplicate | OK | Ochrona po stronie backendu (SF-12/SF-13). UI blokuje podwójne kliknięcie na wiersz i dla „Synchronizuj teraz” |
| 5. Financial audit trail | Not applicable | UI nie zapisuje danych finansowych |
| 6. Secrets stay server-side | OK | Brak kluczy i adresu sklepu w bundlu, komunikaty z API bez sekretów |
| 7. Module and layer boundaries | OK | Tylko API Integrations, link z `orderId`, logika stanu w czystej funkcji i serwisie poza komponentami |
| 8. No identifying data in docs | OK | Dane testowe fikcyjne („1001”, „Z/2026/0001”) |

## Risk

- Recorded: medium
- Assessed: medium. Wskaźnik i komunikat są wspólne dla całej aplikacji oraz SF-19/SF-20, więc błąd może sugerować kompletność raportów (INT-01, Critical 1). Odświeżanie dotyka DOST-01 i zmienia `conventions.md`. Zadanie nie dotyka pieniędzy, danych klientów ani operacji nieodwracalnych, a backend się nie zmienia.

## Assumptions Detected

- Stan INT-01 liczy frontend czystą funkcją. Podane (1), zgodne z `GetSyncStatus.cs`.
- „Nie skonfigurowano” sprawdzane przed stanami PRD. Podane (2).
- „Błąd” dla każdej wartości różnej od `succeeded`. Podane (3).
- Próg 25 h od kursora, akceptowany fałszywy alarm przy zmianie czasu i opóźnionym starcie. Podane (4), Edge Cases.
- Czas „teraz” z zegara klienta. Podane (5).
- `dataComplete` w `running` wymaga udanego `lastRun`. Podane (19).
- Brak odczytu daje `unknown`, z zachowaną poprzednią odpowiedzią. Podane (7, 31).
- Aktywność to `keydown`, `pointerdown` i powrót na kartę, bez `wheel`. Podane (21, 30).
- Bierny obserwator widzi wynik przebiegu przy najbliższej akcji. Podane (25), tylko dla `running`.
- UI nigdy nie zawyża kompletności. Podane (25), ale nieprawdziwe dla stanu „Aktualne” przy bezczynnej karcie (Critical 1).
- Nadwyżka sesji ≤ 60 s jest zgodna z DOST-01. Podane (26), potwierdzone przez koordynatora, nie przez właściciela (Non-Critical 1).
- Żądanie wymuszone po akcji liczy się (albo nie) jako „poprzednie żądanie” w regule aktywności. Niepodane (Non-Critical 2).
- Limit czasu dotyczy tylko `GET /status`. Niepodane wprost (Unclear).
- Wskaźnik, a z nim odświeżanie, jest wszędzie tam, gdzie komunikat. Podane (Behavior 6).
- Ciasteczko `XSRF-TOKEN` ustawia sesja. Podane w Data / API.

## Recommendation

- Revise specification. Rozstrzygnąć Critical 1 (zawyżona kompletność przy bezczynnej karcie poza `running`), poprawić opis tolerancji z pkt 26 i ujednolicić punkt odniesienia reguły aktywności (Non-Critical 1–2). Reszta specyfikacji jest gotowa do planu.
