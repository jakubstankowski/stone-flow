# Spec review — SF-11

> Przegląd wersji 2 specyfikacji (2026-11-06). Poprzedni przegląd wersji 1 (FAIL):
> `03-spec-review-v1.md`.

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Wersja 2 usuwa oba problemy krytyczne z wersji 1. Nie ma sekretu wyzwalacza: harmonogram hostingu tylko budzi proces przez istniejący anonimowy `GET /health`, a należne zadania sprawdza start aplikacji i timer. Dzierżawa ma opisany fencing: warunkowe przejęcie, przedłużanie, utratę i zakończenie. Rozstrzygnięto też wszystkie dwanaście uwag niekrytycznych z wersji 1. Żaden niezmiennik nie jest naruszony. Zostają luki, które plan musi domknąć jawnie: brak w testach punktu wejścia do sprawdzenia należności przy `Scheduling:Enabled=false`, synchroniczny `IJobRunner` wobec „Synchronizuj teraz” w SF-13, brak backoffu dla zadania, które zawiesza się po przekroczeniu czasu, nieobjęty zmianą `test-migrate.sh` oraz ręczny dowód D-08, którego nie da się zebrać bez zarejestrowanego zadania.

## Findings

### Critical Issues

None.

Weryfikacja problemów krytycznych z wersji 1:

1. **Niezmiennik 6 (token w logach)** — rozwiązane. W wersji 2 nie ma tokenu ani nowego endpointu anonimowego (sekcje „Wyzwolenie” pkt 2, „Endpoints”, „Out of scope”). `GET /health` jest już anonimowy (`Program.cs`: `MapHealthChecks("/health").AllowAnonymous()`, wpis `GET /health` w `EndpointSecurityTests.AllowedAnonymous`) i niczego nie wyzwala poza startem procesu. Uwaga do przeglądu v1: produkcyjny `appsettings.json` ma `"Microsoft.AspNetCore": "Warning"`, a `Information` jest tylko w `appsettings.Development.json`. Argument v1 o logowaniu ścieżki przez hosting na produkcji opierał się więc na błędnym odczycie. Problem logów IIS był jednak prawdziwy, a wersja 2 i tak go usuwa.
2. **Utrata dzierżawy i fencing** — rozwiązane. Sekcja „Dzierżawa i przebieg” opisuje przejęcie warunkowe (`LeaseRunId IS NULL OR LeaseExpiresAtUtc < teraz`), przedłużanie co `MaxDuration / 3` pod warunkiem `LeaseRunId = własne Id`, anulowanie tokenu przy utracie (także po dwóch błędach bazy z rzędu), zakończenie pod warunkiem własnej dzierżawy oraz to, co dzieje się ze spóźnionym przebiegiem („Utracono dzierżawę”, bez zmiany `Jobs`). Zachowanie po przekroczeniu `MaxDuration` jest opisane (pkt 4). Liczby się spinają: przy dwóch kolejnych błędach przedłużenia (ok. 20 min po ostatnim udanym) runner anuluje zadanie, zanim dzierżawa wygaśnie (ostatnie udane przedłużenie + 32 min). Inna instancja nie przejmie więc dzierżawy, dopóki właściciel się nie wycofa, o ile zadanie honoruje anulowanie.

### Non-Critical Issues

1. **Testy integracyjne nie mają jak wywołać sprawdzenia należności** (sekcje „Wyzwolenie” pkt 6, „Uruchomienie na żądanie” pkt 1 i 3, „Verification”). Przy `Scheduling:Enabled=false` pętla się nie uruchamia, a `IJobRunner.TryRunAsync` z definicji pomija należność („uruchamia … od razu, bez sprawdzania należności”). Należność sprawdza wyłącznie pętla. Tymczasem testy z sekcji „Verification” („uśpienie (przesunięcie zegara o kilka terminów i jedno sprawdzenie)”, „restart”, „błąd i odstęp”, „porzucenie … zadanie uruchamia się od razu”) i kryteria akceptacji „dokładnie raz przy pierwszym sprawdzeniu”, „nie uruchamia się drugi raz w tym samym okresie”, „nie rusza przed swoim pierwszym terminem” wymagają deterministycznego wywołania jednego sprawdzenia. Spec nie nazywa tego punktu wejścia (np. operacji „sprawdź należne zadania” dostępnej z testu), więc plan musiałby go dopisać sam. Nie wiadomo też, co oznacza `TryRunAsync(key, JobTrigger.Timer)` wywołane z kodu modułu: czy omija należność, czy ją sprawdza.
2. **Synchroniczny `IJobRunner` wobec „Synchronizuj teraz” (SF-13)** (sekcje „Uruchomienie na żądanie” pkt 1, „Types crossing a boundary”, „Invariant impact” wiersz 3). `TryRunAsync` zwraca `Completed(status)`, czyli czeka na koniec przebiegu, a przebieg może trwać do `MaxDuration` = 30 min. SF-13 musiałby albo trzymać żądanie HTTP przez cały przebieg (przez proxy hostingu frontendu), albo samodzielnie uruchamiać pracę w tle, czego `conventions.md` zabrania przez `Task.Run` wokół I/O. PRD sekcja 5 przewiduje stan „Synchronizacja trwa”, a więc start asynchroniczny. Spec nie mówi też, który token SF-13 ma przekazać i jak runner kończy przebieg anulowany innym tokenem niż `stoppingToken`, np. po rozłączeniu klienta (`RequestAborted`). Lista gałęzi zakończenia w „Dzierżawa i przebieg” pkt 5 obejmuje tylko anulowanie przy zamykaniu aplikacji. To ten sam problem, który v1 zgłosiła jako uwagę 3 dla wyzwalacza HTTP, przeniesiony teraz do kontraktu konsumenta.
3. **Zadanie ignorujące anulowanie po przekroczeniu czasu omija odstęp** (sekcje „Dzierżawa i przebieg” pkt 4 i 6, „Edge Cases” → „Przekroczenie czasu”). Gdy zadanie nie kończy się w 2-minutowym zapasie, przebieg zostaje `running`, a później dostaje `abandoned`. Według pkt 6 `abandoned` nie zwiększa licznika i nie ustawia odstępu. Zadanie, które za każdym razem się zawiesza, uruchamia się więc ponownie od razu po każdym wygaśnięciu dzierżawy, a `ConsecutiveFailures` zostaje 0. Przeczy to przypadkowi brzegowemu „Przekroczenie czasu: … `failed`, odstęp 15 min” i wymogowi widocznego niepowodzenia. Do tego `isRunning` = `true` i stan „Synchronizacja trwa” utrzymują się do wygaśnięcia dzierżawy. Ostatnie udane przedłużenie mogło nastąpić tuż przed `MaxDuration`, więc dzierżawa trwa wtedy jeszcze ok. 32 min po nim, choć runner przestał już przedłużać.
4. **Ręczny dowód D-08 jest niewykonalny w zakresie SF-11** (sekcje „Out of scope” → „Rejestracja jakiegokolwiek zadania produkcyjnego, także kontrolnego”, „Verification” → manual, „Dependencies”). Krok ręczny każe „sprawdzić w `GET /api/jobs` przebiegi po recyklingu puli i po nocy bez ruchu”. Bez zarejestrowanego zadania endpoint zwraca pustą listę i nie ma przebiegów do sprawdzenia. Dowód dla D-08 pkt 2 przesuwa się więc do SF-13 lub SF-16, które same zależą od tego mechanizmu. Spec powinna to powiedzieć wprost albo przewidzieć sposób zebrania dowodu.
5. **`test-migrate.sh` i lista kontekstów w testach nie są objęte zmianą** (sekcje „Context” → warstwy i projekty, „Acceptance Criteria” → migracja „przechodzi w CI”). `deploy/scripts/test-migrate.sh` sprawdza na sztywno `"5"` tabel `__EFMigrationsHistory` (scenariusze 1 i 2) i listę schematów po błędzie w `orders` (scenariusz 3). Dopisanie `SchedulingDbContext` do `contexts.sh` psuje scenariusze 1 i 2. Scenariusz 3 nie wykrywa, czy `scheduling` nie został uruchomiony po błędzie. Podobnie `TestDatabase.MigrateAsync` (`api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/TestSupport.cs`) migruje jawną listę pięciu kontekstów. Spec wymienia `contexts.sh`, ale nie te pliki.
6. **Odczyt strefy Europe/Warsaw na hostingu Windows** (sekcje „Należność zadania”, „Assumptions”). Hosting to IIS na Windows. W kodzie istnieje wzorzec `TryFindSystemTimeZoneById("Europe/Warsaw")` z rezerwą na `"Central European Standard Time"` (`Orders.Application/WarsawTime.cs`), ale jest lokalny dla modułu Orders, a Shared nie może go użyć. Spec nie mówi, jak mechanizm w Shared rozwiązuje strefę, ani nie zakłada, że identyfikator IANA działa na koncie.
7. **Brak jawnego odniesienia do zakazu z `rules.md` dla D-08** (sekcje „Context”, „Dependencies”). `rules.md` zabrania implementować obszar zablokowany niezatwierdzoną decyzją (D-08 otwarte). Spec pośrednio rozstrzyga element D-08 pkt 2 „bezpieczne uwierzytelnienie wywołań”: wyzwolenie nie wymaga uwierzytelnienia, bo nie niesie uprawnień. Uzasadnienie techniczne jest przekonujące, a D-08 w PRD blokuje „ostateczną konfigurację środowisk”, a nie budowę mechanizmu. Spec powinna to jednak powiedzieć wprost i wskazać, kto odnotuje przy D-08 (SF-1 / PRD sekcja 12), że ten element przestaje być potrzebny.

### Unclear or Ambiguous Sections

- **„Dzierżawa i przebieg” pkt 3 i 5:** utrata dzierżawy przez dwa błędy bazy z rzędu, gdy wiersz nadal wskazuje własny przebieg. Warunek zakończenia jest wtedy spełniony, ale nie wiadomo, która gałąź się stosuje: „wyjątek” z odstępem, „Utracono dzierżawę” czy osobny opis. To samo dotyczy zadania, które po anulowaniu kończy się `OperationCanceledException`.
- **„Rejestracja zadania” pkt 2:** spec nie mówi, czy unikalność kluczy jest sprawdzana także przy `Scheduling:Enabled=false`. Nie mówi też, jak runner poznaje klucz, harmonogram i `MaxDuration`, skoro są właściwościami instancji usługi scoped. Wymaga to tworzenia wszystkich zadań wraz z ich zależnościami przy każdym sprawdzeniu i każdym `GET /api/jobs`.
- **„Dzierżawa i przebieg”, wstęp:** „Zadanie dostaje osobny, nowy zakres DI”. SF-13 zapisuje wiele zamówień, a błąd jednego nie zatrzymuje przebiegu (PRD sekcja 5). Według `conventions.md` po wycofanej jednostce pracy zakres nie pisze dalej. Spec nie mówi, że zadanie z wieloma jednostkami pracy tworzy własne zakresy.
- **„Stan zadań” pkt 2:** po awarii przebiegu ręcznego, gdy zadanie nie jest już należne w danym okresie, `lastRun.status` = `running` może się utrzymywać do następnego terminu, nawet ok. 24 h, przy `isRunning` = `false`. Spec przyznaje rozbieżność, ale nie mówi, jak ma ją pokazywać UI konsumentów.

### Invented or Unsupported Requirements

None. Wartości techniczne (takt 5 min, `MaxDuration` 30 min, zapas 2 min, odstępy 15 min i 1 h, dwa błędy przedłużenia) i reguła „pierwszego terminu” są jawnie wymienione w „Assumptions” i uzasadnione. Reguła „pierwszego terminu” zgadza się z PRD sekcja 5 („raz dziennie o 23:00”). `Scheduling:Enabled`, `IJobRunner` i `GetJobStatuses` wynikają z potrzeb testów oraz konsumentów (SF-13 „Synchronizuj teraz”, INT-01 „Synchronizacja trwa”) opisanych w PRD.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Context, Dependencies | `rules.md` „Planning and analysis” (zakaz przy otwartym D-08); PRD sekcja 12 D-08; SF-1 „Do ustalenia” pkt 2 | Brak sprzeczności merytorycznej: D-08 blokuje ostateczną konfigurację środowisk, a spec wyłącza konfigurację panelu i dowód. Spec pośrednio uznaje jednak element „bezpieczne uwierzytelnienie wywołań” za zbędny i nie wskazuje, kto zapisze to przy D-08 (uwaga 7). |
| Context, Storage, Assumptions | `conventions.md` „Naming” (schemat = nazwa modułu), „Data access” (tabela ma moduł-właściciela), „Structure”, „Naming” → endpointy w `StoneFlow.{Module}.Api` | Świadome odstępstwo z uzasadnieniem. Spec przewiduje aktualizację `conventions.md` i osobny test architektury. Brak konfliktu do rozstrzygnięcia. |
| Acceptance Criteria (migracja w CI) | `deploy/scripts/test-migrate.sh` (oczekiwane 5 tabel historii) | Kod przeczy kryterium, dopóki skrypt nie zostanie zmieniony, a spec go nie wymienia (uwaga 5). |
| Verification → manual | Out of scope („bez zadania produkcyjnego”) | Krok ręczny wymaga przebiegów, których w zakresie SF-11 nie będzie (uwaga 4). |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sales separate from cash | Not applicable | Mechanizm nie dotyka sprzedaży, wpłat ani wydatków. |
| 2. Backend decimal money | Not applicable | Brak kwot. |
| 3. API-enforced access | OK | Jedyny nowy endpoint `GET /api/jobs` jest objęty polityką fallback i automatycznie testem `Every_protected_endpoint_returns_401_without_session`. Nie ma nowego endpointu anonimowego. `/health` się nie zmienia i nie zwraca danych. Uruchomienie na żądanie jest dostępne tylko z kodu. |
| 4. Retries never duplicate | OK | Dzierżawa z fencingiem i jedno nadrobienie. Unikalność skutków (źródło + zewnętrzne ID, definicja + okres) należy do SF-13/SF-16 i jest wpisana w kontrakt `IScheduledJob`. |
| 5. Financial audit trail | Not applicable | Mechanizm nie zmienia danych finansowych. Autorzy systemowi `AuditAuthor.AutomatedJob` i `WooCommerceSync` istnieją (`IAuditLog.cs`). |
| 6. Secrets stay server-side | OK | Brak sekretu wyzwalacza. Opis błędu w bazie bez stosu i danych żądań. Za treść komunikatów wyjątków odpowiada zadanie, co jest jawnie w kontrakcie. Produkcyjne logowanie `Microsoft.AspNetCore` jest na `Warning`. |
| 7. Module and layer boundaries | OK | Kontrakt w `Shared.Application` (bez EF Core, HTTP i hostingu, zgodnie z `LayerDependencyTests` i `ProjectReferenceTests`). Implementacja w `Shared.Infrastructure` (ma `FrameworkReference Microsoft.AspNetCore.App` i EF Core). Moduły nie znają tabel `scheduling`. |
| 8. No identifying data in docs | OK | Instrukcja używa `<adres-api>`. |

## Risk

- Recorded: medium
- Assessed: medium. Mechanizm sam nie zmienia pieniędzy ani danych klientów, ale współdzielą go SF-13 i SF-16. Jego błąd (brak przebiegu albo podwójny przebieg) przenosi się na import zamówień i koszty. Przegląd kodu musi uwzględnić obu konsumentów, jak zapisano w specyfikacji.

## Assumptions Detected

| Założenie | Czy spec je podaje |
|---|---|
| Każde żądanie HTTP budzi uśpiony proces IIS (in-process), a start hosta wykonuje sprawdzenie | Tak („Wyzwolenie” pkt 2, „Context”) |
| Limit bezczynności puli > 15 min, więc `/health` co 15 min podtrzymuje proces; przebieg do 30 min nie zostanie przerwany uśpieniem | Tak, do potwierdzenia w próbie D-08 |
| Wyzwolenie nie wymaga uwierzytelnienia, bo nie niesie uprawnień | Tak („Assumptions”, zmiana w wersji 2) |
| Zadania honorują anulowanie i nie piszą po nim | Tak (kontrakt `IScheduledJob`) |
| Zadania konsumentów są idempotentne | Tak („Invariant impact” 4) |
| „Teraz” dla dzierżawy pochodzi z `TimeProvider` aplikacji, nie z czasu SQL, więc instancje mają zgodny zegar | Nie. Ukryte, już zgłaszane w v1 (przy jednym serwerze ryzyko małe) |
| Identyfikator strefy `Europe/Warsaw` jest dostępny na hostingu Windows | Nie. Ukryte (uwaga 6) |
| Testy mogą deterministycznie wywołać jedno sprawdzenie należności przy wyłączonej pętli | Nie. Ukryte i niewynikające z kontraktu (uwaga 1) |
| Konsument (SF-13) może czekać synchronicznie na koniec przebiegu wywołanego z HTTP | Nie. Ukryte (uwaga 2) |
| Jeden zakres DI na przebieg wystarcza zadaniom z wieloma jednostkami pracy | Nie. Niejasne |
| Dowód D-08 pkt 2 da się zebrać bez zarejestrowanego zadania | Nie. Ukryte i błędne (uwaga 4) |
| Dzienny harmonogram o stałej godzinie wystarcza SF-16; sekwencyjne przebiegi są dopuszczalne | Tak („Assumptions”) |
| Historia przebiegów bez retencji | Tak („Assumptions”) |
| Dzierżawa w tabeli zamiast `sp_getapplock` | Tak („Assumptions”) |

## Recommendation

- Proceed as-is. Plan (`sdd-plan SF-11`) powinien jawnie domknąć uwagi 1–5: punkt wejścia „jedno sprawdzenie” dla testów, sposób wywołania `IJobRunner` przez SF-13 (czekanie albo start w tle) i gałąź zakończenia przy anulowaniu innym niż zamykanie aplikacji, odstęp dla przebiegu porzuconego po przekroczeniu czasu, aktualizację `test-migrate.sh` i `TestDatabase.MigrateAsync`, oraz to, kiedy realnie da się zebrać dowód D-08. Jeśli autor woli rozstrzygnąć uwagi 2 i 3 w specyfikacji, a nie w planie, wystarczy krótka rewizja bez ponownego przeglądu całości.
