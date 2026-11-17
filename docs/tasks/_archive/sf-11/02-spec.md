# Task Specification — SF-11

> Wersja 2 (2026-11-06), po przeglądzie `03-spec-review.md` z wynikiem FAIL. Zmiany: usunięto
> endpoint wyzwalacza z tokenem (budzenie procesu przez istniejący `GET /health`), opisano fencing
> dzierżawy, rozstrzygnięto uwagi niekrytyczne 1–12 i nieuzasadnione natychmiastowe pierwsze
> uruchomienie.

## Source

[SF-11](https://standev.youtrack.cloud/issue/SF-11)

## Goal

Zbudować trwały harmonogram zadań automatycznych backendu: wspólny mechanizm, w którym moduł
rejestruje zadanie z terminem dziennym w strefie Europe/Warsaw, a mechanizm uruchamia je, gdy jest
należne. Stan harmonogramu (ostatni sukces, ostatnia próba, dzierżawa, historia przebiegów) jest
w SQL Server, więc restart, recykling puli i uśpienie procesu nie gubią zadań. Sprawdzenie należnych
zadań uruchamia się przy starcie aplikacji i okresowo w procesie; harmonogram hostingu jedynie budzi
i podtrzymuje proces, wywołując istniejący `GET /health`. Wyzwolenie samo nie niesie żadnych
uprawnień, więc nie wymaga sekretu. Mechanizm chroni przed równoległym przebiegiem tego samego
zadania dzierżawą w SQL z warunkowymi zapisami (fencing), nadrabia termin pominięty w czasie przerwy
jednym uruchomieniem, ponawia po błędzie z odstępem i zostawia zapisany ślad każdego przebiegu,
dostępny przez uwierzytelniony endpoint.

## Context

- PRD sekcja 10 „Automatyzacja”: harmonogram odporny na restart i usypianie hostingu, nie oparty
  wyłącznie na timerze w pamięci. Ten sam zakaz w `conventions.md` („Do not”).
- SF-1 (`docs/tasks/sf-1/01-weryfikacja.md`): SmarterASP.NET .NET Premium W1050. Harmonogram panelu
  wywołuje HTTP GET, minimalny interwał 15 min. Nie potwierdzono nagłówków, limitu czasu, strefy ani
  ustawień usypiania/recyklingu puli. Punkt 3 „Do ustalenia” wymienia wymagania dla SF-11: stan
  i ostatni sukces w SQL, blokada równoległości, idempotencja, nadrobienie zaległych okresów.
- Konsumenci: SF-13 (uzupełnianie WooCommerce raz dziennie o 23:00 Europe/Warsaw oraz „Synchronizuj
  teraz”, PRD sekcja 5) i SF-16 (wydatki cykliczne, WYD-03–06). Ich logika jest poza zakresem.
- Dlaczego harmonogram nie oznacza „timera w pamięci”: timer jedynie wyzwala sprawdzenie, a należność,
  ostatni sukces i blokada są w SQL. Gdy proces śpi, harmonogram hostingu budzi go żądaniem HTTP;
  start aplikacji sam wykonuje sprawdzenie.
- Warstwy i projekty:
  - `StoneFlow.Shared.Application/Scheduling/` — kontrakt dla modułów: `IScheduledJob`, `JobSchedule`,
    `JobRunContext`, `JobTrigger`, `IJobRunner` (uruchomienie na żądanie), zapytanie `GetJobStatuses`
    z handlerem i port odczytu `IJobStatusReader`, DTO stanu. Kontrakt używany przez więcej niż jeden
    moduł leży w `StoneFlow.Shared.Application` (`conventions.md`).
  - `StoneFlow.Shared.Infrastructure/Scheduling/` — runner, wyliczanie terminów, dzierżawa,
    `SchedulingDbContext` (schemat `scheduling`) z migracjami, `BackgroundService` z timerem, opcje,
    implementacja `IJobStatusReader`, rejestracja DI (`AddSharedInfrastructure`).
  - `StoneFlow.Api` (host) — endpoint `GET /api/jobs`, mapowany w `Program.cs`; endpoint nie należy do
    żadnego modułu.
  - `deploy/scripts/contexts.sh` — `SchedulingDbContext` dopisany na końcu listy.
  - `docs/wdrozenia.md` — ustawienie harmonogramu w panelu (GET `<adres-api>/health` co 15 min)
    i sprawdzenie przebiegów.
  - `.sdd/context/conventions.md` — zapis nowych zasad (schemat i tabele w Shared, `BackgroundService`
    w Shared, endpoint w hoście, sposób rejestracji zadań).

## Scope

### In scope

- Kontrakt rejestracji zadania i wyliczanie terminu dziennego „o HH:mm Europe/Warsaw”.
- Trwały stan zadań i historia przebiegów w SQL (`scheduling`).
- Wyzwolenia: przy starcie aplikacji i timer w procesie; uruchomienie na żądanie przez `IJobRunner`
  (dla „Synchronizuj teraz” w SF-13) z tą samą dzierżawą.
- Budzenie i podtrzymanie procesu przez harmonogram hostingu wywołujący istniejący `GET /health`
  (tylko dokumentacja i konfiguracja, bez zmian kodu endpointu).
- Dzierżawa w SQL z fencingiem: przejęcie, przedłużanie, utrata, zakończenie warunkowe.
- Nadrobienie pominiętego terminu jednym uruchomieniem.
- Ponowienie po błędzie z odstępem.
- Zapis śladu każdego przebiegu, w tym przebiegu porzuconego.
- Uwierzytelniony endpoint stanu zadań i zapytanie CQRS.
- Wyłącznik `Scheduling:Enabled` dla testów.
- Testy jednostkowe, integracyjne i architektury; aktualizacja `docs/wdrozenia.md` i `conventions.md`.

### Out of scope

- Logika synchronizacji WooCommerce (SF-13) i wydatków cyklicznych (SF-16), zgodnie ze zgłoszeniem.
- Biznesowy kursor synchronizacji: „od ostatniego sukcesu” w SF-13 oznacza sukces synchronizacji
  z PRD sekcja 5 („sklep odpowiedział i pobrano wszystkie strony”), zapisany przez SF-13. Ostatni
  sukces przebiegu harmonogramu nie jest tym kursorem.
- Rejestracja jakiegokolwiek zadania produkcyjnego, także kontrolnego.
- Ostrzeżenia w UI — dodadzą je SF-14, SF-16 i SF-19.
- Konfiguracja harmonogramu w panelu i zebranie dowodu z restartu i uśpienia (D-08 pkt 2) — osobny
  krok ręczny po wdrożeniu.
- Nowy anonimowy endpoint wyzwalacza lub jakikolwiek sekret wyzwalacza.
- Harmonogramy inne niż dzienny o stałej godzinie.
- Retencja i czyszczenie historii przebiegów.

## Behavior

### Rejestracja zadania

1. Moduł rejestruje w DI implementację `IScheduledJob` (scoped) z: stałym kluczem (np.
   `woocommerce-backfill`), harmonogramem `JobSchedule.DailyAt(new TimeOnly(23, 0))` w Europe/Warsaw
   i maksymalnym czasem przebiegu `MaxDuration` (domyślnie 30 min).
2. Przy starcie runner sprawdza unikalność kluczy (bez dostępu do bazy). Powtórzony klucz zatrzymuje
   start aplikacji z czytelnym błędem.
3. Brak zarejestrowanych zadań (stan po SF-11) oznacza, że wyzwolenie kończy się bez dostępu do bazy.
   Dzięki temu hosty testowe bez bazy i bez zadań działają jak dotąd.
4. Wiersz stanu zadania w `scheduling.Jobs` powstaje przy pierwszym sprawdzeniu, z
   `RegisteredAtUtc` = teraz. Zadanie wyrejestrowane z kodu zostaje w bazie, ale runner go nie
   uruchamia ani nie pokazuje.

### Wyzwolenie

1. Sprawdzenie należnych zadań wykonuje jeden `BackgroundService` (`JobSchedulerService`):
   - raz zaraz po starcie hosta,
   - potem co `Scheduling:TimerInterval` (domyślnie 5 min), timer `PeriodicTimer` z `TimeProvider`.
2. Harmonogram hostingu wywołuje co 15 min `GET <adres-api>/health`. Żądanie budzi uśpiony proces
   (start aplikacji wykonuje sprawdzenie, pkt 1) i podtrzymuje działający proces, więc timer może
   działać. Endpoint `/health` się nie zmienia.
3. Jedno sprawdzenie przechodzi po zarejestrowanych zadaniach po kolei (w kolejności klucza)
   i uruchamia każde należne. Sprawdzenia w jednym procesie się nie nakładają, bo wykonuje je jedna
   pętla `BackgroundService`.
4. Błąd bazy w trakcie sprawdzenia jest logowany; pętla działa dalej i spróbuje przy kolejnym takcie.
5. Zamknięcie aplikacji anuluje `stoppingToken`; pętla kończy się po bieżącym przebiegu (z anulowanym
   tokenem zadania), w granicach limitu łagodnego zamykania hosta.
6. `Scheduling:Enabled = false` wyłącza pętlę (`BackgroundService` kończy się od razu). `IJobRunner`
   działa niezależnie, więc testy uruchamiają przebiegi bezpośrednio i deterministycznie.

### Uruchomienie na żądanie (`IJobRunner`)

1. `IJobRunner.TryRunAsync(jobKey, JobTrigger.Manual, ct)` uruchamia zarejestrowane zadanie od razu,
   bez sprawdzania należności, z tą samą dzierżawą i zapisem przebiegu. Wynik: `Completed(status)`,
   `AlreadyRunning` albo `UnknownJob`.
2. Udane uruchomienie ręczne ustawia ostatni sukces jak każdy inny przebieg; jeśli zaczęło się po
   ostatnim terminie, zadanie nie jest już należne w tym okresie.
3. Pętla harmonogramu używa tej samej ścieżki z `JobTrigger.Startup` albo `JobTrigger.Timer`, po
   sprawdzeniu należności.
4. Nieudane uruchomienie ręczne nie ustawia odstępu (`NextAttemptAtUtc`) i nie zmienia licznika
   kolejnych błędów harmonogramu — decyzję o ponowieniu podejmuje użytkownik. Przebieg zostaje
   zapisany jako `failed`.

### Należność zadania

1. `ostatni termin` = najpóźniejszy moment „HH:mm Europe/Warsaw” nie późniejszy niż teraz.
2. `pierwszy termin` = najwcześniejszy moment „HH:mm Europe/Warsaw” nie wcześniejszy niż
   `RegisteredAtUtc`. Zadanie nie jest należne przed swoim pierwszym terminem — nowo wdrożone zadanie
   rusza dopiero o swojej godzinie.
3. Zadanie jest należne, gdy wszystkie warunki są spełnione:
   - `ostatni termin` ≥ `pierwszy termin`,
   - nie ma sukcesu albo ostatni sukces rozpoczął się przed `ostatnim terminem`,
   - `NextAttemptAtUtc` jest pusty albo minął,
   - zadanie nie ma ważnej dzierżawy.
4. Termin pominięty w czasie przerwy (uśpienie, restart, awaria hostingu) daje przy pierwszym
   sprawdzeniu po przerwie **jedno** uruchomienie, niezależnie od liczby pominiętych terminów.
   Nadrabianie okresów należy do zadania: `JobRunContext` podaje `DueAt` = `ostatni termin`,
   `LastSuccessStartedAt` i źródło wyzwolenia. W `JobRuns.DueAtUtc` zapisuje się `ostatni termin`
   (dla ręcznego — chwila startu).

### Dzierżawa i przebieg

Wszystkie zapisy runnera idą przez `SchedulingDbContext` w zakresie DI runnera, z własnym połączeniem
(`UseModuleSqlServer<SchedulingDbContext>(connectionString, schema)`, bez `ModuleConnection`).
Zadanie dostaje osobny, nowy zakres DI, więc jego jednostka pracy i ewentualne wycofanie nie dotyczą
zapisów runnera (`conventions.md`: po wycofanej jednostce pracy zakres nie pisze dalej).

1. **Przejęcie** — w jednej transakcji `SchedulingDbContext`:
   - warunkowa aktualizacja wiersza `Jobs`: ustawia `LeaseRunId` = nowe Id przebiegu,
     `LeaseExpiresAtUtc` = teraz + `MaxDuration` + 2 min zapasu, `LastAttemptStartedAtUtc` = teraz,
     pod warunkiem `LeaseRunId IS NULL OR LeaseExpiresAtUtc < teraz`;
   - jeśli aktualizacja dotknęła wiersza, a poprzedni `LeaseRunId` wskazywał przebieg `running`,
     ten przebieg dostaje `abandoned` z opisem „Przebieg przerwany (restart lub awaria procesu)”
     i `FinishedAtUtc` = teraz;
   - wstawienie przebiegu `running` (start, termin, źródło).
   Jeśli aktualizacja nie dotknęła wiersza, zadanie działa gdzie indziej: sprawdzenie je pomija,
   a `IJobRunner` zwraca `AlreadyRunning`. Bez wiersza runner najpierw go wstawia (konflikt klucza
   przy równoczesnym wstawieniu oznacza, że wiersz już jest).
2. **Przedłużanie** — co `MaxDuration / 3` warunkowa aktualizacja `LeaseExpiresAtUtc` = teraz +
   `MaxDuration` + 2 min, pod warunkiem `LeaseRunId = własne Id`.
3. **Utrata dzierżawy** — gdy przedłużenie nie dotknie wiersza (przejęła ją inna instancja) albo
   zakończy się błędem bazy dwa razy pod rząd, runner natychmiast anuluje token zadania i przestaje
   przedłużać. Zadanie po anulowaniu nie może wykonywać dalszych zapisów (kontrakt `IScheduledJob`:
   token anulowania jest obowiązujący).
4. **Przekroczenie czasu** — po `MaxDuration` runner anuluje token zadania i czeka na jego koniec
   najwyżej 2 min zapasu. Gdy zadanie nie skończy się w zapasie, runner przestaje przedłużać
   i zostawia dzierżawę do wygaśnięcia; kolejny przebieg oznaczy go jako `abandoned` (pkt 1).
5. **Zakończenie** — w jednej transakcji, wyłącznie pod warunkiem `Jobs.LeaseRunId = własne Id`:
   - **sukces**: przebieg `succeeded`, `FinishedAtUtc`; `Jobs`: `LastSuccessStartedAtUtc` = start
     przebiegu, `ConsecutiveFailures` = 0, `NextAttemptAtUtc` = null;
   - **wyjątek albo przekroczenie czasu** w przebiegu z pętli: przebieg `failed` z opisem;
     `ConsecutiveFailures` + 1; `NextAttemptAtUtc` = koniec + 15 min, gdy to pierwszy kolejny błąd,
     + 1 h przy następnych;
   - **wyjątek w przebiegu ręcznym**: przebieg `failed` z opisem; `Jobs` bez zmian licznika i odstępu;
   - **anulowanie przy zamykaniu aplikacji**: przebieg `failed` z opisem „Przerwano przy zamykaniu
     aplikacji”, bez zmiany licznika i odstępu — po restarcie zadanie jest od razu należne;
   - zawsze: `LeaseRunId` = null, `LeaseExpiresAtUtc` = null.
   Jeśli warunek nie jest spełniony (dzierżawę przejęto), runner nie zmienia `Jobs`; przebieg
   aktualizuje tylko wtedy, gdy ma jeszcze status `running`, na `failed` z opisem „Utracono
   dzierżawę”. Zdarzenie jest logowane jako ostrzeżenie.
6. `abandoned` nie zmienia licznika ani odstępu: przerwanie procesu nie jest błędem zadania, a zadanie
   powinno ruszyć jak najszybciej po restarcie. Przebieg porzucony nadal zostaje w historii jako ślad.
7. Fencing opiera się na warunku `LeaseRunId`; tabela nie ma kolumny `RowVersion`.
8. Opis błędu: typ wyjątku i komunikat, obcięte do 1000 znaków, bez stosu wywołań i bez danych żądań.
   Za treść komunikatów swoich wyjątków (bez sekretów i danych osobowych) odpowiada zadanie. Pełny
   wyjątek trafia do logu z kluczem zadania i Id przebiegu.

### Stan zadań

1. `GET /api/jobs` (sesja właściciela) woła `IDispatcher` z zapytaniem `GetJobStatusesQuery`; handler
   (Shared.Application) czyta przez port `IJobStatusReader` (Shared.Infrastructure). Zwraca każde
   zarejestrowane zadanie: klucz, harmonogram, `isRunning`, ostatni przebieg (status, start, koniec,
   opis błędu), ostatni sukces, liczbę kolejnych błędów i najbliższą dozwoloną próbę. Zadanie bez
   wiersza w bazie pojawia się bez przebiegów.
2. `isRunning` = istnieje ważna dzierżawa (`LeaseExpiresAtUtc` > teraz). Po awarii procesu zadanie
   przestaje być „w toku” najpóźniej po wygaśnięciu dzierżawy, choć jego przebieg ma `running` do
   czasu oznaczenia jako `abandoned`.
3. SF-14, SF-16 i SF-19 wysyłają to samo zapytanie przez `IDispatcher` (kontrakt w Shared.Application),
   żeby pokazać ostrzeżenie przy wynikach, np. stan „Synchronizacja trwa” w INT-01.

## Data / API

### Storage

Nowy schemat `scheduling` w tej samej bazie, właściciel: `SchedulingDbContext`
(`StoneFlow.Shared.Infrastructure`). Pierwsza migracja `InitialScheduling`, dopisana na końcu
`deploy/scripts/contexts.sh`. Zmiana addytywna, zgodna z poprzednią wersją API.

`scheduling.Jobs`

| Kolumna | Typ | Uwagi |
|---|---|---|
| `JobKey` | `nvarchar(100)` | PK |
| `RegisteredAtUtc` | `datetime2` | pierwsze sprawdzenie zadania |
| `LastSuccessStartedAtUtc` | `datetime2` null | start ostatniego udanego przebiegu |
| `LastAttemptStartedAtUtc` | `datetime2` null | |
| `ConsecutiveFailures` | `int` | domyślnie 0 |
| `NextAttemptAtUtc` | `datetime2` null | odstęp po błędzie |
| `LeaseRunId` | `uniqueidentifier` null | właściciel dzierżawy = Id przebiegu |
| `LeaseExpiresAtUtc` | `datetime2` null | |

`scheduling.JobRuns`

| Kolumna | Typ | Uwagi |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `JobKey` | `nvarchar(100)` | FK → `Jobs`, indeks (`JobKey`, `StartedAtUtc` desc) |
| `Trigger` | `nvarchar(20)` | kod: `startup`, `timer`, `manual` |
| `DueAtUtc` | `datetime2` | `ostatni termin`; dla `manual` chwila startu |
| `StartedAtUtc` | `datetime2` | |
| `FinishedAtUtc` | `datetime2` null | |
| `Status` | `nvarchar(20)` | kod: `running`, `succeeded`, `failed`, `abandoned` |
| `Error` | `nvarchar(1000)` null | opis błędu |

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|
| GET | `/api/jobs` | — | `200` `JobStatusDto[]`; `401` bez sesji | sesja właściciela (domyślna polityka) | wszystkie zarejestrowane zadania |
| GET | `/health` | bez zmian | bez zmian | anonimowy, bez zmian | wywoływany przez harmonogram hostingu |

### Types crossing a boundary

- `IScheduledJob` (Shared.Application): `string Key`, `JobSchedule Schedule`, `TimeSpan MaxDuration`,
  `Task RunAsync(JobRunContext context, CancellationToken ct)`. Zadanie musi przerwać pracę i nie
  pisać po anulowaniu tokenu; musi być bezpieczne do ponownego uruchomienia po przerwanym przebiegu
  (niezmiennik 4).
- `JobSchedule`: `DailyAt(TimeOnly localTime)`, strefa Europe/Warsaw; opis tekstowy np.
  `daily 23:00 Europe/Warsaw`.
- `JobTrigger`: `Startup`, `Timer`, `Manual` ↔ kody `startup`, `timer`, `manual`.
- `JobRunContext`: `Guid RunId`, `DateTimeOffset DueAt`, `DateTimeOffset? LastSuccessStartedAt`,
  `JobTrigger Trigger`. Zmiany danych finansowych zadanie audytuje z autorem systemowym
  (`AutomatedJob` albo `WooCommerceSync`) — wybiera zadanie, nie mechanizm.
- `IJobRunner.TryRunAsync(string jobKey, JobTrigger trigger, CancellationToken ct)` →
  `JobRunOutcome` (`Completed` ze statusem przebiegu, `AlreadyRunning`, `UnknownJob`).
- `GetJobStatusesQuery` → `IReadOnlyList<JobStatusDto>`; `JobStatusDto` (OpenAPI): `key`, `schedule`,
  `isRunning`, `lastRun` (`status`, `startedAtUtc`, `finishedAtUtc`, `error`) lub `null`,
  `lastSuccessStartedAtUtc`, `consecutiveFailures`, `nextAttemptAtUtc`. Znaczniki ISO 8601 UTC.
- Konfiguracja `Scheduling:Enabled` (domyślnie `true`), `Scheduling:TimerInterval` (domyślnie
  `00:05:00`, walidowane: od 1 min do 15 min). Brak sekretów, więc start produkcyjny nie wymaga nowej
  konfiguracji i istniejące testy integracyjne startują bez zmian.

## Invariant impact

| Invariant | How it is respected |
|---|---|
| 1. Sales separate from cash | Mechanizm nie tworzy ani nie zmienia sprzedaży, wpłat ani wydatków. |
| 2. Backend decimal money | Mechanizm nie liczy kwot. |
| 3. API-enforced access | Jedyny nowy endpoint `GET /api/jobs` wymaga sesji (domyślna polityka). Nie ma nowego endpointu anonimowego; `GET /health` się nie zmienia i niczego nie wyzwala. Uruchomienie na żądanie jest dostępne tylko z kodu (`IJobRunner`); endpoint dla użytkownika doda SF-13 z autoryzacją. |
| 4. Retries never duplicate | Dzierżawa z fencingiem wyklucza równoległy przebieg i nadpisanie stanu przez spóźniony przebieg; nadrobienie to jedno uruchomienie. Idempotencja skutków (źródło + zewnętrzne ID, definicja + okres) należy do SF-13 i SF-16, a kontrakt `IScheduledJob` to wymaga. |
| 5. Financial audit trail | Mechanizm nie zmienia danych finansowych, więc nie pisze audytu. |
| 6. Secrets stay server-side | Brak sekretu wyzwalacza. Opis błędu bez stosu i danych żądań; logi z kluczem zadania i Id przebiegu. |
| 7. Module and layer boundaries | Kontrakt w `Shared.Application` (bez EF Core i HTTP), implementacja w `Shared.Infrastructure` z własnym schematem; moduły nie znają tabel `scheduling`; Shared nie zawiera logiki modułów. |
| 8. No identifying data in docs | Instrukcja używa `<adres-api>`; brak nazwy sklepu i domeny. |

## Risk

`medium`, bez zmian: mechanizm jest współdzielony przez SF-13 i SF-16, a jego błąd (brak uruchomienia,
podwójny przebieg) przenosi się na import zamówień i koszty. Sam nie zmienia pieniędzy ani danych
klientów. Przegląd kodu musi uwzględnić obu konsumentów.

## UI

Not applicable. Ostrzeżenia o niepowodzeniu zadań dodadzą SF-14, SF-16 i SF-19.

## Edge Cases

- **Uśpienie przez termin:** proces śpi o 23:00, GET `/health` o 23:10 go budzi → sprawdzenie startowe
  uruchamia zadanie raz.
- **Kilka dni przerwy:** pominięte trzy terminy → jedno uruchomienie, `DueAt` = ostatni termin,
  `LastSuccessStartedAt` sprzed przerwy.
- **Nowe zadanie po wdrożeniu o 10:00:** nie rusza do 23:00 tego dnia.
- **Restart w trakcie przebiegu:** dzierżawa wygasa; następne sprawdzenie oznacza stary przebieg jako
  `abandoned` i uruchamia zadanie od razu, bez odstępu.
- **Nakładanie procesów przy recyklingu:** stara i nowa instancja sprawdzają jednocześnie → jedna
  przejmuje dzierżawę, druga pomija zadanie.
- **Spóźniony przebieg po utracie dzierżawy:** jego zakończenie nie zmienia `Jobs` ani cudzej
  dzierżawy.
- **Przebieg dłuższy niż takt timera:** kolejne sprawdzenia w tym procesie czekają (jedna pętla);
  w innym procesie pomijają zadanie (dzierżawa).
- **Przekroczenie czasu:** anulowanie, `failed`, odstęp 15 min; zadanie ignorujące anulowanie zostanie
  `abandoned` po wygaśnięciu dzierżawy.
- **Błąd za błędem:** próby co 15 min, potem co 1 h, aż do sukcesu; sukces zeruje licznik.
- **Sukces ręczny przed terminem dnia** (np. o 22:50): o 23:00 zadanie jest znów należne.
- **Ręczne uruchomienie w trakcie przebiegu z harmonogramu:** `AlreadyRunning`.
- **Zmiana czasu:** 23:00 lokalnie zimą i latem. Godzina z przerwy wiosennej (02:00–02:59) przesuwa
  się na pierwszą istniejącą chwilę po niej; godzina powtórzona jesienią liczy się przy pierwszym
  wystąpieniu.
- **Brak bazy przy sprawdzeniu:** log błędu, kolejna próba przy następnym takcie; aplikacja działa.
- **Brak zarejestrowanych zadań:** sprawdzenie nie łączy się z bazą.
- **Dwa zadania należne naraz:** uruchamiają się po kolei; drugie rusza najpóźniej ok. 32 min po
  pierwszym (`MaxDuration` + zapas).
- **Zamykanie aplikacji w trakcie przebiegu:** `failed` „Przerwano przy zamykaniu aplikacji” bez
  odstępu albo, gdy zapis się nie uda, później `abandoned`.

## Dependencies

- SF-1 — wynik weryfikacji hostingu. D-08 pkt 2 zostaje otwarty do ręcznej próby na hostingu.
- SF-3 — monorepo, CI, szkielet modułów (status `done`).
- Korzysta z istniejących: `TimeProvider`, `UseModuleSqlServer`, CQRS (`IDispatcher`,
  `AddHandlersFromAssembly`), uwierzytelnianie z SF-6, `/health`, `deploy/scripts/contexts.sh` z SF-4.

## Assumptions

- **Pytanie:** co uruchamia zadania? **Odpowiedź (2026-11-05):** GET z harmonogramu hostingu oraz timer
  w procesie; oba tylko sprawdzają w SQL, co jest należne. **Zmiana w wersji 2:** zamiast osobnego
  endpointu z tokenem w ścieżce harmonogram hostingu wywołuje istniejący `GET /health`. Powód: token
  w URL trafiałby do logów IIS (niezmiennik 6), a wyzwolenie nie musi być uwierzytelnione, bo samo
  żądanie tylko budzi proces, a sprawdzenie wykonuje start aplikacji i timer. Intencja odpowiedzi
  (GET z hostingu + timer) jest zachowana.
- **Pytanie:** jak widać niepowodzenie zadania? **Odpowiedź:** każdy przebieg zapisany w SQL i
  uwierzytelniony endpoint z ostatnim przebiegiem i sukcesem; UI dodadzą SF-14, SF-16, SF-19.
- **Pytanie:** nadrabianie i niepowodzenie? **Odpowiedź:** jedno uruchomienie przy najbliższym
  sprawdzeniu, zaległości obsługuje zadanie; po błędzie ponowienie z odstępem (15 min, potem 1 h).
- **Pytanie:** zakres poza mechanizmem? **Odpowiedź:** testy z zadaniem testowym i instrukcja
  w `docs/wdrozenia.md`; konfiguracja panelu i dowód dla D-08 pkt 2 to osobny krok ręczny.
- Harmonogram hostingu co 15 min podtrzymuje proces, jeśli limit bezczynności puli IIS jest dłuższy
  niż 15 min (domyślnie w IIS 20 min; na koncie niepotwierdzone). Przy krótszym limicie przebieg może
  zostać przerwany uśpieniem — wtedy dzierżawa wygaśnie, przebieg będzie `abandoned`, a zadanie ruszy
  po przebudzeniu. Do potwierdzenia w próbie D-08 pkt 2, razem z tym, czy przebieg do `MaxDuration`
  30 min kończy się bez uśpienia.
- Mechanizm w `StoneFlow.Shared.Infrastructure` z własnym `SchedulingDbContext` i schematem
  `scheduling`, choć dotąd tabele miały tylko moduły (audyt: kontrakt w Shared.Application, tabela
  w Reporting). Powód: harmonogram nie jest logiką żadnego modułu, a umieszczenie go w Integrations
  lub Expenses wiązałoby drugiego konsumenta z cudzym modułem. Wymaga osobnego testu architektury
  (`DbContextSchemaTests` jest sparametryzowany listą modułów) i zapisu w `conventions.md`.
- Handler `GetJobStatusesQuery` leży w Shared.Application i jest rejestrowany
  `AddHandlersFromAssembly(Shared.Application)` w `AddSharedInfrastructure`; endpoint w hoście woła
  tylko `IDispatcher`.
- Jedyny obsługiwany harmonogram to dzienny o stałej godzinie Europe/Warsaw — wystarcza SF-13 (23:00);
  SF-16 wybierze własną godzinę.
- Wartości techniczne przyjęte w specyfikacji, nie z PRD: takt timera 5 min, `MaxDuration` 30 min,
  zapas dzierżawy 2 min, odstępy 15 min i 1 h, dwa kolejne błędy przedłużenia oznaczają utratę.
- Zadania w jednym sprawdzeniu biegną po kolei, nie równolegle — przy dwóch zadaniach dziennych
  opóźnienie drugiego jest ograniczone `MaxDuration` pierwszego.
- Dzierżawa w tabeli, a nie `sp_getapplock`: blokada sesyjna znika z połączeniem, a przebieg zadania
  nie trzyma połączenia runnera przez cały czas.
- Historia przebiegów bez retencji: przy jednym–dwóch zadaniach dziennych przyrost jest pomijalny.

## Acceptance Criteria

- [ ] Zadanie należne przed restartem aplikacji uruchamia się po restarcie (nowy host na tej samej
      bazie), bez ręcznej akcji.
- [ ] Zadanie, którego termin minął bez żadnego sprawdzenia (proces uśpiony), uruchamia się dokładnie
      raz przy pierwszym sprawdzeniu po przerwie, także po kilku pominiętych terminach.
- [ ] Zadanie nie uruchamia się drugi raz w tym samym okresie po udanym przebiegu; nowe zadanie nie
      rusza przed swoim pierwszym terminem.
- [ ] Równoczesne próby uruchomienia (dwa runnery na tej samej bazie, pętla i `IJobRunner`) dają jeden
      przebieg; pozostałe dostają `AlreadyRunning` lub pomijają zadanie.
- [ ] Spóźnione zakończenie przebiegu, którego dzierżawę przejęto, nie zmienia stanu zadania ani cudzej
      dzierżawy; utrata dzierżawy anuluje token zadania.
- [ ] Przebieg z wygasłą dzierżawą jest oznaczany jako `abandoned` przy następnym przejęciu, a zadanie
      uruchamia się od razu, bez odstępu.
- [ ] Wyjątek zadania z pętli zapisuje `failed` z opisem i ustawia ponowienie po 15 min, a przy kolejnym
      błędzie po 1 h; sukces zeruje licznik. Wyjątek przebiegu ręcznego nie ustawia odstępu.
- [ ] Przekroczenie `MaxDuration` anuluje token zadania i kończy przebieg jako `failed`.
- [ ] `GET /api/jobs` bez sesji zwraca `401`, z sesją pokazuje ostatni przebieg, błąd, ostatni sukces
      i `isRunning` każdego zadania.
- [ ] Termin 23:00 Europe/Warsaw jest liczony poprawnie w czasie zimowym, letnim i w dni zmiany czasu.
- [ ] Bez zarejestrowanych zadań aplikacja startuje i działa bez bazy jak dotąd (istniejące testy
      integracyjne przechodzą bez zmian konfiguracji).
- [ ] Migracja `scheduling` jest w `contexts.sh` i przechodzi w CI; test architektury dla
      `SchedulingDbContext` i testy zależności warstw przechodzą.
- [ ] `docs/wdrozenia.md` opisuje harmonogram w panelu (GET `<adres-api>/health` co 15 min) i odczyt
      `GET /api/jobs`; `conventions.md` opisuje nowe zasady.

## Verification

- build: `dotnet build stone-flow.slnx` — kompilacja bez błędów.
- tests:
  - jednostkowe (`StoneFlow.Shared.Tests`): wyliczanie `ostatniego` i `pierwszego terminu`
    w Europe/Warsaw (zima, lato, dni zmiany czasu), należność, odstępy ponowień, obcinanie opisu błędu;
  - integracyjne (`StoneFlow.Api.IntegrationTests`, SQL Server, `Scheduling:Enabled=false`,
    `FakeTimeProvider`, zadanie testowe): restart (drugi host na tej samej bazie), uśpienie
    (przesunięcie zegara o kilka terminów i jedno sprawdzenie), równoległość (dwa runnery naraz),
    porzucenie (wygasła dzierżawa), fencing (spóźnione zakończenie), błąd i odstęp, przekroczenie czasu,
    przebieg ręczny, `GET /api/jobs` `401`/`200`; jeden test z `Scheduling:Enabled=true` potwierdza
    sprawdzenie przy starcie;
  - architektury (`StoneFlow.Architecture.Tests`): schemat `scheduling` dla `SchedulingDbContext`,
    zależności warstw Shared.
- manual (po wdrożeniu, poza zakresem SF-11, dowód do D-08 pkt 2): ustawić harmonogram w panelu,
  sprawdzić w `GET /api/jobs` przebiegi po recyklingu puli i po nocy bez ruchu.
