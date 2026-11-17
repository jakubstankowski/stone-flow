# Spec review — SF-21

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa wszystkie kryteria i punkty zakresu zgłoszenia, jest zgodna z PRD §4, §10, §11, D-08 i z faktycznym stanem kodu (sprawdzono m.in. `LaunchDateStartupTests`, `EventId 6051`, `sf_session`, limity logowania, kontrakty raportów, `grossTotal`, `costDateFrom`, `businessFrom`, `check-fresh-commit.sh`, `ci.yml`, `contexts.sh`). Kryteria hostingowe zostają świadomie otwarte dla właściciela z checklistą dowodów, co jest zaakceptowanym podziałem. Nie ma naruszeń invariantów ani konfliktów z decyzjami. Kilka miejsc w projekcie E2E i skanie pakietu wymaga doprecyzowania w planie; najważniejsze to unieważnienie wspólnej sesji `storageState` przez test „Wyloguj”.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **F pkt 26–27.1 — test wylogowania unieważnia wspólną sesję E2E.** „Wyloguj” zmienia znacznik bezpieczeństwa konta (`LogoutHandler` → `RotateSecurityStampAsync`) i kończy wszystkie sesje jedynego konta, także tę zapisaną w `storageState` przez global setup. Przy `workers: 1` każdy test uruchomiony po teście 27.1 dostanie 401 i trafi na `/logowanie`. To stoi w sprzeczności z „każdy test … nie zależy od innych testów” oraz z regułą z Edge Cases, że logowanie w UI odbywa się tylko w teście logowania i w global setup. Plan musi wybrać rozwiązanie, np. ponowne zapisanie `storageState` na końcu testu 27.1 (jedno logowanie mieści się w limicie 10/min), osobny projekt Playwright uruchamiany jako ostatni albo osobne konto. Ostatnia opcja odpada, bo w MVP jest jedno konto.
2. **F pkt 27.5–27.6 — izolacja danych testów na wspólnej bazie.** Wszystkie testy E2E piszą do tej samej bazy `StoneFlowE2E` i tego samego konta, a daty testów są „w środku miesiąca”. Test 27.5 („własny zakres obejmujący tylko jego daty”) i 27.6 (wartość sprzedaży poprzedniego miesiąca) nie określają, jak uniknąć kolizji dat z danymi innych testów (27.2–27.4, 28). Trzeba albo przydzielić każdemu testowi rozłączne dni, albo sprawdzać różnicę wartości przed i po, a nie wartość bezwzględną.
3. **F pkt 28 — wystąpienie cykliczne bieżącego okresu.** Według SF-16 przy zapisie definicji powstają wystąpienia „za wszystkie terminy od początku do dzisiaj, włącznie z dzisiejszym”. Wystąpienie bieżącego miesiąca istnieje więc tylko wtedy, gdy dzień terminu ≤ dzisiaj. Specyfikacja nie mówi, jaki dzień wybiera test; przy dniu późniejszym niż data przebiegu test nie zobaczy wystąpienia. Plan powinien ustalić dzień terminu = 1 (albo dzień przebiegu).
4. **I pkt 39 — wielkość liter i fałszywe trafienia reguł skanu.** Nie określono, czy reguły (`Uid=`, `Password=`, `User Id=`) rozróżniają wielkość liter. Zminifikowany JavaScript zawiera często fragmenty typu `uid=`, więc reguła bez rozróżniania wielkości liter da fałszywe trafienie. Edge case mówi tylko, że test sprawdza przejście bieżącego buildu; trzeba to ustalić wprost.
5. **C pkt 14 — powrót backendu, gdy problematyczny commit zawiera migrację.** Procedura zabrania revertu zmieniającego zastosowane migracje i wtedy każe robić fix forward. To prawdopodobnie najczęstszy przypadek awarii po wdrożeniu, a dla niego procedura nie opisuje częściowego revertu (cofnięcie kodu z zachowaniem plików migracji i snapshotu), który przy expand/contract byłby możliwy. Brak tej ścieżki nie łamie PRD, ale osłabia kryterium „procedura powrotu do działającej wersji”.
6. **UI — mapowanie design guide §10 niepełne.** Podział dowodów w sekcji UI nie wymienia trzech pozycji §10: „obowiązkowe cztery kolory zapisane centralnie” (`lint:colors`), „układ nawiązuje do referencji” i „widoki sprzedaży i przepływów wyraźnie różnią etykiety” (częściowo E2E 27.5). Ponieważ `odbior-mvp.md` ma objąć odbiór UI, każda pozycja §10 powinna mieć wskazany rodzaj dowodu.
7. **F pkt 29 — brak trasy `/reset-hasla/nowe`** (formularz nowego hasła, `app.routes.ts`) na liście stron axe i reflow, choć global setup i tak przez nią przechodzi.
8. **Verification — `npm run typecheck (obejmuje app/e2e/)`.** Dziś `typecheck` sprawdza tylko `tsconfig.app.json` i `tsconfig.spec.json`. Rozszerzenie skryptu albo dodanie `tsconfig` dla `e2e` jest zmianą, której brakuje w Scope pkt 3; podobnie dopisanie `app/test-results/` i `app/playwright-report/` do `.gitignore`, które jest tylko wspomniane w pkt 32.
9. **E pkt 22 — link do przebiegu CI z PR SF-21 w checkliście.** Link powstaje dopiero po uruchomieniu CI na PR, więc `odbior-mvp.md` wymaga dodatkowego commita po pierwszym zielonym przebiegu (który uruchomi CI ponownie). Warto to zapisać w planie, żeby kryterium „wiersze automatyczne mają link do przebiegu CI” było wykonalne.

### Unclear or Ambiguous Sections

- **H. Scenariusze finansowe — pkt 38:** „wpisy obu zmian z polem kwoty, starą i nową wartością” — dla dodanego wydatku (utworzenie) stara wartość jest pusta. Trzeba doprecyzować oczekiwanie dla wpisu utworzenia.
- **B. Skrypt — pkt 9.6:** „tabele główne” są wymienione opisowo, bez nazw schematów i tabel; plan musi je ustalić z modelu EF (nie jest to błąd, ale wymaga decyzji).
- **F. Testy E2E — pkt 26:** „Hasło istnieje tylko w pamięci przebiegu i w katalogu wyników testów” — niejasne, czy hasło jest zapisywane do pliku w `test-results`, który w CI trafia do artefaktu przy błędzie. Hasło jest jednorazowe, a baza efemeryczna, więc nie łamie to invariantu 6, ale lepiej trzymać w artefakcie tylko `storageState`, a nie hasło.
- **Scope pkt 7 / Assumptions 10:** zakres poprawek dostępności jest nieznany do pierwszego przebiegu axe. Kryterium podziału („nie zmieniają funkcji” kontra „wymaga przeprojektowania”) jest ocenne; plan powinien przewidzieć punkt kontrolny po pierwszym przebiegu.

### Invented or Unsupported Requirements

None. Dodatki ponad tekst zgłoszenia (skan `check-bundle-secrets.mjs`, reguła gitleaks dla kluczy WooCommerce, test reflow 375 px, wydzielenie `sql-common.sh`) wynikają z kryteriów zgłoszenia, PRD §10–§11 i design guide §9–§10 oraz są opisane jako decyzje wykonawcy (Assumptions 3, 5), zaakceptowane przez właściciela jako tryb autonomiczny.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Context / Assumptions 14 | `.sdd/context/conventions.md` (tabela układu), artefakty SF-4 | Dokumenty podają `docs/wdrozenia.md`, a plik leży w katalogu głównym. Specyfikacja wykrywa rozbieżność i poprawia ścieżkę w `conventions.md`, bez przenoszenia pliku. Brak konfliktu z decyzją. |
| A. Procedura kopii pkt 2 / Assumptions 13 | SF-1 `01-weryfikacja.md` kontra D-08 (PRD §12) | Artykuł dostawcy: kopia co drugi dzień, CustomBackup z tygodniową retencją; D-08: codziennie, 30 dni. Specyfikacja nie rozstrzyga sama, tylko przekazuje wybór właścicielowi (pytanie otwarte 1). Poprawne. |
| Całość | `rules.md` — „nie implementować obszaru zablokowanego przez D-08” | D-08 jest częściowo rozstrzygnięte (harmonogram, retencja, RPO/RTO zaakceptowane); otwarta pozostaje m.in. próba odtworzenia, którą to zadanie przygotowuje. Specyfikacja nie ustala ostatecznej konfiguracji środowisk, więc nie narusza reguły. |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Bez zmian reguł; testy H sprawdzają rozdzielenie w każdym scenariuszu (pkt 36.1, 36.2, 36.11). |
| 2. Pieniądze jako decimal na backendzie | OK | Bez zmian obliczeń; porównania na `decimal` i ciągach kwot z API. |
| 3. Dostęp wymuszany przez API | OK | Bez nowych endpointów i wyjątków; E2E używa tylko istniejących anonimowych endpointów resetu hasła. |
| 4. Ponowienia nie duplikują | OK | Scenariusz 36.12 (ponowne uruchomienie zadania), E2E 27.2 (podwójne kliknięcie), skrypt z unikalną bazą roboczą. |
| 5. Ślad audytu finansów | OK | Bez zmian zapisu audytu; procedura odtworzenia każe odnotować cofnięcie historii do chwili kopii. |
| 6. Sekrety po stronie serwera | OK | Data i sekrety tylko w konfiguracji serwera; dodatkowy skan pakietu; skrypt nie wypisuje connection stringu; E2E bez sekretów `production`. Patrz uwaga o haśle w artefakcie (Unclear, F pkt 26). |
| 7. Granice modułów i warstw | OK | Bez zmian kodu modułów; testy H tylko przez HTTP. |
| 8. Brak danych identyfikujących w dokumentach | OK | Placeholdery, dane fikcyjne (`owner@example.com`); kopie produkcyjne nie trafiają do repozytorium, CI ani artefaktów. |

## Risk

- Recorded: high
- Assessed: high — zadanie dopuszcza produkcyjne uruchomienie systemu finansowego, obejmuje odtwarzanie bazy z danymi klientów (operacja nieodwracalna przy pomyłce bazy docelowej), procedurę powrotu przy nieodwracalnych migracjach i końcowe uzgodnienie obu widoków finansowych. Zgodne z zapisem; wymagany przegląd człowieka przed merge.

## Assumptions Detected

| Założenie | Czy zapisane w specyfikacji |
|---|---|
| Kryteria hostingowe zostają otwarte dla właściciela; zadanie może przejść do `done` z otwartymi pozycjami | Tak (Assumptions 1) |
| Brak daty uruchomienia zatrzymuje start w Production już dziś, bez zmian kodu | Tak (Assumptions 2; zgodne z kodem) |
| E2E na prawdziwym API w Development, konto przez reset hasła z linkiem z logu | Tak (Assumptions 9; `EventId 6051` loguje treść e-maila) |
| Kopia przed migracją jest krokiem ręcznym (brak API panelu) | Tak (Assumptions 7) |
| Niezależna kopia nie przechodzi przez CI | Tak (Assumptions 8) |
| Panel hostingu udostępnia plik `.bak` (ewentualnie w archiwum) do pobrania | Częściowo (Edge Cases: archiwum `.zip`); możliwość pobrania nie jest zapisana jako założenie |
| Wersja SQL Server na hostingu pozwala odtworzyć kopię w kontenerze `2022-CU27` | Częściowo (Edge Cases) |
| Właściciel potrafi lokalnie uruchomić bash, Docker, .NET SDK i zbudowane rozwiązanie, aby wykonać `verify-restore.sh` | Nie — założenie ukryte w A pkt 5 |
| Wspólna sesja `storageState` przetrwa wszystkie testy | Nie — założenie ukryte i błędne (Non-Critical 1) |
| Dzień terminu wydatku cyklicznego w E2E przypada najpóźniej dzisiaj | Nie — założenie ukryte (Non-Critical 3) |
| Dane testów E2E nie nakładają się datami | Nie — założenie ukryte (Non-Critical 2) |
| `README.md` ma niezatwierdzone zmiany innej osoby | Tak (Assumptions 15) |
| Wersje Playwright i axe zgodne z Node 24 | Tak (Assumptions 16; do sprawdzenia w planie) |

## Recommendation

- Proceed as-is — `sdd-plan` może ruszyć. Plan powinien rozstrzygnąć Non-Critical Issues 1–4 (sesja po teście wylogowania, izolacja dat E2E, dzień terminu w teście cyklicznym, wielkość liter w regułach skanu) i uzupełnić drobne braki (5–9). Nie wymaga to zmiany zakresu ani nowych decyzji właściciela.
