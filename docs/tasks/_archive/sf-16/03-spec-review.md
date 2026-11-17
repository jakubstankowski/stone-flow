# Spec review — SF-16

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Wersja 6 usuwa Critical z v5. Zmiana dnia na wstrzymanej definicji daje `409 recurring_expense_paused`, a reguła 4 nie kwalifikuje żadnego okresu wstrzymanej definicji. Dzięki temu edycja, zadanie, podgląd, `nextDueDate` i szczegóły mówią to samo: wstrzymana definicja nie tworzy niczego. Sprawdziłem też twierdzenie z Assumptions 30, że pozostałe zmiany wstrzymanej definicji (koniec, początek, pola) nie dają po wznowieniu terminu sprzed `PausedOn` bez wystąpienia. Twierdzenie jest prawdziwe, bo historia dni jest w czasie wstrzymania zamrożona, wstrzymanie najpierw tworzy wszystkie należne, a przesunięcie początku przykrywa `StartMoved`. Wszystkie Non-Critical z v5 (NC1–NC7) są domknięte, niezmienniki 1–8 są zachowane. Zostały drobne luki w tekstach UI, kolejności walidacji i przypadkach brzegowych. Plan może je domknąć.

## Findings

### Critical Issues

None.

### Non-Critical Issues

1. **Podgląd przy edycji wstrzymanej definicji pokazuje nieprawdziwy tekst (Behavior → „Dodanie wydatku cyklicznego” pkt 2; „Szczegóły, edycja i usunięcie definicji” pkt 3; Data / API → `RecurringExpensePreview`).** Przy wstrzymanej definicji podgląd zwraca `nextDueDate = null` (reguła 4 i 6). Według pkt 2 UI pokazuje wtedy „Po zapisie nie powstaną kolejne wystąpienia — do końca nie wypada już żaden termin.” Dla wstrzymanej definicji bez końca albo z końcem w przyszłości to zdanie jest fałszywe i przeczy informacji „Zmiany zadziałają od wznowienia”. Spec powinna podać osobny tekst podglądu dla trybu wstrzymanego, np. ukryć linię następnego wystąpienia albo napisać „Wydatek cykliczny jest wstrzymany — wystąpienia powstaną po wznowieniu.” Jest to tekst produktu, ale nie zmienia kosztów, więc plan może go dobrać, a autor zatwierdzić.
2. **Zmiana dnia zaraz po wznowieniu tworzy okres, który wznowienie ogłosiło jako pusty, a Edge Case tego nie pokazuje (Edge Cases → „Wstrzymanie i wznowienie”, przykład recenzenta; Assumptions 30).** Dzień 10, wstrzymanie 05.11, wznowienie 20.11 (przedział [05.11, 20.11), listopad pusty). Tego samego dnia zmiana dnia na 3 daje t(listopad) = 03.11. To termin sprzed `PausedOn`, więc poza przedziałem, w zakresie i bez wpisu w rejestrze, więc listopad powstaje przy zapisie zmiany. To jest zgodne z odpowiedzią 1 i z fragmentem PRD „Późniejsza zmiana dnia ocenia bieżący okres według nowego terminu”, a podgląd pokaże 1 przed zapisem. Odpowiedź 3 też nie jest łamana, bo 03.11 nie wypada w czasie wstrzymania. Zdanie przykładu „Pierwsze wystąpienie to 10.12” jest jednak prawdziwe tylko bez późniejszej zmiany dnia, a uzasadnienie w Assumptions 30 może sugerować, że blokada `409` zamyka tę drogę całkowicie, choć robi to tylko w czasie wstrzymania. Warto dopisać ten wariant jako przypadek brzegowy z testem (wznowienie → zmiana dnia na termin < `PausedOn`, podgląd 1, listopad 03.11).
3. **Kolejność walidacji w `PUT` nie pasuje do walidacji zależnej od stanu (Behavior → „Szczegóły, edycja i usunięcie definicji” pkt 3, kroki 1–3; Data / API → „Walidacja”, wiersz „harmonogram”).** Krok 1 to „walidacja (`400`)” przed blokadą, a reguła „co najmniej jeden termin efektywny według historii dni po zmianie i rejestru” wymaga wczytania definicji, dni i rejestru pod blokadą (krok 2). Nie wiadomo też, co wygrywa przy wstrzymanej definicji ze zmienionym dniem, dla którego nie ma terminu w zakresie: `400` czy `409 recurring_expense_paused`. Plan powinien rozdzielić walidację formatu (krok 1) od walidacji harmonogramu po kroku 3 i ustalić kolejność kodów. To samo dotyczy `GET /preview` z `definitionId`.
4. **Uzasadnienie przedłużenia w Assumptions 30 jest nieścisłe, choć wniosek jest prawdziwy.** Zdanie „dawny koniec ≥ `LastDueDate` > `PausedOn`” zachodzi w chwili wstrzymania, ale nie po skróceniu końca w czasie wstrzymania (np. wstrzymanie 05.11, potem koniec → 01.10, potem przedłużenie bez końca). Wtedy dawny koniec jest < `PausedOn`. Wniosek się nie zmienia, bo terminy ≤ końca z chwili wstrzymania i ≤ `PausedOn` powstały przy wstrzymaniu, a historia dni jest zamrożona. Warto oprzeć uzasadnienie na końcu z chwili wstrzymania i dodać test „skrócenie, potem przedłużenie w czasie wstrzymania, potem wznowienie” (0 wystąpień sprzed `PausedOn`).
5. **Tekst PRD §5, fragment 2: „Wznowienie tworzy bieżący okres, jeśli jego termin nie minął w czasie wstrzymania” (Behavior → „PRD — aktualizacja §5”).** Gdy termin bieżącego okresu jest po dniu wznowienia, wznowienie go nie tworzy. Powstaje on w dniu terminu, jak każdy okres. Zdanie nie przeczy regule „wystąpienie powstaje w dniu terminu” z tej samej sekcji, ale czytane dosłownie sugeruje utworzenie od razu. Wystarczy „…bieżący okres liczy się, jeśli…” albo „…powstaje w dniu terminu, jeśli…”.
6. **Pole „Dzień miesiąca” zablokowane w formularzu a treść `PUT` (UI → „Formularz definicji”; „Szczegóły, edycja…” pkt 3).** API wymaga `dayOfMonth` także dla wstrzymanej definicji i odrzuca inny niż bieżący. Pole `disabled` w formularzu reaktywnym Angular nie trafia do `value`. Plan musi zapewnić wysłanie bieżącego dnia (np. `getRawValue()`) i dodać test, że zapis wstrzymanej definicji nie dostaje `400`.

### Unclear or Ambiguous Sections

- **Behavior → „Dodanie wydatku cyklicznego” pkt 2 (tekst przy braku `nextDueDate`):** zdanie „do końca nie wypada już żaden termin” jest używane także w trybie wstrzymanym, gdzie jest fałszywe (Non-Critical 1).
- **Behavior → „Szczegóły, edycja i usunięcie definicji” pkt 3, krok 1:** zakres „walidacji (`400`)” przed blokadą (Non-Critical 3).
- **Assumptions 30, akapit o przedłużeniu:** „dawny koniec” bez wskazania, że chodzi o koniec z chwili wstrzymania (Non-Critical 4).
- **Behavior → „PRD — aktualizacja §5”, fragment 2:** „Wznowienie tworzy bieżący okres” (Non-Critical 5).

### Invented or Unsupported Requirements

None. Nowe ograniczenie v6, czyli blokada zmiany dnia na wstrzymanej definicji (`409 recurring_expense_paused`, pole zablokowane), nie wynika wprost z PRD. Jest jednak jawną decyzją autora z uzasadnieniem (Assumptions 30), ma prosty sposób obejścia (wznowić, potem zmienić dzień) i chroni zasadę PRD „Wstrzymanie zatrzymuje nowe wystąpienia”. Pozostałe ograniczenia spoza PRD są jawne i uzasadnione, jak w v5 (Assumptions 5, 12, 13, 14, 16, 20, 23).

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| — | — | Brak konfliktów z PRD §3, §5, §6 (WYD-03–07), §7, §8, §11, D-04 ani z design guide. Critical z v5 (PRD §5 „Wstrzymanie zatrzymuje nowe wystąpienia”) rozwiązany regułą 4 i blokadą `409` |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Wystąpienie z `PaidOn = null`, stan „Planowany”. Żadna ścieżka SF-16 nie ustawia zapłaty, jest na to test |
| 2. Pieniądze jako decimal w backendzie | OK | `decimal(18,2)`, kwoty jako stringi parsowane pomocnikami SF-15 bez zaokrągleń. Terminy, liczby, stan i podgląd liczy backend |
| 3. Dostęp egzekwowany w API | OK | Wszystkie trasy, także `preview` i `GET /api/jobs`, wymagają sesji, a zapisy XSRF. `EndpointSecurityTests` bez wpisów anonimowych |
| 4. Ponowienia nie duplikują | OK | `PK_RecurringExpensePeriods (DefinitionId, Period)` w transakcji wystąpienia, rejestr przeżywa usunięcie wydatku, blokada `UPDLOCK, ROWLOCK`, ponowienie tylko w zadaniu w nowym zakresie DI, `RecurringDefinitionRequests` zostaje po usunięciu. Kwalifikacja jest funkcją stanu (z `PausedOn`), więc ponowiony zapis daje ten sam wynik |
| 5. Audyt finansowy | OK | Definicja `Created`/`Updated`/`Deleted` z autorem konta, wystąpienie z `AuditAuthor.AutomatedJob` (`IAuditLog.cs`) i rodzicem `RecurringExpenseDefinition` (`AuditRecord` to dopuszcza). `ExpenseAudit.Created/Updated/Deleted` mają parametr `parent`. Wszystko w jednej `IUnitOfWork`, odrzucone `409 recurring_expense_paused` niczego nie zapisuje |
| 6. Sekrety po stronie serwera | OK | Wyjątek zadania i logi tylko z liczbami i id. Komponent nie pokazuje `error` z `JobStatusDto` |
| 7. Granice modułów i warstw | OK | Wszystko w module Expenses i schemacie `expenses`. Pętla, zakresy DI, blokada SQL i rozpoznanie `1205` w Infrastructure, `RecurringSchedule` to czysta domena. Application bez `IServiceScopeFactory` (test architektury) |
| 8. Brak danych identyfikujących w dokumentach | OK | Przykłady fikcyjne |

## Risk

- Recorded: high
- Assessed: high. Zadanie bez udziału użytkownika zapisuje koszty trafiające do raportów, usunięcie definicji jest nieodwracalne, a model terminów (historia dni, przedziały wyłączeń, wstrzymanie) decyduje, które koszty minionych miesięcy powstają. Przed merge wymagany jest przegląd przez człowieka.

## Assumptions Detected

- Odpowiedzi właściciela 1–3: zapisane (Assumptions 1–3). Odpowiedź 1 realizuje rejestr i wpis dnia od bieżącego okresu, odpowiedź 2 to, że zmiana końca nie tworzy przedziałów, a `Pause` je odcina, odpowiedź 3 przedział `[PausedOn, dzień wznowienia)`. Zmiana dnia po wznowieniu na termin sprzed `PausedOn` jest zgodna z odpowiedzią 1, ale nie ma Edge Case (Non-Critical 2).
- Wstrzymana definicja nie tworzy wystąpień, a jej dnia nie można zmienić: zapisane (30). Twierdzenie, że inne zmiany wstrzymanej definicji nie dają terminów sprzed `PausedOn` bez wystąpienia, sprawdziłem dla przedłużenia, skrócenia i ponownego przedłużenia, początku na później i na wcześniej oraz kombinacji. Zachodzi, uzasadnienie przedłużenia jest nieścisłe (Non-Critical 4).
- Wznowienie tworzy najwyżej jedno wystąpienie (bieżący okres z terminem w dniu wznowienia): niezapisane wprost, ale wynika z reguł 4–5 i Assumptions 30. Tekst dialogu „Wznów” w liczbie pojedynczej jest więc poprawny.
- Termin efektywny `e(p)` w walidacji i `LastDueDate`: zapisane (15, reguła 6). Spójne z `CK_..._LastDueDate`.
- Reguła zastępcza dla okresów przed pierwszym wpisem i jej wyjątek od „dzień wcześniejszych okresów się nie zmienia”: zapisane (reguła 2, Edge Case, test).
- Model ze stanu, wyjątek v4-NC1 akceptowany: zapisane (4, 11).
- `StartMoved` z `ToDate = min(stary początek, dzisiaj)`, w tym początek później → wcześniej: zapisane (12, Edge Cases).
- Blokada, ponowienie tylko w zadaniu, `1205` przez fallback: zapisane (13). Zgodne z `UnitOfWork`.
- Zadanie 00:10, 30 min, odcięcie 400 dni, kandydaci bez wstrzymanych: zapisane (14).
- Przechowywane `LastDueDate` i `DayOfMonth`, filtr `state`: zapisane (15).
- Walidacja, UI, podgląd `GET`, filtr z parametru, idempotencja dodania: zapisane (16–20). Kolejność walidacji harmonogramu względem blokady i `409 recurring_expense_paused` jest niejawna (Non-Critical 3).
- Pozostałe (E2E w SF-21, ryzyko, ostrzeżenie o zadaniu, PRD, `nextDueDate`, skrócenie końca, kolizja rejestru, dialog „Wznów”, `version`): zapisane (21–29).
- Wysłanie bieżącego dnia z zablokowanego pola: niezapisane (Non-Critical 6).
- Migracja tylko dodaje, nowe kolumny `Expenses` nullowalne: zapisane w Storage.

## Recommendation

- Proceed as-is
