# Code review — SF-3 (part a)

## Summary

- Overall result: PASS WITH ISSUES
- Justification: Część a dostarcza cały szkielet backendu z planu: solution, przypięte wersje, 4 moduły × 4 warstwy, `DbContext` per schemat z własną historią migracji, `/health`, Problem Details z `traceId`, korelację logów, OpenAPI tylko w Development i walidowaną datę uruchomienia. Żaden niezmiennik nie jest naruszony, a granice warstw są pilnowane zarówno testami NetArchTest, jak i testami plików `.csproj`. Pozostają odchylenia od planu i drobne problemy poprawności (sztywne czyszczenie `detail` we wszystkich błędach poza Development, `TargetInvocationException` w dyspozytorze, port SQL Server wystawiony na wszystkie interfejsy), które nie blokują merge. Restore, format i build przechodzą; 75 z 77 testów przechodzi, a 2 testy bazy danych (Testcontainers) nie zostały wykonane w przeglądzie z powodu braku Dockera, więc wymagania „migracje na pustej bazie” i „`/health` 200 przy dostępnej bazie” nie są potwierdzone uruchomieniem.
- Risk: medium

Zakres przeglądu: commit `e264270` (część a). Pliki części a nie zmieniły się w późniejszych commitach gałęzi `sf-3c-ci`.

## Project Invariant Violations

| Invariant | File:line | What is wrong |
|---|---|---|

None.

Kontrola niezmienników:

| Niezmiennik | Status | Uwagi |
|---|---|---|
| 1. Sprzedaż oddzielona od gotówki | Nie dotyczy | Brak logiki finansowej. |
| 2. Kwoty decimal na backendzie | Nie dotyczy | Brak kwot. |
| 3. Dostęp wymuszany przez API | OK | Jedyne endpointy: anonimowy `/health` bez danych (`api/src/StoneFlow.Api/Program.cs:40`) i OpenAPI tylko w Development (`Program.cs:35-38`); moduły nie mapują endpointów. |
| 4. Ponowienia bez duplikatów | Nie dotyczy | Brak importu i zadań cyklicznych. |
| 5. Audyt zmian finansowych | Nie dotyczy | Brak danych finansowych. |
| 6. Sekrety po stronie serwera | OK | `appsettings.json` i `appsettings.Development.json` bez connection stringa; `deploy/.env` ignorowany (`.gitignore:11-13`), w repozytorium tylko placeholdery (`deploy/.env.example:2`, `deploy/api.env.example:3-4`); health check loguje tylko typ wyjątku (`Health/SqlServerHealthCheck.cs:36`); Problem Details bez szczegółów poza Development. Hasło `unused` w `StoneFlowApiFactory.cs:15` to atrapa testowa do nieosiągalnego portu. |
| 7. Granice modułów i warstw | OK | Referencje zgodne z tabelą spec; wymuszone przez `LayerDependencyTests`, `ModuleBoundaryTests`, `ProjectReferenceTests` (dodatkowy test plików projektu łapie nieużywane referencje) i `DbContextSchemaTests`. |
| 8. Brak danych identyfikujących | OK | Pliki przykładowe i komentarze bez nazwy handlowej, domeny, danych klientów. |

## Conventions Violations

### Critical (must fix before merge)

None.

### Non-Critical (should fix)

1. `api/src/Shared/StoneFlow.Shared.Infrastructure/StoneFlow.Shared.Infrastructure.csproj:3-5` — `FrameworkReference Microsoft.AspNetCore.App` w części wspólnej. Plan (sekcja 3) przewiduje tylko referencje do `Shared.Application` i EF Core SqlServer. Przez to każdy `*.Infrastructure` modułu przechodnio dostaje cały ASP.NET Core, co jest sprzeczne z zasadą „część wspólna mała” i otwiera drogę do użycia typów HTTP w infrastrukturze modułów. Potrzebne API (`BindConfiguration`, `IConfiguration`) daje pakiet `Microsoft.Extensions.Options.ConfigurationExtensions`.
2. `api/tests/StoneFlow.Architecture.Tests/ProjectReferenceTests.cs:33-40` — test infrastruktury modułu sprawdza tylko `ProjectReference`; nie sprawdza `PackageReference` ani `FrameworkReference`. W połączeniu z punktem 1 nic nie zatrzyma dodania `FrameworkReference Microsoft.AspNetCore.App` do infrastruktury modułu. Pozostałe testy w tym pliku sprawdzają wszystkie trzy rodzaje referencji.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| `stone-flow.slnx` ze wszystkimi projektami z tabeli | Covered | `stone-flow.slnx:1-48`; 23 projekty `src`+`tests`. Otwarcie w Visual Studio nie było sprawdzane w przeglądzie. |
| Przypięte SDK (`global.json`, `rollForward: latestPatch`) | Covered | `global.json:2-5`. |
| Przypięty runtime 10.0.14 | Partial | Pakiety `10.0.14` (`Directory.Packages.props:10-17`) i `dotnet-ef` 10.0.14; jawne sprawdzenie runtime zostawione części c (plan, sekcja 10). |
| Centralne wersje NuGet + lockfile + locked mode w CI | Covered | `Directory.Packages.props:4-6`, `Directory.Build.props:14-15`, `packages.lock.json` per projekt. |
| `Directory.Build.props` (`net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`) | Covered | `Directory.Build.props:4-10`. |
| `.editorconfig` | Covered | `.editorconfig:1-76`. |
| Moduły × 4 warstwy, referencje zgodne z tabelą | Covered | Wszystkie `.csproj` modułów; testy `ProjectReferenceTests`. |
| CQRS: interfejsy i dyspozytor bez MediatR, wyjątek przy braku handlera | Covered | `Shared.Application/Cqrs/*`, `Shared.Infrastructure/Cqrs/Dispatcher.cs:27-30`. |
| `DbContext` per moduł, własny schemat, własna historia migracji, migracja początkowa | Covered | `*DbContext.cs:7-11`, `ModuleDbContextOptions.cs:24-26`, `*_InitialSchema.cs:13`. |
| Migracje stosują się na pustej bazie | Covered | `DatabaseTests.All_module_migrations_apply_to_empty_database` (`DatabaseTests.cs:31-71`). |
| Brak migracji przy starcie | Covered | `Program.cs` nie wywołuje `Migrate()`. |
| `/health` 200 `Healthy` / 503 `Unhealthy`, anonimowy, bez szczegółów | Covered | `Program.cs:20,40`, `SqlServerHealthCheck.cs:14-38`; testy w `HealthEndpointTests.cs` i `DatabaseTests.cs:19-29`. |
| Data uruchomienia: `DateOnly`, `StoneFlow:LaunchDate`, `yyyy-MM-dd`, walidacja przy starcie, bez ścieżki zapisu | Covered | `LaunchDateOptionsValidator.cs:8-30`, `DependencyInjection.cs:16-20` (`ValidateOnStart`), `ILaunchDate.cs:7-10` (tylko getter). |
| Brak daty w `appsettings.json`; przykładowa data w Development | Covered | `appsettings.json:1-10`, `appsettings.Development.json:8-10`; test `Base_appsettings_has_no_launch_date`. |
| Korelacja: `TraceId` w zakresie logów, `X-Correlation-ID`, `traceId` w Problem Details; `traceparent` | Covered | `CorrelationIdMiddleware.cs:16-35`, `ProblemDetailsSetup.cs:12-16`; testy w `ProblemDetailsAndCorrelationTests.cs`. |
| Logi JSON na konsolę przez `ILogger` | Covered | `Program.cs:15-16`. |
| Problem Details dla wyjątków i 4xx/5xx bez treści, bez stack trace poza Development | Covered | `Program.cs:32-33`, `ProblemDetailsSetup.cs:18-22`; patrz Correctness 1. |
| OpenAPI pod `/openapi/v1.json` tylko w Development | Covered | `Program.cs:19,35-38`; `OpenApiTests`. |
| `deploy/docker-compose.yml` z SQL Server, hasło z ignorowanego `.env` | Covered | `deploy/docker-compose.yml:5-14`, `deploy/.env.example`. |
| Przykładowa konfiguracja API bez sekretów | Covered | `deploy/api.env.example:3-4`. |
| Testy: architektury, Shared, integracyjne | Covered | Wszystkie trzy projekty obecne; patrz Test Gaps. |
| README, CI, aktualizacja `.sdd/context` | — | Poza częścią a (część c). |

## Plan Deviations

1. `api/tests/StoneFlow.Api.IntegrationTests/MigrationTests.cs` (plan, sekcja 3) nie istnieje. Test migracji oraz `Healthy_when_database_available` są w `DatabaseTests.cs:19-71` (jedna klasa w kolekcji SQL Server). Zakres testów zachowany.
2. Migracje `InitialSchema` mają puste `Down` z komentarzem (np. `Orders/.../20261103163537_InitialSchema.cs:17-20`); plan (krok 8) zakładał `DropSchema` w `Down`. Uzasadnienie w komentarzu jest poprawne (tabela historii leży w tym schemacie), ale różni się od planu.
3. `Microsoft.EntityFrameworkCore.Design` jest w hoście (`api/src/StoneFlow.Api/StoneFlow.Api.csproj:10-13`), a nie w `StoneFlow.{Modul}.Infrastructure` (plan, sekcja 3). Działa, bo `dotnet ef` używa projektu startowego.
4. `StoneFlow.Shared.Infrastructure` ma `FrameworkReference Microsoft.AspNetCore.App` (`StoneFlow.Shared.Infrastructure.csproj:3-5`), nieprzewidziane w planie (patrz Conventions 1).
5. Dodatkowe pliki spoza planu: `.gitattributes`, `api/tests/Directory.Build.props`, `ProjectReferenceTests.cs`, `Modules.cs`, `RepositoryRoot.cs` (×2), `ThrowingEndpointStartupFilter.cs`, pakiet `Microsoft.Extensions.Diagnostics.Testing` (`Directory.Packages.props:18`). Wszystkie wspierają zakres planu.
6. Nazwy testów `HealthEndpointTests.Healthy_when_database_available` i `Body_contains_only_status` z planu (sekcja 8) — pierwszy jest jako `DatabaseTests.Health_is_healthy_when_database_available`, drugi pokrywa `Assert.Equal("Healthy", …)` w tym samym teście i `Unhealthy_response_has_no_details`.

## Null Safety Issues

1. `api/src/StoneFlow.Api/Observability/CorrelationIdMiddleware.cs:22` — `(Activity.Current ?? fallbackActivity)!`: operator `!` bez komentarza. Logicznie bezpieczny (gdy `Activity.Current` jest null, `fallbackActivity` jest uruchomiona), ale warto to zapisać bez `!` (np. `var activity = Activity.Current ?? fallbackActivity ?? throw …`).
2. `api/src/Shared/StoneFlow.Shared.Infrastructure/Cqrs/Dispatcher.cs:41` — `(Task<TResult>)method.Invoke(...)!`: handler zwracający `null` zamiast `Task` da `NullReferenceException` przy `await` po stronie wywołującego, bez wskazania handlera. Mało prawdopodobne przy `Nullable` włączonym, ale bez obsługi.

## Correctness Issues

1. `api/src/StoneFlow.Api/Observability/ProblemDetailsSetup.cs:18-22` — poza Development czyszczone jest `Detail` każdej odpowiedzi Problem Details, nie tylko tej z nieobsłużonego wyjątku. Spec (Zachowanie 7) i plan (krok 10) wymagają ukrycia szczegółów wyjątku. Gdy kolejne zadania (SF-6, SF-8) zaczną zwracać 400/404/409 z `detail` dla użytkownika (np. `Results.Problem(detail: …)`), w Production `detail` zniknie bez śladu. Dla zadania o ryzyku medium, którego wzorce przejmą wszystkie moduły, to pułapka dla konsumentów; warunek powinien dotyczyć tylko odpowiedzi z wyjątkiem (`context.Exception is not null`).
2. `api/src/Shared/StoneFlow.Shared.Infrastructure/Cqrs/Dispatcher.cs:38-41` — wywołanie przez `MethodInfo.Invoke`: handler, który rzuci wyjątek synchronicznie (np. nie-`async` metoda z `ArgumentException` przed zwróceniem `Task`), dociera do wywołującego jako `TargetInvocationException`, a nie oryginalny wyjątek. To zmieni mapowanie błędów na Problem Details w przyszłych endpointach. Brak testu na ten przypadek.
3. `api/src/Shared/StoneFlow.Shared.Infrastructure/Cqrs/HandlerRegistration.cs:22-25` — dwa handlery tej samej komendy rejestrują się bez błędu; `GetService` zwróci ostatni. Ciche nadpisanie zamiast błędu przy starcie.
4. `deploy/docker-compose.yml:11-12` — `"1433:1433"` wystawia SQL Server z kontem SA na wszystkich interfejsach hosta (także w sieci lokalnej). Dla lokalnej bazy deweloperskiej wystarczy `"127.0.0.1:1433:1433"`.
5. `api/src/StoneFlow.Api/Health/SqlServerHealthCheck.cs:34` — filtr łapie tylko `SqlException`, `InvalidOperationException`, `ArgumentException`. Inne wyjątki (np. `OperationCanceledException` przy przerwanym żądaniu, `Win32Exception`) przechodzą do `HealthCheckService`, który i tak zwraca `Unhealthy`, ale loguje pełny wyjątek z komunikatem (zawiera nazwę serwera). Nie narusza niezmiennika 6 (bez hasła), ale jest niespójne z intencją logowania samego typu.

## Test Gaps

Weryfikacja uruchomiona w przeglądzie (SDK 10.0.403, runtime 10.0.14, 2026-11-03):

| Polecenie | Wynik |
|---|---|
| `dotnet tool restore` | przechodzi (`dotnet-ef` 10.0.14) |
| `dotnet restore stone-flow.slnx --locked-mode` | przechodzi |
| `dotnet format stone-flow.slnx --verify-no-changes --no-restore` | przechodzi |
| `dotnet build stone-flow.slnx -c Release --no-restore` | przechodzi, 0 ostrzeżeń |
| `dotnet test --solution stone-flow.slnx -c Release --no-build` | 75/77 przechodzi; `Architecture.Tests` i `Shared.Tests` w całości zielone; 2 testy `DatabaseTests` (`Health_is_healthy_when_database_available`, `All_module_migrations_apply_to_empty_database`) zakończone `DockerUnavailableException` — Docker niedostępny na maszynie przeglądu. To ograniczenie środowiska, nie dowód błędu, ale te dwa testy **nie** zostały potwierdzone. |

Uwaga środowiskowa: SDK 10.0.403 jest zainstalowany tylko w `%LOCALAPPDATA%\Microsoft\dotnet`; globalny `dotnet` (10.0.400) odrzuca `global.json`. Zgodne z zamierzeniem przypięcia; instrukcja instalacji należy do README (część c).

Luki:


1. Brak testu, że dyspozytor przekazuje oryginalny wyjątek handlera (Correctness 2).
2. Brak testu, że Problem Details 4xx z jawnie ustawionym `detail` zachowuje go poza Development (Correctness 1) — dziś test sprawdza tylko brak `detail` przy wyjątku (`ProblemDetailsAndCorrelationTests.cs:42-55`).
3. `ProjectReferenceTests.Infrastructure_references_only_own_application_and_shared_infrastructure` (`ProjectReferenceTests.cs:33-40`) nie sprawdza pakietów ani referencji frameworka (Conventions 2).
4. `CorrelationIdMiddleware` — brak testu ścieżki z `Activity.Current == null` (`CorrelationIdMiddleware.cs:18-20`); w hoście ASP.NET Core aktywność zwykle istnieje, więc gałąź nie jest wykonywana w testach.

## Code Smells

1. `api/tests/StoneFlow.Architecture.Tests/RepositoryRoot.cs:1-21` i `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/RepositoryRoot.cs:1-21` — identyczna klasa w dwóch projektach testowych. Akceptowalne przy dwóch kopiach, ale warto rozważyć link do wspólnego pliku.
2. `api/src/Modules/*/StoneFlow.*.Infrastructure/DependencyInjection.cs:11-20` — cztery identyczne bloki rejestracji różniące się typem kontekstu. Zgodne z planem i czytelne; uwaga tylko na przyszłość, gdy dojdzie więcej wspólnej konfiguracji.

## Recommendation

- Merge as-is (po potwierdzeniu dwóch testów `DatabaseTests` na maszynie z Dockerem lub w CI); poprawki z sekcji Correctness 1–2 i Conventions 1–2 warto zrobić przed pierwszym zadaniem z endpointem modułu (SF-6/SF-8).
