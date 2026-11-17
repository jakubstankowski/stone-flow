# Implementation done — SF-3

## Summary

Dostarczono szkielet repozytorium Stone Flow w trzech częściach na gałęzi `sf-3c-ci`:

- **a** (`e264270`) — solution `stone-flow.slnx`, przypięte wersje .NET (`global.json`,
  `Directory.Build.props`, `Directory.Packages.props`, `packages.lock.json`, `dotnet-ef`),
  backend ASP.NET Core jako modularny monolit (Shared.Kernel/Application/Infrastructure,
  moduły Orders, Expenses, Reporting, Integrations × Domain/Application/Infrastructure/Api),
  `DbContext` per moduł we własnym schemacie z migracją `InitialSchema`, `/health`,
  Problem Details, korelacja logów, OpenAPI w Development, walidowana data uruchomienia,
  lokalny SQL Server w `deploy/docker-compose.yml`, przykładowa konfiguracja, testy backendu.
- **b** (`63ad6e8`) — aplikacja Angular 22.2.4 w trybie strict (standalone, lazy routing,
  strona startowa „Stone Flow”, Vitest, ESLint, Prettier), przypięty Node 24.21.0 i npm 11.6.1.
- **c** (`7429326`) — workflow GitHub Actions (`backend`, `frontend`, `secrets`),
  `.gitleaks.toml`, `README.md`, aktualizacja `.sdd/context/conventions.md` i `role.md`.

Wyniki przeglądów kodu: a, b, c — PASS WITH ISSUES.

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|
| Solution `stone-flow.slnx` ze wszystkimi projektami z tabeli | Implemented | 23 projekty (`src` + `tests`). Otwarcie w Visual Studio niesprawdzone. |
| Struktura monorepo `api/src`, `api/tests`, `app/`, `deploy/`, `docs/`, `.github/workflows/` | Implemented | |
| Przypięte SDK (`global.json`, `rollForward: latestPatch`) | Implemented | SDK 10.0.403. |
| Przypięty runtime 10.0.14 | Implemented | Pakiety 10.0.14, `dotnet-ef` 10.0.14, kontrola `DOTNET_RUNTIME_VERSION` w CI. |
| `Directory.Build.props`, `Directory.Packages.props`, lockfile NuGet, locked mode w CI | Implemented | |
| Przypięty Node (`.nvmrc`, `engines`, `engine-strict`), lockfile npm, dokładne wersje | Implemented | Node 24.21.0, `packageManager: npm@11.6.1`. |
| `.editorconfig` | Implemented | |
| Moduły × 4 warstwy z referencjami zgodnymi z tabelą „Może zależeć od” | Implemented | |
| CQRS: interfejsy, dyspozytor bez MediatR, wyjątek przy braku handlera | Implemented | |
| Abstrakcja daty uruchomienia (`ILaunchDate`, `DateOnly`, tylko odczyt) | Implemented | |
| Walidacja `StoneFlow:LaunchDate` przy starcie; brak daty w `appsettings.json`; przykładowa data w Development | Implemented | |
| `DbContext` per moduł, własny schemat, historia migracji w schemacie, migracja początkowa | Implemented | Zastosowanie na pustej bazie niepotwierdzone uruchomieniem (Docker niedostępny). |
| Brak migracji przy starcie | Implemented | |
| `GET /health` anonimowy, `200 Healthy` / `503 Unhealthy`, bez szczegółów | Implemented | Ścieżka `503` potwierdzona testem; `200` przy dostępnej bazie niepotwierdzone uruchomieniem. |
| Korelacja: `TraceId`, `X-Correlation-ID`, `traceId` w Problem Details, `traceparent`, logi JSON | Implemented | |
| Problem Details dla wyjątków i 4xx/5xx, bez szczegółów poza Development | Implemented | |
| OpenAPI `/openapi/v1.json` tylko w Development | Implemented | |
| `deploy/docker-compose.yml`, `.env.example`, `api.env.example` bez sekretów | Implemented | |
| Testy: architektury, Shared, integracyjne | Implemented | |
| Angular strict, standalone, strona startowa po polsku, `lang="pl"`, lazy routing, Vitest, ESLint, Prettier | Implemented | |
| CI: push i PR do `main`; backend (restore locked, format, build Release, testy z SQL Server); frontend (`npm ci`, Prettier, ESLint, typecheck, build, Vitest) | Implemented | Workflow nie był jeszcze uruchomiony. |
| Skan sekretów w CI | Implemented | Zadanie `secrets` (gitleaks, cała historia). Nie był jeszcze uruchomiony. |
| Kryterium: CI blokuje błąd typów, formatowania, lintu lub testu (sprawdzone celowo zepsutą gałęzią) | Not implemented | Gałęzie próbne nie zostały wykonane. |
| Ochrona gałęzi `main` | Not implemented | Opisana w README jako krok ręczny właściciela; nie włączona. |
| `README.md` z instrukcją startu i przykładową konfiguracją | Implemented | Odtworzenie na świeżym klonie nie zostało wykonane. |
| Uzupełnienie TODO w `.sdd/context/conventions.md` i `role.md` | Implemented | Pozostałe TODO zgodne z planem c (kroki 8–9). |

## Files

### Created

Repozytorium:
- `stone-flow.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`
- `.config/dotnet-tools.json`, `.editorconfig`, `.gitattributes`, `.gitignore`, `.gitleaks.toml`
- `README.md`, `.github/workflows/ci.yml`
- `deploy/docker-compose.yml`, `deploy/.env.example`, `deploy/api.env.example`

Host `api/src/StoneFlow.Api/`:
- `StoneFlow.Api.csproj`, `Program.cs`, `appsettings.json`, `appsettings.Development.json`,
  `Properties/launchSettings.json`, `packages.lock.json`
- `Observability/CorrelationIdMiddleware.cs`, `Observability/ProblemDetailsSetup.cs`
- `Health/SqlServerHealthCheck.cs`

Część wspólna `api/src/Shared/`:
- `StoneFlow.Shared.Kernel/` — `.csproj`, `AssemblyReference.cs`, `packages.lock.json`
- `StoneFlow.Shared.Application/` — `.csproj`, `AssemblyReference.cs`, `Cqrs/ICommand.cs`,
  `Cqrs/ICommandHandler.cs`, `Cqrs/IDispatcher.cs`, `Cqrs/IQuery.cs`, `Cqrs/IQueryHandler.cs`,
  `LaunchDate/ILaunchDate.cs`, `packages.lock.json`
- `StoneFlow.Shared.Infrastructure/` — `.csproj`, `AssemblyReference.cs`, `Cqrs/Dispatcher.cs`,
  `Cqrs/HandlerRegistration.cs`, `DependencyInjection.cs`, `LaunchDate/ConfiguredLaunchDate.cs`,
  `LaunchDate/LaunchDateOptions.cs`, `LaunchDate/LaunchDateOptionsValidator.cs`,
  `Persistence/ModuleDbContextOptions.cs`, `packages.lock.json`

Moduły `api/src/Modules/{Orders,Expenses,Reporting,Integrations}/` (dla każdego modułu):
- `StoneFlow.{Modul}.Domain/` — `.csproj`, `AssemblyReference.cs`, `packages.lock.json`
- `StoneFlow.{Modul}.Application/` — `.csproj`, `AssemblyReference.cs`, `packages.lock.json`
- `StoneFlow.{Modul}.Infrastructure/` — `.csproj`, `AssemblyReference.cs`, `DependencyInjection.cs`,
  `Persistence/{Modul}DbContext.cs`, `Persistence/Migrations/*_InitialSchema.cs`,
  `*_InitialSchema.Designer.cs`, `{Modul}DbContextModelSnapshot.cs`, `packages.lock.json`
- `StoneFlow.{Modul}.Api/` — `.csproj`, `AssemblyReference.cs`, `{Modul}Module.cs`, `packages.lock.json`

Testy `api/tests/`:
- `Directory.Build.props`
- `StoneFlow.Architecture.Tests/` — `.csproj`, `DbContextSchemaTests.cs`, `LayerDependencyTests.cs`,
  `ModuleBoundaryTests.cs`, `ProjectReferenceTests.cs`, `Modules.cs`, `RepositoryRoot.cs`, `packages.lock.json`
- `StoneFlow.Shared.Tests/` — `.csproj`, `Cqrs/DispatcherTests.cs`,
  `LaunchDate/LaunchDateOptionsValidatorTests.cs`, `packages.lock.json`
- `StoneFlow.Api.IntegrationTests/` — `.csproj`, `DatabaseTests.cs`, `HealthEndpointTests.cs`,
  `LaunchDateStartupTests.cs`, `OpenApiTests.cs`, `ProblemDetailsAndCorrelationTests.cs`,
  `Infrastructure/RepositoryRoot.cs`, `Infrastructure/SqlServerFixture.cs`,
  `Infrastructure/StoneFlowApiFactory.cs`, `Infrastructure/ThrowingEndpointStartupFilter.cs`,
  `packages.lock.json`

Frontend `app/`:
- `.editorconfig`, `.gitattributes`, `.gitignore`, `.npmrc`, `.nvmrc`, `.prettierignore`,
  `.prettierrc.json`, `.vscode/extensions.json`, `.vscode/launch.json`, `.vscode/tasks.json`
- `angular.json`, `eslint.config.js`, `package.json`, `package-lock.json`, `tsconfig.json`,
  `tsconfig.app.json`, `tsconfig.spec.json`, `public/favicon.ico`
- `src/index.html`, `src/main.ts`, `src/styles.css`
- `src/app/app.config.ts`, `src/app/app.routes.ts`, `src/app/app.routes.spec.ts`, `src/app/app.ts`,
  `src/app/app.html`, `src/app/app.spec.ts`
- `src/app/features/start/start-page.ts`, `start-page.html`, `start-page.spec.ts`

Artefakty zadania: `docs/tasks/sf-3/00-raw-task.md`, `02-spec.md`, `03-spec-review.md`,
`04-implementation-plan-{a,b,c}.md`, `05-code-review-{a,b,c}.md`, `06-implementation-done.md`, `task.md`.

### Modified

- `.sdd/context/conventions.md`
- `.sdd/context/role.md`

## Planned Units

| Item from the plan | Part | Status |
|---|---|---|
| Pliki repozytorium (`global.json`, `Directory.*.props`, `.editorconfig`, `.gitignore`, `dotnet-tools.json`) | a | Exists |
| `stone-flow.slnx` i wszystkie projekty z sekcji 3 | a | Exists |
| Projekt `StoneFlow.Api` (host) | a | Exists |
| `CorrelationIdMiddleware` | a | Exists |
| `ProblemDetailsSetup` | a | Exists |
| `SqlServerHealthCheck` i `/health` | a | Exists |
| OpenAPI w Development | a | Exists |
| `Shared.Kernel` | a | Exists |
| `Shared.Application` — interfejsy CQRS, `ILaunchDate` | a | Exists |
| `Shared.Infrastructure` — `Dispatcher`, `HandlerRegistration`, `LaunchDateOptions`, `LaunchDateOptionsValidator`, `ConfiguredLaunchDate`, `ModuleDbContextOptions`, `DependencyInjection` | a | Exists |
| Moduł Orders — Domain, Application, Infrastructure (`OrdersDbContext`, DI), Api (`OrdersModule`) | a | Exists |
| Moduł Expenses — Domain, Application, Infrastructure (`ExpensesDbContext`, DI), Api (`ExpensesModule`) | a | Exists |
| Moduł Reporting — Domain, Application, Infrastructure (`ReportingDbContext`, DI), Api (`ReportingModule`) | a | Exists |
| Moduł Integrations — Domain, Application, Infrastructure (`IntegrationsDbContext`, DI), Api (`IntegrationsModule`) | a | Exists |
| Migracja `InitialSchema` — Orders (`orders`) | a | Exists |
| Migracja `InitialSchema` — Expenses (`expenses`) | a | Exists |
| Migracja `InitialSchema` — Reporting (`reporting`) | a | Exists |
| Migracja `InitialSchema` — Integrations (`integrations`) | a | Exists |
| `appsettings.json`, `appsettings.Development.json`, `launchSettings.json` | a | Exists |
| `deploy/docker-compose.yml`, `deploy/.env.example`, `deploy/api.env.example` | a | Exists |
| `StoneFlow.Architecture.Tests` | a | Exists |
| `StoneFlow.Shared.Tests` | a | Exists |
| `StoneFlow.Api.IntegrationTests` (`SqlServerFixture`, `StoneFlowApiFactory`) | a | Exists |
| `api/tests/StoneFlow.Api.IntegrationTests/MigrationTests.cs` | a | Missing — test w `DatabaseTests.cs` |
| Aplikacja Angular z `ng new` | b | Exists |
| `app/.nvmrc`, `app/.npmrc`, `app/package.json`, `app/package-lock.json` | b | Exists |
| `angular.json`, `tsconfig.json`, `tsconfig.app.json`, `tsconfig.spec.json` | b | Exists |
| `eslint.config.js`, `.prettierrc.json`, `.prettierignore`, `.gitignore` | b | Exists |
| `src/index.html`, `src/main.ts` | b | Exists |
| `app.config.ts`, `app.routes.ts` (trasa `''` z `loadComponent`, `**` → `''`) | b | Exists |
| Komponent główny `app.ts`, `app.html` | b | Exists |
| Strona startowa `features/start/start-page` | b | Exists |
| `.github/workflows/ci.yml` — zadania `backend`, `frontend`, `secrets` | c | Exists |
| `.gitleaks.toml` | c | Exists |
| `README.md` | c | Exists |
| Aktualizacja `.sdd/context/conventions.md` | c | Exists |
| Aktualizacja `.sdd/context/role.md` | c | Exists |

## Database

Jedna baza SQL Server, cztery `DbContext`, po jednej migracji na kontekst:

| Kontekst | Schemat | Migracja | Zawartość |
|---|---|---|---|
| `OrdersDbContext` | `orders` | `20261103163537_InitialSchema` | `EnsureSchema("orders")`; historia w `orders.__EFMigrationsHistory` |
| `ExpensesDbContext` | `expenses` | `20261103163552_InitialSchema` | `EnsureSchema("expenses")`; historia w `expenses.__EFMigrationsHistory` |
| `ReportingDbContext` | `reporting` | `20261103163600_InitialSchema` | `EnsureSchema("reporting")`; historia w `reporting.__EFMigrationsHistory` |
| `IntegrationsDbContext` | `integrations` | `20261103163608_InitialSchema` | `EnsureSchema("integrations")`; historia w `integrations.__EFMigrationsHistory` |

Brak tabel biznesowych. Metody `Down` są puste.

## Tests

Wyniki z `dotnet test` i `npm test` uruchomionych 2026-11-03 na tej maszynie (Docker niedostępny).

| Test from the plan | Status | Run result |
|---|---|---|
| `LayerDependencyTests.Domain_does_not_depend_on_http_ef_or_hosting` | Exists | Passed |
| `LayerDependencyTests.Application_does_not_depend_on_infrastructure_http_or_ef` | Exists | Passed |
| `LayerDependencyTests.Shared_kernel_has_no_dependencies` | Exists | Passed |
| `LayerDependencyTests.Domain_does_not_depend_on_outer_layers` | Exists | Passed |
| `ModuleBoundaryTests.Module_does_not_reference_other_modules` | Exists | Passed |
| `DbContextSchemaTests.Context_uses_only_its_schema` | Exists | Passed |
| `DispatcherTests.Resolves_command_handler_and_passes_cancellation` | Exists | Passed |
| `DispatcherTests.Resolves_query_handler` | Exists | Passed |
| `DispatcherTests.Throws_when_handler_missing` | Exists | Passed |
| `LaunchDateOptionsValidatorTests.Accepts_iso_date` | Exists | Passed |
| `LaunchDateOptionsValidatorTests.Rejects_invalid` | Exists | Passed |
| `LaunchDateStartupTests.Host_fails_without_launch_date` | Exists | Passed |
| `LaunchDateStartupTests.Host_fails_with_invalid_launch_date` | Exists | Passed |
| `LaunchDateStartupTests.Launch_date_is_available_to_modules` | Exists | Passed |
| `LaunchDateStartupTests.Base_appsettings_has_no_launch_date` | Exists | Passed |
| `HealthEndpointTests.Healthy_when_database_available` | Exists as `DatabaseTests.Health_is_healthy_when_database_available` | Failed — `DockerUnavailableException` (Docker niedostępny) |
| `HealthEndpointTests.Unhealthy_when_database_unavailable` | Exists | Passed |
| `HealthEndpointTests.Body_contains_only_status` | Covered by `DatabaseTests.Health_is_healthy_when_database_available` and `Unhealthy_response_has_no_details` | See those rows |
| `HealthEndpointTests.Unhealthy_response_has_no_details` | Exists | Passed |
| `HealthEndpointTests.Is_anonymous` | Exists | Passed |
| `ProblemDetailsAndCorrelationTests.Not_found_returns_problem_details_with_trace_id` | Exists | Passed |
| `ProblemDetailsAndCorrelationTests.Unhandled_exception_returns_problem_details` | Exists | Passed |
| `ProblemDetailsAndCorrelationTests.Production_error_has_no_exception_details` | Exists | Passed |
| `ProblemDetailsAndCorrelationTests.Traceparent_is_propagated` | Exists | Passed |
| `ProblemDetailsAndCorrelationTests.Log_scope_contains_correlation_id` | Exists | Passed |
| `ProblemDetailsAndCorrelationTests.Incoming_correlation_header_is_ignored` | Exists | Passed |
| `MigrationTests.All_module_migrations_apply_to_empty_database` | Exists as `DatabaseTests.All_module_migrations_apply_to_empty_database` | Failed — `DockerUnavailableException` (Docker niedostępny) |
| `OpenApiTests.Document_available_in_development` | Exists | Passed |
| `OpenApiTests.Not_exposed_outside_development` | Exists | Passed |
| `app.spec.ts` — komponent główny renderuje `router-outlet` | Exists | Passed |
| `start-page.spec.ts` — nagłówek „Stone Flow” | Exists | Passed |
| `app.routes.spec.ts` — `''` ładuje `StartPage` leniwie | Exists | Passed |
| `app.routes.spec.ts` — nieznana ścieżka przekierowuje na `''` | Exists | Passed |
| CI: zielony przebieg na `main` po merge a, b, c | Not done | Not run |
| CI: błąd formatowania C# na gałęzi próbnej | Not done | Not run |
| CI: błąd typu w `*.spec.ts` | Not done | Not run |
| CI: nieprzechodzący test backendu | Not done | Not run |
| CI: fałszywy klucz w pliku | Not done | Not run |
| Instrukcja z README na czystym klonie | Not done | Not run |

Łącznie backend: 77 testów, 75 Passed, 2 Failed (oba `DatabaseTests`, `DockerUnavailableException`). Frontend: 3 pliki, 4 testy, wszystkie Passed.

## Verification

SDK 10.0.403 z `%LOCALAPPDATA%\Microsoft\dotnet`; Node v24.21.0. Uruchomione 2026-11-03.

| Command | Outcome |
|---|---|
| `dotnet tool restore` | Passed |
| `dotnet restore stone-flow.slnx --locked-mode` | Passed |
| `dotnet format stone-flow.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet build stone-flow.slnx -c Release --no-restore` | Passed (0 ostrzeżeń, 0 błędów) |
| `dotnet test --solution stone-flow.slnx -c Release --no-build` | Failed — 75/77; 2 `DatabaseTests` z `DockerUnavailableException` |
| `npm ci` (w `app/`) | Passed (`npm audit`: 18 moderate) |
| `npm run format:check` | Passed |
| `npm run lint` | Passed |
| `npm run typecheck` | Passed |
| `npm run build` | Passed |
| `npm test -- --watch=false` | Passed (3 pliki, 4 testy) |
| `gh workflow view ci.yml` | Not run (`gh` niedostępny) |
| `gh run list --workflow ci.yml --branch main --limit 1` | Not run (`gh` niedostępny; workflow nie był uruchomiony) |
| Ręczne kroki z planów a, b, c (świeży klon, `curl /health`, usunięcie daty, gałęzie próbne, Visual Studio, ochrona `main`) | Not run |

## Deviations

- `api/tests/StoneFlow.Api.IntegrationTests/MigrationTests.cs` nie powstał; test migracji i `Health_is_healthy_when_database_available` są w `DatabaseTests.cs` (a).
- `HealthEndpointTests.Healthy_when_database_available` przeniesiony do `DatabaseTests` pod nazwą `Health_is_healthy_when_database_available` (a).
- `Down` migracji `InitialSchema` jest pusty; plan zakładał `DropSchema` (a).
- `Microsoft.EntityFrameworkCore.Design` jest w `StoneFlow.Api.csproj` zamiast w `StoneFlow.{Modul}.Infrastructure` (a).
- `StoneFlow.Shared.Infrastructure.csproj` ma `FrameworkReference Microsoft.AspNetCore.App` (a).
- `@angular/build` i `@angular/cli` w wersji 22.2.5, pakiety frameworka 22.2.4 (b).
- gitleaks pobierany jako archiwum binarne z dokładną wersją i sumą SHA-256 (c).

## Additional Implementation

Additional implementation not covered by the original documents:

- `.gitattributes` (katalog główny), `api/tests/Directory.Build.props` (a).
- `api/tests/StoneFlow.Architecture.Tests/ProjectReferenceTests.cs` (6 testów referencji w plikach `.csproj`), `Modules.cs`, `RepositoryRoot.cs` (a).
- `api/tests/StoneFlow.Api.IntegrationTests/Infrastructure/RepositoryRoot.cs`, `Infrastructure/ThrowingEndpointStartupFilter.cs` (a).
- Testy spoza planu: `HealthEndpointTests.Unhealthy_when_connection_string_missing`, `DbContextSchemaTests.Schema_is_module_name_in_lower_case`, `LayerDependencyTests.Shared_application_does_not_depend_on_infrastructure_http_or_ef`, `LayerDependencyTests.Shared_does_not_depend_on_modules` (a).
- Pakiet `Microsoft.Extensions.Diagnostics.Testing` w `Directory.Packages.props` (a).
- Pole `packageManager: "npm@11.6.1"` w `app/package.json` (b).
- Pliki z `ng new` spoza tabeli planu: `app/.vscode/*`, `app/.editorconfig`, `app/.gitattributes`, `app/public/favicon.ico`, `app/src/styles.css` (b).

## Open Items

1. CI nie było uruchomione — workflow startuje tylko przy push / PR do `main`; brak zielonego przebiegu zadań `backend`, `frontend`, `secrets` (c).
2. Kryterium „CI blokuje błąd typów, formatowania, lintu lub testu” niezweryfikowane — gałęzie próbne z kroku 5 planu c nie zostały wykonane (c).
3. Ochrona gałęzi `main` nie jest włączona; wymaga pierwszego zielonego CI (c).
4. `DatabaseTests.Health_is_healthy_when_database_available` i `DatabaseTests.All_module_migrations_apply_to_empty_database` nie przeszły lokalnie z powodu braku Dockera (a).
5. Odtworzenie projektu z README na świeżym klonie i otwarcie `stone-flow.slnx` w Visual Studio nie zostały wykonane.
6. Code review a — Correctness 1: `ProblemDetailsSetup.cs:18-22` czyści `Detail` wszystkich odpowiedzi poza Development, nie tylko wyjątków.
7. Code review a — Correctness 2: `Dispatcher.cs:38-41` — wyjątek synchroniczny handlera dociera jako `TargetInvocationException`.
8. Code review a — Correctness 3: `HandlerRegistration.cs:22-25` — dwa handlery tej samej komendy rejestrują się bez błędu.
9. Code review a — Correctness 4: `deploy/docker-compose.yml:11-12` wystawia port 1433 na wszystkich interfejsach.
10. Code review a — Correctness 5: `SqlServerHealthCheck.cs:34` — filtr wyjątków nie obejmuje m.in. `OperationCanceledException`.
11. Code review a — Conventions 1: `StoneFlow.Shared.Infrastructure.csproj:3-5` — `FrameworkReference Microsoft.AspNetCore.App`.
12. Code review a — Conventions 2: `ProjectReferenceTests.cs:33-40` nie sprawdza `PackageReference` ani `FrameworkReference` infrastruktury modułu.
13. Code review a — Null Safety 1–2: `CorrelationIdMiddleware.cs:22` (`!` bez komentarza), `Dispatcher.cs:41` (`!` na wyniku `Invoke`).
14. Code review a — Test Gaps 1–4: brak testów oryginalnego wyjątku dyspozytora, zachowania `detail` w 4xx poza Development, referencji pakietów/frameworka infrastruktury, gałęzi `Activity.Current == null`.
15. Code review a — Code Smells: zduplikowana klasa `RepositoryRoot.cs`, cztery identyczne bloki `DependencyInjection.cs` modułów.
16. Code review b — Conventions: brak landmarku `<main>` (`app/src/app/app.html:1`).
17. Code review b — Test Gaps: ręczne sprawdzenie, że błąd typu w `start-page.spec.ts` zatrzymuje `npm run typecheck`, bez śladu wykonania.
18. Code review b — Code Smells: komentarz szablonowy w `app/src/styles.css:1`; 18 podatności moderate w zależnościach deweloperskich (`npm audit`).
19. Code review c — Conventions 1: `README.md:26` i `README.md:30` — sprzeczne informacje o minimalnej wersji Visual Studio.
20. Code review c — Correctness 1: `ci.yml:12-14` — `cancel-in-progress: true` dotyczy także `main`.
21. Code review c — Correctness 2: `ci.yml:36-43` sprawdza obecność runtime 10.0.14, nie jego użycie przez testy.
22. Code review c — Code Smells: `ci.yml:58`, `:66` — `if-no-files-found: ignore` przy wynikach testów.
