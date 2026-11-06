# Spec review — SF-14

## Summary

- Overall assessment: FAIL
- Justification: Specyfikacja jest dokładna i w większości zgodna z kodem. Nazwy i typy pól DTO, kody `409`/`404`/`502`/`503` z `IntegrationsProblems.cs`, mapowanie `abandoned`, `isRunning` z `queue.IsPending`, kursor `null` przed pierwszym sukcesem, sposób, w jaki `toApiError` gubi `code` dla `5xx`, oraz przedłużanie sesji przy każdym żądaniu (`SessionCookie.ValidatePrincipalAsync` → `ShouldRenew = true`, `ExpireTimeSpan` 60 min, `SlidingExpiration`) zgadzają się z repozytorium. Dwa problemy dotyczą jednak sedna INT-01. Po pierwsze, przy wynikach „Ostatnia pełna kontrola” nie jest widoczna zawsze, jak wymaga PRD sekcja 5: komunikat dla SF-19/SF-20 przy kompletnych danych nie renderuje niczego. Po drugie, reguła `dataComplete = true` w stanie „Synchronizacja trwa” ukrywa komunikat po nieudanym przebiegu, a jej uzasadnienie jest częściowo sprzeczne z API. Obie sprawy zmieniają kontrakt, z którego skorzystają SF-19 i SF-20, więc trzeba je rozstrzygnąć w specyfikacji przed planem.

## Findings

### Critical Issues

1. **Behavior → 6. Komunikat o niekompletnych danych; Behavior → 4. Wskaźnik w nagłówku; Acceptance Criteria (kryterium `app-incomplete-data-notice`).** PRD sekcja 5: „przy wynikach **zawsze** widać czas ostatniego sukcesu jako »Ostatnia pełna kontrola«”. Design guide sekcja 6, wiersz „Synchronizacja”, mówi to samo: „zawsze »Ostatnia pełna kontrola« z czasem ostatniego sukcesu”. W specyfikacji czas pokazują tylko ekran „Integracja” i `app-incomplete-data-notice`, a komunikat przy `dataComplete = true` „nie renderuje niczego”. Wskaźnik w nagłówku pokazuje samą etykietę stanu, bez czasu. W stanie „Aktualne” widok raportowy (SF-19/SF-20 osadzają według zgłoszenia tylko ten komunikat) nie pokaże więc żadnego czasu ostatniej pełnej kontroli. PRD uzasadnia ten wymóg tym, że pominiętego webhooka nie da się wykryć przed uzupełnianiem, więc „Aktualne” nie zwalnia z pokazania czasu. Specyfikacja musi wskazać element, który spełnia „zawsze”: na przykład czas w wskaźniku nagłówka (nagłówek jest przy każdym widoku) albo stały, zwarty wariant komunikatu dla danych kompletnych. Od tego wyboru zależy kontrakt dla SF-19/SF-20, więc nie można go zostawić planowi.
2. **Behavior → 1. Wyliczanie stanu (reguła `dataComplete`: „`running` → `true` tylko wtedy, gdy lista powodów jest pusta”); Assumptions, pkt 6.** Zgłoszenie wymaga, żeby „UI nie sugeruje kompletności przy awarii”, a INT-01 mówi to samo. Weźmy scenariusz: przebieg kończy się `failed` albo `abandoned` przy świeżym kursorze i bez otwartych błędów, a stan to „Błąd” z komunikatem. Użytkownik klika „Synchronizuj teraz”. Stan zmienia się na „Synchronizacja trwa”, `dataComplete` na `true`, a komunikat w widokach raportowych znika na cały czas przebiegu, choć ostatni znany wynik to awaria. Uzasadnienie w pkt 6 („Wynik poprzedniego przebiegu w trakcie nowego jest niewidoczny w API”) jest częściowo nieprawdziwe. Po przyjęciu żądania `isRunning = true` wynika z `queue.IsPending` (`GetSyncStatus.cs`), a `lastRun` to wtedy nadal poprzedni, nieudany przebieg, widoczny w odpowiedzi. W trakcie samego przebiegu wynik nie jest znany, a przy niepewności INT-01 każe przyjąć wariant ostrożny. Kryterium „Przy awarii … żaden element nie pokazuje »Aktualne«” tego przypadku nie wychwytuje, bo komunikat znika bez etykiety „Aktualne”. Trzeba zmienić regułę, na przykład `running` → `dataComplete = false`, albo co najmniej `false`, gdy znany `lastRun` jest różny od `succeeded`. Trzeba też poprawić założenie 6 i dopisać przypadek do Edge Cases i testów.

### Non-Critical Issues

1. **Behavior → 3. Odświeżanie; Behavior → 4 (region `aria-live`); Verification → manual („Synchronizuj teraz → … potem »Aktualne« albo »Błąd«”).** Okres 10 s przy `running` też wymaga aktywności po ostatnim żądaniu. Kliknięcie „Synchronizuj teraz” poprzedza `refresh()` po `202`, więc kolejne odświeżenie czeka na następną aktywność. Kto po kliknięciu tylko patrzy (albo słucha czytnika ekranu), widzi „Synchronizacja trwa” bez końca. Ogłoszenie „Stan integracji: …” nie przychodzi, a przeładowanie listy po końcu synchronizacji się nie odbywa. Region `aria-live` informuje więc biernie tylko o zmianach z zegara lokalnego (granica 25 h). Trzeba to zapisać jako znane ograniczenie albo dodać ograniczony wyjątek: odpytywanie bez aktywności po akcji użytkownika, najdłużej przez limit przebiegu (30 min). Wyjątek trzeba wtedy osobno uzasadnić względem DOST-01, bo przedłuża sesję najwyżej o ten limit.
2. **Behavior → 2 (`refresh()`: „równoległe wywołania współdzielą jedno żądanie w toku”) i 3 („Należne, ale wstrzymane odświeżenie wysyła się przy pierwszej aktywności”; „Po akcjach”).** Gdy karta była bezczynna dłużej niż okres, kliknięcie „Synchronizuj teraz” lub „Ponów” (`pointerdown`) najpierw wysyła należne `GET /status`, a dopiero potem `POST`. `refresh()` po `202` dołącza do tego żądania w toku i dostaje stan sprzed akcji, czyli `isRunning = false`. UI nie pokazuje wtedy „Synchronizacja trwa”, a okres zostaje 60 s. Trzeba ustalić, że odświeżenie po akcji wysyła nowe żądanie po odpowiedzi akcji albo ponawia odczyt po zakończeniu żądania w toku. Przypadek należy dodać do testów `integration-status.spec.ts`.
3. **Behavior → 3 (lista zdarzeń aktywności); Scope → In scope (wpis w `conventions.md`); Assumptions, pkt 8.** Kierunek jest poprawny i sprawdzony w kodzie: przy `ShouldRenew = true` każde żądanie przesuwa 60 min bezczynności, więc stałe odpytywanie łamałoby DOST-01. Ograniczenie „60 min od ostatniej aktywności plus najwyżej jeden okres” też jest policzone dobrze. Lista zdarzeń zawiera jednak `pointermove` i `wheel`, które przychodzą także nad widocznym, nieaktywnym oknem przeglądarki, gdy użytkownik pracuje w innym programie. Samo przesunięcie kursora nad oknem podtrzymuje wtedy sesję. Specyfikacja ustala tu, czym jest „bezczynność” z DOST-01, i zapisuje to jako regułę projektu w `conventions.md`, a według Assumptions bez pytań do właściciela. Trzeba albo zawęzić listę do celowych działań (`pointerdown`, `keydown`, ewentualnie `touchstart`), albo zapisać tę interpretację jako decyzję do potwierdzenia przez właściciela.
4. **Behavior → 6 („W stanie `loading` … nie renderuje niczego”); Behavior → 2.** Nie określono limitu czasu `GET /status`. Wiszące żądanie zatrzymuje stan `loading`, a wspólne żądanie w toku blokuje kolejne odczyty. Widoki raportowe pokazują wtedy wyniki bez komunikatu, czyli jak kompletne. Trzeba podać limit czasu prowadzący do `unknown` albo pokazywać komunikat w wariancie `unknown` po krótkim czasie ładowania.
5. **Assumptions, pkt 4 („zostaje co najmniej 20 min zapasu”); Edge Cases.** W dniu zmiany czasu z letniego na zimowy przebiegi o 23:00 Europe/Warsaw dzieli 25 h czasu rzeczywistego. Kursor to start − 10 min, więc warunek „ponad 25 godzin” zaczyna działać ok. 22:50 CET, przed startem przebiegu. Raz w roku przez ok. 10 min UI fałszywie pokazuje „Dane niekompletne”. To samo dzieje się, gdy uśpiony hosting uruchomi przebieg z opóźnieniem większym niż 50 min (D-08). Fałszywy alarm jest bezpieczny, ale założenie trzeba poprawić, a przypadek wpisać do Edge Cases.
6. **Behavior → 3 („po »Ponów« (każdy wynik poza błędem sieci) → `refresh()`”) a Behavior → 5, tabela „Ponów”, wiersz `400` `csrf_invalid` („—”).** Te dwa miejsca są ze sobą sprzeczne. Trzeba wybrać jedno zachowanie.
7. **Behavior → 5 (kolumna „Akcja”: dostępna nazwa „Ponów zamówienie {externalId}”); UI → Komponenty i pliki.** `app-button` nie ma wejścia na `aria-label`, a tabela plików nie przewiduje zmiany `shared/ui/button.ts`. Trzeba zapisać, czy nazwa powstaje z ukrytego tekstu w treści przycisku (z zachowaniem etykiety widocznej w nazwie, WCAG 2.5.3), czy z rozszerzenia przycisku. Podobnie wyłączone „Synchronizuj teraz” nie przyjmuje fokusu. Powód wyłączenia warto powiązać z przyciskiem (`aria-describedby`), żeby użytkownik klawiatury i czytnika go znalazł (design guide sekcja 2: wyłączenie nie może ukrywać instrukcji).
8. **Acceptance Criteria (pierwsze kryterium); Verification → `integration-state.spec.ts`.** Lista wymaganych przypadków nie obejmuje pierwszeństwa `notConfigured` przed `running`, `isRunning = true` przy nieudanym `lastRun`, nieznanej wartości `lastRun.status` → „Błąd” ani `dataComplete` w stanie `running`. Te przypadki są w Edge Cases i powinny trafić do kryterium.

### Unclear or Ambiguous Sections

- **Behavior → 5 („Ponów” „tylko przy `configured = true`”; „Synchronizuj teraz” wyłączone przy `running` i `notConfigured`).** Nie określono dostępności obu akcji, gdy stan to `loading` albo `unknown`, czyli gdy `configured` nie jest znane.
- **Behavior → 2 (`announcement` „tylko przy zmianie `kind` względem poprzedniego znanego stanu”).** Nie wiadomo, czy przejście do „Stan nieznany” i powrót z niego są ogłaszane. `unknown` nie jest `kind` funkcji, a „znany stan” sugeruje, że nie.
- **Behavior → 3 („należne po 60 s od ostatniego żądania”).** Nie wiadomo, czy chodzi o ostatnie żądanie `/status`, czy o dowolne żądanie API (na przykład zapis zamówienia, który i tak przedłuża sesję).
- **Behavior → 5, tabela „Ponów”.** Brak zachowania dla `404` bez kodu (`toApiError` → `problem` z `code = null`) i dla równoległych „Ponów” w dwóch wierszach (dwa przeładowania listy, kolejność komunikatów w jednym regionie wyniku).

### Invented or Unsupported Requirements

None.

Stany „Nie skonfigurowano”, „Sprawdzanie stanu…” i „Stan nieznany” wykraczają poza cztery stany PRD sekcja 5. Design guide sekcja 6 prosi, żeby nie dodawać statusów biznesowych. Te stany nie są jednak statusami biznesowymi, tylko wynikają z INT-01 („nie sugeruje kompletności przy awarii”). Specyfikacja jawnie je uzasadnia (Assumptions 2 i 7), a żaden z nich nie daje `dataComplete = true`. Filtr „Zamknięte/Wszystkie” wynika z „historii błędów” w zgłoszeniu. Wpis w `conventions.md` dokumentuje regułę wynikającą z DOST-01 (zastrzeżenie w Non-Critical 3).

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → 6; Behavior → 4 | PRD sekcja 5 („przy wynikach zawsze widać … »Ostatnia pełna kontrola«”); design guide sekcja 6 (Statusy, „Synchronizacja”) | Przy kompletnych danych żaden element widoku raportowego nie pokazuje czasu ostatniej pełnej kontroli (Critical 1) |
| Behavior → 1 (reguła `dataComplete` dla `running`); Assumptions 6 | INT-01 i kryterium zgłoszenia „UI nie sugeruje kompletności przy awarii”; `GetSyncStatus.cs` (`lastRun` widoczny przy `queue.IsPending`) | Komunikat znika po awarii na czas kolejnego przebiegu; uzasadnienie niezgodne z API (Critical 2) |
| Behavior → 1 (krok 1, `notConfigured`) | PRD sekcja 5 (cztery stany); design guide sekcja 6 („Nie dodawać statusów biznesowych…”) | Dodatkowy stan techniczny; uzasadniony INT-01 i jawny w Assumptions 2, bez sprzeczności merytorycznej |
| Assumptions 4 („Ostatnia pełna kontrola” = kursor) | PRD sekcja 5 („czas ostatniego sukcesu”) | Pokazywany jest kursor (start − 10 min), nie czas sukcesu; wariant ostrożniejszy, zgodny z SF-13 S2 i zapisany jawnie |
| Behavior → 3; wpis w `conventions.md` | PRD DOST-01 (60 min bezczynności) | Brak sprzeczności co do kierunku. Definicja aktywności (`pointermove`, `wheel`) to niepotwierdzona interpretacja „bezczynności” (Non-Critical 3) |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Brak kwot i raportów finansowych |
| 2. Backend decimal money | Not applicable | Frontend nie liczy kwot |
| 3. API-enforced access | OK | Endpointy SF-13 wymagają sesji (polityka domyślna), zapisy XSRF; `401` obsługuje interceptor; odświeżanie w tle nie podtrzymuje bezczynnej sesji (zastrzeżenie co do definicji aktywności w Non-Critical 3) |
| 4. Retries never duplicate | OK | Ochrona po stronie backendu (SF-12/SF-13); UI blokuje podwójne kliknięcie |
| 5. Financial audit trail | Not applicable | UI nie zapisuje danych finansowych |
| 6. Secrets stay server-side | OK | Brak kluczy i adresu sklepu w bundlu; komunikaty z `IntegrationsProblems.cs` bez szczegółów sklepu |
| 7. Module and layer boundaries | OK | Frontend woła tylko API Integrations; link do zamówienia z `orderId` bez wywołań Orders; logika w czystej funkcji poza komponentem |
| 8. No identifying data in docs | OK | Dane testowe fikcyjne („1001”, „Z/2026/0001”) |

## Risk

- Recorded: medium
- Assessed: medium. Wskaźnik i komunikat są wspólne dla całej aplikacji i dla SF-19/SF-20, a odświeżanie dotyka zasady wygasania sesji (DOST-01). Zadanie nie zmienia pieniędzy, danych klientów ani operacji nieodwracalnych, a backend zostaje bez zmian.

## Assumptions Detected

- Stan INT-01 liczy frontend czystą funkcją. Podane (1), zgodne z SF-13 („Stany INT-01 wylicza SF-14”).
- „Nie skonfigurowano” jest sprawdzane przed stanami PRD. Podane (2).
- „Błąd” obejmuje każdą wartość różną od `succeeded`. Podane (3).
- Próg 25 h liczony od kursora, z zapasem „co najmniej 20 min”. Podane (4), ale zapas jest nieprawdziwy w dniu zmiany czasu (Non-Critical 5). Ukryte jest założenie, że przebiegi dzieli zawsze 24 h.
- Czas „teraz” pochodzi z zegara klienta. Podane (5).
- W trakcie przebiegu wynik poprzedniego jest niewidoczny. Podane (6), częściowo nieprawdziwe (Critical 2).
- Brak odczytu daje `unknown` i `dataComplete = false`. Podane (7).
- Odświeżanie zależy od aktywności. Podane (8). Niepodane: że ruch kursora i kółko liczą się jako aktywność w rozumieniu DOST-01 (Non-Critical 3).
- SF-19/SF-20 same pokażą „Ostatnią pełną kontrolę” przy kompletnych danych. Niepodane, ukryte (Critical 1).
- Żądanie `/status` kończy się w skończonym czasie. Niepodane (Non-Critical 4).
- Ciasteczko `XSRF-TOKEN` istnieje po przeładowaniu strony. Podane w Data / API i zgodne z wzorcem zapisów zamówień (bez `GET /api/auth/csrf`).
- Wskaźnik, a więc odświeżanie, jest zawsze obecny tam, gdzie komunikat. Podane (Behavior 6).
- Limit przebiegu wynosi 30 min. Podane w pkt 4, bez odwołania do miejsca w kodzie SF-13.

## Recommendation

- Revise specification
