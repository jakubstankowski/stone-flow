# Implementation Plan — SF-3 (część a)

## 0. Część

- **Litera:** a — backend.
- **Dostarcza:** solution `stone-flow.slnx`, przypięte wersje .NET (SDK, pakiety, narzędzie
  `dotnet-ef`), projekty backendu (host, część wspólna, cztery moduły × cztery warstwy),
  EF Core z migracją początkową per moduł, `/health`, Problem Details, korelację logów,
  OpenAPI, odczyt daty uruchomienia, lokalny SQL Server w `docker-compose`, przykładową
  konfigurację bez sekretów oraz testy backendu.
- **Wymaga wcześniejszego merge:** brak. Część b jest niezależna; część c wymaga a i b.

## 1. Zakres

Część tworzy od zera cały backend z `02-spec.md` (repozytorium nie zawiera kodu). Dotyka
warstw domeny, aplikacji, infrastruktury i API wszystkich czterech modułów (tylko szkielety,
bez encji i endpointów biznesowych), części wspólnej i hosta. Nie obejmuje CI, README ani
aktualizacji `.sdd/context` (część c) ani aplikacji Angular (część b).

## 2. Obszary

| Warstwa | Moduł / funkcja | Dlaczego |
|---|---|---|
| Repozytorium | katalog główny | `stone-flow.slnx`, `global.json`, `Directory.*.props`, `.editorconfig`, `.gitignore`, manifest narzędzi |
| API (host) | `StoneFlow.Api` | kompozycja modułów, OpenAPI, Problem Details, korelacja, logi, `/health` |
| Wspólne | `StoneFlow.Shared.Kernel` / `.Application` / `.Infrastructure` | abstrakcje CQRS i dyspozytor, data uruchomienia, wspólna konfiguracja providera EF Core |
| Domena, aplikacja | `StoneFlow.{Orders,Expenses,Reporting,Integrations}.Domain` / `.Application` | puste warstwy z granicami wymuszonymi referencjami |
| Infrastruktura | `StoneFlow.{Modul}.Infrastructure` | `DbContext` w schemacie modułu, migracja początkowa, rejestracja |
| API modułu | `StoneFlow.{Modul}.Api` | metoda rejestracji modułu (bez endpointów) |
| Testy | `api/tests/*` | architektura, część wspólna, integracja hosta z SQL Server |
| Wdrożenia (lokalne) | `deploy/` | `docker-compose` z SQL Server, przykładowe zmienne |

## 3. Pliki

`{Modul}` ∈ `Orders`, `Expenses`, `Reporting`, `Integrations`; `{schemat}` ∈ `orders`,
`expenses`, `reporting`, `integrations`. Wiersz z `{Modul}` oznacza cztery pliki.

| Ścieżka | Akcja | Cel |
|---|---|---|
| `stone-flow.slnx` | create | solution z folderami `api/src/Shared`, `api/src/Modules/{Modul}`, `api/tests` |
| `global.json` | create | SDK `10.0.403`, `rollForward: latestPatch`; `test.runner: Microsoft.Testing.Platform` |
| `Directory.Build.props` | create | `net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel=latest`, `RestorePackagesWithLockFile`, `RestoreLockedMode` gdy `CI=true` |
| `Directory.Packages.props` | create | `ManagePackageVersionsCentrally`; dokładne stabilne wersje: `Microsoft.AspNetCore.OpenApi`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.Data.SqlClient`, `xunit.v3`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql`, `NetArchTest.Rules` |
| `.config/dotnet-tools.json` | create | lokalne narzędzie `dotnet-ef` w wersji zgodnej z EF Core |
| `.editorconfig` | create | styl C# i ważność reguł egzekwowanych przez `dotnet format` i build |
| `.gitignore` | create | `bin/`, `obj/`, `.vs/`, `*.user`, `TestResults/`, `deploy/.env`, `deploy/*.env` poza `*.example` |
| `deploy/docker-compose.yml` | create | `mcr.microsoft.com/mssql/server` z dokładnym tagiem 2022, hasło SA z `deploy/.env`, port 1433, wolumen |
| `deploy/.env.example` | create | `MSSQL_SA_PASSWORD=<ustaw-silne-haslo>` (placeholder) |
| `deploy/api.env.example` | create | wzór zmiennych API: `ConnectionStrings__StoneFlow=<...>`, `StoneFlow__LaunchDate=<yyyy-MM-dd>` (placeholdery) |
| `api/src/StoneFlow.Api/StoneFlow.Api.csproj` | create | host Web SDK, `UserSecretsId`, referencje do `*.Api`, `*.Infrastructure`, `Shared.Infrastructure` |
| `api/src/StoneFlow.Api/Program.cs` | create | kompozycja: logowanie JSON, Problem Details, korelacja, moduły, health, OpenAPI |
| `api/src/StoneFlow.Api/Observability/CorrelationIdMiddleware.cs` | create | zakres logów z `TraceId`, nagłówek `X-Correlation-ID` |
| `api/src/StoneFlow.Api/Observability/ProblemDetailsSetup.cs` | create | `AddProblemDetails` z `traceId`, bez szczegółów wyjątku poza Development |
| `api/src/StoneFlow.Api/Health/SqlServerHealthCheck.cs` | create | `SELECT 1` na `ConnectionStrings:StoneFlow` z krótkim timeoutem |
| `api/src/StoneFlow.Api/appsettings.json` | create | logowanie; **bez** `StoneFlow:LaunchDate` i bez connection stringa |
| `api/src/StoneFlow.Api/appsettings.Development.json` | create | przykładowa `StoneFlow:LaunchDate`; bez connection stringa (user-secrets) |
| `api/src/StoneFlow.Api/Properties/launchSettings.json` | create | profil `http`/`https` Development |
| `api/src/Shared/StoneFlow.Shared.Kernel/StoneFlow.Shared.Kernel.csproj` | create | projekt bez zależności |
| `api/src/Shared/StoneFlow.Shared.Kernel/AssemblyReference.cs` | create | znacznik zestawu dla testów architektury |
| `api/src/Shared/StoneFlow.Shared.Application/StoneFlow.Shared.Application.csproj` | create | referencja do `Shared.Kernel` |
| `api/src/Shared/StoneFlow.Shared.Application/Cqrs/ICommand.cs`, `IQuery.cs`, `ICommandHandler.cs`, `IQueryHandler.cs`, `IDispatcher.cs` | create | abstrakcje CQRS |
| `api/src/Shared/StoneFlow.Shared.Application/LaunchDate/ILaunchDate.cs` | create | odczyt daty uruchomienia (`DateOnly Value`), bez zapisu |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/StoneFlow.Shared.Infrastructure.csproj` | create | referencje do `Shared.Application`, EF Core SqlServer |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Cqrs/Dispatcher.cs` | create | rozwiązywanie handlera z DI, wyjątek przy braku |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Cqrs/HandlerRegistration.cs` | create | rejestracja handlerów z zestawu (refleksja, bez Scrutor) |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/LaunchDate/LaunchDateOptions.cs` | create | sekcja `StoneFlow`, właściwość `LaunchDate` jako string |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/LaunchDate/LaunchDateOptionsValidator.cs` | create | `IValidateOptions`: wymagane, `yyyy-MM-dd`, `InvariantCulture`, poprawny dzień kalendarza |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/LaunchDate/ConfiguredLaunchDate.cs` | create | `ILaunchDate` z opcji, singleton |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/Persistence/ModuleDbContextOptions.cs` | create | jedyna „wspólna konfiguracja EF Core”: `UseSqlServer` z historią migracji w schemacie modułu i `MigrationsAssembly` |
| `api/src/Shared/StoneFlow.Shared.Infrastructure/DependencyInjection.cs` | create | `AddSharedInfrastructure`: dyspozytor, opcje daty z `ValidateOnStart` |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Domain/StoneFlow.{Modul}.Domain.csproj` | create | referencja tylko do `Shared.Kernel` |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Domain/AssemblyReference.cs` | create | znacznik zestawu |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Application/StoneFlow.{Modul}.Application.csproj` | create | referencje do `{Modul}.Domain`, `Shared.Application` |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Application/AssemblyReference.cs` | create | znacznik zestawu i źródło skanowania handlerów |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Infrastructure/StoneFlow.{Modul}.Infrastructure.csproj` | create | referencje do `{Modul}.Application`, `Shared.Infrastructure`, EF Core Design (`PrivateAssets=all`) |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Infrastructure/Persistence/{Modul}DbContext.cs` | create | `HasDefaultSchema("{schemat}")`, stała `Schema` |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Infrastructure/Persistence/Migrations/*_InitialSchema.cs` (+ `.Designer.cs`, `{Modul}DbContextModelSnapshot.cs`) | create | wygenerowane `dotnet ef`; `Up` z `EnsureSchema("{schemat}")` |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Infrastructure/DependencyInjection.cs` | create | `Add{Modul}Infrastructure`: `DbContext` przez `ModuleDbContextOptions`, handlery z `{Modul}.Application` |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Api/StoneFlow.{Modul}.Api.csproj` | create | `FrameworkReference Microsoft.AspNetCore.App`, referencja do `{Modul}.Application` |
| `api/src/Modules/{Modul}/StoneFlow.{Modul}.Api/{Modul}Module.cs` | create | `Add{Modul}Module` / `Map{Modul}Endpoints` (puste ciało grupy) |
| `api/tests/StoneFlow.Architecture.Tests/StoneFlow.Architecture.Tests.csproj` | create | xUnit v3, NetArchTest, referencje do wszystkich projektów `src` |
| `api/tests/StoneFlow.Architecture.Tests/LayerDependencyTests.cs` | create | reguły warstw (sekcja 8) |
| `api/tests/StoneFlow.Architecture.Tests/ModuleBoundaryTests.cs` | create | brak odwołań między modułami |
| `api/tests/StoneFlow.Architecture.Tests/DbContextSchemaTests.cs` | create | każdy kontekst mapuje tylko swój schemat i trzyma historię migracji w nim |
| `api/tests/StoneFlow.Shared.Tests/StoneFlow.Shared.Tests.csproj` | create | xUnit v3, referencja do `Shared.Infrastructure` |
| `api/tests/StoneFlow.Shared.Tests/Cqrs/DispatcherTests.cs` | create | dyspozytor |
| `api/tests/StoneFlow.Shared.Tests/LaunchDate/LaunchDateOptionsValidatorTests.cs` | create | walidacja daty |
| `api/tests/StoneFlow.Api.IntegrationTests/StoneFlow.Api.IntegrationTests.csproj` | create | xUnit v3, `Mvc.Testing`, `Testcontainers.MsSql` |
| `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/SqlServerFixture.cs` | create | kontener SQL Server na kolekcję testów |
| `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/StoneFlowApiFactory.cs` | create | `WebApplicationFactory<Program>` z konfiguracją testową i przechwytywaniem logów |
| `api/tests/StoneFlow.Api.IntegrationTests/HealthEndpointTests.cs` | create | 200/503, brak szczegółów |
| `api/tests/StoneFlow.Api.IntegrationTests/ProblemDetailsAndCorrelationTests.cs` | create | 404/500, `traceId`, `X-Correlation-ID`, `traceparent`, logi |
| `api/tests/StoneFlow.Api.IntegrationTests/LaunchDateStartupTests.cs` | create | start blokowany przy braku/złym formacie; `appsettings.json` bez daty |
| `api/tests/StoneFlow.Api.IntegrationTests/MigrationTests.cs` | create | migracje czterech kontekstów na pustej bazie |
| `api/tests/StoneFlow.Api.IntegrationTests/OpenApiTests.cs` | create | dokument w Development, brak poza nim |

## 4. Kroki implementacji

1. [ ] **Pliki repozytorium:** `.gitignore`, `.editorconfig`, `global.json`
   (`dotnet --version` = `10.0.403`), `Directory.Build.props`, `Directory.Packages.props`
   (dokładne najnowsze stabilne wersje pakietów zgodne z .NET 10, odczytane z NuGet przy
   implementacji; bez preview/RC), `.config/dotnet-tools.json` z `dotnet-ef`.
2. [ ] **Solution i projekty:** `stone-flow.slnx` i wszystkie `.csproj` z sekcji 3 z
   referencjami zgodnymi z tabelą „Może zależeć od” w spec. `dotnet restore` tworzy
   `packages.lock.json` per projekt (commitowane). `dotnet build` przechodzi.
3. [ ] **`Shared.Kernel` i znaczniki:** `AssemblyReference` w każdym projekcie `src`.
4. [ ] **CQRS:** interfejsy w `Shared.Application/Cqrs` (sygnatury poglądowe:
   `Task<TResult> SendAsync<TResult>(ICommand<TResult>, CancellationToken)`,
   `Task<TResult> QueryAsync<TResult>(IQuery<TResult>, CancellationToken)`); `Dispatcher` i
   `HandlerRegistration` w `Shared.Infrastructure`. Brak handlera → `InvalidOperationException`
   z nazwą typu komendy/zapytania. Testy z sekcji 8 (DispatcherTests) najpierw.
5. [ ] **Data uruchomienia:** `ILaunchDate`, `LaunchDateOptions`,
   `LaunchDateOptionsValidator` (wymagane; `DateOnly.TryParseExact(value, "yyyy-MM-dd",
   CultureInfo.InvariantCulture, DateTimeStyles.None)`; komunikat wskazuje klucz
   `StoneFlow:LaunchDate`, bez innych wartości konfiguracji), `ConfiguredLaunchDate`,
   rejestracja z `ValidateOnStart`. Testy walidatora najpierw.
6. [ ] **Wspólna konfiguracja EF Core:** `ModuleDbContextOptions` — rozszerzenie ustawiające
   `UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory",
   schema).MigrationsAssembly(...))`. Nic więcej w części wspólnej (odpowiedź na niejasność z
   przeglądu spec).
7. [ ] **Moduły:** dla każdego `{Modul}`: `{Modul}DbContext` z `HasDefaultSchema`,
   `DependencyInjection.Add{Modul}Infrastructure` (connection string
   `ConnectionStrings:StoneFlow`; brak connection stringa nie blokuje startu, bo `/health` ma
   wtedy zwrócić `Unhealthy`), `{Modul}Module.Add{Modul}Module`/`Map{Modul}Endpoints`.
8. [ ] **Migracje początkowe:** dla każdego kontekstu `dotnet ef migrations add InitialSchema
   --project api/src/Modules/{Modul}/StoneFlow.{Modul}.Infrastructure --startup-project
   api/src/StoneFlow.Api --context {Modul}DbContext --output-dir Persistence/Migrations`;
   dopisanie `migrationBuilder.EnsureSchema("{schemat}")` w `Up` i `DropSchema` w `Down`.
9. [ ] **Host — logowanie i korelacja:** `AddJsonConsole` z `IncludeScopes`;
   `CorrelationIdMiddleware` na początku potoku: `TraceId` z `Activity.Current` (ASP.NET Core
   tworzy aktywność z `traceparent` albo nową), `ILogger.BeginScope` z `CorrelationId`,
   nagłówek odpowiedzi `X-Correlation-ID` ustawiany w `OnStarting`. Przychodzący nagłówek
   `X-Correlation-ID` jest ignorowany (jedynym źródłem jest W3C `traceparent`).
10. [ ] **Host — Problem Details:** `AddProblemDetails` z `CustomizeProblemDetails` dodającym
    `traceId`; `UseExceptionHandler()` i `UseStatusCodePages()`; szczegóły wyjątku (`detail`,
    stack trace) tylko w Development.
11. [ ] **Host — health:** `AddHealthChecks().AddCheck<SqlServerHealthCheck>("sql")`;
    `MapHealthChecks("/health")` z domyślnym zapisem (`Healthy`/`Unhealthy`, `text/plain`),
    mapowaniem `Unhealthy → 503`, `AllowAnonymous()`. Check zwraca `Unhealthy` bez opisu i
    wyjątku przy braku connection stringa, błędzie połączenia lub timeoucie (timeout 5 s).
12. [ ] **Host — OpenAPI i moduły:** `AddOpenApi()`; `MapOpenApi()` tylko gdy
    `IsDevelopment()`; wywołania `AddSharedInfrastructure`, `Add{Modul}Infrastructure`,
    `Add{Modul}Module`, `Map{Modul}Endpoints`. `public partial class Program` dla testów.
    Brak automatycznego `Migrate()` przy starcie.
13. [ ] **Konfiguracja:** `appsettings.json` bez daty i connection stringa;
    `appsettings.Development.json` z przykładową datą; `launchSettings.json`.
14. [ ] **Lokalny SQL Server:** `deploy/docker-compose.yml`, `deploy/.env.example`,
    `deploy/api.env.example`. Hasło SA tylko w `deploy/.env` (ignorowany).
15. [ ] **Testy architektury, części wspólnej i integracyjne** z sekcji 8.
16. [ ] **Weryfikacja** z sekcji 9; `dotnet format --verify-no-changes` czysty.

## 5. Dane i kontrakty

- **Storage:** SQL Server, jedna baza `StoneFlow`. Migracja `InitialSchema` w każdym z
  czterech kontekstów tworzy schemat `{schemat}` i tabelę `{schemat}.__EFMigrationsHistory`.
  Brak tabel biznesowych.
- **Endpointy:**

  | Metoda | Ścieżka | Odpowiedź | Auth |
  |---|---|---|---|
  | GET | `/health` | `200 Healthy` / `503 Unhealthy`, `text/plain` | anonimowy |
  | GET | `/openapi/v1.json` | dokument OpenAPI; tylko Development (poza nim `404`) | anonimowy |

  Każda odpowiedź ma nagłówek `X-Correlation-ID`. Błędy: `application/problem+json` z
  `traceId`.
- **Typy na granicy:** `ILaunchDate` (`DateOnly Value`); `ICommand<TResult>`,
  `IQuery<TResult>`, `ICommandHandler<TCommand,TResult>`, `IQueryHandler<TQuery,TResult>`,
  `IDispatcher`. Konfiguracja: `StoneFlow:LaunchDate` (`yyyy-MM-dd`),
  `ConnectionStrings:StoneFlow`.

## 6. Przypadki brzegowe i null safety

| Przypadek | Gdzie obsłużony |
|---|---|
| Brak `StoneFlow:LaunchDate` | `LaunchDateOptionsValidator` + `ValidateOnStart` → host nie startuje |
| `03.11.2026`, `2026-02-30`, `2026-11-03T10:00` | `TryParseExact` z dokładnym formatem → walidacja odrzuca |
| `LaunchDate` w `appsettings.json` (przegląd, punkt 7) | plik bez klucza + test `LaunchDateStartupTests` czytający plik bazowy |
| SQL Server niedostępny przy starcie | brak `Migrate()` przy starcie; `SqlServerHealthCheck` → `503` |
| Brak connection stringa | `SqlServerHealthCheck` sprawdza null/pusty → `Unhealthy`; kontekst rejestrowany z wartością z konfiguracji, nieużywany w SF-3 |
| Żądanie z `traceparent` | `Activity` z ASP.NET Core → ten sam `TraceId` w logach, nagłówku, Problem Details |
| Przychodzący `X-Correlation-ID` | ignorowany (krok 9) |
| Nieistniejąca ścieżka | `UseStatusCodePages` → `404` Problem Details |
| Nieobsłużony wyjątek | `UseExceptionHandler` → `500` Problem Details bez szczegółów poza Development |
| Brak handlera CQRS | `Dispatcher` → `InvalidOperationException` |
| `Activity.Current` null | middleware generuje `TraceId` przez `ActivityTraceId.CreateRandom()` tylko wtedy; typ `string` nie-null w nagłówku |
| Niezgodna wersja SDK | `global.json` → `dotnet` kończy błędem |
| Domena z referencją do EF Core / moduł z referencją do innego modułu | testy architektury czerwone |

## 7. Niezmienniki

| Niezmiennik | Gdzie egzekwowany | Test |
|---|---|---|
| 3. Dostęp wymuszany przez API | jedyne endpointy: `/health` (anonimowy, bez danych), OpenAPI tylko w Development | `HealthEndpointTests.Body_contains_only_status`, `OpenApiTests.Not_exposed_outside_development` |
| 6. Sekrety po stronie serwera | connection string tylko z user-secrets/zmiennych; `.env` ignorowany; health i Problem Details bez szczegółów | `HealthEndpointTests.Unhealthy_response_has_no_details`, `ProblemDetailsAndCorrelationTests.Production_error_has_no_exception_details`; skan sekretów w CI — część c |
| 7. Granice modułów i warstw | referencje projektów; `ModuleDbContextOptions` + `HasDefaultSchema` | `LayerDependencyTests`, `ModuleBoundaryTests`, `DbContextSchemaTests` |
| 8. Brak danych identyfikujących | pliki przykładowe z placeholderami | przegląd kodu (część c dodaje skan sekretów) |

Niezmienniki 1, 2, 4, 5 — nie dotyczy (brak logiki i danych finansowych).

## 8. Testy

| Test | Typ | Co dowodzi |
|---|---|---|
| `LayerDependencyTests.Domain_does_not_depend_on_http_ef_or_hosting` | architektura | `*.Domain` bez `Microsoft.AspNetCore*`, `Microsoft.EntityFrameworkCore*`, `Microsoft.Extensions.Hosting*`, `Microsoft.Data.SqlClient` |
| `LayerDependencyTests.Application_does_not_depend_on_infrastructure_http_or_ef` | architektura | `*.Application` bez EF Core, ASP.NET Core, SqlClient i bez `*.Infrastructure` (przegląd, punkt 5) |
| `LayerDependencyTests.Shared_kernel_has_no_dependencies` | architektura | `Shared.Kernel` nie zależy od innych projektów `StoneFlow.*` ani od frameworków |
| `LayerDependencyTests.Domain_does_not_depend_on_outer_layers` | architektura | `*.Domain` bez `*.Application`, `*.Infrastructure`, `*.Api` |
| `ModuleBoundaryTests.Module_does_not_reference_other_modules` | architektura (teoria × 4) | typy `StoneFlow.{Modul}.*` nie odwołują się do `StoneFlow.{Inny}.*` |
| `DbContextSchemaTests.Context_uses_only_its_schema` | architektura (teoria × 4) | domyślny schemat kontekstu = schemat modułu; każda encja (gdy powstanie) w tym schemacie; historia migracji w tym schemacie |
| `DispatcherTests.Resolves_command_handler_and_passes_cancellation` | jednostkowy | dyspozytor wywołuje właściwy handler z tym samym `CancellationToken` |
| `DispatcherTests.Resolves_query_handler` | jednostkowy | zapytania |
| `DispatcherTests.Throws_when_handler_missing` | jednostkowy | brak handlera → wyjątek |
| `LaunchDateOptionsValidatorTests.Accepts_iso_date` | jednostkowy | `2026-11-03` → `DateOnly(2026,11,3)` |
| `LaunchDateOptionsValidatorTests.Rejects_invalid` | jednostkowy (teoria) | brak, pusty, `03.11.2026`, `2026-02-30`, `2026-11-03T10:00`, ` 2026-11-03` |
| `LaunchDateStartupTests.Host_fails_without_launch_date` | integracyjny | brak klucza → `OptionsValidationException` przy starcie |
| `LaunchDateStartupTests.Host_fails_with_invalid_launch_date` | integracyjny | zły format → start przerwany |
| `LaunchDateStartupTests.Launch_date_is_available_to_modules` | integracyjny | `ILaunchDate` z DI zwraca wartość z konfiguracji |
| `LaunchDateStartupTests.Base_appsettings_has_no_launch_date` | integracyjny | `appsettings.json` nie zawiera `StoneFlow:LaunchDate` |
| `HealthEndpointTests.Healthy_when_database_available` | integracyjny (Testcontainers) | `200`, treść `Healthy` |
| `HealthEndpointTests.Unhealthy_when_database_unavailable` | integracyjny | connection string na nieosiągalny serwer → `503`, `Unhealthy` |
| `HealthEndpointTests.Body_contains_only_status` / `Unhealthy_response_has_no_details` | integracyjny | treść to wyłącznie status |
| `HealthEndpointTests.Is_anonymous` | integracyjny | brak nagłówków uwierzytelnienia → nie `401` |
| `ProblemDetailsAndCorrelationTests.Not_found_returns_problem_details_with_trace_id` | integracyjny | `404`, `application/problem+json`, `traceId` = `X-Correlation-ID` |
| `ProblemDetailsAndCorrelationTests.Unhandled_exception_returns_problem_details` | integracyjny | ścieżka testowa rzucająca wyjątek (`IStartupFilter` w fabryce testowej) → `500` Problem Details |
| `ProblemDetailsAndCorrelationTests.Production_error_has_no_exception_details` | integracyjny | środowisko Production → brak `detail`/stack trace/komunikatu wyjątku |
| `ProblemDetailsAndCorrelationTests.Traceparent_is_propagated` | integracyjny | `TraceId` z nagłówka `traceparent` w `X-Correlation-ID` i `traceId` |
| `ProblemDetailsAndCorrelationTests.Log_scope_contains_correlation_id` | integracyjny | przechwycony log żądania ma `CorrelationId` = nagłówek |
| `ProblemDetailsAndCorrelationTests.Incoming_correlation_header_is_ignored` | integracyjny | własny `X-Correlation-ID` w żądaniu nie zmienia odpowiedzi |
| `MigrationTests.All_module_migrations_apply_to_empty_database` | integracyjny (Testcontainers) | cztery schematy i cztery tabele historii po `Migrate()` każdego kontekstu |
| `OpenApiTests.Document_available_in_development` / `Not_exposed_outside_development` | integracyjny | `200` w Development, `404` w Production |

## 9. Weryfikacja

```
dotnet tool restore
dotnet restore stone-flow.slnx --locked-mode
dotnet format stone-flow.slnx --verify-no-changes
dotnet build stone-flow.slnx -c Release --no-restore
dotnet test --solution stone-flow.slnx -c Release --no-build      # wymaga Dockera
```

Ręcznie:
- `docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d`; ustawienie
  `ConnectionStrings:StoneFlow` przez `dotnet user-secrets`; `dotnet ef database update` dla
  czterech kontekstów; `dotnet run --project api/src/StoneFlow.Api`.
- `curl -i http://localhost:{port}/health` przy działającym i zatrzymanym kontenerze.
- Usunięcie `StoneFlow:LaunchDate` z `appsettings.Development.json` → API nie startuje.
- `curl -i http://localhost:{port}/nie-ma` → Problem Details; `traceId` = `X-Correlation-ID` =
  `CorrelationId` w logu.
- `stone-flow.slnx` otwiera się w Visual Studio.
- Celowe dodanie referencji EF Core do `StoneFlow.Orders.Domain` → testy architektury czerwone
  (cofnąć).

## 10. Ryzyka

- **Wersje przy otwartym D-08:** SDK 10.0.403 / runtime 10.0.14 według SF-1; korekta po
  zamknięciu D-08 to osobna zmiana. Runtime sprawdzany w CI (część c).
- **Tworzenie schematów na hostingu:** jedna baza z czterema schematami zakłada, że konto SQL
  SmarterASP.NET może tworzyć schematy (przegląd, punkt 8). Niepotwierdzone w SF-1 — do
  weryfikacji w SF-4 / D-08.
- **Logi produkcyjne:** JSON na konsolę wystarcza lokalnie i w CI; na IIS stdout zwykle nie jest
  zachowywany. Ujście logów produkcyjnych należy do SF-4 (przegląd, punkt 9).
- **Wyjątek `/health`:** SF-6 musi przejąć anonimowy `/health` na liście wyjątków od DOST-01.
- **Testcontainers lokalnie:** testy integracyjne wymagają Dockera; bez niego `dotnet test`
  kończy się błędem tych testów (nie są pomijane po cichu).
- **Wersje pakietów:** dokładne numery (EF Core, OpenAPI, xUnit v3, Testcontainers, NetArchTest)
  ustalane przy implementacji jako najnowsze stabilne zgodne z .NET 10 i zapisane w
  `Directory.Packages.props`.
