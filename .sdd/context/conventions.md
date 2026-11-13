# Conventions

How code in this repository is structured and named. Plans are written in these terms
and reviews check against them.

The repository skeleton was created in SF-3. Update this file when a task changes a convention.

## Engineering principles

Project invariants (`.sdd/context/rules.md`) and recorded decisions take precedence over every
rule below. Each rule is meant to be checked in review.

- **KISS:** the simplest solution that meets the current requirements and invariants. A plain
  CRUD use case needs no artificial aggregate, service layer or pipeline.
- **YAGNI:** no abstraction, configuration switch, generic parameter or extension point for a
  hypothetical caller. The need must exist in the current task or an accepted document; a second
  real caller is a reason to evaluate sharing, not an automatic extraction rule.
- **DRY:** share the same business knowledge (a rule, a calculation, a definition) and stable
  behaviour, not similar-looking syntax. Small duplication is acceptable when a shared
  abstraction would couple independent features or modules.
- **SRP:** a class or method has one coherent responsibility and one reason to change. A handler
  that also holds domain rules, mapping and I/O details has several.
- **OCP:** extensibility follows a real need; no strategy hierarchies or plugin points in advance.
- **LSP:** an implementation honours its contract, including the meaning of the result, the
  errors it reports and cancellation (a port documented as returning `null` when nothing is
  found does not throw for that case).
- **ISP:** small, cohesive contracts; a port groups operations used together (`IAccountService`)
  and is not split into one-method interfaces without a reason.
- **DIP:** dependencies point inwards. Application defines the ports it needs; Infrastructure
  implements them (`Application/Abstractions/Ports.cs` → `Infrastructure/`).
- Prefer composition; inheritance only where the subtype is truly substitutable.
- Create an interface for an architectural boundary (port), an interchangeable implementation or
  a useful test seam — not for every class.
- Change only what the task needs; improve touched code only when correctness or clarity
  requires it.

## Repository layout

Monorepo that clearly separates frontend, backend, tests, documentation and deployment
configuration.

| Path | What | Stack |
|---|---|---|
| `docs/` | PRD, design guide, task artifacts; operating procedures and the MVP acceptance checklist in `docs/operations/` (SF-21; customer data anonymisation `anonimizacja.md`, SF-22) | Markdown |
| `documents/` | Source requirements, DEMO workbooks, reference dashboard | docx, xlsx, webp |
| `app/` | Frontend application | Angular, Tailwind CSS, daisyUI |
| `app/e2e/` | End-to-end tests (`*.e2e.ts`, outside Vitest), `app/playwright.config.ts` (SF-21) | Playwright, axe-core |
| `api/src/` | Backend application | ASP.NET Core, EF Core |
| `api/tests/` | Backend tests; frontend unit tests live next to the code in `app/src` | xUnit v3 (Microsoft.Testing.Platform), NetArchTest, Testcontainers; Vitest |
| `deploy/` | Local SQL Server, example variables, deployment scripts (`deploy/scripts/`, SF-4; backup check `verify-restore.sh`, SF-21); procedure in `wdrozenia.md` (repository root) | bash, GitHub Actions, Vercel, SmarterASP.NET |
| `stone-flow.slnx` | Solution for Visual Studio and the `dotnet` CLI | |

Pinned in the repository: SDK, runtime and Node versions, the frontend lockfile,
`global.json` and central .NET package versions.

| What | Where |
|---|---|
| .NET SDK 10.0.403 (runtime 10.0.14) | `global.json`; runtime checked in CI (`DOTNET_RUNTIME_VERSION`) |
| NuGet package versions | `Directory.Packages.props` (transitive pinning on), `packages.lock.json` per project |
| `dotnet-ef` | `.config/dotnet-tools.json` |
| Node 24.21.0, npm 11 | `app/.nvmrc`, `engines` + `packageManager` in `app/package.json`, `app/.npmrc` (`engine-strict`) |
| npm dependencies | exact versions in `app/package.json`, `app/package-lock.json` |

## Backend

### Structure

Modular monolith, with modules for access (accounts and sessions, SF-6), orders, expenses,
reporting and integrations.
Onion layers: domain (business rules), application (use cases), infrastructure (data access
and external services), API (HTTP). The shared part stays small and holds no module logic.
A module lives in `api/src/Modules/{Module}/` as four projects:

| Project | Holds | References |
|---|---|---|
| `StoneFlow.{Module}.Domain` | entities and business rules | `StoneFlow.Shared.Kernel` only |
| `StoneFlow.{Module}.Application` | commands, queries and their handlers | own Domain, `StoneFlow.Shared.Application` |
| `StoneFlow.{Module}.Infrastructure` | `{Module}DbContext`, entity configurations and migrations in `Persistence/`, DI registration | own Application, `StoneFlow.Shared.Infrastructure` |
| `StoneFlow.{Module}.Api` | endpoint registration (`{Module}Module`) | own Application |

The host `api/src/StoneFlow.Api` composes the modules. `api/src/Shared/` holds the CQRS
abstractions and dispatcher, the launch date (`ILaunchDate`) and the shared EF Core provider
setup (`UseModuleSqlServer`). These boundaries are enforced by `StoneFlow.Architecture.Tests`.

Scheduled jobs (SF-11) are the one shared part with its own data: the contract
(`IScheduledJob`, `JobDefinition`, `JobSchedule`, `IJobRunner`, `GetJobStatusesQuery`) lives in
`StoneFlow.Shared.Application/Scheduling`, the runner, `SchedulingDbContext` (schema `scheduling`,
migrations in `Scheduling/Migrations`) and the `BackgroundService` in
`StoneFlow.Shared.Infrastructure/Scheduling`. The scheduler holds no module logic. Its endpoint
`GET /api/jobs` is mapped by the host (`StoneFlow.Api/Scheduling`), because it belongs to no module.

- A module registers a job in its `DependencyInjection.cs` with
  `services.AddScheduledJob<TJob>(new JobDefinition(key, JobSchedule.DailyAt(...), maxDuration))`.
  Keys are stable, `[a-z0-9-]`; the schedule is a daily local time in Europe/Warsaw.
- A job must stop and write nothing after its token is cancelled, must be safe to rerun after an
  interrupted run (invariant 4), and must keep secrets and personal data out of exception messages
  (they are stored in the run history). It runs in a fresh DI scope; for several units of work it
  creates its own scopes with `IServiceScopeFactory`.
- `JobRunContext.LastSuccessStartedAt` is the scheduler's last successful run, not a business
  cursor such as the sync "since last success" — a job keeps its own cursor in its module.
- Manual runs ("Synchronizuj teraz") go through `IJobRunner.TryRunNowAsync`, which uses the same
  lease; never add a second lock for the same job.

### Layer responsibilities

| Layer | Responsible for | Not responsible for |
|---|---|---|
| Api | binding, reading the identity (account id from claims), HTTP specifics (cookies, status codes), calling `IDispatcher`, mapping the result to `TypedResults` / Problem Details | business decisions, data access |
| Application | orchestrating one use case: input validation, authorisation of the operation and of the concrete resource, calling the domain and ports, the transaction boundary | business rules and calculations owned by the domain; EF Core, HTTP |
| Domain | business rules, invariants, calculations, allowed state transitions, policies (`PasswordPolicy`, `LockoutPolicy`) | I/O, HTTP, EF Core, hosting |
| Infrastructure | EF Core (`{Module}DbContext`, configurations, migrations), Identity, port implementations, e-mail, external HTTP, DI registration; translating provider errors into port results | business rules, use-case orchestration |

- A handler does not take over domain rules: a rule that decides an outcome (status transition,
  amount, eligibility) lives in a domain type or domain operation and is unit-tested there.
- Application references neither EF Core nor HTTP (architecture tests). Projections, paging and
  transactions are expressed through ports defined in Application (e.g. `IAccessUnitOfWork`, a
  read port returning DTOs) and implemented in Infrastructure.
- A contract used by more than one module lives in `StoneFlow.Shared.Application` (project
  reference rules allow nothing else); a module never references another module.

### Application organisation

- Application is organised by feature / use case, as in `Access.Application`: `Login/`,
  `PasswordReset/`, `Session/`; ports in `Abstractions/`.
- The command or query, its handler, validation and result stay together in the feature folder.
  Small related types may share one file (`Login/Login.cs` holds `LoginCommand`, `LoginResult`
  and `LoginHandler`).
- No global `Commands/`, `Queries/`, `Handlers/`, `Services/` or `Repositories/` folders.
- Existing names and structure are not migrated as part of an unrelated task.

### CQRS

`ICommand<TResult>`, `IQuery<TResult>`, their handlers and `IDispatcher` live in
`StoneFlow.Shared.Application/Cqrs`; endpoints call only `IDispatcher`. Handlers are registered
per module assembly (`AddHandlersFromAssembly` in the module's `DependencyInjection.cs`).

- A command expresses a business intent (`ConfirmPasswordReset`, later e.g. cancelling an order),
  not a generic "update entity properties".
- One handler runs one use case. It may read the data it needs to validate, authorise and apply
  the change.
- A command returns the minimum the client needs: an id, an outcome/status, validation errors or
  the data the screen needs next. There is no non-generic `ICommand`; a command with nothing
  meaningful to return uses a simple result (`LogoutCommand` → `bool`).
- A query does not change business state and starts no business operation. Technical logging is
  allowed.
- A query returns a DTO or projection; EF entities, `DbContext` and `IQueryable` never leave
  Infrastructure — ports return records shaped for the use case (`AccountInfo`).
- CQRS here needs no separate databases, read models, event sourcing or broker.
- The dispatcher only resolves and invokes the handler — it adds **no** validation,
  authorisation, transaction or retry. The handler (or a port it calls) owns each explicitly:
  - input validation: in the handler, returned as a result (`FieldErrors` → `ValidationProblem`),
    not thrown;
  - authentication: the fallback policy in the host; **authorisation of the concrete resource**
    (does the record exist and may this account act on it): in the handler — being logged in
    does not replace it;
  - transaction: one `SaveChanges` per use case is already atomic. In financial modules
    (Orders, Expenses, Reporting) every state-changing use case runs in
    `IUnitOfWork.ExecuteInTransactionAsync(work, commitWhen, …)` (SF-7), which also covers the
    audit entry; `commitWhen` returns `false` for a failure result so partial writes roll back.
    Access keeps its own `IAccessUnitOfWork`. Integrations (SF-13) changes orders only through
    `IShopOrderSync` (which uses `IUnitOfWork` in Orders); its own tables (`IntegrationsDbContext`)
    have their own connection and use transactions of that context, not `IUnitOfWork` (see "Data
    access").
- Handlers do not call other handlers through the dispatcher. When use cases share behaviour,
  extract a module-local operation (a domain method or an Application helper used by both) and
  state who owns validation, the transaction, retries and side effects. No global service layer.

### C# and .NET

Formatting and naming style live in `.editorconfig` and are enforced in the build
(`EnforceCodeStyleInBuild`, warnings as errors); they are not repeated here.

- Nullable reference types are on (`Directory.Build.props`); optional values are explicit
  (`T?`). Use `!` only where an invariant is already established — never to skip validation.
- Dependencies are explicit constructor parameters resolved by DI. No service locator in
  business logic; the dispatcher and the composition root (`DependencyInjection.cs`,
  `Program.cs`) are the accepted infrastructure exceptions.
- Respect DI lifetimes: a singleton never captures a scoped `DbContext` or request state.
- Async for all I/O, with `CancellationToken` propagated to EF Core, HTTP, SMTP and ports. No
  `.Result`, `.Wait()`, `async void` (except event handlers that require it) or `Task.Run`
  around ordinary async I/O.
- Do not swallow cancellation or errors. Catch an exception only to recover, to translate it at
  a boundary (e.g. a known constraint violation into a result) or to add useful context.
- Dispose only what the code owns (`await using` for a transaction it opened); never dispose
  dependencies owned by DI.
- Time-dependent behaviour uses the injected `TimeProvider` (`LoginHandler`,
  `ConfirmPasswordResetHandler`), never `DateTime.Now`/`UtcNow`, so tests control time.
- Structured logging with the correlation id; what must never be logged is defined in
  `rules.md` (invariant 6) and in the security rules below.
- No generic repositories, base handlers or internal frameworks without a demonstrated need.

### Naming

- Projects and namespaces: `StoneFlow.{Module}.{Layer}`; shared code `StoneFlow.Shared.{Kernel|Application|Infrastructure}`.
- SQL schema = module name in lower case (`access`, `orders`, `expenses`, `reporting`, `integrations`);
  each module keeps its migration history in `{schema}.__EFMigrationsHistory`. The shared scheduler
  uses schema `scheduling` the same way.
- Private fields `_camelCase`, constants and `static readonly` fields `PascalCase`, interfaces
  `I`-prefixed (`.editorconfig`).
- Endpoints (decided in SF-6): Minimal API in `StoneFlow.{Module}.Api`; `Map{Module}Endpoints`
  maps one route group `MapGroup("/api/{area}")` per file `Endpoints/*Endpoints.cs`, request and
  response DTOs next to it. Handlers use `TypedResults`, call only `IDispatcher` and map the result
  to HTTP — no business logic. Names `.WithName("{Module}.{Action}")`.
- All API routes start with `/api` (except `/health`), so the frontend proxy forwards one branch.
  Exception: server-to-server webhooks under `/webhooks/*` (SF-12, `POST /webhooks/woocommerce/orders`).
  They are called by the shop directly (not through Vercel), carry no cookies, are outside the
  CSRF check and authenticate with an HMAC signature of the raw body; each one is listed in
  `EndpointSecurityTests.AllowedAnonymous` and has its own rate-limit policy.

### Access and security (SF-6)

- Module `Access` (schema `access`) owns the single owner account (ASP.NET Core Identity with
  its own one-table store), password reset tokens (SHA-256 hashes) and Data Protection keys.
- Every endpoint requires a session by default (fallback authorization policy, also for paths
  without an endpoint → 401). A new anonymous endpoint needs `.AllowAnonymous()` **and** an entry in
  the allowed list of `EndpointSecurityTests` plus the reason in the task spec.
- Every `POST`/`PUT`/`PATCH`/`DELETE` under `/api` is CSRF-checked globally
  (`CsrfValidationMiddleware`, header `X-XSRF-TOKEN`); endpoints add nothing for it.
- Pipeline order in `Program.cs`: correlation → exception handler → status pages → HSTS + HTTPS
  redirection (outside Development, SF-4) → routing → CORS
  (no origins allowed) → rate limiter → authentication → authorization → CSRF → endpoints.
- The current account id comes from `ClaimTypes.NameIdentifier`; handlers receive it as a command
  parameter, never read `HttpContext`.
- Problem Details: user-facing text in `detail`, a stable machine code in the `code` extension;
  `detail` is removed only for unhandled exceptions outside Development. Error classes and the
  current handling of database exceptions: see "Errors and validation".
- Security events are logged with the account id only — never e-mail, password or token.

### Data access

- EF Core with SQL Server and versioned migrations. Each table has one owning module;
  schemas per module are recommended.
- Keys, constraints, transactions and concurrency control wherever concurrent edits could
  overwrite data.
- Business dates are separate from technical timestamps. Timestamps are stored in UTC and
  presented in Europe/Warsaw.
- Read only the columns needed and project to the DTO before materialising (`Select`). Use
  `AsNoTracking` when entities are read without being changed; a pure DTO projection needs no
  extra tracking control.
- Lists filter, sort and paginate in the database, with a maximum page size and a stable sort
  (a unique tie-breaker such as the id).
- No N+1 queries or uncontrolled loading of relations.
- A `DbContext` is not thread-safe: no parallel operations on one instance.
- One `SaveChanges` is already a transaction for its changes; add an explicit transaction only
  when several writes, or a read followed by writes, must be atomic.
- Keep external calls (WooCommerce, SMTP) outside a long database transaction where possible.
- Retries must not duplicate business effects; uniqueness is enforced by database constraints,
  and an application pre-check does not replace a unique constraint.
- Concurrency tokens and constraints wherever they protect an invariant; a lost update is
  reported as a conflict, never silently overwritten.
- No outbox by default. A durable coordination mechanism is required only where a use case must
  survive a crash between the database write and an external effect.
- Money, audit and idempotency follow `rules.md` invariants 2, 4 and 5 as written.
- Financial audit (SF-7): every create, change and delete of financial data calls
  `IAuditLog.RecordAsync` inside the same `IUnitOfWork` call, with the author from the command
  (`AuditAuthor.Account(accountId)`) or a system author (`WooCommerceSync`, `AutomatedJob`).
  Value format: field names are stable English camelCase property names (`amount`, `paidAt`);
  amounts are invariant decimals with a dot (`1234.50`); dates `yyyy-MM-dd`; timestamps ISO 8601
  UTC; dictionary values (status, channel, category) as stable codes, not labels; no value is
  `null`, never an empty string. Fields with personal data set `IsPersonalData`; the object label
  never contains personal data. No changed fields → no audit call. Business date and parent per
  object: order — order date; payment, refund, sales correction — own date, parent = the order;
  expense — cost date, parent = the recurring definition for a recurring occurrence;
  recurring definition — cycle start; owner withdrawal — withdrawal date.
- Audit entries are append-only, with one exception (SF-22, DANE-01): customer data anonymisation
  replaces the values of selected fields with a marker through `IAuditRedaction`
  (`Shared.Application/Audit`, rule in `AuditRecord.Redact`, implementation in
  `Reporting.Infrastructure`), in the caller's `IUnitOfWork` transaction. No other code changes a
  stored entry. `IAuditActivity` reads the time of the last entry of an object and its children
  (used for the retention start, so deleted entries count); both ports share one scope definition
  (`AuditScope`).
- Financial module contexts share one connection per DI scope (`ModuleConnection`), registered
  with `UseModuleSqlServer<TContext>(serviceProvider, schema)`. Therefore: no `AddDbContextPool`,
  no parallel queries in one scope (no `Task.WhenAll` over queries, no interleaved streaming), no
  `EnableRetryOnFailure`, no direct `Database.BeginTransaction` — use `IUnitOfWork`. A background
  job or sync creates a new DI scope per unit of work; after a rolled-back unit of work the scope
  does no further writes. `SchedulingDbContext` is the exception: it has its own connection (not
  `ModuleConnection`) and uses transactions of its own context for the job lease, so scheduler
  writes never join a job's unit of work. `IntegrationsDbContext` (SF-13: sync cursor, runs, order
  error history) follows the same pattern, so a rolled-back order write never rolls back its error
  record; Integrations calls Orders only through `IShopOrderSync`/`IShopOrderLookup`.
- Migrations run before the new code is published (SF-4), so every migration must work with the
  previous API version (expand/contract: add first, remove in a later deployment). Never edit an
  applied migration; fix forward. A new module with a database is appended to
  `deploy/scripts/contexts.sh`.
- The backend is published before the frontend, so an API change must keep the previous frontend
  working within one deployment.

### Errors and validation

Validation and authorisation run in the API independently of the UI. Errors use Problem
Details with consistent HTTP codes and expose no secrets or production details. Logs carry a
correlation ID, without passwords, tokens or unnecessary personal data.

Error classes — each is decided where it is known, never by parsing message text:

| Class | Where it is decided | Response |
|---|---|---|
| Input validation | handler result with field errors | `400` `ValidationProblem` |
| Business conflict (a rule or the current state forbids the operation) | domain / handler result | `409` or `422` Problem with `code` |
| Concurrency conflict | Infrastructure catches `DbUpdateConcurrencyException`; the port returns a conflict result | `409` Problem with `code` |
| Known constraint violation (e.g. duplicate) | Infrastructure, by SQL error number (`2601`, `2627`, as in `AccessUserStore`), translated to a result | `409` Problem with `code`, or the idempotent outcome |
| Infrastructure unavailable (no connection, timeout) | exception handler | `503`, no details |
| Unexpected program error | exception handler | `500`, no details outside Development |

**Discrepancy with the code (not changed yet):** `SecuritySetup.SelectStatusCode` maps **every**
exception with a `DbException` in its chain to `503`, so an untranslated constraint violation
(`DbUpdateException` → `SqlException`) also becomes `503`, while `DbUpdateConcurrencyException`
becomes `500`. Until a separate task narrows the fallback, each use case translates its known
constraint and concurrency cases in Infrastructure, with a test asserting the resulting status.

## Frontend

### Structure and naming

Strict TypeScript, standalone components, feature areas, lazy-loaded routes. File naming
follows the Angular CLI 22 style guide (`orders-page.ts`, no `.component` suffix). Selectors use
the `app` prefix (`app-data-table`), enforced by ESLint.

| Path in `app/src/app/` | Holds |
|---|---|
| `features/{area}/` | One feature area per main section, loaded with `loadComponent`/`loadChildren` |
| `layout/` | Application shell, main navigation (`nav-items.ts` is the single list of sections), page title strategy |
| `shared/ui/` | Reusable UI elements: `app-button`, `app-field`, `app-confirm-dialog`, `app-data-table`, `app-status-badge`, `app-loading-state`, `app-empty-state`, `app-error-state` |
| `shared/format/` | Polish formatting functions and pipes (`plnAmount`, `plDate`, `plMonth`, `plDateTime`; `formatTime`, `formatReadTime` without pipes) |
| `shared/dictionaries/` | Code-to-label dictionaries used by more than one feature area (expense categories, payment methods, order dictionaries, shop status); feature models re-export them under their own names, and feature areas never import each other |

Routes use Polish slugs without diacritics (`/pulpit`, `/zamowienia`, `/wydatki`, `/operacje`,
`/historia-zmian`, `/integracja`), with `title` and `data.section` equal to the section label.
`/` and unknown paths redirect to `/pulpit`.

### Components

Shared repeated UI elements, without building a component system in advance. Component
specifics are in `docs/DESIGN-GUID-stone-flow.md` section 6. Before adding a new element, reuse
`shared/ui/`:

- Actions use `app-button` (`primary` for the one main action of a section, `secondary`,
  `danger`); a saving button gets `busy`, which blocks repeated clicks and submit.
- Form controls sit inside `app-field` and carry `#control`; the field binds the label,
  `aria-required`, `aria-invalid` and `aria-describedby`.
- Deletion and other destructive actions are confirmed with `app-confirm-dialog`
  (`await dialog.open()`), naming the object and the effect.
- Lists use `app-data-table`: it renders one page from the API and emits page, sort and filter
  events; it never sorts or pages locally. Filters are projected with the `tableFilters`
  attribute. A column shows text from `value(row)`, or any content (status, link to the object,
  marker) from `<ng-template appTableCell="{key}" [appTableCellOf]="rows" let-row>`.
  `[mobileCards]="true"` (off by default) turns rows into cards below `md`, with the column
  header shown in every cell; other lists keep the plain table.
- Headings of empty states and dialogs are passed as `heading`, never `title` (a `title` input
  would leave a native tooltip on the host element).
- Statuses use `app-status-badge` with visible text; the tone only reinforces it.
- Amounts and dates are displayed only through `shared/format/` (`12 345,67 zł`, `28.10.2026`,
  `październik 2026`, technical times in Europe/Warsaw, missing values as „Brak danych”).
  Amounts may be passed as decimal strings from the API; the formatter does no arithmetic.

### State and data fetching

Decided in SF-6. The frontend presents results; it does not calculate finances.

- The API is called only through `HttpClient` provided by `provideAppHttp()`
  (`core/http/app-http.ts`), with **relative** URLs `/api/...` — never an absolute API address in
  the bundle. Locally `app/proxy.conf.mjs` forwards `/api` to the API
  (`STONEFLOW_API_URL`, default `http://localhost:5080`); in production the hosting rewrite does
  the same (SF-4).
- One client per area (`*-api.ts`) exposes async methods returning Promises of typed DTOs
  (`firstValueFrom` over `HttpClient`); state lives in services
  with signals (no state library). Flows that orchestrate several calls live in the service, not
  in components.
- Errors are mapped with `toApiError()` (`network` for status 0 and 5xx, `validation` for
  `ValidationProblemDetails`, `problem` with `detail` and `code`); screens branch on `code`, not on
  the text.
- XSRF is automatic (`XSRF-TOKEN` cookie → `X-XSRF-TOKEN` header on state-changing requests);
  before a state-changing auth request the session service fetches `GET /api/auth/csrf`.
- Any `401` (except the login call) is handled by the global interceptor: the session becomes
  anonymous and the user goes to `/logowanie` with „Sesja wygasła…”. Route guards are a
  convenience; the API enforces access.
- Forms use Signal Forms (`@angular/forms/signals`) with `app-field` (`#control` + `[formField]`)
  and `novalidate`; validation runs in the UI and again in the API.
- Forms show validation, save state and protection against double submission. Lists handle
  loading, empty and error states.
- Background requests (decided in SF-14, e.g. the integration status refresh): „Żądania w tle (np.
  odświeżanie stanu integracji) wysyła się tylko z widocznej karty i tylko w chwili celowej
  aktywności użytkownika (`keydown`, `pointerdown`, powrót na widoczną kartę), która nastąpiła po
  poprzednim odczycie, bo każde żądanie przedłuża sesję (DOST-01). Ruch kursora i kółko nie są
  aktywnością. Brak świeżego odczytu pokazuje się jako stan ostrożny, nie jako stan kompletny.”
  Client time limits use the RxJS `timeout` operator in the area client, before `firstValueFrom`.

| Path in `app/src/app/` | Holds |
|---|---|
| `core/http/` | `provideAppHttp()`, `toApiError()` |
| `core/auth/` | session service, auth API client, guards, 401 interceptor, safe return URL |
| `features/auth/` | `/logowanie`, `/reset-hasla`, `/reset-hasla/nowe` (outside the shell, `layout/auth-layout`) |

### Styling

Tailwind CSS for layout and spacing, daisyUI for components. Colours live centrally in CSS
variables and one custom daisyUI theme; components use semantic tokens, never local hex
codes. The only colour literals are the `--sf-*` tokens in `app/src/styles.css`; the theme
`stone-flow` maps daisyUI roles to them. Components use daisyUI semantic classes
(`btn-primary`, `bg-base-100`) or token utilities (`text-(--sf-muted)`).
`npm run lint` runs `lint:colors`, which fails on hex, `rgb()`, `hsl()` or `oklch()` literals in
`app/src/app/`. Never put graphite text on the accent (2.86:1) or use `--sf-secondary` as text or
the only field border (2.05:1). Tokens, contrast rules, states and number formats are in
`docs/DESIGN-GUID-stone-flow.md`. The target is WCAG 2.2 AA, and status is never conveyed by
colour alone.

## Business logic for `sdd-scaffold`

Business logic lives in the domain and application layers, never in endpoints or components
(`rules.md`). `sdd-scaffold` has not been used so far: SF-3 to SF-6 were implemented in full,
and `rules.md` allows `TODO` only in stubs produced by `sdd-scaffold`. What the stubs leave to
the developer is an open decision (see "Open decisions").

## Implementation order

The `sdd-plan` default (dependencies, logic, wiring, UI) applies, in this repository's terms:

1. Domain types and rules, with unit tests.
2. Application: ports, commands/queries, handlers and results.
3. Infrastructure: entity configuration, port implementations, DI and the migration (a new
   module is also appended to `deploy/scripts/contexts.sh`).
4. Api: endpoints, request/response DTOs, `EndpointSecurityTests` entries.
5. Integration tests: authorisation, persistence, constraints, concurrency.
6. Frontend: `*-api.ts` client, state service, pages and routes.

Project-level order (which task comes first) is in PRD section 13 and
`docs/tasks/_project/task-breakdown.md`.

## Verification

Backend, from the repository root:

```
dotnet restore stone-flow.slnx --locked-mode
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet build stone-flow.slnx -c Release --no-restore
dotnet test --solution stone-flow.slnx -c Release --no-build
```

Database tests use SQL Server in a container (Docker, CI) or, when `STONEFLOW_TEST_SQLSERVER` is set
to a server connection string (e.g. `Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True`),
that server; each test class gets its own database.

Frontend, from `app/`:

```
npm ci
npm run format:check
npm run lint
npm run typecheck
npm run build
npm test -- --watch=false
npm run test:scripts
node scripts/check-bundle-secrets.mjs dist
```

`npm run typecheck` also checks `app/e2e/` and `playwright.config.ts` (`tsconfig.e2e.json`).
`check-bundle-secrets.mjs` fails on server configuration key names and secret patterns in the
built bundle (and on an empty directory); its rules are listed in the script.

Deployment scripts (Git Bash on Windows; CI job `deploy-dry-run`), after a Release build, with
`STONEFLOW_TEST_SQLSERVER` (server connection string) and `SQLCMD` (authenticated `sqlcmd` command,
e.g. `sqlcmd -S (localdb)\MSSQLLocalDB -E`):

```
deploy/scripts/test-migrate.sh
RESTORE_TEST_DIR='<directory the SQL Server can write, ending with a separator>' deploy/scripts/test-verify-restore.sh
```

`RESTORE_TEST_DIR` is needed on LocalDB (no default backup path); in CI the container's
`/var/opt/mssql/data/` with `SQL_HOST_EXEC="docker exec -i <container>"`.

End-to-end tests (SF-21; CI job `e2e`), from `app/`: they start the built API
(`api/src/StoneFlow.Api/bin/Release/net10.0`, environment Development, e-mail in log mode) and
`ng serve --configuration production`, and use the database `StoneFlowE2E` on the server given in
`STONEFLOW_E2E_SQLSERVER` (connection string without a database). Locally, Chromium only:

```
dotnet build stone-flow.slnx -c Release
ConnectionStrings__StoneFlow='<server>;Database=StoneFlowE2E;…' deploy/scripts/migrate.sh   # from the repository root
npx playwright install chromium
STONEFLOW_E2E_SQLSERVER='<server connection string>' npm run e2e
```

- Ports default to 5080 (API) and 4200 (app); when a development server is running, set
  `STONEFLOW_E2E_API_PORT` and `STONEFLOW_E2E_APP_PORT`. The run fails rather than reuse a running
  server.
- One file: `npx playwright test e2e/orders.e2e.ts`; the session test alone:
  `npx playwright test --project=session --no-deps` (it logs out every session, so it runs last).
- The owner account gets at most 3 reset links per 15 minutes, so more frequent runs on the same
  database fail in global setup — wait or recreate `StoneFlowE2E`.
- Report: `app/playwright-report/index.html`; traces in `app/test-results/` (both ignored by git).

CI must check builds of both applications, types, formatting and the relevant tests.

## Review checklist

1. Is the use case one coherent intent, with each responsibility in the right layer?
2. Does every query stay a read and return a DTO (no entity, `DbContext` or `IQueryable`)?
3. Does each business rule have one owner (the domain), without copies in handlers or the UI?
4. Is the concrete resource authorised in the handler, not only "logged in"?
5. Do transactions, constraints, concurrency tokens and retries protect the invariants?
6. Does all I/O propagate `CancellationToken` and respect DI lifetimes?
7. Is every error in the right class (see "Errors and validation"), without parsing message
   text or leaking details?
8. Does every new abstraction serve a current, real need?
9. Do tests cover behaviour, failures and the relevant boundaries (authorisation, database
   constraints, invariants) — not private methods or a coverage percentage?

## Open decisions

- What `sdd-scaffold` stubs leave to the developer (domain rules, handler bodies, tests) —
  owner to decide before the first use of `sdd-scaffold`.
- Narrowing the `DbException` → `503` fallback (see "Errors and validation") — a separate code
  task with tests.

## Do not

- Add event sourcing, a message broker or separate databases without a separate
  justification.
- Rely only on an in-memory timer for scheduled work; hosting may restart or sleep the
  process.
- Lower a technology version silently if the hosting plan does not support it.
- Use preview, next or RC versions as the default production basis.
- Treat a link from the shop admin panel, or browser-supplied data, as proof of
  authentication.
- Allow preview deployments access to the production database or integrations by default.
- Treat stored Excel formula values or manual summaries as authoritative control totals.
