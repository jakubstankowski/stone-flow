# Task Specification — SF-7

## Source

[SF-7](https://standev.youtrack.cloud/issue/SF-7)

## Goal

Zbudować wspólny mechanizm zapisu audytu zmian finansowych, z którego skorzystają moduły
zamówień, wydatków i integracji. Wpis zawiera autora (konto albo autor systemowy), czas UTC,
obiekt z datą biznesową, operację i zakres zmiany. Zapis odbywa się w tej samej transakcji co
zmiana danych: nieudany zapis audytu cofa zmianę. Ślad usunięcia pozostaje czytelny po
zniknięciu obiektu. Moduł zapisuje wpis przez jawny interfejs, bez dostępu do tabel innego
modułu.

## Context

- **Właściciel danych audytu:** moduł **Reporting** (schemat `reporting`, tabela
  `reporting.AuditEntries`). PRD sekcja 10 przypisuje listę „Historia zmian” raportowaniu.
- **Kontrakt zapisu:** `IAuditLog` i typy wpisu w `StoneFlow.Shared.Application/Audit`. Reguły
  referencji projektów dopuszczają współdzielone kontrakty tylko tam. Implementacja jest w
  `StoneFlow.Reporting.Infrastructure`, a reguły poprawności wpisu w `StoneFlow.Reporting.Domain`.
- **Wspólna transakcja:** `IUnitOfWork` w `StoneFlow.Shared.Application/Persistence` z
  implementacją w `StoneFlow.Shared.Infrastructure/Persistence`. Nie ma transakcji rozproszonej.
  - `ModuleConnection` (scoped, rejestrowany w `AddSharedInfrastructure`) to jedno połączenie SQL
    na zakres DI (żądanie). Bez `PersistSecurityInfo` (zmiana po przeglądzie 03, zapisana w
    planie): bazę tworzy pierwsza migracja modułu Access na własnym połączeniu, skrypty wdrożenia
    migrują po jednym kontekście na proces, a `test-migrate.sh` tworzy bazę z góry.
  - Konteksty modułów finansowych (Orders, Expenses, Reporting, Integrations) rejestrują się
    przeciążeniem `UseModuleSqlServer<TContext>(serviceProvider, schema)`. Przeciążenie podaje EF
    połączenie zakresu i dodaje `ModuleTransactionInterceptor`. Shared nie zna typów modułów;
    kontekst trafia do interceptora przez zdarzenia EF. Pula kontekstów (`AddDbContextPool`) jest
    niedozwolona, bo opcje zależą od zakresu.
  - **Dołączanie do transakcji** dla kontekstów utworzonych przed i po jej rozpoczęciu:
    - każde polecenie EF (zapytanie, `ExecuteUpdate`/`ExecuteDelete`, polecenia `SaveChanges`)
      dostaje aktywną transakcję w interceptorze poleceń (`CommandInitialized`);
    - przed `SaveChanges` interceptor zapisu wywołuje `Database.UseTransaction(transakcja)`, więc
      EF nie otwiera własnej transakcji (SqlClient nie obsługuje transakcji równoległych);
    - po zakończeniu jednostki pracy (zatwierdzenie albo wycofanie) dołączone konteksty dostają
      `UseTransaction(null)`.
- Moduł Access (SF-6) pozostaje bez zmian: własne połączenie i własna jednostka pracy.

## Scope

### In scope

- Aktualizacja `.sdd/context/conventions.md`: transakcje modułów finansowych przez
  `IUnitOfWork`, zapis audytu, zakaz puli kontekstów, nowy zakres DI na jednostkę pracy w
  zadaniach.

- Kontrakt `IAuditLog.RecordAsync(AuditEntry, CancellationToken)` i typy: autor, obiekt,
  operacja, zmiana pola.
- Encja domenowa wpisu z walidacją oraz tabela `reporting.AuditEntries` z migracją.
- Wspólne połączenie i `IUnitOfWork` dla kontekstów modułów finansowych, z automatycznym
  dołączaniem kontekstów do transakcji.
- Odrzucenie zapisu audytu poza transakcją jednostki pracy.
- Testy: domenowe i integracyjne na SQL Server (moduł testowy z własną tabelą).

### Out of scope

- Lista „Historia zmian” i jej API (SF-18). W tym zadaniu nie powstaje endpoint.
- Anonimizacja wpisów (DANE-01, „zadanie 22”). Mechanizm ją umożliwia przez oznaczenie pól z
  danymi osobowymi, ale jej nie realizuje.
- Wywołania audytu w konkretnych przypadkach użycia (zamówienia, wydatki — kolejne zadania).

## Behavior

1. Handler komendy finansowej wykonuje pracę w
   `IUnitOfWork.ExecuteInTransactionAsync(work, commitWhen)`:
   1. jednostka pracy otwiera wspólne połączenie i rozpoczyna transakcję;
   2. handler zmienia dane modułu (`SaveChanges` kontekstu modułu dołącza do transakcji);
   3. handler wywołuje `IAuditLog.RecordAsync` z autorem, obiektem, operacją i zakresem zmiany;
   4. wpis trafia do `reporting.AuditEntries` w tej samej transakcji;
   5. jednostka pracy zatwierdza transakcję, chyba że `commitWhen(wynik)` zwraca `false` —
      wtedy wycofuje ją i zwraca wynik. Dotyczy to handlera, który przetłumaczył błąd (np.
      konflikt współbieżności, naruszenie constraintu) na wynik zamiast wyjątku.
2. Wyjątek w kroku 2, 3 lub 4 (walidacja wpisu, błąd bazy, anulowanie) wycofuje całą
   transakcję. Nie zostaje ani zmiana danych, ani wpis.
2a. Zadanie w tle i synchronizacja tworzą nowy zakres DI na każdą jednostkę pracy (ponowienie
    nie dziedziczy połączenia ani śledzonych encji wycofanej próby).
2b. Gdy operacja niczego nie zmieniła (np. ponowienie bez zmian), handler nie wywołuje
    `RecordAsync` — pusta lista zmian jest błędem i wycofałaby operację (niezmiennik 4).
3. `RecordAsync` poza aktywną transakcją jednostki pracy kończy się `InvalidOperationException`
   i nic nie zapisuje (zabezpieczenie przed zapisem audytu bez zmiany danych lub odwrotnie).
4. Czas wpisu (`OccurredAtUtc`) ustala zapis audytu z `TimeProvider` w UTC. Wywołujący nie
   podaje czasu.
5. Autor:
   - `Account` z identyfikatorem konta, który handler dostaje w komendzie (konwencja SF-6);
   - `WooCommerceSync` („Synchronizacja WooCommerce”) albo `AutomatedJob` („Zadanie
     automatyczne”) bez identyfikatora konta.
6. Obiekt to rodzaj, identyfikator, data biznesowa, etykieta zapisana w chwili zmiany (np.
   numer zamówienia) oraz **obiekt nadrzędny**. Dzięki temu usunięty obiekt pozostaje
   rozpoznawalny, a wpisy wpłat, zwrotów i korekt można znaleźć po zamówieniu (lista SF-18,
   anonimizacja SF-22):

   | Rodzaj | Data biznesowa | Obiekt nadrzędny |
   |---|---|---|
   | `Order` | data zamówienia | brak |
   | `Payment` | data wpłaty | `Order` (wymagany) |
   | `Refund` | data zwrotu | `Order` (wymagany) |
   | `SalesCorrection` | data korekty | `Order` (wymagany) |
   | `Expense` | data kosztu | `RecurringExpenseDefinition` dla wystąpienia cyklicznego, inaczej brak |
   | `RecurringExpenseDefinition` | data rozpoczęcia cyklu | brak |
   | `OwnerWithdrawal` | data wypłaty | brak |

   Gdy zmiana dotyczy samej daty biznesowej, wpis ma nową datę, a stara i nowa wartość są w
   zakresie zmiany. Wystąpienia wydatku cyklicznego są audytowane jako `Expense`.
7. Zakres zmiany to lista pól `(nazwa, stara wartość, nowa wartość, dane osobowe)`:
   - `Created`: nowe wartości (stara pusta);
   - `Updated`: tylko zmienione pola;
   - `Deleted`: ostatnie wartości obiektu (nowa pusta) — ślad usunięcia.

   Lista nie może być pusta. Format wartości (tekst przygotowany przez wywołującego):
   - kwoty: dziesiętnie z kropką, kultura niezmienna (`1234.50`);
   - daty: `yyyy-MM-dd`; czas: ISO 8601 w UTC;
   - wartości słownikowe (status, kanał, kategoria): stały kod, nie etykieta;
   - brak wartości: `null` (nie pusty tekst);
   - nazwa pola: stała nazwa właściwości po angielsku w camelCase (`amount`, `paidAt`).

   Poprawność oznaczenia `IsPersonalData` należy do wywołującego; etykieta obiektu **nie może**
   zawierać danych osobowych (kontrakt; sprawdza to przegląd kodu modułów).
8. Wpisy są tylko dodawane: kontrakt nie ma operacji zmiany ani usunięcia. Baza nie blokuje
   zmian (brak triggerów), żeby anonimizacja (SF-22) mogła wyczyścić oznaczone wartości.

## Data / API

### Storage

`reporting.AuditEntries` (migracja w `StoneFlow.Reporting.Infrastructure`):

| Kolumna | Typ | Uwagi |
|---|---|---|
| `Id` | `bigint identity` | PK, kolejność zapisu |
| `OccurredAtUtc` | `datetime2` | UTC |
| `AuthorKind` | `nvarchar(30)` | `Account`, `WooCommerceSync`, `AutomatedJob` |
| `AuthorAccountId` | `uniqueidentifier null` | wymagany dokładnie dla `Account` (check constraint); bez FK do `access` |
| `ObjectType` | `nvarchar(50)` | `Order`, `Payment`, `Refund`, `SalesCorrection`, `Expense`, `RecurringExpenseDefinition`, `OwnerWithdrawal` |
| `ObjectId` | `uniqueidentifier` | bez FK (obiekt może być usunięty) |
| `ObjectBusinessDate` | `date` | data biznesowa obiektu |
| `ObjectLabel` | `nvarchar(200)` | etykieta w chwili zmiany |
| `ParentObjectType` | `nvarchar(50) null` | rodzaj obiektu nadrzędnego |
| `ParentObjectId` | `uniqueidentifier null` | identyfikator obiektu nadrzędnego; razem z rodzajem albo oba puste (check constraint) |
| `Operation` | `nvarchar(20)` | `Created`, `Updated`, `Deleted` |
| `Changes` | `nvarchar(max)` | JSON: `[{"field","old","new","personal"}]` |

Indeksy: `OccurredAtUtc` (filtr okresu w SF-18), `(ObjectType, ObjectId)` (historia
obiektu) i `(ParentObjectType, ParentObjectId)` (wpisy powiązane z zamówieniem).

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| — | — | — | — | — | Brak endpointów (lista: SF-18) |

### Types crossing a boundary

W `StoneFlow.Shared.Application/Audit`:

- `IAuditLog`: `Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)`;
- `AuditEntry(AuditAuthor Author, AuditObject Object, AuditOperation Operation,
  IReadOnlyList<AuditChange> Changes)`;
- `AuditAuthor`: `Account(Guid)`, `WooCommerceSync`, `AutomatedJob`;
- `AuditObject(AuditObjectType Type, Guid Id, DateOnly BusinessDate, string Label,
  AuditObjectReference? Parent = null)`;
- `AuditObjectReference(AuditObjectType Type, Guid Id)`;
- enumy `AuditAuthorKind`, `AuditObjectType`, `AuditOperation` w `StoneFlow.Shared.Kernel/Audit`
  (wspólne słownictwo domeny Reporting i kontraktu);
- `AuditChange(string Field, string? OldValue, string? NewValue, bool IsPersonalData = false)`.

W `StoneFlow.Shared.Application/Persistence`:

- `IUnitOfWork`: `Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work,
  Func<T, bool> commitWhen, CancellationToken cancellationToken)`; `commitWhen` jest wymagany
  (zmiana po przeglądzie 03, zapisana w planie) — przypadek użycia bez wyniku błędu podaje
  `_ => true`.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 5. Financial audit trail | Wpis z autorem, czasem UTC, obiektem, operacją i zakresem zmiany. Ślad usunięcia przez migawkę wartości. Zapis wyłącznie w transakcji zmiany danych. Brak operacji usuwania i zmiany wpisów w kontrakcie. |
| 7. Module and layer boundaries | Moduły zapisują przez `IAuditLog` (Shared.Application), bez referencji do Reporting i bez dostępu do jego tabel. Domena Reporting nie zależy od EF. Wspólne połączenie jest szczegółem Shared.Infrastructure. |
| 6. Secrets stay server-side | Wpis nie zawiera sekretów; połączenie bez zmian w konfiguracji. |
| 8. No identifying data in docs | Przykłady i testy z danymi fikcyjnymi. |
| 1–4 | Nie dotyczy (brak obliczeń finansowych i ponowień w tym zadaniu). |

## Risk

high — dane audytu finansowego i zmiana sposobu obsługi transakcji wszystkich modułów
finansowych. Wymagany przegląd człowieka przed merge.

## UI

Not applicable.

## Edge Cases

- `RecordAsync` bez transakcji → `InvalidOperationException`, nic nie zapisano.
- Wpis niepoprawny (pusta etykieta, etykieta > 200 znaków, brak zmian, pusta nazwa pola, autor
  `Account` bez identyfikatora) → wyjątek domenowy, rollback zmiany danych.
- Błąd bazy przy zapisie wpisu → rollback zmiany danych.
- Błąd zmiany danych po zapisie wpisu → rollback wpisu.
- Anulowanie (`CancellationToken`) w trakcie → rollback.
- Kilka wpisów w jednej transakcji (np. zamówienie i wpłata) → wszystkie albo żaden.
- Zagnieżdżone `ExecuteInTransactionAsync` → `InvalidOperationException` (jedna granica
  transakcji na przypadek użycia).
- Wartości z danymi osobowymi są oznaczone `personal: true`, a zanonimizować je można bez
  zmiany struktury wpisu.
- Obiekt nadrzędny niezgodny z tabelą w punkcie 6 (np. wpłata bez zamówienia) → wyjątek
  domenowy, rollback.
- Handler zwraca wynik błędu po częściowym zapisie → `commitWhen` = `false` → rollback.
- Kontekst utworzony przed rozpoczęciem transakcji i kontekst utworzony w jej trakcie → oba
  wykonują zapytania, `ExecuteUpdate` i `SaveChanges` w tej transakcji.
- Kolejna jednostka pracy w tym samym zakresie po zakończeniu poprzedniej → działa (konteksty
  odłączone).

## Dependencies

- SF-3: moduły, schematy, migracje per kontekst, `contexts.sh`.
- SF-6: identyfikator konta w komendzie (autor `Account`).

## Assumptions

Pytania doprecyzowujące i odpowiedzi (04.11.2026). Użytkownik nie odpowiedział na
poszczególne punkty („leć od razu ze /sdd-spec”, „rób wszystko sam”), więc przyjęto
propozycje 1a–6:

- **Q1 Właściciel danych:** jedna centralna tabela, zapis przez jawny interfejs, ta sama
  transakcja przez wspólne połączenie (propozycja 1a). **Zmiana względem propozycji:**
  właścicielem jest istniejący moduł Reporting, a nie nowy moduł `Audit`. PRD sekcja 10 wymienia
  moduły (zamówienia, wydatki, raportowanie z listą „Historia zmian”, integracje), a nowy moduł
  wymagałby decyzji spoza zadania.
- **Q2 Zakres zmiany:** lista pól z wartością starą i nową w JSON; migawka przy usunięciu;
  oznaczenie danych osobowych.
- **Q3 Obiekt:** rodzaj, identyfikator, data biznesowa, etykieta z chwili zmiany.
- **Q4 Autor:** konto (identyfikator) albo jeden z dwóch autorów systemowych; przekazywany
  jawnie przez handler.
- **Q5 Sposób zapisu:** jawne wywołanie `IAuditLog.RecordAsync` w handlerze, bez interceptora
  zmian EF.
- **Q6 Trwałość:** wpisy tylko dodawane; bez endpointu w tym zadaniu.
- Identyfikatory obiektów finansowych są typu `Guid` (konwencja istniejących encji).
- Moduł Access nie korzysta ze wspólnego połączenia; jego transakcje (SF-6) nie obejmują
  danych finansowych.
- Po przeglądzie specyfikacji (03, FAIL) dodano: mechanizm dołączania kontekstów do transakcji,
  obiekt nadrzędny, `commitWhen`, zasady formatu wartości i daty biznesowej, `PersistSecurityInfo`,
  nowy zakres DI na jednostkę pracy w zadaniach, brak wpisu bez zmian, aktualizację konwencji.
- Właściciel danych (Reporting zamiast nowego modułu) wymaga potwierdzenia przez człowieka w
  przeglądzie przed merge.

## Acceptance Criteria

- [ ] Moduł zapisuje wpis przez `IAuditLog` bez referencji do Reporting i bez dostępu do jego
  tabel (testy architektury przechodzą).
- [ ] Nieudany zapis audytu (walidacja i błąd bazy) cofa zmianę danych (test integracyjny).
- [ ] Nieudana zmiana danych cofa wpis audytu (test integracyjny).
- [ ] Wpis zawiera autora, czas UTC, obiekt z datą biznesową, operację i zakres zmiany.
- [ ] Autor systemowy jest odróżniony od konta, także ograniczeniem w bazie.
- [ ] Zapis audytu poza transakcją jest odrzucany.
- [ ] Ślad usunięcia zawiera ostatnie wartości obiektu.
- [ ] Dwa konteksty (utworzony przed i w trakcie transakcji) wykonują zapytanie,
  `ExecuteUpdate` i `SaveChanges` w jednej transakcji; wycofanie cofa zmiany obu (test
  integracyjny, także na kontenerze SQL z uwierzytelnianiem SQL w CI).
- [ ] `commitWhen = false` wycofuje zmiany.
- [ ] Wpłata, zwrot i korekta bez obiektu nadrzędnego `Order` są odrzucane.

## Verification

- build: `dotnet build stone-flow.slnx -c Release`
- tests: `dotnet test --solution stone-flow.slnx -c Release` (SQL: kontener lub
  `STONEFLOW_TEST_SQLSERVER`)
- migrations: `deploy/scripts/test-migrate.sh` (LocalDB: `STONEFLOW_TEST_SQLSERVER` i `SQLCMD`
  jak w README; CI: zadanie `deploy-dry-run`)
- manual: przegląd człowieka (risk high)
