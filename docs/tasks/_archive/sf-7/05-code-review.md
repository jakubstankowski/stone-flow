# Code review — SF-7

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Zakres ze specyfikacji jest pokryty. Działają: kontrakt `IAuditLog`/`IUnitOfWork`,
  wspólne połączenie z interceptorem, encja `AuditRecord` z regułami obiektu nadrzędnego, tabela
  z check constraints i indeksami, migracja oraz testy rollbacku w obie strony, `commitWhen`,
  zagnieżdżenia i dwóch kontekstów. Nie znalazłem naruszenia niezmienników ani błędu krytycznego.
  Do poprawienia: testy nie dowodzą, że potrzebna jest ścieżka `UseTransaction` w
  `SavingChanges`, kryterium akceptacji z uwierzytelnianiem SQL (CI) nie zostało jeszcze
  sprawdzone, a kilka przypadków brzegowych ze specyfikacji nie ma testu.
- Risk: high. Przed merge wymagany jest przegląd człowieka: wspólna transakcja modułów
  finansowych, Reporting jako właściciel danych audytu i kontrakt audytu.

Weryfikacja wykonana w tym przeglądzie: `dotnet build stone-flow.slnx -c Release` (0 ostrzeżeń,
0 błędów), `StoneFlow.Reporting.Tests` (22/22), `AuditLogTests` na LocalDB z uwierzytelnianiem
Windows (10/10), `StoneFlow.Architecture.Tests` (55/55). Na LocalDB sprawdziłem też ręcznie
zachowanie `INSERT … OUTPUT` przy wyzwalaczu (uwaga C4). Nie uruchamiałem pełnego zestawu testów,
`test-migrate.sh` ani testów z uwierzytelnianiem SQL, bo Docker jest niedostępny.

## Project Invariant Violations

| Invariant | File:line | What is wrong |
|---|---|---|

None.

Sprawdzenie po nazwie:

| Invariant | Ocena |
|---|---|
| 1. Sales separate from cash | Nie dotyczy (brak obliczeń). |
| 2. Backend decimal money | Nie dotyczy. Format kwot w zakresie zmiany określa kontrakt (tekst przygotowany przez wywołującego). |
| 3. API-enforced access | Nie dotyczy (brak endpointów). |
| 4. Retries never duplicate | Nie dotyczy. Konwencja nowego zakresu DI na jednostkę pracy jest zapisana (`conventions.md:233-238`). |
| 5. Financial audit trail | Spełniony na poziomie mechanizmu. Wpis zawiera autora, czas UTC z `TimeProvider`, obiekt z datą biznesową, operację i zakres zmiany. Zapis poza transakcją jest odrzucany (`AuditLog.cs:17-21`), rollback działa w obie strony, a kontrakt nie ma operacji zmiany ani usunięcia. Wywołania w przypadkach użycia należą do kolejnych zadań. |
| 6. Secrets stay server-side | Spełniony. Bez `PersistSecurityInfo` (`ModuleConnection.cs:19-20`), connection string nie trafia do komunikatów ani logów. |
| 7. Module and layer boundaries | Spełniony. Kontrakt jest w Shared.Application, słownictwo w Shared.Kernel, a Shared.Infrastructure nie zna typów modułów, bo kontekst przychodzi przez `DbContextEventData`. Domena Reporting zależy tylko od Kernel. Test sondy używa wyłącznie `IAuditLog`. Testy architektury przechodzą. |
| 8. No identifying data in docs | Spełniony. Dane testowe są fikcyjne. |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `.sdd/context/conventions.md:229` — reguła formatu wartości odsyła do
   `docs/tasks/sf-7/02-spec.md`. Po `sdd-archive` folder trafi do `docs/tasks/_archive/`, więc
   odnośnik przestanie działać. Reguły formatu (kwoty, daty, kody, `null`, nazwy pól w camelCase)
   powinny być wpisane bezpośrednio w konwencje.
2. `docs/tasks/sf-7/02-spec.md:26` i `:173` — specyfikacja nadal opisuje
   `PersistSecurityInfo=true` i `commitWhen = null`. Plan zapisuje oba odstępstwa (sekcje 5 i 10),
   ale specyfikacja nie została zaktualizowana. Trzeba ją poprawić albo zapisać odstępstwa w
   `06-implementation-done.md`, żeby kolejne zadania nie korzystały z nieaktualnego kontraktu.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Kontrakt `IAuditLog.RecordAsync` i typy wpisu | Covered | `IAuditLog.cs`, `AuditVocabulary.cs`. Zgodne z „Types crossing a boundary”. |
| `IUnitOfWork.ExecuteInTransactionAsync(work, commitWhen, ct)` | Covered | `commitWhen` jest wymagany, a nie opcjonalny. To odstępstwo zapisane w planie. |
| Wspólne połączenie na zakres DI, interceptor, bez puli | Covered | `ModuleConnection`, `ModuleTransactionInterceptor`, `UseModuleSqlServer(sp, schema)`. Orders, Expenses, Reporting i Integrations są przepięte, Access zostaje bez zmian. |
| Dołączanie kontekstów utworzonych przed transakcją i w jej trakcie (zapytanie, `ExecuteUpdate`, `SaveChanges`) | Covered | `CommandInitialized` i `SavingChanges(Async)`, test `AuditLogTests.cs:176`. Patrz T1. |
| Odłączenie po zakończeniu i kolejna jednostka pracy w tym samym zakresie | Covered | `ModuleConnection.cs:57-77`, test `AuditLogTests.cs:212-226`. |
| Odrzucenie audytu poza transakcją | Covered | `AuditLog.cs:17-21`, test `:143`. |
| Czas z `TimeProvider` w UTC | Covered | `AuditLog.cs:24`, `AuditRecord.cs:120`. |
| Autor: konto albo systemowy, także constraint w bazie | Covered | `AuditRecord.cs:86-91`, `CK_AuditEntries_Author`, test `:239`. |
| Obiekt z datą biznesową, etykietą i obiektem nadrzędnym według tabeli w pkt 6 | Covered | `AuditRecord.cs:137-156` zgadza się z tabelą. Wydatek może mieć definicję cykliczną albo nic. Obowiązek podania definicji dla wystąpienia cyklicznego należy do SF-11, co plan zapisuje. |
| Zakres zmiany jako JSON `[{field,old,new,personal}]`, niepusty | Covered | `AuditRecordConfiguration.cs:51-77`, test `:67-74`. |
| Ślad usunięcia | Covered | Na poziomie kontraktu i domeny (`AuditRecordTests` `Deletion_keeps_last_values_as_trace`). |
| `commitWhen = false` → rollback i zwrot wyniku | Covered | `UnitOfWork.cs:20-30`, test `:157`. Patrz C1. |
| Zagnieżdżona jednostka pracy → `InvalidOperationException` | Covered | `ModuleConnection.cs:27-30`, test `:230`. |
| Tabela `reporting.AuditEntries`, check constraints, trzy indeksy, migracja addytywna | Covered | `20261104142451_AuditEntries.cs`. |
| Aktualizacja konwencji | Covered | Patrz uwaga konwencji 1. |
| Edge: anulowanie → rollback | Partial | Kod obsługuje anulowanie (wyjątek przed commitem, dispose wycofuje transakcję), ale nie ma testu (T2). |
| Edge: kilka wpisów w jednej transakcji → wszystkie albo żaden | Partial | Wynika z mechanizmu, ale nie ma testu (T2). |
| AC: dwa konteksty także na kontenerze SQL z uwierzytelnianiem SQL w CI | Partial | Lokalnie sprawdzone tylko z uwierzytelnianiem Windows (T3). |

## Plan Deviations

None. Wszystkie pliki z sekcji 3 planu są w commicie 7eae0ab. Odstępstwa od specyfikacji
(`commitWhen` wymagany, brak `PersistSecurityInfo`) plan zapisuje sam. Dodatkowo powstał
`StoneFlow.Reporting.Tests/packages.lock.json`, co wynika z lockfile'ów per projekt.

## Null Safety Issues

1. `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Audit/AuditLog.cs:23-34` —
   brak strażnika dla `entry.Author`, `entry.Object` i `entry.Changes`. Wartość `null` przekazana
   przez `null!` albo deserializację kończy się `NullReferenceException` zamiast
   `ArgumentException`. Rollback i tak nastąpi, więc to niska waga.

## Correctness Issues

1. **C1 (niska)** `api/src/Shared/StoneFlow.Shared.Infrastructure/Persistence/UnitOfWork.cs:27` —
   `RollbackAsync(cancellationToken)` przy `commitWhen = false`. Jeśli token zostanie anulowany po
   zakończeniu `work`, wywołujący dostaje `OperationCanceledException` zamiast wyniku-porażki. To
   rozmija się z kontraktem `IUnitOfWork.cs:14-16` („wycofuje zmiany i zwraca wynik”). Wycofanie nie
   powinno zależeć od tokenu: wystarczy `CancellationToken.None` albo pozostawienie wycofania
   `DisposeAsync`.
2. **C2 (niska)** `api/src/Shared/StoneFlow.Shared.Infrastructure/Persistence/ModuleConnection.cs:57-73` —
   `ChangeTracker.Clear()` po wycofaniu obejmuje tylko konteksty dołączone przez `SaveChanges`.
   Kontekst, który w wycofanej jednostce wczytał i zmienił śledzone encje bez `SaveChanges` (np.
   handler zwrócił wynik-porażkę przed zapisem), zachowuje te zmiany. Kolejny `SaveChanges` w tym
   samym zakresie by je zapisał. Dziś chroni przed tym tylko reguła konwencji „po wycofaniu zakres
   nie zapisuje” (`conventions.md:237-238`). Warto to zapisać wprost w komentarzu `EndTransaction`
   albo czyścić także konteksty widziane w `CommandInitialized`.
3. **C3 (informacyjna, do potwierdzenia w CI)**
   `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/TestSupport.cs:79-95` i
   `ModuleDbContextOptions.cs:44-49` — przy uwierzytelnianiu SQL bez `PersistSecurityInfo`
   `SqlConnection.ConnectionString` traci hasło po pierwszym otwarciu. Ponowne otwarcie tego samego
   obiektu działa, ale EF buduje połączenie do `master` z `ConnectionString`, więc
   `Migrate` lub `EnsureCreated` na nieistniejącej bazie, wywołane po otwarciu wspólnego
   połączenia, zakończy się błędem logowania. Obecne ścieżki tego unikają:
   `TestDatabase.MigrateAsync` zaczyna od Access na własnym połączeniu, `migrate.sh` uruchamia
   osobny proces na kontekst, a `test-migrate.sh` tworzy bazę z góry. Ścieżka zależy jednak od
   kolejności i trzeba ją potwierdzić w CI (T3).
4. **C4 (niska, test)** `api/tests/StoneFlow.Api.IntegrationTests/Audit/AuditLogTests.cs:113-114` —
   wyzwalacz `INSTEAD OF INSERT … THROW 50000` nigdy się nie wykona. EF wstawia wiersz z
   kolumną identity przez `INSERT … OUTPUT INSERTED.Id` bez `INTO`, a SQL Server odrzuca takie
   polecenie na tabeli z wyzwalaczem błędem 334. Sprawdziłem to na LocalDB. Test nadal dowodzi, że
   błąd bazy przy audycie wycofuje zmianę danych, ale nie z opisanej przyczyny. Warto sprawdzić numer
   błędu albo wymusić błąd inaczej, np. constraintem.

## Test Gaps

1. **T1 (średnia)** `api/src/Shared/StoneFlow.Shared.Infrastructure/Persistence/ModuleTransactionInterceptor.cs:12-25`
   oraz `AuditLogTests.cs:38-39`, `:197-200`, `:300-305` — każdy `SaveChanges` w testach zapisuje
   jeden wiersz. EF wykonuje takie polecenie bez własnej transakcji, a transakcję podpina sam
   `CommandInitialized`. Gdyby usunąć `SavingChanges` z wywołaniem `UseTransaction`, wszystkie
   testy nadal by przeszły. Bez tego wywołania zapis wielu encji w jednym `SaveChanges` (EF
   rozpoczyna wtedy własną transakcję) kończy się błędem „SqlConnection does not support parallel
   transactions”, a właśnie dla tego przypadku powstał interceptor (spec, Context). Brakuje testu
   z co najmniej dwiema encjami w jednym `SaveChanges` wewnątrz jednostki pracy i rollbackiem.
2. **T2 (niska)** `AuditLogTests.cs` — brak testów dla dwóch przypadków brzegowych ze
   specyfikacji: anulowania `CancellationToken` w trakcie pracy (rollback) i kilku wpisów audytu
   w jednej transakcji (wszystkie albo żaden).
3. **T3 (średnia, przed merge)** — kryterium akceptacji „dwa konteksty … także na kontenerze SQL z
   uwierzytelnianiem SQL w CI” nie zostało jeszcze sprawdzone. Autor uruchamiał testy tylko na
   LocalDB z uwierzytelnianiem Windows, ja też, bo Docker jest niedostępny. Przed merge trzeba mieć
   zielony przebieg CI (testy z kontenerem i `deploy-dry-run`), co obejmuje też C3.

## Code Smells

1. `api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure/Persistence/AuditRecordConfiguration.cs:53` —
   `JsonSerializerDefaults.Web` z domyślnym enkoderem zapisuje znaki spoza ASCII (polskie litery w
   wartościach) jako `\uXXXX`. Po parsowaniu dane są poprawne, ale w surowej kolumnie trudniej je
   czytać i wyszukiwać tekstem (anonimizacja SF-22, ręczna diagnostyka). Do rozważenia
   `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Nit.
2. `api/src/Modules/Reporting/StoneFlow.Reporting.Domain/Audit/AuditRecord.cs:122` —
   `isAccount ? authorAccountId : null` jest zbędne, bo warunek w liniach 86-91 już to gwarantuje.
   Nit.

## Recommendation

- Merge as-is, pod dwoma warunkami: zielony CI z uwierzytelnianiem SQL (T3) i przegląd
  człowieka (risk high). Krytycznych uwag nie ma. Zalecam domknąć T1 i uwagę konwencji 1 w tym
  samym PR. Pozostałe uwagi mogą trafić do kolejnych zadań.
