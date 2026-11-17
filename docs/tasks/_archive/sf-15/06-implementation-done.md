# Implementation done — SF-15

## Summary

Dostarczono wydatki jednorazowe i zapłatę wydatku (backend + frontend):
- **moduł Expenses:** encja `Expense`, tabela `expenses.Expenses`, `decimal(18,2)`, `rowversion`;
- **ograniczenia CHECK:** brutto ≠ 0; netto 0 dozwolone; gdy netto ≠ 0, znak brutto i |netto| ≤ |brutto|;
- **słowniki:** 7 kategorii i 4 metody;
- **stan** Planowany/Opłacony/Po terminie: jedna reguła w domenie, filtr SQL zgodny;
- **API:**
  - lista z filtrami (data kosztu, data zapłaty, kategoria, stan, znak, tekst), sortowanie z pustymi datami na końcu, stronicowanie;
  - szczegóły, dodanie z `Idempotency-Key`, edycja z wersją;
  - „Oznacz jako opłacony” (data ≤ dzisiaj, Europe/Warsaw), „Cofnij oznaczenie zapłaty”, usunięcie z wersją;
  - Problem Details z kodami;
- **audyt** każdej zmiany w tej samej jednostce pracy; usunięcie z pełnym śladem;
- **UI:** lista z filtrami (rozwijany panel na telefonie), dodanie, szczegóły z akcjami i dialogami, ujemne kwoty jako „Korekta / zwrot od dostawcy”;
- **wspólne:** `todayInWarsaw` przeniesione do `shared/format` (importy w Orders), `parseSignedPlnAmount`;
- **kontrakt dla SF-16:** opcjonalny rodzic w audycie; rejestr definicja–okres powstanie w SF-16.

Po przeglądzie kodu (`05-code-review.md`, PASS WITH ISSUES) dodano:
- test HTTP konfliktu w bazie (`409`);
- spec strony tworzenia;
- „Brak danych” na liście;
- jednokrotny komunikat 404;
- testy sortowania, pustego wyniku i błędu listy.

Ponownego przeglądu kodu nie wykonano.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Model, kategorie, brutto ≠ 0, reguły netto (decyzja właściciela: netto 0 OK) | Implemented | domena, walidacja, CHECK |
| Dodawanie (idempotencja), poprawianie (wersja), usuwanie z potwierdzeniem i audytem | Implemented | |
| „Oznacz jako opłacony”, data zapłaty w formularzu, ≤ dzisiaj | Implemented | |
| Usunięcie daty zapłaty z potwierdzeniem i audytem | Implemented | potwierdzenie tylko w UI |
| Planowany / Opłacony / Po terminie | Implemented | |
| Ujemne wydatki „Korekta / zwrot od dostawcy” | Implemented | |
| Lista z filtrami (data kosztu), kolumny dat | Implemented | |
| Data kosztu sprzed uruchomienia, późniejsza data zapłaty | Implemented | |
| E2E | Not implemented | odroczone do SF-21 (brak infrastruktury) |

## Files

Backend `api/src/Modules/Expenses/**` (Domain, Application — przypadki użycia CreateExpense, EditExpense, MarkExpensePaid, UnmarkExpensePaid, DeleteExpense, GetExpense, ListExpenses —, Infrastructure z migracją `20261106210851_Expenses`, Api), testy `api/tests/StoneFlow.Expenses.Tests/**`, `api/tests/StoneFlow.Api.IntegrationTests/Expenses/**`, frontend `app/src/app/features/expenses/**`, `app/src/testing/expenses-testing.ts`, `shared/format/pl-format.ts`, importy w `features/orders`, `app.routes.ts`, `stone-flow.slnx`; dokumenty `docs/tasks/sf-15/*`.

## Planned Units

Wszystkie jednostki z planu: Exists. `expense-form.spec.ts`: Missing.

## Database

`ExpensesDbContext`, migracja `20261106210851_Expenses` (addytywna): tabela `expenses.Expenses`, CHECK `CK_Expenses_GrossAmount` i `CK_Expenses_NetAmount`, unikalny filtrowany indeks `CreationRequestId`, indeksy `CostDate` i `PaidOn`.

## Tests

| Test | Status | Run result |
|---|---|---|
| Expenses.Tests (domena, wejście, audyt) | Exists | Passed 48/48 |
| Integracyjne Expenses (LocalDB) | Exists | Passed 31/31 |
| Regresja OrdersApiTests | Exists | Passed 39/39 |
| EndpointSecurityTests | Exists | Passed 9/9 (przed rundą poprawek) |
| Frontend | Exists | Passed 467/467 |

## Verification

| Command | Outcome |
|---|---|
| `dotnet build stone-flow.slnx -c Release -m:1` | Passed |
| `dotnet format --verify-no-changes` | Passed |
| Architecture.Tests | Passed 56/56 (przed rundą poprawek) |
| `npm test`, `lint`, `format:check` | Passed |
| `npm run build`, `typecheck` | Passed (przed rundą poprawek) |
| Pełny `dotnet test`, `test-migrate.sh` | Not run (CI) |

## Deviations

- Brak osobnego `expense-form.spec.ts`.
- `WarsawTime` w Expenses.Application jest publiczny (testy granicy doby).
- `app-shell.spec.ts` renderuje `/historia-zmian` zamiast `/wydatki`.
- Komunikat „Wydatek usunięty.” po usunięciu na liście.
- W teście audytu testowe ograniczenie z `WITH NOCHECK`.

## Additional Implementation

Additional implementation not covered by the original documents: metoda płatności ustawiana w akcji „Oznacz jako opłacony” (opisane w spec).

## Open Items

- **Human review required before merge** (ryzyko high).
- Pełny zestaw testów i `test-migrate.sh` — CI.
- E2E odroczone do SF-21.
- Z `05-code-review.md`:
  - brak `expense-form.spec.ts` (`busy`, błąd brutto 0, przypięcie błędów API do pól);
  - błąd walidacji „Oznacz jako opłacony” zawsze pod polem daty;
  - `today` liczone raz przy otwarciu formularza;
  - brak przeniesienia fokusu przy otwarciu i zamknięciu formularzy w szczegółach;
  - `ExpenseInput.cs` duży.
- Ponowny przegląd kodu po poprawkach nie został wykonany.
