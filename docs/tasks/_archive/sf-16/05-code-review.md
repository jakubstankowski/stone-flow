# Code review — SF-16

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Funkcje domeny są zgodne z regułami 1–8 spec v6. Dotyczy to dni 29–31, ostatniego dnia, zakresu włącznie, przedziałów `[From, To)`, historii dni z regułą dla okresów przed pierwszym wpisem, terminu efektywnego, `LastDueDate` i stanu. Rejestr `PK_RecurringExpensePeriods`, blokada `UPDLOCK, ROWLOCK` na początku każdego przypadku użycia, ponowienie tylko w zadaniu i wycofanie przez `commitWhen` realizują niezmiennik 4. Audyt (autor `AutomatedJob`, rodzic = definicja, rodzic w zmianach i usunięciu wystąpienia) realizuje niezmiennik 5. Żaden niezmiennik nie jest naruszony. Dwa problemy trzeba jednak poprawić przed merge. Pierwszy: tłumaczenie numeru 547 bez sprawdzania nazwy ograniczenia zamienia każde naruszenie `CHECK` na „brak definicji”, więc zadanie może po cichu nie tworzyć kosztów. Drugi: „Spróbuj ponownie” w UI wysyła nieaktualne dane z formularza. Brakuje też kilku testów, które wymienia sekcja Verification.
- Risk: high. Przed merge wymagany jest przegląd przez człowieka.

## Project Invariant Violations

None.

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | `Expense.CreateOccurrence` odrzuca `PaidOn` (`Expense.cs`, `CreateOccurrence`). `RecurringExpenseDefinition.CreateOccurrence` ustawia `PaidOn: null` (`RecurringExpenseDefinition.cs:198`). Test: `Occurrence_is_a_planned_cost_…`, `Occurrence_requires_no_payment_date` |
| 2. Pieniądze jako decimal w backendzie | OK | `decimal(18,2)`, parsowanie przez `ExpenseInput.ParseAmount`, `CHECK` kwot. Frontend tylko formatuje |
| 3. Dostęp egzekwowany w API | OK | Grupa bez `AllowAnonymous`. `EndpointSecurityTests` sprawdza 401 i XSRF dla wszystkich tras z `EndpointDataSource`. `404` z blokady |
| 4. Ponowienia nie duplikują | OK | PK rejestru w tej samej transakcji co wystąpienie. Blokada `RecurringDefinitionLock.cs:18`. Rejestr zostaje po usunięciu wydatku. `RecurringDefinitionRequests` bez FK. Patrz jednak Correctness 1 (547) |
| 5. Audyt finansowy | OK | Audyt w tej samej `IUnitOfWork`, `Updated` przy pause/resume/dayOfMonth, `Deleted` przed usunięciem. Brakuje testu „błąd audytu wycofuje całość” (Test Gaps 1) |
| 6. Sekrety po stronie serwera | OK | Wyjątek zadania zawiera tylko liczbę (`RecurringExpensesJob.cs:60`), logi tylko id. UI nie pokazuje `error` |
| 7. Granice modułów i warstw | OK | Wszystko w schemacie `expenses`. Zakresy DI, pętla i rozpoznanie 1205 są w Infrastructure. Domena bez EF |
| 8. Brak danych identyfikujących | OK | Dane testowe fikcyjne |

## Conventions Violations

### Critical (must fix before merge)

1. `api/src/Modules/Expenses/StoneFlow.Expenses.Infrastructure/Persistence/RecurringExpenseStore.cs:94`: `catch … SqlException { Number: 547 }` → `OccurrenceSaveStatus.DefinitionMissing` bez sprawdzenia nazwy ograniczenia.
   - SQL Server zgłasza 547 także dla naruszenia `CHECK`. Test `Database_constraints_reject_…` sam to potwierdza: każde naruszenie `CHECK` daje 547.
   - Skutek: naruszenie `CK_Expenses_*`, `CK_Expenses_Recurring` albo `CK_RecurringExpensePeriods_Period` przy wstawianiu wystąpień wygląda jak „brak definicji”.
     - W zadaniu: `GenerateRecurringOccurrences.cs` (gałąź `_ => DefinitionMissing`), a w zadaniu to nie błąd. Przebieg kończy się `Succeeded`, ostrzeżenie się nie pokazuje, a koszty są po cichu zaniżone. Każdy kolejny przebieg powtarza to samo.
     - U użytkownika: `RecurringChange.cs:132` daje `404 „Nie znaleziono…”` zamiast błędu programu.
   - Narusza to konwencję „Do not swallow … errors; catch only to translate a known constraint violation” i spec (Storage → Tłumaczenia: „547 (FK…)”, „Inne naruszenia to błędy programu”).
   - Tłumaczenie trzeba zawęzić do `FK_RecurringExpensePeriods_RecurringExpenseDefinitions_DefinitionId`, np. po nazwie w komunikacie albo przez sprawdzenie istnienia definicji po błędzie.
2. `app/src/app/features/expenses/expense-create-page.ts:84-95` i `:128`: `retryRecurring()` wysyła zapamiętane `lastRecurringInput`, a nie bieżący model formularza.
   - Po `503` dane zostają w formularzu (spec „Współbieżność i błędy”), więc użytkownik może je poprawić i kliknąć „Spróbuj ponownie”.
   - Zapisze się wtedy starsza wersja definicji (kwota, dzień, początek), a ekran pokazuje nowszą. To błąd zapisu danych finansowych.
   - Przycisk nie jest blokowany, gdy trwa zapis z „Zapisz”, więc możliwe są dwa równoległe POST-y z tym samym kluczem. Backend to obsłuży, ale UI nie powinien do tego dopuszczać.
3. `app/src/app/features/expenses/recurring-expense-details-page.ts:249-259`: ten sam problem przy edycji.
   - `again = async () => { await call(); }` ponawia `update` z `input` przechwyconym przy pierwszym zapisie.
   - Wrapper porzuca wynik `{ error }`, więc `400` (np. `endsOn`) albo inny błąd z ponowienia nie trafia ani do pól, ani do alertu.

### Non-Critical (should fix)

1. `api/src/Modules/Expenses/StoneFlow.Expenses.Application/PreviewRecurringExpense/PreviewRecurringExpense.cs:106-108`: liczby `droppedDueCount`, `createdAfterEndCount` i `createdBeforeStartCount` liczy handler. Konwencja: „A handler does not take over domain rules”. Należą do `RecurringSchedule`, gdzie mają test jednostkowy.
2. `api/src/Modules/Expenses/StoneFlow.Expenses.Infrastructure/Persistence/RecurringExpenseReader.cs:93`: `null!` w projekcji DTO, a potem `with { Schedule = … }`. To `!` bez ustalonego niezmiennika. Lepiej zrobić projekcję do typu pośredniego.
3. `RecurringExpenseDefinition.cs:140` i `PreviewRecurringExpense.cs:105`: `edit.Next!`. Niezmiennik (brak naruszenia ⇒ `Next` ≠ null) jest ustalony, ale typ go nie wyraża. Drobne.
4. Frontend: reguły tekstów i porównań (`next <= today`) leżą w plikach komponentów: `recurring-expense-details-page.ts:41-73`, `recurring-expenses-list-page.ts:28`, `recurring-job-notice.ts:74-80`. Plan każe trzymać je w funkcjach modeli lub serwisów (rules.md: „No business logic in Angular components”). `today` liczone raz przy konstrukcji (`recurring-expense-details-page.ts:122`) jest nieaktualne po północy.
5. Odstępstwa zgłoszone przez autora (ocena):
   - Części domeny w jednym pliku (`RecurringDefinitionParts.cs`): akceptowalne („Small related types may share one file”).
   - `OwnsMany` dla dni i wyłączeń: akceptowalne. Blokada i wersja definicji obejmują części, bo każda zmiana części zmienia też kolumnę definicji (`DayOfMonth`, `StartsOn`, `PausedOn`), więc `UPDATE … WHERE Version` zawsze się wykonuje.
   - Podgląd nowej definicji zwraca 0 zamiast `null`: akceptowalne, bo UI pokazuje linie tylko przy `> 0`.
   - Audyt wystąpień pojedynczo (`OccurrenceGenerator.cs:52-58`, `IAuditLog.RecordAsync` robi `SaveChanges` na wpis): odstępstwo od „SaveChanges na wsad” (Behavior → Tworzenie wystąpień, pkt 4). Definicja od 2000 r. to około 320 dodatkowych zapytań w transakcji. Test < 30 s według autora przechodzi. Do zapisania w 06.
   - 2601/2627 bez nazwy indeksu (`RecurringExpenseStore.cs:90`): w praktyce równoważne. Nowe wydatki mają nowe id, `UX_Expenses_CreationRequestId` jest filtrowany, a `UX_RecurringExpensePeriods_ExpenseId` dostaje nowe id. Akceptowalne, inaczej niż przy 547.
   - Komunikat wersji „wydatku” (`ExpenseVersion.Validate`): drobne. Tekst mówi o wydatku, a nie o wydatku cyklicznym.
   - `internal` pomocniki w `ExpensesEndpoints.cs`: akceptowalne.
   - Brak `min`/`max` na polu dnia (NG8022, `expense-form.html:58-65`): akceptowalne. Zakres egzekwują walidacja UI i API.
   - Filtr `wydatekCykliczny` czyści inne filtry (`expenses.service.ts:264-268`): uzasadnione, ale nieopisane w spec. Link „Wszystkie wydatki” (`expenses-subnav.ts:244`) nie zdejmuje filtra definicji.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Model terminów, reguły 1–3 (29–31, ostatni dzień, przełom roku) | Covered | `RecurringPeriod.DateOn`, `RecurringSchedule.DayOf/DueDate`; testy `RecurringScheduleTests` |
| Reguła 4: kwalifikacja (zakres, wyłączenia, wstrzymanie, rejestr), należne na D, kandydaci od `StartsOn` do `min(D, EndsOn)` | Covered | `RecurringSchedule.cs:56-90` |
| Reguła 2: historia dni (wpis od bieżącego okresu, usuwanie późniejszych, okres przed pierwszym wpisem), blokada zmiany dnia na wstrzymanej | Covered | `RecurringScheduleState.WithSchedule`, `RecurringSchedule.Edit` |
| Reguła 5: przedziały `Pause` i `StartMoved` (`min(stary, dzisiaj)`), puste niezapisywane | Covered | `RecurringScheduleState.cs:86-116` |
| Reguła 6: `nextDueDate`, następny po zapisie, termin efektywny, `LastDueDate` | Covered | Limit 1200 okresów bez końca |
| Reguła 7: stan i filtr `state` w SQL | Covered | `RecurringExpenseReader.cs:186-192`; test zgodności |
| Reguła 8 (odpowiedzi 1–3, v2, v3, v4, NC2, NC4) | Covered | Testy domeny i część HTTP |
| Blokada i kolejność kroków (edycja NC3, wstrzymanie z generowaniem przed zmianą, wznowienie, usunięcie, komenda zadania) | Covered | `RecurringChangeRunner`, handlery |
| Generator: wsady po 100, rejestr, audyt `AutomatedJob` z rodzicem, kolizja → `DuplicatePeriod` | Partial | Audyt pojedynczo, 547 zbyt szeroko (Critical 1) |
| Dodanie z idempotencją (powtórzenie z zapisaną liczbą, `recurring_expense_deleted`, `idempotency_key_reused`, wyścig) | Covered | `CreateRecurringExpense.cs` |
| Zadanie `recurring-expenses` (00:10, 30 min, faza odczytu, zakres na definicję, jedno ponowienie, błędy zbierane, anulowanie) | Covered | `RecurringExpensesJob.cs` |
| Podgląd (`occurrencesToCreate`, `beforeLaunchCount`, `nextDueDate`, `dropped…`, `created…`, `paused`, `409` przy wstrzymanej) | Covered | |
| Endpointy, kody Problem Details, `:guid`, nazwy | Covered | |
| Zmiany SF-15 (filtr, `recurringDefinitionId`, `recurring`, rodzic audytu w edycji, zapłacie i usunięciu) | Covered | |
| Migracja addytywna, kaskady, `CHECK`, indeksy | Covered | `20261108102216_RecurringExpenses.cs` |
| PRD §5 | Covered | Tekst zgodny z „Behavior → PRD” |
| UI: formularz, podgląd, lista, szczegóły, dialogi, zablokowany dzień, NC6, oznaczenie i filtr | Covered | |
| UI: ponowienie po `503` | Partial | Critical 2 i 3 |
| `recurring-job-notice` (jeden odczyt, reguła 25 h, stan ostrożny, `role="status"`) | Partial | Uwagi w Correctness 4 i 5 |
| E2E | Missing (zgodnie ze spec) | Odroczone do SF-21 |

## Plan Deviations

- Pliki domeny `RecurringExpenseDay.cs`, `RecurringExpenseExclusion.cs`, `RecurringExpensePeriod.cs` i `RecurringDefinitionRequest.cs` połączone w `RecurringDefinitionParts.cs`. Dodany `RecurringScheduleState.cs`.
- `RecurringChangeResult.cs` jest w `Recurring/RecurringChange.cs` razem z `RecurringChangeRunner`.
- `ExpensesApiTests.cs` (plan: modify) bez zmian. Testy filtra i rodzica audytu są w `RecurringExpensesApiTests.cs`.
- Audyt wystąpień pojedynczo zamiast wsadu.
- Frontend: brak `min`/`max` na polu dnia. Filtr definicji czyści inne filtry.

## Null Safety Issues

- `RecurringExpenseReader.cs:93`: `null!` (Non-Critical 2).
- `RecurringExpenseReader.cs:105`: `schedules[id]` po osobnym zapytaniu. Definicja usunięta między zapytaniami daje `KeyNotFoundException`, czyli `500` zamiast `404`.
- `RecurringExpenseReader.cs:150-179` z `RecurringScheduleState.cs:34-37`: nagłówki, dni, wyłączenia i rejestr to cztery osobne zapytania bez wspólnej migawki. Definicja usunięta po zapytaniu nagłówków ma pustą historię dni, a konstruktor stanu rzuca `ArgumentException`.
  - W fazie odczytu zadania wywraca to cały przebieg (`failed`, ponowienie po 15 min).
  - Na liście daje `500`.
  - Przy jednym użytkowniku to mało prawdopodobne, ale łatwe do uniknięcia: pominąć nagłówek bez dni.

## Correctness Issues

1. (Krytyczne) 547 tłumaczone jako „brak definicji” także dla `CHECK`: `RecurringExpenseStore.cs:94` (Conventions Critical 1).
2. (Krytyczne) Ponowienie w UI z nieaktualnymi danymi i porzucony wynik błędu: `expense-create-page.ts:84-95`, `recurring-expense-details-page.ts:249-259` (Conventions Critical 2 i 3).
3. (Ważne) Wyścig odczytów harmonogramu w `RecurringExpenseReader.SchedulesAsync`, opisany w Null Safety.
4. (Drobne) `recurring-job-warning.ts:45-47`: `running` bez sukcesu nigdy nie ostrzega, także po wcześniejszych błędach (`consecutiveFailures > 0`). Spec zwalnia z ostrzeżenia tylko pierwszy przebieg. Podobnie ponowienie `running` po `failed` przy świeżym sukcesie chowa ostrzeżenie. Okno jest krótkie, bo `MaxDuration` to 30 min.
5. (Drobne) `recurring-job-notice.ts:152`: stan ostrożny dla każdego błędu, także `4xx`. Spec mówi o sieci i `5xx`.
6. (Drobne) `recurring-expense-preview.ts:111-163`: brak anulowania starszych żądań. „Ostatnie wygrywa” działa przez numer sekwencji. Przez czas opóźnienia i odpowiedzi widać podgląd dla poprzednich wartości, bez stanu „nieaktualny”.
7. (Drobne) `recurring-expense-details-page.html:80`: „Edytuj” nie jest blokowane w trakcie akcji `pause`, `resume` i `delete`.
8. (Drobne) `expense-form.ts:113`, `:369-372`: `action` jest teraz opcjonalne. Formularz bez `action` i bez `recurring` po „Zapisz” po cichu nic nie robi. To słabsza ochrona typów dla SF-15, ale regresji nie ma.

## Test Gaps

1. Brak testu integracyjnego „błąd audytu wycofuje definicję razem z wystąpieniami” (spec: Invariant impact 5 i Acceptance Criteria „błąd audytu wycofuje całość”). SF-15 ma odpowiednik `ExpensesApiTests.Audit_failure_rolls_back_the_expense_change`.
2. Brak testu stałej liczby zapytań fazy odczytu zadania i listy (Verification → wydajność). Brak zgłosił autor.
3. Brak testu „zmiana kwoty równoległa z przebiegiem” (Verification → współbieżność). Brak zgłosił autor.
4. Brak testu, że naruszenie `CHECK` przy wstawianiu wystąpień kończy przebieg błędem. Wykryłby Critical 1.
5. Anulowanie: test `Cancelled_run_stops_after_the_current_definition…` przerywa między definicjami. Nie sprawdza wycofania jednostki przerwanej w trakcie („po anulowaniu brak wierszy”). Częściowo pokrywa to ogólny `AuditLogTests.Cancellation_rolls_back_data_and_audit`.
6. `CHECK` bez testu: `CK_RecurringExpenseDefinitions_NetAmount`, `CK_RecurringDefinitionRequests_CreatedOccurrences`, `CK_RecurringExpenseDays_DayOfMonth`.
7. Frontend:
   - `busy` przycisków i blokada powtórnego kliknięcia;
   - klucz trybu cyklicznego różny od jednorazowego;
   - `409 idempotency_key_reused` na stronie dodawania;
   - ponowienie po zmianie formularza i ponowienie `update` zakończone błędem;
   - `running` po błędach w `recurringJobWarning`;
   - skeleton listy i szczegółów;
   - `409 already_paused`, `not_paused`, `ended` i `paused` na stronie;
   - `droppedDueCount` na stronie edycji;
   - komunikat po dodaniu na szczegółach;
   - `aria-current` podsekcji;
   - trasa `/wydatki/cykliczne/:id` w `app.routes.spec.ts`;
   - „Wyczyść filtry” dla filtra definicji.

## Code Smells

- `RecurringExpenseDefinition.cs:189-191`: `CreateOccurrence` buduje `Schedule` (sortowanie i kopie list) przy każdym wystąpieniu. Przy kilkuset okresach to zbędne alokacje. Drobne.
- `PreviewRecurringExpense.cs:72`: `state` liczone przed `CheckNew`, które liczy je ponownie. Duplikacja.
- `recurring-expense-details-page.html:158-160`: etykieta „Utworzone wystąpienia” jest powtórzona w `dt` i w treści, więc czytnik ekranu przeczyta ją dwa razy.
- `expenses-subnav.ts:245-248`: `border-transparent` i `border-(--sf-accent-text)` ustawiają tę samą właściwość, a wynik zależy od kolejności w CSS. Podkreślenie aktywnej zakładki trzeba sprawdzić wizualnie (niepewne).
- `recurring-expenses.service.ts:43`: komentarz mówi o „503”, a kod mapuje `kind === 'network'`.

## Recommendation

- Fix critical issues before merge: zawęzić tłumaczenie 547, poprawić ponowienie w UI i dodać testy z Test Gaps 1 i 4. Ryzyko high: przed merge wymagany jest przegląd przez człowieka.

Weryfikacja recenzenta: `dotnet test --project api/tests/StoneFlow.Expenses.Tests -c Release --no-build`: 206/206 (na binariach z ostatniego buildu autora). Pozostałe wyniki (build, format, integracyjne, frontend) pochodzą z raportu autora. Recenzent ich nie powtarzał.
