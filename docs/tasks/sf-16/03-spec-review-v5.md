# Spec review — SF-16

## Summary

- Overall assessment: FAIL
- Justification: Wersja 5 zastępuje granicę `OccurrencesFrom` historią dni i przedziałami wyłączeń i tym rozwiązuje wszystkie Critical z v2–v4. Marzec przed początkiem, sierpień po wznowieniu, v3-Critical1 (20.11, dzień 10 → 25), v3-Critical2 (czerwiec przy końcu 25.06, także po przedłużeniu) i v4-Critical („najpierw dzień, potem przedłużenie”) dają teraz wynik zgodny z odpowiedziami 1–3. Non-Critical z v4 też są domknięte, a niezmienniki 1–8 są zachowane. Model ma jednak jedną nową lukę. Zmiana dnia na wstrzymanej definicji w miesiącu wstrzymania, na dzień wcześniejszy niż dzień wstrzymania, daje okres, który kwalifikuje się według reguły 4. Edycja tworzy wtedy koszt w czasie wstrzymania, a podgląd, `nextDueDate` i szczegóły obiecują, że wstrzymana definicja nie tworzy niczego. Spec przeczy sama sobie co do tego, czy koszt powstaje, a jedna i druga odpowiedź łamie inny jej zapis albo PRD („Wstrzymanie zatrzymuje nowe wystąpienia”).

## Findings

### Critical Issues

1. **Zmiana dnia na wstrzymanej definicji może utworzyć wystąpienie w czasie wstrzymania, choć podgląd i reguły pochodne zakładają, że wstrzymana definicja nie tworzy niczego (Behavior → „Model terminów”, reguły 4 i 6; „Szczegóły, edycja i usunięcie definicji” pkt 1 i 3, krok 7; Data / API → `RecurringExpensePreview`; Acceptance Criteria, punkt o wstrzymaniu; Verification → „Liczba w podglądzie równa się liczbie utworzonych”).**
   - Przykład: dzień 10, wstrzymanie 05.11. Wstrzymanie najpierw generuje terminy ≤ 05.11, a 10.11 jeszcze nie minął, więc listopad nie powstaje. 20.11 definicja jest nadal wstrzymana, a użytkownik zmienia dzień na 3. Reguła 2 daje wpis od listopada z dniem 3, więc t(listopad) = 03.11. Termin 03.11 < `PausedOn` = 05.11, więc nie leży w `[PausedOn, ∞)`. Nie ma go w rejestrze i jest w zakresie, więc według reguły 4 listopad jest należny. Edycja w kroku 7 woła `OccurrenceGenerator` „z nowym stanem” i nie ma warunku na wstrzymanie, więc listopad powstaje 03.11, w czasie wstrzymania.
   - Te same miejsca spec mówią co innego:
     - `RecurringExpensePreview.occurrencesToCreate` ma „0 przy definicji wstrzymanej”;
     - reguła 6 mówi, że `nextDueDate` wstrzymanej definicji nie ma, „bo `[PausedOn, ∞)` wyklucza wszystko od dnia wstrzymania”, a tu wyklucza tylko terminy ≥ 05.11;
     - szczegóły pokazują „Brak — wydatek cykliczny jest wstrzymany”, a według kolejności z pkt 1 pierwsza reguła („Termin … już minął … utworzy je zadanie automatyczne”) wygrywa, choć zadanie pomija definicje wstrzymane.
   - Każde z dwóch rozstrzygnięć łamie coś innego. Jeśli edycja tworzy listopad, łamie PRD §5 („Wstrzymanie zatrzymuje nowe wystąpienia”), kryterium zgłoszenia („Wstrzymanie blokuje kolejne”) i zgodność podglądu z zapisem (podgląd 0, zapis 1). Jeśli nie tworzy, okres i tak powstanie przy wznowieniu, bo przedział `[05.11, dzień wznowienia)` nie obejmuje 03.11. Wtedy `resumePreview` musi to pokazać, a tekst PRD §5 (fragment 2) i odpowiedź 3 trzeba zestawić z tym przypadkiem. Spec nie ma reguły, Edge Case ani testu dla edycji wstrzymanej definicji.
   - Przypadek jest wąski: to jedyna droga do terminu < `PausedOn` bez wystąpienia, bo wstrzymanie najpierw tworzy wszystkie należne, a przesunięcie początku na wcześniej jest przykryte przedziałem `startMoved`. Rozstrzygnięcie zmienia jednak to, które koszty istnieją, więc należy do specyfikacji, a nie do planu. Możliwe kierunki dla autora (bez przesądzania):
     - przy wstrzymaniu wyłączać cały bieżący okres, a nie tylko terminy od `PausedOn`;
     - zablokować zmianę dnia na wstrzymanej definicji, tak jak wstrzymanie zakończonej (Assumptions 5);
     - jawnie dopuścić utworzenie (odpowiedź 1 dla terminu sprzed wstrzymania) i poprawić podgląd, regułę 6 i szczegóły;
     - zapytać właściciela.

### Non-Critical Issues

1. **Tekst PRD §5, fragment 1, w jednym akapicie mówi „nowy dzień obowiązuje od następnego okresu” i „Nowy dzień obowiązuje od bieżącego okresu” (Behavior → „PRD — aktualizacja §5”).** Pierwsze zdanie to cytat odpowiedzi 1 dla okresu, który ma już wystąpienie, a drugie to reguła wynikająca. W kontekście się nie wykluczają, ale czytelnik PRD dostaje dwa sprzeczne brzmienia. Warto przeformułować np. „…okres, który ma już wystąpienie, nie dostaje drugiego, więc nowy dzień zadziała w nim dopiero od następnego okresu”.
2. **Fragment 2 tekstu PRD („gdy termin bieżącego okresu minął w czasie wstrzymania, wystąpienia za ten okres nie ma”) jest szerszy niż model (Edge Cases → „Zmiana dnia po wznowieniu w bieżącym okresie”).** Spec świadomie tworzy listopad 18.11 po wznowieniu 15.11 i późniejszej zmianie dnia z 10 na 18. Przegląd v3 uznał to za zgodne z odpowiedzią 1, bo odpowiedź 3 dotyczy chwili wznowienia. Tekst PRD powinien to zawęzić („przy wznowieniu…”), inaczej PRD i Edge Case sobie przeczą.
3. **Walidacja „co najmniej jeden termin w zakresie według historii dni po zmianie” może odrzucić zmianę dnia, gdy jedyny okres w zakresie ma już wystąpienie (Data / API → „Walidacja”, wiersz „harmonogram”).** Przykład: początek 05.11, koniec 25.11, dzień 10, listopad utworzony 10.11. 20.11 zmiana na 28 daje t(listopad) = 28.11 > koniec, więc `400 „W tym okresie nie wypada żaden termin.”`, choć definicja ma wystąpienie. Jest to spójne z `CK_..._LastDueDate`, ale komunikat myli. Warto dopisać Edge Case z decyzją: zostaje `400`, czy walidacja uwzględnia rejestr (wtedy trzeba ustalić `LastDueDate`).
4. **„Dzień okresów wcześniejszych się nie zmienia” nie zachodzi dosłownie dla okresów przed pierwszym wpisem (Behavior → reguła 2).** Początek 15.03.2027 (wpis od marca 2027, dzień 10). 20.11.2026 początek przesunięty na 01.09.2026, a potem zmiana dnia na 25 usuwa wpis z marca 2027. Wrzesień i październik dostają wtedy z reguły zastępczej dzień 25 zamiast 10. Bez skutku dla kosztów, bo oba terminy leżą w przedziale `startMoved` [01.09, 20.11), a `LastDueDate` przy końcu też liczy się od nowa. Warto doprecyzować zdanie i dodać ten wariant do testu „okres przed pierwszym wpisem”.
5. **Brakujące przypadki brzegowe i testy dla przedziałów (Edge Cases; Verification → `StoneFlow.Expenses.Tests`):**
   - kilka wstrzymań i potem przedłużenie;
   - nakładające się przedziały `pause` i `startMoved`;
   - przesunięcie początku na później, a potem z powrotem na wcześniej, z okresem należnym i nieutworzonym (np. przez błąd zadania). Okres odpada przy pierwszej zmianie (`droppedDueCount`) i nie wraca przy drugiej, bo trafia do `[nowy początek, min(stary początek, dzisiaj))`. To zgodne z PRD, ale warto to zapisać;
   - `droppedDueCount` przy przesunięciu początku na później, a nie tylko przy zmianie dnia.
6. **Zapis `Reason` jako `pause`/`startMoved` (Data / API → `RecurringExpenseExclusions`).** Pozostałe słowniki w tabelach (`Category`, `PaymentMethod`) są zapisywane jako „nazwa wartości” enumu. Konwencję zapisu i `CHECK` warto ujednolicić albo uzasadnić. Plan może to domknąć.
7. **Faza odczytu zadania wczytuje cały rejestr kandydatów (Behavior → „Zadanie harmonogramu” pkt 2.1; Storage → `IX_..._Generation`).** Rejestr rośnie o 12 wierszy rocznie na definicję. Przy skali z D-08 (jeden użytkownik) to bez znaczenia, a test wydajności (500 definicji, < 60 s) to pokrywa. Indeks `(PausedOn, EndsOn)` nie obejmuje `StartsOn` z filtra kandydatów. Plan może dobrać indeks albo uznać go za zbędny.

### Unclear or Ambiguous Sections

- **Behavior → reguła 6, „Brak wartości … Dotyczy to też definicji wstrzymanej”:** uzasadnienie „`[PausedOn, ∞)` wyklucza wszystko” jest fałszywe po zmianie dnia w miesiącu wstrzymania (Critical 1).
- **Behavior → „Szczegóły, edycja i usunięcie definicji” pkt 1, kolejność „Najbliższego terminu”:** przy definicji wstrzymanej z terminem ≤ dzisiaj pierwsza reguła obiecuje utworzenie przez zadanie, które pomija definicje wstrzymane (Critical 1).
- **Behavior → „Szczegóły, edycja i usunięcie definicji” pkt 3:** nie wiadomo, czy edycja wstrzymanej definicji generuje wystąpienia (Critical 1).
- **Behavior → „PRD — aktualizacja §5”, fragmenty 1 i 2:** dwa brzmienia „od którego okresu” i zbyt szerokie „wystąpienia za ten okres nie ma” (Non-Critical 1 i 2).
- **Behavior → reguła 2, „Dzień okresów wcześniejszych się nie zmienia”:** wyjątek dla okresów przed pierwszym wpisem (Non-Critical 4).

### Invented or Unsupported Requirements

None. Historia dni i przedziały wyłączeń to mechanizm realizacji odpowiedzi 1–3 i zasad PRD §5, a nie nowa funkcja. Spec jawnie wyłącza je z „historii wersji definicji” (Out of scope). Ograniczenia spoza PRD są jawne i uzasadnione, jak w v3–v4: zakres 2000–2099, odcięcie 400 dni, próg 25 h, odrzucenie wstrzymania definicji zakończonej, `recurring_expense_deleted`, `503 recurring_expense_busy`, `ToDate = min(stary początek, dzisiaj)` (Assumptions 5, 12, 13, 14, 16, 20, 23).

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → reguła 4; „Szczegóły, edycja…” pkt 3, krok 7 | PRD §5 („Wstrzymanie zatrzymuje nowe wystąpienia”); zgłoszenie, AC „Wstrzymanie blokuje kolejne” | Edycja wstrzymanej definicji ze zmianą dnia na termin < `PausedOn` tworzy wystąpienie w czasie wstrzymania (Critical 1) |
| Behavior → reguła 4 i krok 7 edycji | Data / API → `RecurringExpensePreview` („0 przy definicji wstrzymanej”); reguła 6; Verification („liczba w podglądzie = liczba utworzonych”) | Podgląd i `nextDueDate` mówią 0 / brak, a generowanie tworzy 1 (Critical 1) |
| Behavior → „PRD — aktualizacja §5”, fragment 2 | Edge Cases → „Zmiana dnia po wznowieniu w bieżącym okresie” | Proponowany tekst PRD wyklucza okres bieżący, który model tworzy po późniejszej zmianie dnia (Non-Critical 2) |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Wystąpienie z `PaidOn = null`, stan „Planowany”. Żadna ścieżka SF-16 nie ustawia zapłaty, jest na to test |
| 2. Pieniądze jako decimal w backendzie | OK | `decimal(18,2)`, kwoty jako stringi parsowane pomocnikami SF-15 bez zaokrągleń. Terminy, liczby, stan i podgląd liczy backend |
| 3. Dostęp egzekwowany w API | OK | Wszystkie trasy, także `preview` i `GET /api/jobs`, wymagają sesji, a zapisy XSRF. `EndpointSecurityTests` bez wpisów anonimowych |
| 4. Ponowienia nie duplikują | OK | `PK_RecurringExpensePeriods (DefinitionId, Period)` w transakcji wystąpienia, rejestr przeżywa usunięcie wydatku, blokada `UPDLOCK, ROWLOCK`, ponowienie tylko w zadaniu w nowym zakresie DI, `RecurringDefinitionRequests` zostaje po usunięciu. Kwalifikacja jest funkcją stanu, więc ponowiony zapis daje ten sam wynik. Critical 1 dotyczy znaczenia wstrzymania, a nie duplikatów |
| 5. Audyt finansowy | OK | Definicja `Created`/`Updated`/`Deleted` z autorem konta, wystąpienie z `AuditAuthor.AutomatedJob` (jest w `IAuditLog.cs`) i rodzicem `RecurringExpenseDefinition` (`AuditVocabulary.cs`). `ExpenseAudit.Created/Updated/Deleted` mają już parametr `parent`. Wszystko w jednej `IUnitOfWork`. Dni i wyłączenia wynikają z audytowanych `dayOfMonth`, `startsOn` i `pausedOn` |
| 6. Sekrety po stronie serwera | OK | Wyjątek zadania i logi tylko z liczbami i id. Komponent nie pokazuje `error` z `JobStatusDto` |
| 7. Granice modułów i warstw | OK | Wszystko w module Expenses i schemacie `expenses`. Pętla, zakresy DI, blokada SQL i rozpoznanie `1205` są w Infrastructure, `RecurringSchedule` to czysta domena. Application bez `IServiceScopeFactory` (test architektury) |
| 8. Brak danych identyfikujących w dokumentach | OK | Przykłady fikcyjne |

## Risk

- Recorded: high
- Assessed: high. Zadanie bez udziału użytkownika zapisuje koszty trafiające do raportów, usunięcie definicji jest nieodwracalne, a nowy model terminów (historia dni, przedziały wyłączeń) decyduje, które koszty minionych miesięcy powstają (Critical 1). Przed merge wymagany jest przegląd przez człowieka.

## Assumptions Detected

- Odpowiedzi właściciela 1–3: zapisane (Assumptions 1–3). Odpowiedź 1 jest zrealizowana przez rejestr i wpis dnia od bieżącego okresu. Odpowiedź 2 przez to, że zmiana końca nie tworzy przedziałów, a przedziały `pause` odcinają okresy wstrzymania, niezależnie od kolejności względem zmiany dnia (v4-Critical rozwiązane). Odpowiedź 3 przez przedział `[PausedOn, dzień wznowienia)`. **Niezapisane:** edycja wstrzymanej definicji (Critical 1).
- Model ze stanu zamiast kursora i granicy: zapisane (4). Twierdzenie o niezależności od kolejności sprawdziłem dla par: dzień i przedłużenie, dzień i początek na wcześniej, dzień i początek na później, wstrzymanie i zmiana dnia. Zachodzi z wyjątkiem v4-NC1, opisanym i zaakceptowanym (Assumptions 11). Wynik zależy od dnia wykonania zmiany (bieżący okres, `dzisiaj` w `startMoved`), co jest zamierzone.
- Wstrzymanie a przerwa, wstrzymanie najpierw tworzy należne, odrzucenie wstrzymania zakończonej: zapisane (5). Przy zmianie dnia w czasie wstrzymania założenie „wstrzymana nie tworzy niczego” jest ukryte i nieprawdziwe (Critical 1).
- Tworzenie przy zapisie bez limitu, wsady po 100: zapisane (6).
- Wystąpienia sprzed uruchomienia, `CostDate = DueDate`, autor „Zadanie automatyczne”, fizyczne usunięcie definicji z kaskadą dni, wyłączeń i rejestru: zapisane (7–10).
- Zmiana dnia od bieżącego okresu, różnica v4-NC1 akceptowana, v4-NC2 widoczne w podglądzie: zapisane (11).
- `startMoved` z `ToDate = min(stary początek, dzisiaj)`: zapisane (12). Przesunięcie na później i potem na wcześniej jest zgodne z PRD, ale nie ma Edge Case (Non-Critical 5).
- Blokada, ponowienie tylko w zadaniu, `1205` przez fallback: zapisane (13). Zgodne z `UnitOfWork.ExecuteInTransactionAsync`: `RollbackAsync` tylko przy `commitWhen = false`, a na ścieżce wyjątku `EndTransaction(false)` i `await using`.
- Zadanie 00:10, 30 min, odcięcie 400 dni: zapisane (14).
- Przechowywane `LastDueDate` i `DayOfMonth`, przeliczane przy zmianie początku, końca lub dni, z testem zgodności z domeną i filtrem `state`: zapisane (15). Filtr SQL (`LastDueDate ≤ dzisiaj` przed `PausedOn IS NOT NULL`) odpowiada regule 7.
- Reguła zastępcza „przed pierwszym wpisem obowiązuje dzień pierwszego wpisu”: zapisana w regule 2. Wyjątek od „dzień wcześniejszych okresów się nie zmienia” po usunięciu późniejszych wpisów nie jest zapisany (Non-Critical 4).
- Walidacja, UI, podgląd `GET`, filtr z parametru, idempotencja dodania: zapisane (16–20).
- E2E w SF-21, ryzyko, ostrzeżenie o zadaniu, aktualizacja PRD, `nextDueDate`, skrócenie końca, kolizja rejestru, dialog „Wznów”, zwracana `version`: zapisane (21–29).
- Migracja tylko dodaje, nowe kolumny `Expenses` nullowalne: zapisane w Storage. Zgodne ze strategią expand.

## Recommendation

- Revise specification
