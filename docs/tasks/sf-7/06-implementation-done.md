# Implementation done — SF-7

## Summary

Dostarczono mechanizm zapisu audytu zmian finansowych:
- kontrakty `IAuditLog` i `IUnitOfWork` w `StoneFlow.Shared.Application`, słownictwo audytu w
  `StoneFlow.Shared.Kernel`;
- wspólne połączenie modułów finansowych z interceptorem dołączającym konteksty do transakcji
  jednostki pracy (`StoneFlow.Shared.Infrastructure`);
- encję `AuditRecord` (Reporting.Domain) i zapis do `reporting.AuditEntries` z migracją
  `AuditEntries` (Reporting.Infrastructure);
- testy domenowe, integracyjne i architektury oraz zasady w `conventions.md`.

Moduły finansowe nie wywołują jeszcze audytu — zrobią to zadania zamówień i wydatków.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Zapis przez jawny interfejs bez dostępu do tabel innego modułu | Implemented | `IAuditLog` w Shared.Application; testy architektury bez zmian reguł |
| Zapis w tej samej transakcji co zmiana danych | Implemented | `IUnitOfWork` + `ModuleConnection` + `ModuleTransactionInterceptor` |
| Nieudany zapis audytu cofa zmianę (walidacja i błąd bazy) | Implemented | `AuditLogTests` |
| Nieudana zmiana danych cofa wpis | Implemented | `AuditLogTests` |
| Autor, czas UTC, obiekt z datą biznesową, operacja, zakres zmiany | Implemented | `AuditRecord`, `AuditEntries` |
| Autor systemowy odróżniony od konta (także w bazie) | Implemented | `CK_AuditEntries_Author` |
| Obiekt nadrzędny (zamówienie dla wpłaty, zwrotu, korekty) | Implemented | reguły w `AuditRecord`, `CK_AuditEntries_Parent`, indeks |
| Ślad usunięcia z ostatnimi wartościami | Implemented | `Deleted` + zakres zmiany (test domenowy) |
| Zapis poza transakcją odrzucany | Implemented | `InvalidOperationException` |
| `commitWhen` wycofuje wynik błędu | Implemented | wymagany parametr |
| Dwa konteksty (przed i w trakcie transakcji): zapytanie, `ExecuteUpdate`, `SaveChanges` | Implemented | testy integracyjne; uwierzytelnianie SQL — w CI |
| Oznaczenie danych osobowych do anonimizacji | Implemented | `IsPersonalData` → `personal` w JSON |
| Aktualizacja konwencji | Implemented | `conventions.md` (CQRS, Data access) |

## Files

### Created

- `api/src/Shared/StoneFlow.Shared.Kernel/Audit/AuditVocabulary.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Audit/IAuditLog.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Persistence/IUnitOfWork.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Persistence/{ModuleConnection,ModuleTransactionInterceptor,UnitOfWork}.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Audit/AuditRecord.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Audit/AuditLog.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Persistence/AuditRecordConfiguration.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Persistence/Migrations/20261104142451_AuditEntries{,.Designer}.cs`
- `api/tests/StoneFlow.Reporting.Tests/**` (projekt, lockfile, `Audit/AuditRecordTests.cs`)
- `api/tests/StoneFlow.Api.IntegrationTests/Audit/AuditLogTests.cs`
- `docs/tasks/sf-7/{02-spec,03-spec-review-v1,03-spec-review,04-implementation-plan,05-code-review,06-implementation-done}.md`

### Modified

- `api/src/Shared/StoneFlow.Shared.Infrastructure/{DependencyInjection.cs,Persistence/ModuleDbContextOptions.cs}`
- `api/src/Modules/{Orders,Expenses,Reporting,Integrations}/…Infrastructure/DependencyInjection.cs`
- `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Persistence/{ReportingDbContext.cs,Migrations/ReportingDbContextModelSnapshot.cs}`
- `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/StoneFlowApiFactory.cs`
- `api/tests/StoneFlow.Architecture.Tests/DbContextSchemaTests.cs`
- `stone-flow.slnx`, `.sdd/context/conventions.md`, `docs/tasks/sf-7/task.md`

## Planned Units

| Item from the plan | Status |
|---|---|
| Słownictwo audytu (Kernel) | Exists |
| `IAuditLog` i typy wpisu, `IUnitOfWork` | Exists |
| `ModuleConnection`, `ModuleTransactionInterceptor`, `UnitOfWork`, przeciążenie `UseModuleSqlServer` | Exists |
| Rejestracja kontekstów modułów finansowych przez wspólne połączenie | Exists |
| `AuditRecord`, `AuditLog`, konfiguracja EF, migracja `AuditEntries` | Exists |
| Testy domenowe, integracyjne, architektury | Exists |
| Konwencje | Exists |

## Database

Reporting: migracja `20261104142451_AuditEntries` — tabela `reporting.AuditEntries` z check
constraints `CK_AuditEntries_Author`, `CK_AuditEntries_Parent` i indeksami
`OccurredAtUtc`, `(ObjectType, ObjectId)`, `(ParentObjectType, ParentObjectId)`. Tylko dodanie
(zgodna z poprzednią wersją API).

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| `AuditRecordTests` (22) | Exists | Passed |
| `AuditLogTests` (12, w tym po przeglądzie: kilka poleceń w jednym `SaveChanges`, anulowanie, błąd bazy przez constraint) | Exists | Passed (LocalDB) |
| `DatabaseTests` (migracje) | Exists | Passed (LocalDB) |
| `DbContextSchemaTests` | Exists | Passed |
| Kontrola mutacyjna: wyłączenie `UseTransaction` w interceptorze | — | test `Save_with_several_statements…` nie przechodzi (oczekiwane), po przywróceniu przechodzi |

## Verification

| Command | Outcome |
|---|---|
| `dotnet restore stone-flow.slnx --locked-mode` | Passed |
| `dotnet format stone-flow.slnx --verify-no-changes` | Passed |
| `dotnet build stone-flow.slnx -c Release` | Passed (0 ostrzeżeń) |
| `dotnet test --solution stone-flow.slnx -c Release` (LocalDB) | Passed — 223 testy; w pierwszym przebiegu 42 błędy sprzątania fixture (LocalDB niedostępny przy usuwaniu baz), powtórzony `StoneFlow.Api.IntegrationTests`: 85/85 bez błędów |
| `deploy/scripts/test-migrate.sh` (LocalDB) | Passed (przed poprawkami po przeglądzie; poprawki nie zmieniają migracji) |
| CI na PR z kontenerem SQL (uwierzytelnianie SQL) i `deploy-dry-run` | Not run (uruchomi się na PR) |

## Deviations

- `commitWhen` jest wymagany (spec pierwotnie: opcjonalny) — zapisane w planie; spec poprawiona.
- Bez `PersistSecurityInfo` (spec pierwotnie: z nim) — zapisane w planie; spec poprawiona.
- Właściciel danych: moduł Reporting zamiast nowego modułu `Audit` (spec, Assumptions).
- Część kodu powstała równolegle z przeglądami specyfikacji na prośbę właściciela (plan, uwaga).

## Additional Implementation

Additional implementation not covered by the original documents:

- `StoneFlowApiFactory.ConfigureTestServices` (rejestracja kontekstu modułu testowego w testach).
- Pominięcie kontekstów zwolnionych przed końcem jednostki pracy w `ModuleConnection.EndTransaction`.
- JSON zakresu zmiany zapisuje polskie znaki bez sekwencji `\uXXXX`.

## Open Items

- **Human review required before merge** (risk high) — w tym potwierdzenie właściciela danych
  audytu (Reporting) i projektu wspólnej transakcji.
- CI z kontenerem SQL (uwierzytelnianie SQL) musi być zielone przed merge (05: T3, C3 —
  tworzenie brakującej bazy na wspólnym połączeniu działa dzięki kolejności migracji).
- 05 C2: `ChangeTracker.Clear()` obejmuje tylko konteksty dołączone przez `SaveChanges`;
  niezapisane zmiany innych kontekstów chroni reguła w konwencjach (zakres nie zapisuje po
  wycofaniu).
- Moduły finansowe wywołają audyt w swoich zadaniach (zamówienia, wydatki, integracje);
  egzekwowanie rodzica dla wystąpienia wydatku cyklicznego — w module wydatków.
- PR SF-7 zawiera commit konwencji z PR #5; po scaleniu #5 zostanie w nim sam audyt.
