# Implementation Plan — SF-15

Tryb: **fullstack**. Zadanie dodaje tabelę, API i UI, a między warstwami powstaje nowy kontrakt.
Plany backendu i frontendu wyprowadzono ze spec niezależnie, każdy z własnym kontraktem, a potem
porównano je w „Integration Check”. Obie części napisał jeden autor po kolei: najpierw backend,
potem frontend z kontraktu w spec. Zadanie nie jest dzielone na części. Backend i frontend to
jedna funkcja o ryzyku `high`, ale da się je przejrzeć w jednym przeglądzie, a kolejność
wdrożenia (backend przed frontendem, migracja addytywna) i tak jest zachowana.

## Scope

Moduł Expenses dostaje wydatek jednorazowy:

- domena: encja, słowniki, reguła stanu „Planowany”/„Opłacony” i „Po terminie”, reguły kwot;
- przypadki użycia: dodanie (idempotentne), poprawa, „Oznacz jako opłacony”, „Cofnij oznaczenie
  zapłaty”, usunięcie, szczegóły, lista;
- tabela `expenses.Expenses` z migracją addytywną;
- endpointy `/api/expenses`;
- audyt każdej zmiany w `IUnitOfWork`.

Frontend dostaje sekcję `/wydatki`: listę, dodanie, szczegóły z akcjami i dialogami.
`todayInWarsaw` przechodzi do `shared/format`, a importy w Orders są aktualizowane. Dochodzi
nowy projekt testów `StoneFlow.Expenses.Tests` i testy integracyjne API.

Uwagi z przeglądu (`03-spec-review.md`) są rozstrzygnięte w `02-spec.md` (notka na górze,
Assumptions 5 i 16–20) i odwzorowane w krokach poniżej:

| Uwaga | Gdzie w planie |
|---|---|
| 1 | B7, test I-6 |
| 2 | B4 `ExpenseAudit` z rodzicem |
| 3 | B3, B10 |
| 4 | B1, B9, test D-2 |
| 5 | B4 |
| 6 | F1 |
| 7 | Risks |
| 8 | F6 |
| 9 | bez zmian |
| 10 | F5 |

## Backend Plan

### Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Domain | `Expenses.Domain/Expenses/` | encja `Expense`, słowniki, reguły stanu i kwot, pola audytu |
| Application | `Expenses.Application` | przypadki użycia, walidacja wejścia, kody, audyt, porty |
| Infrastructure | `Expenses.Infrastructure/Persistence` | konfiguracja EF, store i reader, migracja, DI |
| Api | `Expenses.Api` | endpointy, kontrakty, Problem Details |
| Tests | `StoneFlow.Expenses.Tests` (nowy), `StoneFlow.Api.IntegrationTests/Expenses` (nowy folder) | domena, walidacja, audyt; HTTP i baza |

### Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Modules/Expenses/StoneFlow.Expenses.Domain/Expenses/ExpenseVocabulary.cs` | create | `ExpenseCategory`, `ExpensePaymentMethod`, `ExpensePaymentStatus`, `ExpenseField`, `ExpenseFieldValue`, `ExpenseFieldChange` |
| `…/Expenses.Domain/Expenses/ExpensePayment.cs` | create | `ExpensePayment.Compute(paidOn, dueDate, today)` → `(Status, Overdue)`: jedyna reguła stanu |
| `…/Expenses.Domain/Expenses/ExpenseAmounts.cs` | create | `MaxAmount`, `Check(gross, net)` → `ExpenseAmountError` (brutto 0, znak netto, netto > brutto) |
| `…/Expenses.Domain/Expenses/Expense.cs` | create | encja, `ExpenseContent`, `CreateManual`, `Edit`, `MarkPaid`, `UnmarkPaid`, `Snapshot`, wyliczanie zmian, `ExpenseChange` z naruszeniem `AlreadyPaid`/`NotPaid` |
| `…/Expenses.Application/Abstractions/FieldErrors.cs` | create | kopia z Orders (moduł nie może referencjonować Orders) |
| `…/Expenses.Application/Abstractions/Ports.cs` | create | `StoredExpense`, `StoreSaveStatus`, `StoreSaveResult`, `IExpenseStore`, `ExpenseCreationMatch`, `IExpenseReader` |
| `…/Expenses.Application/WarsawTime.cs` | create | `Today(instant)` na `WarsawTimeZone.Zone` |
| `…/Expenses.Application/ExpenseCodes.cs` | create | kody camelCase kategorii, metod, stanów, pól |
| `…/Expenses.Application/ExpenseInput.cs` | create | `ExpenseInput` (tekst z JSON), walidacja → `ExpenseContent`, skrót SHA-256, `ExpenseVersion`, `ExpenseFieldKeys`, `ExpenseMessages`, parser kwot ze znakiem |
| `…/Expenses.Application/ExpenseAudit.cs` | create | `Created`/`Updated`/`Deleted`, format wartości, etykieta, opcjonalny rodzic |
| `…/Expenses.Application/ExpenseChange.cs` | create | wspólny wynik zmian (`ExpenseChangeOutcome`, `ExpenseChangeResult`) i pomocnik zapisu z audytem dla edycji i akcji zapłaty |
| `…/Expenses.Application/CreateExpense/CreateExpense.cs` | create | komenda, wynik, handler (wzór `CreateOrderHandler`) |
| `…/Expenses.Application/EditExpense/EditExpense.cs` | create | poprawa z wersją |
| `…/Expenses.Application/MarkExpensePaid/MarkExpensePaid.cs` | create | „Oznacz jako opłacony” |
| `…/Expenses.Application/UnmarkExpensePaid/UnmarkExpensePaid.cs` | create | „Cofnij oznaczenie zapłaty” |
| `…/Expenses.Application/DeleteExpense/DeleteExpense.cs` | create | usunięcie fizyczne z audytem `Deleted` |
| `…/Expenses.Application/GetExpense/GetExpense.cs` | create | `ExpenseDetailsDto` (stan z `ExpensePayment.Compute`), zapytanie i handler |
| `…/Expenses.Application/ListExpenses/ListExpenses.cs` | create | `ExpenseListItemDto` (stan z domeny), kryteria, walidacja parametrów, handler |
| `…/Expenses.Infrastructure/Persistence/ExpenseConfiguration.cs` | create | tabela, kolumny, `CHECK`, indeksy, `rowversion` |
| `…/Expenses.Infrastructure/Persistence/ExpensesDbContext.cs` | modify | `DbSet<Expense>`, `ApplyConfigurationsFromAssembly` |
| `…/Expenses.Infrastructure/Persistence/ExpenseStore.cs` | create | load/add/save/delete; tłumaczenie `DbUpdateConcurrencyException` i 2601/2627 |
| `…/Expenses.Infrastructure/Persistence/ExpenseReader.cs` | create | projekcje, filtry, sortowanie (puste na końcu), stronicowanie |
| `…/Expenses.Infrastructure/Persistence/Migrations/*_Expenses.cs` (+ Designer, snapshot) | create/modify | migracja `dotnet ef migrations add Expenses` |
| `…/Expenses.Infrastructure/DependencyInjection.cs` | modify | rejestracja store i reader |
| `…/Expenses.Api/ExpenseProblems.cs` | create | kody i teksty błędów |
| `…/Expenses.Api/Endpoints/ExpenseContracts.cs` | create | żądania i odpowiedzi, mapowanie |
| `…/Expenses.Api/Endpoints/ExpensesEndpoints.cs` | create | grupa `/api/expenses`, 7 tras |
| `…/Expenses.Api/ExpensesModule.cs` | modify | `MapExpensesEndpoints` mapuje grupę |
| `api/tests/StoneFlow.Expenses.Tests/StoneFlow.Expenses.Tests.csproj` (+ `packages.lock.json`) | create | nowy projekt testów |
| `api/tests/StoneFlow.Expenses.Tests/Domain/ExpenseTests.cs` | create | reguły domeny |
| `api/tests/StoneFlow.Expenses.Tests/Application/ExpenseInputTests.cs` | create | walidacja i skrót |
| `api/tests/StoneFlow.Expenses.Tests/Application/ExpenseAuditTests.cs` | create | format audytu |
| `api/tests/StoneFlow.Api.IntegrationTests/Expenses/ExpensesTest.cs` | create | baza testu, logowanie, pomocnicze wywołania |
| `api/tests/StoneFlow.Api.IntegrationTests/Expenses/ExpensesApiTests.cs` | create | testy HTTP i bazy |
| `stone-flow.slnx` | modify | projekt testów |

### Implementation steps

- **B1 Domena.**
  - `ExpenseAmounts.Check`: brutto ≠ 0. Netto `null` jest poprawne, `0` jest dozwolone. Gdy
    netto ≠ 0, ma znak brutto i `|netto| ≤ |brutto|`. Zakres `|x| ≤ 9 999 999 999 999 999,99`.
  - `ExpensePayment.Compute`: `Paid`, gdy jest `paidOn`; inaczej `Planned`, a
    `overdue = dueDate < today`.
  - `Expense`: prywatne settery, `CreateManual(id, content, requestId, hash, nowUtc)`.
    `Edit(content, now)` zwraca zmiany. `MarkPaid(paidOn, method, now)` daje `AlreadyPaid`, gdy
    `PaidOn` jest już ustawione; `UnmarkPaid(now)` daje `NotPaid`, gdy nie jest. Każda zmiana
    zwraca listę `ExpenseFieldChange` w stałej kolejności `Snapshot`. Przy braku zmian
    `UpdatedAtUtc` się nie zmienia. Domena nie sprawdza „dzisiaj”, bo robi to walidacja
    wejścia, a zapis z zadania SF-16 może mieć inne źródło czasu.
- **B2 Application: porty i pomocniki.** `Ports.cs` jak w Orders, `FieldErrors`. `WarsawTime`
  na `WarsawTimeZone.Zone`. `ExpenseCodes` (`TryParse`/`Of`).
- **B3 Walidacja wejścia.**
  - `ExpenseInput.Validate(errors, today)` zwraca `ExpenseContent?`:
    - teksty przycięte, puste jako `null`;
    - kwoty wzorcem `^-?[0-9]+(\.[0-9]+)?$`, skala ≤ 2, reguły `ExpenseAmounts` z kluczami
      `grossAmount`/`netAmount`;
    - daty `yyyy-MM-dd`, `paidOn ≤ today`;
    - długości z tabeli spec.
  - `ComputeRequestHash` liczy SHA-256 kanonicznego JSON-a.
  - `ExpenseVersion.Validate` (base64, 8 bajtów).
  - `ValidatePaid(paidOn, method, errors, today)` dla akcji.
- **B4 Audyt.** `ExpenseAudit.Created/Updated/Deleted(expense, changes, author, parent = null)`:
  - obiekt `Expense` z datą kosztu;
  - etykieta „Wydatek – {etykieta kategorii}”;
  - `payer` jako dane osobowe;
  - format `0.00` ze znakiem, data `yyyy-MM-dd`, kody słowników.
- **B5 Create.** Wzór `CreateOrderHandler`: walidacja (400), skrót, a w jednostce pracy
  `FindByCreationRequest`, `Add` i audyt `Created`. Zatwierdza się tylko `Created`. Po
  `DuplicateRequest` następuje ponowny odczyt po kluczu i rozstrzygnięcie `Repeated`/`KeyReused`.
- **B6 Edit.** Walidacja wersji i treści (400). W jednostce pracy kolejno:
  1. `Load` (404);
  2. porównanie wersji (409);
  3. `expense.Edit`;
  4. brak zmian: `Saved` ze starą wersją, bez zapisu;
  5. `Save(expected)`: `Conflict` daje 409;
  6. audyt `Updated`.

  Zatwierdzany jest tylko `Saved` ze zmianami. Wynik bez zmian niczego nie zapisuje, więc też
  nie wymaga commitu.
- **B7 Mark/Unmark.** Kolejność z poprawionej spec:
  1. walidacja (400);
  2. `Load` (404);
  3. **wersja (409 `Conflict`) przed regułą domeny**;
  4. `MarkPaid`/`UnmarkPaid`, który zwraca `AlreadyPaid`/`NotPaid` (409);
  5. zapis i audyt.

  Wspólny przebieg zapisu z audytem dla B6 i B7 trafia do `ExpenseChange.cs`.
- **B8 Delete.** Walidacja wersji (400), `Load` (404), wersja (409), audyt `Deleted` ze
  `Snapshot`, `Delete(expected)`. `Conflict` z bazy daje 409 i wycofanie, także wpisu audytu.
- **B9 Infrastructure.**
  - `ExpenseConfiguration`:
    - kolumny zgodnie ze spec;
    - `CK_Expenses_GrossAmount`;
    - `CK_Expenses_NetAmount` z poprawioną regułą netto 0;
    - `CK_Expenses_CreationRequest`;
    - `UX_Expenses_CreationRequestId` (filtrowany);
    - `IX_Expenses_CostDate` (`CostDate`, `Id`) i `IX_Expenses_PaidOn` (filtrowany);
    - `rowversion` jako właściwość cienia `Version`.
  - `ExpenseStore` jak `OrderStore`, plus `DeleteAsync`.
  - `ExpenseReader`:
    - filtry z poprawionej spec (jednostronne zakresy, `state` jako jedyny predykat SQL stanu);
    - sortowanie: daty `dueDate`/`paidOn` z pustymi zawsze na końcu
      (`OrderBy(x => x.DueDate == null)`), potem wartość, potem `Id`;
    - projekcja surowych pól. Stan liczy DTO przez `ExpensePayment.Compute`.
  - Migracja: `dotnet ef migrations add Expenses`, która tworzy tylko tabelę i indeksy.
- **B10 Read side.**
  - `ListExpensesHandler`: walidacja parametrów jak `ListOrdersHandler` (page, pageSize ≤ 100,
    sort, direction, daty, `category[]`, `state`, `sign`, `search ≤ 100`). `today` pochodzi
    z `TimeProvider`.
  - Pozycje mają `Status`/`Overdue` z `ExpensePayment.Compute(PaidOn, DueDate, today)`.
  - `GetExpenseHandler`: ta sama funkcja.
- **B11 Api.**
  - Endpointy ze spec, `WithName("Expenses.*")`, konto z `ClaimTypes.NameIdentifier`.
  - `Idempotency-Key`: jedna wartość UUID ≠ `Guid.Empty`.
  - `DELETE` przyjmuje `[FromBody] { version }` i zwraca `204`.
  - Wynik dyspozytora mapuje się na `TypedResults` i `ExpenseProblems`.
  - Bez nowych endpointów anonimowych.
- **B12 Tests.** Patrz „Tests”.

### Data & contracts

- **Storage:** `expenses.Expenses` (spec „Storage”). Migracja addytywna, a schemat `expenses`
  i wpis w `contexts.sh` już istnieją.
- **API:** patrz „API Contract”.
- **Shared types:** brak nowych. Używane są istniejące: `IUnitOfWork`, `IAuditLog`,
  `AuditObjectType.Expense`, `WarsawTimeZone`.

### Edge cases & null safety

| Edge case (spec) | Gdzie |
|---|---|
| brutto 0 / `-0` / puste; netto 0 przyjęte; znak; netto > brutto; skala; zakres | `ExpenseInput` + `ExpenseAmounts`, `CHECK` w bazie |
| `paidOn` jutro; granica 23:30 UTC | `ExpenseInput.Validate(today)`, `WarsawTime.Today` |
| data kosztu sprzed uruchomienia, zapłata przed kosztem | brak reguły (przyjęte) |
| termin dzisiaj / wczoraj / opłacony | `ExpensePayment.Compute` |
| nieaktualna wersja → `expense_conflict` przed `already_paid`/`not_paid` | B7 |
| edycja bez zmian | B6 |
| usunięcie zmienionego (409) / usuniętego (404) | B8, `ExpenseStore.DeleteAsync` |
| klucz pusty / nie-UUID / wielokrotny | `ExpensesEndpoints.IdempotencyKey` |
| `%`, `_`, `[` w wyszukiwaniu; > 100 znaków | `ExpenseReader.EscapeLike`, `ListExpensesHandler` |
| zakres jednostronny; `state=planned` + `paidOnFrom` daje pusto | `ExpenseReader.Filter` |
| strona poza zakresem | `ExpenseReader.ListAsync` (pusta lista, prawdziwy `total`) |
| `paidOn` `null` przy `Paid` niemożliwe | `ExpensePayment.Compute` przyjmuje `DateOnly?`, a stan wynika tylko z niego |

## Frontend Plan

### Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Feature | `app/src/app/features/expenses/` | lista, dodanie, szczegóły, formularze, dialogi |
| Shared | `app/src/app/shared/format/pl-format.ts` | `todayInWarsaw`, `parseSignedPlnAmount` |
| Feature | `app/src/app/features/orders/` | importy `todayInWarsaw` z nowego miejsca |
| Routing | `app/src/app/app.routes.ts` (+ spec) | `loadChildren` dla `wydatki` |

### Files

| File path | Action | Purpose |
|---|---|---|
| `app/src/app/shared/format/pl-format.ts` (+ `pl-format.spec.ts`) | modify | `todayInWarsaw` przeniesione, nowy `parseSignedPlnAmount` |
| `app/src/app/features/orders/order-form-model.ts` | modify | usunięcie `todayInWarsaw` i import z `shared/format` |
| `app/src/app/features/orders/entry-form-model.ts`, `payment-form.ts`, `refund-form.ts`, `sales-correction-form.ts` | modify | import `todayInWarsaw` z `shared/format` |
| `app/src/app/features/orders/payment-form.spec.ts`, `refund-form.spec.ts`, `sales-correction-form.spec.ts` | modify | jw. |
| `app/src/app/features/expenses/expense-models.ts` | create | typy kontraktu, kody, etykiety, tony, `EXPENSE_PROBLEM` |
| `app/src/app/features/expenses/expenses-api.ts` (+ spec) | create | klient `/api/expenses` |
| `app/src/app/features/expenses/expenses.service.ts` (+ spec) | create | stan listy na sygnałach, przepływy zapisu i akcji |
| `app/src/app/features/expenses/expense-form-model.ts` (+ spec) | create | model, walidacja UI, mapowanie na `ExpenseInput` |
| `app/src/app/features/expenses/expense-form.ts`, `.html` (+ spec) | create | formularz dodania i edycji |
| `app/src/app/features/expenses/expense-paid-form.ts`, `.html` | create | formularz „Oznacz jako opłacony” |
| `app/src/app/features/expenses/expenses-list-page.ts`, `.html` (+ spec) | create | `/wydatki` |
| `app/src/app/features/expenses/expense-create-page.ts`, `.html` | create | `/wydatki/nowy` |
| `app/src/app/features/expenses/expense-details-page.ts`, `.html` (+ spec) | create | `/wydatki/:id` |
| `app/src/app/features/expenses/expenses.routes.ts` | create | trasy potomne |
| `app/src/app/features/expenses/expenses-page.ts`, `.html` | delete | placeholder |
| `app/src/app/app.routes.ts`, `app.routes.spec.ts` | modify | `loadChildren`, oczekiwany komponent `ExpensesListPage` |

### Implementation steps

- **F1** `pl-format.ts`:
  - `todayInWarsaw` przenoszę bez zmian;
  - `parseSignedPlnAmount`: opcjonalny `-` (także `−` U+2212) plus `parsePlnAmount`, wynik
    `-1234.50`; `-0` daje `0.00`, a wtedy walidacja zera je odrzuca;
  - importy w Orders przechodzą na nowe miejsce.
- **F2** `expense-models.ts`: typy `ExpenseInput`, `ExpenseDetails`, `ExpenseListItem`,
  `ExpenseListPage`, `ExpenseListQuery`, `ExpenseCategory`, `ExpensePaymentMethod`,
  `ExpensePaymentStatus`, `ExpenseStateFilter`, `ExpenseSignFilter` oraz etykiety
  i `EXPENSE_PROBLEM`.
- **F3** `expenses-api.ts`: `list`, `get`, `create` (nagłówek `Idempotency-Key`,
  `observe: 'response'`), `update`, `markPaid`, `unmarkPaid`, `delete` (`body: { version }`).
- **F4** `expenses.service.ts`, wzór `OrdersService`:
  - sygnały strony, sortowania i filtrów; `activeFilters`, `registryEmpty`, `dateRangeInvalid`
    (obie pary dat);
  - `load` z sekwencją żądań;
  - `loadExpense`, `create` (`saved`/`keyReused`/`error`), `update`/`markPaid`/`unmarkPaid`/
    `delete` dające `saved`/`conflict`/`notFound`/`stateChanged`/`error`;
  - `takeNotice`.
- **F5** `expenses-list-page`:
  - `app-data-table` z `filterable`, który na telefonie daje panel „Filtry”;
  - filtry: szukaj, data kosztu od/do, data zapłaty od/do, kategorie (checkboxy), stan
    (select), znak (select);
  - kolumny ze spec; komórki `description` (link plus etykieta korekty) i `state` (odznaki);
  - `filteredEmptyTitle` zależy od tego, czy jest aktywny filtr daty.
- **F6** `expense-form-model.ts`:
  - model tekstowy, `emptyExpenseForm(today)`, `expenseFormFrom(details)`, `toExpenseInput`;
  - walidatory: opis, kategoria, data kosztu, kwota brutto (format, ≠ 0, zakres cyfr), netto
    (format, znak niezerowego netto), `paidOn ≤ today`, długości;
  - `isNegativeAmount(text)` i teksty `NEGATIVE_LABEL`, `NEGATIVE_HINT`, `paidHint(negative)`.
- **F7** `expense-form`:
  - Signal Forms, sekcje „Wydatek”, „Kwoty”, „Daty”, „Płatność i dokument”, „Komentarz”;
  - podsumowanie błędów i błędy API przypinane po kluczu;
  - wejście `confirmClearPaid: () => Promise<boolean>` jest wywoływane przed zapisem, gdy
    `initial.paidOn` było ustawione, a teraz jest puste. Potwierdzenie żyje tylko w UI.
- **F8** `expense-paid-form`: data (domyślnie dzisiaj, `max`), metoda. Opis zależy od znaku:
  „Wydatek trafi do wypływów…” albo „Korekta / zwrot od dostawcy obniży wypływy…”.
- **F9** `expense-create-page`: klucz `crypto.randomUUID()` przy otwarciu, obsługa
  `keyReused`, przejście do szczegółów.
- **F10** `expense-details-page`:
  - stany `loading`/`notFound`/`error`/`loaded`;
  - akcje „Edytuj”, „Oznacz jako opłacony” / „Cofnij oznaczenie zapłaty”, „Usuń”, „Wróć do
    listy”;
  - dialogi: usunięcie (tekst neutralny) i cofnięcie zapłaty, ten drugi użyty też przez
    `confirmClearPaid`;
  - alert konfliktu z „Wczytaj ponownie”, komunikaty `aria-live`.
- **F11** Trasy: `expenses.routes.ts`, `app.routes.ts` (`loadChildren`) i aktualizacja
  `app.routes.spec.ts`. Placeholder do usunięcia.

### Edge cases & null safety

| Edge case | Gdzie |
|---|---|
| kwota `-1 234,50`, `1234,5`, `0`, `-0` | `parseSignedPlnAmount`, `expense-form-model` |
| netto 0 przyjęte, znak niezerowego netto | `expense-form-model` |
| `|netto| > |brutto|` | tylko API: błąd przypięty do `netAmount` |
| data zapłaty po dzisiaj | walidator + `max` w polu |
| wyczyszczenie daty zapłaty | `expense-form` → `confirmClearPaid` |
| 409 konflikt / stan zmieniony | `ExpensesService.change` → alert w szczegółach |
| 404 | `notFound` w szczegółach |
| pola opcjonalne `null` z API | wyświetlane przez `formatDate`/`formatPln` jako „Brak danych” |

## API Contract

| Method | Route | Request | Response |
|---|---|---|---|
| GET | `/api/expenses` | `page`, `pageSize`, `sort` (`costDate`/`dueDate`/`paidOn`/`grossAmount`), `direction`, `costDateFrom`, `costDateTo`, `paidOnFrom`, `paidOnTo`, `category[]`, `state` (`planned`/`paid`/`overdue`), `sign` (`positive`/`negative`), `search` | `200 { items: ExpenseListItem[], page, pageSize, total }`; `400` |
| GET | `/api/expenses/{id}` | — | `200 ExpenseDetails`; `404 expense_not_found` |
| POST | `/api/expenses` | `Idempotency-Key`; `ExpenseInput` | `201 { id, version }` + `Location`; `200 { id, version }`; `400`; `409 idempotency_key_reused { expenseId }` |
| PUT | `/api/expenses/{id}` | `ExpenseInput & { version }` | `200 { version }`; `400`; `404`; `409 expense_conflict` |
| POST | `/api/expenses/{id}/mark-paid` | `{ paidOn, paymentMethod, version }` | `200 { version }`; `400`; `404`; `409 expense_conflict` / `expense_already_paid` |
| POST | `/api/expenses/{id}/unmark-paid` | `{ version }` | `200 { version }`; `400`; `404`; `409 expense_conflict` / `expense_not_paid` |
| DELETE | `/api/expenses/{id}` | body `{ version }` | `204`; `400`; `404`; `409 expense_conflict` |

Typy:

- `ExpenseInput = { description, category, counterparty, costDate, dueDate, paidOn,
  grossAmount, netAmount, documentReference, paymentMethod, payer, comment }`. Opcjonalne pola
  mają wartość `string | null`.
- `ExpenseListItem = { id, description, category, counterparty, costDate, dueDate, paidOn,
  grossAmount, paymentStatus, overdue }`.
- `ExpenseDetails = ExpenseInput & { id, paymentStatus, overdue, createdAtUtc, updatedAtUtc,
  version }`.

Kwoty są stringami `"-1234.50"`, daty `yyyy-MM-dd`. Wszystkie trasy wymagają sesji, a zapisy
XSRF.

## Integration Check

| Frontend expects | Backend provides | Gap |
|---|---|---|
| `POST /api/expenses` z `Idempotency-Key`, `201`/`200 { id, version }`, `409` z `expenseId` | B5, B11 | — |
| `PUT` z `version` w ciele | B6 | — |
| `mark-paid { paidOn, paymentMethod, version }` | B7 | — |
| `unmark-paid { version }` | B7 | — |
| `DELETE` z `{ version }` w ciele, `204` | B8, B11 | — |
| lista z `category` wielokrotnym, `state`, `sign`, jednostronnymi zakresami | B10, B9 | — |
| `paymentStatus` / `overdue` w pozycji i szczegółach | B10 (domena) | — |
| klucze błędów `grossAmount`, `netAmount`, `paidOn`, `version`… | B3 | — |

No integration gaps.

## Invariant Check

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | `ExpensePayment.Compute` (stan tylko z `PaidOn`); `ExpenseInput` odrzuca `paidOn > today`; SF-15 nie tworzy wypływów | D-1, A-2 (granica 23:30 UTC), I-5 (przyszła data zapłaty → 400, stan „planned”) |
| 2. Decimal po stronie backendu | `decimal(18,2)`, parser tekstowy bez zaokrągleń; frontend tylko formatuje i sprawdza tekstowo | A-1, D-2, F-1 (`parseSignedPlnAmount` bez `Number`) |
| 3. Dostęp egzekwowany w API | polityka domyślna, XSRF, brak `AllowAnonymous` | I-9, `EndpointSecurityTests` (bez zmian w `AllowedAnonymous`) |
| 4. Ponowienia nie duplikują | `UX_Expenses_CreationRequestId`, handler `CreateExpense` | I-2 (wyścig równoległych żądań), I-3 (`KeyReused`) |
| 5. Audyt finansowy | `ExpenseAudit` w każdej komendzie w `IUnitOfWork` | A-3, I-7 (wpisy `Created`/`Updated`/`Deleted`), I-8 (błąd audytu wycofuje zmianę) |
| 6. Sekrety po stronie serwera | brak sekretów; Problem Details bez danych | n/d |
| 7. Granice modułów i warstw | brak referencji do Orders; Application bez EF | `StoneFlow.Architecture.Tests` |
| 8. Brak danych identyfikujących | fikcyjne dane w testach („Dostawca A”), bez stałych przypominających sekrety (gitleaks) | przegląd |

## Unified Implementation Order

1. B1 domena → D-testy.
2. B2–B4 Application (porty, walidacja, kody, audyt) → A-testy.
3. B5–B8, B10 handlery.
4. B9 Infrastructure + migracja.
5. B11 Api.
6. Testy integracyjne I-*.
7. F1 `shared/format` + importy Orders.
8. F2–F4 modele, klient, serwis.
9. F6–F8 formularze.
10. F5, F9, F10 strony.
11. F11 trasy.
12. Testy frontendu.

## Tests

| Test | Type | What it proves |
|---|---|---|
| D-1 `ExpensePayment.Compute` | unit (Expenses.Tests) | `Paid` z datą; `Planned` bez; „Po terminie” tylko przy `dueDate < today` i bez daty zapłaty |
| D-2 `ExpenseAmounts.Check` | unit | brutto 0 odrzucone; netto 0 dozwolone; znak; `|netto| ≤ |brutto|`; zakres |
| D-3 `Expense.Edit/MarkPaid/UnmarkPaid` | unit | zmienione pola w kolejności; brak zmian bez `UpdatedAtUtc`; `AlreadyPaid`/`NotPaid` |
| A-1 `ExpenseInput` | unit | każda reguła walidacji, klucze JSON, normalizacja, skrót niezależny od białych znaków |
| A-2 `paidOn` i granica dnia | unit | 23:30 UTC zimą to już następny dzień |
| A-3 `ExpenseAudit` | unit | `Created`/`Updated`/`Deleted`, `payer` jako dane osobowe, etykieta „Wydatek – Cięcie”, kwota `-1234.50`, rodzic `null` |
| I-1 CRUD | integration (SQL) | `201` + `Location`, `GET`, `PUT`, `DELETE` `204` → `404` |
| I-2 idempotencja z wyścigiem | integration | dwa równoległe `POST` z jednym kluczem dają jeden wiersz |
| I-3 `KeyReused` i brak klucza | integration | `409` z `expenseId`; `400 idempotency_key_required` |
| I-4 walidacja | integration | `400` z kluczami `grossAmount`, `netAmount`, `paidOn`; netto 0 przyjęte |
| I-5 akcje zapłaty | integration | mark → `paid`; unmark → `planned`; przyszła data → 400 |
| I-6 kolejność 409 | integration | stara wersja daje `expense_conflict`; aktualna wersja daje `expense_already_paid`/`expense_not_paid` |
| I-7 audyt | integration | wpisy w `reporting.AuditEntries` z operacją, etykietą, datą kosztu i zakresem |
| I-8 wycofanie audytu | integration | port audytu rzuca wyjątek → brak wydatku i brak zmiany |
| I-9 dostęp | integration | `401` bez sesji dla każdej trasy; zapis bez XSRF → `400 csrf_invalid` |
| I-10 lista | integration | filtry (zakresy jednostronne, kategorie, stan, znak, tekst z `%`), sortowanie z pustymi na końcu, stronicowanie, zgodność filtra `state` z `paymentStatus`/`overdue` |
| I-11 `CHECK` | integration | bezpośredni `INSERT` z brutto 0 albo złym netto jest odrzucany przez bazę |
| F-1 `pl-format` | unit (Vitest) | `parseSignedPlnAmount`, `todayInWarsaw` |
| F-2 `expense-form-model` | unit | walidatory, mapowanie, netto 0, znak |
| F-3 `expenses-api` | unit | adresy, nagłówek, ciało `DELETE`, parametry listy |
| F-4 `expenses.service` | unit | mapowanie wyników 409/404, filtry aktywne, zakresy dat |
| F-5 `expenses-list-page` | component | pusta ewidencja, kolumny, etykieta korekty, odznaki stanu |
| F-6 `expense-details-page` | component | akcje zależne od stanu, dialog usunięcia, cofnięcie, konflikt |
| F-7 `expense-form` | component | błąd brutto 0, potwierdzenie przy wyczyszczeniu daty zapłaty |

E2E: **odroczone** do SF-21. W repozytorium nie ma infrastruktury E2E (spec, Assumptions 18).
Odroczenie trafi do otwartych punktów `06-implementation-done.md`.

## Verification

Kolejno, z ograniczoną pamięcią (`-m:1`):

```
dotnet build stone-flow.slnx -c Release -m:1
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet test --project api/tests/StoneFlow.Expenses.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release --no-build
STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True' \
  dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- \
  --filter-namespace "*Expenses*"   (+ osobno *EndpointSecurityTests, *OrdersApiTests)
cd app && npm run lint && npm test -- --watch=false && npm run build && npm run typecheck && npm run format:check
```

Manual (ryzyko `high`, przegląd człowieka przed merge):

- przejście scenariuszy z sekcji „Verification → manual” spec;
- przegląd kodu domeny stanu i kwot, `CHECK` w migracji, kolejności `409` i audytu `Deleted`.

## Risks

- **E2E odroczone.** Proces „zapisz wydatek” nie ma testu end to end do czasu SF-21.
- **Fallback `DbException` → `503`.** Nieprzetłumaczone naruszenie `CHECK` daje 503. Walidacja
  poprzedza zapis, a I-11 sprawdza samą bazę.
- **SF-16** musi zapisywać rejestr par definicja–okres przy tworzeniu wystąpienia (spec
  „Kontrakt dla SF-16”). W przeciwnym razie usunięte wystąpienie wróci.
- **Usuwanie fizyczne:** wydatek usunięty po utracie odpowiedzi i ponowione dodanie z tym samym
  kluczem utworzy nowy wiersz (spec Edge Cases). Ryzyko jest znane i akceptowane.
