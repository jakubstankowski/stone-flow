# Implementation Plan — SF-11

Tryb: plan jednowarstwowy (tylko backend, bez UI i bez nowego kontraktu dla frontendu poza
`GET /api/jobs`). Bez podziału na części: zmiana jest addytywna i mieści się w jednym przeglądzie.

## 1. Scope

Plan dostarcza trwały harmonogram zadań ze specyfikacji `02-spec.md` (wersja 2): kontrakt w
`StoneFlow.Shared.Application/Scheduling`, implementację w `StoneFlow.Shared.Infrastructure/Scheduling`
(`SchedulingDbContext`, schemat `scheduling`, runner z dzierżawą i fencingiem, `BackgroundService`),
endpoint `GET /api/jobs` w hoście, migrację, testy jednostkowe, integracyjne i architektury oraz zmiany
w skryptach migracji, `docs/wdrozenia.md` i `conventions.md`. Domyka uwagi niekrytyczne 1–7
z `03-spec-review.md` (sekcja 10 i odwołania „[R1]”–„[R7]” niżej).

## 2. Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Shared.Application | `Scheduling/` | kontrakt zadań, definicja, harmonogram dzienny i strefa, wynik uruchomienia, zapytanie stanu i port odczytu |
| Shared.Infrastructure | `Scheduling/` | `SchedulingDbContext` i migracja, magazyn dzierżawy, runner, sprawdzenie należności, `BackgroundService`, opcje, rejestracja |
| Host `StoneFlow.Api` | `Scheduling/JobsEndpoints.cs`, `Program.cs` | `GET /api/jobs` |
| Testy | Shared.Tests, Api.IntegrationTests, Architecture.Tests | dowody kryteriów |
| Deploy | `deploy/scripts/contexts.sh`, `deploy/scripts/test-migrate.sh` | migracja nowego kontekstu w CI i wdrożeniu [R5] |
| Docs | `docs/wdrozenia.md`, `.sdd/context/conventions.md` | konfiguracja hostingu, D-08, nowe zasady [R7] |

## 3. Files

| File path | Action | Purpose |
|---|---|---|
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/IScheduledJob.cs` | create | `Task RunAsync(JobRunContext, CancellationToken)` |
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobDefinition.cs` | create | `JobDefinition(string Key, JobSchedule Schedule, TimeSpan MaxDuration)` + walidacja klucza |
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobSchedule.cs` | create | `DailyAt(TimeOnly)`, `LatestSlotAtOrBefore(instant)`, `FirstSlotAtOrAfter(instant)`, `Describe()` |
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/WarsawTimeZone.cs` | create | strefa `Europe/Warsaw` z rezerwą `Central European Standard Time` [R6] |
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobDue.cs` | create | czysta reguła należności i odstępu ponowień |
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobRunContext.cs` | create | `RunId`, `DueAt`, `LastSuccessStartedAt`, `Trigger` |
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobTrigger.cs` | create | `Startup`, `Timer`, `Manual` + kody |
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobRunStatus.cs` | create | `Running`, `Succeeded`, `Failed`, `Abandoned` + kody |
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/IJobRunner.cs` | create | `TryRunNowAsync(key, ct)` → `JobRunOutcome` |
| `api/src/Shared/StoneFlow.Shared.Application/Scheduling/GetJobStatuses.cs` | create | `GetJobStatusesQuery`, `JobStatusDto`, `JobRunDto`, handler, port `IJobStatusReader` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/SchedulingDbContext.cs` | create | schemat `scheduling`, encje `JobState`, `JobRun` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/Migrations/*_InitialScheduling*.cs` | create | migracja generowana `dotnet ef` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/SchedulingOptions.cs` | create | `Enabled`, `TimerInterval` + walidator |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobRegistry.cs` | create | singleton z definicjami; sprawdzenie unikalności kluczy |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobLeaseStore.cs` | create | SQL: przejęcie, przedłużenie, zakończenie warunkowe, odczyt stanu |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobExecutor.cs` | create | jeden przebieg: zakres DI, przedłużanie, limit czasu, klasyfikacja wyniku |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobScheduler.cs` | create | `CheckDueAsync(trigger, ct)` — jedno sprawdzenie należności; implementuje `IJobRunner` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobSchedulerService.cs` | create | `BackgroundService`: start + `PeriodicTimer` z `TimeProvider` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobStatusReader.cs` | create | implementacja portu odczytu |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/SchedulingRegistration.cs` | create | `AddScheduling()` (wołane z `AddSharedInfrastructure`) i `AddScheduledJob<TJob>(JobDefinition)` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/DependencyInjection.cs` | modify | `AddScheduling()`, `AddHandlersFromAssembly(Shared.Application)` |
| `api/src/StoneFlow.Api/Scheduling/JobsEndpoints.cs` | create | `GET /api/jobs` przez `IDispatcher` |
| `api/src/StoneFlow.Api/Program.cs` | modify | `app.MapJobsEndpoints()` |
| `api/tests/StoneFlow.Shared.Tests/Scheduling/JobScheduleTests.cs` | create | terminy w Europe/Warsaw |
| `api/tests/StoneFlow.Shared.Tests/Scheduling/JobDueTests.cs` | create | należność, pierwszy termin, odstęp |
| `api/tests/StoneFlow.Shared.Tests/Scheduling/JobRegistryTests.cs` | create | duplikat klucza, pusta rejestracja, walidacja opcji |
| `api/tests/StoneFlow.Api.IntegrationTests/Scheduling/SchedulerTests.cs` | create | scenariusze SQL |
| `api/tests/StoneFlow.Api.IntegrationTests/Scheduling/TestJobs.cs` | create | sterowalne zadanie testowe |
| `api/tests/StoneFlow.Api.IntegrationTests/Scheduling/JobsEndpointTests.cs` | create | `401` / `200` |
| `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/TestSupport.cs` | modify | `MigrateAsync` migruje też `SchedulingDbContext` [R5] |
| `api/tests/StoneFlow.Architecture.Tests/DbContextSchemaTests.cs` | modify | osobny test `SchedulingDbContext` → `scheduling` |
| `deploy/scripts/contexts.sh` | modify | `api/src/Shared/StoneFlow.Shared.Infrastructure|SchedulingDbContext` na końcu |
| `deploy/scripts/test-migrate.sh` | modify | 6 tabel historii; `scheduling` na liście schematów niezmigrowanych po błędzie [R5] |
| `docs/wdrozenia.md` | modify | harmonogram w panelu, `GET /api/jobs`, D-08 [R4][R7] |
| `.sdd/context/conventions.md` | modify | nowe zasady Shared/Scheduling |

## 4. Implementation steps

1. **Kontrakt (Shared.Application).** Utworzyć typy z sekcji 3. Odstępstwo od specyfikacji
   „Types crossing a boundary”, bez zmiany zachowania: klucz, harmonogram i `MaxDuration` nie są
   właściwościami `IScheduledJob` (usługa scoped), tylko `JobDefinition` przekazywaną przy rejestracji
   `AddScheduledJob<TJob>(definition)`. Rejestr zna definicje bez tworzenia zakresu i bez bazy. Klucz:
   1–100 znaków `[a-z0-9-]`.
   - `IJobRunner.TryRunNowAsync(string jobKey, CancellationToken ct)` — zawsze `JobTrigger.Manual` [R1].
     Moduły nie mogą udawać wyzwolenia harmonogramu; specyfikacyjne `TryRunAsync(key, trigger, ct)`
     zawęża się do jedynego dozwolonego dla modułów źródła.
   - `JobRunOutcome`: `Completed(JobRunStatus)`, `AlreadyRunning`, `UnknownJob`.
2. **Terminy (Shared.Application).** `WarsawTimeZone.Zone`: `TryFindSystemTimeZoneById("Europe/Warsaw")`,
   rezerwa `Central European Standard Time` (jak `Orders.Application/WarsawTime.cs`, którego nie
   przenosimy — „Existing names and structure are not migrated”) [R6]. `JobSchedule`:
   - `LatestSlotAtOrBefore(now)`: lokalna data `now` w strefie; kandydat = data + godzina; jeśli
     kandydat > now, data − 1 dzień;
   - `FirstSlotAtOrAfter(t)`: symetrycznie;
   - zamiana lokalnej godziny na UTC: godzina nieistniejąca (przerwa wiosenna) → pierwsza istniejąca
     chwila po przerwie (lokalnie + przesunięcie przed zmianą); godzina niejednoznaczna (jesień) →
     pierwsze wystąpienie (większe przesunięcie).
3. **Reguła należności (Shared.Application, `JobDue`).** Czysta funkcja nad stanem (`RegisteredAt`,
   `LastSuccessStartedAt`, `NextAttemptAt`, `LeaseExpiresAt`) i `now`, zgodnie z „Należność zadania”
   pkt 1–3. `NextBackoff(consecutiveFailuresAfter, finishedAt)`: 1 → +15 min, ≥2 → +1 h.
   `TruncateError(Exception)`: `"{Type}: {Message}"`, max 1000 znaków, bez stosu.
4. **Stan i migracja (Shared.Infrastructure).** `SchedulingDbContext` (`Schema = "scheduling"`),
   rejestrowany `AddDbContext` z `UseModuleSqlServer<SchedulingDbContext>(connectionString, Schema)`
   (wariant z connection stringiem, **nie** `ModuleConnection`), tabele i indeks wg „Storage”. Kody
   statusów i źródeł jako `nvarchar(20)` z konwerterem. Wygenerować `InitialScheduling`:
   `dotnet ef migrations add InitialScheduling -p api/src/Shared/StoneFlow.Shared.Infrastructure
   -s api/src/StoneFlow.Api -c SchedulingDbContext -o Scheduling/Migrations`.
5. **Magazyn dzierżawy (`JobLeaseStore`).** Operacje SQL przez `ExecuteUpdateAsync` / zapisy
   w transakcji kontekstu (`Database.BeginTransactionAsync` na **własnym** kontekście — zakaz
   z `conventions.md` dotyczy kontekstów modułów finansowych na `ModuleConnection`; odnotować to
   w `conventions.md`):
   - `EnsureJobAsync(key, now)` — wstawienie wiersza, gdy go brak; `DbUpdateException` z naruszeniem PK
     (SQL 2627/2601) oznacza, że wiersz już jest;
   - `TryAcquireAsync(key, runId, trigger, dueAt, now, leaseUntil)` — w jednej transakcji: warunkowy
     `UPDATE … WHERE JobKey = @k AND (LeaseRunId IS NULL OR LeaseExpiresAtUtc < @now)` z
     `OUTPUT deleted.LeaseRunId` (surowy SQL parametryzowany przez `SqlQuery`, bo potrzebny poprzedni
     właściciel); gdy 0 wierszy → `null`; gdy poprzedni właściciel ≠ null → `UPDATE JobRuns SET
     Status='abandoned' … WHERE Id=@prev AND Status='running'`; wstawienie przebiegu `running`;
   - `TryRenewAsync(key, runId, now, leaseUntil)` → bool (warunek `LeaseRunId = @runId`);
   - `CompleteAsync(key, runId, JobCompletion)` — w transakcji: warunkowa aktualizacja `Jobs`
     (`LeaseRunId = @runId`) ze zmianami wg rodzaju zakończenia; gdy 0 wierszy → aktualizacja przebiegu
     tylko `WHERE Status='running'` na `failed` „Utracono dzierżawę” i wynik `LeaseLost`;
   - `MarkFailedKeepLeaseAsync(key, runId, …)` — dla zadania, które nie skończyło się po anulowaniu
     (krok 6) [R3].
6. **Przebieg (`JobExecutor`).** Dla przejętej dzierżawy:
   - nowy zakres DI (`IServiceScopeFactory`), `scope.ServiceProvider.GetRequiredKeyedService<IScheduledJob>(key)`
     (rejestracja keyed po kluczu w `AddScheduledJob`);
   - `CancellationTokenSource` połączony z tokenem wywołującego; `CancelAfter(MaxDuration)` przez
     `TimeProvider`;
   - pętla przedłużania co `MaxDuration / 3` (`Task.Delay(…, timeProvider, ct)`), równoległa do zadania
     w tym samym `async` (bez `Task.Run`); zapisy przedłużania przez osobny zakres runnera (nie
     współdzielą `DbContext` z zakończeniem — brak równoległych zapytań na jednym kontekście);
   - utrata dzierżawy (0 wierszy) albo dwa kolejne błędy przedłużenia → anulowanie tokenu zadania,
     koniec przedłużania; klasyfikacja zakończenia: `failed` „Utracono dzierżawę” (lub „Błąd bazy przy
     przedłużaniu dzierżawy”) z licznikiem i odstępem — przez warunkowe `CompleteAsync`; gdy dzierżawa
     nadal wskazuje własny przebieg (dwa błędy bazy), zapis się uda i odstęp obowiązuje (niejasność
     z przeglądu);
   - klasyfikacja wyniku zadania:
     - zakończone bez wyjątku → `Succeeded`;
     - `OperationCanceledException` przy przekroczonym `MaxDuration` → `Failed` „Przekroczono
       maksymalny czas przebiegu”, licznik + odstęp (pętla) / bez (ręczny);
     - anulowanie tokenu wywołującego (zamykanie aplikacji, `RequestAborted` w SF-13) → `Failed`
       „Przerwano (zamykanie aplikacji lub anulowanie wywołania)”, bez licznika i odstępu [R2];
     - inny wyjątek → `Failed` z `TruncateError`, licznik + odstęp (pętla) / bez (ręczny);
   - gdy po anulowaniu zadanie nie kończy się w 2 min zapasu: `MarkFailedKeepLeaseAsync` zapisuje
     przebieg `failed` „Zadanie nie zakończyło się po anulowaniu”, zwiększa licznik i ustawia odstęp,
     **nie zwalnia** dzierżawy (zadanie może jeszcze działać); przedłużanie ustaje, dzierżawa wygasa
     sama. Kolejne przejęcie nie oznaczy go jako `abandoned` (status już nie `running`), a odstęp
     obowiązuje — zawieszające się zadanie nie rusza od nowa po każdym wygaśnięciu [R3]. Gdy zadanie
     skończy się później, jego `CompleteAsync` jest pomijane (wynik już zapisany; warunek
     `Status='running'` dla przebiegu i odrębna flaga w executorze);
   - zapis zakończenia z `CancellationToken.None` i krótkim limitem (10 s), żeby wynik trafił do bazy
     także przy zamykaniu;
   - log: początek/koniec przebiegu (klucz, Id, wynik), pełny wyjątek na poziomie Error.
7. **Sprawdzenie i ręczne uruchomienie (`JobScheduler`).**
   - `CheckDueAsync(JobTrigger trigger, CancellationToken ct)` — publiczna w Infrastructure, jedyne
     wejście dla pętli i testów integracyjnych [R1]: gdy rejestr pusty → koniec bez bazy; dla każdej
     definicji po kluczu: `EnsureJobAsync`, odczyt stanu, `JobDue.IsDue`, `TryAcquireAsync`,
     `JobExecutor`. Błąd bazy przy jednym zadaniu → log, kolejne zadania nadal sprawdzane.
   - `TryRunNowAsync(key, ct)` (`IJobRunner`) — nieznany klucz → `UnknownJob`; `EnsureJobAsync`,
     `TryAcquireAsync` bez sprawdzania należności → `AlreadyRunning` albo przebieg `Manual`.
     Wywołanie czeka na koniec przebiegu. Uruchomienie w tle („Synchronizacja trwa”) to decyzja SF-13
     (może wywołać runner z własnej pracy w tle); zapisane w Ryzykach [R2].
   - Każde sprawdzenie i przebieg w nowym zakresie runnera; zadanie w osobnym zakresie. Zadanie, które
     potrzebuje wielu jednostek pracy (SF-13), tworzy dodatkowe zakresy sam przez `IServiceScopeFactory`
     — zapis w `conventions.md`.
8. **Pętla (`JobSchedulerService : BackgroundService`).** `StartAsync`: walidacja rejestru (duplikat
   klucza → wyjątek, host nie startuje) **niezależnie od `Enabled`** (niejasność z przeglądu), potem
   `base.StartAsync`. `ExecuteAsync`: gdy `!Enabled` → return; `await Task.Yield()`;
   `CheckDueAsync(Startup)`; `PeriodicTimer(TimerInterval, timeProvider)`; w pętli
   `CheckDueAsync(Timer)`. Każdy wyjątek poza anulowaniem jest logowany, pętla trwa.
9. **Opcje.** `SchedulingOptions` (`Scheduling:Enabled` = true, `Scheduling:TimerInterval` = 5 min),
   walidator: interwał 1–15 min; `ValidateOnStart`.
10. **Odczyt stanu.** `JobStatusReader.ReadAsync(definitions, now)`: jeden odczyt `Jobs` po kluczach
    zarejestrowanych + ostatni przebieg każdego (`JobRuns` po `JobKey`, `StartedAtUtc desc`, `Take(1)`
    na klucz). `isRunning` = `LeaseExpiresAtUtc > now`. Ostatni przebieg ze statusem `running` przy
    `isRunning = false` jest pokazywany jako `abandoned` (dzierżawa wygasła, oznaczenie w bazie przy
    następnym przejęciu) — niejasność „running ~24 h” z przeglądu. Handler `GetJobStatusesHandler`
    w Shared.Application; zadania bez wiersza → DTO bez przebiegu. Pusty rejestr → pusta lista bez
    dostępu do bazy.
11. **Rejestracja.** `AddScheduling()` w `AddSharedInfrastructure`: opcje, `SchedulingDbContext`,
    `JobRegistry` (singleton z `IEnumerable<JobDefinition>`), `JobLeaseStore` (scoped), `JobExecutor`,
    `JobScheduler` (singleton; `IJobRunner` → ta sama instancja), `IJobStatusReader`, hosted service.
    `AddHandlersFromAssembly(Shared.Application.AssemblyReference.Assembly)`.
    `AddScheduledJob<TJob>(JobDefinition)` rejestruje `JobDefinition` jako singleton i `TJob` jako
    keyed scoped `IScheduledJob`.
12. **Endpoint.** `api/src/StoneFlow.Api/Scheduling/JobsEndpoints.cs`: `MapGet("/api/jobs")`, domyślna
    polityka (sesja), `IDispatcher.QueryAsync(new GetJobStatusesQuery())` → `TypedResults.Ok`,
    `WithTags("Jobs")`, OpenAPI. `Program.cs`: `app.MapJobsEndpoints()`.
13. **Migracje w skryptach i testach [R5].** `contexts.sh`: dopisać na końcu. `test-migrate.sh`:
    5 → 6 tabel historii (linie z `history_tables`), lista schematów po błędzie: dodać `'scheduling'`.
    `TestDatabase.MigrateAsync`: dodać `SchedulingDbContext`.
14. **Testy** wg sekcji 8. Zadanie testowe sterowane obiektem współdzielonym (singleton w teście):
    licznik uruchomień, tryb (sukces, wyjątek, czekanie na sygnał, ignorowanie anulowania).
15. **Dokumentacja.**
    - `docs/wdrozenia.md` sekcja 4 lub nowa „Harmonogram zadań”: zadanie w panelu „Scheduled tasks”:
      GET `https://<adres-api>/health` co 15 min; drugi niezależny budzik to monitor Better Stack
      (sekcja 9, co 3 min); odczyt `GET /api/jobs` po zalogowaniu; brak sekretów. Sekcja 10: dowód
      D-08 pkt 2 (restart, uśpienie) wymaga zarejestrowanego zadania — do zebrania przy wdrożeniu
      SF-13 lub SF-16 [R4]; „bezpieczne uwierzytelnienie wywołań” z D-08 pkt 2 nie dotyczy tego
      projektu, bo wywołanie nie niesie uprawnień — do odnotowania przez właściciela przy zamykaniu
      D-08 [R7].
    - `.sdd/context/conventions.md`: w „Structure” — Shared ma też harmonogram zadań (`Scheduling/`)
      z własnym schematem `scheduling`; w „CQRS”/„Persistence” — `SchedulingDbContext` ma własne
      połączenie i może używać transakcji własnego kontekstu; rejestracja zadań
      `AddScheduledJob<TJob>(JobDefinition)` w `DependencyInjection.cs` modułu; zadanie respektuje
      token, jest idempotentne i tworzy własne zakresy dla wielu jednostek pracy; endpoint `/api/jobs`
      w hoście.

## 5. Data & contracts

- Storage: schemat `scheduling`, tabele `Jobs`, `JobRuns` wg specyfikacji („Storage”); indeks
  `IX_JobRuns_JobKey_StartedAtUtc` (`JobKey`, `StartedAtUtc` desc); FK `JobRuns.JobKey → Jobs.JobKey`.
  Migracja `InitialScheduling`, addytywna.
- API: `GET /api/jobs` → `200`:
  ```
  [{ "key": "…", "schedule": "daily 23:00 Europe/Warsaw", "isRunning": false,
     "lastRun": { "status": "failed", "startedAtUtc": "…", "finishedAtUtc": "…", "error": "…" } | null,
     "lastSuccessStartedAtUtc": "…" | null, "consecutiveFailures": 0, "nextAttemptAtUtc": "…" | null }]
  ```
  `401` bez sesji (Problem Details z istniejącego potoku).
- Typy współdzielone: `IScheduledJob`, `JobDefinition`, `JobSchedule`, `JobRunContext`, `JobTrigger`,
  `JobRunStatus`, `IJobRunner`, `JobRunOutcome`, `GetJobStatusesQuery`, `JobStatusDto`, `JobRunDto`,
  `IJobStatusReader`.

## 6. Edge cases & null safety

| Przypadek ze specyfikacji | Gdzie |
|---|---|
| Uśpienie przez termin, kilka dni przerwy | `JobDue.IsDue` + `CheckDueAsync(Startup)` przy starcie |
| Nowe zadanie przed pierwszym terminem | `JobDue` (`FirstSlotAtOrAfter(RegisteredAt)`) |
| Restart w trakcie przebiegu | `TryAcquireAsync` (oznaczenie `abandoned`, bez odstępu) |
| Nakładanie procesów | warunkowy `UPDATE` w `TryAcquireAsync` |
| Spóźniony przebieg | warunek `LeaseRunId` w `CompleteAsync`, `LeaseLost` |
| Przebieg dłuższy niż takt | jedna pętla + dzierżawa |
| Przekroczenie czasu, zawieszone zadanie | `JobExecutor` (`CancelAfter`, zapas, `MarkFailedKeepLeaseAsync`) |
| Błąd za błędem | `JobDue.NextBackoff` w `CompleteAsync` |
| Sukces ręczny przed terminem | `JobDue` porównuje start sukcesu z ostatnim terminem |
| Ręczne w trakcie | `TryRunNowAsync` → `AlreadyRunning` |
| Zmiana czasu | `JobSchedule` (krok 2) |
| Brak bazy | `CheckDueAsync` łapie i loguje; pętla trwa |
| Brak zadań | `CheckDueAsync` / `JobStatusReader` kończą bez bazy |
| Zamykanie aplikacji | klasyfikacja „anulowanie wywołującego” + zapis z `CancellationToken.None` |

Null: `LastSuccessStartedAtUtc`, `NextAttemptAtUtc`, `LeaseRunId`, `LeaseExpiresAtUtc`,
`FinishedAtUtc`, `Error`, `lastRun` są nullowalne w typach; brak wiersza `Jobs` obsługuje
`EnsureJobAsync` przed odczytem.

## 7. Invariants

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 3. API-enforced access | `GET /api/jobs` bez `AllowAnonymous` (domyślna polityka) | `JobsEndpointTests.Requires_session` (401); istniejący `EndpointSecurityTests` (lista anonimowych bez zmian) |
| 4. Retries never duplicate | `JobLeaseStore.TryAcquireAsync`/`CompleteAsync` (fencing), `JobDue` (jedno nadrobienie) | `SchedulerTests`: `Concurrent_checks_run_once`, `Late_completion_does_not_touch_new_lease`, `Missed_slots_run_once` |
| 6. Secrets stay server-side | brak sekretu; `JobDue.TruncateError` bez stosu | `JobDueTests.Error_is_truncated_without_stack` |
| 7. Module and layer boundaries | kontrakt w Shared.Application bez EF/HTTP; własny schemat | `LayerDependencyTests.Shared_application_does_not_depend_on_infrastructure_http_or_ef` (istniejący), nowy `DbContextSchemaTests.Scheduling_context_uses_only_its_schema` |
| 8. No identifying data in docs | `docs/wdrozenia.md` z `<adres-api>` | przegląd dokumentacji |

1, 2, 5: nie dotyczą (brak danych finansowych).

## 8. Tests

| Test | Type | What it proves |
|---|---|---|
| `JobScheduleTests` — ostatni/pierwszy termin zimą, latem, 29.03.2026 i 25.10.2026, godzina 02:30 w przerwie i niejednoznaczna, przejście przez północ UTC | unit | terminy w Europe/Warsaw |
| `JobDueTests` — brak sukcesu przed/po pierwszym terminie, sukces przed/po terminie, odstęp aktywny/minął, ważna dzierżawa, `NextBackoff` 1 i 2+, `TruncateError` | unit | reguła należności, odstępy, opis błędu |
| `JobRegistryTests` — duplikat klucza, zły klucz, walidacja `TimerInterval` | unit | start zatrzymany przy błędnej konfiguracji |
| `Due_job_runs_after_restart` — host A rejestruje zadanie, zegar po terminie, host A zamknięty bez sprawdzenia; host B na tej samej bazie: `CheckDueAsync(Startup)` → 1 przebieg `succeeded` | integration | AC restart |
| `Missed_slots_run_once` — zegar +3 dni bez sprawdzeń, dwa sprawdzenia → 1 przebieg, `DueAt` = ostatni termin | integration | AC uśpienie, „dokładnie raz” |
| `Not_due_before_first_slot` / `Not_rerun_in_same_period` | integration | AC pierwszy termin i jeden przebieg na okres |
| `Concurrent_checks_run_once` — dwa hosty na tej samej bazie, zadanie czeka na sygnał, równoczesne `CheckDueAsync` i `TryRunNowAsync` → 1 przebieg, `AlreadyRunning` | integration | AC równoległość |
| `Expired_lease_is_abandoned_and_rerun` — przebieg wstrzymany, zegar > wygaśnięcie, drugi host przejmuje → stary `abandoned`, nowy biegnie, brak odstępu | integration | AC porzucenie |
| `Late_completion_does_not_touch_new_lease` — jak wyżej, potem stary przebieg kończy się → `Jobs` bez zmian, stary `failed` „Utracono dzierżawę”, token starego anulowany przy próbie przedłużenia | integration | AC fencing |
| `Failure_sets_backoff_then_success_resets` — wyjątek → `failed`, `NextAttemptAt` +15 min; przed odstępem brak przebiegu; drugi wyjątek → +1 h; sukces → licznik 0 | integration | AC błąd i odstęp |
| `Manual_failure_has_no_backoff` | integration | AC ręczny |
| `Timeout_cancels_job` — `MaxDuration` krótki, zegar przesunięty → token anulowany, `failed` „Przekroczono…”; wariant zadania ignorującego anulowanie → `failed` z odstępem, dzierżawa zostaje, brak ponownego startu przed odstępem | integration | AC limit czasu, [R3] |
| `Startup_check_runs_due_job` — `Scheduling:Enabled=true`, zadanie należne → przebieg `startup` po starcie hosta | integration | wyzwolenie przy starcie |
| `No_jobs_no_database` — istniejące testy bez bazy przechodzą; host z nieosiągalną bazą i bez zadań startuje, brak logów błędu harmonogramu | integration | AC bez zadań |
| `JobsEndpointTests` — `401` bez sesji; `200` z sesją: zadanie bez przebiegu, po błędzie, w toku (`isRunning`) | integration | AC stan zadań |
| `Scheduling_context_uses_only_its_schema` | architecture | schemat `scheduling`, historia migracji w schemacie |
| istniejące `LayerDependencyTests`, `ProjectReferenceTests` | architecture | granice Shared |
| `deploy/scripts/test-migrate.sh` (CI) | migration | 6 kontekstów, kolejność, błąd w środku |

Testy integracyjne: `SqlServerCollection`, `Scheduling:Enabled=false` (poza testem startu),
`FakeTimeProvider` z fabryki, zadanie testowe dodane przez `ConfigureTestServices` →
`AddScheduledJob<TestJob>(…)`, wywołania przez `factory.Services.GetRequiredService<JobScheduler>()`.

## 9. Verification

- `dotnet tool restore`
- `dotnet restore stone-flow.slnx --locked-mode` (po dodaniu zależności — aktualizacja lockfile
  bez `--locked-mode`, jeśli potrzebna; Shared.Infrastructure ma już EF Core SqlServer i ASP.NET Core)
- `dotnet format stone-flow.slnx --verify-no-changes --no-restore`
- `dotnet build stone-flow.slnx -c Release --no-restore`
- `dotnet test --solution stone-flow.slnx -c Release --no-build` (integracyjne wymagają Dockera lub
  `STONEFLOW_TEST_SQLSERVER`)
- `deploy/scripts/test-migrate.sh` — w CI (job migracji).
- Ręcznie po wdrożeniu (poza SF-11): zadanie w panelu hostingu; dowód D-08 pkt 2 przy SF-13/SF-16.

## 10. Risks

- **[R2] SF-13 „Synchronizuj teraz”:** `TryRunNowAsync` czeka na koniec przebiegu. SF-13 zdecyduje,
  czy wywołuje go w żądaniu, czy w swojej pracy w tle; kontrakt pozwala na oba warianty, a dzierżawa
  i `isRunning` pokazują „Synchronizacja trwa” niezależnie od sposobu wywołania.
- **[R4] Dowód D-08 pkt 2** nie może powstać w SF-11 (brak zadania produkcyjnego). Przesunięty do
  wdrożenia SF-13 lub SF-16; zapisany w `docs/wdrozenia.md` sekcja 10.
- **[R7] D-08 „bezpieczne uwierzytelnienie wywołań”:** projekt usuwa potrzebę uwierzytelnienia
  wywołania; właściciel powinien to odnotować przy zamykaniu D-08 (PRD sekcja 12 nie jest zmieniana
  w SF-11).
- **Limit bezczynności IIS** niepotwierdzony — przy limicie < 15 min Better Stack (3 min) i tak
  podtrzymuje proces; bez obu budzików zadanie ruszy dopiero przy następnym żądaniu.
- **`OUTPUT deleted.*` przez surowy SQL** — jedyne miejsce z ręcznym SQL; parametryzowane, pokryte
  testami integracyjnymi.
- **Czas testów integracyjnych** — CI ma limit 45 min (commit `ce1e921`); testy harmonogramu używają
  `FakeTimeProvider` i sygnałów zamiast czekania na zegar.
