# Implementation Plan — SF-7

Tryb: **pojedynczy obszar (backend)**. Bez UI i bez endpointów. Bez podziału na części.

> Uwaga o kolejności: na prośbę właściciela („rób wszystko sam”, „nie mam czasu”) część kodu
> powstała równolegle z przeglądami specyfikacji jako próba wykonalności. Plan opisuje stan
> docelowy i zamyka uwagi z `03-spec-review.md` (PASS WITH ISSUES). Przegląd kodu ocenia
> zgodność z tym planem.

## 1. Scope

Kontrakty audytu i jednostki pracy w Shared.Application oraz słownictwo w Shared.Kernel.
Wspólne połączenie z interceptorem transakcji i `UnitOfWork` w Shared.Infrastructure.
Encja `AuditRecord` w Reporting.Domain, zapis `AuditLog` i tabela `reporting.AuditEntries` z
migracją w Reporting.Infrastructure. Rejestracja kontekstów modułów finansowych przez wspólne
połączenie. Testy domenowe, integracyjne i architektury. Aktualizacja konwencji.

## 2. Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Shared.Kernel | `Audit/` | enumy autor, rodzaj obiektu, operacja (wspólne dla domeny i kontraktu) |
| Shared.Application | `Audit/`, `Persistence/` | `IAuditLog`, typy wpisu, `IUnitOfWork` |
| Shared.Infrastructure | `Persistence/` | `ModuleConnection`, `ModuleTransactionInterceptor`, `UnitOfWork`, `UseModuleSqlServer(sp, schema)`, DI |
| Reporting | Domain `Audit/`, Infrastructure `Audit/`, `Persistence/` | encja, zapis, konfiguracja, migracja |
| Orders, Expenses, Integrations | Infrastructure DI | wspólne połączenie |
| Testy | Reporting.Tests (nowy), Api.IntegrationTests, Architecture.Tests | dowody |
| Dokumentacja | `conventions.md` | zasady jednostki pracy, audytu, wspólnego połączenia |

## 3. Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Shared/StoneFlow.Shared.Kernel/Audit/AuditVocabulary.cs` | create | `AuditAuthorKind`, `AuditObjectType`, `AuditOperation` |
| `api/src/Shared/StoneFlow.Shared.Application/Audit/IAuditLog.cs` | create | kontrakt i typy wpisu (`AuditEntry`, `AuditAuthor`, `AuditObject`, `AuditObjectReference`, `AuditChange`) |
| `api/src/Shared/StoneFlow.Shared.Application/Persistence/IUnitOfWork.cs` | create | granica transakcji z wymaganym `commitWhen` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Persistence/ModuleConnection.cs` | create | połączenie zakresu, transakcja, dołączanie i odłączanie kontekstów |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Persistence/ModuleTransactionInterceptor.cs` | create | `CommandInitialized` + `SavingChanges(Async)` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Persistence/UnitOfWork.cs` | create | commit / rollback, `commitWhen`, odłączanie |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Persistence/ModuleDbContextOptions.cs` | modify | przeciążenie `UseModuleSqlServer<TContext>(sp, schema)` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/DependencyInjection.cs` | modify | `ModuleConnection`, `IUnitOfWork`, `TimeProvider` |
| `api/src/Modules/{Orders,Expenses,Reporting,Integrations}/…Infrastructure/DependencyInjection.cs` | modify | rejestracja kontekstu przez wspólne połączenie |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Audit/AuditRecord.cs` | create | encja z regułami poprawności (autor, obiekt, rodzic, etykieta, zmiany) |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Audit/AuditLog.cs` | create | `IAuditLog`: wymaga transakcji `ModuleConnection`, czas z `TimeProvider` |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Persistence/AuditRecordConfiguration.cs` | create | tabela, check constraints, indeksy, JSON zmian |
| `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Persistence/ReportingDbContext.cs` | modify | `AuditEntries` |
| `…/Reporting.Infrastructure/Persistence/Migrations/*_AuditEntries.cs` | create | migracja (tylko dodanie — zgodna z poprzednią wersją) |
| `api/tests/StoneFlow.Reporting.Tests/**` + `stone-flow.slnx` | create/modify | testy domenowe |
| `api/tests/StoneFlow.Api.IntegrationTests/Audit/AuditLogTests.cs` | create | testy transakcji i audytu na SQL Server |
| `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/StoneFlowApiFactory.cs` | modify | `ConfigureTestServices` (kontekst modułu testowego) |
| `api/tests/StoneFlow.Architecture.Tests/DbContextSchemaTests.cs` | modify | rejestracja `AddSharedInfrastructure` |
| `.sdd/context/conventions.md` | modify | zasady SF-7 |

## 4. Implementation steps

1. Shared.Kernel: enumy słownictwa audytu.
2. Shared.Application: `IAuditLog` i typy; `IUnitOfWork` z wymaganym `commitWhen`.
3. Shared.Infrastructure:
   - `ModuleConnection` (scoped, `IDisposable` i `IAsyncDisposable`) tworzy połączenie z
     konfiguracji, **bez** `PersistSecurityInfo` (uwaga 2 przeglądu: bazę tworzy pierwsza
     migracja Access na własnym połączeniu, `test-migrate.sh` tworzy bazę z góry, a hosting ma
     bazę);
   - interceptor: `CommandInitialized` przypisuje aktywną transakcję, `SavingChanges(Async)`
     wywołuje `UseTransaction`;
   - `UnitOfWork`: zagnieżdżenie → `InvalidOperationException`; zatwierdzenie tylko, gdy
     `commitWhen(wynik)`; w `finally` odłączenie kontekstów, a po wycofaniu `ChangeTracker.Clear()`
     (uwaga 4); kontekst zwolniony wcześniej jest pomijany;
   - przeciążenie `UseModuleSqlServer(sp, schema)` i rejestracje DI.
4. Moduły finansowe: rejestracja kontekstów przez przeciążenie (bez puli).
5. Reporting: `AuditRecord.Create` (reguły z tabeli w spec pkt 6, długości, enumy), konfiguracja
   EF (stringi dla enumów, JSON `[{field,old,new,personal}]`, check constraints autora i
   rodzica, indeksy), `AuditLog` (sprawdza `ModuleConnection.Transaction`, nie
   `CurrentTransaction` kontekstu), migracja `AuditEntries`.
6. Testy (sekcja 8), konwencje (uwaga 3: brak równoległych zapytań, `EnableRetryOnFailure`,
   bezpośredniego `BeginTransaction`).

## 5. Data & contracts

- Storage: `reporting.AuditEntries` zgodnie ze spec (z kolumnami rodzica).
- API endpoints: None.
- Shared types: zgodnie ze spec; `commitWhen` wymagany (zmiana względem spec, która dopuszczała
  `null` — uwaga 1 przeglądu).

## 6. Edge cases & null safety

| Edge case | Gdzie |
|---|---|
| Zapis audytu bez transakcji | `AuditLog` → `InvalidOperationException` |
| Niepoprawny wpis | `AuditRecord.Create` → `ArgumentException` → rollback |
| Błąd bazy przy audycie / przy danych | wyjątek w `work` → rollback |
| Wynik błędu | `commitWhen` → rollback |
| Zagnieżdżenie | `ModuleConnection.BeginTransactionAsync` |
| Kontekst przed / w trakcie transakcji, zwolniony wcześniej | interceptor + `EndTransaction` |
| Śledzone encje po wycofaniu | `ChangeTracker.Clear()` |
| Rodzic niezgodny z rodzajem | `AuditRecord.RequireAllowedParent` + check constraint (rodzaj i id razem) |
| Reguła rodzica dla wydatku cyklicznego | domena dopuszcza brak; obowiązek podania rodzica dla wystąpienia cyklicznego egzekwuje moduł wydatków (SF-11) |

## 7. Invariants

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 5. Financial audit trail | `UnitOfWork` + `AuditLog` + `AuditRecord` | `AuditLogTests` (commit razem, rollback w obie strony, poza transakcją, `commitWhen`), `AuditRecordTests` |
| 7. Module and layer boundaries | kontrakt w Shared.Application, implementacja w Reporting.Infrastructure | testy architektury (bez zmian reguł, przechodzą); test sondy używa wyłącznie `IAuditLog` |
| 6. Secrets | brak zmian w konfiguracji połączenia | przegląd |

## 8. Tests

| Test | Type | What it proves |
|---|---|---|
| `AuditRecordTests` (22) | unit | autor konto/systemowy, UTC, etykieta, zmiany, pola, rodzic wg rodzaju, enumy, ślad usunięcia |
| `Commit_saves_data_and_complete_audit_entry_together` | integration | komplet pól wpisu, JSON, dane osobowe, rodzic |
| `System_author_is_stored_without_account` | integration | rozróżnienie autora |
| `Invalid_audit_entry_rolls_back_data_change` | integration | nieudany audyt (walidacja) cofa zmianę |
| `Database_failure_of_audit_rolls_back_data_change` | integration | nieudany audyt (błąd bazy, trigger) cofa zmianę |
| `Failed_data_change_rolls_back_audit_entry` | integration | nieudana zmiana cofa audyt |
| `Audit_outside_unit_of_work_is_rejected` | integration | zapis tylko w transakcji |
| `Commit_when_false_rolls_back_and_returns_result` | integration | `commitWhen` |
| `Contexts_created_before_and_during_transaction_share_it_for_query_update_and_save` | integration | zapytanie, `ExecuteUpdate`, `SaveChanges` dwóch kontekstów, rollback, kolejna jednostka pracy |
| `Nested_unit_of_work_is_rejected` | integration | jedna granica |
| `Database_rejects_account_author_without_account_and_parent_without_id` | integration | check constraints |
| `DatabaseTests.All_module_migrations_apply_to_empty_database` | integration | migracje (z nową) |
| `DbContextSchemaTests` | architektura | schemat Reporting obejmuje `AuditEntries` |

## 9. Verification

- `dotnet restore stone-flow.slnx --locked-mode`, `dotnet format stone-flow.slnx --verify-no-changes`
- `dotnet build stone-flow.slnx -c Release`, `dotnet test --solution stone-flow.slnx -c Release`
  (LocalDB lokalnie; kontener z uwierzytelnianiem SQL w CI)
- `deploy/scripts/test-migrate.sh` (LocalDB) i `deploy-dry-run` w CI
- **Ręczny przegląd człowieka przed merge (risk high)**: wspólna transakcja, właściciel danych
  (Reporting), kontrakt audytu.

## 10. Risks

- Uwierzytelnianie SQL ze wspólnym połączeniem sprawdzi dopiero CI (lokalnie LocalDB z
  uwierzytelnianiem Windows).
- Zmiana spec: `commitWhen` wymagany; brak `PersistSecurityInfo` (spec go przewidywała).
- Właściciel danych audytu (Reporting) do potwierdzenia przez człowieka.
