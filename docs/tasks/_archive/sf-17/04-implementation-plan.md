# Implementation Plan — SF-17

Tryb: **fullstack**. Zadanie dodaje tabelę, API i UI w Expenses, pierwszy odczyt w Reporting oraz
nowy kontrakt międzymodułowy (`ICashOperationSource`). Plany backendu i frontendu napisał jeden
autor po kolei: najpierw backend, potem frontend z kontraktem wyprowadzonym ze spec (nie z planu
backendu). Oba porównano w „Integration Check”. Zadanie nie jest dzielone na części: migracje są
addytywne, backend wdraża się przed frontendem, a całość mieści się w jednym przeglądzie
(ryzyko `high`, przegląd człowieka przed merge).

## Scope

- **Expenses:** encja `OwnerWithdrawal`, tabela `expenses.OwnerWithdrawals`, przypadki użycia
  (podpowiedzi osób, szczegóły, dodanie z `Idempotency-Key`, poprawa i usunięcie z wersją), audyt
  w tej samej jednostce pracy, endpointy `/api/owner-withdrawals`, implementacja
  `ICashOperationSource` dla opłaconych wydatków i wypłat.
- **Orders:** implementacja `ICashOperationSource` dla wpłat i zwrotów, indeksy po datach operacji.
- **Shared:** `CashOperationKind` (Kernel), `CashOperation` i `ICashOperationSource` (Application).
- **Reporting:** reguły `CashFlowPolicy`, `CashFlowTotals`, `CashMovement` (Domain), przypadek
  `ListOperations` (Application), endpoint `GET /api/operations` (Api).
- **Frontend:** `features/operations/` zastępuje zaślepkę listą „Operacje”, dodaniem i szczegółami
  wypłaty.

### Rozstrzygnięcia uwag z przeglądu (`03-spec-review.md`)

| # | Uwaga przeglądu | Decyzja | Gdzie w planie |
|---|---|---|---|
| 1 | `page` poza zakresem | `page` < 1 albo nie-liczba → `400` przy kluczu `page` (komunikat jak `ListExpensesHandler`: „Numer strony musi być liczbą całkowitą od 1.”). Strona za ostatnią → `200` z pustym `items` i prawdziwymi `total` i `totals` (jak `ExpenseReader.ListAsync`, `skip >= total`). | B14, test R-3, I-O-9 |
| 2 | `PUT` wypłaty bez zmienionych pól | Dokładnie jak `EditExpense` z SF-15 (`ExpenseChangeRunner.ChangeAsync`, gałąź `result.Changes.Count == 0`, oraz `Expense.Changed`, który ustawia `UpdatedAtUtc` tylko przy zmianach): `200 { version }` z **tą samą** wersją, bez `SaveChanges` (więc `rowversion` się nie zmienia), `UpdatedAtUtc` bez zmian, brak wpisu audytu. | B1, B6, testy D-W-3, I-W-6 |
| 3 | Błąd sieci / `5xx` przy zapisie i usunięciu | Wzór Expenses (`expenseErrorMessage` w `expense-form-model.ts`): `toApiError` daje `network` dla statusu 0 i 5xx → `NETWORK_ERROR_MESSAGE` („Nie udało się połączyć z serwerem.”); `csrf_invalid` → „Odśwież stronę i spróbuj ponownie.”; inny problem → `detail` z API albo ogólny tekst „Nie udało się zapisać wypłaty.” / „Nie udało się usunąć wypłaty.”. Uwaga: w kodzie SF-15 ogólny tekst to „Nie udało się zapisać wydatku.”, a nie „Nie udało się zapisać. Spróbuj ponownie.” — plan powiela rzeczywisty wzór. Formularz dodania zachowuje `Idempotency-Key` po błędzie, więc ponowienie nie tworzy duplikatu. | F6, F8, F9, testy F-W-2, F-W-4 |
| 4 | Wpłaty do zamówień pending / failed / szkiców | Są na liście (rzeczywiste pieniądze), tak jak wpłaty do zamówień anulowanych. Źródło Orders nie filtruje po statusie, źródle ani anulowaniu zamówienia. | B10, test I-O-4 |
| 5 | Kody API ↔ `CashOperationKind` | `payment` ↔ `Payment`, `refund` ↔ `Refund`, `expense` ↔ `PaidExpense`, `withdrawal` ↔ `OwnerWithdrawal`. Jedno miejsce: `OperationCodes` w `Reporting.Application`. | B13, test R-4 |
| 6 | Wyczerpanie limitu przy kolejnym źródle | Gdy pozostały limit (`10 001 − wczytane`) spada do 0, kolejne źródła (i kolejne tabele w źródle Orders/Expenses) nie są wywoływane — nie ma `TOP 0`. Potem endpoint zwraca `422 operations_range_too_large`. | B10, B11, B14, testy R-5, I-O-10 |
| 7 | Kolumna „Opis” | Wpłata/zwrot → numer zamówienia (link do zamówienia); wydatek → opis wydatku (link) i etykieta kategorii jako tekst drugorzędny; wypłata → osoba (link do wypłaty). `sourceLabel` = tekst linku = wartość `description`. „Wypłata właściciela” nie jest `sourceLabel` (zostaje tylko w kolumnie „Rodzaj”). | B10, B11, F5, testy I-O-2, F-O-4 |
| 8 | `effectiveFrom` dla okresu w całości przed uruchomieniem | `effectiveFrom` = `CashFlowPolicy.EffectiveFrom(from, launch)` = max(`from`, data uruchomienia), czyli data uruchomienia (późniejsza niż `to`); `items` puste, `total` 0, sumy `0.00`; źródła nie są odpytywane. | B14, testy R-2, I-O-7 |
| 9 | Etykiety filtra rodzaju | „Wpłata”, „Zwrot” (oba rodzaje zwrotu), „Opłacony wydatek”, „Wypłata właściciela”. | F2, F5 |

Decyzja 7 zmienia zdanie spec (Data / API → Format danych: „„Wypłata właściciela” dla wypłat”)
i uwagę w tabeli niezmienników spec („`sourceLabel` bez danych osobowych”). Niezmiennik 8 dotyczy
dokumentów, a osoba i tak jest w `description` tej samej odpowiedzi (spec pkt 9: opis wypłaty =
osoba), więc decyzja nie narusza żadnego niezmiennika. Etykieta obiektu audytu nadal nie zawiera
osoby.

Inne przyjęte rozstrzygnięcia (z kodu, bez decyzji właściciela):

- **Remis przy sortowaniu:** data według kierunku (`desc` domyślnie), potem rodzaj w stałej
  kolejności `payment`, `refund`, `expense`, `withdrawal` i `id` rosnąco — niezależnie od kierunku
  („stała kolejność”, spec pkt 11).
- **Znak minus:** kwoty formatuje wyłącznie `formatPln` z `shared/format` (jak lista wydatków, którą
  spec wskazuje jako wzór). Formatter daje `-` (U+002D, `pl-format.spec.ts:21`), a spec wspomina
  U+2212. Plan nie zmienia formatera — zob. Risks.

## Backend Plan

### Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Kernel | `StoneFlow.Shared.Kernel/Operations/` | słownik `CashOperationKind` (używany przez `Reporting.Domain`) |
| Shared Application | `StoneFlow.Shared.Application/Operations/` | kontrakt `CashOperation`, `ICashOperationSource` |
| Domain | `Expenses.Domain/OwnerWithdrawals/` | encja wypłaty, reguła kwoty, pola audytu |
| Application | `Expenses.Application` | wejście, kody, audyt, porty, 5 przypadków użycia |
| Infrastructure | `Expenses.Infrastructure/Persistence` | konfiguracja EF, store, reader, źródło operacji, migracja, DI |
| Api | `Expenses.Api` | endpointy `/api/owner-withdrawals`, kontrakty, Problem Details |
| Infrastructure | `Orders.Infrastructure/Persistence` | źródło operacji (wpłaty, zwroty), indeksy dat, migracja, DI |
| Domain | `Reporting.Domain/CashFlow/` | `CashFlowPolicy`, `CashFlowTotals`, `CashMovement`, `CashDirection` |
| Application | `Reporting.Application/Operations/` | `ListOperations` (walidacja, łączenie źródeł, bezpiecznik, sortowanie, stronicowanie) |
| Api | `Reporting.Api` | `GET /api/operations`, kontrakty, `ReportingProblems` |
| Tests | `StoneFlow.Reporting.Tests`, `StoneFlow.Expenses.Tests`, `StoneFlow.Api.IntegrationTests` | domena, aplikacja, HTTP i baza |

Reporting nie dostaje zmian schematu ani DbContextu.

### Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Shared/StoneFlow.Shared.Kernel/Operations/CashOperationKind.cs` | create | `enum CashOperationKind { Payment, Refund, PaidExpense, OwnerWithdrawal }` — kolejność deklaracji = stała kolejność remisu |
| `api/src/Shared/StoneFlow.Shared.Application/Operations/CashOperation.cs` | create | `record CashOperation(Kind, Id, Date, Amount, Method, RefundKind, Description, Category, SourceId, SourceLabel)` |
| `api/src/Shared/StoneFlow.Shared.Application/Operations/ICashOperationSource.cs` | create | `Kinds`, `ListAsync(from, to, kinds, limit, ct)`; dokumentacja: `from` to efektywny początek, wynik ≤ `limit`, tylko żądane rodzaje, kwota zapisana |
| `api/src/Modules/Expenses/StoneFlow.Expenses.Domain/OwnerWithdrawals/OwnerWithdrawal.cs` | create | encja, `OwnerWithdrawalContent`, `Create`, `Edit`, `Snapshot`, `AmountErrorOf` |
| `…/Expenses.Domain/OwnerWithdrawals/OwnerWithdrawalVocabulary.cs` | create | `OwnerWithdrawalMethod { Transfer, Blik, Cash }`, `OwnerWithdrawalField`, `OwnerWithdrawalFieldValue`, `OwnerWithdrawalFieldChange`, `OwnerWithdrawalAmountError` |
| `…/Expenses.Application/ExpenseInput.cs` | modify | `ParseAmount` i `AmountError` → `internal` (wspólny parser tekstu kwoty, bez kopii reguły formatu); `ExpenseVersion.Parse` bez zmian (już publiczne) |
| `…/Expenses.Application/OwnerWithdrawalCodes.cs` | create | kody metod (`transfer`, `blik`, `cash`) i pól (`person`, `amount`, `withdrawnOn`, `method`) |
| `…/Expenses.Application/OwnerWithdrawalInput.cs` | create | `OwnerWithdrawalInput` (tekst z JSON) → `Validate(errors, today)`, `ComputeRequestHash`, `OwnerWithdrawalVersion`, `OwnerWithdrawalFieldKeys`, `OwnerWithdrawalMessages` |
| `…/Expenses.Application/OwnerWithdrawalAudit.cs` | create | `Created` / `Updated` / `Deleted`, etykieta „Wypłata właściciela”, `person` jako dane osobowe |
| `…/Expenses.Application/OwnerWithdrawalChange.cs` | create | `OwnerWithdrawalChangeOutcome { Saved, ValidationFailed, NotFound, Conflict }`, `OwnerWithdrawalChangeResult` |
| `…/Expenses.Application/Abstractions/OwnerWithdrawalPorts.cs` | create | `StoredOwnerWithdrawal`, `IOwnerWithdrawalStore`, `OwnerWithdrawalCreationMatch`, `IOwnerWithdrawalReader` (wyniki `StoreSaveResult`/`StoreSaveStatus` z `Ports.cs`) |
| `…/Expenses.Application/CreateOwnerWithdrawal/CreateOwnerWithdrawal.cs` | create | komenda, wynik, handler (wzór `CreateExpenseHandler`) |
| `…/Expenses.Application/EditOwnerWithdrawal/EditOwnerWithdrawal.cs` | create | poprawa z wersją (przebieg `ExpenseChangeRunner` bez reguł zapłaty) |
| `…/Expenses.Application/DeleteOwnerWithdrawal/DeleteOwnerWithdrawal.cs` | create | usunięcie z wersją, audyt `Deleted` przed `DELETE` (wzór `DeleteExpenseHandler`) |
| `…/Expenses.Application/GetOwnerWithdrawal/GetOwnerWithdrawal.cs` | create | `OwnerWithdrawalRecordDto`, zapytanie, handler |
| `…/Expenses.Application/ListOwnerWithdrawalPeople/ListOwnerWithdrawalPeople.cs` | create | zapytanie i handler podpowiedzi osób |
| `…/Expenses.Infrastructure/Persistence/OwnerWithdrawalConfiguration.cs` | create | tabela, `CHECK`, indeksy, `rowversion` |
| `…/Expenses.Infrastructure/Persistence/ExpensesDbContext.cs` | modify | `DbSet<OwnerWithdrawal> OwnerWithdrawals` |
| `…/Expenses.Infrastructure/Persistence/OwnerWithdrawalStore.cs` | create | load/add/save/delete, tłumaczenie `DbUpdateConcurrencyException` i 2601/2627 (wzór `ExpenseStore`) |
| `…/Expenses.Infrastructure/Persistence/OwnerWithdrawalReader.cs` | create | `GetAsync`, `FindByCreationRequestAsync`, `ListPeopleAsync` |
| `…/Expenses.Infrastructure/Persistence/ExpenseCashOperationSource.cs` | create | `ICashOperationSource` dla `PaidExpense` i `OwnerWithdrawal` |
| `…/Expenses.Infrastructure/Persistence/Migrations/<ts>_OwnerWithdrawals.cs` (+ `.Designer.cs`, `ExpensesDbContextModelSnapshot.cs`) | create/modify | migracja addytywna |
| `…/Expenses.Infrastructure/DependencyInjection.cs` | modify | store, reader, `ICashOperationSource` |
| `…/Expenses.Api/Endpoints/EndpointSupport.cs` | create | `IdempotencyKey(request)` i `CurrentAccount(user)` przeniesione z `ExpensesEndpoints` (używane przez obie grupy) |
| `…/Expenses.Api/Endpoints/ExpensesEndpoints.cs` | modify | użycie `EndpointSupport` (bez zmiany zachowania) |
| `…/Expenses.Api/Endpoints/OwnerWithdrawalsEndpoints.cs` | create | grupa `/api/owner-withdrawals`, 5 tras |
| `…/Expenses.Api/Endpoints/OwnerWithdrawalContracts.cs` | create | żądania, odpowiedzi, mapowanie |
| `…/Expenses.Api/OwnerWithdrawalProblems.cs` | create | kody i teksty błędów wypłat |
| `…/Expenses.Api/ExpensesModule.cs` | modify | `MapExpensesEndpoints` mapuje także `OwnerWithdrawalsEndpoints`; komentarz |
| `api/src/Modules/Orders/StoneFlow.Orders.Infrastructure/Persistence/OrderEntryConfiguration.cs` | modify | `IX_Payments_PaidOn` w `PaymentConfiguration`, `IX_Refunds_RefundedOn` w `RefundConfiguration` |
| `…/Orders.Infrastructure/Persistence/OrderCashOperationSource.cs` | create | `ICashOperationSource` dla `Payment` i `Refund` |
| `…/Orders.Infrastructure/Persistence/Migrations/<ts>_CashOperationDateIndexes.cs` (+ `.Designer.cs`, `OrdersDbContextModelSnapshot.cs`) | create/modify | migracja addytywna (dwa indeksy) |
| `…/Orders.Infrastructure/DependencyInjection.cs` | modify | `ICashOperationSource` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/CashFlow/CashFlowPolicy.cs` | create | `DirectionOf(kind)`, `EffectiveFrom(from, launchDate)` |
| `…/Reporting.Domain/CashFlow/CashFlowTotals.cs` | create | `CashDirection`, `CashMovement(Kind, Amount)`, `CashFlowTotals(Inflows, Outflows, Change)` + `Of(movements)` |
| `…/Reporting.Application/Abstractions/FieldErrors.cs` | create | kopia typu z Expenses (moduł nie referencjonuje innego modułu; precedens w Expenses) |
| `…/Reporting.Application/Operations/OperationCodes.cs` | create | kody rodzajów (decyzja 5), kierunków, `sourceType`, format kwoty i daty |
| `…/Reporting.Application/Operations/ListOperations.cs` | create | `ListOperationsQuery`, kryteria, DTO, wynik, `ListOperationsHandler` |
| `…/Reporting.Api/Endpoints/OperationsEndpoints.cs` | create | grupa `/api/operations` |
| `…/Reporting.Api/Endpoints/OperationContracts.cs` | create | odpowiedzi i mapowanie |
| `…/Reporting.Api/ReportingProblems.cs` | create | `operations_range_too_large` (422) |
| `…/Reporting.Api/ReportingModule.cs` | modify | `MapReportingEndpoints` mapuje `OperationsEndpoints`; komentarz |
| `api/tests/StoneFlow.Reporting.Tests/StoneFlow.Reporting.Tests.csproj` (+ `packages.lock.json`) | modify | referencja `StoneFlow.Reporting.Application` (testy handlera) |
| `api/tests/StoneFlow.Reporting.Tests/CashFlow/CashFlowPolicyTests.cs` | create | R-1, R-2 |
| `api/tests/StoneFlow.Reporting.Tests/CashFlow/CashFlowTotalsTests.cs` | create | R-6 |
| `api/tests/StoneFlow.Reporting.Tests/Operations/ListOperationsHandlerTests.cs` | create | R-3 … R-5, R-7 (fałszywe źródła i `ILaunchDate`) |
| `api/tests/StoneFlow.Expenses.Tests/TestOwnerWithdrawals.cs` | create | budowniczy treści testowej (fikcyjne osoby) |
| `api/tests/StoneFlow.Expenses.Tests/Domain/OwnerWithdrawalTests.cs` | create | D-W-1 … D-W-3 |
| `api/tests/StoneFlow.Expenses.Tests/Application/OwnerWithdrawalInputTests.cs` | create | A-W-1, A-W-2 |
| `api/tests/StoneFlow.Expenses.Tests/Application/OwnerWithdrawalAuditTests.cs` | create | A-W-3 |
| `api/tests/StoneFlow.Api.IntegrationTests/Expenses/ExpensesTest.cs` | modify | `AuditRowsAsync(id, objectType = "Expense")` — ten sam pomocnik dla wypłat |
| `api/tests/StoneFlow.Api.IntegrationTests/Expenses/OwnerWithdrawalsApiTests.cs` | create | I-W-1 … I-W-9 |
| `api/tests/StoneFlow.Api.IntegrationTests/Reporting/OperationsApiTests.cs` | create | I-O-1 … I-O-12 (dane przez `OrdersTest` + `SendAsync` na `/api/expenses` i `/api/owner-withdrawals`) |

Bez zmian: `stone-flow.slnx` (brak nowych projektów), `deploy/scripts/contexts.sh` (brak nowego
kontekstu), `EndpointSecurityTests.AllowedAnonymous` (brak tras anonimowych), pliki `.csproj` w
`api/src` (wszystkie potrzebne referencje już istnieją: Orders/Expenses.Infrastructure →
Application → `Shared.Application` → `Shared.Kernel`; `Reporting.Domain` → `Shared.Kernel`).

### Implementation steps

Kolejność TDD: każdy krok domeny i aplikacji zaczyna się od czerwonego testu (sekcja „Tests”),
potem kod, potem zielony test.

- **B1 Expenses.Domain — wypłata** (test D-W-* najpierw).
  - `OwnerWithdrawalContent(string Person, decimal Amount, DateOnly WithdrawnOn, OwnerWithdrawalMethod Method)`
    — wartości już znormalizowane.
  - `OwnerWithdrawal`: `PersonMaxLength = 100`; prywatne settery; `Id`, `Person`, `Amount`,
    `WithdrawnOn`, `Method`, `CreationRequestId?`, `CreationRequestHash?`, `CreatedAtUtc`,
    `UpdatedAtUtc`.
  - `AmountErrorOf(decimal)` → `None` / `NotPositive` (≤ 0) / `OutOfRange`
    (`!ExpenseAmounts.IsInRange`, ten sam limit `decimal(18,2)` co wydatek).
  - `Create(id, content, creationRequestId, hash, nowUtc)`: rzuca `ArgumentException` przy pustym
    id/kluczu, pustej osobie, osobie > 100 albo `AmountErrorOf ≠ None` (walidacja wejścia
    sprawdza to wcześniej).
  - `Edit(content, nowUtc)` → `IReadOnlyList<OwnerWithdrawalFieldChange>` w kolejności `Snapshot`
    (`Person`, `Amount`, `WithdrawnOn`, `Method`); `UpdatedAtUtc = nowUtc` **tylko** przy
    niepustej liście (jak `Expense.Changed`, decyzja 2).
  - Domena nie zna „dzisiaj” (jak `Expense`): datę ≤ dziś sprawdza wejście.
- **B2 Expenses.Application — kody i wejście** (A-W-1, A-W-2 najpierw).
  - `OwnerWithdrawalCodes`: `Of(method)`, `TryParse(code, out method)` (porównanie dokładne),
    `Of(field)` camelCase.
  - `OwnerWithdrawalInput(string? Person, string? Amount, string? WithdrawnOn, string? Method)`.
    `Validate(errors, today)` → `OwnerWithdrawalContent?`:
    - `person`: `ExpenseInput.Text` (przycięcie), wymagana, ≤ 100;
    - `amount`: `ExpenseInput.ParseAmount` (wzorzec, skala ≤ 2, zakres), potem
      `OwnerWithdrawal.AmountErrorOf` (> 0);
    - `withdrawnOn`: wymagana, `yyyy-MM-dd`, ≤ `today`;
    - `method`: wymagana, kod ze słownika.
  - `ComputeRequestHash(content)`: SHA-256 kanonicznego JSON-a `{ person, amount "0.00", withdrawnOn, method code }`
    (wzór `ExpenseInput.ComputeRequestHash`).
  - `OwnerWithdrawalVersion.Validate(text, errors)` na `ExpenseVersion.Parse`, komunikaty wypłaty,
    klucz `version`.
  - Klucze błędów = pola JSON: `person`, `amount`, `withdrawnOn`, `method`, `version`.
  - Komunikaty: „Podaj osobę.”, „Osoba może mieć najwyżej 100 znaków.”, „Podaj kwotę.”, „Podaj
    kwotę jako liczbę, np. 1234.50.”, „Kwota może mieć najwyżej dwa miejsca po przecinku.”, „Kwota
    jest zbyt duża.”, „Kwota musi być większa od 0 zł.”, „Podaj datę wypłaty.”, „Podaj poprawną datę
    wypłaty.”, „Data wypłaty nie może być późniejsza niż dzisiaj.”, „Wybierz metodę.”, „Nieznana
    metoda.”, „Brak wersji wypłaty. Wczytaj dane ponownie.”, „Niepoprawna wersja wypłaty. Wczytaj
    dane ponownie.”
- **B3 Audyt** (A-W-3 najpierw). `OwnerWithdrawalAudit.Created/Updated/Deleted(withdrawal, [changes,] author)`:
  - obiekt `AuditObjectType.OwnerWithdrawal`, id wypłaty, data biznesowa `WithdrawnOn` (dla
    `Updated` — nowa data), etykieta „Wypłata właściciela”, rodzic `null`;
  - wartości: kwota `ExpenseInput.FormatAmount` (`2000.00`), data `yyyy-MM-dd`, metoda jako kod;
  - `IsPersonalData(field)` = `field == Person`;
  - `Created`: wszystkie pola jako nowe; `Updated`: tylko zmienione; `Deleted`: wszystkie jako
    stare z nową `null`.
- **B4 Porty** (`OwnerWithdrawalPorts.cs`):
  - `IOwnerWithdrawalStore`: `LoadAsync(id)` → `StoredOwnerWithdrawal?`, `AddAsync`, `SaveAsync(w, expectedVersion)`,
    `DeleteAsync(w, expectedVersion)` → `StoreSaveResult` (istniejący typ);
  - `IOwnerWithdrawalReader`: `GetAsync(id)` → `OwnerWithdrawalRecordDto?`,
    `FindByCreationRequestAsync(key)` → `OwnerWithdrawalCreationMatch?`, `ListPeopleAsync(max)` →
    `IReadOnlyList<string>`.
- **B5 Create** (wzór `CreateExpenseHandler`): walidacja (400) → skrót → w
  `IUnitOfWork.ExecuteInTransactionAsync`: `FindByCreationRequest`, `OwnerWithdrawal.Create`,
  `AddAsync`, audyt `Created`; `commitWhen` tylko dla `Created`. Po `DuplicateRequest` ponowny
  odczyt po kluczu: ten sam skrót → `Repeated` (id, aktualna wersja), inny → `KeyReused` (id).
  Brak dopasowania po kluczu → ponowne zgłoszenie oryginalnego wyjątku (błąd programu).
- **B6 Edit**: walidacja wersji i treści (400) → w jednostce pracy: `Load` (404) → porównanie wersji
  (409) → `Edit` → **brak zmian: `Saved` z wersją zapisaną, bez `SaveAsync` i bez audytu**
  (decyzja 2) → `SaveAsync(expected)` (`Conflict` → 409) → audyt `Updated`. `commitWhen`:
  `Outcome == Saved`. Przebieg jak `ExpenseChangeRunner.ChangeAsync`, ale bez gałęzi
  `AlreadyPaid`/`NotPaid`; nie rozszerzam `ExpenseChangeRunner` (inny typ encji i wyniku, jeden
  przypadek użycia — KISS).
- **B7 Delete** (wzór `DeleteExpenseHandler`): walidacja wersji (400) → `Load` (404) → wersja (409)
  → audyt `Deleted` ze wszystkimi polami → `DeleteAsync(expected)`; konflikt w bazie → 409 i
  wycofanie razem z audytem.
- **B8 Read side**:
  - `GetOwnerWithdrawalHandler(reader)` → `OwnerWithdrawalRecordDto?` (znaczniki jako UTC,
    `DateTime.SpecifyKind` jak `ExpenseReader.GetAsync`).
  - `ListOwnerWithdrawalPeopleHandler(reader)` → `reader.ListPeopleAsync(50)`.
- **B9 Expenses.Infrastructure**:
  - `OwnerWithdrawalConfiguration` (tabela `OwnerWithdrawals`, schemat z kontekstu):
    - `Id` `ValueGeneratedNever`; `Person` `nvarchar(100)` wymagane; `Amount` `HasPrecision(18, 2)`;
      `WithdrawnOn` `date`; `Method` `HasConversion<string>()` `nvarchar(16)`;
      `CreationRequestHash` `binary(32)`; `CreatedAtUtc`, `UpdatedAtUtc` `datetime2`;
      `Version` jako właściwość cienia `IsRowVersion()`;
    - `CK_OwnerWithdrawals_Amount` `[Amount] > 0`;
    - `CK_OwnerWithdrawals_Method` `[Method] IN (N'Transfer', N'Blik', N'Cash')`;
    - `CK_OwnerWithdrawals_CreationRequest` (oba pola klucza albo żadne, jak w `Expenses`);
    - `UX_OwnerWithdrawals_CreationRequestId` unikalny, filtr `[CreationRequestId] IS NOT NULL`;
    - `IX_OwnerWithdrawals_WithdrawnOn` (`WithdrawnOn`) `INCLUDE (Amount)`.
  - `OwnerWithdrawalStore`: kopia przebiegu `ExpenseStore` (oczekiwana wersja przez
    `OriginalValue`, `DbUpdateConcurrencyException` → `Conflict`, 2601/2627 przy dodaniu →
    `DuplicateRequest` z wyjątkiem).
  - `OwnerWithdrawalReader.ListPeopleAsync(max)`: w SQL `GroupBy(w => w.Person.ToUpper())` i w
    grupie zapis osoby z najnowszym `UpdatedAtUtc` (remis: `Id`), sortowanie alfabetyczne według
    collation bazy, `Take(max)`. Daje unikalne osoby bez rozróżniania wielkości liter z ostatnio
    użytym zapisem (spec Edge Cases).
  - Migracja (z katalogu repozytorium):
    `dotnet ef migrations add OwnerWithdrawals -p api/src/Modules/Expenses/StoneFlow.Expenses.Infrastructure -s api/src/StoneFlow.Api -c ExpensesDbContext -o Persistence/Migrations`.
    Tylko `CreateTable` + indeksy + `CHECK`; `Down` usuwa tabelę. Sprawdzić wygenerowany kod przed
    commitem.
- **B10 Źródła operacji** (Infrastructure modułów-właścicieli, wzór `ShopOrderLookup`; projekcja w
  SQL, `AsNoTracking`, encje nie wychodzą poza Infrastructure).
  - `OrderCashOperationSource(OrdersDbContext)`, `Kinds = { Payment, Refund }`:
    - gdy `kinds` zawiera `Payment`: `Payments` złączone z `Orders` po `OrderId`,
      `Date >= from && Date <= to`, `OrderBy(Date).ThenBy(Id).Take(remaining)` →
      `CashOperation(Payment, p.Id, p.Date, p.Amount, OrderCodes.Of(p.Method), null, o.Number, null, p.OrderId, o.Number)`;
    - gdy `kinds` zawiera `Refund` i `remaining > 0`: to samo dla `Refunds` z
      `RefundKind = OrderCodes.Of(r.Kind)`, metodą `OrderCodes.Of(r.Method)`;
    - **bez filtra statusu, źródła ani anulowania zamówienia** (decyzja 4);
    - `remaining = limit − już wczytane w tym wywołaniu`; przy 0 druga tabela nie jest odpytywana
      (decyzja 6). Zapytania sekwencyjnie (`ModuleConnection`).
    - Korekty sprzedaży nie są czytane (niezmiennik 1).
    - Mapowanie enum → kod po materializacji (projekcja do anonimowego typu z enumem, potem
      `OrderCodes.Of`).
  - `ExpenseCashOperationSource(ExpensesDbContext)`, `Kinds = { PaidExpense, OwnerWithdrawal }`:
    - `PaidExpense`: `Expenses.Where(PaidOn != null && PaidOn >= from && PaidOn <= to)`,
      `OrderBy(PaidOn).ThenBy(Id).Take(remaining)` →
      `CashOperation(PaidExpense, e.Id, e.PaidOn!.Value, e.GrossAmount, method code albo null, null, e.Description, ExpenseCodes.Of(e.Category), e.Id, e.Description)`
      (kwota ze znakiem, metoda nullowalna — „Nie podano” w UI);
    - `OwnerWithdrawal`: `OwnerWithdrawals.Where(WithdrawnOn in [from, to])` →
      `CashOperation(OwnerWithdrawal, w.Id, w.WithdrawnOn, w.Amount, OwnerWithdrawalCodes.Of(w.Method), null, w.Person, null, w.Id, w.Person)`;
    - `remaining` jak wyżej.
  - `sourceLabel` = `description` dla wszystkich rodzajów (decyzja 7).
  - Orders: w `PaymentConfiguration` `builder.HasIndex(p => p.Date).IncludeProperties(p => new { p.Amount, p.OrderId }).HasDatabaseName("IX_Payments_PaidOn")`;
    w `RefundConfiguration` analogicznie `IX_Refunds_RefundedOn`. Korekty sprzedaży bez indeksu.
  - Migracja Orders:
    `dotnet ef migrations add CashOperationDateIndexes -p api/src/Modules/Orders/StoneFlow.Orders.Infrastructure -s api/src/StoneFlow.Api -c OrdersDbContext -o Persistence/Migrations`.
    Tylko `CreateIndex` ×2 (`Down`: `DropIndex`).
- **B11 DI** (dwie implementacje kontraktu, rozwiązywane jako `IEnumerable<ICashOperationSource>`):
  - `Orders.Infrastructure/DependencyInjection.cs`: `services.AddScoped<ICashOperationSource, OrderCashOperationSource>();`
  - `Expenses.Infrastructure/DependencyInjection.cs`: `services.AddScoped<IOwnerWithdrawalStore, OwnerWithdrawalStore>();`,
    `services.AddScoped<IOwnerWithdrawalReader, OwnerWithdrawalReader>();`,
    `services.AddScoped<ICashOperationSource, ExpenseCashOperationSource>();`
  - Zwykłe `AddScoped` (nie `TryAdd…`), żeby obie rejestracje się sumowały. Scoped, bo zależą od
    scoped `DbContext` na wspólnym `ModuleConnection`. Kolejność rejestracji (Program.cs: Orders,
    potem Expenses) nie wpływa na wynik, bo Reporting sortuje.
- **B12 Reporting.Domain** (R-1, R-2, R-6 najpierw; bez I/O):
  - `enum CashDirection { Inflow, Outflow }`.
  - `CashFlowPolicy.DirectionOf(CashOperationKind)`: `Payment` → `Inflow`, pozostałe → `Outflow`.
  - `CashFlowPolicy.EffectiveFrom(DateOnly from, DateOnly launchDate)` = max.
  - `record CashMovement(CashOperationKind Kind, decimal Amount)`.
  - `record CashFlowTotals(decimal Inflows, decimal Outflows, decimal Change)`;
    `Of(IEnumerable<CashMovement>)`: suma kwot zapisanych według `DirectionOf`, `Change = Inflows − Outflows`;
    bez zaokrąglania (wejście ma skalę 2), wartości ujemne zachowane; pusta kolekcja → `0`.
- **B13 Reporting.Application — kody**: `OperationCodes`:
  - rodzaje (decyzja 5): `payment` ↔ `Payment`, `refund` ↔ `Refund`, `expense` ↔ `PaidExpense`,
    `withdrawal` ↔ `OwnerWithdrawal`; `TryParse` dokładny;
  - kierunki `inflow` / `outflow`;
  - `SourceTypeOf(kind)`: `Payment`/`Refund` → `order`, `PaidExpense` → `expense`,
    `OwnerWithdrawal` → `withdrawal`;
  - `FormatAmount` (`0.00`, kultura niezmienna), `FormatDate` (`yyyy-MM-dd`), `TryParseDate`.
- **B14 `ListOperationsHandler(IEnumerable<ICashOperationSource> sources, ILaunchDate launchDate)`**
  (R-3 … R-5, R-7 najpierw, na fałszywych źródłach):
  1. Walidacja `ListOperationsQuery(From, To, Kind[], Direction, Page, PageSize)` (tekst z HTTP):
     - `from` / `to` wymagane, `yyyy-MM-dd` („Podaj datę początkową.” / „Podaj datę końcową.” /
       „Podaj datę w formacie RRRR-MM-DD.”); `from > to` → błąd przy `from` („Data początkowa nie
       może być późniejsza niż końcowa.”);
     - `kind` każdy ze słownika (błąd przy `kind`, raz); puste = wszystkie; duplikaty usunięte;
     - `direction` `asc` | `desc`, domyślnie `desc`;
     - `page` liczba całkowita ≥ 1, domyślnie 1, inaczej błąd przy `page` (decyzja 1);
     - `pageSize` 1–100, domyślnie 25.
     Błędy → wynik z `FieldErrors` (400).
  2. `effectiveFrom = CashFlowPolicy.EffectiveFrom(from, launchDate.Value)`.
  3. `effectiveFrom > to` → pusty wynik bez wywoływania źródeł: `items = []`, `total = 0`, sumy 0,
     `effectiveFrom` = data uruchomienia (decyzja 8).
  4. Dla każdego źródła po kolei (`foreach` z `await`, bez `Task.WhenAll`):
     `requested = kinds ∩ source.Kinds`; puste → pomiń; `remaining = 10 001 − loaded.Count`;
     `remaining ≤ 0` → przerwij pętlę (decyzja 6); inaczej
     `ListAsync(effectiveFrom, to, requested, remaining, ct)`.
  5. `loaded.Count > 10 000` (`MaxOperations`) → wynik `TooManyOperations` (422), bez sum.
  6. `totals = CashFlowTotals.Of(loaded.Select(o => new CashMovement(o.Kind, o.Amount)))`.
  7. Sortowanie w pamięci: `Date` według kierunku, potem `Kind` rosnąco (kolejność enum), potem
     `Id` rosnąco; `total = loaded.Count`; `Skip((page − 1) × pageSize).Take(pageSize)` — strona za
     końcem daje pustą listę (decyzja 1).
  8. Pozycja DTO: `CashOperation` + `Direction = CashFlowPolicy.DirectionOf(kind)`.
  Wynik: `ListOperationsResult(OperationsPageDto? Page, FieldErrors? Errors, bool TooManyOperations)`;
  `OperationsPageDto(Items, Page, PageSize, Total, Totals, EffectiveFrom, LaunchDate)`.
  Odstępstwo od „stronicowanie w bazie” uzasadnia spec (D-08, bezpiecznik w SQL).
- **B15 Expenses.Api** (`OwnerWithdrawalsEndpoints`, `MapGroup("/api/owner-withdrawals").WithTags("OwnerWithdrawals")`):
  - `GET /people` → `Expenses.ListOwnerWithdrawalPeople` → `200 { people }`;
  - `GET /{id:guid}` → `Expenses.GetOwnerWithdrawal` → `200` / `404 owner_withdrawal_not_found`;
  - `POST ""` → `Expenses.CreateOwnerWithdrawal`: konto z `ClaimTypes.NameIdentifier`
    (`EndpointSupport.CurrentAccount`), klucz `EndpointSupport.IdempotencyKey` (jeden UUID ≠
    `Guid.Empty`, inaczej `400 idempotency_key_required`); `201 { id, version }` + `Location`
    `/api/owner-withdrawals/{id}`, `200` powtórzenie, `409 idempotency_key_reused` z
    `ownerWithdrawalId`, `400`;
  - `PUT /{id:guid}` → `Expenses.EditOwnerWithdrawal` → `200 { version }`, `400`, `404`,
    `409 owner_withdrawal_conflict`;
  - `DELETE /{id:guid}` z `[FromBody] { version }` → `Expenses.DeleteOwnerWithdrawal` → `204`,
    `400`, `404`, `409`.
  - `OwnerWithdrawalProblems`: `owner_withdrawal_not_found` („Nie znaleziono wypłaty. Mogła zostać
    usunięta.”), `owner_withdrawal_conflict` („Wypłata została zmieniona w międzyczasie. Wczytaj
    aktualne dane.”), `idempotency_key_required` (tekst jak w `ExpenseProblems`),
    `idempotency_key_reused` („Ten formularz zapisał już wypłatę. Odśwież formularz, aby dodać
    kolejną.”).
  - Kontrakty: kwota `"2000.00"`, data `yyyy-MM-dd`, metoda kod, wersja base64.
- **B16 Reporting.Api** (`OperationsEndpoints`, `MapGroup("/api/operations").WithTags("Operations")`,
  `MapGet("", …).WithName("Reporting.ListOperations")`): parametry `[FromQuery] string? from, to,
  string[]? kind, string? direction, page, pageSize` → dyspozytor → `200` / `ValidationProblem` /
  `ReportingProblems.RangeTooLarge()` (`422`, `code = operations_range_too_large`, `detail` „Za dużo
  operacji w wybranym okresie. Wybierz krótszy okres.”). Mapowanie DTO → JSON w
  `OperationContracts` (kody z `OperationCodes`).
- **B17 Testy integracyjne** — sekcja „Tests”.

### Data & contracts

- **Storage:**
  - `expenses.OwnerWithdrawals` (B9), migracja `<ts>_OwnerWithdrawals`.
  - `orders.Payments`: `IX_Payments_PaidOn (PaidOn) INCLUDE (Amount, OrderId)`;
    `orders.Refunds`: `IX_Refunds_RefundedOn (RefundedOn) INCLUDE (Amount, OrderId)`; migracja
    `<ts>_CashOperationDateIndexes`.
  - Obie migracje addytywne (expand), zgodne z poprzednią wersją API. Reporting bez zmian.
- **API:** sekcja „API Contract”.
- **Typy przez granicę:** `CashOperationKind` (Kernel), `CashOperation`, `ICashOperationSource`
  (Shared.Application). Istniejące: `ILaunchDate`, `IUnitOfWork`, `IAuditLog`,
  `AuditObjectType.OwnerWithdrawal`, `WarsawTimeZone`.

### Authorization

Każdy endpoint: fallback policy hosta (sesja, 401) → CSRF globalnie dla `POST`/`PUT`/`DELETE` →
handler. Konto do audytu z claimu w endpointcie, przekazane w komendzie. Jest jeden właściciel i
jedna firma, więc zasób nie ma dodatkowego zakresu poza istnieniem (404) i wersją (409).
`GET /api/operations` i odczyty wypłat też wymagają sesji (niezmiennik 3).

### Background jobs

None.

### Edge cases & null safety

| Edge case (spec) | Gdzie |
|---|---|
| okres w całości przed uruchomieniem | B14 krok 3; `effectiveFrom` = data uruchomienia |
| `from > to` | B14 krok 1 → `400` przy `from` |
| > 10 000 operacji; limit wyczerpany po pierwszym źródle | B10 `remaining`, B14 kroki 4–5 → `422` |
| wpłata z dnia uruchomienia do zamówienia sprzed uruchomienia (URU-01); wpłata dzień wcześniej | filtr po dacie wpisu w B10 (nie po dacie zamówienia), `effectiveFrom` z B14 |
| zwrot / zapłata wydatku od uruchomienia do obiektu sprzed uruchomienia | jw. (data zwrotu, data zapłaty) |
| filtr „Opłacony wydatek” z samymi korektami: wypływy < 0, zmiana > 0 | `CashFlowTotals.Of` (bez wartości bezwzględnej) |
| wydatek bez metody | `CashOperation.Method = null` → `method: null` |
| cofnięcie zapłaty / usunięcie źródła | odczyt na żywo, bez kopii danych |
| zwrot sprzedaży i zwrot pieniędzy | `refundKind` w pozycji |
| zamówienie anulowane, pending, failed, szkic z wpłatami | B10 bez filtra statusu (decyzja 4) |
| strona poza zakresem, `page` < 1 | B14 kroki 1 i 7 (decyzja 1) |
| równoległa edycja wypłaty | B6/B7 porównanie wersji + `rowversion` w `SaveAsync`/`DeleteAsync` |
| ten sam klucz i treść / inna treść / wyścig | B5 |
| data wypłaty jutro (Warsaw), granica 23:30 UTC | `WarsawTime.Today(timeProvider)` w handlerze, B2 |
| osoba z białymi znakami; podpowiedzi bez duplikatów | B2 przycięcie; B9 `ListPeopleAsync` |
| `PUT` bez zmian | B6 (decyzja 2) |
| `PaidOn` nullowalne w źródle wydatków | predykat `PaidOn != null` w SQL; `!` dopiero po nim |
| `method` nullowalne | `CashOperation.Method` `string?`; kontrakt `method: string \| null` |

## Frontend Plan

### Step 0 — aplikacja

Jedna aplikacja: `app/`. Praca w `app/src/app/features/operations/` (zastępuje zaślepkę).

### Expected API contract (wyprowadzony ze spec)

| Method | Route | Request shape | Response shape | Used by |
|---|---|---|---|---|
| GET | `/api/operations` | `from`, `to`, `kind` (powtarzany), `direction`, `page`, `pageSize` | `{ items: OperationItem[], page, pageSize, total, totals: { inflows, outflows, change }, effectiveFrom, launchDate }`; `422 operations_range_too_large` | `OperationsApi.list` |
| GET | `/api/owner-withdrawals/people` | — | `{ people: string[] }` | `OwnerWithdrawalsApi.people` |
| GET | `/api/owner-withdrawals/{id}` | — | `WithdrawalDetails`; `404 owner_withdrawal_not_found` | `OwnerWithdrawalsApi.get` |
| POST | `/api/owner-withdrawals` | `Idempotency-Key`; `WithdrawalInput` | `201`/`200 { id, version }`; `409 idempotency_key_reused { ownerWithdrawalId }` | `OwnerWithdrawalsApi.create` |
| PUT | `/api/owner-withdrawals/{id}` | `WithdrawalInput & { version }` | `{ version }`; `409 owner_withdrawal_conflict` | `OwnerWithdrawalsApi.update` |
| DELETE | `/api/owner-withdrawals/{id}` | body `{ version }` | `204` | `OwnerWithdrawalsApi.delete` |

`OperationItem = { kind: 'payment'|'refund'|'expense'|'withdrawal', id, date, direction: 'inflow'|'outflow', amount, method: string|null, refundKind: 'salesReturn'|'moneyReturn'|null, description, category: string|null, sourceType: 'order'|'expense'|'withdrawal', sourceId, sourceLabel }`.

### Routing

| Path | Page | Loaded how | Guard |
|---|---|---|---|
| `/operacje` | `OperationsListPage` | `section('operacje', { loadChildren: OPERATIONS_ROUTES })`, ścieżka `''` | istniejący guard powłoki (sesja) |
| `/operacje/wyplaty/nowa` | `WithdrawalCreatePage` | `loadComponent`, `title: 'Dodaj wypłatę'` | jw. |
| `/operacje/wyplaty/:id` | `WithdrawalDetailsPage` | `loadComponent`, `title: 'Wypłata właściciela'` | jw. |

`wyplaty/nowa` przed `wyplaty/:id` (jak `EXPENSES_ROUTES`).

### Files

| File path | Action | Purpose |
|---|---|---|
| `app/src/app/features/operations/operations-page.ts`, `.html` | delete | zaślepka |
| `app/src/app/features/operations/operations.routes.ts` | create | `OPERATIONS_ROUTES` |
| `app/src/app/features/operations/operation-models.ts` | create | typy listy, kody, etykiety rodzaju/filtra/kierunku/metody/kategorii, `NEGATIVE_AMOUNT_LABEL`, `isNegativeAmount`, `sourceLink(item)`, `OPERATIONS_PROBLEM` |
| `app/src/app/features/operations/withdrawal-models.ts` | create | `WithdrawalInput`, `WithdrawalDetails`, `WithdrawalCreated`, `WithdrawalVersion`, `WithdrawalMethod`, `WITHDRAWAL_METHOD_LABELS`, `WITHDRAWAL_PROBLEM` |
| `app/src/app/features/operations/operations-api.ts` (+ `.spec.ts`) | create | klient `GET /api/operations` |
| `app/src/app/features/operations/owner-withdrawals-api.ts` (+ `.spec.ts`) | create | klient `/api/owner-withdrawals` (wzór `ExpensesApi`) |
| `app/src/app/features/operations/operations.service.ts` (+ `.spec.ts`) | create | stan listy na sygnałach (wzór `ExpensesService`) |
| `app/src/app/features/operations/owner-withdrawals.service.ts` (+ `.spec.ts`) | create | przepływy wypłaty, `takeNotice`, podpowiedzi osób |
| `app/src/app/features/operations/withdrawal-form-model.ts` (+ `.spec.ts`) | create | model, walidatory, mapowanie, `withdrawalErrorMessage`, `WITHDRAWAL_MESSAGES` |
| `app/src/app/features/operations/withdrawal-form.ts`, `.html` (+ `.spec.ts`) | create | formularz dodania i edycji (wzór `ExpenseForm`) |
| `app/src/app/features/operations/operations-list-page.ts`, `.html` (+ `.spec.ts`) | create | `/operacje` |
| `app/src/app/features/operations/withdrawal-create-page.ts`, `.html` (+ `.spec.ts`) | create | `/operacje/wyplaty/nowa` |
| `app/src/app/features/operations/withdrawal-details-page.ts`, `.html` (+ `.spec.ts`) | create | `/operacje/wyplaty/:id` |
| `app/src/app/app.routes.ts` | modify | `operacje` przez `loadChildren` |
| `app/src/app/app.routes.spec.ts` | modify | `/operacje` poza `SECTIONS` (już nie zaślepka); osobny blok jak „Wydatki” (`/operacje` z odpowiedzią `/api/operations`, `/operacje/wyplaty/nowa` z `/people`) |
| `app/src/app/layout/app-shell.spec.ts` | modify | druga nawigacja testu nagłówka na sekcję bez zapytań (np. `/pulpit`), bo `/operacje` pobiera dane (jak przy `/wydatki` w SF-15) |

`nav-items.ts`, `main-nav.spec.ts`, `return-url.spec.ts` bez zmian (ścieżka `/operacje` zostaje).

Etykiety kategorii i metod są kopią słowników z `features/expenses`/`features/orders` w
`operation-models.ts`: w repozytorium nie ma importów między feature'ami, a mała duplikacja
słownika etykiet jest dopuszczalna (conventions → DRY).

### Implementation steps

- **F1 Modele** (`operation-models.ts`, `withdrawal-models.ts`):
  - kody i typy z kontraktu; `OPERATION_KINDS = ['payment', 'refund', 'expense', 'withdrawal']`;
  - `KIND_FILTER_LABELS` (decyzja 9): `payment` „Wpłata”, `refund` „Zwrot”, `expense` „Opłacony
    wydatek”, `withdrawal` „Wypłata właściciela”;
  - `kindLabel(item)`: `refund` → `REFUND_KIND_LABELS[refundKind]` („Zwrot sprzedaży” / „Zwrot
    pieniędzy”), pozostałe jak w filtrze;
  - `DIRECTION_LABELS`: „Wpływ” / „Wypływ”;
  - `METHOD_LABELS`: przelew, BLIK, gotówka, pobranie; `NO_METHOD = 'Nie podano'`;
  - `CATEGORY_LABELS` (kopia z wydatków), `NEGATIVE_AMOUNT_LABEL = 'Korekta / zwrot od dostawcy'`,
    `isNegativeAmount(text)` (test tekstu `-`, bez arytmetyki);
  - `sourceLink(item)`: `order` → `['/zamowienia', sourceId]`, `expense` → `['/wydatki', sourceId]`,
    `withdrawal` → `['/operacje', 'wyplaty', sourceId]`;
  - `OPERATIONS_PROBLEM.rangeTooLarge = 'operations_range_too_large'`;
    `WITHDRAWAL_PROBLEM` (`notFound`, `conflict`, `keyReused`).
- **F2 Klienci API**:
  - `OperationsApi.list(query)`: `from`, `to`, `direction`, `page`, `pageSize` i `kind` przez
    `params.append` dla każdego rodzaju;
  - `OwnerWithdrawalsApi`: `people()`, `get(id)`, `create(input, key)` (nagłówek
    `Idempotency-Key`, `observe: 'response'`), `update(id, input, version)`,
    `delete(id, version)` (`body: { version }`).
- **F3 `OperationsService`** (`providedIn: 'root'`, więc stan przetrwa przejście do źródła — spec
  pkt 14):
  - sygnały: `from`/`to` (domyślnie pierwszy i ostatni dzień bieżącego miesiąca z
    `todayInWarsaw()` — operacja na tekście daty, nie na kwotach), `kinds`, `direction`
    (`desc`), `page`; wynik: `items`, `total`, `totals`, `effectiveFrom`, `launchDate`, `loading`,
    `error`, `rangeError`;
  - `periodInvalid`: puste „od”/„do” albo „od” > „do” (porównanie tekstu `yyyy-MM-dd`) → bez
    żądania, komunikat przy polu („Podaj datę.” / `DATE_RANGE_MESSAGE`);
  - `load()` z licznikiem sekwencji (wzór `ExpensesService.load`); `422 operations_range_too_large`
    → `rangeError = detail` z API, `items = []`, sumy ukryte; inny błąd → `error`
    („Nie udało się wczytać operacji.”);
  - `launchNotice`: tekst „Operacje liczone od {formatDate(launchDate)} (data uruchomienia).”, gdy
    `effectiveFrom > from` filtra (porównanie tekstu dat z odpowiedzi);
  - `activeFilters`: okres („Okres: 01.11.2026 – 30.11.2026”, usunięcie wraca do bieżącego
    miesiąca) i każdy rodzaj („Rodzaj: Zwrot”); `kindFilterActive`;
  - `setPage`, `setSort` (tylko `date`), `updatePeriod`, `toggleKind` (stała kolejność
    `OPERATION_KINDS`), `removeFilter`, `clearFilters`; każda zmiana filtra → strona 1.
  - **Bez obliczeń pieniędzy:** sumy, kwoty i liczba pozycji pochodzą z API; serwis nie sumuje ani
    nie zmienia znaku.
- **F4 `OwnerWithdrawalsService`**: `people()` (błąd → pusta lista, podpowiedzi są pomocą),
  `load(id)` → `loaded`/`notFound`/`error`; `create` → `saved`/`keyReused`/`error`
  (notatka „Wypłata zapisana.”, wzór SF-15); `update` → `saved`/`conflict`/`notFound`/`error`
  („Zmiany zapisane.”); `delete` → jw. („Wypłata usunięta.”); `takeNotice()`.
- **F5 `OperationsListPage`**:
  - nagłówek „Operacje” i akcja główna `app-button variant="primary"` „Dodaj wypłatę” →
    `/operacje/wyplaty/nowa`;
  - `app-data-table` `filterable` (panel „Filtry” na telefonie), `[filters]` = chipy; filtry w
    `tableFilters`: `app-field` „Data od”, „Data do” (`type="date"`), grupa checkboxów „Rodzaj”
    (`fieldset` + `legend`); komunikat okresu (`periodInvalid` albo `rangeError`) przy polach;
  - pod filtrami `launchNotice` (gdy jest);
  - wiersz podsumowania (`aria-live="polite"`): „Wpływy”, „Wypływy”, „Zmiana środków” —
    `formatPln(totals.*)` (ujemne ze znakiem, bez wartości bezwzględnej) i etykieta „Kwoty brutto
    w PLN według daty operacji pieniężnej.”; ukryty przy błędzie i `rangeError`;
  - kolumny (`DataTableColumn<OperationItem>`): `date` „Data” (`formatDate`, sortowalna — jedyna),
    `kind` „Rodzaj” (`kindLabel`), `description` „Opis” (szablon `appTableCell`: `routerLink`
    `sourceLink(row)` z tekstem `sourceLabel`; dla `expense` pod linkiem etykieta kategorii jako
    tekst drugorzędny `text-(--sf-muted)`) — decyzja 7, `direction` „Kierunek”
    (`DIRECTION_LABELS`, tekst), `amount` „Kwota” (`formatPln(amount)`, `align: 'end'`,
    `tabular-nums`; szablon dopisuje `NEGATIVE_AMOUNT_LABEL`, gdy `kind === 'expense'` i
    `isNegativeAmount`), `method` „Metoda” (`METHOD_LABELS` albo „Nie podano”);
  - stany: `loading` → `loading-state` tabeli; `emptyTitle` „Brak operacji w wybranym okresie”;
    `filteredEmptyTitle` przy aktywnym filtrze rodzaju „Brak operacji wybranych rodzajów w wybranym
    okresie”, w przeciwnym razie ten sam tekst co pusty; błąd → `error` z „Spróbuj ponownie”
    (`retry` → `load`);
  - przy wejściu: `load()` i `takeNotice()` z `OwnerWithdrawalsService` (np. „Wypłata usunięta.”)
    w regionie `aria-live`.
- **F6 `withdrawal-form-model.ts`**:
  - `WithdrawalFormModel { person, amount, withdrawnOn, method }` (tekst);
    `emptyWithdrawalForm(today)` (data = dziś, metoda `''` — bez wartości domyślnej);
    `withdrawalFormFrom(details)` (kwota przez formatowanie do pola jak w `expenseFormFrom`);
    `toWithdrawalInput(model)` (`parsePlnAmount` → kanoniczny ciąg, osoba przycięta);
  - walidatory: osoba wymagana, ≤ 100 po przycięciu; kwota wymagana, `parsePlnAmount` ≠ `null`,
    `≠ '0.00'` (porównanie tekstu), ≤ 16 cyfr całkowitych (długość części całkowitej tekstu); data
    wymagana, ≤ dziś (tekst); metoda wymagana;
  - `WITHDRAWAL_MESSAGES` (teksty jak w API, B2) + `saveFailed` „Nie udało się zapisać wypłaty.”,
    `deleteFailed` „Nie udało się usunąć wypłaty.”, `csrf`;
  - `withdrawalErrorMessage(error, fallback)` — wzór `expenseErrorMessage` (decyzja 3).
- **F7 `WithdrawalForm`** (Signal Forms, `novalidate`, `app-field` + `#control` + `[formField]`):
  - pola: „Osoba” (`input` z `list` → `<datalist>` z `people`), „Kwota (zł)”, „Data wypłaty”
    (`type="date"`, `max` = dziś), „Metoda” (`select` z pustą opcją „Wybierz…”);
  - podsumowanie błędów z fokusem na pierwszym błędnym polu, błędy API przypinane po kluczu
    (`person`, `amount`, `withdrawnOn`, `method`; wzór `ExpenseForm.save`), pozostałe w
    podsumowaniu;
  - `app-button` `busy` w trakcie zapisu (blokada podwójnego wysłania);
  - wejścia: `initial`, `action: (input) => Promise<WithdrawalFormOutcome>`, `submitLabel`,
    `people`; wyjście `cancel`.
- **F8 `WithdrawalCreatePage`**: klucz `crypto.randomUUID()` przy otwarciu, zachowany po błędzie
  (ponowienie bez duplikatu), nowy tylko w „Dodaj kolejną wypłatę” po `keyReused`; `saved` →
  `/operacje/wyplaty/{id}`; `keyReused` → komunikat z fokusem i link do istniejącej wypłaty (wzór
  `ExpenseCreatePage`); `error` → `withdrawalErrorMessage(error, saveFailed)`.
- **F9 `WithdrawalDetailsPage`**:
  - stany `loading` (`loading-state`) / `notFound` (`empty-state` heading „Nie znaleziono wypłaty.
    Mogła zostać usunięta.” + „Wróć do listy”) / `error` (`error-state` „Nie udało się wczytać
    wypłaty.” + „Spróbuj ponownie”) / `loaded`;
  - widok: Osoba, Kwota (`formatPln`), Data wypłaty (`formatDate`), Metoda, Utworzono i Ostatnia
    zmiana (`formatDateTime`, Europe/Warsaw); akcje „Edytuj” (tryb edycji w miejscu z
    `WithdrawalForm`), „Usuń”, „Wróć do listy”;
  - usunięcie: `app-confirm-dialog` heading „Usunąć wypłatę?”, opis z kwotą i datą oraz skutkiem
    („zniknie z listy „Operacje” i z wypływów”), przycisk `danger`; `saved` → `/operacje`
    (komunikat na liście); `conflict` → alert „Wypłata została zmieniona w międzyczasie. Wczytaj
    aktualne dane.” z „Wczytaj ponownie”; `notFound` → stan `notFound`; `error` →
    `withdrawalErrorMessage(error, deleteFailed)`;
  - edycja: `saved` → widok + ponowne wczytanie + „Zmiany zapisane.”; `conflict`/`notFound`/`error`
    jak wyżej (`saveFailed`).
- **F10 Trasy**: `operations.routes.ts`, `app.routes.ts` (`loadChildren`), usunięcie zaślepki,
  aktualizacja `app.routes.spec.ts` i `app-shell.spec.ts`.

### State

- Lokalny w komponencie: tryb widoku/edycji, `busy`, alert, notatka, stan formularza.
- Serwis (root): filtry, strona, kierunek, wynik listy (przetrwa nawigację); jednorazowa notatka.
- Stan serwera: lista, sumy, `effectiveFrom`, `launchDate`, szczegóły wypłaty — zawsze z API.
- Pochodne: `activeFilters`, `kindFilterActive`, `periodInvalid`, `launchNotice` — bez obliczeń
  pieniężnych.

### Styling

- Tokeny przez klasy daisyUI i `text-(--sf-muted)`; brak literałów kolorów (`lint:colors`).
- Jedna akcja główna widoku: „Dodaj wypłatę” na liście; „Zapisz” w formularzu.
- Kwoty `tabular-nums`, wyrównane do prawej; kierunek jako tekst w osobnej kolumnie (bez
  kodowania kolorem).
- Na telefonie: panel „Filtry” z `app-data-table`, sumy w kolumnie.

### Accessibility

- Etykiety pól przez `app-field`; checkboxy rodzaju w `fieldset`/`legend`.
- `aria-live="polite"` dla sum, informacji o dacie uruchomienia i komunikatów sukcesu.
- Fokus na pierwszym błędzie po nieudanym zapisie; fokus na komunikacie `keyReused`.
- `app-confirm-dialog` zarządza fokusem; linki w kolumnie „Opis” są zwykłymi `<a>` (klawiatura).

### Edge cases (frontend)

| Edge case | Gdzie |
|---|---|
| sumy ujemne | `formatPln` bez zmiany znaku |
| wydatek ujemny | dopisek `NEGATIVE_AMOUNT_LABEL` w komórce kwoty |
| `method: null` | „Nie podano” |
| `category: null` (nie-wydatek) | brak tekstu drugorzędnego |
| `refundKind` przy zwrocie | `kindLabel` |
| okres przed uruchomieniem | `launchNotice` z `effectiveFrom`/`launchDate` z API |
| `422` | `rangeError` przy filtrze okresu |
| puste / odwrócone daty | `periodInvalid`, bez żądania |
| `409`/`404`/sieć przy zapisie i usunięciu | F8, F9, decyzja 3 |
| `404` szczegółów | stan `notFound` |

## API Contract

| Method | Route | Request | Response |
|---|---|---|---|
| GET | `/api/owner-withdrawals/people` | — | `200 { people: string[] }` (unikalne bez rozróżniania wielkości liter, ostatnio użyty zapis, alfabetycznie, ≤ 50) |
| GET | `/api/owner-withdrawals/{id}` | — | `200 { id, person, amount, withdrawnOn, method, createdAtUtc, updatedAtUtc, version }`; `404 owner_withdrawal_not_found` |
| POST | `/api/owner-withdrawals` | nagłówek `Idempotency-Key` (UUID); `{ person, amount, withdrawnOn, method }` | `201 { id, version }` + `Location`; `200 { id, version }` (powtórzenie); `400` (pola / `idempotency_key_required`); `409 idempotency_key_reused { ownerWithdrawalId }` |
| PUT | `/api/owner-withdrawals/{id}` | `{ person, amount, withdrawnOn, method, version }` | `200 { version }` (bez zmian: ta sama wersja); `400`; `404`; `409 owner_withdrawal_conflict` |
| DELETE | `/api/owner-withdrawals/{id}` | body `{ version }` | `204`; `400`; `404`; `409 owner_withdrawal_conflict` |
| GET | `/api/operations` | `from`, `to` (`yyyy-MM-dd`, wymagane), `kind` (0..n: `payment`, `refund`, `expense`, `withdrawal`), `direction` (`desc` domyślnie \| `asc`), `page` (≥ 1, domyślnie 1), `pageSize` (1–100, domyślnie 25) | `200 { items, page, pageSize, total, totals: { inflows, outflows, change }, effectiveFrom, launchDate }`; `400` (klucze `from`, `to`, `kind`, `direction`, `page`, `pageSize`); `422 operations_range_too_large` |

- `method` wypłaty: `transfer` \| `blik` \| `cash`. `method` operacji: `transfer` \| `blik` \|
  `cash` \| `cashOnDelivery` \| `null`.
- `items[]`: `{ kind, id, date, direction, amount, method, refundKind, description, category, sourceType, sourceId, sourceLabel }`;
  `sourceLabel` = `description` (decyzja 7); `category` tylko dla `expense`; `refundKind` tylko
  dla `refund`.
- Kwoty jako ciągi `"-1234.50"`, daty `yyyy-MM-dd`, znaczniki ISO 8601 UTC, wersja base64.
- Wszystkie trasy wymagają sesji; zapisy przechodzą globalną kontrolę XSRF.

## Integration Check

| Frontend expects | Backend provides | Gap |
|---|---|---|
| `GET /api/operations` z powtarzanym `kind` | B16 `string[]? kind` | — |
| `totals.inflows/outflows/change` jako ciągi | B14 `CashFlowTotals` → `OperationCodes.FormatAmount` | — |
| `effectiveFrom`, `launchDate` | B14 (decyzja 8) | — |
| `422` z `code = operations_range_too_large` i `detail` | B16 `ReportingProblems` | — |
| `refundKind`, `category`, `method` nullowalne | B10 → B16 | — |
| `sourceType` + `sourceId` do linku; `sourceLabel` jako tekst | B10, B13 (decyzja 7) | — |
| `GET /people` → `{ people }` | B8, B15 | — |
| `POST` z `Idempotency-Key`, `201`/`200`, `409` z `ownerWithdrawalId` | B5, B15 | — |
| `PUT` z `version` w ciele; ta sama wersja przy braku zmian | B6 | — |
| `DELETE` z `{ version }` w ciele, `204` | B7, B15 | — |
| klucze błędów `person`, `amount`, `withdrawnOn`, `method`, `version` | B2 | — |
| sesja + XSRF | host (fallback policy, `CsrfValidationMiddleware`) | — |

No integration gaps.

## Invariant Check

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sales separate from cash | wypłata tylko w `expenses.OwnerWithdrawals` i jako `Outflow` (`CashFlowPolicy`); źródła czytają wyłącznie wpłaty, zwroty, wydatki z `PaidOn` i wypłaty (bez korekt sprzedaży, nieopłaconych wydatków, wartości zamówień) | R-1; I-O-3 (korekta i nieopłacony wydatek pominięte); I-W-8 (wypłata nie trafia do `/api/expenses` ani do `expenses.Expenses`) |
| 2. Backend decimal money | `decimal(18,2)`, `CHECK`, parser tekstowy bez zaokrągleń, sumy w `CashFlowTotals` w backendzie; frontend tylko formatuje ciągi | R-6; A-W-1; I-O-5 (sumy z ujemnym wydatkiem); F-O-3 (UI pokazuje sumy z API bez liczenia) |
| 3. API-enforced access | fallback policy, XSRF, brak `AllowAnonymous` | `EndpointSecurityTests` (automatycznie obejmuje nowe trasy: 401 i CSRF); I-W-9, I-O-12 |
| 4. Retries never duplicate | `UX_OwnerWithdrawals_CreationRequestId`, `CreateOwnerWithdrawalHandler` | I-W-2 (równoległe `POST` z jednym kluczem → jeden wiersz), I-W-3 (`KeyReused`) |
| 5. Financial audit trail | `OwnerWithdrawalAudit` w każdej komendzie w `IUnitOfWork`; `Deleted` przed `DELETE` w tej samej transakcji | A-W-3; I-W-5 (wpisy `Created`/`Updated`/`Deleted`, `person` jako dane osobowe, etykieta bez osoby); I-W-7 (błąd audytu wycofuje zapis) |
| 6. Secrets stay server-side | brak nowych sekretów; Problem Details bez szczegółów | n/d |
| 7. Module and layer boundaries | kontrakt w `Shared.Application`, słownik w `Shared.Kernel`, implementacje w Infrastructure modułów-właścicieli, Reporting bez mapowania tabel `orders`/`expenses`, wywołania sekwencyjne | `StoneFlow.Architecture.Tests` bez zmian i bez nowych wyjątków; I-O-11 (każdy rodzaj ma dokładnie jedno źródło) |
| 8. No identifying data in docs | fikcyjne osoby w testach i dokumentach („Właściciel A”, „Wspólnik B”), etykieta audytu bez osoby | przegląd |

Brak blokerów.

### Wpływ na testy architektury

Bez zmian w `StoneFlow.Architecture.Tests` i bez nowych wyjątków. Istniejące testy obejmują nowe
typy:

- `Shared_kernel_has_no_dependencies` — `CashOperationKind` to czysty enum;
- `Shared_application_does_not_depend_on_infrastructure_http_or_ef` — `ICashOperationSource` i
  `CashOperation` bez EF/HTTP;
- `Domain_does_not_depend_on_outer_layers` / `…_http_ef_or_hosting` — `Reporting.Domain/CashFlow`
  używa tylko Kernel; `Expenses.Domain/OwnerWithdrawals` bez EF;
- `Application_does_not_depend_on_infrastructure_http_or_ef` — `ListOperations`, przypadki wypłat;
- `ModuleBoundaryTests` — Reporting nie odwołuje się do `StoneFlow.Orders`/`StoneFlow.Expenses`,
  a źródła tylko do `Shared.Application`;
- `ProjectReferenceTests` — brak zmian `.csproj` w `api/src`;
- `DbContextSchemaTests` — `OwnerWithdrawal` w schemacie `expenses`, Reporting bez nowych encji.

## Unified Implementation Order

1. **Testy domeny Reporting (czerwone):** R-1, R-2, R-6 → `CashOperationKind` (Kernel) →
   `CashFlowPolicy`, `CashFlowTotals`, `CashMovement` (B12) → zielone.
2. **Testy domeny Expenses (czerwone):** D-W-1 … D-W-3 → `OwnerWithdrawal` i słownik (B1) → zielone.
3. **Testy aplikacji Expenses (czerwone):** A-W-1 … A-W-3 → `ExpenseInput` (`internal` parser),
   `OwnerWithdrawalCodes`, `OwnerWithdrawalInput`, `OwnerWithdrawalAudit` (B2, B3) → zielone.
4. **Kontrakt współdzielony:** `CashOperation`, `ICashOperationSource`.
5. **Testy aplikacji Reporting (czerwone):** referencja w `StoneFlow.Reporting.Tests.csproj`
   (+ lock), R-3 … R-5, R-7 → `OperationCodes`, `FieldErrors`, `ListOperationsHandler` (B13, B14)
   → zielone.
6. **Aplikacja Expenses:** porty i handlery wypłat (B4–B8).
7. **Infrastructure:** `OwnerWithdrawalConfiguration`, store, reader, `ExpenseCashOperationSource`,
   migracja `OwnerWithdrawals` (B9, B10); indeksy Orders, `OrderCashOperationSource`, migracja
   `CashOperationDateIndexes` (B10); DI obu źródeł (B11).
8. **Api:** `EndpointSupport`, `OwnerWithdrawalsEndpoints`, kontrakty, problemy (B15);
   `OperationsEndpoints`, kontrakty, `ReportingProblems` (B16).
9. **Testy integracyjne:** I-W-*, I-O-*, regresja `ExpensesApiTests`, `PaymentsApiTests`,
   `EndpointSecurityTests`.
10. **Frontend:** modele (F1) → klienci + specy (F2) → serwisy + specy (F3, F4) → model formularza
    + spec (F6) → formularz (F7) → strony (F5, F8, F9) → trasy i aktualizacja specyfikacji tras i
    powłoki (F10).
11. Weryfikacja (sekcja „Verification”), potem ręczne scenariusze do `06-implementation-done.md`.

## Tests

| Test | Type | What it proves |
|---|---|---|
| R-1 `CashFlowPolicyTests.DirectionOf` | unit (Reporting.Tests) | `Payment` → wpływ; `Refund`, `PaidExpense`, `OwnerWithdrawal` → wypływ (każdy rodzaj, teoria) |
| R-2 `CashFlowPolicyTests.EffectiveFrom` | unit | `from` przed / w dniu / po uruchomieniu → max; okres w całości przed uruchomieniem → data uruchomienia > `to` |
| R-6 `CashFlowTotalsTests` | unit | sumy według kierunku; wypłata 2000 zwiększa wypływy o 2000; wydatek −50 obniża wypływy o 50; same korekty → wypływy < 0, zmiana > 0; pusto → 0; `Change = Inflows − Outflows`; skala 2 bez zaokrągleń |
| R-3 `ListOperationsHandlerTests` — walidacja i strony | unit (fałszywe źródła, `ILaunchDate`) | brak `from`/`to`, zły format, `from > to` → błąd przy `from`; nieznany `kind`; `page` 0 / `-1` / `abc` → błąd przy `page`; `pageSize` 0/101; strona za końcem → puste `items`, prawdziwe `total` i `totals` |
| R-4 `ListOperationsHandlerTests` — rodzaje i scalanie | unit | mapowanie kodów (decyzja 5); filtr rodzaju przekazuje do źródła tylko jego rodzaje; źródło bez żądanych rodzajów nie jest wołane; sumy tylko dla wybranych rodzajów; sortowanie data/rodzaj/id w obu kierunkach; sumy niezależne od strony i kierunku |
| R-5 `ListOperationsHandlerTests` — bezpiecznik | unit | `limit` drugiego źródła = 10 001 − wczytane; pierwsze źródło zwraca 10 001 → drugie nie jest wołane (decyzja 6) i wynik `TooManyOperations`; dokładnie 10 000 → strona i sumy |
| R-7 `ListOperationsHandlerTests` — data uruchomienia | unit | źródła dostają `effectiveFrom`; okres w całości przed uruchomieniem → żadne źródło nie jest wołane, puste wyniki, sumy 0, `effectiveFrom` = data uruchomienia (decyzja 8) |
| D-W-1 `OwnerWithdrawalTests.AmountErrorOf` | unit (Expenses.Tests) | 0 i ujemna → `NotPositive`; > `MaxAbsolute` → `OutOfRange`; 0,01 i maksimum poprawne |
| D-W-2 `OwnerWithdrawalTests.Create` | unit | pola ustawione, `CreatedAtUtc = UpdatedAtUtc`; puste id/klucz, pusta lub za długa osoba, zła kwota → `ArgumentException` |
| D-W-3 `OwnerWithdrawalTests.Edit` | unit | zmienione pola w kolejności `Snapshot`; brak zmian → pusta lista i `UpdatedAtUtc` bez zmian (decyzja 2) |
| A-W-1 `OwnerWithdrawalInputTests` | unit | każda reguła z kluczem JSON i komunikatem; przycięcie osoby; kwota `-1`, `0`, `0.00`, `1.234`, 17 cyfr; nieznana metoda; skrót niezależny od białych znaków osoby |
| A-W-2 `OwnerWithdrawalInputTests` — granica dnia | unit | data jutro (Warsaw) → błąd; 23:30 UTC zimą to już następny dzień |
| A-W-3 `OwnerWithdrawalAuditTests` | unit | `Created`/`Updated`/`Deleted`; obiekt `OwnerWithdrawal`, data biznesowa = data wypłaty, etykieta „Wypłata właściciela” bez osoby, rodzic `null`; `person` jako dane osobowe; kwota `2000.00`, metoda jako kod |
| I-W-1 `OwnerWithdrawalsApiTests` — CRUD | integration (SQL) | `201` + `Location`, `GET` z polami i znacznikami UTC, `PUT` → nowa wersja, `DELETE` `204` → `GET` `404` |
| I-W-2 idempotencja z wyścigiem | integration | dwa równoległe `POST` z jednym kluczem → jeden wiersz (dekorator store z barierą, wzór `ExpensesApiTests`) |
| I-W-3 powtórzenie i `KeyReused`; brak klucza | integration | `200` z tym samym `id`; inna treść → `409 idempotency_key_reused` z `ownerWithdrawalId`; brak/pusty/zły klucz → `400 idempotency_key_required` |
| I-W-4 walidacja | integration | `400` z kluczami `person`, `amount`, `withdrawnOn`, `method`; data jutro w Warsaw; data sprzed uruchomienia przyjęta |
| I-W-5 audyt | integration | wpisy w `reporting.AuditEntries` (`ObjectType = OwnerWithdrawal`) z operacją, etykietą, datą wypłaty, polami; `person` z `personal = true`; `Deleted` ze wszystkimi starymi wartościami |
| I-W-6 `PUT` bez zmian | integration | ta sama wersja w odpowiedzi, `UpdatedAtUtc` bez zmian, brak nowego wpisu audytu (decyzja 2) |
| I-W-7 współbieżność i wycofanie | integration | nieaktualna wersja → `409 owner_withdrawal_conflict` przy `PUT` i `DELETE`; zmiana „z innej karty” między odczytem a zapisem → `409`; błąd audytu wycofuje zmianę |
| I-W-8 wypłata nie jest wydatkiem | integration | po dodaniu wypłaty `GET /api/expenses` puste, `expenses.Expenses` puste; `GET /people` bez duplikatów wielkości liter, z ostatnio użytym zapisem, alfabetycznie; `CHECK` odrzuca `Amount = 0` i nieznaną metodę przy bezpośrednim `INSERT` |
| I-W-9 dostęp | integration | `401` bez sesji dla każdej trasy wypłat; zapis bez XSRF odrzucony |
| I-O-1 `OperationsApiTests` — wszystkie rodzaje | integration | wpłata, zwrot sprzedaży, zwrot pieniędzy, opłacony wydatek, wypłata na liście z poprawnym `kind`, `direction`, `amount`, `method`, `refundKind`, `date` |
| I-O-2 opis i źródło | integration | `description` = `sourceLabel`: numer zamówienia / opis wydatku / osoba; `category` dla wydatku; `sourceType` + `sourceId` (zamówienie dla wpłaty i zwrotu) — decyzja 7 |
| I-O-3 pominięte | integration | korekta sprzedaży, nieopłacony wydatek, zamówienie bez wpłat — brak na liście; po „Oznacz jako opłacony” wydatek pojawia się w okresie daty zapłaty, po „Cofnij” znika |
| I-O-4 statusy zamówień | integration | wpłaty do zamówienia anulowanego i do zamówienia ze sklepu w stanie pending/failed (`SeedShopOrderAsync` + wpłata) są na liście (decyzja 4) |
| I-O-5 sumy | integration | wypłata 2000 + wpłata 1000 + wydatek −50 → `inflows`, `outflows`, `change` z dokładnymi ciągami; filtr „expense” z samą korektą → `outflows` ujemne, `change` dodatnie; sumy takie same dla każdej strony i kierunku |
| I-O-6 data uruchomienia (URU-01) | integration | (launch 2026-11-03) wpłata 2026-11-03 do zamówienia z 2026-10-28 jest na liście; wpłata 2026-11-02 nie ma; zwrot i zapłata wydatku od uruchomienia do obiektów sprzed są; `effectiveFrom` = 2026-11-03 przy `from` = 2026-11-01 |
| I-O-7 okres przed uruchomieniem | integration | `from`/`to` w październiku → `200`, `items` puste, sumy `0.00`, `effectiveFrom` = `launchDate` (decyzja 8) |
| I-O-8 wydatek bez metody | integration | `method: null` |
| I-O-9 walidacja i strony | integration | `from > to` → `400` przy `from`; `page=0` → `400` przy `page`; strona za końcem → `200` z pustym `items` i prawdziwym `total`/`totals` (decyzja 1); `pageSize=101` → `400` |
| I-O-10 bezpiecznik | integration | 10 000 wypłat wstawionych SQL-em → `200`; 10 001 → `422 operations_range_too_large` z `detail`; 10 001 wpłat + filtr bez wypłat też `422` |
| I-O-11 rejestracja źródeł | integration | `GetServices<ICashOperationSource>()` w zakresie hosta: dwa źródła, rodzaje rozłączne, suma = wszystkie 4 rodzaje |
| I-O-12 dostęp | integration | `GET /api/operations` bez sesji → `401` |
| Regresja | integration | `ExpensesApiTests` (zmieniony `ExpensesTest.AuditRowsAsync`, `EndpointSupport`), `PaymentsApiTests` (migracja indeksów), `EndpointSecurityTests` |
| Architektura | arch | `StoneFlow.Architecture.Tests` bez zmian i bez wyjątków |
| F-O-1 `operations-api.spec.ts` | unit (Vitest) | adres, `kind` powtarzany, pozostałe parametry |
| F-O-2 `owner-withdrawals-api.spec.ts` | unit | nagłówek `Idempotency-Key`, `201`/`200`, ciało `DELETE`, `PUT` z `version`, `/people` |
| F-O-3 `operations.service.spec.ts` | unit | domyślny bieżący miesiąc (Warsaw); `periodInvalid` bez żądania; strona 1 po zmianie filtra; `422` → `rangeError`; `launchNotice` z `effectiveFrom`/`launchDate`; chipy i ich usuwanie; sumy przepisane z API bez zmian (brak arytmetyki) |
| F-O-4 `operations-list-page.spec.ts` | component | kolumny i etykiety (rodzaj z `refundKind`, kierunek tekstem, „Nie podano”, dopisek korekty); link „Opis” do źródła z `sourceLabel` i kategorią; filtry rodzaju z etykietami decyzji 9; sumy ze znakiem minus; stany pusty/filtrowany/błąd/ładowanie; „Dodaj wypłatę” jako akcja główna |
| F-W-1 `withdrawal-form-model.spec.ts` | unit | walidatory (osoba, kwota 0 / `0,00` / ujemna / 3 miejsca / 17 cyfr, data jutro, metoda), mapowanie na `WithdrawalInput`, `withdrawalErrorMessage` (network/5xx, csrf, detail, fallback) |
| F-W-2 `withdrawal-form.spec.ts` | component | błędy przy polach i podsumowanie, fokus na pierwszym błędzie, błąd API przypięty do pola, `datalist` z osobami, `select` bez domyślnej metody, blokada w trakcie zapisu |
| F-W-3 `owner-withdrawals.service.spec.ts` | unit | mapowanie `409`/`404`/`keyReused`/sieć; notatki |
| F-W-4 `withdrawal-create-page.spec.ts` | component | ten sam klucz przy ponowieniu po błędzie sieci; nowy po „Dodaj kolejną wypłatę”; przejście do szczegółów |
| F-W-5 `withdrawal-details-page.spec.ts` | component | stany ładowania/404/błąd z ponowieniem; edycja w miejscu; dialog „Usunąć wypłatę?”; po usunięciu `/operacje`; konflikt z „Wczytaj ponownie”; komunikat błędu sieci przy usunięciu |
| `app.routes.spec.ts`, `app-shell.spec.ts` | component | trasy `/operacje`, `/operacje/wyplaty/nowa` z tytułami; nagłówek powłoki bez nieobsłużonych żądań |

E2E: odroczone do SF-21 (brak infrastruktury E2E, jak w SF-15). Ręczne scenariusze UI trafią do
`06-implementation-done.md` (spec, Verification → manual).

## Verification

Pamięć na tej maszynie jest ograniczona: build z `-m:1`, polecenia kolejno, bez równoległych
ciężkich przebiegów (nie uruchamiać testów .NET i `npm run build` naraz).

Backend, z katalogu repozytorium (Git Bash):

```
dotnet restore api/tests/StoneFlow.Reporting.Tests          # jednorazowo: odświeża packages.lock.json po nowej referencji
dotnet build stone-flow.slnx -c Release -m:1
dotnet format stone-flow.slnx --verify-no-changes
dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Expenses.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release --no-build
export STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True'
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OwnerWithdrawalsApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OperationsApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*ExpensesApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*PaymentsApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*EndpointSecurityTests"
```

Po `dotnet ef migrations add …` przejrzeć wygenerowane `Up`/`Down` (tylko `CreateTable`/
`CreateIndex`). Pełny `dotnet test` i `deploy/scripts/test-migrate.sh` uruchamia CI.

Frontend, z `app/`, kolejno:

```
npm run typecheck
npm test -- --watch=false
npm run lint
npm run format:check
npm run build
```

Manual (ryzyko `high` — **przegląd człowieka przed merge**):

- scenariusze ręczne UI (dodanie, poprawa, usunięcie wypłaty; lista z filtrami, sumami, datą
  uruchomienia, przejściem do źródła i powrotem z zachowanym filtrem; telefon) zapisane w
  `06-implementation-done.md`;
- przegląd: `CashFlowPolicy`/`CashFlowTotals`, predykatów dat w obu źródłach (data wpisu, nie
  zamówienia; `PaidOn != null`), bezpiecznika 10 001, audytu `Deleted` przed `DELETE`, `CHECK` w
  migracji, rejestracji obu źródeł w DI.

## Risks

- **Decyzja 7 zmienia zdanie spec** (`sourceLabel` „Wypłata właściciela” → osoba) i uwagę w
  tabeli niezmienników spec. Niezmiennika nie narusza (osoba jest już w `description`), ale
  `02-spec.md` w tym miejscu przestaje się zgadzać z kodem; `06-implementation-done.md` powinien to
  odnotować jako odstępstwo z decyzji.
- **Znak minus U+2212 vs `-`.** Spec (UI → Dostępność) wspomina U+2212, a `formatPln` daje U+002D,
  tak jak lista wydatków, którą spec wskazuje jako wzór. Plan zostawia formater bez zmian (jedna
  ścieżka formatowania, brak regresji w innych widokach). Zmiana znaku w całej aplikacji byłaby
  osobnym zadaniem.
- **Decyzja 3 a rzeczywisty wzór.** Ogólny tekst w SF-15 to „Nie udało się zapisać wydatku.”, a
  dla sieci/5xx „Nie udało się połączyć z serwerem.”, nie „Nie udało się zapisać. Spróbuj
  ponownie.”. Plan powiela kod (decyzja: „reuse the expenses pattern”).
- **Łączenie w pamięci** (odstępstwo od „stronicowanie w bazie”) — uzasadnione w spec; przy
  wzroście skali bezpiecznik `422` ujawni potrzebę zmiany.
- **`ListPeopleAsync`:** `GroupBy` z wyborem rekordu w grupie musi przetłumaczyć się na SQL (EF
  Core 10 tłumaczy `GroupBy(...).Select(g => g.OrderByDescending(...).First())`). Jeśli nie, test
  I-W-8 to pokaże; wtedy zapytanie rozbić na dwa kroki w SQL, bez wczytywania wszystkich wypłat.
  Kolejność alfabetyczna wynika z collation bazy (polskie litery mogą sortować się inaczej niż w
  `pl-PL`).
- **Fallback `DbException` → `503`:** nieprzetłumaczone naruszenie `CHECK` dałoby `503`;
  walidacja poprzedza zapis, a I-W-8 sprawdza same ograniczenia.
- **Usuwanie fizyczne:** wypłata usunięta po utracie odpowiedzi i ponowione dodanie z tym samym
  kluczem utworzy nowy wiersz (jak w SF-15) — znane i akceptowane.
- **Wydajność testu I-O-10** (10 001 wierszy na LocalDB): wstawienie zbiorcze jednym `INSERT …
  SELECT` z tabeli liczb, nie przez API.
- **SF-20** ma użyć tych samych `ICashOperationSource`, `CashFlowPolicy` i `CashFlowTotals`; zmiana
  ich znaczenia po SF-17 wymaga przeglądu obu widoków.
