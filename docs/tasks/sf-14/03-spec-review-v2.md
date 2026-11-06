# Spec review — SF-14

## Summary

- Overall assessment: FAIL
- Justification: Wersja 2 rozwiązuje oba problemy krytyczne z `03-spec-review-v1.md`. Komunikat renderuje się zawsze, z czasem „Ostatniej pełnej kontroli”, a `dataComplete` w `running` jest `true` tylko po udanym `lastRun`. Rozwiązuje też wszystkie uwagi niekrytyczne i niejasności v1. Kontrakt API (`GetSyncStatus.cs`, `IntegrationsProblems.cs`, `RequestSync.cs`, `BackfillJob.Definition` 30 min, `csrf_invalid` z `SecuritySetup.cs`) oraz kod frontendu (`app-button`, `app-status-badge`, `toApiError`, `app-shell`) zgadzają się ze specyfikacją. Nowy problem jest jeden, ale blokuje plan. Seria odczytów `running` co 10 s bez aktywności, trwająca do 35 min, świadomie łamie wymóg PRD DOST-01 („sesja wygasa po 60 minutach bezczynności”). Decyzję podjął autor specyfikacji bez potwierdzenia właściciela, uzasadnił ją wymogiem, którego INT-01 nie zawiera, i zapisuje ją w `conventions.md` jako ogólną furtkę dla kolejnych zadań.

## Findings

### Critical Issues

1. **Behavior → 3. Odświeżanie („Odświeżanie podczas `running`”); Assumptions, pkt 20 i 21 (wpis w `conventions.md`); Invariant impact, wiersz 3; Acceptance Criteria (kryterium o bezczynnej karcie).**
   - **Sprzeczność z PRD.** PRD DOST-01 i sekcja 3: „Sesja wygasa po 60 minutach bezczynności”. Każde żądanie przedłuża sesję (`SessionCookie.ValidatePrincipalAsync` → `ShouldRenew = true`, `ExpireTimeSpan` = `SessionPolicy.IdleTimeout`). Specyfikacja dopuszcza więc, żeby bezczynna sesja żyła około 95 min od ostatniej aktywności: 35 min serii, do tego jeden okres przed pierwszym odczytem `running` i 60 min od ostatniego żądania. Przykład: użytkownik klika „Synchronizuj teraz” i odchodzi. Inny przykład: był aktywny o 22:59, a przebieg o 23:00 trwa do limitu. Sama specyfikacja nazywa to „świadomym odstępstwem”.
   - **Brak decyzji właściciela.** Według Assumptions („Właściciel zlecił pracę bez dodatkowych pytań. Poniższe punkty to decyzje autora specyfikacji”) odstępstwa od wymogu PRD dotyczącego bezpieczeństwa sesji nikt poza autorem nie zatwierdził. Core sekcja 8: sprzeczność z zapisanym wymaganiem wymaga nowej decyzji, nie obejścia w specyfikacji.
   - **Uzasadnienie nie wynika z INT-01.** Zdanie „INT-01: użytkownik ma zobaczyć wynik synchronizacji bez ruszania myszą” nie ma pokrycia w INT-01 („UI pokazuje ostatnią pełną kontrolę, błąd i dostępną akcję; nie sugeruje kompletności przy awarii…”). INT-01 jest spełnione także bez wyjątku. W trakcie przebiegu nagłówek pokazuje „Synchronizacja trwa”, a komunikat wariant ostrzegawczy (`dataComplete = false` przy `lastRun.status = running`, Assumptions 19), więc UI nie sugeruje kompletności. Wynik pojawi się przy następnej aktywności albo po powrocie na kartę.
   - **Utrwalenie w konwencjach.** Wpis w `conventions.md` („Wyjątek z limitem czasu wymaga uzasadnienia w specyfikacji”) pozwala każdej kolejnej specyfikacji przedłużać bezczynną sesję ponad DOST-01, jeśli tylko poda uzasadnienie.
   - **Do rozstrzygnięcia przed planem**, jednym z dwóch sposobów:
     - usunąć wyjątek i zapisać w Edge Cases znane ograniczenie (bierny obserwator widzi wynik przy następnej aktywności), co v1 dopuszczał jako pierwszą opcję;
     - zapisać jawną decyzję właściciela, która zmienia lub doprecyzowuje DOST-01 (dopuszczalne przedłużenie, jego limit i warunki), i dopiero wtedy zostawić wyjątek oraz wpis w `conventions.md`.
   - Niezależnie od wyboru warto potwierdzić u właściciela tolerancję „plus najwyżej jeden okres” (60 s), bo to również przedłużenie ponad 60 min, choć pomijalne.

### Non-Critical Issues

1. **Behavior → 2 (`refresh({ force })`: „`switchMap` albo `AbortController`”, limit 15 s); Behavior → 5 („Przeładowanie listy po każdym wyniku anuluje wcześniejsze przeładowanie w toku”).** Konwencja projektu (`conventions.md` → Frontend → „State and data fetching”) każe klientom obszaru zwracać Promise (`firstValueFrom`), tak jak w `orders-api.ts` i `auth-api.ts`. Takiego żądania nie da się anulować przez `switchMap`. Specyfikacja zostawia mechanizm do wyboru i nie mówi, czy `integration-api.ts` odstępuje od konwencji. Wiążąca jest gwarancja „odpowiedź starszego żądania nie nadpisuje stanu”. Trzeba ją zapisać jako regułę (np. numer sekwencyjny odczytu, ignorowanie spóźnionych odpowiedzi) i wskazać, gdzie leży limit czasu (opcja `timeout` `HttpClient` albo operator RxJS w serwisie). Ewentualne odstępstwo od konwencji Promise trzeba nazwać.
2. **Behavior → 1 (reguła `dataComplete` dla `running`); Assumptions 19.** Uzasadnienie „wynik trwającego przebiegu nie jest jeszcze znany” dotyczy tak samo żądania przyjętego, a jeszcze nierozpoczętego. Po `202` przy wcześniejszym sukcesie komunikat jest najpierw zwarty (`lastRun.status = succeeded`), po starcie przebiegu staje się ostrzeżeniem, a po sukcesie wraca do zwartej linii. Każdy wariant jest bezpieczny, ale przejścia warto opisać w Edge Cases, żeby SF-19/SF-20 i testy komponentu nie traktowały tego migotania jako błędu.
3. **Behavior → 4 (dostępna nazwa wskaźnika i `title`).** Nazwa „Stan integracji: {etykieta}. Ostatnia pełna kontrola: … Przejdź do ekranu Integracja” zawiera widoczną etykietę, ale od niej się nie zaczyna. Dla „Ponów” specyfikacja stosuje ostrzejszą regułę „nazwa zaczyna się od widocznej etykiety (WCAG 2.5.3)”, a tu nie, więc warto to ujednolicić. Do tego `title` z tym samym czasem staje się opisem linku, przez co czytnik czyta czas dwa razy. Trzeba ustalić kolejność tekstu ukrytego względem odznaki i to, czy `title` ma być pomijany w drzewie dostępności.
4. **Behavior → 5 (kolumna „Akcja”, równoległe „Ponów”).** `app-button` przy `busy` ukrywa całą treść (`[hidden]="busy()"`), razem z `<span class="sr-only"> zamówienie {externalId}</span>`. Kilka równoległych ponowień daje kilka przycisków o tej samej nazwie „Ponawianie…”. Trzeba albo to zaakceptować, albo zapisać, że nazwa w `busy` też wskazuje zamówienie.
5. **Behavior → 5 („Synchronizuj teraz”, `aria-describedby`); UI → Dostępność.** Natywny `disabled` usuwa przycisk z kolejności Tab, więc `aria-describedby` pomaga tylko w trybie przeglądania czytnika. Wymóg design guide sekcja 2 („Wyłączenie kontrolki nie może ukrywać instrukcji”) spełnia widoczny tekst powodu obok przycisku, a nie samo powiązanie. Zapis „Wyłączenie nie ukrywa więc instrukcji” przypisuje skutek złemu elementowi. Kryterium jest jednak spełnione.

### Unclear or Ambiguous Sections

- **Behavior → 3 („Seria kończy się przy pierwszym odczycie innym niż `running`”).** Nie wiadomo, czy nieudany odczyt (błąd, limit 15 s, czyli `unknown`) kończy serię. Jeśli tak, jedno wolne żądanie w trakcie przebiegu zatrzymuje odczyty, a UI zostaje na „Stan nieznany” do aktywności. Jeśli nie, seria trwa dalej. Ten punkt zależy też od rozstrzygnięcia Critical 1.
- **Behavior → 2 (sygnały `status` i `lastFullCheckAtUtc`) a Behavior → 6 (wariant `unknown`) i Behavior → 4 (`title`).** Nie wiadomo, czy w `unknown` po wcześniejszym sukcesie „Ostatnia pełna kontrola” pokazuje ostatni znany czas, czy „Brak danych”. Nie określono też, czy `status` zachowuje poprzednią odpowiedź (od tego zależy np. `configured` dla „Ponów”).
- **Behavior → 3 (aktywność: `pointerdown`, bez `wheel`).** Przewijanie dotykiem wywołuje `pointerdown`, a kółkiem nie. Ta niespójność nie łamie DOST-01, ale warto ją zapisać świadomie, bo trafia do `conventions.md`.

### Invented or Unsupported Requirements

- **Behavior → 3, seria `running` bez aktywności.** Wymóg „wynik synchronizacji ma się pojawić bez ruchu użytkownika” przypisano INT-01, ale nie wynika on ani z INT-01, ani ze zgłoszenia (Critical 1).

Pozostałe elementy spoza dosłownego zgłoszenia mają uzasadnienie w PRD, w design guide albo w API SF-13 i są jawnie opisane w Assumptions:
- stany techniczne „Nie skonfigurowano”, „Sprawdzanie stanu…” i „Stan nieznany” (Assumptions 2 i 7, żaden nie daje `dataComplete = true`);
- filtr Otwarte/Zamknięte/Wszystkie (Assumptions 14);
- limit 15 s (Assumptions 22);
- wejście `describedBy` w `app-button` (Assumptions 24).

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → 3 (seria `running`); Assumptions 20–21; wpis w `conventions.md` | PRD DOST-01 i sekcja 3 („Sesja wygasa po 60 minutach bezczynności”); `SessionCookie` (`ShouldRenew = true`) | Bezczynna sesja może żyć ok. 95 min. Odstępstwo bez decyzji właściciela, uzasadnione wymogiem, którego INT-01 nie zawiera (Critical 1) |
| Behavior → 3 („plus najwyżej jeden okres”) | PRD DOST-01 | Przedłużenie o ≤ 60 s. Pomijalne, ale też niepotwierdzone |
| Behavior → 2 (`switchMap` / `AbortController`) | `conventions.md` → „State and data fetching” (klienci zwracają Promise przez `firstValueFrom`) | Mechanizm anulowania nieokreślony i potencjalnie niezgodny z konwencją (Non-Critical 1) |
| Behavior → 6 (komunikat zawsze) | Design guide sekcja 4 („Komunikat o niekompletnych danych, jeżeli występuje”) | Brak rzeczywistej sprzeczności: wariant kompletny to zwarta linia czasu bez ostrzeżenia, a PRD sekcja 5 („zawsze”) ma pierwszeństwo |
| Assumptions 4 („Ostatnia pełna kontrola” = kursor) | PRD sekcja 5 („czas ostatniego sukcesu”) | Pokazywany jest kursor (start − 10 min). Wariant ostrożniejszy, zgodny z SF-13 S2 i zapisany jawnie |
| Behavior → 1 (`notConfigured`) | PRD sekcja 5 (cztery stany); design guide sekcja 6 | Stan techniczny wynikający z INT-01, jawny w Assumptions 2. Bez sprzeczności merytorycznej |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Brak kwot i raportów finansowych |
| 2. Backend decimal money | Not applicable | Frontend nie liczy kwot |
| 3. API-enforced access | OK | Endpointy SF-13 wymagają sesji, zapisy XSRF (`csrf_invalid` z `SecuritySetup.cs`), `401` obsługuje interceptor. API nadal uwierzytelnia każde żądanie. Przedłużanie bezczynnej sesji to sprzeczność z PRD DOST-01, a nie naruszenie tego inwariantu (Critical 1) |
| 4. Retries never duplicate | OK | Ochrona po stronie backendu (SF-12/SF-13). UI blokuje podwójne kliknięcie na wiersz i dla „Synchronizuj teraz” |
| 5. Financial audit trail | Not applicable | UI nie zapisuje danych finansowych |
| 6. Secrets stay server-side | OK | Brak kluczy i adresu sklepu w bundlu. Teksty z `IntegrationsProblems.cs` bez szczegółów sklepu |
| 7. Module and layer boundaries | OK | Frontend woła tylko API Integrations, link do zamówienia z `orderId`, logika stanu w czystej funkcji poza komponentami |
| 8. No identifying data in docs | OK | Dane testowe fikcyjne („1001”, „Z/2026/0001”) |

## Risk

- Recorded: medium
- Assessed: medium. Wskaźnik i komunikat są wspólne dla całej aplikacji oraz dla SF-19/SF-20, a odświeżanie dotyka zasady wygasania sesji (DOST-01) i zmienia `conventions.md`. Zadanie nie dotyka pieniędzy, danych klientów ani operacji nieodwracalnych, a backend zostaje bez zmian.

## Assumptions Detected

- Stan INT-01 liczy frontend czystą funkcją. Podane (1), zgodne z `GetSyncStatus.cs`.
- „Nie skonfigurowano” jest sprawdzane przed stanami PRD. Podane (2).
- „Błąd” obejmuje każdą wartość różną od `succeeded`. Podane (3).
- Próg 25 h liczony od kursora, z akceptowanym fałszywym alarmem w dniu zmiany czasu i przy opóźnionym starcie. Podane (4) i w Edge Cases.
- Czas „teraz” pochodzi z zegara klienta. Podane (5).
- `dataComplete` w `running` wymaga udanego `lastRun`. Podane (19), zgodne z `queue.IsPending` w `GetSyncStatus.cs`.
- Brak odczytu daje `unknown` i `dataComplete = false`. Podane (7).
- Odświeżanie zależy od aktywności, a aktywność to `keydown`, `pointerdown` i powrót na kartę. Podane (8, 21).
- Wynik synchronizacji ma się pojawić bez aktywności, co uzasadnia przedłużenie sesji o 35 min. Podane (20), ale bez pokrycia w INT-01 i bez decyzji właściciela (Critical 1).
- Limit przebiegu wynosi 30 min. Podane (20), zgodne z `BackfillJob.Definition` (`TimeSpan.FromMinutes(30)`).
- Spóźnione odpowiedzi da się anulować lub zignorować przy klientach zwracających Promise. Niepodane, mechanizm zostawiony do wyboru (Non-Critical 1).
- Ostatnia znana odpowiedź jest zachowywana w `unknown`. Niepodane (Unclear).
- Ciasteczko `XSRF-TOKEN` ustawia sesja, więc `GET /api/auth/csrf` nie jest potrzebne. Podane w Data / API, zgodne z `XsrfCookie.cs` i wzorcem zapisów zamówień.
- Wskaźnik, a więc odświeżanie, jest obecny wszędzie tam, gdzie komunikat. Podane (Behavior 6).

## Recommendation

- Revise specification. Wystarczy rozstrzygnąć Critical 1 (usunąć wyjątek albo zapisać decyzję właściciela co do DOST-01) i doprecyzować Non-Critical 1 oraz niejasności. Reszta specyfikacji jest gotowa do planu.
