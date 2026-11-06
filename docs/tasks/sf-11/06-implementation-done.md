# Implementation done — SF-11

## Summary

Dostarczono trwały harmonogram zadań backendu:
- kontrakt w `StoneFlow.Shared.Application/Scheduling`;
- implementację w `StoneFlow.Shared.Infrastructure/Scheduling`: `SchedulingDbContext` (schemat `scheduling`, migracja `InitialScheduling`), dzierżawę z fencingiem, przebieg z limitem czasu i przedłużaniem, sprawdzenie należności, `BackgroundService` z wyzwoleniem przy starcie i co 5 min, uruchomienie na żądanie `IJobRunner`;
- endpoint `GET /api/jobs` w hoście;
- testy jednostkowe, integracyjne i architektury;
- zmiany w skryptach migracji, `docs/wdrozenia.md` i `conventions.md`.

Po przeglądzie kodu (`05-code-review.md`, PASS WITH ISSUES) poprawiono K-1, K-2, K-3, K-4, K-6 i K-7 oraz dodano testy T-1, T-2, T-4 i test dla K-2. Ponownego przeglądu kodu nie wykonano.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Rejestracja zadania (klucz, harmonogram dzienny Europe/Warsaw, `MaxDuration`), unikalność kluczy | Implemented | `AddScheduledJob<TJob>(JobDefinition)`, `JobRegistry` |
| Brak zadań → brak dostępu do bazy | Implemented | `JobScheduler.CheckDueAsync`, `JobStatusReader` |
| Wyzwolenie przy starcie i timer (`Scheduling:TimerInterval`), `Scheduling:Enabled` | Implemented | `JobSchedulerService` |
| Budzenie procesu przez `GET /health` z harmonogramu hostingu | Implemented | dokumentacja `docs/wdrozenia.md` sekcja 10; bez zmian kodu `/health` |
| Należność: pierwszy termin, ostatni termin, jedno nadrobienie, odstęp, dzierżawa | Implemented | `JobDue.IsDue` |
| Dzierżawa: przejęcie, `abandoned`, przedłużanie, utrata, zakończenie warunkowe | Implemented | `JobLeaseStore`, `JobExecutor` |
| Przekroczenie czasu i zadanie ignorujące anulowanie | Implemented | `JobExecutor`, `MarkHungAsync` |
| Odstępy 15 min / 1 h, sukces zeruje licznik; ręczny przebieg bez odstępu | Implemented | `JobLeaseStore.CompleteAsync` |
| Zamykanie aplikacji: `failed` bez licznika i odstępu | Implemented | `JobExecutor.Classify` |
| Opis błędu bez stosu, max 1000 znaków | Implemented | `JobDue.DescribeError` |
| `IJobRunner` (uruchomienie na żądanie, `AlreadyRunning`, `UnknownJob`) | Implemented | `JobScheduler.TryRunNowAsync` |
| `GET /api/jobs` przez `IDispatcher`, `isRunning` z dzierżawy | Implemented | `JobsEndpoints`, `GetJobStatusesHandler`, `JobStatusReader` |
| Migracja `scheduling` w `contexts.sh` | Implemented | |
| Test architektury `SchedulingDbContext` | Implemented | `DbContextSchemaTests.Scheduling_context_uses_only_its_schema` |
| `docs/wdrozenia.md` i `conventions.md` | Implemented | |

## Files

### Created

- `api/src/Shared/StoneFlow.Shared.Application/Scheduling/IScheduledJob.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobDefinition.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobSchedule.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Scheduling/WarsawTimeZone.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobDue.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobTypes.cs`
- `api/src/Shared/StoneFlow.Shared.Application/Scheduling/GetJobStatuses.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/SchedulingDbContext.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/SchedulingOptions.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobRegistry.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobLeaseStore.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobExecutor.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobScheduler.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobSchedulerService.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobStatusReader.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/SchedulingRegistration.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/Migrations/20261106074754_InitialScheduling.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/Migrations/20261106074754_InitialScheduling.Designer.cs`
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/Migrations/SchedulingDbContextModelSnapshot.cs`
- `api/src/StoneFlow.Api/Scheduling/JobsEndpoints.cs`
- `api/tests/StoneFlow.Shared.Tests/Scheduling/JobScheduleTests.cs`
- `api/tests/StoneFlow.Shared.Tests/Scheduling/JobDueTests.cs`
- `api/tests/StoneFlow.Shared.Tests/Scheduling/JobRegistryTests.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Scheduling/SchedulerHost.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Scheduling/SchedulerTests.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Scheduling/JobsEndpointTests.cs`

### Modified

- `api/src/Shared/StoneFlow.Shared.Infrastructure/DependencyInjection.cs`
- `api/src/StoneFlow.Api/Program.cs`
- `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/TestSupport.cs`
- `api/tests/StoneFlow.Architecture.Tests/DbContextSchemaTests.cs`
- `deploy/scripts/contexts.sh`
- `deploy/scripts/test-migrate.sh`
- `docs/wdrozenia.md`
- `.sdd/context/conventions.md`

## Planned Units

| Item from the plan | Status |
|---|---|
| Kontrakt Shared.Application/Scheduling | Exists |
| `WarsawTimeZone`, `JobSchedule`, `JobDue` | Exists |
| `SchedulingDbContext` + migracja `InitialScheduling` | Exists |
| `JobLeaseStore` | Exists |
| `JobExecutor` | Exists |
| `JobScheduler` (`CheckDueAsync`, `IJobRunner`) | Exists |
| `JobSchedulerService` | Exists |
| `SchedulingOptions` + walidator | Exists |
| `JobStatusReader`, `GetJobStatusesHandler` | Exists |
| `AddScheduling`, `AddScheduledJob` | Exists |
| `GET /api/jobs` | Exists |
| `contexts.sh`, `test-migrate.sh`, `TestDatabase.MigrateAsync` | Exists |
| `docs/wdrozenia.md`, `conventions.md` | Exists |

## Database

SQL Server: `SchedulingDbContext`, migracja `20261106074754_InitialScheduling`. Tworzy schemat `scheduling` z tabelami `Jobs` i `JobRuns`, kluczem obcym `JobRuns.JobKey → Jobs.JobKey` oraz indeksem `IX_JobRuns_JobKey_StartedAtUtc`.

## Tests

| Test from the plan | Status | Run result |
|---|---|---|
| `JobScheduleTests` | Exists | Passed (Shared.Tests 45/45) |
| `JobDueTests` | Exists | Passed |
| `JobRegistryTests` | Exists | Passed |
| `Due_job_runs_after_restart` | Exists | Passed (Scheduling 18/18, LocalDB) |
| `Missed_slots_run_once` | Exists | Passed |
| `Not_due_before_first_slot` / `Not_rerun_in_same_period` | Exists jako `New_job_waits_for_first_slot_and_runs_once_per_period` | Passed |
| `Concurrent_checks_run_once` | Exists jako `Concurrent_attempts_run_once` i `Simultaneous_checks_on_two_hosts_run_once_per_period` | Passed |
| `Expired_lease_is_abandoned_and_rerun` | Exists | Passed |
| `Late_completion_does_not_touch_new_lease` | Exists jako `Late_completion_does_not_touch_taken_over_lease` i `Lost_lease_cancels_running_job` | Passed |
| `Failure_sets_backoff_then_success_resets` | Exists | Passed |
| `Manual_failure_has_no_backoff` | Exists | Passed |
| `Timeout_cancels_job` (z wariantem zadania ignorującego anulowanie) | Exists jako `Timeout_cancels_job_and_counts_as_failure` i `Hung_job_is_failed_with_backoff_and_keeps_lease` | Passed |
| `Startup_check_runs_due_job` | Exists | Passed |
| `No_jobs_no_database` | Exists jako `JobsEndpointTests.Without_registered_jobs_returns_empty_list_without_database` | Passed |
| `JobsEndpointTests` (401, 200) | Exists | Passed |
| `Scheduling_context_uses_only_its_schema` | Exists | Passed (Architecture 56/56) |
| `deploy/scripts/test-migrate.sh` | Exists (zmieniony) | Not run |

## Verification

| Command | Outcome |
|---|---|
| `dotnet build stone-flow.slnx` (Debug) | Passed |
| `dotnet build stone-flow.slnx -c Release --no-restore` | Not run |
| `dotnet restore stone-flow.slnx --locked-mode` | Not run |
| `dotnet format stone-flow.slnx --verify-no-changes` | Passed (exit 0) |
| `dotnet test api/tests/StoneFlow.Shared.Tests` | Passed (45/45) |
| `dotnet test api/tests/StoneFlow.Architecture.Tests` | Passed (56/56) |
| `dotnet test api/tests/StoneFlow.Api.IntegrationTests -- --filter-namespace "*Scheduling*"` (LocalDB) | Passed (18/18) |
| `dotnet test --solution stone-flow.slnx` (pełny zestaw) | Not run — przerwany przez system z powodu braku pamięci |
| `deploy/scripts/test-migrate.sh` | Not run |

## Deviations

- Klucz, harmonogram i `MaxDuration` są w `JobDefinition` przekazywanej przy rejestracji, a nie we właściwościach `IScheduledJob`.
- `IJobRunner.TryRunAsync(key, trigger, ct)` ze specyfikacji zawężono do `TryRunNowAsync(key, ct)` (zawsze `Manual`). Sprawdzenie należności wywołuje się przez `JobScheduler.CheckDueAsync(trigger, ct)`.
- Typy kontraktu połączono w `JobTypes.cs`; zadanie testowe jest w `SchedulerHost.cs`, a nie w `TestJobs.cs`.
- Przejęcie dzierżawy to `ExecuteUpdate` z warunkiem na odczytany stan (`LeaseRunId`, `LastAttemptStartedAtUtc`), a nie surowy SQL z `OUTPUT`.
- `MarkFailedKeepLeaseAsync` z planu nazywa się `MarkHungAsync`; `ReadAsync(definitions, now)` ma postać `ReadAsync(ct)`.
- Duplikaty kluczy wykrywa konstruktor `JobRegistry`, a nie `StartAsync`.
- `docs/wdrozenia.md`: nowa sekcja 10 „Harmonogram zadań”, dawna sekcja 10 ma numer 11.

## Additional Implementation

Additional implementation not covered by the original documents:
- `JobStatusReader` pokazuje przebieg `running` bez ważnej dzierżawy jako `abandoned`. Wynika to z rozstrzygnięcia niejasności z `03-spec-review.md` w planie; w samej specyfikacji tego nie ma.
- `TryRunNowAsync` ponawia próbę przejęcia raz, gdy dzierżawę zwolniono między odczytem a przejęciem (poprawka K-7).

## Open Items

- Pełny zestaw testów (`dotnet test --solution stone-flow.slnx`), build Release i `deploy/scripts/test-migrate.sh` nie zostały uruchomione lokalnie; do potwierdzenia w CI.
- Ponowny przegląd kodu po poprawkach K-1–K-7 nie został wykonany.
- Nierozwiązane ustalenia z `05-code-review.md`:
  - K-5: zadanie, które przekroczyło `MaxDuration`, ale skończyło się w zapasie, jest zapisywane jako `succeeded`;
  - zapytania N+1 i odczyt całych encji w `JobStatusReader`;
  - krok 500 ms rzeczywistego czasu w `Waiting.AdvanceUntilAsync`;
  - T-3: brak przypadków `isRunning = true`, `lastRun = null` i mapowania `running` → `abandoned` w `JobsEndpointTests`;
  - T-5: brak testu dwóch kolejnych błędów bazy przy przedłużaniu.
- D-08 pkt 2: dowód z hostingu przy wdrożeniu SF-13 lub SF-16 (`docs/wdrozenia.md` sekcja „Otwarte do weryfikacji na środowiskach”).
