# Code review — SF-11

> Przegląd niescommitowanych zmian na gałęzi `sf-11-job-scheduler` (working tree, 2026-11-06) wobec
> `02-spec.md` (wersja 2), `03-spec-review.md` i `04-implementation-plan.md`. Przeczytane w całości
> wszystkie zmienione i nowe pliki z `git status`.

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Mechanizm jest zgodny ze specyfikacją i planem: dzierżawa z warunkowymi zapisami,
  zakończenie warunkowe względem `LeaseRunId`, `abandoned` bez odstępu, odstępy 15 min / 1 h, DST
  w `JobSchedule` i brak dostępu do bazy bez zadań. Żaden niezmiennik nie jest naruszony. Zostają dwa
  ważne problemy poprawności. Pierwszy: między odczytem stanu a przejęciem dzierżawy jest okno, w którym
  zadanie może przebiec drugi raz w tym samym okresie (kryterium „jeden przebieg”). Drugi: wyjątek
  anulowania zgłoszony przez samo zadanie (np. limit czasu `HttpClient` w SF-13) trafia do historii
  jako fikcyjny „Nieznany błąd zadania”. Do tego luki w testach ścieżek zamykania i zawieszenia.
- Risk: medium. Przegląd uwzględnia obu konsumentów: SF-13 (wyjątki HTTP, uwaga K-2) i SF-16 (krótkie
  zadanie, przy którym okno z uwagi K-1 jest realne).

## Project Invariant Violations

None.

Sprawdzenie każdego niezmiennika:

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Nie dotyczy | Mechanizm nie dotyka sprzedaży, wpłat ani wydatków. |
| 2. Backend decimal money | Nie dotyczy | Brak kwot. |
| 3. API-enforced access | OK | `api/src/StoneFlow.Api/Scheduling/JobsEndpoints.cs:15` bez `AllowAnonymous` (polityka domyślna); test `JobsEndpointTests.Requires_session` (`JobsEndpointTests.cs:15`). `/health` bez zmian. |
| 4. Retries never duplicate | OK (z uwagą K-1) | Idempotencja skutków należy do zadań (kontrakt `IScheduledJob.cs:7-12`). Mechanizm sam może w rzadkim oknie uruchomić zadanie drugi raz (K-1). To słabsza gwarancja harmonogramu, nie naruszenie niezmiennika, bo ten wymaga unikalności po stronie skutków. |
| 5. Financial audit trail | Nie dotyczy | Mechanizm nie zmienia danych finansowych. |
| 6. Secrets stay server-side | OK | Brak sekretu. `JobDue.DescribeError` (`JobDue.cs:52-56`) bez stosu, obcięte do 1000 znaków; pełny wyjątek tylko w logu (`JobExecutor.cs:124`). `docs/wdrozenia.md` ostrzega przed tokenem w URL. |
| 7. Module and layer boundaries | OK | Kontrakt w `Shared.Application/Scheduling` bez EF Core i HTTP; implementacja i schemat `scheduling` w `Shared.Infrastructure`; test `DbContextSchemaTests.Scheduling_context_uses_only_its_schema`. |
| 8. No identifying data in docs | OK | `docs/wdrozenia.md` używa `<adres-api>`. |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobExecutor.cs:123-125` — połknięty
   wyjątek (`conventions.md`: „Do not swallow cancellation or errors”). Szczegóły w K-2.
2. `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobStatusReader.cs:23-32` — osobne
   zapytanie o ostatni przebieg dla każdego zadania (N+1) i materializacja całych encji zamiast
   projekcji `Select` do DTO (`conventions.md` „Data access”: „No N+1 queries”, „project to the DTO
   before materialising”). Przy jednym–dwóch zadaniach koszt jest pomijalny, ale to jawna reguła.
3. `api/tests/StoneFlow.Api.IntegrationTests/Scheduling/SchedulerTests.cs:49` —
   `after.RunsAsync().GetAwaiter().GetResult()` w warunku oczekiwania. Synchroniczne czekanie na
   operację asynchroniczną, zakazane przez `conventions.md` („No `.Result`, `.Wait()`”). Kod testowy.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Rejestracja zadania z kluczem, `DailyAt`, `MaxDuration` (domyślnie 30 min) | Covered | `JobDefinition.cs`, `SchedulingRegistration.AddScheduledJob`; odstępstwo od kształtu `IScheduledJob` uzgodnione w planie (krok 1). |
| Unikalność kluczy przy starcie, bez bazy, niezależnie od `Enabled` | Covered | `JobRegistry.cs:13-17`, rozwiązywany przy konstrukcji `JobSchedulerService` → `JobScheduler`. Test tylko jednostkowy (`JobRegistryTests.Duplicate_keys_are_rejected`). |
| Brak zadań → wyzwolenie i `GET /api/jobs` bez bazy | Covered | `JobScheduler.cs:44` (pusta pętla), `JobStatusReader.cs:11-14`; test `Without_registered_jobs_returns_empty_list_without_database` na nieosiągalnej bazie. |
| Wiersz `Jobs` przy pierwszym sprawdzeniu, `RegisteredAtUtc` = teraz | Covered | `JobLeaseStore.EnsureJobAsync` z obsługą 2627/2601. |
| Sprawdzenie przy starcie i co `TimerInterval` (`PeriodicTimer` z `TimeProvider`) | Covered | `JobSchedulerService.cs:26-35`; test `Startup_check_runs_due_job`. |
| Błąd bazy przy sprawdzeniu logowany, pętla trwa | Covered | `JobScheduler.cs:55-58`, `JobSchedulerService.cs:53-56`. Bez testu. |
| `Scheduling:Enabled=false` wyłącza pętlę; walidacja interwału 1–15 min | Covered | `JobSchedulerService.cs:21-24`, `SchedulingOptionsValidator`. |
| `IJobRunner` — ręcznie, ta sama dzierżawa, `Completed`/`AlreadyRunning`/`UnknownJob`, bez odstępu przy błędzie | Covered | `JobScheduler.TryRunNowAsync`; test `Manual_run_ignores_schedule_and_its_failure_sets_no_backoff`. |
| Należność: pierwszy termin, ostatni termin, sukces, odstęp, dzierżawa | Covered | `JobDue.IsDue`; testy jednostkowe i integracyjne. |
| Nadrobienie jednym uruchomieniem, `DueAt` = ostatni termin | Covered | Test `Missed_slots_run_once`. |
| Przejęcie dzierżawy warunkowe, `abandoned` dla poprzedniego właściciela | Partial | Warunek przejęcia nie chroni przed ponownym przebiegiem w tym samym okresie (K-1). |
| Przedłużanie co `MaxDuration / 3`, utrata przy 0 wierszy lub 2 błędach bazy | Covered | `JobExecutor.RenewAsync`; test utraty (`Lost_lease_cancels_running_job`). Ścieżka dwóch błędów bazy bez testu. |
| Przekroczenie czasu: anulowanie, `failed`, odstęp; zawieszenie po zapasie | Covered | `JobExecutor.cs:43,55-68`; testy `Timeout_cancels_job_and_counts_as_failure`, `Hung_job_is_failed_with_backoff_and_keeps_lease` (z luką T-2). |
| Zakończenie warunkowe (sukces, błąd z pętli, błąd ręczny, zamykanie, utrata) | Partial | Zgodne z wyjątkiem K-2 (anulowanie zgłoszone przez zadanie). Zamykanie bez testu (T-1). |
| Opis błędu: typ + komunikat, 1000 znaków, bez stosu | Covered | `JobDue.DescribeError`; test jednostkowy. |
| `GET /api/jobs`: klucz, harmonogram, `isRunning`, ostatni przebieg, sukces, licznik, najbliższa próba; 401 bez sesji | Covered | `JobStatusReader`, `JobsEndpoints`; testy 401, pusta lista, stan po błędzie. Brak testu `isRunning = true` (T-3). |
| Schemat `scheduling`, tabele i indeks wg „Storage”, migracja addytywna | Covered | `InitialScheduling.cs` zgodna z tabelami ze specyfikacji. |
| `contexts.sh`, `test-migrate.sh`, `TestDatabase.MigrateAsync` | Covered | Dopisane; `test-migrate.sh` nie był uruchomiony w tym przeglądzie (tylko w CI). |
| DST: 23:00 zimą, latem, w dni zmiany; przerwa wiosenna, powtórzona godzina | Covered | `JobSchedule.SlotOn`; `JobScheduleTests` (29.03 i 25.10.2026, 02:30). Logika sprawdzona ręcznie: przerwa → 03:00 CEST, jesień → większe przesunięcie (pierwsze wystąpienie). |
| Strefa na Windows (rezerwa `Central European Standard Time`) | Covered | `WarsawTimeZone.cs`. |
| `docs/wdrozenia.md` i `conventions.md` | Covered | Sekcje 10 i 11 w `wdrozenia.md`; nowe akapity w `conventions.md`. |

## Plan Deviations

- Plan (sekcja 3) przewidywał osobne pliki `JobRunContext.cs`, `JobTrigger.cs`, `JobRunStatus.cs`,
  `IJobRunner.cs`. Typy są w jednym pliku
  `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobTypes.cs`. Plik `TestJobs.cs` nie
  istnieje; zadanie testowe jest w `api/tests/StoneFlow.Api.IntegrationTests/Scheduling/SchedulerHost.cs`.
- Plan (krok 5) przewidywał przejęcie surowym SQL z `OUTPUT deleted.LeaseRunId`. Implementacja
  (`JobLeaseStore.cs:67-76`) używa `ExecuteUpdateAsync` z warunkiem `LeaseRunId == previousRunId`,
  gdzie `previousRunId` pochodzi z wcześniejszego odczytu. Warunek jest ostrzejszy niż w specyfikacji
  (`LeaseRunId IS NULL OR LeaseExpiresAtUtc < teraz`).
- `MarkFailedKeepLeaseAsync` z planu nazywa się `MarkHungAsync` (`JobLeaseStore.cs:167`).
- Plan (krok 10): `JobStatusReader.ReadAsync(definitions, now)`. Implementacja: `ReadAsync(ct)`
  z definicjami z `JobRegistry` i czasem z `TimeProvider`.
- Plan (krok 8): walidacja rejestru w `StartAsync`. Implementacja wykonuje ją przez konstruktor
  `JobRegistry` przy rozwiązaniu usługi hostowanej. Skutek jest ten sam.
- Plan (sekcja 8) wymieniał testy endpointu „zadanie bez przebiegu” i „w toku (`isRunning`)”, a dla
  `Concurrent_checks_run_once` „równoczesne `CheckDueAsync` i `TryRunNowAsync`”. Tych przypadków
  nie ma albo są sekwencyjne (T-3, T-4).

## Null Safety Issues

None. Pola nullowalne (`LeaseRunId`, `LeaseExpiresAtUtc`, `LastSuccessStartedAtUtc`,
`NextAttemptAtUtc`, `FinishedAtUtc`, `Error`, `lastRun`) są nullowalne w typach i obsłużone.
Porównanie `j.LeaseRunId == previousRunId` (`JobLeaseStore.cs:69`) przy domyślnej semantyce null EF
Core daje `IS NULL` dla parametru `null` (pokryte przez scenariusze z `previousRunId = null`
i `Expired_lease_is_abandoned_and_job_reruns_without_backoff`). `JobScheduler.cs:92-93` zgłasza
wyjątek, gdy wiersza nie ma po wstawieniu. Uzasadnione.

## Correctness Issues

### Ważne (do poprawy przed scaleniem)

**K-1. Okno TOCTOU: drugi przebieg w tym samym okresie.**
`api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobScheduler.cs:77-84` czyta stan
(`PrepareAsync`, osobny zakres), liczy `JobDue.IsDue` na tym odczycie i dopiero potem przejmuje
dzierżawę. Warunek przejęcia w `JobLeaseStore.cs:67-70` sprawdza tylko `LeaseRunId == previousRunId`
i wygaśnięcie. Przebieg zdarzeń: proces B czyta stan (brak sukcesu, `LeaseRunId = null`). Proces A
w tym czasie przejmuje dzierżawę, wykonuje zadanie i kończy je sukcesem, a `LeaseRunId` wraca do
`null`. Wtedy warunek B (`LeaseRunId IS NULL`) jest spełniony i zadanie biegnie drugi raz w tym samym
okresie. Okno jest wąskie (od odczytu do `UPDATE`), ale dotyczy nakładania procesów przy recyklingu,
które specyfikacja wymienia wprost. Zadanie SF-16 bez pracy do wykonania może skończyć się
w milisekundach. Narusza kryterium „równoczesne próby uruchomienia dają jeden przebieg”.
Kolumna `LastAttemptStartedAtUtc` jest zapisywana (`JobLeaseStore.cs:75`), ale nigdzie nieczytana,
a nadawałaby się na token tego warunku (np. `LastAttemptStartedAtUtc == odczytana wartość` albo
`LastSuccessStartedAtUtc` w `WHERE`). Test `Concurrent_attempts_run_once` (`SchedulerTests.cs:92`)
sprawdza próby w trakcie trzymanej dzierżawy, więc tego przypadku nie wykrywa.

**K-2. Anulowanie zgłoszone przez samo zadanie zapisuje fikcyjny błąd.**
`api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/JobExecutor.cs:123-125`: gdy zadanie
zgłosi `OperationCanceledException` niezwiązany z tokenami runnera, metoda async `RunJobAsync` kończy
się stanem `Canceled`. Typowy przykład w SF-13 to `TaskCanceledException` z limitu czasu `HttpClient`.
Wtedy `jobTask.Exception` = `null`, a do historii i logu (`LogJobException`) trafia sztuczny
`InvalidOperationException: Nieznany błąd zadania.`. Prawdziwy wyjątek i jego komunikat znikają, choć
specyfikacja wymaga typu i komunikatu w opisie oraz pełnego wyjątku w logu („Dzierżawa i przebieg”
pkt 8). Licznik i odstęp działają poprawnie. Dotyczy bezpośrednio konsumenta SF-13.

### Drobne

**K-3.** `JobExecutor.cs:61-67` — `MarkHungAsync` zwiększa licznik i ustawia odstęp także wtedy, gdy
anulowanie przyszło od wywołującego (zamykanie aplikacji), a nie z limitu czasu. Specyfikacja
(pkt 5) mówi, że zamykanie nie zmienia licznika. W praktyce limit łagodnego zamykania hosta
(domyślnie 30 s) jest krótszy niż zapas 2 min, więc ta gałąź prawie nigdy się nie wykona.

**K-4.** `JobExecutor.cs:50-54` z `RenewAsync` (`:161`, `:167`) — `leaseLost.Cancel()` wykonuje
synchronicznie, na zadaniu przedłużania, callbacki rejestracji tokenu zadania. Wyjątek z takiego
callbacku (`AggregateException`) wychodzi poza `catch` w `RenewAsync` i psuje `renewTask`. Wtedy
`await renewTask` (`:59`) zgłasza wyjątek, zakończenie nie zostaje zapisane, a przebieg czeka jako
`running` na wygaśnięcie dzierżawy. Mało prawdopodobne, ale łatwe do zabezpieczenia.

**K-5.** `JobExecutor.cs:103-106` — zadanie, które zignorowało limit czasu, ale skończyło się w zapasie
bez wyjątku, zostaje zapisane jako `succeeded`. Kryterium mówi, że przekroczenie `MaxDuration` „kończy
przebieg jako `failed`”. Taka klasyfikacja jest do obrony (praca wykonana), ale to niejawne odstępstwo.

**K-6.** `JobDue.cs:44` uznaje dzierżawę za wygasłą przy `lease <= now`, a `JobLeaseStore.cs:70`
wymaga `LeaseExpiresAtUtc < now`. Na samej granicy sprawdzenie uzna zadanie za należne, a przejęcie
się nie uda. Nieszkodliwe, ale niespójne.

**K-7.** `JobScheduler.cs:69-72` — gdy stan odczytany przez `TryRunNowAsync` wskazuje dzierżawę, którą
właściciel zwolni przed `UPDATE`, warunek `LeaseRunId == previousRunId` nie zostanie spełniony, a
wywołujący dostanie `AlreadyRunning`, choć nic już nie biegnie. Fałszywie negatywny wynik, bez
skutków dla danych.

Sprawdzone bez uwag: kolejność `SetProperty` w `JobLeaseStore.cs:140-141` i `:183-184` (SQL Server
liczy wszystkie wyrażenia `SET` na wartościach sprzed aktualizacji, więc `ConsecutiveFailures == 0`
odnosi się do starego licznika: 15 min przy pierwszym błędzie, 1 h przy kolejnych). Transakcje
przejęcia i zakończenia są na własnym kontekście. Przedłużanie i zapis wyniku działają w osobnych
zakresach, więc brak równoległych operacji na jednym `DbContext`. Brak `Task.Run`. `TimeProvider`
w `CancellationTokenSource`, `Task.Delay` i `PeriodicTimer`. Zapis zakończenia z własnym limitem 10 s
niezależnym od `stoppingToken`. Spóźnione zadanie obserwuje `ObserveLate`. `graceTask` w zwykłej
ścieżce nie jest awaitowany, ale kończy się bez wyjątku po `stopRenewal`.

## Test Gaps

- **T-1.** Brak testu zamykania w trakcie przebiegu: anulowanie tokenu wywołującego daje `failed`
  „Przerwano…” bez licznika i bez odstępu (`JobExecutor.cs:118-121`). To osobna gałąź specyfikacji
  („Dzierżawa i przebieg” pkt 5, przypadek brzegowy „Zamykanie aplikacji”).
- **T-2.** `SchedulerTests.cs:335`: komentarz mówi „Po wygaśnięciu dzierżawy, ale przed odstępem”, ale
  zegar nie jest przesuwany. Dzierżawa (ostatnie przedłużenie ok. 4 min + 5 min) jest jeszcze ważna,
  więc test pokazuje tylko blokadę dzierżawą, a nie kluczową właściwość [R3]: odstęp blokuje ponowny
  start po wygaśnięciu dzierżawy zawieszonego zadania.
- **T-3.** `JobsEndpointTests.cs` nie ma przypadków z planu: zadanie bez przebiegu (`lastRun = null`)
  i zadanie w toku (`isRunning = true`). Nie jest też sprawdzane mapowanie `running` bez dzierżawy
  na `abandoned` (`JobStatusReader.cs:51-60`).
- **T-4.** Nie ma testu prawdziwie równoczesnego przejęcia (K-1). `Concurrent_attempts_run_once`
  (`SchedulerTests.cs:92`) jest sekwencyjny wobec trzymanej dzierżawy.
- **T-5.** Brak testu ścieżki dwóch kolejnych błędów bazy przy przedłużaniu (`RenewFailedError`)
  i testu zadania kończącego się własnym `OperationCanceledException` (K-2).
- **T-6.** Determinizm: `Waiting.AdvanceUntilAsync` (`SchedulerHost.cs:113-122`) daje każdemu krokowi
  zegara 500 ms rzeczywistego czasu na zapis w bazie i sprawdza warunek po ostatnim kroku bez
  dodatkowego czekania. `Lost_lease_cancels_running_job` (`SchedulerTests.cs:217`, `maxSteps: 4`) ma
  po przedłużeniu w 2. kroku ok. 1–1,5 s na zapytanie SQL. Na wolnym CI (SQL w Dockerze) to możliwe
  źródło niestabilności.

Uruchomione w tym przeglądzie: `dotnet test api/tests/StoneFlow.Shared.Tests --no-build` — 45/45.
Build, format, testy architektury i 15 testów integracyjnych harmonogramu raportuje autor; nie
uruchamiałem ich ponownie (ograniczona pamięć). Pełny zestaw testów integracyjnych i
`deploy/scripts/test-migrate.sh` nie zostały uruchomione ani przez autora, ani w przeglądzie.

## Code Smells

- `api/src/Shared/StoneFlow.Shared.Application/Scheduling/JobTypes.cs:45-51` — `JobCodes.ParseTrigger`
  nieużywane (martwy kod).
- `api/src/Shared/StoneFlow.Shared.Infrastructure/Scheduling/SchedulingDbContext.cs:54` /
  `JobLeaseStore.cs:75` — `LastAttemptStartedAtUtc` tylko zapisywane, nigdzie nieczytane ani
  niezwracane przez API (zob. K-1).

## Recommendation

- Fix critical issues before merge. Brak uwag krytycznych i naruszeń niezmienników. Przed scaleniem
  warto poprawić K-1 (warunek przejęcia odporny na zakończony w międzyczasie przebieg) i K-2 (prawdziwy
  wyjątek przy anulowaniu zgłoszonym przez zadanie) oraz domknąć testy T-1 i T-2. Pozostałe uwagi można
  przenieść do SF-13/SF-16.
