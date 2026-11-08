# Task Specification — SF-15

## Source

[SF-15](https://standev.youtrack.cloud/issue/SF-15)

> **Zmiana po przeglądzie (06.11.2026).** Decyzja właściciela: kwota netto 0,00 zł jest dozwolona.
> Gdy netto ≠ 0, ma ten sam znak co brutto, a reguła |netto| ≤ |brutto| zostaje. Ta sama poprawka
> rozstrzyga pozostałe uwagi z `03-spec-review.md`:
> - kolejność kodów `409`;
> - kontrakt SF-16;
> - stan na liście liczony funkcją domeny;
> - `WarsawTimeZone`;
> - przeniesienie `todayInWarsaw`;
> - odroczenie E2E;
> - teksty dialogów i etykiet;
> - panel filtrów.

## Goal

Moduł Expenses dostaje wydatek jednorazowy jako obiekt finansowy. Wydatek ma datę kosztu,
opcjonalny termin i datę zapłaty. Ma też kwotę brutto różną od 0 zł (ujemna oznacza korektę albo
zwrot od dostawcy) i opcjonalną kwotę netto przepisaną z dokumentu. Użytkownik może wydatek dodać
(z ochroną przed ponownym wysłaniem), poprawić (z kontrolą wersji) i usunąć (z potwierdzeniem).
Ma też dwie akcje: „Oznacz jako opłacony” i „Cofnij oznaczenie zapłaty”. Każda zmiana zostawia
wpis audytu w tej samej transakcji. Stan „Planowany” albo „Opłacony” wynika wyłącznie z daty
zapłaty, a informację „Po terminie” wylicza domena. Lista wydatków filtruje, sortuje i stronicuje
w bazie, także po dacie kosztu i dacie zapłaty. Zapis wydatku nie tworzy wypływu: wypływem jest
tylko wydatek z datą zapłaty, w okresie tej daty, a liczą to SF-17 i SF-20.

## Context

- **Backend, moduł Expenses** (schemat `expenses`, szkielet z SF-3: `ExpensesDbContext`,
  migracja `InitialSchema` bez tabel, `ExpensesModule` bez endpointów). Wzorcem jest moduł Orders
  (SF-8, SF-9). Zmiany w warstwach:
  - **Domain** `Expenses.Domain/Expenses/`:
    - encja `Expense`;
    - słowniki `ExpenseCategory` i `ExpensePaymentMethod`;
    - stan `ExpensePaymentStatus` (Planned, Paid) i reguła „Po terminie”;
    - pola audytu `ExpenseField`;
    - wyliczanie zmienionych pól, jak `Order.Snapshot`/`Changed`.
  - **Application:**
    - foldery `CreateExpense/`, `EditExpense/`, `MarkExpensePaid/`, `UnmarkExpensePaid/`,
      `DeleteExpense/`, `GetExpense/`, `ListExpenses/`;
    - `ExpenseInput` (walidacja i normalizacja), `ExpenseCodes` (kody słowników), `ExpenseAudit`
      (z opcjonalnym rodzicem, w SF-15 zawsze `null`);
    - `Abstractions/` (porty `IExpenseStore`, `IExpenseReader`, `FieldErrors`);
    - `WarsawTime` (dzisiaj w Europe/Warsaw), oparty na istniejącym
      `StoneFlow.Shared.Application.Scheduling.WarsawTimeZone.Zone`.
  - **Infrastructure:**
    - `Persistence/ExpenseConfiguration.cs`, `ExpenseStore`, `ExpenseReader`;
    - migracja z tabelą `expenses.Expenses`;
    - rejestracja w `DependencyInjection.cs` (kontekst jest już w `deploy/scripts/contexts.sh`).
  - **Api:**
    - `Endpoints/ExpensesEndpoints.cs` i `Endpoints/ExpenseContracts.cs`;
    - `ExpenseProblems.cs`;
    - `ExpensesModule.MapExpensesEndpoints` mapuje grupę `/api/expenses`.
- **Shared:**
  - `IUnitOfWork` i `IAuditLog` (SF-7);
  - `AuditObjectType.Expense`: data biznesowa to data kosztu, bez obiektu nadrzędnego. Rodzica
    `RecurringExpenseDefinition` dopuszcza już `AuditRecord` dla SF-16.

  Brak nowych typów współdzielonych. Moduł nie odwołuje się do Orders: `FieldErrors` i
  parsowanie kwot mają kopie lokalne. To świadoma, mała duplikacja, bo moduł nie może
  referencjonować innego modułu. Strefę czasową bierze ze Shared.
- **Frontend `features/expenses/`:** zastępuje placeholder `expenses-page`. Dochodzą:
  - trasy potomne `expenses.routes.ts` (lista, „Dodaj wydatek”, szczegóły);
  - klient `expenses-api.ts`, serwis stanu `expenses.service.ts` na sygnałach;
  - model formularza, formularz wydatku, lista, szczegóły z akcjami i dialogami.

  Elementy wspólne z `shared/ui/` i `shared/format/` są używane bez zmian. Wyjątki:
  - `todayInWarsaw` przechodzi z `features/orders/order-form-model.ts` do
    `shared/format/pl-format.ts`, bo ma drugiego wywołującego. Importy w Orders przechodzą na nowe
    miejsce: `entry-form-model.ts`, `payment-form.ts`, `refund-form.ts`,
    `sales-correction-form.ts`, `order-form-model.ts` i trzy pliki `*.spec.ts` formularzy wpisów;
  - dochodzi parser kwoty ze znakiem (`parseSignedPlnAmount`), bo `parsePlnAmount` odrzuca kwoty
    ujemne.
- Uwierzytelnianie (polityka domyślna), XSRF (globalny middleware), Problem Details z `code`
  i konwencje kwot (string dziesiętny z kropką) działają jak w SF-6 i SF-8.
- **Testy:** nowy projekt `api/tests/StoneFlow.Expenses.Tests` (domena, walidacja, audyt),
  dodany do `stone-flow.slnx`. Testy HTTP i bazy idą do `StoneFlow.Api.IntegrationTests`, jak
  w Orders.

## Scope

### In scope

- **Model wydatku:**
  - wymagane: opis, kategoria, data kosztu, kwota brutto;
  - opcjonalne: kontrahent, termin, data zapłaty, kwota netto, referencja dokumentu, metoda
    płatności, płatnik, komentarz.
- **Słowniki** jako stałe kody w kodzie: kategorie kosztów i metody płatności z PRD §7.
- **Dodanie wydatku** z `Idempotency-Key`, jak w SF-8.
- **Poprawa** wszystkich pól, w tym ustawienie, zmiana i usunięcie daty zapłaty, z kontrolą
  wersji (`rowversion`).
- **„Oznacz jako opłacony”:** data zapłaty, domyślnie dzisiaj, i metoda płatności.
- **„Cofnij oznaczenie zapłaty”:** usuwa datę zapłaty, wymaga potwierdzenia.
- **Usunięcie** z potwierdzeniem: fizyczne, a ślad `Deleted` ze wszystkimi polami zostaje
  w audycie.
- **Stan wydatku:**
  - „Planowany” bez daty zapłaty, „Opłacony” z datą zapłaty;
  - informacja „Po terminie” dla planowanego wydatku z minionym terminem.
- **Ujemne kwoty:** UI pokazuje je ze znakiem minus i opisem „Korekta / zwrot od dostawcy”.
- **Lista wydatków:**
  - filtry: data kosztu od–do, data zapłaty od–do, kategoria (wielokrotna), stan (planowany,
    opłacony, po terminie), znak kwoty, tekst;
  - sortowanie;
  - stronicowanie w bazie;
  - kolumny: data kosztu, termin, data zapłaty.
- **Audyt** każdego utworzenia, zmiany i usunięcia, w tym akcji zapłaty.
- **Daty:** dozwolona data kosztu sprzed uruchomienia, a data zapłaty może być późniejsza
  (URU-01).

### Out of scope

- Definicje cykliczne, wystąpienia, opcja „Powtarzaj co miesiąc” i oznaczenie cykliczności na
  liście: SF-16. SF-15 nie dodaje kolumn ani pól dla cykliczności (patrz „Kontrakt dla SF-16”).
- Powiązanie wydatków z zamówieniami (D-07).
- Raporty: koszty w widoku „Sprzedaż” (SF-19), wypływy, lista „Operacje” i jej porty odczytu
  (SF-17, SF-20). Należy do nich też suma kwot listy wydatków i uzgodnienie karty „Koszty”
  z listą, które SF-19 ma rozstrzygnąć.
- Wykluczanie kwot sprzed daty uruchomienia: robią raporty. SF-15 zapisuje takie wydatki
  normalnie.
- Lista „Historia zmian” (SF-18). SF-15 tylko zapisuje wpisy.
- Wypłaty właścicieli (SF-17).
- Załączniki i skany dokumentów, wyliczanie netto ze stawki VAT, przechowywanie stawki,
  wyliczanie zobowiązań podatkowych.
- Słownik kontrahentów i płatników: oba pola to wolny tekst.

## Behavior

### Stan wydatku (reguła domeny)

`ExpensePaymentStatus` i „Po terminie” wylicza funkcja domeny z `PaidOn`, `DueDate` i dzisiejszej
daty w Europe/Warsaw. Datę podaje handler z `TimeProvider`.

1. `PaidOn` uzupełnione: stan `Paid` („Opłacony”), `overdue = false`.
2. `PaidOn` puste: stan `Planned` („Planowany”).
3. `overdue = true` wtedy i tylko wtedy, gdy stan to `Planned`, `DueDate` jest podany i
   `DueDate < dzisiaj`. W dniu terminu wydatek nie jest jeszcze po terminie.
4. Stan nie jest przechowywany. Szczegóły i pozycje listy liczą go tą samą funkcją domeny
   z surowych `PaidOn` i `DueDate`, jak `OrderListItemDto.Payment` w Orders. SQL odtwarza regułę
   tylko w predykacie filtra `state`. Test sprawdza, że filtr wybiera dokładnie te wiersze, którym
   funkcja domeny przypisuje dany stan.
5. Stan nie zależy od daty kosztu, kwoty, znaku ani metody. Metoda (np. „Pobranie”) nie
   oznacza zapłaty.

### Dodanie wydatku

1. Na liście „Wydatki” użytkownik wybiera „Dodaj wydatek” (`/wydatki/nowy`). Frontend generuje
   klucz idempotencji (UUID) przy otwarciu formularza.
2. Formularz ma sekcje:
   - „Wydatek”: opis, kategoria, kontrahent;
   - „Kwoty”: brutto, netto;
   - „Daty”: data kosztu (domyślnie dzisiaj), termin, data zapłaty;
   - „Płatność i dokument”: metoda, płatnik, referencja dokumentu;
   - „Komentarz”.
3. Gdy brutto jest ujemne, przy polu kwoty pojawia się opis „Korekta / zwrot od dostawcy” i
   wyjaśnienie: „Obniża koszty w okresie daty kosztu, a po zapłacie — wypływy w okresie daty
   zapłaty.”
4. „Zapisz” przechodzi w stan `busy` („Zapisywanie…”), a UI waliduje dane. API waliduje je
   ponownie i w jednej jednostce pracy:
   1. szuka wydatku po kluczu;
   2. zapisuje wydatek;
   3. zapisuje audyt `Created`.

   Zatwierdzany jest tylko wynik `Created`.
5. Odpowiedź `201 { id, version }` z `Location: /api/expenses/{id}`. Powtórzenie z tym samym
   kluczem i tymi samymi danymi (po normalizacji) daje `200` z tym samym `id` i aktualną wersją.
   Ten sam klucz z innymi danymi daje `409 idempotency_key_reused` z `expenseId`. Wyścig dwóch
   żądań z jednym kluczem kończy się naruszeniem unikalnego indeksu (`2601`/`2627`). Wtedy
   transakcja się wycofuje, a wydatek jest czytany po kluczu na nowo, jak w `CreateOrderHandler`.
6. Skrót żądania to SHA-256 kanonicznego JSON-a znormalizowanej treści:
   - teksty przycięte, pusty tekst jako `null`;
   - kwoty `0.00`, daty `yyyy-MM-dd`, słowniki jako kody.
7. Po sukcesie UI przechodzi do szczegółów wydatku i pokazuje komunikat „Wydatek zapisany”.

### Lista

1. `/wydatki` pokazuje tabelę `app-data-table`. Kolumny:
   - „Data kosztu”, „Opis”, „Kategoria”, „Kontrahent”;
   - „Kwota brutto” z podstawą „brutto” w nagłówku, wyrównana do prawej, `tabular-nums`;
   - „Termin płatności”, „Data zapłaty”;
   - „Stan”: `app-status-badge` „Planowany” albo „Opłacony”, a obok „Po terminie” (ton
     ostrzeżenia), gdy dotyczy.

   Opis prowadzi do szczegółów. Wiersz z ujemną kwotą pokazuje kwotę ze znakiem minus i pod
   opisem etykietę „Korekta / zwrot od dostawcy”.
2. Filtry przy tabeli:
   - data kosztu od–do i data zapłaty od–do;
   - kategoria (wielokrotny wybór);
   - stan: wszystkie, planowane, opłacone, po terminie;
   - znak kwoty: wszystkie, wydatki (dodatnie), korekty i zwroty (ujemne);
   - tekst.

   Widać aktywne filtry i jest „Wyczyść filtry”. Na telefonie filtry są w rozwijanym panelu
   „Filtry” (`app-data-table` z `filterable`, jak lista zamówień). Filtry, sortowanie i strona
   żyją w serwisie stanu, jak na liście zamówień, więc powrót ze szczegółów zachowuje widok.
   Zakres dat może być jednostronny dla obu par: samo „od” albo samo „do”.
3. Sortowanie po: dacie kosztu (domyślnie malejąco), terminie, dacie zapłaty, kwocie brutto.
   Remis rozstrzyga `Id`. Przy sortowaniu po terminie albo dacie zapłaty wiersze bez wartości
   są zawsze na końcu, niezależnie od kierunku.
4. Paginacja podaje zakres i liczbę rekordów: domyślnie 25, najwyżej 100 na stronę.
5. Stany widoku:
   - ładowanie: skeleton;
   - pusta ewidencja: „Brak wydatków” z akcją „Dodaj wydatek”;
   - pusty wynik filtrów: „Brak wydatków w wybranym okresie” (design guide §8), gdy aktywny jest
     filtr daty, a w pozostałych przypadkach „Brak wydatków dla wybranych filtrów”; w obu
     przypadkach z „Wyczyść filtry”;
   - błąd: komunikat i „Spróbuj ponownie”.

### Szczegóły i poprawa

1. `/wydatki/{id}` pokazuje:
   - nagłówek z opisem, stanem („Planowany”/„Opłacony”) i „Po terminie”;
   - sekcje danych: kwoty z podstawą brutto/netto, daty z odrębnymi etykietami „Data kosztu”,
     „Termin płatności”, „Data zapłaty”, płatność i dokument, komentarz;
   - techniczne czasy utworzenia i zmiany w Europe/Warsaw.

   Brak wartości pokazuje się jako „Brak danych”.
2. Akcje:
   - „Edytuj” (`primary`);
   - „Oznacz jako opłacony”, tylko dla stanu „Planowany”;
   - „Cofnij oznaczenie zapłaty”, tylko dla stanu „Opłacony”;
   - „Usuń” (`danger`);
   - „Wróć do listy”.

   Gdy otwarty jest formularz poprawy albo zapłaty, pozostałe akcje zapisu są niedostępne.
3. „Edytuj” rozwija ten sam formularz co przy dodaniu, wypełniony aktualnymi danymi.
   „Zapisz zmiany” wysyła `PUT` z `version`.

   Kolejność w API:
   1. walidacja (`400`);
   2. w jednej jednostce pracy: istnienie (`404`) i wersja (`409 expense_conflict`);
   3. edycja domeny, która zwraca zmienione pola;
   4. bez zmian: sukces bez zapisu i bez audytu, z dotychczasową wersją;
   5. zapis z oczekiwaną wersją; `DbUpdateConcurrencyException` daje `409`;
   6. audyt `Updated` z samymi zmienionymi polami.
4. Usunięcie daty zapłaty w formularzu: UI przed wysłaniem pokazuje to samo potwierdzenie co akcja
   „Cofnij oznaczenie zapłaty”. Zmiana daty zapłaty na inną datę nie wymaga potwierdzenia.
   Potwierdzenie istnieje tylko w UI. API przyjmuje `PUT` z `paidOn = null` bez dodatkowej flagi,
   a audyt `Updated` zapisuje zmianę.
5. Po sukcesie szczegóły się odświeżają, a UI pokazuje „Zmiany zapisane”.

### Oznacz jako opłacony

1. W szczegółach wydatku „Planowany” akcja „Oznacz jako opłacony” rozwija formularz z polami:
   - „Data zapłaty”: wymagana, domyślnie dzisiaj (Europe/Warsaw);
   - „Metoda płatności”: wypełniona aktualną metodą, można ją zmienić albo wyczyścić.

   Opis wyjaśnia: „Wydatek trafi do wypływów w okresie daty zapłaty.” Przy ujemnej kwocie brutto
   opis brzmi: „Korekta / zwrot od dostawcy obniży wypływy w okresie daty zapłaty.”
2. `POST /api/expenses/{id}/mark-paid { paidOn, paymentMethod, version }`. Kolejność:
   1. walidacja (`400`): `paidOn` wymagane, nie późniejsze niż dzisiaj, znana metoda;
   2. w jednej jednostce pracy: istnienie (`404`) i wersja (`409 expense_conflict`);
   3. reguła domeny: wydatek już opłacony daje `409 expense_already_paid`;
   4. ustawienie `PaidOn` i metody;
   5. zapis i audyt `Updated` (`paidOn` `null` → data, `paymentMethod`, jeśli się zmieniła).

   Metoda w żądaniu zastępuje zapisaną, a `null` ją czyści.
3. UI odświeża szczegóły i pokazuje „Wydatek oznaczony jako opłacony”.

### Cofnij oznaczenie zapłaty

1. Akcja „Cofnij oznaczenie zapłaty” w wydatku „Opłacony” otwiera `app-confirm-dialog`:
   - nagłówek: „Cofnąć oznaczenie zapłaty?”;
   - treść: „Wydatek „{opis}” wróci do stanu Planowany i przestanie liczyć się do wypływów
     z {data zapłaty}. Zmiana zostanie zapisana w historii zmian.”;
   - przyciski: „Cofnij oznaczenie” i „Anuluj”.
2. `POST /api/expenses/{id}/unmark-paid { version }`. Kolejność:
   1. wersja (`400`);
   2. istnienie (`404`) i wersja (`409`);
   3. reguła domeny: wydatek bez daty zapłaty daje `409 expense_not_paid`;
   4. wyczyszczenie `PaidOn`, metoda zostaje;
   5. audyt `Updated` (`paidOn` data → `null`).
3. UI odświeża szczegóły i pokazuje „Cofnięto oznaczenie zapłaty”.

### Usunięcie

1. „Usuń” otwiera `app-confirm-dialog` (ton `danger`):
   - nagłówek: „Usunąć wydatek?”;
   - treść: „Wydatek „{opis}” z {data kosztu} na {kwota} zostanie usunięty z ewidencji i
     raportów. Ślad usunięcia zostanie w historii zmian.” Tekst jest neutralny, bo frontend nie
     zna daty uruchomienia, więc nie wie, czy wydatek był w raportach;
   - przyciski: „Usuń” i „Anuluj”.
2. `DELETE /api/expenses/{id}` z `{ version }` w ciele, jak przy wpisach Orders. Kolejność:
   1. wersja (`400`);
   2. istnienie (`404`) i wersja (`409`);
   3. audyt `Deleted` ze wszystkimi niepustymi polami jako stare wartości;
   4. fizyczne usunięcie wiersza.
3. Odpowiedź `204`. UI wraca do listy z komunikatem „Wydatek usunięty”. Kolejny odczyt `GET` daje
   `404 expense_not_found`. Wpis audytu prowadzi do śladu usunięcia (SF-18).

### Współbieżność i błędy

- Konflikt wersji (`409 expense_conflict`): UI zostawia wpisane dane i pokazuje „Wydatek został
  zmieniony w międzyczasie. Wczytaj aktualne dane.” z akcją „Wczytaj ponownie”. Bez cichego
  nadpisania.
- `404` po usunięciu w innej karcie: „Nie znaleziono wydatku. Mógł zostać usunięty.” z „Wróć do
  listy”.
- `400` z polami: błąd przy polu i podsumowanie nad formularzem. Dane zostają.
- `401`: obsługuje globalny interceptor (SF-6).

## Data / API

### Storage

**`expenses.Expenses`** (nowa tabela, migracja `Expenses` w `ExpensesDbContext`; tylko dodanie,
zgodne z poprzednią wersją API):

| Kolumna | Typ | Uwagi |
|---|---|---|
| `Id` | `uniqueidentifier` PK | nadawany w aplikacji |
| `Description` | `nvarchar(200)` not null | |
| `Category` | `nvarchar(32)` not null | nazwa wartości enum (`HasConversion<string>()`, jak w Orders) |
| `Counterparty` | `nvarchar(200)` null | |
| `CostDate` | `date` not null | data kosztu: okres kosztów (SF-19) |
| `DueDate` | `date` null | termin |
| `PaidOn` | `date` null | data zapłaty: stan i okres wypływu (SF-17, SF-20) |
| `GrossAmount` | `decimal(18,2)` not null | |
| `NetAmount` | `decimal(18,2)` null | |
| `DocumentReference` | `nvarchar(100)` null | |
| `PaymentMethod` | `nvarchar(16)` null | nazwa wartości enum |
| `Payer` | `nvarchar(100)` null | |
| `Comment` | `nvarchar(2000)` null | |
| `CreationRequestId` | `uniqueidentifier` null | klucz idempotencji; `null` dla wydatków tworzonych bez żądania użytkownika (SF-16) |
| `CreationRequestHash` | `binary(32)` null | |
| `CreatedAtUtc`, `UpdatedAtUtc` | `datetime2` not null | znaczniki techniczne UTC |
| `Version` | `rowversion` | właściwość cienia, jak `OrderConfiguration.VersionProperty` |

Ograniczenia i indeksy:

- `CK_Expenses_GrossAmount`: `[GrossAmount] <> 0`.
- `CK_Expenses_NetAmount`: `[NetAmount] IS NULL OR (ABS([NetAmount]) <= ABS([GrossAmount]) AND
  ([NetAmount] = 0 OR SIGN([NetAmount]) = SIGN([GrossAmount])))`.
- `CK_Expenses_CreationRequest`: `CreationRequestId` i `CreationRequestHash` są oba puste albo
  oba podane.
- `UX_Expenses_CreationRequestId`: unikalny, filtrowany `WHERE [CreationRequestId] IS NOT NULL`.
- `IX_Expenses_CostDate` (`CostDate`, `Id`) i filtrowany `IX_Expenses_PaidOn` (`PaidOn`, `Id`)
  `WHERE [PaidOn] IS NOT NULL`. Obsługują filtry listy, a później zapytania kosztów i wypływów.

Brak ograniczenia `PaidOn ≤ dzisiaj` w bazie, bo „dzisiaj” zależy od strefy i czasu. Pilnuje
tego walidacja aplikacji.

**Kontrakt dla SF-16.** Wystąpienie cykliczne to zwykły wiersz `Expenses`. SF-16 dodaje
migracją addytywną:

- kolumny nullowalne, np. `RecurringDefinitionId` i okres;
- własny rejestr par definicja–okres z unikalnością. Wiersz rejestru powstaje przy **tworzeniu**
  wystąpienia, w tej samej jednostce pracy, i nie jest usuwany razem z wydatkiem. Dlatego
  usunięte wystąpienie nie wraca (PRD §5, inwariant 4), a `DeleteExpense` z SF-15 pozostaje bez
  zmian;
- rodzica `RecurringExpenseDefinition` w `ExpenseAudit`. SF-15 przygotowuje `ExpenseAudit`
  z opcjonalnym rodzicem, który w SF-15 jest zawsze `null`. SF-16 podaje go dla wystąpień przy
  utworzeniu, edycji, akcjach zapłaty i usunięciu.

SF-15 nie zakłada, że każdy wydatek ma `CreationRequestId`. Edycja, akcje zapłaty, usunięcie,
lista i szczegóły działają na każdym wierszu `Expenses`, więc SF-16 nie zmienia ich kontraktu
poza dodaniem pól odczytu i podaniem rodzica audytu. Żadna kolumna SF-15 nie jest usuwana ani
zmieniana.

### Endpoints

Wszystkie wymagają ważnej sesji (SF-6), a `POST`, `PUT` i `DELETE` także nagłówka
`X-XSRF-TOKEN`. Nie ma endpointów anonimowych. Nazwy `.WithName("Expenses.{Action}")`.

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/expenses` | query: `page` (≥1), `pageSize` (1–100, domyślnie 25), `sort` (`costDate`, `dueDate`, `paidOn`, `grossAmount`), `direction` (`asc`/`desc`), `costDateFrom`, `costDateTo`, `paidOnFrom`, `paidOnTo`, `category` (wielokrotny), `state` (`planned`, `paid`, `overdue`), `sign` (`positive`, `negative`), `search` (≤100 znaków) | `200 ExpenseListPage { items, page, pageSize, total }`; `400` przy złych parametrach | sesja | odczyt |
| GET | `/api/expenses/{id}` | — | `200 ExpenseDetails`; `404 expense_not_found` | sesja | odczyt |
| POST | `/api/expenses` | nagłówek `Idempotency-Key` (UUID, wymagany); body `ExpenseInput` | `201 { id, version }` + `Location`; `200 { id, version }` przy powtórzeniu; `400` walidacja albo `idempotency_key_required`; `409 idempotency_key_reused` (+ `expenseId`) | sesja + XSRF | zapis |
| PUT | `/api/expenses/{id}` | body `ExpenseInput` + `version` | `200 { version }`; `400`; `404`; `409 expense_conflict` | sesja + XSRF | zapis |
| POST | `/api/expenses/{id}/mark-paid` | `{ paidOn, paymentMethod, version }` | `200 { version }`; `400`; `404`; `409 expense_conflict`, `409 expense_already_paid` | sesja + XSRF | zapis |
| POST | `/api/expenses/{id}/unmark-paid` | `{ version }` | `200 { version }`; `400`; `404`; `409 expense_conflict`, `409 expense_not_paid` | sesja + XSRF | zapis |
| DELETE | `/api/expenses/{id}` | `{ version }` w ciele JSON | `204`; `400`; `404`; `409 expense_conflict` | sesja + XSRF | zapis |

Problem Details (`ExpenseProblems`): tekst w `detail`, kod w `code`:

| Kod | Status | `detail` |
|---|---|---|
| `expense_not_found` | 404 | „Nie znaleziono wydatku. Mógł zostać usunięty.” |
| `expense_conflict` | 409 | „Wydatek został zmieniony w międzyczasie. Wczytaj aktualne dane.” |
| `idempotency_key_required` | 400 | „Brak poprawnego klucza żądania. Odśwież formularz i spróbuj ponownie.” |
| `idempotency_key_reused` | 409 | „Ten formularz zapisał już wydatek. Odśwież formularz, aby dodać kolejny.” |
| `expense_already_paid` | 409 | „Wydatek jest już oznaczony jako opłacony.” |
| `expense_not_paid` | 409 | „Wydatek nie jest oznaczony jako opłacony.” |

Infrastruktura tłumaczy:

- `DbUpdateConcurrencyException` na wynik konfliktu;
- `2601`/`2627` na `UX_Expenses_CreationRequestId` na wynik idempotentny.

Naruszenie innego indeksu albo ograniczenia `CHECK` jest błędem programu, bo walidacja
poprzedza zapis. Każde tłumaczenie ma test sprawdzający kod HTTP. Bez testu fallback
`DbException` → `503` z konwencji zamaskowałby błąd.

### Types crossing a boundary

Kwoty idą jako string dziesiętny z kropką i opcjonalnym minusem (`"-1234.50"`). Daty mają format
`yyyy-MM-dd`, słowniki to kody camelCase, a frontend mapuje je na etykiety.

- **Kategorie** `ExpenseCategory` (stałe kody, PRD §7):

  | Kod | Etykieta |
  |---|---|
  | `materialPurchase` | „Zakup materiału u dostawców” |
  | `cutting` | „Cięcie” |
  | `hdsTransport` | „Transport HDS” |
  | `marketing` | „Marketing” (podpowiedź: Google Ads, OLX, SEO, media społecznościowe) |
  | `otherOperations` | „Pozostała działalność” |
  | `vatTax` | „Podatek VAT” |
  | `ppeTax` | „Podatek PPE” |

- **Metody** `ExpensePaymentMethod` (PRD §7): `transfer` „Przelew”, `blik` „BLIK”, `cash`
  „Gotówka”, `cashOnDelivery` „Pobranie”.
- **`ExpenseInput`:**
  - `description`, `category`, `counterparty?`;
  - `costDate`, `dueDate?`, `paidOn?`;
  - `grossAmount`, `netAmount?`;
  - `documentReference?`, `paymentMethod?`, `payer?`, `comment?`.
- **Walidacja** (API; UI powtarza te reguły, w których nie trzeba porównywać kwot):

  | Pole | Reguła |
  |---|---|
  | `description` | wymagany, 1–200 znaków po przycięciu |
  | `category` | wymagana, znany kod |
  | `counterparty` | ≤ 200 |
  | `costDate` | wymagana, `yyyy-MM-dd`, bez ograniczenia zakresu (także sprzed uruchomienia i przyszła) |
  | `dueDate` | opcjonalny, `yyyy-MM-dd`, dowolna data (także wcześniejsza niż data kosztu) |
  | `paidOn` | opcjonalna, `yyyy-MM-dd`, nie późniejsza niż dzisiaj w Europe/Warsaw (`TimeProvider`), może być wcześniejsza niż data kosztu |
  | `grossAmount` | wymagana, ≠ 0, najwyżej 2 miejsca po przecinku, `|kwota| ≤ 9 999 999 999 999 999,99` (zakres `decimal(18,2)`) |
  | `netAmount` | opcjonalna, te same reguły formatu; `0.00` dozwolone; gdy ≠ 0, ten sam znak co brutto; `|netto| ≤ |brutto|` |
  | `documentReference` | ≤ 100 |
  | `paymentMethod` | znany kod albo brak |
  | `payer` | ≤ 100 |
  | `comment` | ≤ 2000 |

  - Teksty są przycinane, a pusty tekst zamienia się na `null`.
  - Kwoty parsuje kultura niezmienna wzorcem `^-?[0-9]+(\.[0-9]+)?$`. Nie ma zaokrąglania:
    więcej niż dwa miejsca to błąd.
  - Klucze błędów to nazwy pól JSON (`grossAmount`, `netAmount`, `paidOn`, `version`).
  - Komunikaty, m.in.:
    - „Kwota brutto nie może wynosić 0 zł.”;
    - „Kwota netto musi mieć ten sam znak co brutto.”;
    - „Kwota netto nie może być większa od brutto.”;
    - „Data zapłaty nie może być późniejsza niż dzisiaj.”
  - Nieznane kody słowników są odrzucane.
- **`ExpenseListItem`:** `id`, `description`, `category`, `counterparty`, `costDate`, `dueDate`,
  `paidOn`, `grossAmount`, `paymentStatus` (`planned`/`paid`), `overdue`.
- **`ExpenseDetails`:** pola `ExpenseInput` oraz `id`, `paymentStatus`, `overdue`,
  `createdAtUtc`, `updatedAtUtc`, `version` (base64 z `rowversion`).
- **Filtry listy:**
  - `state=planned`: `PaidOn IS NULL`; `paid`: `PaidOn IS NOT NULL`; `overdue`:
    `PaidOn IS NULL AND DueDate < @dzisiaj`. To jedyne miejsce, w którym SQL odtwarza regułę stanu.
    `paymentStatus` i `overdue` w pozycjach listy liczy funkcja domeny z surowych wartości;
  - każdy zakres dat (`costDate*`, `paidOn*`) może być jednostronny;
  - `sign=positive`: `GrossAmount > 0`; `negative`: `GrossAmount < 0`;
  - `paidOnFrom`/`paidOnTo` pokazują tylko wydatki opłacone w zakresie;
  - `search` szuka podciągu (`LIKE` z escapowaniem `%`, `_`, `[`) w `Description`,
    `Counterparty` i `DocumentReference`;
  - zakres „od” późniejszy niż „do” daje `400`;
  - filtry łączy AND, a kategorie wewnątrz filtra OR.
- **Audyt (SF-7, konwencje „Value format”):**
  - `AuditObject(Expense, id, costDate, label, parent)`. `ExpenseAudit` przyjmuje opcjonalnego
    rodzica, który w SF-15 jest zawsze `null`;
  - etykieta: „Wydatek – {etykieta kategorii}”, np. „Wydatek – Cięcie”. Nie zawiera wolnego
    tekstu, więc nie ma w niej danych osobowych;
  - przy zmianie daty kosztu wpis ma nową datę biznesową;
  - nazwy pól: `description`, `category`, `counterparty`, `costDate`, `dueDate`, `paidOn`,
    `grossAmount`, `netAmount`, `documentReference`, `paymentMethod`, `payer`, `comment`;
  - `payer` ma `IsPersonalData = true`, tak jak „Przyjął” przy wpłacie;
  - wartości: kwota `-1234.50`, data `yyyy-MM-dd`, kategoria i metoda jako kody, brak wartości
    `null`;
  - `Created`: niepuste pola jako nowe wartości. `Updated`: tylko zmienione pola, także przy akcjach
    zapłaty. `Deleted`: wszystkie niepuste pola jako stare wartości z nową `null`;
  - brak zmian oznacza brak wywołania audytu.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | Wydatek bez daty zapłaty ma stan „Planowany” i nie jest wypływem. Przyszła data zapłaty jest odrzucana, więc plan nie może udawać zapłaty. Planowany termin to `dueDate`, a nie `paidOn`. SF-15 nie tworzy żadnego wypływu. Wypływy liczą SF-17 i SF-20 wyłącznie z `PaidOn`. |
| 2. Pieniądze jako decimal w backendzie | `decimal(18,2)` w bazie i `decimal` w domenie. Kwoty w API to stringi dziesiętne. Parsowanie bez zaokrągleń i bez `float`/`double`. Frontend nie liczy: przetwarza kwoty tekstowo (`parseSignedPlnAmount`, `formatPln`), a porównanie `|netto| ≤ |brutto|` robi tylko API. |
| 3. Dostęp egzekwowany w API | Wszystkie trasy `/api/expenses` wymagają sesji (polityka domyślna) i XSRF przy zapisie. Handler sprawdza istnienie konkretnego wydatku (`404`). Testy integracyjne: `401` bez sesji dla każdej trasy, odrzucenie zapisu bez XSRF. Brak wpisów w `EndpointSecurityTests.AllowedAnonymous`. |
| 4. Ponowienia nie duplikują | Dodanie wymaga `Idempotency-Key`, a unikalny filtrowany indeks na `CreationRequestId` chroni przed duplikatem. Test: równoległe żądania z jednym kluczem dają jeden wydatek. Akcje zapłaty i edycja są chronione wersją, więc powtórzone żądanie po sukcesie dostaje `409` i nic nie dubluje. Unikalność definicja–okres należy do SF-16. |
| 5. Audyt finansowy | Każde utworzenie, zmiana (także „Oznacz jako opłacony” i cofnięcie) i usunięcie wywołuje `IAuditLog.RecordAsync` w tej samej `IUnitOfWork`, z autorem `AuditAuthor.Account`. Usunięcie zapisuje wszystkie wartości, więc nie znika bez śladu. Test: błąd audytu wycofuje zmianę wydatku. |
| 6. Sekrety po stronie serwera | Bez sekretów i integracji. Problem Details i logi nie zawierają danych wydatku ani płatnika. |
| 7. Granice modułów i warstw | Expenses nie odwołuje się do Orders ani Reporting. Używa wyłącznie `StoneFlow.Shared.*`, a lokalne kopie pomocników są świadomą duplikacją. Domena nie zależy od EF Core ani HTTP. Granice sprawdzają `StoneFlow.Architecture.Tests`. |
| 8. Brak danych identyfikujących w dokumentach | Przykłady w specyfikacji i testach są fikcyjne („Dostawca A”), bez nazwy handlowej i domeny sklepu. |

## Risk

**high.** Zadanie dotyczy pieniędzy, bo wydatki to źródło kosztów i wypływów w obu widokach.
Usunięcie jest nieodwracalne, a ślad zostaje tylko w audycie. Błąd w stanie zapłaty zamieniłby
plan w wypływ (inwariant 1). Model musi przyjąć SF-16 bez migracji niszczącej. Przed merge
wymagany jest przegląd przez człowieka.

## UI

- **Trasy** (`expenses.routes.ts`, leniwie, w sekcji `wydatki`):
  - `/wydatki`: lista, tytuł „Wydatki”;
  - `/wydatki/nowy`: „Dodaj wydatek”;
  - `/wydatki/:id`: „Wydatek”.

  `app.routes.ts` zmienia `loadComponent` sekcji na `loadChildren`, jak dla „Zamówienia”.
- **Komponenty:**
  - `expenses-list-page`: `app-data-table`, filtry w `tableFilters`;
  - `expense-create-page`;
  - `expense-details-page`: dane, akcje, dialogi `app-confirm-dialog` do usunięcia i cofnięcia
    zapłaty;
  - `expense-form`: Signal Forms, `app-field` z `#control`, `novalidate`;
  - `expense-paid-form`: „Oznacz jako opłacony”;
  - `expense-form-model`: walidacja UI, mapowanie na `ExpenseInput`;
  - `expense-models`: kody, etykiety kategorii, metod i stanów.

  Kwoty i daty wyświetla wyłącznie `shared/format`.
- **Główna akcja:** na liście „Dodaj wydatek” (`primary`), w formularzu „Zapisz” albo „Zapisz
  zmiany”, w szczegółach „Edytuj”. „Usuń” to `danger`, pozostałe akcje są `secondary`.
- **Formularz:**
  - etykiety nad polami, wymagane pola oznaczone także dla czytnika (`aria-required`);
  - błędy przy polu (`aria-describedby`) i podsumowanie nad formularzem przy wielu błędach;
  - dane zostają po błędzie API;
  - kwoty wpisuje się po polsku (`-1 234,50`, `1234,5`). Frontend zamienia je tekstowo na
    kanoniczny string. UI sprawdza format, zero brutto i zgodność znaku niezerowego netto
    z brutto, a resztę API;
  - pola dat to `type="date"` z odrębnymi etykietami „Data kosztu”, „Termin płatności”, „Data
    zapłaty” i podpowiedziami:
    - przy dacie zapłaty: „Uzupełnij dopiero po zapłacie. Data nie może być późniejsza niż
      dzisiaj.”;
    - przy terminie: „Planowany termin zapłaty — nie oznacza zapłaty.”;
  - „Kategoria” i „Metoda płatności” to `select` (metoda z opcją „Nie podano”);
  - „Płatnik” to opcjonalne pole tekstowe z podpowiedzią „Kto zapłacił; nie wpływa na
    obliczenia.”
- **Ujemne kwoty:** znak minus w formacie `-1 234,50 zł` i etykieta „Korekta / zwrot od dostawcy”
  w formularzu, na liście i w szczegółach. Tekst niesie znaczenie, a ton jest tylko dodatkiem.
- **Stany:**
  - `app-status-badge`: „Planowany” (`neutral`), „Opłacony” (`success`), „Po terminie”
    (`warning`), zawsze z tekstem;
  - widoki: ładowanie, pusto (ewidencja albo filtr), błąd z „Spróbuj ponownie”, konflikt
    edycji, nie znaleziono;
  - przyciski zapisu z `busy` („Zapisywanie…”) blokują ponowne wysłanie;
  - komunikaty sukcesu w regionie `aria-live`.
- **Dostępność (WCAG 2.2 AA):**
  - pełna obsługa klawiaturą formularzy, filtrów, dat i dialogów;
  - dialog przechwytuje fokus i zwraca go do przycisku, który go otworzył;
  - widoczny fokus;
  - sortowanie z `aria-sort`;
  - semantyczne nagłówki sekcji i tabela;
  - na telefonie jedna kolumna formularza, a tabela przechodzi w karty albo ma kontrolowane
    przewijanie z zachowaną kwotą i stanem;
  - cała strona bez poziomego przewijania;
  - kolory wyłącznie z tokenów (`lint:colors`).

## Edge Cases

- Kwota brutto `0`, `0.00`, `-0` albo puste pole daje `400`. Netto `0.00` przy brutto `100.00`
  albo `-100.00` jest przyjmowane (faktura korygująca sam VAT).
- Brutto `-500.00`, netto `-406.50`: przyjęte. Brutto `-500.00`, netto `406.50`: `400` (znak).
  Brutto `100.00`, netto `120.00`: `400`.
- Kwota z trzema miejscami po przecinku (`10.005`) daje `400` bez zaokrąglania. Kwota
  `9999999999999999.99` i jej ujemny odpowiednik są przyjmowane, a większa wartość daje `400`.
- Data zapłaty jutro daje `400`. Granica dnia: o 23:30 UTC, gdy w Polsce jest już następny dzień,
  „dzisiaj” liczy się według Europe/Warsaw (test z `FakeTimeProvider`).
- Data kosztu sprzed uruchomienia z datą zapłaty po uruchomieniu (URU-01): przyjęte bez
  ostrzeżenia. Data zapłaty wcześniejsza niż data kosztu: przyjęta.
- Termin dzisiaj: „Planowany” bez „Po terminie”. Termin wczoraj bez daty zapłaty: „Po terminie”.
  Ten sam wydatek opłacony nie jest po terminie.
- Wersja jest sprawdzana przed regułą domeny. Druga karta, która ma nieaktualną wersję (np. po
  oznaczeniu zapłaty w pierwszej), dostaje `409 expense_conflict`. Kody `409 expense_already_paid`
  i `409 expense_not_paid` pojawiają się tylko przy aktualnej wersji: „Oznacz jako opłacony” na
  opłaconym wydatku albo cofnięcie na planowanym, wysłane z bieżącą wersją.
- Edycja bez zmian: `200` z tą samą wersją, bez audytu.
- Dwie karty edytują ten sam wydatek: druga dostaje `409 expense_conflict`, a jej dane zostają
  w formularzu.
- Usunięcie wydatku, który ktoś właśnie zmienił: `409`. Usunięcie już usuniętego: `404`.
- Dodanie z tym samym kluczem po usunięciu tak utworzonego wydatku tworzy nowy wydatek, bo wiersz
  z kluczem zniknął. Frontend generuje nowy klucz przy każdym otwarciu formularza, a usunięcie
  zamyka formularz, więc w praktyce ten przypadek nie wystąpi.
- `Idempotency-Key` pusty, nie-UUID albo podany wielokrotnie daje `400 idempotency_key_required`.
- Tekst wyszukiwania z `%` albo `_` jest szukany dosłownie. Ponad 100 znaków daje `400`.
- `paidOnFrom` bez `paidOnTo` filtruje otwarcie. `state=planned` razem z `paidOnFrom` daje pusty
  wynik, a nie błąd.
- Strona poza zakresem (np. `page=50` przy 3 stronach) daje pustą listę z prawdziwym `total`.
- Wygaśnięcie sesji w trakcie wypełniania: `401`, przejście do logowania. Formularz nie jest
  zachowywany (design guide §8).

## Dependencies

- SF-6 (sesja, XSRF, polityka domyślna, `toApiError`): gotowe.
- SF-7 (`IUnitOfWork`, `IAuditLog`, `AuditObjectType.Expense`): gotowe.
- Kolejne zadania korzystają z modelu: SF-16 (wystąpienia jako wiersze `Expenses`), SF-17
  i SF-20 (wypływy z `PaidOn`), SF-19 (koszty z `CostDate`, `GrossAmount`, kategorie).

## Assumptions

Odpowiedzi właściciela (06.11.2026) na pytania doprecyzowujące:

1. **Przyszła data zapłaty.** Pytanie: czy data zapłaty może być w przyszłości? Odpowiedź: data
   zapłaty najwyżej dzisiaj (Europe/Warsaw), a przyszła jest odrzucana. Plan wpisuje się jako
   termin, więc plan nigdy nie udaje wypływu.
2. **Usunięcie daty zapłaty.** Pytanie: czy można usunąć datę zapłaty z opłaconego wydatku?
   Odpowiedź: tak, z potwierdzeniem i audytem. Spec daje akcję „Cofnij oznaczenie zapłaty” i to
   samo potwierdzenie przy wyczyszczeniu pola w formularzu.
3. **Etykieta stanu bez daty zapłaty.** Pytanie: „Planowany” czy „Nieopłacony”? Odpowiedź:
   „Planowany” (zgodnie z design guide) z dodatkową informacją „Po terminie”, gdy termin minął.
4. **Płatnik.** Pytanie: słownik czy tekst? Odpowiedź: opcjonalne pole tekstowe bez wpływu na
   obliczenia.

Decyzje autora specyfikacji, każda z uzasadnieniem:

5. **Netto (decyzja właściciela z 06.11.2026 po przeglądzie spec):** netto 0,00 zł jest
   dozwolone, np. przy fakturze korygującej sam VAT. Gdy netto ≠ 0, ma ten sam znak co brutto
   (PRD). Reguła |netto| ≤ |brutto| zostaje: brutto to netto plus nieujemny VAT, więc większe
   netto oznacza zamienione pola albo literówkę. Brak netto to `null`, a zero to wartość (PRD §8).
   Netto nie wpływa na obliczenia, bo raporty używają brutto.
6. **Usunięcie jest fizyczne, a ślad zostaje w audycie** (pełny zestaw wartości w `Deleted`).
   Tak samo usuwane są wpłaty, zwroty i korekty w SF-9 i SF-10. Wymaganie „nie usuwa historii
   bez śladu” spełnia wpis audytu, który SF-18 pokazuje także dla usuniętego obiektu. Usuwanie
   miękkie wymagałoby filtrowania każdego zapytania kosztów i wypływów w SF-17, SF-19 i SF-20,
   a pominięty filtr policzyłby usunięty wydatek. Ślad, który blokuje ponowne utworzenie
   wystąpienia cyklicznego, to rejestr SF-16 zapisywany przy tworzeniu wystąpienia. Nie zależy
   od wiersza wydatku, więc `DeleteExpense` nie wymaga zmian w SF-16.
7. **Brak numeru wydatku.** PRD go nie przewiduje. Etykieta audytu to „Wydatek – {kategoria}”, bo
   opis i kontrahent to wolny tekst i mogą zawierać dane osobowe, a etykieta nie może.
   Rozpoznawalność dają data biznesowa (data kosztu) i wartości w zakresie zmiany.
8. **Płatnik jest oznaczony w audycie jako dane osobowe** (`IsPersonalData`), tak jak „Przyjął”
   przy wpłacie. Kontrahent, opis i komentarz nie są oznaczane, podobnie jak dostawcy i komentarz
   zamówienia.
9. **Kategorie:** siedem stałych kodów. „Marketing” jest jedną kategorią, a Google Ads, OLX, SEO
   i media społecznościowe to tylko podpowiedź (PRD podaje je w nawiasie jako przykłady). „Podatki
   VAT i PPE” to dwie kategorie, „Podatek VAT” i „Podatek PPE”, bo design guide mówi o
   „kategoriach kosztów” VAT i PPE. Obie są zwykłymi kosztami, a system nie wylicza zobowiązania
   podatkowego.
10. **Metody płatności wydatku:** te same cztery co w PRD §7 (przelew, BLIK, gotówka, pobranie),
    jako osobny słownik modułu Expenses. Metoda jest opcjonalna, bo przy planowanym wydatku może
    być nieznana. Metoda „Pobranie” nie oznacza zapłaty.
11. **Daty:** data kosztu bez ograniczenia zakresu, także przyszła, jak data zamówienia w SF-8.
    Termin dowolny. Data zapłaty może poprzedzać datę kosztu (przedpłata, zaliczka u dostawcy).
    Wydatek z datą sprzed uruchomienia zapisuje się normalnie, a wykluczanie z raportów należy do
    SF-19 i SF-20.
12. **Kolumny dat na liście:** data kosztu, termin i data zapłaty jako osobne kolumny. Uzgodnienie
    kosztów wymaga daty kosztu, a uzgodnienie wypływów daty zapłaty. Oznaczenie cykliczności
    dojdzie w SF-16.
13. **„Oznacz jako opłacony” ustawia datę zapłaty i metodę** (metoda wypełniona aktualną). Przy
    zapłacie metoda jest zwykle znana dopiero teraz. Cofnięcie zostawia metodę bez zmian.
    Akcje zapłaty są dostępne w szczegółach, nie w wierszach listy: jeden punkt akcji, mniej
    stanów listy.
14. **Filtr stanu „po terminie”** to podzbiór planowanych. „Dzisiaj” liczy handler z
    `TimeProvider` w Europe/Warsaw i przekazuje do zapytania.
15. **Suma kwot na liście** jest poza SF-15. Raporty pomijają kwoty sprzed uruchomienia, więc
    suma listy bez tej reguły mogłaby nie zgadzać się z kartą „Koszty”. Uzgodnienie listy z kartą
    ma rozstrzygnąć SF-19.
16. **Duplikacja pomocników** (`FieldErrors`, parsowanie kwot) w Expenses zamiast przeniesienia
    do Shared: moduł nie może referencjonować Orders. Przeniesienie do Shared zmieniłoby kod
    niezwiązany z zadaniem, a konwencje dopuszczają małą duplikację między modułami. Lokalny
    `WarsawTime` (dzisiejsza data) opiera się na współdzielonym `WarsawTimeZone.Zone`
    i nie kopiuje wyszukiwania strefy. We frontendzie `todayInWarsaw` przechodzi do
    `shared/format/pl-format.ts`, bo ma drugiego wywołującego w innej funkcji. Importy w Orders
    zostają zaktualizowane.
17. **Ryzyko:** bez zmian (`high`).
18. **Testy E2E są odroczone.** Repozytorium nie ma infrastruktury E2E, a poprzednie zadania też jej
    nie miały. Proces „zapisz wydatek” end to end (rules.md, priorytet testów 5; PRD §11) trafia
    do odbioru MVP w SF-21. W SF-15 zastępują go testy integracyjne API na SQL Server i testy
    komponentów. Odroczenie jest zapisane w planie i w otwartych punktach dostawy.
19. **Stan filtrów listy** żyje w serwisie stanu (jak lista zamówień), a nie w adresie. Taki jest
    istniejący wzorzec, a powrót ze szczegółów zachowuje widok.
20. **Jedna etykieta „Termin płatności”** na liście, w szczegółach i w formularzu.

## Acceptance Criteria

- [ ] Kwota brutto 0 zł jest odrzucana (`400`) w API i w formularzu. Netto 0,00 zł jest
      przyjmowane. Niezerowe netto z innym znakiem niż brutto i `|netto| > |brutto|` są odrzucane.
- [ ] Ujemna kwota brutto jest przyjmowana i pokazywana ze znakiem minus i opisem „Korekta / zwrot
      od dostawcy” w formularzu, na liście i w szczegółach.
- [ ] Wydatek bez daty zapłaty ma stan „Planowany”, a nie „Opłacony”. Po uzupełnieniu daty
      (w formularzu albo akcją „Oznacz jako opłacony”) ma stan „Opłacony”.
- [ ] Planowany wydatek z minionym terminem pokazuje „Po terminie”, a w dniu terminu i po zapłacie
      nie pokazuje.
- [ ] Data zapłaty późniejsza niż dzisiaj (Europe/Warsaw) jest odrzucana.
- [ ] „Cofnij oznaczenie zapłaty” i wyczyszczenie daty zapłaty w formularzu wymagają potwierdzenia,
      przywracają stan „Planowany” i zostawiają audyt `Updated` z `paidOn`.
- [ ] Dodanie, poprawa, obie akcje zapłaty i usunięcie zapisują audyt (autor, obiekt `Expense`
      z datą kosztu, operacja, zakres zmiany) w tej samej transakcji. Błąd audytu wycofuje zmianę.
- [ ] Usunięcie wymaga potwierdzenia wskazującego wydatek i skutek. Po usunięciu `GET` zwraca
      `404`, a audyt `Deleted` zawiera wszystkie wartości.
- [ ] Wydatek można wpisać z datą kosztu sprzed daty uruchomienia i późniejszą datą zapłaty.
- [ ] Listę można przefiltrować po dacie kosztu od–do. Działają też filtry daty zapłaty,
      kategorii, stanu (w tym „po terminie”), znaku i tekstu. Filtrowanie, sortowanie i
      stronicowanie działają w bazie ze stabilną kolejnością.
- [ ] Podwójne wysłanie formularza dodania (ten sam `Idempotency-Key`) tworzy jeden wydatek, także
      przy równoległych żądaniach. Inne dane z tym samym kluczem dają `409 idempotency_key_reused`.
- [ ] Zmiana ze starą wersją daje `409 expense_conflict` i niczego nie nadpisuje. UI pokazuje
      konflikt z „Wczytaj ponownie” i zostawia wpisane dane.
- [ ] Bez sesji każda trasa `/api/expenses` zwraca `401`. Zapis bez tokenu XSRF jest odrzucany.
- [ ] Lista i szczegóły biorą stan z tej samej funkcji domeny. Filtr `state` w SQL wybiera
      dokładnie te wiersze, którym funkcja przypisuje dany stan (test zgodności).
- [ ] Migracja tylko dodaje tabelę i indeksy (expand). Model przyjmuje kolumny SF-16 bez zmiany
      istniejących.

## Verification

- build: `dotnet restore stone-flow.slnx --locked-mode`, `dotnet format stone-flow.slnx
  --verify-no-changes --no-restore`, `dotnet build stone-flow.slnx -c Release --no-restore`.
  W `app/`: `npm ci`, `npm run format:check`, `npm run lint`, `npm run typecheck`,
  `npm run build`.
- tests:
  - `dotnet test --solution stone-flow.slnx -c Release --no-build`:
    - `StoneFlow.Expenses.Tests`: stan i „Po terminie” (granice dnia), reguły kwot i znaku,
      wyliczanie zmian, walidacja `ExpenseInput` (każda reguła z tabeli), skrót idempotencji,
      format audytu (`Created`/`Updated`/`Deleted`, `payer` jako dane osobowe, etykieta bez
      wolnego tekstu);
    - `StoneFlow.Api.IntegrationTests`, na SQL Server: CRUD i akcje zapłaty z kodami HTTP,
      idempotencja z wyścigiem, konflikt wersji, `CHECK` i indeks unikalny, audyt i jego
      wycofanie, filtry, sortowanie z wartościami pustymi na końcu i stronicowanie listy,
      zgodność filtra `state` (SQL) z funkcją domeny, `401` bez sesji i odrzucenie bez XSRF;
    - `StoneFlow.Architecture.Tests`: granice modułu Expenses;
  - E2E: odroczone do SF-21 (Assumptions 18);
  - `npm test -- --watch=false`: `expense-form-model` (kwoty ze znakiem, netto, przyszła data
    zapłaty), `parseSignedPlnAmount`, `expenses-api`, `expenses.service`, strony listy,
    tworzenia i szczegółów (stany, dialogi, potwierdzenie wyczyszczenia daty zapłaty, konflikt,
    `busy`).
- manual:
  - dodaj wydatek dodatni i ujemny;
  - podwójne kliknięcie „Zapisz” tworzy jeden wydatek;
  - oznacz jako opłacony i cofnij oznaczenie;
  - spróbuj wpisać jutrzejszą datę zapłaty;
  - usuń wydatek i sprawdź wpis audytu w bazie (`reporting`);
  - przefiltruj po dacie kosztu i dacie zapłaty;
  - edytuj w dwóch kartach i sprawdź konflikt;
  - przejdź formularz i dialogi samą klawiaturą;
  - sprawdź widok na szerokości telefonu.
