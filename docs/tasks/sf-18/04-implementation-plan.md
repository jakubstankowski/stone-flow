# Implementation Plan — SF-18

Tryb: **fullstack**. Zadanie dodaje zapytanie, port odczytu, indeks i endpoint w Reporting oraz
stronę `/historia-zmian` we frontendzie (nowy kontrakt `GET /api/change-history`). Plany backendu i
frontendu napisał jeden autor po kolei: najpierw backend, potem frontend z kontraktem wyprowadzonym ze
spec (nie z planu backendu). Oba porównano w „Integration Check”. Zadanie nie jest dzielone na
części: migracja jest addytywna (jeden indeks), backend wdraża się przed frontendem, a całość mieści
się w jednym przeglądzie. Ryzyko `high` — przegląd człowieka przed merge.

## Scope

- **Reporting.Application:** przypadek `ListChangeHistory` (walidacja parametrów, okres zmiany
  Europe/Warsaw → przedział UTC, kryteria), port `IChangeHistoryReader`, kody API.
- **Reporting.Infrastructure:** `ChangeHistoryReader` na `ReportingDbContext` (filtry, sortowanie,
  stronicowanie i `deleted` w SQL), indeks `IX_AuditEntries_ObjectBusinessDate`, migracja, DI.
- **Reporting.Api:** endpoint `GET /api/change-history` i kontrakty JSON.
- **Reporting.Domain:** bez zmian (spec, założenie 12).
- **Frontend:** `features/change-history/` zastępuje zaślepkę listą z filtrami, chipami, zakresem
  zmiany, przejściem do obiektu i znacznikiem „Usunięto”; słowniki etykiet używane przez kilka
  obszarów przechodzą do `shared/dictionaries/`; `app-data-table` dostaje opcjonalny układ kart na
  telefonie.

### Uwagi z przeglądu (`03-spec-review.md`)

Wszystkie 12 uwag niekrytycznych poprawiono już w spec v2 (`02-spec.md`). Plan traktuje spec jako
wiążącą i niczego w niej nie zmienia.

| # | Uwaga przeglądu | Gdzie rozwiązana w spec v2 | Gdzie w planie |
|---|---|---|---|
| 1 | Filtr daty biznesowej a zmiana przesuwająca obiekt | Edge Cases („porównuje datę biznesową zapisaną we wpisie”), założenie 10, podpowiedź w UI | B3, F5 (podpowiedź), I-H-4 |
| 2 | Pola-listy zamówienia | katalog: `suppliers`, `zoReferences` jako „lista” | F2 (format `list`), F-H-2 |
| 3 | „—” zamiast „Brak danych” | Format wyświetlania: „Brak danych” (`NO_DATA`) | F2, F-H-2 |
| 4 | Źródło etykiet słowników | „Stałe używane przez więcej niż jedną funkcję przenosi się do `app/src/app/shared/`” | F1 (konkretne pliki niżej) |
| 5 | Słowniki zależne od rodzaju obiektu | „Słownik zależy od pary (rodzaj obiektu, pole)” | F2 (tabela katalogu) |
| 6 | Pola wydatku cyklicznego | katalog wypisuje pola definicji wprost | F2 |
| 7 | Warunki testu `405` | Endpoints: z sesją i tokenem CSRF, bez sesji `401` | I-H-8, I-H-9 |
| 8 | Pusta ewidencja vs pusty wynik filtra | UI → Stany: dwa komunikaty | F4 (`activeFilters`), F5 |
| 9 | Nazwa znacznika usunięcia | `object.deleted` w całej spec | B4, F1 |
| 10 | Czas po stronie backendu | Behavior 3: `WarsawTimeZone`; kontrakt: `occurredAt` z „Z” | B2, B6, R-H-1, I-H-1 |
| 11 | Polskie nazwy rodzajów obiektu | lista nazw; rodzaj w kolumnie tylko dla czytników ekranu | F1, F5 |
| 12 | Zamówień nie usuwa się | założenie 11 | F1 (`objectLink`) |

Uwagi z sekcji „Unclear” przeglądu też są w spec v2: kolejność skrótu = kolejność `changes`
(Behavior 5), `app-status-badge` z tonami (UI → Tokeny), `400` z API przy polu (Endpoints),
domena bez zmian (założenie 12).

### Rozstrzygnięcia planu (z kodu, bez decyzji właściciela)

| # | Punkt | Rozstrzygnięcie |
|---|---|---|
| P1 | Strefa czasu | `StoneFlow.Shared.Application/Scheduling/WarsawTimeZone.Zone` (bez nowej kopii; lokalne `WarsawTime` w Orders i Expenses zostają). Północ w Europe/Warsaw nigdy nie wypada w przerwie ani w powtórzonej godzinie (zmiany czasu o 02:00/03:00), więc `TimeZoneInfo.ConvertTimeToUtc(date 00:00, Zone)` wystarcza — bez pętli jak w `JobSchedule.SlotOn`. |
| P2 | `occurredAt` z „Z” | `ChangeHistoryReader` po materializacji ustawia `DateTime.SpecifyKind(…, Utc)` (wzór `ExpenseReader.GetAsync`), a `ChangeHistoryCodes.FormatTimestamp` daje format `"O"` (np. `2026-11-05T12:00:00.0000000Z`) i sam też ustawia `Kind = Utc`, więc wynik nie zależy od wejścia. |
| P3 | Nazwa migracji | `AuditEntriesBusinessDateIndex` w `Reporting.Infrastructure/Persistence/Migrations`; `dotnet ef … --configuration Release` (`bin/Debug` bywa zablokowany przez działające API). |
| P4 | `EndpointSecurityTests` | Bez zmian w pliku. Test sam wylicza endpointy: `Every_protected_endpoint_returns_401_without_session` obejmie `GET /api/change-history`, a `AllowedAnonymous` się nie zmienia. Osobny test `401` jest też w `ChangeHistoryApiTests` (I-H-9). |
| P5 | Test `405` | Wzór `OrdersApiTests.Delete_is_not_possible`: `OrdersTest.SendAsync` (sesja + `X-XSRF-TOKEN`) z `POST`, `PUT`, `PATCH`, `DELETE` na `/api/change-history` → `405`, liczba wierszy `reporting.AuditEntries` bez zmian. |
| P6 | Skrót zakresu zmiany | Utworzenie: „Etykieta: jest”; usunięcie: „Etykieta: było”; zmiana: „Etykieta: było → jest”. To ta sama reguła co kolumny rozwinięcia (Behavior 6). Pokazuje dwa pierwsze pola, potem „i N więcej”. |
| P7 | Pusta lista | Chip „Zmiana: …” pokazuje się tylko przy okresie innym niż domyślne 30 dni. Domyślne filtry dają więc zero chipów, a `app-data-table` pokazuje `emptyTitle` „Nie ma jeszcze żadnych zmian.”. Każdy inny filtr to wariant filtrowany „Brak zmian dla wybranych filtrów.” z „Wyczyść filtry” (istniejąca logika `filters().length > 0` w `data-table.html`). |
| P8 | Format czasu | Kolumna „Czas” używa `formatDateTime` z `shared/format` (`05.11.2026, 13:00`, Europe/Warsaw). Spec zapisuje `dd.MM.yyyy HH:mm`, bez przecinka. Konwencje każą formatować czasy tylko przez `shared/format`, a jest tam jeden format czasu technicznego — zob. Risks. |
| P9 | Karty na telefonie | `app-data-table` dostaje opcjonalne wejście `mobileCards` (domyślnie `false`, inne listy bez zmian). Poniżej `md` wiersz jest kartą, a każda komórka ma widoczny nagłówek kolumny. DOM jest jeden, więc identyfikatory `aria-controls` się nie powtarzają. |
| P10 | Metody we wszystkich obiektach | Wpłata `method`, zwrot `method`, wypłata `method`, wydatek i definicja `paymentMethod` → wspólny `PAYMENT_METHOD_LABELS` z `shared/dictionaries`. Kody zwrotu i wypłaty to podzbiór tych samych kodów z tymi samymi etykietami. `REFUND_METHOD_LABELS` i `WITHDRAWAL_METHOD_LABELS` zostają w swoich obszarach jako listy opcji formularzy. |

## Backend Plan

### Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Application | `Reporting.Application/ChangeHistory/` | zapytanie, walidacja, okres → UTC, kody |
| Application | `Reporting.Application/Abstractions/` | port `IChangeHistoryReader` z kryteriami i DTO |
| Infrastructure | `Reporting.Infrastructure/Persistence/` | reader, indeks, migracja |
| Infrastructure | `Reporting.Infrastructure/DependencyInjection.cs` | rejestracja readera |
| Api | `Reporting.Api` | `GET /api/change-history`, kontrakty |
| Tests | `StoneFlow.Reporting.Tests`, `StoneFlow.Api.IntegrationTests` | aplikacja, HTTP i baza |

Domain, Shared.Kernel (`AuditObjectType`, `AuditOperation`, `AuditAuthorKind`) i zapis audytu
(`IAuditLog`, `AuditLog`) bez zmian. Brak nowych projektów i referencji `.csproj`:
`Reporting.Application` już referencjonuje `Shared.Application` (`WarsawTimeZone`) i `Reporting.Domain`
(`AuditFieldChange`), a `StoneFlow.Reporting.Tests` już referencjonuje `Reporting.Application`.

### API contract

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/change-history` | query: `changedFrom`, `changedTo` (wymagane, `yyyy-MM-dd`), `businessFrom`, `businessTo` (opcjonalne), `objectType` (0..n), `direction` (`desc` \| `asc`), `page` (≥ 1), `pageSize` (1–100) | `200 ChangeHistoryPageResponse`; `400 ValidationProblem` (klucze = nazwy parametrów) | sesja (polityka fallback hosta) | Reporting; jedno konto (D-05), brak zakresu zasobu |
| POST/PUT/PATCH/DELETE | `/api/change-history` | — | `405` (z sesją i CSRF), `401` (bez sesji) | — | — |

### Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Modules/Reporting/StoneFlow.Reporting.Application/Abstractions/ChangeHistoryPorts.cs` | create | `IChangeHistoryReader`, `ChangeHistoryCriteria`, `ChangeHistoryEntryDto`, `ChangeHistoryPageDto` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Application/ChangeHistory/ChangeHistoryCodes.cs` | create | kody rodzajów obiektu, operacji i autorów; `FormatDate`, `TryParseDate`, `FormatTimestamp` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Application/ChangeHistory/ChangeHistoryPeriod.cs` | create | `ToUtcRange(from, to)` → `[from 00:00, to+1 00:00)` Europe/Warsaw jako UTC |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Application/ChangeHistory/ListChangeHistory.cs` | create | `ListChangeHistoryQuery`, `ListChangeHistoryResult`, `ListChangeHistoryHandler` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Persistence/ChangeHistoryReader.cs` | create | implementacja portu (projekcja, `deleted`, strona) |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Persistence/AuditRecordConfiguration.cs` | modify | `builder.HasIndex(record => record.ObjectBusinessDate)` (nazwa domyślna `IX_AuditEntries_ObjectBusinessDate`) |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Persistence/Migrations/<ts>_AuditEntriesBusinessDateIndex.cs` (+ `.Designer.cs`, `ReportingDbContextModelSnapshot.cs`) | create/modify | migracja addytywna: `CreateIndex` / `DropIndex` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/DependencyInjection.cs` | modify | `services.AddScoped<IChangeHistoryReader, ChangeHistoryReader>()` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Api/Endpoints/ChangeHistoryEndpoints.cs` | create | grupa `/api/change-history`, `MapGet` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Api/Endpoints/ChangeHistoryContracts.cs` | create | rekordy odpowiedzi i mapowanie DTO → JSON |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Api/ReportingModule.cs` | modify | `ChangeHistoryEndpoints.Map(endpoints)`; komentarz klasy |
| `api/tests/StoneFlow.Reporting.Tests/ChangeHistory/ChangeHistoryPeriodTests.cs` | create | R-H-1 |
| `api/tests/StoneFlow.Reporting.Tests/ChangeHistory/ChangeHistoryCodesTests.cs` | create | R-H-2 |
| `api/tests/StoneFlow.Reporting.Tests/ChangeHistory/ListChangeHistoryHandlerTests.cs` | create | R-H-3, R-H-4 (fałszywy reader zapisujący kryteria) |
| `api/tests/StoneFlow.Api.IntegrationTests/Reporting/ChangeHistoryApiTests.cs` | create | I-H-1 … I-H-10; dane przez `OrdersTest` (`SendAsync` na `/api/orders`, `/api/expenses`, wpłaty przez `OrderEntryRequests`, zegar `Time`) oraz wpisy wstawiane SQL-em do `reporting.AuditEntries` (wzór `AuditLogTests`) dla dokładnych czasów i remisów |

Bez zmian: `stone-flow.slnx`, `deploy/scripts/contexts.sh` (kontekst Reporting już jest),
`EndpointSecurityTests` (P4), `StoneFlow.Architecture.Tests`, `Reporting.Domain`.

### Implementation steps

Kolejność TDD: każdy krok aplikacji zaczyna się od czerwonego testu (sekcja „Tests”).

- **B1 Port** (`Abstractions/ChangeHistoryPorts.cs`):
  - `ChangeHistoryCriteria(DateTime FromUtc, DateTime ToUtcExclusive, DateOnly? BusinessFrom, DateOnly? BusinessTo, IReadOnlySet<AuditObjectType> ObjectTypes, bool Descending, int Page, int PageSize)`;
    pusty zbiór `ObjectTypes` = wszystkie rodzaje.
  - `ChangeHistoryEntryDto(long Id, DateTime OccurredAtUtc, AuditAuthorKind AuthorKind, AuditObjectType ObjectType, Guid ObjectId, DateOnly BusinessDate, string Label, AuditObjectType? ParentType, Guid? ParentId, bool Deleted, AuditOperation Operation, IReadOnlyList<AuditFieldChange> Changes)`;
    `OccurredAtUtc` ma `Kind = Utc` (dokumentacja portu). `AuthorAccountId` nie wychodzi z portu
    (niezmiennik 6, D-05).
  - `ChangeHistoryPageDto(IReadOnlyList<ChangeHistoryEntryDto> Items, int Total, int Page, int PageSize)`.
  - `IChangeHistoryReader.ListAsync(ChangeHistoryCriteria, CancellationToken)` → `ChangeHistoryPageDto`.
    Dokumentacja: strona za końcem → puste `Items` z prawdziwym `Total`; `Deleted` nie zależy od
    filtrów.
- **B2 `ChangeHistoryPeriod`** (R-H-1 najpierw): `ToUtcRange(DateOnly from, DateOnly to)` →
  `(DateTime FromUtc, DateTime ToUtcExclusive)`: `ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue, Unspecified), WarsawTimeZone.Zone)`
  i to samo dla `to.AddDays(1)` (P1). Wynik z `Kind = Utc`.
- **B3 `ChangeHistoryCodes`** (R-H-2 najpierw), porównanie dokładne (`StringComparison.Ordinal`):
  - rodzaje obiektu: `Order` ↔ `order`, `Payment` ↔ `payment`, `Refund` ↔ `refund`,
    `SalesCorrection` ↔ `salesCorrection`, `Expense` ↔ `expense`,
    `RecurringExpenseDefinition` ↔ `recurringExpenseDefinition`, `OwnerWithdrawal` ↔ `ownerWithdrawal`;
    `Of(type)`, `TryParse(code, out type)`;
  - operacje: `created`, `updated`, `deleted`; autorzy: `account`, `wooCommerceSync`, `automatedJob`
    (tylko `Of`, bo nie są parametrami);
  - `FormatDate` (`yyyy-MM-dd`), `TryParseDate` (dokładny wzorzec, kultura niezmienna — jak
    `OperationCodes`, osobna kopia w obszarze, bo to dwie linie i niezależne funkcje);
  - `FormatTimestamp(DateTime)` → `SpecifyKind(Utc).ToString("O", InvariantCulture)` (P2).
- **B4 `ListChangeHistoryHandler(IChangeHistoryReader reader)`** (R-H-3, R-H-4 najpierw):
  1. Walidacja `ListChangeHistoryQuery(string? ChangedFrom, string? ChangedTo, string? BusinessFrom, string? BusinessTo, IReadOnlyList<string>? ObjectType, string? Direction, string? Page, string? PageSize)`
     (tekst z HTTP, przycięty; pusty = brak), błędy w `FieldErrors` (istniejący typ w
     `Reporting.Application/Abstractions`):
     - `changedFrom` / `changedTo` wymagane: „Podaj początek okresu zmiany.” / „Podaj koniec okresu
       zmiany.”; zły format: „Podaj datę w formacie RRRR-MM-DD.”; `changedFrom > changedTo` → przy
       `changedFrom`: „Początek okresu zmiany nie może być późniejszy niż koniec.”;
     - `businessFrom` / `businessTo` opcjonalne, każda osobno; zły format jak wyżej; oba podane i
       `businessFrom > businessTo` → przy `businessFrom`: „Data biznesowa „od” nie może być późniejsza
       niż „do”.”;
     - `objectType` każdy z `ChangeHistoryCodes`; nieznany → jeden błąd przy `objectType` („Nieznany
       rodzaj obiektu.”); duplikaty usunięte; pusto = wszystkie;
     - `direction` `desc` (domyślnie) | `asc`, inaczej „Kierunek sortowania to asc albo desc.”;
     - `page` liczba całkowita ≥ 1 (domyślnie 1), `pageSize` 1–100 (domyślnie 25) — komunikaty jak
       w `ListOperationsHandler` („Numer strony musi być liczbą całkowitą od 1.”, „Rozmiar strony
       musi być liczbą od 1 do 100.”).
  2. Błędy → `ListChangeHistoryResult(null, errors)` bez wywołania readera.
  3. `ChangeHistoryPeriod.ToUtcRange` → `ChangeHistoryCriteria` → `reader.ListAsync` →
     `ListChangeHistoryResult(page)`.
  Handler nie zawiera logiki biznesowej poza walidacją i zamianą okresu (domena bez zmian).
  Okres nie ma limitu długości (założenie 5).
- **B5 `ChangeHistoryReader(ReportingDbContext context)`** (Infrastructure, `internal sealed`):
  - `AuditEntries` (bez śledzenia — sama projekcja DTO):
    `OccurredAtUtc >= FromUtc && OccurredAtUtc < ToUtcExclusive`;
    `BusinessFrom` → `ObjectBusinessDate >= BusinessFrom`; `BusinessTo` → `ObjectBusinessDate <= BusinessTo`;
    `ObjectTypes.Count > 0` → `ObjectTypes.Contains(r.ObjectType)` (lista enumów z konwersją na tekst).
  - `total = await CountAsync`; `skip = (long)(Page − 1) × PageSize`; `skip >= total` → puste
    `Items` bez drugiego zapytania (strona poza zakresem, spec Edge Cases).
  - Sortowanie: `OccurredAtUtc`, potem `Id`, oba w kierunku `Descending` (stabilny remis, spec
    Behavior 3); `Skip((int)skip).Take(PageSize)`.
  - Projekcja w SQL z `Deleted = context.AuditEntries.Any(d => d.ObjectType == r.ObjectType && d.ObjectId == r.ObjectId && d.Operation == AuditOperation.Deleted)`
    (skorelowane `EXISTS` na indeksie `(ObjectType, ObjectId)`, niezależne od filtrów — Behavior 4).
    `Changes` przez istniejący konwerter JSON.
  - Po materializacji `OccurredAtUtc = DateTime.SpecifyKind(…, Utc)` (P2).
  - Zapytania po kolei (`ModuleConnection`, bez `Task.WhenAll`), `CancellationToken` przekazany.
- **B6 Indeks i migracja:**
  - `AuditRecordConfiguration.Configure`: `builder.HasIndex(record => record.ObjectBusinessDate);`
    obok istniejących indeksów.
  - Z katalogu repozytorium (`dotnet tool restore` jednorazowo):
    `dotnet ef migrations add AuditEntriesBusinessDateIndex -p api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure -s api/src/StoneFlow.Api -c ReportingDbContext -o Persistence/Migrations --configuration Release`.
  - `Up`: tylko `CreateIndex("IX_AuditEntries_ObjectBusinessDate", schema "reporting", table "AuditEntries", column "ObjectBusinessDate")`;
    `Down`: `DropIndex`. Przejrzeć wygenerowany kod przed commitem. Migracja jest zgodna z poprzednią
    wersją API (expand).
- **B7 DI:** `services.AddScoped<IChangeHistoryReader, ChangeHistoryReader>();` w
  `AddReportingInfrastructure` (scoped: zależy od scoped `ReportingDbContext`). Handler rejestruje
  istniejące `AddHandlersFromAssembly`.
- **B8 Api** (`ChangeHistoryEndpoints`, `internal static`):
  - `endpoints.MapGroup("/api/change-history").WithTags("ChangeHistory")`;
    `group.MapGet(string.Empty, ListAsync).WithName("Reporting.ListChangeHistory")` — tylko `GET`.
  - Parametry `[FromQuery] string? changedFrom, changedTo, businessFrom, businessTo, string[]? objectType, string? direction, page, pageSize`
    → `dispatcher.QueryAsync(new ListChangeHistoryQuery(…))` →
    `Results<Ok<ChangeHistoryPageResponse>, ValidationProblem>`.
  - `ChangeHistoryContracts.ToResponse(ChangeHistoryPageDto)`:
    - `ChangeHistoryPageResponse(Items, Total, Page, PageSize)`;
    - `ChangeHistoryItemResponse(string Id, string OccurredAt, ChangeHistoryAuthorResponse Author, ChangeHistoryObjectResponse Object, string Operation, IReadOnlyList<ChangeHistoryChangeResponse> Changes)`;
    - `Id = entry.Id.ToString(CultureInfo.InvariantCulture)`; `OccurredAt = ChangeHistoryCodes.FormatTimestamp`;
    - `ChangeHistoryAuthorResponse(string Kind)`;
    - `ChangeHistoryObjectResponse(string Type, Guid Id, string BusinessDate, string Label, ChangeHistoryParentResponse? Parent, bool Deleted)`;
    - `ChangeHistoryParentResponse(string Type, Guid Id)` — `null`, gdy wpis nie ma rodzica;
    - `ChangeHistoryChangeResponse(string Field, string? OldValue, string? NewValue, bool IsPersonalData)`
      — wartości bez zmian z audytu, kolejność `changes` z wpisu.
  - `ReportingModule.MapReportingEndpoints`: `OperationsEndpoints.Map` i `ChangeHistoryEndpoints.Map`.
- **B9 Testy integracyjne** — sekcja „Tests”.

### Data & contracts

- **Storage:** `reporting.AuditEntries` + indeks nieunikalny `IX_AuditEntries_ObjectBusinessDate
  (ObjectBusinessDate)`, migracja `<ts>_AuditEntriesBusinessDateIndex`. Istniejące
  `IX_AuditEntries_OccurredAtUtc` i `IX_AuditEntries_ObjectType_ObjectId` obsługują okres i `deleted`.
- **API:** sekcja „API Contract”.
- **Typy przez granicę:** brak nowych typów współdzielonych; enumy z `Shared.Kernel.Audit` i
  `WarsawTimeZone` z `Shared.Application` (istniejące).

### Authorization

Fallback policy hosta (sesja → `401`) → handler. Jedyny endpoint to `GET`, więc CSRF go nie dotyczy.
Inne metody na tej ścieżce: bez sesji `401`, z sesją i tokenem `405` (routing). Jedno konto (D-05),
więc nie ma zakresu zasobu do autoryzacji. Odpowiedź nie zawiera `AuthorAccountId`.

### Background jobs

None.

### Edge cases & null safety

| Edge case (spec) | Gdzie |
|---|---|
| czas letni / zimowy w okresie (25.10.2026, 29.03.2026) | B2 (`ConvertTimeToUtc` z `WarsawTimeZone`); R-H-1, I-H-3 |
| wpis 23:30 czasu polskiego | B2 (zimą 22:30 UTC tego samego dnia); R-H-1, I-H-3 |
| filtr daty biznesowej z jedną granicą | B4, B5 (każda granica osobno, włącznie); I-H-4 |
| zmiana w listopadzie obiektu z datą październikową | B5 (dwa niezależne predykaty); I-H-4 |
| data biznesowa wpisu = wartość po zmianie | B5 porównuje `ObjectBusinessDate` wpisu (spec, założenie 10); I-H-4 |
| obiekt usunięty po okresie filtra | B5 (`EXISTS` bez filtrów); I-H-7 |
| wpłata usunięta z istniejącego zamówienia; definicja cykliczna usunięta | B5 (`deleted` po `(ObjectType, ObjectId)` samego wpisu, nie rodzica); I-H-2, I-H-7 |
| równy `OccurredAtUtc` | B5 remis po `Id` w tym samym kierunku; I-H-5 |
| `pageSize` > 100, `page` 0, zła data, nieznany `objectType` | B4 → `400` przy kluczu; R-H-3, I-H-6 |
| strona poza zakresem | B5 (`skip >= total` → puste, prawdziwy `total`); I-H-5 |
| `ParentType` / `ParentId` nullowalne | DTO `AuditObjectType?`/`Guid?`; kontrakt `parent: null`; mapowanie tylko gdy oba mają wartość (CHECK `CK_AuditEntries_Parent` gwarantuje parę) |
| `OldValue` / `NewValue` nullowalne | przekazane bez zmian jako `null` |
| `OccurredAtUtc` z EF jako `Unspecified` | P2; I-H-1 sprawdza „Z” |

## Frontend Plan

### Step 0 — aplikacja

Jedna aplikacja: `app/`. Praca w `app/src/app/features/change-history/` (zastępuje zaślepkę) oraz w
`app/src/app/shared/` (słowniki, opcja kart w tabeli).

### Expected API contract (wyprowadzony ze spec)

| Method | Route | Request shape | Response shape | Used by |
|---|---|---|---|---|
| GET | `/api/change-history` | `changedFrom`, `changedTo`, opcjonalnie `businessFrom`, `businessTo`, `objectType` (powtarzany), `direction`, `page`, `pageSize` | `{ items: ChangeHistoryItem[], total, page, pageSize }`; `400` z `errors` po kluczach parametrów | `ChangeHistoryApi.list` |

`ChangeHistoryItem = { id: string, occurredAt: string, author: { kind: 'account'|'wooCommerceSync'|'automatedJob' }, object: { type: AuditObjectTypeCode, id: string, businessDate: string, label: string, parent: { type: AuditObjectTypeCode, id: string } | null, deleted: boolean }, operation: 'created'|'updated'|'deleted', changes: { field: string, oldValue: string|null, newValue: string|null, isPersonalData: boolean }[] }`.

### Routing

| Path | Page | Loaded how | Guard |
|---|---|---|---|
| `/historia-zmian` | `ChangeHistoryPage` | istniejące `section('historia-zmian', { loadComponent })` w `app.routes.ts` — ścieżka, plik i nazwa klasy się nie zmieniają | istniejący guard powłoki (sesja) |

`app.routes.ts`, `nav-items.ts`, `main-nav.spec.ts`, `return-url.spec.ts` bez zmian.

### Słowniki — co przechodzi do `shared/` (spec, uwaga 4)

Dziś obszary nie importują się nawzajem; `operation-models.ts` ma kopie `CATEGORY_LABELS`,
`METHOD_LABELS` i `REFUND_KIND_LABELS`. Historia zmian potrzebuje słowników z Orders i Expenses, więc
każdy z nich staje się używany przez więcej niż jeden obszar i przechodzi do
`app/src/app/shared/dictionaries/`. Modele obszarów eksportują je dalej pod dotychczasowymi nazwami,
więc pozostałe importy w obszarach się nie zmieniają.

| Słownik (dziś) | Nowe miejsce | Kto eksportuje dalej |
|---|---|---|
| `CATEGORY_LABELS`, typ `ExpenseCategory` (`features/expenses/expense-models.ts`) + kopia w `operation-models.ts` (`OperationCategory`) | `shared/dictionaries/expense-categories.ts`: `ExpenseCategory`, `EXPENSE_CATEGORY_LABELS` | `expense-models.ts` (`CATEGORY_LABELS`), `operation-models.ts` (`CATEGORY_LABELS`, `OperationCategory = ExpenseCategory`) — kopia usunięta |
| `PAYMENT_METHOD_LABELS` (`expense-models.ts`, `order-models.ts`) + `METHOD_LABELS` (`operation-models.ts`) — identyczne | `shared/dictionaries/payment-methods.ts`: `PaymentMethodCode`, `PAYMENT_METHOD_LABELS` | `expense-models.ts` i `order-models.ts` (`PAYMENT_METHOD_LABELS`; `ExpensePaymentMethod`, `PaymentMethod` = `PaymentMethodCode`), `operation-models.ts` (`METHOD_LABELS`, `OperationMethod`) — kopie usunięte |
| `CHANNEL_LABELS`, `STATUS_LABELS`, `CUSTOMER_KIND_LABELS`, `REFUND_KIND_LABELS` i typy `OrderChannel`, `FulfillmentStatus`, `CustomerKind`, `RefundKind` (`order-models.ts`) + kopia `REFUND_KIND_LABELS` w `operation-models.ts` | `shared/dictionaries/order-dictionaries.ts`: te same nazwy, z wyjątkiem `FULFILLMENT_STATUS_LABELS` (zamiast niejednoznacznego `STATUS_LABELS`) | `order-models.ts` (`STATUS_LABELS` jako alias), `operation-models.ts` (`REFUND_KIND_LABELS`, `OperationRefundKind = RefundKind`) — kopia usunięta |
| `shopStatusLabel` (`features/orders/shop-status.ts` + `.spec.ts`) | `shared/dictionaries/shop-status.ts` (+ `.spec.ts`), przeniesione `git mv` | import w `order-details-page.ts` i `orders-list-page.ts` zmieniony na `shared` |

Bez zmian: `REFUND_METHOD_LABELS` (`order-models.ts`), `WITHDRAWAL_METHOD_LABELS`
(`withdrawal-models.ts`) — listy opcji formularzy jednego obszaru (P10); `STATUS_TONES`,
`SOURCE_LABELS`, `PAYMENT_STATE_*`, `RECURRING_*` — nie są potrzebne w historii.

### Files

| File path | Action | Purpose |
|---|---|---|
| `app/src/app/shared/dictionaries/expense-categories.ts` | create | `ExpenseCategory`, `EXPENSE_CATEGORY_LABELS` |
| `app/src/app/shared/dictionaries/payment-methods.ts` | create | `PaymentMethodCode`, `PAYMENT_METHOD_LABELS` |
| `app/src/app/shared/dictionaries/order-dictionaries.ts` | create | kanały, stany realizacji, rodzaj klienta, rodzaj zwrotu |
| `app/src/app/shared/dictionaries/shop-status.ts`, `shop-status.spec.ts` | create (move) | z `features/orders/` bez zmiany treści |
| `app/src/app/features/orders/shop-status.ts`, `shop-status.spec.ts` | delete (move) | przeniesione |
| `app/src/app/features/orders/order-models.ts` | modify | dalszy eksport słowników z `shared` zamiast definicji |
| `app/src/app/features/orders/order-details-page.ts`, `orders-list-page.ts` | modify | import `shopStatusLabel` z `shared/dictionaries/shop-status` |
| `app/src/app/features/expenses/expense-models.ts` | modify | dalszy eksport kategorii i metod z `shared` |
| `app/src/app/features/operations/operation-models.ts` | modify | usunięte kopie `CATEGORY_LABELS`, `METHOD_LABELS`, `REFUND_KIND_LABELS` i typów → eksport z `shared` |
| `app/src/app/shared/ui/data-table.ts`, `data-table.html` | modify | wejście `mobileCards` (P9) |
| `app/src/app/shared/ui/data-table.spec.ts` | modify | F-H-6 |
| `app/src/app/features/change-history/change-history-models.ts` | create | typy kontraktu, rodzaje, etykiety, tony, `objectLink`, stałe tekstów |
| `app/src/app/features/change-history/audit-field-catalog.ts` (+ `.spec.ts`) | create | katalog pól, `fieldLabel`, `formatFieldValue`, `changeSummary` |
| `app/src/app/features/change-history/change-history-api.ts` (+ `.spec.ts`) | create | klient `GET /api/change-history` |
| `app/src/app/features/change-history/change-history.service.ts` (+ `.spec.ts`) | create | stan listy na sygnałach, `defaultChangePeriod` |
| `app/src/app/features/change-history/change-history-page.ts`, `.html` | modify (rewrite) | lista zamiast zaślepki |
| `app/src/app/features/change-history/change-history-page.spec.ts` | create | F-H-5 |
| `app/src/testing/change-history-testing.ts` | create | `changeHistoryItem()`, `changeHistoryPage()` z danymi fikcyjnymi |
| `app/src/app/app.routes.spec.ts` | modify | `/historia-zmian` poza `SECTIONS` (to już nie zaślepka); osobny test z odpowiedzią `/api/change-history` (wzór bloku „Operacje”) |
| `app/src/app/layout/app-shell.spec.ts` | modify | test nagłówka na `/historia-zmian` obsługuje żądanie `GET /api/change-history` (pusta strona z `change-history-testing`) |
| `app/src/app/layout/page-title-strategy.spec.ts` | modify | jw. — `verify()` nie może zostać z nieobsłużonym żądaniem |
| `.sdd/context/conventions.md` | modify | tabela „Path in `app/src/app/`”: `shared/dictionaries/`; „Components”: opcja `mobileCards` |

### Implementation steps

- **F1 Słowniki i modele.**
  - Pliki `shared/dictionaries/*` (tabela wyżej). Treść etykiet bez zmian.
  - Modele obszarów: `export type { … } from` / `export { … } from` (alias `as` tylko dla
    `CATEGORY_LABELS`, `STATUS_LABELS`, `METHOD_LABELS`). Zero importów między obszarami
    (`features/x` → `features/y`); sprawdza to F-H-7.
  - `change-history-models.ts`:
    - typy z kontraktu (`AuditObjectTypeCode`, `AuditOperationCode`, `AuditAuthorKindCode`,
      `ChangeHistoryChange`, `ChangeHistoryItem`, `ChangeHistoryPage`, `ChangeHistoryQuery`);
    - `OBJECT_TYPES` w stałej kolejności (`order`, `payment`, `refund`, `salesCorrection`,
      `expense`, `recurringExpenseDefinition`, `ownerWithdrawal`) i `OBJECT_TYPE_LABELS`:
      „Zamówienie”, „Wpłata”, „Zwrot”, „Korekta sprzedaży”, „Wydatek”, „Wydatek cykliczny”,
      „Wypłata właściciela”;
    - `AUTHOR_LABELS`: „Właściciel”, „Synchronizacja WooCommerce”, „Zadanie automatyczne”;
    - `OPERATION_LABELS`: „Utworzenie”, „Zmiana”, „Usunięcie”; `OPERATION_TONES`: `neutral`, `info`,
      `warning`; `DELETED_LABEL = 'Usunięto'` (ton `warning`);
    - `objectLink(item): string[] | null`: `null`, gdy `object.deleted`; `order` →
      `['/zamowienia', id]`; `payment`/`refund`/`salesCorrection` → `['/zamowienia', parent.id]`
      (gdy `parent` jest `null` → `null`, bez założenia o niepustej wartości; zamówień nie usuwa się,
      założenie 11); `expense` → `['/wydatki', id]`; `recurringExpenseDefinition` →
      `['/wydatki', 'cykliczne', id]`; `ownerWithdrawal` → `['/operacje', 'wyplaty', id]`;
    - `BUSINESS_DATE_HINT` = tekst podpowiedzi ze spec (Edge Cases);
    - teksty stanów: `EMPTY_TITLE = 'Nie ma jeszcze żadnych zmian.'`,
      `FILTERED_EMPTY_TITLE = 'Brak zmian dla wybranych filtrów.'`,
      `LIST_ERROR_MESSAGE = 'Nie udało się wczytać historii zmian.'`.
- **F2 Katalog pól** (`audit-field-catalog.ts`; F-H-2 najpierw):
  - `FieldFormat = 'text' | 'amount' | 'date' | 'dictionary' | 'boolean' | 'list' | 'number'`;
    `AuditFieldDefinition { label, format, dictionary? }`;
    `AUDIT_FIELD_CATALOG: Readonly<Record<AuditObjectTypeCode, Readonly<Record<string, AuditFieldDefinition>>>>`.
  - Pola, etykiety i słowniki (nazwy pól sprawdzone w `OrderField`, `EntryField`, `ExpenseField`,
    `RecurringDefinitionField`, `OwnerWithdrawalField` i `*Codes.Of(field)` — camelCase):

    | Obiekt | Pole → etykieta (format / słownik) |
    |---|---|
    | `order` | `orderDate` Data zamówienia (data); `channel` Kanał (`CHANNEL_LABELS`); `value` Wartość brutto (kwota); `fulfillmentStatus` Status realizacji (`FULFILLMENT_STATUS_LABELS`); `customerKind` Rodzaj klienta (`CUSTOMER_KIND_LABELS`); `customerName` Imię i nazwisko lub nazwa firmy; `customerPhone` Telefon; `customerEmail` E-mail; `customerStreet` Ulica i numer; `customerPostalCode` Kod pocztowy; `customerCity` Miejscowość; `customerTaxId` NIP; `suppliers` Dostawcy (lista); `zoReferences` Referencje ZO (lista); `invoiceNumber` Numer faktury; `notes` Uwagi do realizacji; `comment` Komentarz; `hasPhotos` Zdjęcia (tak/nie); `hasReview` Opinia (tak/nie); `shopStatus` Status w sklepie (`shopStatusLabel`); `shopDeleted` Usunięte w sklepie (tak/nie); `shopRefundedAmount` Kwota zwrócona w sklepie (kwota) |
    | `payment` | `amount` Kwota (kwota); `date` Data wpłaty (data); `method` Metoda (`PAYMENT_METHOD_LABELS`); `receivedBy` Przyjął |
    | `refund` | `kind` Rodzaj zwrotu (`REFUND_KIND_LABELS`); `amount` Kwota; `date` Data zwrotu; `method` Metoda (`PAYMENT_METHOD_LABELS`, P10); `reason` Powód |
    | `salesCorrection` | `amount` Kwota; `date` Data korekty; `reason` Powód |
    | `expense` | `description` Opis; `category` Kategoria (`EXPENSE_CATEGORY_LABELS`); `counterparty` Kontrahent; `costDate` Data kosztu; `dueDate` Termin płatności; `paidOn` Data zapłaty (daty); `grossAmount` Kwota brutto; `netAmount` Kwota netto (kwoty); `documentReference` Referencja dokumentu; `paymentMethod` Metoda płatności (`PAYMENT_METHOD_LABELS`); `payer` Płatnik; `comment` Komentarz |
    | `recurringExpenseDefinition` | `description` Opis; `category` Kategoria; `counterparty` Kontrahent; `grossAmount` Kwota brutto; `netAmount` Kwota netto; `paymentMethod` Metoda płatności; `payer` Płatnik; `comment` Komentarz; `dayOfMonth` Dzień miesiąca (liczba); `startsOn` Początek; `endsOn` Koniec; `pausedOn` Data wstrzymania (daty) |
    | `ownerWithdrawal` | `person` Osoba; `amount` Kwota; `withdrawnOn` Data wypłaty (data); `method` Metoda (`PAYMENT_METHOD_LABELS`) |

    Etykiety pól biorą tekst z istniejących formularzy, bez dopisku „(zł)”. Nie są wspólne z innym
    obszarem, więc zostają w katalogu.
  - `fieldLabel(type, field)`: etykieta z katalogu; nieznane pole → sama nazwa `field`.
  - `formatFieldValue(type, field, value: string | null)`:
    - `null` → `NO_DATA` („Brak danych”);
    - kwota → `formatPln(value)` (bez arytmetyki);
    - data → `formatDate`;
    - słownik → etykieta albo kod bez zmian, gdy go nie ma (`Object.hasOwn`);
    - tak/nie: `"true"` → „tak”, `"false"` → „nie”, inna wartość bez zmian;
    - lista → `JSON.parse`, gdy wynik to tablica tekstów → `join(', ')`, w przeciwnym razie (także
      wyjątek) surowa wartość;
    - liczba i tekst → bez zmian;
    - nieznane pole → surowa wartość. Nigdy nie rzuca wyjątku.
  - `changeSummary(item): { lines: string[]; more: number }` — dwa pierwsze `changes` w kolejności z
    API (P6): `created` → „Etykieta: jest”, `deleted` → „Etykieta: było”, `updated` → „Etykieta:
    było → jest”; `more = changes.length − 2` (min. 0) → w UI „i N więcej”.
- **F3 `ChangeHistoryApi.list(query)`** (`providedIn: 'root'`, wzór `OperationsApi`):
  `HttpParams` z `changedFrom`, `changedTo`, `direction`, `page`, `pageSize`; `businessFrom` /
  `businessTo` tylko gdy niepuste; `objectType` przez `append` dla każdego rodzaju (kolejność
  `OBJECT_TYPES`). `firstValueFrom`, ścieżka względna.
- **F4 `ChangeHistoryService`** (`providedIn: 'root'`, więc filtry i strona przetrwają przejście
  do obiektu i powrót — Behavior 8; wzór `OperationsService`):
  - `defaultChangePeriod(today = todayInWarsaw())` → `{ changedFrom: today − 29 dni, changedTo: today }`
    (arytmetyka na dacie kalendarzowej przez `Date.UTC`, nie na kwotach);
  - sygnały: filtry `{ changedFrom, changedTo, businessFrom, businessTo, objectTypes }`, sortowanie
    (`occurredAt`, domyślnie `desc`), strona, `items`, `total`, `loading`, `error`, `apiErrors`
    (błędy pól z `400`), `generalError` (błąd `400` bez znanego pola);
  - błędy przy polach (computed, porównanie tekstu `yyyy-MM-dd`): `changedFromError` („Podaj datę.”
    / `DATE_RANGE_MESSAGE` przy „od” > „do” / błąd API `changedFrom`), `changedToError`,
    `businessFromError` (odwrócony zakres / API), `businessToError` (API), `objectTypeError` (API);
    `filtersInvalid` → bez żądania (UI → Stany: „bez zapytania”);
  - `load()` z licznikiem sekwencji; `toApiError`:
    - `validation` → błędy przypięte do kluczy `changedfrom`, `changedto`, `businessfrom`,
      `businessto`, `objecttype` (`fieldErrorOf`); pozostałe klucze → `generalError` (pierwszy
      komunikat);
    - inny błąd → `error = LIST_ERROR_MESSAGE` z „Spróbuj ponownie”;
  - `activeFilters` (P7): okres tylko gdy ≠ domyślny („Zmiana: 01.10.2026 – 31.10.2026”);
    data biznesowa („Data biznesowa: 01.10.2026 – 31.10.2026” / „Data biznesowa: od 01.10.2026” /
    „Data biznesowa: do 31.10.2026”); każdy rodzaj („Rodzaj: Wydatek”);
  - `updateFilters(change)`, `toggleObjectType(type, checked)` (stała kolejność `OBJECT_TYPES`),
    `removeFilter(id)` (`period` → domyślny okres, `business` → oba pola puste, `type:{code}` →
    odznaczenie), `clearFilters()` → domyślne; każda zmiana filtra → strona 1 i wyczyszczenie
    `apiErrors`; `setPage`, `setSort` (tylko `occurredAt`).
- **F5 `ChangeHistoryPage`** (`/historia-zmian`):
  - nagłówek `h1` „Historia zmian”; **brak akcji głównej** i przycisków dodawania/edycji (UI → Akcja
    główna);
  - `app-data-table` `caption="Historia zmian"`, `filterable` (panel „Filtry” na telefonie),
    `mobileCards` (P9), `[filters]="activeFilters()"`, `emptyTitle` / `filteredEmptyTitle` z F1,
    `loading` (szkielet), `error` + `retry`;
  - filtry w `tableFilters` (`form novalidate`): `app-field` „Zmiana od”, „Zmiana do” (`required`,
    `type="date"`), „Data biznesowa od”, „Data biznesowa do” (`type="date"`), pod nimi
    `BUSINESS_DATE_HINT` (`hint` pola „Data biznesowa od”), `fieldset`/`legend` „Rodzaj obiektu” z
    7 checkboxami `OBJECT_TYPE_LABELS`; błąd `objectTypeError` pod grupą; `generalError` jako
    komunikat `role="alert"` nad tabelą;
  - kolumny (`DataTableColumn<ChangeHistoryItem>`, `rowKey = id`):
    - `occurredAt` „Czas”, sortowalna (jedyna), szablon `<time [attr.datetime]="row.occurredAt">`
      z `formatDateTime` (P8);
    - `author` „Autor”: `AUTHOR_LABELS[row.author.kind]`;
    - `object` „Obiekt”, szablon: `<span class="sr-only">{{ rodzaj }}: </span>`, potem link
      `routerLink=objectLink(row)` z `object.label` albo, przy `deleted`, sam tekst etykiety i
      `app-status-badge` „Usunięto” (`warning`). Pod spodem data biznesowa `formatDate` jako tekst
      drugorzędny (`text-(--sf-muted)`);
    - `operation` „Operacja”: `app-status-badge` z `OPERATION_LABELS` / `OPERATION_TONES`;
    - `changes` „Zakres zmiany”, szablon: linie `changeSummary` i „i N więcej”, przycisk
      „Pokaż zmiany” / „Ukryj zmiany” (`type="button"`, `min-h-11`, `aria-expanded`,
      `aria-controls="zmiany-{id}"`). Po rozwinięciu pod przyciskiem `div#zmiany-{id}` z tabelą
      (`caption` sr-only „Zmiany: {etykieta}”): nagłówki `th scope="col"` „Pole” / „Było” / „Jest”
      (`created` bez „Było”, `deleted` bez „Jest” — Behavior 6), wiersze `th scope="row"` z
      `fieldLabel` i komórki `formatFieldValue`. Przycisk zostaje w DOM, więc po zwinięciu fokus
      jest dalej na nim;
  - stan rozwinięcia: lokalny sygnał `Set<string>` id w komponencie (nie w serwisie);
  - `constructor` → `service.load()`.
- **F6 `app-data-table` — `mobileCards`** (F-H-6 najpierw):
  - `readonly mobileCards = input(false)`;
  - przy `true` poniżej `md`: `thead` `max-md:sr-only`, `table`/`tbody`/`tr` jako bloki, `tr` jako
    karta (`rounded-box border border-(--sf-secondary) p-3`, odstęp między kartami), `td` bez
    `sticky`, z widocznym nagłówkiem kolumny `<span class="block text-xs text-(--sf-muted) md:hidden">`
    nad treścią;
  - przy `false` (domyślnie) DOM i klasy bez zmian — istniejące listy niezmienione (`data-table.spec`
    i specy list przechodzą bez zmian).
- **F7 Specyfikacje tras i powłoki:** `app.routes.spec.ts` — usunięcie `/historia-zmian` z
  `SECTIONS` i import `ChangeHistoryPage` przeniesiony do nowego testu sekcji (odpowiedź
  `changeHistoryPage()` na `GET /api/change-history`, `h1` „Historia zmian”, komponent
  `ChangeHistoryPage`); `app-shell.spec.ts` i `page-title-strategy.spec.ts` — obsłużenie żądania listy.
- **F8 Konwencje:** dopisanie `shared/dictionaries/` i `mobileCards` w `.sdd/context/conventions.md`.

### State

- Lokalny w komponencie: zbiór rozwiniętych wierszy.
- Serwis (root): filtry, sortowanie, strona, wynik, błędy pól i ogólny (przetrwa nawigację).
- Stan serwera: lista i `total` — zawsze z API, bez lokalnego filtrowania, sortowania ani
  stronicowania.
- Pochodne: `activeFilters`, błędy pól, `filtersInvalid`, `changeSummary`, `objectLink`.

### Styling

- Tokeny przez klasy daisyUI i `text-(--sf-muted)`; brak literałów kolorów (`lint:colors`).
- Brak akcji głównej (widok tylko do odczytu).
- Operacja i „Usunięto” jako tekst w `app-status-badge`; ton tylko wzmacnia znaczenie.
- Telefon: panel „Filtry” z `app-data-table`, wiersze jako karty (`mobileCards`), obszary
  interakcji ≥ 44 px (`min-h-11`).

### Accessibility

- Etykiety pól przez `app-field`; checkboxy w `fieldset`/`legend`; podpowiedź przez `hint`
  (`aria-describedby`).
- Przycisk rozwinięcia: `aria-expanded`, `aria-controls`; tabela zmian z `caption`, `th scope="col"`
  i `th scope="row"`.
- Czas w `<time datetime>`; rodzaj obiektu jako tekst `sr-only`; „Usunięto” jako tekst.
- Fokus po zwinięciu zostaje na przycisku (ten sam element DOM).
- Link do obiektu to zwykłe `<a routerLink>` (klawiatura).

### Edge cases (frontend)

| Edge case | Gdzie |
|---|---|
| pole nieznane katalogowi | F2 `fieldLabel` / `formatFieldValue` (nazwa i surowa wartość) |
| lista jako tablica JSON; wartość nieczytelna | F2 format `list` |
| `null` w „Było” / „Jest” | `NO_DATA` |
| nieznany kod słownika | kod bez zmian |
| `deleted = true` | brak linku, znacznik „Usunięto” |
| `parent = null` przy wpłacie / zwrocie / korekcie | `objectLink` → `null` (etykieta bez linku) |
| filtr z jedną granicą daty biznesowej | F4 chip „od …” / „do …”, parametr tylko niepusty |
| odwrócony okres / brak daty | F4 `filtersInvalid`, bez żądania |
| `400` z API | F4 błędy przy polach, nieznany klucz → komunikat ogólny |
| pusto przy domyślnych / przy innych filtrach | P7 |
| powrót z obiektu | stan w serwisie root |

## API Contract

| Method | Route | Request | Response |
|---|---|---|---|
| GET | `/api/change-history` | `changedFrom`, `changedTo` (`yyyy-MM-dd`, wymagane, `changedFrom ≤ changedTo`), `businessFrom`, `businessTo` (`yyyy-MM-dd`, opcjonalne, oba → `businessFrom ≤ businessTo`), `objectType` (0..n: `order`, `payment`, `refund`, `salesCorrection`, `expense`, `recurringExpenseDefinition`, `ownerWithdrawal`), `direction` (`desc` domyślnie \| `asc`), `page` (≥ 1, domyślnie 1), `pageSize` (1–100, domyślnie 25) | `200 { items, total, page, pageSize }`; `400` ValidationProblem z kluczami `changedFrom`, `changedTo`, `businessFrom`, `businessTo`, `objectType`, `direction`, `page`, `pageSize` |
| POST, PUT, PATCH, DELETE | `/api/change-history` | — | `405` (sesja + CSRF); `401` bez sesji |

- `items[]`: `{ id: string, occurredAt: string (ISO 8601 UTC z „Z”), author: { kind }, object: { type, id, businessDate, label, parent: { type, id } | null, deleted }, operation, changes: [{ field, oldValue, newValue, isPersonalData }] }`.
- `author.kind`: `account` \| `wooCommerceSync` \| `automatedJob`; `operation`: `created` \|
  `updated` \| `deleted`.
- Wartości `changes` w formacie zapisu audytu (konwencje „Value format”), bez zmian.
- Sortowanie: `occurredAt`, potem `id` w tym samym kierunku. Strona za końcem → `items: []` z
  prawdziwym `total`.
- Wymaga sesji (polityka fallback). Nie ma tras zapisu.

## Integration Check

| Frontend expects | Backend provides | Gap |
|---|---|---|
| `GET /api/change-history` z `changedFrom`, `changedTo`, `businessFrom`, `businessTo`, powtarzanym `objectType`, `direction`, `page`, `pageSize` | B8 `[FromQuery]` o tych nazwach, `string[]? objectType` | — |
| opcjonalne `businessFrom`/`businessTo` pomijane, gdy puste | B4 (puste = brak) | — |
| `{ items, total, page, pageSize }` | B8 `ChangeHistoryPageResponse` (camelCase z domyślnej serializacji) | — |
| `id` jako tekst | B8 `Id.ToString(InvariantCulture)` | — |
| `occurredAt` ISO z „Z” (`<time datetime>`, `formatDateTime`) | B5 + B3 `FormatTimestamp` (P2) | — |
| `object.parent` `{ type, id }` albo `null`; `object.deleted` | B5, B8 | — |
| kody `author.kind`, `operation`, `object.type` | B3 | — |
| `changes[].field/oldValue/newValue/isPersonalData` w kolejności audytu | B5 (konwerter JSON), B8 | — |
| `400` z kluczami parametrów | B4 `FieldErrors` → `ValidationProblem` | — |
| sesja; brak zapisów | fallback policy; tylko `MapGet` | — |

No integration gaps.

## Invariant Check

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 1. Sales separate from cash | nie dotyczy (lista nie liczy sprzedaży ani przepływów) | n/d |
| 2. Backend decimal money | brak obliczeń; frontend tylko formatuje tekst kwoty z audytu przez `formatPln` | F-H-2 (kwota `1234.50` → `1 234,50 zł` bez arytmetyki) |
| 3. API-enforced access | fallback policy hosta; brak `AllowAnonymous` | `EndpointSecurityTests.Every_protected_endpoint_returns_401_without_session` (automatycznie, P4); I-H-9 |
| 4. Retries never duplicate | nie dotyczy (wyłącznie odczyt) | n/d |
| 5. Financial audit trail | tylko `MapGet`; zapis audytu bez zmian; `deleted` i wpis usunięcia z ostatnimi wartościami | I-H-1 (trzy wpisy wydatku, `Deleted` ze starymi wartościami); I-H-7; I-H-8 (`405`, liczba wierszy bez zmian); F-H-5 (brak akcji zmieniających) |
| 6. Secrets stay server-side | odpowiedź bez `AuthorAccountId` i sekretów | I-H-1 (brak właściwości `accountId` w `author`) |
| 7. Module and layer boundaries | Reporting czyta tylko `reporting.AuditEntries`; port w Application, implementacja w Infrastructure; Domain bez zmian; frontend bez importów między obszarami | `StoneFlow.Architecture.Tests` bez zmian i bez nowych wyjątków; F-H-7 |
| 8. No identifying data in docs | dane fikcyjne w testach i fabrykach (`Wydatek – Cięcie`, `ZAM/2026/11/0001`, „Klient Testowy”) | przegląd |

Brak blokerów.

### Wpływ na testy architektury

Bez zmian w `StoneFlow.Architecture.Tests`. `ListChangeHistory` i port nie używają EF ani HTTP
(`Application_does_not_depend_on_infrastructure_http_or_ef`), `ChangeHistoryReader` jest w
Infrastructure, Reporting nie odwołuje się do innych modułów (`ModuleBoundaryTests`), brak zmian
`.csproj` (`ProjectReferenceTests`), brak nowych encji (`DbContextSchemaTests`).

## Unified Implementation Order

1. **Testy aplikacji Reporting (czerwone):** R-H-1, R-H-2 → `ChangeHistoryPeriod`,
   `ChangeHistoryCodes` (B2, B3) → zielone.
2. **Port:** `ChangeHistoryPorts.cs` (B1).
3. **Testy handlera (czerwone):** R-H-3, R-H-4 → `ListChangeHistory` (B4) → zielone.
4. **Infrastructure:** indeks w `AuditRecordConfiguration`, migracja
   `AuditEntriesBusinessDateIndex` (B6), `ChangeHistoryReader` (B5), DI (B7).
5. **Api:** `ChangeHistoryContracts`, `ChangeHistoryEndpoints`, `ReportingModule` (B8).
6. **Testy integracyjne:** I-H-1 … I-H-10; regresja `AuditLogTests`, `OperationsApiTests`,
   `EndpointSecurityTests`, `OpenApiTests`.
7. **Frontend — słowniki:** `shared/dictionaries/*`, przeniesienie `shop-status`, dalszy eksport w
   `order-models.ts`, `expense-models.ts`, `operation-models.ts` (F1); `npm run typecheck` i
   istniejące testy zielone przed dalszą pracą.
8. **Frontend — tabela:** `mobileCards` + spec (F6).
9. **Frontend — historia:** modele (F1) → katalog + spec (F2) → klient + spec (F3) → serwis + spec
   (F4) → strona + spec (F5) → specy tras i powłoki (F7) → konwencje (F8).
10. Weryfikacja (sekcja „Verification”), potem ręczne scenariusze UI do `06-implementation-done.md`.

## Tests

| Test | Type | What it proves |
|---|---|---|
| R-H-1 `ChangeHistoryPeriodTests` | unit (Reporting.Tests) | 05.11.2026 → `[2026-11-04T23:00Z, 2026-11-05T23:00Z)`; 25.10.2026 (koniec czasu letniego) → `[2026-10-24T22:00Z, 2026-10-25T23:00Z)` (25 h); 29.03.2026 → `[2026-03-28T23:00Z, 2026-03-29T22:00Z)` (23 h); okres wielodniowy; wynik `Kind = Utc` |
| R-H-2 `ChangeHistoryCodesTests` | unit | każdy `AuditObjectType`, `AuditOperation`, `AuditAuthorKind` ma kod (teoria po `Enum.GetValues`), kody rodzajów w obie strony, dokładne porównanie (`Order` ≠ `order`); `FormatTimestamp` dla `Unspecified` i `Utc` kończy się „Z” i wraca do tej samej chwili |
| R-H-3 `ListChangeHistoryHandlerTests` — walidacja | unit (fałszywy reader) | brak / zły format / odwrócony okres zmiany → błąd przy `changedFrom` lub `changedTo`; odwrócona data biznesowa → `businessFrom`; jedna granica daty biznesowej przyjęta; nieznany `objectType` → jeden błąd; `direction` `up`; `page` `0`/`-1`/`abc`; `pageSize` `0`/`101`; przy błędzie reader nie jest wołany |
| R-H-4 `ListChangeHistoryHandlerTests` — kryteria | unit | reader dostaje przedział UTC z `ChangeHistoryPeriod`, daty biznesowe, zbiór rodzajów bez duplikatów (pusty = wszystkie), `Descending` domyślnie `true`, `asc` → `false`, domyślne `page` 1 i `pageSize` 25; wynik readera przekazany bez zmian |
| I-H-1 `ChangeHistoryApiTests` — wydatek dodany, poprawiony, usunięty | integration (SQL) | trzy wpisy (`created`, `updated`, `deleted`) z `author.kind = account`, `object.type = expense`, etykietą, datą biznesową, wszystkie `deleted = true`; wpis usunięcia ma wszystkie stare wartości i `newValue = null`; wpis zmiany tylko zmienione pola w kolejności audytu; `occurredAt` = czas zegara testu z „Z”; `id` jako tekst; brak `AuthorAccountId` w odpowiedzi |
| I-H-2 wpłata i rodzic | integration | wpis wpłaty ma `parent = { type: order, id }`; po usunięciu wpłaty jej wpisy mają `deleted = true`, a wpisy zamówienia `false` |
| I-H-3 okres w Europe/Warsaw | integration (wpisy wstawione SQL-em) | `changedFrom = changedTo = 2026-10-25`: 2026-10-24T21:59:59Z poza, 2026-10-24T22:00Z w, 2026-10-25T22:59Z w, 2026-10-25T23:00Z poza; wpis z 23:30 czasu polskiego 05.11 (22:30Z) w dniu 05.11 |
| I-H-4 data biznesowa i rodzaj | integration | tylko „od”, tylko „do”, oba (granice włącznie); zmiana z listopada obiektu z datą październikową widoczna przy okresie zmiany = listopad i dacie biznesowej = październik; wpis z datą po zmianie (listopad) nie trafia do filtra „październik”; `objectType=expense`; powtarzany `objectType=expense&objectType=payment`; filtry łącznie |
| I-H-5 sortowanie i strony | integration | domyślnie malejąco; `asc`; równy `OccurredAtUtc` → kolejność po `id` w tym samym kierunku i stabilna przy powtórzeniu; `pageSize=2` stronicuje; strona za końcem → `items: []`, prawdziwy `total` |
| I-H-6 walidacja | integration | `400` z kluczem: brak `changedFrom`, `changedTo=2026-13-01`, `changedFrom > changedTo`, `businessFrom > businessTo`, `objectType=cost`, `page=0`, `pageSize=101`, `direction=up` |
| I-H-7 `deleted` niezależne od filtrów | integration | wpis `created` z października i wpis `deleted` z listopada: przy okresie = październik wpis ma `deleted = true`; przy `objectType` innym niż obiekt — brak wpisu, a przy jego rodzaju `deleted = true` |
| I-H-8 tylko odczyt | integration | `POST`, `PUT`, `PATCH`, `DELETE` na `/api/change-history` z sesją i tokenem CSRF (`OrdersTest.SendAsync`) → `405`; `COUNT(*)` z `reporting.AuditEntries` bez zmian (wzór `OrdersApiTests.Delete_is_not_possible`) |
| I-H-9 dostęp | integration | `GET` bez sesji → `401`; `POST` bez sesji → `401` |
| I-H-10 indeks | integration | `sys.indexes` ma `IX_AuditEntries_ObjectBusinessDate` na `reporting.AuditEntries` |
| Regresja | integration | `AuditLogTests`, `OperationsApiTests`, `EndpointSecurityTests` (nowa trasa objęta automatycznie), `OpenApiTests` |
| Architektura | arch | `StoneFlow.Architecture.Tests` bez zmian i bez wyjątków |
| Migracje | narzędzie | `dotnet ef migrations has-pending-model-changes` dla `ReportingDbContext` bez zmian |
| F-H-1 `change-history-api.spec.ts` | unit (Vitest) | adres, parametry, `objectType` powtarzany, puste daty biznesowe pominięte |
| F-H-2 `audit-field-catalog.spec.ts` | unit | etykiety pól każdego rodzaju; kwota (`1234.50` → `1 234,50 zł`), data, słowniki według pary (rodzaj, pole): `method` wpłaty z `cashOnDelivery`, `kind` zwrotu, `category`, `channel`, `fulfillmentStatus`, `shopStatus`; nieznany kod bez zmian; tak/nie; lista `["A","B"]` → „A, B”, nieczytelna lista bez zmian; `null` → „Brak danych”; nieznane pole → nazwa i surowa wartość, bez wyjątku; `changeSummary` dla `created`/`updated`/`deleted` i „i N więcej” |
| F-H-3 `change-history-models` (w `audit-field-catalog.spec.ts` albo osobny blok) | unit | `objectLink` dla 7 rodzajów, rodzic dla wpłaty/zwrotu/korekty, `deleted` → `null`, `parent = null` → `null` |
| F-H-4 `change-history.service.spec.ts` | unit | `defaultChangePeriod` (przełom miesiąca i roku, rok przestępny); pierwsze żądanie z domyślnym okresem, `desc`, strona 1; zmiana filtra → strona 1; `filtersInvalid` bez żądania; `400` → błąd przy polu, nieznany klucz → `generalError`; błąd sieci → `LIST_ERROR_MESSAGE`; chipy (okres tylko ≠ domyślny, data biznesowa z jedną granicą, rodzaje) i ich usuwanie; `clearFilters`; stan zostaje po ponownym wstrzyknięciu (root) |
| F-H-5 `change-history-page.spec.ts` | component | kolumny i etykiety (autor, operacja jako badge z tonem, `<time datetime>`, rodzaj `sr-only`, data biznesowa); link do obiektu dla każdego rodzaju; „Usunięto” bez linku; skrót i „i N więcej”; rozwinięcie: `aria-expanded`/`aria-controls`, tabela z `th scope`, kolumny według operacji, fokus na przycisku po zwinięciu; filtry i podpowiedź; puste „Nie ma jeszcze żadnych zmian.” bez „Wyczyść filtry” przy domyślnych filtrach, „Brak zmian dla wybranych filtrów.” z „Wyczyść filtry” po filtrze; błąd z „Spróbuj ponownie”; ładowanie; brak przycisków dodawania i edycji |
| F-H-6 `data-table.spec.ts` | component | `mobileCards = true` → każda komórka ma nagłówek kolumny `md:hidden`, `thead` `max-md:sr-only`; domyślnie DOM bez zmian |
| F-H-7 granice obszarów | statyczny (lint/grep w weryfikacji) | brak importów `features/<a>` → `features/<b>`; `shop-status.spec.ts` przechodzi w nowym miejscu; specy zamówień, wydatków i operacji przechodzą po przeniesieniu słowników |
| `app.routes.spec.ts`, `app-shell.spec.ts`, `page-title-strategy.spec.ts` | component | `/historia-zmian` ładuje `ChangeHistoryPage` z `h1` i tytułem „Historia zmian · Stone Flow”; brak nieobsłużonych żądań |

E2E: odroczone do SF-21 (brak infrastruktury E2E, jak w SF-15 i SF-17). Ręczne scenariusze UI
trafią do `06-implementation-done.md` (spec, Verification → manual).

## Verification

Pamięć na tej maszynie jest ograniczona: build z `-m:1`, polecenia kolejno, bez równoległych
ciężkich przebiegów. Działające API może blokować `bin/Debug`, dlatego wszystko w `Release`.

Backend, z katalogu repozytorium (Git Bash):

```
dotnet tool restore
dotnet ef migrations add AuditEntriesBusinessDateIndex -p api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure -s api/src/StoneFlow.Api -c ReportingDbContext -o Persistence/Migrations --configuration Release
dotnet build stone-flow.slnx -c Release -m:1
dotnet format stone-flow.slnx --verify-no-changes
dotnet ef migrations has-pending-model-changes -p api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure -s api/src/StoneFlow.Api -c ReportingDbContext --configuration Release
dotnet test --project api/tests/StoneFlow.Reporting.Tests -c Release --no-build
dotnet test --project api/tests/StoneFlow.Architecture.Tests -c Release --no-build
export STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true'
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*ChangeHistoryApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*AuditLogTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OperationsApiTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*EndpointSecurityTests"
dotnet test --project api/tests/StoneFlow.Api.IntegrationTests -c Release --no-build -- --filter-class "*OpenApiTests"
```

Po `dotnet ef migrations add` przejrzeć `Up`/`Down` (tylko `CreateIndex`/`DropIndex`). Pełny
`dotnet test` i `deploy/scripts/test-migrate.sh` uruchamia CI.

Frontend, z `app/`, kolejno:

```
npm run typecheck
npm test -- --watch=false
npm run lint
npm run format:check
npm run build
grep -rnE "from '\.\./(orders|expenses|operations|integration|auth|dashboard|change-history)/" src/app/features
```

Ostatnie polecenie (F-H-7) nie może nic zwrócić.

Manual (ryzyko `high` — **przegląd człowieka przed merge**):

- scenariusze ręczne UI w `06-implementation-done.md` (spec Verification): dodanie, poprawa i
  usunięcie wydatku → trzy wpisy, znacznik „Usunięto” bez linku, rozwinięty wpis usunięcia z
  ostatnimi wartościami; filtr daty biznesowej z podpowiedzią; przejścia do zamówienia (także z
  wpłaty), wydatku, wydatku cyklicznego i wypłaty; powrót z zachowanym filtrem i stroną; widok
  telefonu (karty, panel filtrów); pusta lista przy domyślnych i przy zawężonych filtrach;
- przegląd: zamiana okresu na UTC (`ChangeHistoryPeriod`), predykat `deleted` niezależny od filtrów,
  brak `AuthorAccountId` w odpowiedzi, migracja (tylko indeks), przeniesienie słowników bez zmiany
  etykiet, `mobileCards` nie zmienia innych list.

## Risks

- **Format czasu (P8).** Spec: `dd.MM.yyyy HH:mm`; `formatDateTime` daje `dd.MM.yyyy, HH:mm` (z
  przecinkiem), jedyny format czasu technicznego w `shared/format`. Plan używa wspólnego formatera
  (konwencje: daty tylko przez `shared/format`). Jeśli właściciel chce formatu bez przecinka, zmiana
  dotyczy wszystkich widoków i powinna być osobnym zadaniem. `06-implementation-done.md` odnotuje to
  jako świadome odstępstwo.
- **Karty na telefonie (P9)** zmieniają współdzielony `app-data-table` (ryzyko `medium` dla
  komponentu). Opcja jest domyślnie wyłączona, a F-H-6 i istniejące specy list sprawdzają brak
  regresji. Zmiana `display` elementów tabeli poniżej `md` może osłabić semantykę tabeli w części
  czytników ekranu. Widoczny nagłówek w każdej komórce zachowuje kontekst. Do sprawdzenia ręcznie
  na telefonie.
- **Rozwinięcie w komórce.** Tabela „Pole / Było / Jest” jest w komórce „Zakres zmiany”, bo
  `app-data-table` nie ma wierszy rozwijanych na całą szerokość. Na szerokim ekranie komórka będzie
  wysoka. Jeśli okaże się nieczytelna, wiersz rozwijany na całą szerokość wymagałby kolejnej zmiany
  wspólnego komponentu.
- **Przeniesienie słowników** dotyka plików Orders, Expenses i Operations (głównie importy i dalsze
  eksporty, etykiety bez zmian). Regresję wychwycą istniejące specy tych obszarów. Dalsze eksporty
  pod starymi nazwami ograniczają zmianę, ale zostawiają dwie nazwy dla `STATUS_LABELS` /
  `FULFILLMENT_STATUS_LABELS` i `CATEGORY_LABELS` / `EXPENSE_CATEGORY_LABELS`.
- **`REFUND_METHOD_LABELS` i `WITHDRAWAL_METHOD_LABELS` (P10)** nadal powtarzają teksty metod. Plan
  ich nie zmienia (listy opcji formularzy jednego obszaru). Zmiana etykiety metody wymaga więc
  zmiany w trzech miejscach.
- **`Contains` na liście enumów z konwersją na tekst** (filtr `objectType`) i skorelowane `Any` w
  projekcji muszą przetłumaczyć się na SQL w EF Core 10. I-H-4 i I-H-7 to pokażą. Gdyby tłumaczenie
  się nie udało, trzeba zmienić kształt zapytania, ale bez wczytywania całej tabeli.
- **Wydajność `deleted`.** `EXISTS` dla każdego wiersza strony (≤ 100) na indeksie
  `(ObjectType, ObjectId)` jest tani; `COUNT` przy długim okresie bez filtrów czyta indeks
  `OccurredAtUtc`. Okres bez limitu (założenie 5) jest akceptowany przy skali D-08.
- **Zależność od SF-17.** Linki do wypłat (`/operacje/wyplaty/:id`) i `operation-models.ts` pochodzą
  z SF-17. PR #17 musi być zmergowany przed tym zadaniem (spec, Dependencies).
- **Dane osobowe w zakresie zmiany** są widoczne dla właściciela bez zmian (założenie 7). Anonimizacja
  (DANE-01, D-09) jest poza zakresem i nie jest przez ten plan blokowana ani przesądzana.
