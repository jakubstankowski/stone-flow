# Code review — SF-15

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Implementacja pokrywa specyfikację (z notką poprawek z 06.11.2026) i plan. Żaden
  inwariant nie jest naruszony. Stan „Planowany”/„Opłacony”/„Po terminie” liczy jedna funkcja domeny
  (`ExpensePayment.Compute`), a SQL odtwarza ją tylko w predykacie filtra `state`, z testem
  zgodności. Reguły netto (0 dozwolone, znak przy ≠ 0, |netto| ≤ |brutto|) są w domenie, walidacji
  i `CHECK`. Wersja jest sprawdzana przed regułą zapłaty. Każda zmiana zapisuje audyt w tej samej
  jednostce pracy. Uwagi dotyczą luk w testach (tłumaczenie `DbUpdateConcurrencyException`,
  strona tworzenia, komponent formularza, sortowanie po dacie zapłaty i kwocie), pokazywania braku
  wartości na liście jako „—” zamiast „Brak danych” i kilku drobiazgów w UI. Nic z tego nie
  blokuje merge.
- Risk: high. Przed merge wymagany jest przegląd przez człowieka, niezależnie od wyniku.

## Project Invariant Violations

None.

| Invariant | Status | Gdzie sprawdzone |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy (plan ≠ wypływ) | OK | Stan zależy wyłącznie od `PaidOn` (`ExpensePayment.cs:16-19`). Przyszła data zapłaty jest odrzucana przy dodaniu, poprawie i „Oznacz jako opłacony” (`ExpenseInput.cs:88-91`, `:152-155`), a „dzisiaj” pochodzi z `TimeProvider` w Europe/Warsaw (`WarsawTime.cs`). Zapis nie tworzy innych rekordów pieniężnych (test `Create_does_not_create_any_money_record…`). Metoda płatności nie zmienia stanu. |
| 2. Decimal i precyzja w backendzie | OK | `decimal(18,2)` (`ExpenseConfiguration.cs:48-49`), parser tekstowy bez zaokrągleń, skala > 2 daje błąd (`ExpenseInput.cs:196-224`). Frontend operuje na tekście (`parseSignedPlnAmount`, `isNegativeAmount`), bez `Number`. Porównanie |netto| ≤ |brutto| robi tylko API. |
| 3. Dostęp egzekwowany w API | OK | Brak `AllowAnonymous`. Polityka domyślna i globalny CSRF. Test `Anonymous_requests_and_writes_without_csrf_are_rejected` sprawdza `401` dla 7 tras i `csrf_invalid` dla POST, mark-paid i DELETE. Handler sprawdza istnienie zasobu (`404`). |
| 4. Ponowienia nie duplikują | OK | `UX_Expenses_CreationRequestId` (filtrowany, unikalny), ponowny odczyt po kluczu po 2601/2627 (`CreateExpense.cs:59-74`, `ExpenseStore.cs:257-261`). Testy: powtórzenie daje `200`, inne dane `409` z `expenseId`, cztery równoległe żądania dają jeden wiersz i jeden wpis audytu. |
| 5. Audyt finansowy | OK | `Created`/`Updated`/`Deleted` w `IUnitOfWork` (`CreateExpense.cs:98`, `ExpenseChange.cs:94`, `DeleteExpense.cs:48`). Usunięcie zapisuje wszystkie niepuste pola jako stare wartości. Brak zmian oznacza brak wpisu. Test wycofania przy błędzie audytu obejmuje dodanie, zapłatę i usunięcie. |
| 6. Sekrety po stronie serwera | Not applicable | Brak sekretów i integracji. Problem Details nie zawierają danych wydatku. |
| 7. Granice modułów i warstw | OK | Expenses referencjonuje tylko `StoneFlow.Shared.*` (strefa z `Shared.Application.Scheduling.WarsawTimeZone`). Domena nie zależy od EF ani HTTP. Rejestracja w `Modules.cs` testów architektury. Orders zmienia tylko importy frontendu. |
| 8. Brak danych identyfikujących w dokumentach | OK | Dane testowe fikcyjne („Dostawca A”, „Właściciel”). |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `app/src/app/features/expenses/expenses-list-page.ts:39`, `:50`, `:56`: brak kontrahenta,
   terminu i daty zapłaty na liście jest pokazywany jako „—”. Design guide (formaty: „brak wartości
   jako »Brak danych«”) i konwencje („missing values as »Brak danych«”, wyświetlanie przez
   `shared/format/`) wymagają tekstu „Brak danych”. `formatDate(null)` zwraca go już sam. W
   szczegółach jest poprawnie (`expense-details-page.html:415`, `:432`, `:436`), więc ta sama
   wartość wygląda inaczej na liście i w szczegółach.
2. `api/src/Modules/Expenses/StoneFlow.Expenses.Infrastructure/DependencyInjection.cs:337`:
   `ExpenseChangeRunner` (typ Application) jest rejestrowany w Infrastructure. Infrastructure jest
   tu korzeniem kompozycji modułu, więc to dopuszczalne, ale warto o tym wiedzieć przy SF-16, który
   doda kolejnych wywołujących.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Model wydatku, słowniki kategorii i metod (PRD §7) | Covered | `ExpenseVocabulary.cs`, `ExpenseCodes.cs`, `expense-models.ts` |
| Brutto ≠ 0; netto 0 dozwolone, znak przy ≠ 0, \|netto\| ≤ \|brutto\| | Covered | Domena `ExpenseAmounts.cs:37-67`, walidacja `ExpenseInput.cs:93-124`, `CHECK` w `ExpenseConfiguration.cs:31-34` i migracji, UI `expense-form-model.ts:154-190` (bez porównania wartości) |
| Zakres `decimal(18,2)`, skala ≤ 2 bez zaokrągleń | Covered | `ExpenseAmounts.MaxAbsolute`, `AmountError.Scale` |
| Data zapłaty ≤ dzisiaj (Europe/Warsaw, `TimeProvider`) | Covered | Test granicy 23:30 UTC (jednostkowy i integracyjny) |
| Dodanie z `Idempotency-Key` (201/200/409, wyścig) | Covered | |
| Poprawa z wersją, bez zmian bez audytu i ze starą wersją | Covered | `ExpenseChange.cs:83-86`; test `Edit_clears_payment_date…` |
| „Oznacz jako opłacony” (data, metoda zastępuje albo czyści) | Covered | |
| „Cofnij oznaczenie zapłaty” (metoda zostaje) | Covered | |
| Kolejność 409: wersja przed regułą domeny | Covered | `ExpenseChange.cs:69-81`; test `Stale_version_gives_conflict_before_the_payment_rule…` |
| Usunięcie fizyczne z pełnym `Deleted`, potem 404 | Covered | |
| Stan z jednej funkcji domeny, filtr SQL zgodny | Covered | `ListExpenses.cs:129-131`, `GetExpense.cs:41`, `ExpenseReader.cs:181-187`; test `State_filter_matches_the_state_computed_by_the_domain` |
| Lista: filtry (jednostronne zakresy, kategorie OR, stan, znak, tekst z escapowaniem) | Covered | |
| Lista: sortowanie z pustymi datami na końcu, remis po `Id` | Covered | `ExpenseReader.cs:209-219`. Test obejmuje tylko `dueDate` (patrz Test Gaps) |
| Stronicowanie w bazie, maks. 100, strona poza zakresem | Covered | `ExpenseReader.cs:93-97` liczy `skip` jako `long`, więc duży `page` nie przepełnia |
| Tłumaczenie `DbUpdateConcurrencyException` i 2601/2627 z testem kodu HTTP | Partial | Tłumaczenie jest (`ExpenseStore.cs:274-277`, `:291-294`), ale żaden test nie dochodzi do konfliktu na poziomie bazy. Wcześniejsze sprawdzenie wersji w handlerze przechwytuje każdy sekwencyjny konflikt |
| Migracja addytywna, kontekst już w `contexts.sh` | Covered | Migracja tworzy tylko tabelę i indeksy. Snapshot ma wyłącznie dodane linie (+95). `deploy/scripts/contexts.sh` zawiera `ExpensesDbContext` |
| Kontrakt SF-16 (`CreationRequestId` nullowalny, rodzic audytu opcjonalny) | Covered | `CK_Expenses_CreationRequest`, `ExpenseAudit(…, parent = null)` z testem |
| Frontend: lista, filtry w panelu, stany pusty/filtr/błąd | Covered | `filteredEmptyTitle` zależny od filtra daty |
| Frontend: formularz (sekcje, podpowiedzi, ujemna kwota z opisem, `busy`) | Covered | |
| Frontend: szczegóły, akcje zależne od stanu, dialogi z treścią ze spec | Covered | |
| Potwierdzenie przy wyczyszczeniu daty zapłaty w formularzu | Covered | `expense-form.ts:245-249` używa dialogu cofnięcia |
| Konflikt: „Wczytaj ponownie”, dane zostają | Covered | |
| `todayInWarsaw` w `shared/format`, importy Orders, `parseSignedPlnAmount` | Covered | Wszystkie pliki Orders wymienione w spec zaktualizowane |
| Komunikaty sukcesu w `aria-live` | Covered | |
| E2E | Deferred | Odroczone do SF-21 (spec, Assumptions 18) |

## Plan Deviations

1. Brak `app/src/app/features/expenses/expense-form.spec.ts` (plan, Frontend Files: „`expense-form.ts`,
   `.html` (+ spec)”, test F-7). Część F-7 przejął `expense-details-page.spec.ts:163` (potwierdzenie
   wyczyszczenia daty zapłaty). Błąd „brutto 0 zł” na poziomie komponentu i `busy` nie mają testu
   komponentu, a zero brutto ma tylko test modelu (`expense-form-model.spec.ts:18`).
2. `WarsawTime` w `Expenses.Application` jest publiczny. Plan nie określał widoczności. Wszystkie
   typy Application w module są publiczne, a inny moduł nie może referencjonować
   `Expenses.Application` (testy architektury), więc to odstępstwo nie ma skutków.
3. `app/src/app/layout/app-shell.spec.ts:57`: test nagłówka powłoki renderuje `/historia-zmian`
   zamiast `/wydatki`, bo lista wydatków wysyła teraz żądanie. Test dotyczy powłoki, a nagłówek
   `/wydatki` sprawdza `app.routes.spec.ts:85-111` (test „loads the expenses section”). Odstępstwo akceptowalne.
4. Komunikat po usunięciu to „Wydatek usunięty.” (`expenses.service.ts:278`), a spec podaje go bez
   kropki. Pozostałe komunikaty sukcesu też kończą się kropką, więc to spójne. Bez wpływu.
5. `ExpensesApiTests.cs:302`: testowe ograniczenie `CK_Test_RejectAll` dodano z `WITH NOCHECK`. To
   konieczne, bo w bazie testu jest już wpis `Created`, który ograniczenie by odrzuciło. Dotyczy
   wyłącznie bazy jednego testu. Akceptowalne.
6. Weryfikacja: autor zgłasza, że regresja `OrdersApiTests` nie została ukończona (brak pamięci).
   Backend Orders nie jest zmieniony (zmieniają się tylko importy `todayInWarsaw` we frontendzie),
   więc ryzyko jest niskie. Mimo to test powinien przejść przed merge.

## Null Safety Issues

None.

`!` występuje tylko po ustalonym niezmienniku: `CreateExpense.cs:69` (wynik bez `Created` i
`Existing` zawsze ma `Duplicate`) oraz `ExpensesEndpoints.cs:299`, `:302`, `:358` (wersja i `Id`
przy `Created`/`Repeated`/`Saved`).

## Correctness Issues

1. `app/src/app/features/expenses/expense-details-page.ts:248-251`: przy `404` po akcji (wydatek
   usunięty w innej karcie) ekran pokazuje naraz alert „Nie znaleziono wydatku. Mógł zostać
   usunięty.” i pusty stan z tym samym tekstem (`expense-details-page.html:318`). Czytnik ekranu
   ogłosi komunikat dwa razy. Drobne.
2. `app/src/app/features/expenses/expense-details-page.ts:210-213`: błąd walidacji z „Oznacz jako
   opłacony” jest przypinany do pola daty zapłaty niezależnie od klucza. Błąd `paymentMethod` albo
   `version` pojawi się więc pod „Data zapłaty”. Drobne, bo UI ogranicza metodę do listy.
3. `app/src/app/features/expenses/expense-form.ts:99` i `expense-paid-form.ts:29`: `today` liczone
   raz przy tworzeniu komponentu. Formularz otwarty przez północ odrzuca w UI dzisiejszą datę, a
   API ją przyjmie. Drobne, bo API pozostaje źródłem prawdy.
4. Fokus (dostępność): po wejściu w tryb „Edytuj” albo „Oznacz jako opłacony” i po zamknięciu
   formularza (`expense-details-page.ts:127-147`, `:233-241`) fokus nie jest przenoszony. Kliknięty
   przycisk znika z DOM, więc fokus spada na `body`. Dialogi zwracają fokus poprawnie
   (`app-confirm-dialog`). Spec wymaga pełnej obsługi klawiaturą, a ten przypadek ją utrudnia.
   Do sprawdzenia w przeglądzie ręcznym.

Nie znalazłem błędów w logice domeny, kolejności kodów, transakcjach ani filtrach SQL.

## Test Gaps

1. **Tłumaczenie `DbUpdateConcurrencyException` → `409` bez testu HTTP** (`ExpenseStore.cs:274-277`,
   `:291-294`). Spec („Każde tłumaczenie ma test sprawdzający kod HTTP”) wymaga takiego testu. Bez
   niego fallback `DbException`/`500` mógłby zamaskować regresję. Testy konfliktu trafiają tylko w
   sprawdzenie wersji w handlerze (`ExpenseChange.cs:69`, `DeleteExpense.cs:43`). Potrzebny jest
   test, który zmienia wiersz między `LoadAsync` a `SaveAsync`, np. przez podmieniony store albo
   `UPDATE` w bazie z poziomu testu.
2. **Strona tworzenia bez testu.** Spec (Verification) wymaga testów stron „listy, tworzenia i
   szczegółów (…, `busy`)”, a `expense-create-page` nie ma speca: brak testu `keyReused`, przejścia
   do szczegółów, „Dodaj kolejny wydatek” z nowym kluczem i zachowania klucza po błędzie.
3. **Formularz bez testu komponentu** (patrz Plan Deviations 1): brak testu `busy` (blokada
   podwójnego wysłania w UI), opisu ujemnej kwoty w formularzu i przypinania błędów API do pól.
4. **Sortowanie:** test obejmuje tylko `dueDate` (`ExpensesApiTests.cs:379-395`). Brak testu
   `paidOn` z pustymi na końcu w obu kierunkach i sortowania po `grossAmount`.
5. **Lista (frontend):** `expenses-list-page.spec.ts` ma dwa testy. Brak testu pustego wyniku
   filtrów (oba teksty, w tym „Brak wydatków w wybranym okresie”), stanu błędu z „Spróbuj ponownie”
   i komunikatu po usunięciu.
6. Drobne: brak testu `Idempotency-Key` podanego wielokrotnie (`ExpensesEndpoints.cs:372`),
   wyszukiwania z `[` i przyjęcia `9999999999999999.99` przez API i bazę (jest tylko test
   jednostkowy zakresu).
7. Test konfliktu w UI (`expense-details-page.spec.ts:148-161`) sprawdza, że formularz istnieje, ale
   nie sprawdza, że wpisana zmiana została w polu.

## Code Smells

1. `api/src/Modules/Expenses/StoneFlow.Expenses.Application/ExpenseInput.cs`: plik zawiera
   `ExpenseInput`, `ExpensePaidInput`, `ExpenseVersion`, `ExpenseFieldKeys` i `ExpenseMessages`
   (390 linii). Konwencje dopuszczają małe powiązane typy w jednym pliku, ale `ExpenseVersion` i
   `ExpenseMessages` są używane przez wszystkie przypadki użycia. Rozważyć osobne pliki. Drobne.
2. `ExpenseChangeResult` służy też usunięciu, choć wyniki `AlreadyPaid`/`NotPaid` go nie dotyczą
   (`DeleteExpense.cs:9`). To świadome uproszczenie, bez wpływu na działanie.
3. Świadoma, zapisana duplikacja z Orders (`FieldErrors`, parser kwot) zgodna ze spec
   (Assumptions 16). Bez uwag.

## Recommendation

- Fix critical issues before merge: nie dotyczy, brak krytycznych. Zalecane przed merge:
  - test tłumaczenia `DbUpdateConcurrencyException` (Test Gaps 1);
  - test strony tworzenia (Test Gaps 2);
  - „Brak danych” na liście (Conventions 1);
  - ukończenie regresji `OrdersApiTests`.

  Pozostałe uwagi można przenieść do otwartych punktów w `06-implementation-done.md`.
- Ryzyko `high`: przed merge wymagany jest przegląd przez człowieka. Powinien objąć scenariusze
  ręczne ze spec (klawiatura, telefon, dwie karty) i kod reguły stanu, `CHECK` oraz audytu `Deleted`.

## Weryfikacja przeglądu

Uruchomione przez recenzenta (bez przebudowy, na istniejących binariach Release):

- `dotnet test --project api/tests/StoneFlow.Expenses.Tests -c Release --no-build`: 48/48.
- `STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;…' dotnet test --project
  api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-namespace "*Expenses*"`:
  30/30.

Pozostałe wyniki (build, format, Architecture 56/56, EndpointSecurity 9/9, frontend 460/460, lint,
build, typecheck, prettier) zgłosił autor. Recenzent ich nie powtarzał.
