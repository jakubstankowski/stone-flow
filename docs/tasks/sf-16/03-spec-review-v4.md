# Spec review — SF-16

## Summary

- Overall assessment: FAIL
- Justification: Wersja 4 rozwiązuje oba Critical z v3. Definicja z 20.11 poprawiona z dnia 10 na 25 tworzy listopad 25.11, początek 15.12 przy zmianie 10 → 20 daje grudzień 20.12, a czerwiec przy końcu 25.06 i zmianie 28 → 20 nie powstaje. Wszystkie pięć Non-Critical z v3 też jest domkniętych, a niezmienniki 1–8 są zachowane. Krok (b) reguły 7 podnosi jednak granicę `OccurrencesFrom` na pierwszy dzień bieżącego okresu także przy definicji zakończonej przed bieżącym okresem. Późniejsze przedłużenie nie tworzy wtedy zaległych wystąpień między dawnym końcem a dzisiaj. To narusza odpowiedź właściciela 2 i proponowany przez samą spec tekst PRD §5. Spec zna to zagrożenie, bo z tego samego powodu odrzuca wstrzymanie definicji zakończonej (Assumptions 5).

## Findings

### Critical Issues

1. **Zmiana dnia na definicji zakończonej odcina okresy, które według odpowiedzi 2 ma utworzyć późniejsze przedłużenie (Behavior → „Pojęcia i reguły domeny”, reguła 4 i reguła 7 krok (b); Assumptions 2, 5 i 11; Edge Cases → „Zmiana dnia nie wciąga miesiąca końca”; Behavior → „PRD — aktualizacja §5”, fragment 2).**
   - Przykład oparty na Edge Case ze spec: koniec 25.06, dzień 28, stan „Zakończony”. 20.11 użytkownik zmienia dzień na 20. Krok (b) ustawia `OccurrencesFrom` = max(granica, 01.11) = 01.11, choć koniec to 25.06. 21.11 użytkownik przedłuża definicję („bez końca”). Zmiana końca nie rusza granicy, więc lipiec–październik mają terminy < 01.11 i według reguły 5 nie są należne. Powstaje tylko listopad (20.11).
   - Odpowiedź 2 (Assumptions 2): „przedłużenie tworzy zaległe wystąpienia za okresy między dawnym końcem a dzisiaj (…). Okresy z czasu wstrzymania nie powstają.” Lipiec–październik nie przypadły w czasie wstrzymania, więc według decyzji właściciela powinny powstać. Proponowany tekst PRD §5 (fragment 2) mówi to samo: „przy przedłużeniu zakończonej definicji: za okresy między dawnym końcem a dzisiaj, bez okresów z czasu wstrzymania”. Reguła 4 twierdzi: „Przerwa w działaniu systemu i zmiana końca nie ruszają granicy. Terminy z przerwy i z przedłużenia są więc uzupełniane”. Reguła 7 (b) przeczy więc zarówno decyzji właściciela, jak i własnym zdaniom spec.
   - Wynik zależy od kolejności zapisów. Przedłużenie i zmiana dnia w jednym zapisie albo w kolejności „najpierw przedłużenie, potem dzień” tworzą lipiec–październik. Kolejność „najpierw dzień, potem przedłużenie” tych wystąpień nie tworzy, co zaniża koszty czterech minionych miesięcy. Spec opisuje tylko pierwszą kolejność (reguła 7, ostatni punkt „Skutki”; Assumptions 11).
   - Assumptions 5 odrzuca wstrzymanie definicji zakończonej właśnie dlatego, że „granica przesunięta przy późniejszym wznowieniu odcięłaby okresy po dawnym końcu przy przedłużeniu (Assumptions 2)”. Zmiana dnia na takiej definicji jest dozwolona i daje ten sam skutek. Brakuje reguły, Edge Case i testu.
   - Rozstrzygnięcie zmienia to, które koszty istnieją, więc należy do specyfikacji, a nie do planu. Możliwe kierunki dla autora (bez przesądzania): ograniczyć krok (b), gdy `EndsOn` < pierwszy dzień bieżącego okresu, i ustalić, według którego dnia powstają wtedy okresy z przedłużenia; odrzucić zmianę dnia na takiej definicji, jak wstrzymanie; albo zapisać decyzję właściciela.

### Non-Critical Issues

1. **„Łączny zapis daje ten sam zbiór okresów co osobne zapisy” nie jest ogólnie prawdą (reguła 7, ostatni punkt „Skutki”; Assumptions 11; Acceptance Criteria, punkt o zmianie dnia; Verification → „przedłużenie i zmiana dnia razem i osobno”).** Przykład: koniec 30.06 → 15.11, dzień 10 → 25, dzisiaj 20.11. Osobne przedłużenie tworzy listopad z terminem 10.11 (≤ 15.11, minął). Potem zmiana dnia go nie rusza. Zapis łączny daje listopadowi w kroku (b) termin 25.11 > 15.11, więc listopad się nie liczy. Zbiory się różnią (lipiec–listopad wobec lipiec–październik), a spec dopuszcza różnicę tylko w terminie. Zachowanie jest zgodne z regułą PRD [początek, koniec], ale twierdzenie i kryterium akceptacji trzeba zawęzić (np. „gdy nowy termin bieżącego okresu mieści się w zakresie”) albo opisać wyjątek i dodać go do testów.
2. **Należny, a nieutworzony okres bieżący przepada, gdy nowy termin wypada poza zakres (reguła 7, „Skutki”; Edge Cases → „Nowy termin bieżącego okresu przed początkiem” i „… po końcu”).** Reguła ogólna to obejmuje, ale oba przykłady w v4 dotyczą okresu, który jeszcze nie był należny (termin w przyszłości). Przypadek z NC1 v3 to okres już należny i nieutworzony przez błąd zadania, np. początek 05.11, dzień 10, zadanie nie działało 10.11, dzisiaj 20.11, zmiana na dzień 3. Ten przypadek zniknął z przykładów. Przypadek należny trzeba dodać do Edge Cases i testów. Warto też rozważyć, czy podgląd ma powiedzieć, że okres z minionym terminem nie powstanie. Szczegóły pokazują go teraz jako „Termin … już minął … utworzy je zadanie automatyczne”.
3. **Kontrakt `OccurrenceGenerator` nie opisuje trybu kroku (a) (Behavior → „Tworzenie wystąpień (`OccurrenceGenerator`)”, pkt 1).** Generator „liczy okresy należne na D (reguła 5)” z bieżącego dnia definicji. Krok (a) wymaga generowania według **starego** dnia i tylko dla okresów sprzed bieżącego, zanim definicja dostanie nowy dzień. Nie wiadomo, czy to parametr generatora (dzień, górna granica okresu), czy kolejność operacji na encji. Plan może to domknąć, ale kontrakt warto doprecyzować.
4. **`DuplicateRequest` w kroku (a) i kolejność audytu (Behavior → „Szczegóły, edycja i usunięcie definicji” pkt 3; „Blokada i kolejność kroków”).** Wycofanie całości i `503 recurring_expense_busy` są opisane tylko przy kroku 7 (generowanie (b)). Krok (a) idzie wcześniej, w kroku 3, i nie ma takiego zdania. Do tego „Blokada i kolejność kroków” stawia audyt definicji po generowaniu (pkt 5), a edycja przed nim (pkt 6 przed 7). W jednej transakcji nie ma to skutku, ale opis powinien być jeden.

### Unclear or Ambiguous Sections

- **Behavior → reguła 4:** zdanie „zmiana końca nie rusza granicy. Terminy (…) z przedłużenia są więc uzupełniane” nie zachodzi po wcześniejszej zmianie dnia na definicji zakończonej (Critical 1).
- **Behavior → reguła 7, ostatni punkt „Skutki”; Assumptions 11, ostatni punkt:** „ten sam zbiór okresów” ma wyjątki: nowy termin bieżącego okresu poza zakresem (Non-Critical 1) i kolejność „najpierw dzień, potem przedłużenie” (Critical 1).
- **Behavior → „Tworzenie wystąpień (`OccurrenceGenerator`)”:** brak opisu wejść dla kroku (a) (Non-Critical 3).
- **Behavior → „Blokada i kolejność kroków” wobec „Szczegóły, edycja i usunięcie definicji” pkt 3:** różna kolejność audytu i generowania (Non-Critical 4).

### Invented or Unsupported Requirements

None. Dwuetapowa zmiana dnia z reguły 7 wynika z odpowiedzi 1 i z zasad PRD §5 (zakaz minionych wystąpień przy zmianie początku, zaległe tylko po przerwie i przy przedłużeniu). Ograniczenia spoza PRD są jawne i uzasadnione, jak w v3: zakres 2000–2099, odcięcie 400 dni, próg 25 h, odrzucenie wstrzymania definicji zakończonej, `recurring_expense_deleted`, `503 recurring_expense_busy` (Assumptions 5, 13, 14, 16, 20, 23). Wyjątkiem jest skutek opisany w Critical 1. Nie wynika on z żadnej decyzji, tylko z luki w regule.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Behavior → reguła 7 krok (b); Edge Cases „Zmiana dnia nie wciąga miesiąca końca” | Assumptions 2 (odpowiedź właściciela z 08.11.2026) | Po zmianie dnia na definicji zakończonej przed bieżącym okresem późniejsze przedłużenie nie tworzy zaległych za okresy między dawnym końcem a dzisiaj, choć nie przypadły w czasie wstrzymania |
| Behavior → reguła 7 krok (b) | Behavior → „PRD — aktualizacja §5”, fragment 2; reguła 4 | Proponowany tekst PRD i reguła 4 obiecują uzupełnienie terminów z przedłużenia, a krok (b) je odcina |
| Behavior → reguła 7 krok (b) | Assumptions 5 (uzasadnienie odrzucenia wstrzymania definicji zakończonej) | Ten sam skutek (granica odcinająca okresy po dawnym końcu) jest dopuszczony przy zmianie dnia |
| Reguła 7, „Skutki”; Acceptance Criteria (zmiana dnia) | Reguła 5 i PRD §5 (termin między początkiem a końcem) | Twierdzenie o tym samym zbiorze okresów nie zachodzi, gdy nowy termin bieżącego okresu wypada po nowym końcu (Non-Critical 1) |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Wystąpienie z `PaidOn = null`, stan „Planowany”; żadna ścieżka SF-16 nie ustawia zapłaty; jest test |
| 2. Pieniądze jako decimal w backendzie | OK | `decimal(18,2)`, kwoty jako stringi, parsowanie SF-15 bez zaokrągleń; terminy, liczby, stan i podgląd liczy backend |
| 3. Dostęp egzekwowany w API | OK | Wszystkie trasy, także `preview` i `GET /api/jobs`, wymagają sesji, zapisy XSRF; `EndpointSecurityTests`, brak wpisów anonimowych |
| 4. Ponowienia nie duplikują | OK | `PK_RecurringExpensePeriods (DefinitionId, Period)` w transakcji wystąpienia; rejestr przeżywa usunięcie wydatku; blokada `UPDLOCK, ROWLOCK`; ponowienie tylko w zadaniu, w nowym zakresie DI; `RecurringDefinitionRequests` po usunięciu definicji. Critical 1 dotyczy tego, które okresy się liczą, a nie duplikatów |
| 5. Audyt finansowy | OK | Definicja `Created`/`Updated`/`Deleted` z autorem konta; wystąpienie z `AuditAuthor.AutomatedJob` (istnieje w `IAuditLog.cs`) i rodzicem `RecurringExpenseDefinition` (istnieje w `AuditVocabulary.cs`); wszystko w jednej `IUnitOfWork` |
| 6. Sekrety po stronie serwera | OK | Komunikat wyjątku zadania i logi tylko z liczbami i id; komponent nie pokazuje `error` z `JobStatusDto` |
| 7. Granice modułów i warstw | OK | Wszystko w module Expenses i schemacie `expenses`; pętla, zakresy DI, blokada SQL i rozpoznanie `1205` w Infrastructure; Application bez `IServiceScopeFactory` (test architektury); domena bez EF/HTTP |
| 8. Brak danych identyfikujących w dokumentach | OK | Przykłady fikcyjne („Abonament – Dostawca A”) |

## Risk

- Recorded: high
- Assessed: high. Zadanie bez udziału użytkownika zapisuje koszty trafiające do raportów, a usunięcie definicji jest nieodwracalne. Reguły granicy przy zmianie dnia i przedłużeniu bezpośrednio decydują, które koszty minionych miesięcy powstają (Critical 1). Przed merge wymagany jest przegląd przez człowieka.

## Assumptions Detected

- Odpowiedzi właściciela 1–3: zapisane (Assumptions 1–3). Odpowiedź 1 jest teraz zrealizowana bez wyjątków ustanowionych przez autora (Critical 1 z v3 rozwiązane). Odpowiedź 3 jest zrealizowana przy wznowieniu. Późniejszą zmianę dnia ocenia krok (b), co zgadza się z uwagą przeglądu v3. Odpowiedź 2 jest zrealizowana, z wyjątkiem kolejności „najpierw zmiana dnia, potem przedłużenie” (Critical 1).
- Rejestr zamiast kursora, granica `OccurrencesFrom`: zapisane (4).
- `PausedOn` bez przedziałów, wstrzymanie najpierw tworzy należne, odrzucenie wstrzymania definicji zakończonej: zapisane (5). Uzasadnienie tego odrzucenia nie zostało przeniesione na zmianę dnia (Critical 1).
- Tworzenie w transakcji zapisu bez limitu, wsady po 100: zapisane (6).
- Wystąpienia sprzed uruchomienia, `CostDate = DueDate`, autor „Zadanie automatyczne”, fizyczne usunięcie definicji: zapisane (7–10).
- Zmiana dnia w dwóch krokach (okresy minione według starego dnia, nowy dzień od bieżącego okresu): zapisane (11). Górna granica (czerwiec przy końcu 25.06) jest teraz chroniona (Critical 2 z v3 rozwiązane), a nowy termin poza [początek, koniec] jest opisany (NC1 z v3). **Niezapisane:** skutek podniesionej granicy dla późniejszego przedłużenia (Critical 1) i wyjątek od „tego samego zbioru okresów” (Non-Critical 1).
- „Minione” = termin przed dzisiaj, wzór zmiany początku: zapisane (12). Poprawne, także w połączeniu z krokiem (a).
- Blokada `UPDLOCK, ROWLOCK`, ponowienie tylko w zadaniu, `1205` przez fallback `DbException` → `503`: zapisane (13). Zgodne z kodem: `UnitOfWork.ExecuteInTransactionAsync` woła `RollbackAsync` tylko dla wyniku z `commitWhen = false`, a na ścieżce wyjątku zostają `EndTransaction(false)` i zwolnienie przez `await using`.
- Zadanie 00:10, 30 min, pętla w Infrastructure, odcięcie 400 dni: zapisane (14).
- Stan „Zakończony” według `LastDueDate`: zapisane (15).
- Walidacja, umiejscowienie UI, podgląd `GET`, filtr z parametru, idempotencja dodania, kolejność „najpierw skrót”: zapisane (16–20, 28).
- E2E w SF-21, ryzyko, ostrzeżenie o zadaniu, aktualizacja PRD, `nextDueDate`, skrócenie końca, kolizja rejestru, dialog „Wznów”: zapisane (21–27, 29).
- Okres z czasu wstrzymania według dnia obowiązującego przy wznowieniu: zapisane w Acceptance Criteria i Edge Cases (NC2 z v3 rozwiązane).
- Zwracana `version` po zapisie definicji: zapisane (NC5 z v3 rozwiązane).

## Recommendation

- Revise specification
