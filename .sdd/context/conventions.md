# Conventions

How code in this repository is structured and named. Plans are written in these terms
and reviews check against them.

The repository skeleton was created in SF-3. Update this file when a task changes a convention.

## Repository layout

Monorepo that clearly separates frontend, backend, tests, documentation and deployment
configuration.

| Path | What | Stack |
|---|---|---|
| `docs/` | PRD, design guide, task artifacts | Markdown |
| `documents/` | Source requirements, DEMO workbooks, reference dashboard | docx, xlsx, webp |
| `app/` | Frontend application | Angular, Tailwind CSS, daisyUI |
| `api/src/` | Backend application | ASP.NET Core, EF Core |
| `api/tests/` | Backend tests; frontend unit tests live next to the code in `app/src` | xUnit v3 (Microsoft.Testing.Platform), NetArchTest, Testcontainers; Vitest |
| `deploy/` | Deployment configuration (in SF-3 only the local SQL Server and example variables) | Vercel, SMART ASP.NET |
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

Modular monolith, with initial modules for orders, expenses, reporting and integrations.
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

### Naming

- Projects and namespaces: `StoneFlow.{Module}.{Layer}`; shared code `StoneFlow.Shared.{Kernel|Application|Infrastructure}`.
- SQL schema = module name in lower case (`orders`, `expenses`, `reporting`, `integrations`);
  each module keeps its migration history in `{schema}.__EFMigrationsHistory`.
- Private fields `_camelCase`, constants and `static readonly` fields `PascalCase`, interfaces
  `I`-prefixed (`.editorconfig`).
- TODO: confirm routes and the endpoint style (controllers or Minimal API) — decided by the
  first task with a module endpoint (SF-6).

### Data access

- CQRS: a command is one use case with its validation, authorisation and transaction
  boundary; a query returns data shaped for a view or report.
- EF Core with SQL Server and versioned migrations. Each table has one owning module;
  schemas per module are recommended.
- Keys, constraints, transactions and concurrency control wherever concurrent edits could
  overwrite data.
- Business dates are separate from technical timestamps. Timestamps are stored in UTC and
  presented in Europe/Warsaw.
- I/O is asynchronous and supports request cancellation.
- Lists filter, sort and paginate through the API.

### Errors and validation

Validation and authorisation run in the API independently of the UI. Errors use Problem
Details with consistent HTTP codes and expose no secrets or production details. Logs carry a
correlation ID, without passwords, tokens or unnecessary personal data.

## Frontend

### Structure and naming

Strict TypeScript, standalone components, feature areas, lazy-loaded routes. File naming
follows the Angular CLI 22 style guide (`start-page.ts`, no `.component` suffix). Feature areas
live in `app/src/app/features/{area}/` and are loaded with `loadComponent`/`loadChildren`.

### Components

Shared repeated UI elements, without building a component system in advance. Component
specifics are in `docs/DESIGN-GUID-stone-flow.md` section 6.

### State and data fetching

TODO: confirm the approach in the first task that fetches data. The frontend presents results;
it does not calculate finances.
Forms show validation, save state and protection against double submission. Lists handle
loading, empty and error states.

### Styling

Tailwind CSS for layout and spacing, daisyUI for components. Colours live centrally in CSS
variables and one custom daisyUI theme; components use semantic tokens, never local hex
codes. Tokens, contrast rules, states and number formats are in
`docs/DESIGN-GUID-stone-flow.md`. The target is WCAG 2.2 AA, and status is never conveyed by
colour alone.

## Business logic for `sdd-scaffold`

TODO: confirm what the developer writes by hand. The requirements place business logic in
the domain and application layers, not in controllers or components.

## Implementation order

TODO: confirm if it differs from the default in `sdd-plan`. Project-level order is in PRD
section 13.

## Verification

Backend, from the repository root:

```
dotnet restore stone-flow.slnx --locked-mode
dotnet format stone-flow.slnx --verify-no-changes --no-restore
dotnet build stone-flow.slnx -c Release --no-restore
dotnet test --solution stone-flow.slnx -c Release --no-build   # database tests need Docker
```

Frontend, from `app/`:

```
npm ci
npm run format:check
npm run lint
npm run typecheck
npm run build
npm test -- --watch=false
```

CI must check builds of both applications, types, formatting and the relevant tests.

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
