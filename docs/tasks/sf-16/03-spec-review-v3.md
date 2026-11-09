# Spec review — SF-16

## Summary

- Overall assessment: FAIL
- Justification: Wersja 3 usuwa oba przypadki z Critical v2, czyli marzec przed początkiem 15.03 i sierpień pusty po wznowieniu 15.08. Rozwiązuje też wszystkie siedem Non-Critical z v2 i jest zgodna z kodem `UnitOfWork`, `ModuleConnection` i fallbackiem `SelectStatusCode`. Niezmienniki 1–8 są zachowane. Nowa reguła „granica na poziomie okresu według starego dnia” działa jednak za szeroko w jedną stronę, a za wąsko w drugą. Odcina okresy bieżące i przyszłe z terminem w zakresie [początek, koniec], wbrew PRD §5 i odpowiedzi 1, co spec sama nazywa „wyjątkiem od odpowiedzi 1”. Nie chroni za to górnej granicy: zmiana dnia potrafi utworzyć miniony okres z miesiąca końca, który przy starym dniu się nie liczył, a więc ten sam rodzaj błędu co Critical v2.

## Findings

### Critical Issues

1. **Granica liczona według starego dnia odcina okresy bieżące i przyszłe z terminem w zakresie (Behavior → „Pojęcia i reguły domeny”, reguła 7 „zmiana dnia miesiąca”, ostatni punkt „wyjątek”; Assumptions 11, ostatni punkt; Edge Cases → „Bieżący okres, który przy starym dniu się nie liczył”; Behavior → „PRD — aktualizacja §5”, fragment 1).**
   - Najczęstszy przypadek: 20.11 użytkownik dodaje definicję z domyślnym początkiem „dzisiaj” (20.11) i dniem 10. Tego samego dnia poprawia dzień na 25. Według reguły 7: G = 20.11, a P* to grudzień, bo 10.11 < 20.11. `OccurrencesFrom` = max(20.11, 01.12) = 01.12. Listopad z terminem 25.11 (w przyszłości, ≥ początku, bez wystąpienia) nie powstanie **nigdy**. Ta sama definicja zapisana od razu z dniem 25 utworzyłaby 25.11. Wynik zależy więc od historii edycji, a nie od definicji.
   - Tak samo jest przy początku w przyszłości: początek 15.12, dzień 10 → 20. Granica 01.01 odcina grudzień z terminem 20.12, choć nic jeszcze nie minęło.
   - Przypadek opisany w spec (początek 15.11, dzień 10 → 18, dzisiaj 20.11) ma ten sam skutek dla bieżącego okresu z minionym terminem.
   - PRD §5: „Okres się liczy, gdy termin wypada między początkiem a końcem definicji, włącznie z tymi dniami.” Odpowiedź 1: „Jeśli bieżący okres nie ma jeszcze wystąpienia, a nowy termin już minął, wystąpienie bieżącego okresu powstaje od razu.” Odpowiedź mówi też, że nowy dzień obowiązuje „od następnego okresu” tylko wtedy, gdy okres ma już wystąpienie. Zakaz PRD i Critical v2 dotyczyły **minionych** okresów sprzed bieżącego. Okresy bieżący i przyszłe nie są „minione”, więc „reguła wynikająca z zakazu tworzenia minionych wystąpień” w proponowanym tekście PRD ich nie obejmuje.
   - Spec sama pisze „Wyjątek od odpowiedzi 1” (Assumptions 11). Autor nie może ustanowić wyjątku od decyzji właściciela. Trzeba zawęzić regułę do okresów sprzed bieżącego albo uzyskać i zapisać decyzję właściciela. Dotyczy to też bieżącego okresu, którego stary termin wypadł w czasie wstrzymania, a nowy jest po wznowieniu: odpowiedź 3 dotyczy wznowienia, a nie późniejszej zmiany dnia.
   - Brakuje Edge Case i testu „definicja z początkiem dzisiaj albo w przyszłości, zmiana dnia przed pierwszym terminem”.

2. **Zmiana dnia tworzy miniony okres z miesiąca końca, który przy starym dniu się nie liczył (reguła 5 i 7; Assumptions 11; Behavior → „PRD — aktualizacja §5”, fragment 1).**
   - Przykład: koniec 25.06, dzień 28, dzisiaj 20.11, stan „Zakończony” (`LastDueDate` = 28.05). Czerwiec się nie liczył, bo 28.06 > 25.06. Zmiana dnia na 20 daje 20.06, a to mieści się w [`StartsOn`, `EndsOn`]. Termin jest też ≥ `OccurrencesFrom` (granica dotyczy tylko dołu) i nie ma go w rejestrze. Zapis tworzy więc w listopadzie wystąpienie za czerwiec, a `LastDueDate` zmienia się na 20.06.
   - To ten sam rodzaj skutku co Critical v2, tyle że przy górnej granicy. PRD §5 mówi: „poza zapisem nowej definicji tworzenie zaległych wystąpień dotyczy wyłącznie przerw w działaniu systemu”. Odpowiedź 2 dopuszcza zaległe tylko przy przedłużeniu, a tu koniec się nie zmienia. Narusza to też zasadę zapisaną w samej spec (Assumptions 11: „zmiana dnia nie może wciągać okresów, które przy starym dniu się nie liczyły”) i jej własny tekst PRD. Nawias tego tekstu wymienia tylko „termin przed początkiem albo w czasie wstrzymania”.
   - Reguła 7 i tekst PRD są więc ze sobą sprzeczne. Implementacja według wzoru utworzy czerwiec, a według zasady nie. Brakuje reguły, Edge Case i testu dla miesiąca końca, także przy łącznym przedłużeniu i zmianie dnia.

### Non-Critical Issues

1. **Okres, który się liczył, przepada, gdy nowy termin wypada przed początkiem albo po końcu (reguła 7, skutek „okres, który przy starym dniu się liczył … liczy się dalej”).** Przykład: początek 05.11, dzień 10, listopad nie powstał przez błąd zadania, dzisiaj 20.11. Po zmianie na dzień 3 termin 03.11 < `StartsOn`, więc reguła 5 listopad odrzuca. Jest to zgodne z regułą PRD [początek, koniec], ale niezgodne z dosłownym brzmieniem odpowiedzi 1 i z twierdzeniem spec „liczy się dalej”. Tak samo działa miesiąc końca przy nowym terminie po `EndsOn`. Trzeba to opisać w regule, w Edge Cases i w testach.
2. **„Okres z czasu wstrzymania” to w praktyce pojęcie zależne od dnia (Acceptance Criteria, punkt o wstrzymaniu; Edge Cases → „Okres liczony przy starym dniu, z nowym terminem przed dawną granicą”).** AC mówi: „Wznowienie nie tworzy okresów z terminem w czasie wstrzymania”. W przykładzie wznowienie jest 05.03, a dzień zmienia się z 10 na 3. Spec świadomie tworzy wtedy marzec z terminem 03.03, czyli z dnia wstrzymania. Kwalifikacja według dnia obowiązującego w chwili wznowienia jest spójna z Assumptions 11, ale nie wynika z AC. Warto zapisać tę definicję w regułach i w AC.
3. **PRD §5, fragment 2: „Wznowienie tworzy kolejne okresy”.** Obecne PRD mówi „bieżący i kolejne”. Bieżący okres nadal powstaje, gdy jego termin przypada w dniu wznowienia albo później (Edge Cases: „wznowienie 10.11 → listopad powstaje”). Nowy tekst wyraża to tylko pośrednio, przez warunek „gdy termin bieżącego okresu minął”. Proponowane brzmienie: „Wznowienie tworzy bieżący okres (jeśli jego termin nie minął w czasie wstrzymania) i kolejne”.
4. **PRD §5, fragment 1: wewnętrzne napięcie tekstu.** Zdanie przypisane właścicielowi każe utworzyć bieżący okres. Następne zdanie („reguła wynikająca”) wyklucza okresy, które przy starym dniu się nie liczyły, także bieżący. To skutek Critical 1, do poprawienia razem z nim.
5. **Kolejność zapis → generowanie przy edycji i wznowieniu (Behavior → „Blokada i kolejność kroków”, „Szczegóły, edycja i usunięcie definicji” pkt 3, „Wstrzymanie i wznowienie” pkt 2).** Model jest poprawny: generowanie nie zmienia definicji ani wersji, a `DuplicateRequest` wycofuje także zapis definicji. Spec nie mówi jednak wprost, że `version` w odpowiedzi to wersja po zapisie definicji, a nie po generowaniu. Generowanie jej nie zmienia, więc to tylko doprecyzowanie dla planu.

### Unclear or Ambiguous Sections

- **Behavior → reguła 7, „zmiana dnia miesiąca”:** formuła jest jednoznaczna, ale jej skutki opisane w punktach „Skutki” i w Assumptions 11 („okresy, które przy starym dniu się nie liczyły, nie wchodzą”) obejmują tylko dolną granicę (Critical 2). Twierdzenie „liczy się dalej” ma wyjątki przy `StartsOn`/`EndsOn` (Non-Critical 1).
- **Assumptions 11, ostatni punkt:** „Wyjątek od odpowiedzi 1” to decyzja autora sprzeczna z decyzją właściciela (Critical 1).
- **Assumptions 11:** „Zbiór okresów nie zależy od kolejności zapisów” jest prawdą dla przedłużenia i zmiany dnia, ale nie dla „dodanie, potem zmiana dnia” wobec „dodanie od razu z nowym dniem” (Critical 1).
- **Behavior → „PRD — aktualizacja §5”:** fragment 1 (Non-Critical 4) i fragment 2 (Non-Critical 3).

### Invented or Unsupported Requirements

- Wykluczenie bieżącego i przyszłych okresów przy zmianie dnia (reguła 7, „wyjątek”; Assumptions 11) nie ma podstawy w PRD ani w odpowiedziach właściciela (Critical 1).
- Pozostałe ograniczenia spoza PRD są jawne i uzasadnione, jak w v2: zakres 2000–2099, odcięcie 400 dni, próg 25 h, odrzucenie wstrzymania definicji zakończonej, `recurring_expense_deleted`, `503 recurring_expense_busy` (Assumptions 5, 13, 14, 16, 20, 23).

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → reguła 7 („wyjątek”); Assumptions 11; Edge Cases „Bieżący okres, który przy starym dniu się nie liczył” | Assumptions 1 (odpowiedź właściciela z 08.11.2026) | Bieżący okres bez wystąpienia z minionym albo przyszłym nowym terminem nie powstaje; spec nazywa to „wyjątkiem od odpowiedzi 1” |
| Behavior → reguła 7 | PRD §5 („Okres się liczy, gdy termin wypada między początkiem a końcem definicji”) | Okresy przyszłe z terminem w zakresie przepadają trwale po zmianie dnia (np. 25.11 przy definicji z 20.11) |
| Behavior → reguły 5 i 7 (brak ograniczenia górnego) | PRD §5 („poza zapisem nowej definicji tworzenie zaległych wystąpień dotyczy wyłącznie przerw w działaniu systemu”); Assumptions 2 | Zmiana dnia tworzy miniony okres z miesiąca końca, który przy starym dniu się nie liczył, bez zmiany końca |
| Behavior → „PRD — aktualizacja §5”, fragment 1 | Ten sam fragment (zdanie przypisane właścicielowi) | „Reguła wynikająca” wyklucza bieżący okres, który zdanie właściciela każe utworzyć |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Wystąpienie z `PaidOn = null`, stan „Planowany”; żadna ścieżka SF-16 nie ustawia zapłaty; jest test |
| 2. Pieniądze jako decimal w backendzie | OK | `decimal(18,2)`, kwoty jako stringi, parsowanie SF-15; terminy, liczby i stan liczy backend, także podgląd |
| 3. Dostęp egzekwowany w API | OK | Wszystkie trasy, także `preview` i `GET /api/jobs`, wymagają sesji, zapisy XSRF; `EndpointSecurityTests` |
| 4. Ponowienia nie duplikują | OK | `PK_RecurringExpensePeriods (DefinitionId, Period)` w transakcji wystąpienia; rejestr przeżywa usunięcie wydatku; blokada `UPDLOCK`; ponowienie tylko w zadaniu, w nowym zakresie DI; `RecurringDefinitionRequests` po usunięciu definicji. Błędy z Critical 1–2 dotyczą tego, które okresy się liczą, a nie duplikatów |
| 5. Audyt finansowy | OK | Definicja `Created`/`Updated`/`Deleted` z autorem konta; wystąpienie z `AuditAuthor.AutomatedJob` i rodzicem `RecurringExpenseDefinition` (oba istnieją w `Shared`); dalsze zmiany z rodzicem; wszystko w jednej `IUnitOfWork` |
| 6. Sekrety po stronie serwera | OK | Komunikat wyjątku zadania i logi tylko z liczbami i id; komponent nie pokazuje `error` z `JobStatusDto` |
| 7. Granice modułów i warstw | OK | Wszystko w module Expenses i schemacie `expenses`; pętla, zakresy DI, blokada SQL i rozpoznanie `1205` w Infrastructure; Application bez `IServiceScopeFactory` (test architektury); domena bez EF/HTTP |
| 8. Brak danych identyfikujących w dokumentach | OK | Przykłady fikcyjne („Abonament – Dostawca A”) |

## Risk

- Recorded: high
- Assessed: high. Zadanie bez udziału użytkownika zapisuje koszty trafiające do raportów, a usunięcie definicji jest nieodwracalne. Reguła zmiany dnia (Critical 1–2) bezpośrednio zaniża albo zawyża koszty bieżących i minionych miesięcy. Przed merge wymagany jest przegląd przez człowieka.

## Assumptions Detected

- Odpowiedzi właściciela 1–3: zapisane (Assumptions 1–3). Odpowiedź 2 i odpowiedź 3 (przy wznowieniu) są zrealizowane poprawnie. Odpowiedź 1 jest zrealizowana z wyjątkiem ustanowionym przez autora (Critical 1).
- Rejestr zamiast kursora, granica `OccurrencesFrom`: zapisane (4).
- `PausedOn` bez przedziałów, wstrzymanie najpierw tworzy należne, odrzucenie wstrzymania zakończonej: zapisane (5).
- Tworzenie w transakcji zapisu bez limitu, wsady po 100: zapisane (6).
- Wystąpienia sprzed uruchomienia, `CostDate = DueDate`, autor „Zadanie automatyczne”, fizyczne usunięcie definicji: zapisane (7–10).
- Granica na poziomie okresu przy zmianie dnia: zapisane (11). Skutki dla bieżącego i przyszłych okresów (Critical 1) oraz brak ograniczenia od góry (Critical 2) to decyzje bez podstawy w PRD albo luki. Utrata okresu przy nowym terminie spoza [początek, koniec] nie jest zapisana (Non-Critical 1).
- „Minione” = termin przed dzisiaj, wzór zmiany początku: zapisane (12). Poprawne.
- Blokada `UPDLOCK, ROWLOCK` jak `OrderLock`, ponowienie tylko w zadaniu, `1205` przez fallback `DbException` → `503`: zapisane (13). Sprawdzone w kodzie: `UnitOfWork.ExecuteInTransactionAsync` na ścieżce wyjątku nie woła `RollbackAsync` (tylko `EndTransaction(false)` i zwolnienie przez `await using`). `SecuritySetup.SelectStatusCode` przechodzi łańcuch `InnerException`, więc `DbUpdateException` z `SqlException` 1205 też daje `503`. Konteksty, które tylko czytały (blokada), nie są czyszczone, ale ponowienie w nowym zakresie omija ten problem.
- Zadanie 00:10, 30 min, pętla w Infrastructure, odcięcie 400 dni: zapisane (14).
- Stan „Zakończony” według `LastDueDate`: zapisane (15). `CHECK` jest teraz jawny dla obu członów i ma test obu niespójnych wariantów (NC3 z v2 rozwiązane).
- Walidacja, umiejscowienie UI, podgląd `GET`, filtr z parametru: zapisane (16–19).
- Idempotencja w `RecurringDefinitionRequests`, kolejność „najpierw skrót”: zapisane (20, 28).
- E2E w SF-21, ryzyko: zapisane (21–22).
- Ostrzeżenie o zadaniu z `running` bez sukcesu wyłączonym: zapisane (23). NC7 z v2 rozwiązane.
- `nextDueDate` jako najbliższy nieutworzony kwalifikujący się termin z pierwszeństwem przed „Zakończony”: zapisane (25). NC4 z v2 rozwiązane.
- Dialog „Wznów” z `resumePreview`: zapisane (29). NC6 z v2 rozwiązane. `occurrencesToCreate` przy wznowieniu jest najwyżej 1, bo granica ≥ dzisiaj, więc tekst o „wystąpieniu za bieżący okres” jest poprawny.
- **Niezapisane:** traktowanie miesiąca końca przy zmianie dnia (Critical 2); definicja „okresu z czasu wstrzymania” według dnia obowiązującego przy wznowieniu (Non-Critical 2).

## Recommendation

- Revise specification
