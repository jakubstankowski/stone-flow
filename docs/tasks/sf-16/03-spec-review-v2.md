# Spec review — SF-16

## Summary

- Overall assessment: FAIL
- Justification: Wersja 2 rozwiązuje oba problemy krytyczne z v1 i wszystkie dziesięć niekrytycznych. Zmiana dnia nie odcina już zaległych okresów z przerwy działania, łączny zapis daje te same okresy co dwa osobne, a ostrzeżenie o niepowodzeniu zadania jest w zakresie i ma czystą funkcję stanu oraz testy. Rejestr `(DefinitionId, Period)`, blokada `UPDLOCK`, jednostka pracy na definicję i audyt z rodzicem spełniają niezmienniki 1–8. Nowa reguła „zmiana dnia nie rusza granicy” ma jednak skutek uboczny sprzeczny z PRD §5 i odpowiedzią właściciela 3. Zmiana dnia tworzy wtedy wystąpienia za miniony okres, który przy dotychczasowym dniu nigdy nie był należny: okres sprzed początku albo okres bieżący z chwili wznowienia, pominięty zgodnie z odpowiedzią 3. Poprawka jest wąska, ale zmienia koszty minionych miesięcy, więc wymaga zmiany reguły albo decyzji właściciela.

## Findings

### Critical Issues

1. **Zmiana dnia tworzy minione wystąpienia za okresy, które przy starym dniu nie były należne (Behavior → „Pojęcia i reguły domeny”, reguły 4, 5 i 7; Assumptions 11; Edge Cases → „Zmiana dnia wciąga wcześniejszy okres w zakres”; Behavior → „PRD — aktualizacja §5”).**
   Reguła 5 liczy termin z **bieżącego** dnia dla wszystkich okresów od `OccurrencesFrom`, a zmiana dnia granicy nie przesuwa. Okres, którego nie było w rejestrze, bo przy starym dniu jego termin wypadał przed początkiem albo przed granicą, staje się po zmianie dnia należny i powstaje od razu. Spec sama podaje taki przypadek: początek 15.03, dzień 10 → 20, dzisiaj 20.11. Wystąpienie za marzec powstaje w listopadzie, czyli koszt trafia do okresu sprzed ośmiu miesięcy.
   - Drugi przypadek wynika z tych samych reguł, ale spec go nie opisuje. Dzień 10, wstrzymanie 01.08, wznowienie 15.08 (granica 15.08), więc sierpień jest pusty zgodnie z odpowiedzią 3 („brak wystąpienia za bieżący okres …, pierwsze w następnym okresie”). Po zmianie dnia na 20 w listopadzie sierpień ma termin 20.08 ≥ 15.08 i nie ma go w rejestrze. Wystąpienie za sierpień powstaje więc mimo odpowiedzi 3.
   - PRD §5 mówi: „poza zapisem nowej definicji tworzenie zaległych wystąpień dotyczy wyłącznie przerw w działaniu systemu” oraz „Zmiana początku istniejącej definicji nie tworzy minionych wystąpień”. Odpowiedzi 1–3 dodają wyjątki tylko dla okresu **bieżącego** przy zmianie dnia (odpowiedź 1) i dla okresów między dawnym końcem a dzisiaj przy przedłużeniu (odpowiedź 2). Zaległe z przerwy działania pokrywa PRD („w tym zaległych”). Okres wciągnięty w zakres zmianą dnia nie mieści się w żadnym z tych przypadków. W skutku to rozszerzenie zakresu wstecz, którego PRD zabrania przy zmianie początku.
   - Assumptions 11 uznaje ten skutek za dopuszczalny („Podgląd pokazuje to przed zapisem”). To decyzja autora, a nie wynik odpowiedzi właściciela. Proponowany tekst PRD („zmiana dnia miesiąca obowiązuje od pierwszego okresu bez wystąpienia”) jest podpisany jako „decyzja użytkownika z 08.11.2026”, a jednocześnie spec zostawia zdanie o „wyłącznie przerwach” bez zmian. PRD byłoby więc wewnętrznie sprzeczne.
   - Potrzebna jest poprawka reguły albo wyraźna decyzja właściciela zapisana w Assumptions. Przykładowy kierunek dla autora: zmiana dnia nie tworzy wystąpień za okresy sprzed bieżącego, które przy dotychczasowym dniu nie były należne (termin przed początkiem albo przed granicą). Zaległe z przerwy i z przedłużenia oraz okres bieżący z odpowiedzi 1 nadal powstają. Trzeba też dodać przypadek sierpnia do Edge Cases i testów.

### Non-Critical Issues

1. **Ponowienie po `DuplicateRequest` i `Busy`: zakres DI (Behavior → „Tworzenie wystąpień” pkt 7–8; „Zadanie harmonogramu” pkt 2.2).** Spec mówi „Komenda zadania powtarza jednostkę definicji” i „handler użytkownika powtarza cały przypadek użycia”. Ponowienie odbywa się więc w tym samym zakresie DI. Konwencje („Data access”) mówią: „A background job or sync creates a new DI scope per unit of work; after a rolled-back unit of work the scope does no further writes.” `ModuleConnection.EndTransaction` czyści po wycofaniu `ChangeTracker` dołączonych kontekstów, więc technicznie to zadziała. Kontekst, który tylko czytał, nie jest jednak dołączony i nie jest czyszczony. Spec powinna wskazać, gdzie żyje ponowienie zadania (w `RecurringExpensesJob`, w nowym zakresie, zgodnie z konwencją, albo w handlerze z uzasadnionym wyjątkiem od konwencji). Przy dodaniu definicji kolizja rejestru jest w ogóle niemożliwa, bo niezatwierdzonej definicji nikt inny nie widzi. Ta ścieżka ponowienia to martwy kod.
2. **Zakleszczenie `1205` i wycofanie (Behavior → „Tworzenie wystąpień” pkt 8; Data / API → „Tłumaczenia błędów”).** Przy zakleszczeniu SQL Server sam wycofuje transakcję. Potem `UnitOfWork` wywołuje `RollbackAsync` na transakcji, która już się zakończyła, a `SqlTransaction` może wtedy rzucić `InvalidOperationException`. Wynik `Busy` nie dotarłby wtedy do wywołującego. Test przez „atrapę portu zgłaszającą błąd zakleszczenia” nie sprawdza tej ścieżki. Warto opisać to zachowanie (albo zostawić je do weryfikacji w planie) i dodać test z prawdziwą transakcją, np. przez `RAISERROR`/`THROW` z kodem 1205 albo wymuszone zakleszczenie.
3. **`CK_RecurringExpenseDefinitions_LastDueDate` nie egzekwuje spójności z `EndsOn` (Data / API → Storage).** `CHECK` przepuszcza wynik `UNKNOWN`. Dlatego `EndsOn` podane przy `LastDueDate IS NULL` oraz `EndsOn IS NULL` przy podanym `LastDueDate` oba przechodzą, bo drugi człon porównuje z `NULL`. Reguła stanu i filtr `state` opierają się na tej kolumnie. Trzeba jawnie zapisać oba przypadki (`EndsOn IS NOT NULL AND LastDueDate IS NOT NULL AND …`). Test `CHECK` powinien obejmować oba niespójne warianty.
4. **„Zakończony” przy nieutworzonym ostatnim terminie (reguły 6 i 8; „Szczegóły, edycja i usunięcie definicji” pkt 1).** Przykład: `LastDueDate = dzisiaj`, a wystąpienie jeszcze nie powstało (przed 00:10 albo przy błędzie zadania). Wtedy stan to „Zakończony”, a reguła 6 daje dwie sprzeczne wartości `nextDueDate`: „najwcześniejszy nieutworzony, także już należny” albo „po ostatnim terminie brak wartości”. Szczegóły mogą pokazać „Brak” albo „Termin … już minął … utworzy je zadanie automatyczne”. Trzeba ustalić pierwszeństwo.
5. **Kolejność blokady przy wstrzymaniu (Behavior → „Tworzenie wystąpień” pkt 1 vs „Wstrzymanie i wznowienie” pkt 1).** Zdanie „Handlery zapisu wołają generowanie po własnym `UPDATE` definicji, więc mają już jej blokadę” nie zgadza się ze wstrzymaniem, w którym generowanie idzie **przed** zmianą stanu i zapisem z wersją. Model jest poprawny, bo generator i tak bierze `UPDLOCK`, a późniejszy `UPDATE` z wersją wykrywa konflikt. Zdanie trzeba jednak poprawić, żeby plan nie pominął blokady.
6. **Treść dialogu „Wznów” (Behavior → „Wstrzymanie i wznowienie” pkt 2).** Warunek „gdy termin bieżącego okresu przypada dzisiaj” nie uwzględnia rejestru. Przy wstrzymaniu w dniu terminu po utworzeniu wystąpienia i wznowieniu tego samego dnia dialog obieca wystąpienie, które nie powstanie. Tekst powinien wynikać z `resumePreview.occurrencesToCreate`.
7. **Ostrzeżenie przy pierwszym przebiegu w toku (Behavior → „Ostrzeżenie o niepowodzeniu zadania” pkt 2).** Warunek „`lastSuccessStartedAtUtc` pusty, a `lastRun` istnieje” jest spełniony także wtedy, gdy pierwszy przebieg po wdrożeniu ma status `running`. Pokazuje się wtedy „nie powiodło się”, choć przebieg jeszcze trwa. Wystarczy wykluczyć `running` bez wcześniejszego przebiegu albo dopisać ten przypadek do testów `recurringJobWarning`.

### Unclear or Ambiguous Sections

- **Behavior → „Pojęcia i reguły domeny”, reguła 6:** „Przy definicji wstrzymanej i po ostatnim terminie brak wartości” a „Może być ≤ dzisiaj, gdy okres jest już należny” (Non-Critical 4).
- **Behavior → „Tworzenie wystąpień”, pkt 7:** „Komenda zadania powtarza jednostkę definicji”, a pkt 8 mówi „w nowej jednostce pracy”. Nie wiadomo, czy ponowienie to nowy zakres DI (Non-Critical 1).
- **Behavior → „Dodanie wydatku cyklicznego”, pkt 4:** kolejność rozstrzygania przy tym samym kluczu, innych danych i usuniętej definicji. Nie wiadomo, czy wynikiem jest `recurring_expense_deleted` czy `idempotency_key_reused`.
- **Assumptions 11:** „Wynik nie zależy od kolejności zapisów” jest prawdą. „Okres wcześniej poza zakresem może przy nowym dniu wejść w zakres i powstać” to jednak decyzja autora, a nie wynik odpowiedzi 1 (Critical 1).
- **Behavior → „PRD — aktualizacja §5”:** nowe zdanie („obowiązuje od pierwszego okresu bez wystąpienia”) i pozostawione zdanie „wyłącznie przerw w działaniu systemu” razem dają sprzeczny tekst PRD (Critical 1).

### Invented or Unsupported Requirements

None. Ograniczenia spoza PRD są jawnie opisane jako decyzje autora z uzasadnieniem: zakres dat 2000–2099, odcięcie kandydatów po 400 dniach, próg ostrzeżenia 25 h przejęty z INT-01, odrzucenie wstrzymania definicji zakończonej, `recurring_expense_deleted` i `503 recurring_expense_busy` (Assumptions 5, 13, 14, 16, 20, 23). Wyjątkiem jest skutek zmiany dnia z Critical 1.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → reguły 4, 5, 7; Assumptions 11; Edge Cases „Zmiana dnia wciąga wcześniejszy okres w zakres” | PRD §5 („poza zapisem nowej definicji tworzenie zaległych wystąpień dotyczy wyłącznie przerw w działaniu systemu”; „Zmiana początku istniejącej definicji nie tworzy minionych wystąpień”) | Zmiana dnia tworzy wystąpienie za miniony okres, który przy starym dniu nie był należny (marzec przy początku 15.03) |
| Te same reguły (przypadek nieopisany) | Assumptions 3 (odpowiedź właściciela z 08.11.2026) | Okres pominięty przy wznowieniu po minionym terminie powstaje po późniejszej zmianie dnia |
| Behavior → „PRD — aktualizacja §5” | PRD §5 (zdanie pozostawione bez zmian) | Nowe zdanie przypisane właścicielowi i stary tekst razem są sprzeczne |
| Behavior → „Tworzenie wystąpień” pkt 7–8 | `.sdd/context/conventions.md` → „Data access” (nowy zakres DI na jednostkę pracy, brak zapisów po wycofaniu) | Ponowienie w tym samym zakresie bez uzasadnienia wyjątku (Non-Critical 1) |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Wystąpienie z `PaidOn = null`, stan „Planowany”, żadna ścieżka SF-16 nie ustawia daty zapłaty; jest test |
| 2. Pieniądze jako decimal w backendzie | OK | `decimal(18,2)`, kwoty jako stringi, parsowanie z `ExpenseInput`; terminy, liczby i stan liczy backend, podgląd z backendu |
| 3. Dostęp egzekwowany w API | OK | Wszystkie trasy, także `preview` i `GET /api/jobs`, wymagają sesji, zapisy XSRF; `404` dla konkretnej definicji; `EndpointSecurityTests` |
| 4. Ponowienia nie duplikują | OK | `PK_RecurringExpensePeriods (DefinitionId, Period)` w transakcji wystąpienia, rejestr przeżywa usunięcie wydatku, blokada `UPDLOCK` porządkuje zadanie i użytkownika, `RecurringDefinitionRequests` zachowane po usunięciu definicji (`409 recurring_expense_deleted`); testy dwóch przebiegów, restartu, wyścigu i wstawienia duplikatu |
| 5. Audyt finansowy | OK | `Created`/`Updated`/`Deleted` definicji z autorem konta i datą = początek; wystąpienie z `AutomatedJob` i rodzicem; dalsze zmiany z rodzicem; `AuditRecord.RequireAllowedParent` dopuszcza `Expense` → `RecurringExpenseDefinition` (zweryfikowane w kodzie); `IAuditLog` wymaga transakcji jednostki pracy |
| 6. Sekrety po stronie serwera | OK | Komunikat wyjątku zadania i logi tylko z liczbami i id; ostrzeżenie nie pokazuje `error` z historii |
| 7. Granice modułów i warstw | OK | Wszystko w module Expenses i schemacie `expenses`. Pętla, zakresy DI i blokada SQL są w Infrastructure, a `IDispatcher` z Infrastructure jest dopuszczalny, bo zadanie nie jest handlerem. Domena bez EF/HTTP. Zastrzeżenie co do miejsca ponowienia: Non-Critical 1 |
| 8. Brak danych identyfikujących w dokumentach | OK | Przykłady fikcyjne („Abonament – Dostawca A”) |

## Risk

- Recorded: high
- Assessed: high. Zadanie bez udziału użytkownika zapisuje koszty trafiające do raportów, a usunięcie definicji jest nieodwracalne. Reguły okresów (granica, zmiana dnia, przedłużenie, wstrzymanie) wpływają wprost na koszty minionych miesięcy (Critical 1). Przed merge wymagany jest przegląd przez człowieka.

## Assumptions Detected

- Odpowiedzi właściciela 1–3: zapisane (Assumptions 1–3). Odpowiedzi 1 i 2 są zrealizowane poprawnie, także łącznie. Odpowiedź 3 nie jest zrealizowana przy późniejszej zmianie dnia (Critical 1).
- Rejestr zamiast kursora, granica `OccurrencesFrom`: zapisane (4).
- `PausedOn` bez przedziałów, wstrzymanie najpierw tworzy należne, odrzucenie wstrzymania zakończonej: zapisane (5).
- Tworzenie w transakcji zapisu bez limitu, wsady po 100: zapisane (6). `IAuditLog` zapisuje każdy wpis osobnym `SaveChanges`, więc wsady dotyczą tylko wydatków i rejestru. Nie jest to problem poprawności, a test wydajności od 2000 r. to pokryje.
- Wystąpienia sprzed uruchomienia, `CostDate = DueDate`, autor „Zadanie automatyczne”, fizyczne usunięcie definicji: zapisane (7–10).
- Zmiana dnia nie przesuwa granicy: zapisane (11). Skutek uboczny sprzeczny z PRD i odpowiedzią 3 (Critical 1).
- „Minione” = termin przed dzisiaj, wzór zmiany początku: zapisane (12). Sprawdzone na przypadkach granicy przeszłej i przyszłej, poprawne.
- Blokada `UPDLOCK, ROWLOCK` jak `OrderLock`, READ COMMITTED, `1205` → ponowienie albo `503`: zapisane (13). Zachowanie transakcji po zakleszczeniu niezapisane (Non-Critical 2).
- Zadanie 00:10, 30 min, pętla w Infrastructure przez `IDispatcher`, odcięcie 400 dni: zapisane (14). Uzasadnienie odcięcia (RPO/RTO, przedłużenie synchroniczne) jest spójne.
- Stan „Zakończony” według `LastDueDate`, pierwszeństwo przed „Wstrzymany”: zapisane (15). Pierwszeństwo wobec `nextDueDate` nieokreślone (Non-Critical 4).
- Walidacja, umiejscowienie UI, podgląd `GET`, filtr z parametru adresu: zapisane (16–19). Zgodne z design guide §3 i §6.
- Idempotencja w `RecurringDefinitionRequests` z zapisaną liczbą wystąpień: zapisane (20).
- E2E w SF-21, ryzyko: zapisane (21–22).
- Ostrzeżenie o zadaniu (źródło `GET /api/jobs`, próg 25 h, odczyt przy wejściu): zapisane (23). Zgodne z rozdziałem z SF-11 i kontraktem `JobStatusDto` (kody statusów zweryfikowane w `JobCodes`).
- Aktualizacja PRD, `nextDueDate` jako najbliższy nieutworzony, skrócenie końca, kolizja jako wynik idempotentny: zapisane (24–27).
- **Niezapisane:** miejsce ponowienia jednostki w zadaniu wobec konwencji zakresów (Non-Critical 1); zachowanie `UnitOfWork` po wycofaniu transakcji przez serwer przy `1205` (Non-Critical 2).

## Recommendation

- Revise specification
