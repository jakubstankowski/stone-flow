# Implementation Plan — SF-16

## Scope

Plan realizuje `02-spec.md` w wersji 6 z poprawką redakcyjną. Spec przeszła przegląd v6 z wynikiem
PASS WITH ISSUES. Uwagi NC1–NC6 z tego przeglądu są wpisane w spec, a plan je realizuje.

Zakres:

- **Backend, moduł Expenses:**
  - domena terminów (historia dni, wyłączenia, kwalifikacja, terminy, stan);
  - encje: definicja, rejestr okresów, klucze żądań;
  - migracja addytywna w `ExpensesDbContext`;
  - przypadki użycia CQRS, zadanie `recurring-expenses` i endpointy `/api/recurring-expenses`;
  - zmiany w SF-15: rodzic w audycie wystąpienia, filtr `recurringDefinitionId`, pola
    cykliczności.
- **Frontend `features/expenses`:**
  - opcja „Powtarzaj co miesiąc” z podglądem;
  - lista i szczegóły wydatków cyklicznych z dialogami;
  - `recurring-job-notice`;
  - oznaczenie „Cykliczny” i filtr definicji na liście wydatków.
- **Dokumentacja:** PRD §5 jest już zaktualizowane tekstem ze spec.

Tryb planu to fullstack (storage, API i UI). Zadanie nie jest dzielone na części: spec jest jedna,
a backend i frontend łączy jeden kontrakt. Implementują je równolegle dwaj wykonawcy: backend
i frontend, oba według sekcji „API Contract”.

## Backend Plan

### Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Modules/Expenses/StoneFlow.Expenses.Domain/Recurring/RecurringPeriod.cs` | create | `readonly record struct RecurringPeriod(int Year, int Month)`: `FirstDay`, `Of(DateOnly)`, `Next()`, `Previous()`, porównanie, `DaysInMonth` |
| `.../Domain/Recurring/RecurringSchedule.cs` | create | czyste funkcje reguł 2–7: `DayOf`, `DueDate`, `Qualifies`, `DuePeriods`, `NextUncreatedDueDate`, `NextDueDateAfter`, `LastDueDate`, `HasEffectiveDueInRange`, `Status` |
| `.../Domain/Recurring/RecurringExpenseDefinition.cs` | create | encja z treścią, `StartsOn`, `EndsOn`, `PausedOn`, `DayOfMonth`, `LastDueDate`, kolekcje `Days` i `Exclusions`; `Create`, `Edit`, `Pause`, `Resume`, `Snapshot`/`Changed` |
| `.../Domain/Recurring/RecurringExpenseDay.cs`, `RecurringExpenseExclusion.cs` | create | części definicji (EF owned/child) |
| `.../Domain/Recurring/RecurringExpensePeriod.cs` | create | wiersz rejestru (definicja, okres, termin, id wydatku, czas) |
| `.../Domain/Recurring/RecurringDefinitionRequest.cs` | create | klucz idempotencji dodania |
| `.../Domain/Recurring/RecurringVocabulary.cs` | create | `RecurringDefinitionStatus`, `RecurringExclusionReason` (`Pause`, `StartMoved`), `RecurringDefinitionField`, `RecurringContent`, `RecurringScheduleInput`, `RecurringRuleViolation` (`AlreadyPaused`, `NotPaused`, `Ended`, `PausedDayChange`, `NoDueInRange`), zestaw zmian |
| `.../Domain/Expenses/Expense.cs` | modify | `RecurringDefinitionId`, `RecurringPeriod` (`DateOnly?`), `CreateOccurrence(...)` |
| `.../Application/Abstractions/RecurringPorts.cs` | create | `IRecurringDefinitionLock`, `IRecurringExpenseStore`, `IRecurringExpenseReader` i ich rekordy wyników |
| `.../Application/Recurring/RecurringExpenseInput.cs` | create | walidacja pól bez stanu (używa `ExpenseInput.Text`, parsowania kwot i dat: parser kwot z `ExpenseInput` udostępniony jako `internal static`), skrót żądania |
| `.../Application/Recurring/RecurringExpenseAudit.cs` | create | wpisy audytu definicji (etykieta „Wydatek cykliczny – {kategoria}”, data biznesowa = `StartsOn`) |
| `.../Application/Recurring/OccurrenceGenerator.cs` | create | operacja modułu: okresy należne → `Expense.CreateOccurrence`, rejestr, audyt `Created` (`AutomatedJob`, rodzic), wsady po 100; wynik `Created(n)` albo `DuplicateRequest` |
| `.../Application/Recurring/RecurringCodes.cs` | create | kody API: stan (`active`/`paused`/`ended`), problemy, pola audytu |
| `.../Application/CreateRecurringExpense/CreateRecurringExpense.cs` | create | komenda i handler (idempotencja przez `RecurringDefinitionRequests`) |
| `.../Application/EditRecurringExpense/EditRecurringExpense.cs` | create | `PUT` z kolejnością NC3 |
| `.../Application/PauseRecurringExpense/…`, `ResumeRecurringExpense/…`, `DeleteRecurringExpense/…` | create | akcje |
| `.../Application/GetRecurringExpense/…`, `ListRecurringExpenses/…`, `PreviewRecurringExpense/…` | create | zapytania (podgląd na kopii stanu) |
| `.../Application/GenerateRecurringOccurrences/…`, `ListRecurringGenerationCandidates/…` | create | komenda jednej definicji i zapytanie fazy odczytu zadania |
| `.../Application/RecurringChangeResult.cs` | create | wspólny wynik zmian definicji (`Saved`, `ValidationFailed`, `NotFound`, `Conflict`, `AlreadyPaused`, `NotPaused`, `Ended`, `PausedDayChange`, `Busy`) z `Version` i `CreatedOccurrences` |
| `.../Application/ExpenseChange.cs`, `DeleteExpense/DeleteExpense.cs` | modify | rodzic audytu dla wystąpienia |
| `.../Application/ExpenseAudit.cs` | modify | metoda `ParentOf(Expense)` |
| `.../Application/ListExpenses/ListExpenses.cs`, `GetExpense/GetExpense.cs`, `Abstractions/Ports.cs` | modify | filtr `recurringDefinitionId`, pola `RecurringDefinitionId` / `recurring` |
| `.../Application/ExpenseInput.cs` | modify | parser kwot i dat jako `internal static` do ponownego użycia (bez zmiany zachowania) |
| `.../Infrastructure/Persistence/RecurringExpenseDefinitionConfiguration.cs` | create | tabela definicji z `CHECK`, indeksami i kolekcjami `RecurringExpenseDays` i `RecurringExpenseExclusions` (FK kaskadowe) |
| `.../Infrastructure/Persistence/RecurringExpensePeriodConfiguration.cs`, `RecurringDefinitionRequestConfiguration.cs` | create | rejestr (PK, unikalny `ExpenseId`, FK kaskadowe) i żądania |
| `.../Infrastructure/Persistence/ExpenseConfiguration.cs` | modify | dwie kolumny, `CK_Expenses_Recurring`, filtrowany indeks |
| `.../Infrastructure/Persistence/ExpensesDbContext.cs` | modify | nowe `DbSet` |
| `.../Infrastructure/Persistence/RecurringDefinitionLock.cs` | create | `SqlQuery … WITH (UPDLOCK, ROWLOCK)` jak `OrderLock` |
| `.../Infrastructure/Persistence/RecurringExpenseStore.cs`, `RecurringExpenseReader.cs` | create | porty; tłumaczenia `2601/2627`, `547`, `DbUpdateConcurrencyException` |
| `.../Infrastructure/Persistence/ExpenseReader.cs` | modify | filtr i pola cykliczności |
| `.../Infrastructure/Persistence/Migrations/*_RecurringExpenses.cs` (+ Designer, snapshot) | create | migracja addytywna (`dotnet ef migrations add RecurringExpenses`) |
| `.../Infrastructure/Recurring/RecurringExpensesJob.cs` | create | `IScheduledJob`: faza odczytu, zakres DI na definicję, jedno ponowienie przy `DuplicateRequest` albo `1205`, zbieranie błędów |
| `.../Infrastructure/DependencyInjection.cs` | modify | rejestracja portów i `AddScheduledJob<RecurringExpensesJob>` |
| `.../Api/Endpoints/RecurringExpensesEndpoints.cs`, `RecurringExpenseContracts.cs` | create | grupa `/api/recurring-expenses` |
| `.../Api/Endpoints/ExpensesEndpoints.cs`, `ExpenseContracts.cs` | modify | parametr `recurringDefinitionId`, pola odpowiedzi |
| `.../Api/ExpenseProblems.cs`, `ExpensesModule.cs` | modify | nowe kody, mapowanie grupy |
| `api/tests/StoneFlow.Expenses.Tests/Domain/RecurringScheduleTests.cs`, `RecurringExpenseDefinitionTests.cs` | create | reguły i skutki z reguły 8 oraz NC1–NC6 |
| `api/tests/StoneFlow.Expenses.Tests/Application/RecurringExpenseInputTests.cs`, `RecurringExpenseAuditTests.cs` | create | walidacja i audyt |
| `api/tests/StoneFlow.Api.IntegrationTests/Expenses/RecurringExpensesTest.cs`, `RecurringExpensesApiTests.cs`, `RecurringExpensesJobTests.cs` | create | HTTP, baza i zadanie |
| `api/tests/StoneFlow.Api.IntegrationTests/Expenses/ExpensesApiTests.cs` | modify | rodzic audytu wystąpienia, filtr listy |

### Domain design (reguły 1–8 spec)

- **Historia dni** to lista `(FromPeriod, Day)` posortowana rosnąco.
  - `DayOf(p)`: ostatni wpis z `FromPeriod ≤ p`, a gdy żadnego nie ma, pierwszy wpis.
  - Zmiana dnia przy bieżącym okresie C usuwa wpisy `≥ C` i dodaje `(C, nowy)`.
  - `DayOfMonth` = dzień ostatniego wpisu.
- **Wyłączenia** to `(From, To, Reason)` z zakresem półotwartym.
  - Wznowienie dodaje `[PausedOn, dzisiaj)`, o ile przedział jest niepusty.
  - Przesunięcie początku na wcześniej dodaje `[nowy, min(stary, dzisiaj))`, o ile jest niepusty.
- **Kwalifikacja:**
  - warunki: nie wstrzymana, `StartsOn ≤ t ≤ EndsOn`, `t` poza wyłączeniami, okresu nie ma
    w rejestrze;
  - należny oznacza dodatkowo `t ≤ D`;
  - kandydaci to okresy od okresu `StartsOn` do okresu `min(D, EndsOn)`;
  - dla `nextDueDate` iteracja idzie do pierwszego trafienia albo do okresu `EndsOn`; bez końca
    obowiązuje twardy limit 1200 okresów (zakres 2000–2099).
- **Termin efektywny:** `DueDate` z rejestru dla okresów zarejestrowanych, w pozostałych przypadkach
  `t(p)`. Z niego liczy się `LastDueDate` (najpóźniejszy w zakresie) i walidacja „co najmniej jeden
  termin w zakresie”.
- **Stan:** `Ended`, gdy `LastDueDate ≤ D`; w przeciwnym razie `Paused`, gdy `PausedOn` jest
  ustawione; w pozostałych przypadkach `Active`.
- **Operacje encji.** Zwracają zmienione pola albo naruszenie reguły. Rejestr dostają jako
  `IReadOnlyDictionary<RecurringPeriod, DateOnly>`, potrzebny do `LastDueDate` i walidacji.
  - `Edit(content, schedule, today, registry, now)`:
    1. `PausedDayChange`, gdy definicja jest wstrzymana, a dzień się różni;
    2. stan po zmianie liczony na kopii;
    3. `NoDueInRange`, gdy w zakresie nie ma żadnego terminu;
    4. zastosowanie zmian.
  - `Pause(today)`: `AlreadyPaused` albo `Ended`.
  - `Resume(today)`: `NotPaused`.
- **Podgląd** stosuje `Edit` do kopii (`Clone()`) bez zapisu.

### Application flow

Kolejność kroków jest taka jak w spec, sekcja „Blokada i kolejność kroków”, a dla `PUT` jak w NC3.

- **Blokada wiersza.** `IRecurringDefinitionLock.LockAsync(id)` wykonuje SQL z blokadą wiersza
  i zwraca `bool`. Potem `IRecurringExpenseStore.LoadAsync(id)` wczytuje encję śledzoną z dniami,
  wyłączeniami i wersją.
- **`OccurrenceGenerator.GenerateAsync(definition, today, ct)`:**
  1. wczytuje rejestr: `store.RegisteredPeriodsAsync`;
  2. liczy okresy należne;
  3. dla każdego tworzy wystąpienie: `Expense.CreateOccurrence` z nowym `Guid`;
  4. przekazuje wsady po 100 do `store.AddOccurrencesAsync(batch)`. Port dodaje wydatki i wiersze
     rejestru, a potem wywołuje `SaveChanges`;
  5. zapisuje audyt przez `IAuditLog` z autorem `AuditAuthor.AutomatedJob` i rodzicem;
  6. zwraca liczbę albo `DuplicateRequest`. Brak definicji przy `547` też daje wynik, nie wyjątek.
- **Dodanie:**
  1. `RecurringDefinitionRequests.Find(key)` → porównanie skrótu: inny skrót daje `KeyReused`;
     ten sam skrót przy istniejącej definicji daje `Repeated` z zapisanymi `CreatedOccurrences`;
     ten sam skrót przy usuniętej definicji daje `Deleted`;
  2. nowa definicja: definicja, audyt `Created`, generowanie, wiersz żądania;
  3. wyścig o klucz kończy się naruszeniem PK. Po wycofaniu żądanie jest czytane ponownie.
- **Edycja, wstrzymanie, wznowienie:** po `DuplicateRequest` całość jest wycofywana, a wynik to
  `Busy` (`503`). Te handlery nie ponawiają.
- **Komenda zadania:** blokada, potem generowanie. Wynik to `Created(n)`, `Gone` albo
  `DuplicateRequest`.

### Job (`RecurringExpensesJob`, Infrastructure)

- **Definicja:** `new JobDefinition("recurring-expenses", JobSchedule.DailyAt(new TimeOnly(0, 10)),
  TimeSpan.FromMinutes(30))`.
- **Faza odczytu:** jeden zakres i `ListRecurringGenerationCandidatesQuery(D)`, który zwraca listę
  id.
- **Pętla po definicjach:**
  - przed każdą definicją sprawdza token anulowania;
  - tworzy nowy zakres DI i wysyła komendę przez `IDispatcher`;
  - wynik `DuplicateRequest` albo `SqlException` z numerem 1205 w łańcuchu daje jedno ponowienie
    w nowym zakresie;
  - pozostałe wyjątki (poza anulowaniem) są logowane z id definicji i liczone jako błąd.
- **Koniec przebiegu:** jeśli były błędy, przebieg rzuca
  `RecurringExpensesJobException("Nie utworzono wystąpień dla {n} wydatków cyklicznych.")`.

## Frontend Plan

### Files (`app/src/app/features/expenses/`)

| File | Action | Purpose |
|---|---|---|
| `recurring-expense-models.ts` | create | typy kontraktu, kody problemów, etykiety stanów, odmiana liczebnika |
| `recurring-expenses-api.ts` (+ `.spec.ts`) | create | klient `/api/recurring-expenses` (`list`, `get`, `preview`, `create` z `Idempotency-Key`, `update`, `pause`, `resume`, `delete`) |
| `recurring-expenses.service.ts` (+ `.spec.ts`) | create | stan listy (sygnały, jak `expenses.service.ts`) |
| `jobs-api.ts`, `recurring-job-warning.ts` (+ `.spec.ts`), `recurring-job-notice.ts` (+ `.spec.ts`) | create | `GET /api/jobs`, czysta funkcja ostrzeżenia i komponent (odczyt przy wejściu) |
| `recurring-expense-preview.ts` (+ `.spec.ts`) | create | podgląd z opóźnieniem 300 ms; teksty, w tym tryb wstrzymany (NC1) i `droppedDueCount` |
| `recurring-expense-form-model.ts` (+ `.spec.ts`) | create | model trybu definicji, walidacja UI, mapowanie na `RecurringExpenseInput` (dzień z modelu także przy zablokowanym polu, NC6) |
| `expense-form.ts` / `.html` | modify | pole wyboru „Powtarzaj co miesiąc”, sekcja „Harmonogram”, tryb definicji (create i edit), zablokowany dzień przy wstrzymaniu |
| `expense-create-page.ts` / `.html` (+ spec) | modify | parametr `cykliczny=1`, osobny klucz idempotencji, zapis definicji, nawigacja i komunikat |
| `recurring-expenses-list-page.ts` / `.html` (+ spec) | create | lista z filtrami i stanami |
| `recurring-expense-details-page.ts` / `.html` (+ spec) | create | szczegóły, edycja, dialogi wstrzymania, wznowienia i usunięcia |
| `expenses-subnav.ts` | create | linki podsekcji |
| `expenses-list-page.ts` / `.html` (+ spec) | modify | „Cykliczny”, filtr z `wydatekCykliczny`, notice, subnav |
| `expenses.service.ts`, `expenses-api.ts`, `expense-models.ts` | modify | filtr `recurringDefinitionId`, pola `recurringDefinitionId` i `recurring` |
| `expense-details-page.ts` / `.html` (+ spec) | modify | informacja o wystąpieniu, link, zdanie w dialogu usunięcia |
| `expenses.routes.ts` | modify | `cykliczne` i `cykliczne/:id` przed `:id` |

Komponenty używają `app-data-table`, `app-field`, `app-button`, `app-confirm-dialog`,
`app-status-badge` i `shared/format` bez zmian. Kolory pochodzą wyłącznie z tokenów. Logika (reguła
ostrzeżenia, teksty podglądu, mapowanie formularza) mieszka w funkcjach i serwisach, nie
w komponentach.

## API Contract

Wszystkie trasy wymagają sesji, a zapisy dodatkowo XSRF. Kwoty są przesyłane jako string, daty
w formacie `yyyy-MM-dd`, okres jako `yyyy-MM`, a `version` jako base64.

| Method | Route | Request | Response |
|---|---|---|---|
| GET | `/api/recurring-expenses` | `page`, `pageSize`, `sort` (`description`/`startsOn`/`grossAmount`), `direction`, `state` (`active`/`paused`/`ended`), `search` | `{ items: RecurringExpenseListItem[], page, pageSize, total }` |
| GET | `/api/recurring-expenses/{id}` | — | `RecurringExpenseDetails`; `404 recurring_expense_not_found` |
| GET | `/api/recurring-expenses/preview` | `dayOfMonth`, `startsOn`, `endsOn?`, `definitionId?` | `RecurringExpensePreview`; `400`; `404`; `409 recurring_expense_paused` |
| POST | `/api/recurring-expenses` | nagłówek `Idempotency-Key`; `RecurringExpenseInput` | `201`/`200 { id, version, createdOccurrences }`; `400` (`idempotency_key_required`); `409 idempotency_key_reused` (+`recurringExpenseId`), `409 recurring_expense_deleted` |
| PUT | `/api/recurring-expenses/{id}` | `RecurringExpenseInput` + `version` | `200 { version, createdOccurrences }`; `400`; `404`; `409 recurring_expense_conflict`, `recurring_expense_paused`; `503 recurring_expense_busy` |
| POST | `/api/recurring-expenses/{id}/pause` | `{ version }` | `200 { version, createdOccurrences }`; `404`; `409 recurring_expense_conflict`, `recurring_expense_already_paused`, `recurring_expense_ended`; `503` |
| POST | `/api/recurring-expenses/{id}/resume` | `{ version }` | `200 { version, createdOccurrences }`; `404`; `409 recurring_expense_conflict`, `recurring_expense_not_paused`; `503` |
| DELETE | `/api/recurring-expenses/{id}` | `{ version }` | `204`; `404`; `409 recurring_expense_conflict` |
| GET | `/api/expenses` | + `recurringDefinitionId` | pozycje + `recurringDefinitionId: string \| null` |
| GET | `/api/expenses/{id}` | — | + `recurring: { definitionId, period, definitionExists } \| null` |
| GET | `/api/jobs` | — | `JobStatusDto[]` (SF-11) |

Typy:

- `RecurringExpenseInput`: `description`, `category`, `counterparty`, `grossAmount`, `netAmount`,
  `paymentMethod`, `payer`, `comment`, `dayOfMonth` (number), `startsOn`, `endsOn`.
- `RecurringExpenseListItem`: `id`, `description`, `category`, `counterparty`, `grossAmount`,
  `dayOfMonth`, `startsOn`, `endsOn`, `status`, `pausedOn`, `nextDueDate`.
- `RecurringExpenseDetails`: pola wejścia oraz `id`, `status`, `pausedOn`, `nextDueDate`,
  `occurrenceCount`, `resumePreview: { occurrencesToCreate, nextDueDate } | null`,
  `createdAtUtc`, `updatedAtUtc`, `version`.
- `RecurringExpensePreview`: `occurrencesToCreate`, `firstImmediateDueDate`, `beforeLaunchCount`,
  `nextDueDate`, `paused` (bool, do tekstu NC1; pole dodane w planie, bo tryb wstrzymany musi być
  rozpoznawalny bez osobnego zapytania), `droppedDueCount`, `createdAfterEndCount`,
  `createdBeforeStartCount`.

## Integration Check

| Frontend expects | Backend provides | Gap |
|---|---|---|
| podgląd rozpoznaje tryb wstrzymany (NC1) | `paused` w `RecurringExpensePreview` | Brak. Pole dodane w kontrakcie, spec go nie wymienia; zapisane w Risks |
| szczegóły znają `pausedOn` do blokady pola dnia | `pausedOn` w `RecurringExpenseDetails` | Brak |
| filtr listy wydatków „Wydatek cykliczny: {opis}” | `GET /api/recurring-expenses/{id}` (opis), `404` → „(usunięty)” | Brak |

## Invariant Check

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | `Expense.CreateOccurrence` ustawia `PaidOn = null`; żadna ścieżka SF-16 nie ustawia `PaidOn` | integracyjny: wystąpienie ma `PaidOn` null i `paymentStatus = planned` |
| 2. Pieniądze jako decimal w backendzie | `decimal(18,2)`, parsowanie z SF-15, `CHECK` kwot | testy wejścia i `CHECK` |
| 3. Dostęp egzekwowany w API | polityka domyślna i XSRF; `404` dla definicji | `EndpointSecurityTests`, `401` bez sesji dla nowych tras |
| 4. Ponowienia nie duplikują | `PK_RecurringExpensePeriods`, `RecurringDefinitionRequests`, blokada | dwa przebiegi, wstawienie duplikatu do rejestru, powtórzenie z kluczem, usunięte wystąpienie nie wraca |
| 5. Audyt finansowy | `IAuditLog` w tej samej `IUnitOfWork`; `AutomatedJob` i rodzic | testy audytu definicji i wystąpień, rodzic przy zmianach wystąpienia |
| 6. Sekrety po stronie serwera | wyjątek zadania tylko z liczbą | test komunikatu wyjątku |
| 7. Granice modułów i warstw | zakresy DI tylko w Infrastructure, domena bez EF | `StoneFlow.Architecture.Tests` |
| 8. Brak danych identyfikujących | fikcyjne dane testów | przegląd |

## Unified Implementation Order

1. Domena: okres, harmonogram, encje, `Expense` (+ testy domeny).
2. Application: porty, wejście, audyt, generator, handlery, zapytania (+ testy wejścia i audytu).
3. Infrastructure: konfiguracje, lock, store, reader, migracja, job, DI.
4. Api: kontrakty, endpointy, problemy; zmiany SF-15.
5. Testy integracyjne (HTTP, zadanie, współbieżność, `CHECK`, bezpieczeństwo).
6. Frontend: modele i klienci, serwisy, funkcje (ostrzeżenie, podgląd, formularz), komponenty,
   strony, trasy (+ testy Vitest).
7. Weryfikacja (sekcja niżej), kolejno, z logami w scratchpadzie.

## Tests

Pełna lista jest w spec, w sekcji „Verification”. Minimalny zestaw dostarczenia:

| Test | Type | What it proves |
|---|---|---|
| `RecurringScheduleTests` | unit | terminy 29–31, przełom roku, dzień z historii (okres przed pierwszym wpisem, NC4), kwalifikacja z przedziałami, termin efektywny (NC3), stan, `nextDueDate`, `LastDueDate` |
| `RecurringExpenseDefinitionTests` | unit | skutki z reguły 8 (odpowiedzi 1–3, v2 marzec/sierpień, v3-C1/C2, v4-Critical, v4-NC1/NC2); wstrzymana (zmiana dnia odrzucona, 0 okresów); NC2 (wznowienie → dzień 3 → 03.11); NC4 (skrócenie → przedłużenie → wznowienie); `StartMoved` z dzisiaj/wczoraj; nakładające się przedziały; później → wcześniej |
| `RecurringExpenseInputTests`, `RecurringExpenseAuditTests` | unit | walidacja pól, skrót, pola i etykieta audytu |
| `RecurringExpensesApiTests` | integration (SQL) | CRUD, kody, kolejność `409`/`400` (NC3), idempotencja (powtórzenie, usunięcie, inne dane), podgląd = zapis, wstrzymanie i wznowienie, zmiana dnia, przedłużenie, `version` po zapisie, kaskada, rodzic audytu wystąpienia, `401`/XSRF, `CHECK` |
| `RecurringExpensesJobTests` | integration (SQL) | przebieg przez `IJobRunner`: termin dzisiaj, dwa przebiegi, przeskok 3 miesięcy, odcięcie 400 dni, błąd jednej definicji, kolizja rejestru z ponowieniem, wydajność (500 definicji < 60 s, dodanie od 2000 < 30 s) |
| Vitest: `recurring-job-warning`, `recurring-expense-preview`, `recurring-expense-form-model`, `recurring-expenses-api`, strony | unit/component | reguła ostrzeżenia (granice 25 h, `running`), teksty podglądu (NC1), `dayOfMonth` przy zablokowanym polu (NC6), dialogi, oznaczenie „Cykliczny” |

## Verification

Kolejno, z logami w scratchpadzie:

1. `dotnet build stone-flow.slnx -c Release -m:1`
2. `dotnet format stone-flow.slnx --verify-no-changes`
3. `StoneFlow.Expenses.Tests` i `StoneFlow.Architecture.Tests`
4. Integracyjne z `STONEFLOW_TEST_SQLSERVER=Server=(localdb)\MSSQLLocalDB;…`:
   - `--filter-namespace "*Expenses*"`;
   - `*SchedulerTests`, `*JobsEndpointTests`, `*EndpointSecurityTests`.
5. W `app/`: `npm run lint`, `npm test -- --watch=false`, `npm run build`, `npm run typecheck`,
   `npm run format:check`.
6. Ręcznie (poza zakresem automatu): kopia testowa, dowód D-08 pkt 2.
7. **Przegląd przez człowieka przed merge (ryzyko high).**

## Risks

- **Pole `paused` w podglądzie** to rozszerzenie kontraktu względem spec, potrzebne do tekstu NC1.
  Jest addytywne.
- **Rozmiar zmiany:** około 60 plików. Przegląd kodu warto skupić na domenie terminów
  i generatorze.
- **Test zakleszczenia z prawdziwą blokadą** może być niestabilny. Według spec dopuszczalny jest
  wtedy wyjątek z portu na poziomie store.
- **Testy E2E** są odroczone do SF-21.
- **Migracja** musi powstać przez `dotnet ef`, z narzędziem z `.config/dotnet-tools.json`.
