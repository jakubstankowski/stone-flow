# Conventions

How code in this repository is structured and named. Plans are written in these terms
and reviews check against them.

No application code exists yet. Everything here comes from the PRD and the requirements
documents; update this file when the projects are initialised.

## Repository layout

Monorepo that clearly separates frontend, backend, tests, documentation and deployment
configuration.

| Path | What | Stack |
|---|---|---|
| `docs/` | PRD, design guide, task artifacts | Markdown |
| `documents/` | Source requirements, DEMO workbooks, reference dashboard | docx, xlsx, webp |
| TODO: confirm | Frontend application | Angular, Tailwind CSS, daisyUI |
| TODO: confirm | Backend application | ASP.NET Core, EF Core |
| TODO: confirm | Tests | TODO: confirm |
| TODO: confirm | Deployment configuration | Vercel, SMART ASP.NET |

Pinned in the repository: SDK, runtime and Node versions, the frontend lockfile,
`global.json` and central .NET package versions.

## Backend

### Structure

Modular monolith, with initial modules for orders, expenses, reporting and integrations.
Onion layers: domain (business rules), application (use cases), infrastructure (data access
and external services), API (HTTP). The shared part stays small and holds no module logic.
TODO: confirm the example path for a new feature.

### Naming

TODO: confirm files, types, functions and routes.

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

Strict TypeScript, standalone components, feature areas, lazy-loaded routes. TODO: confirm
file and folder naming.

### Components

Shared repeated UI elements, without building a component system in advance. Component
specifics are in `docs/DESIGN-GUID-stone-flow.md` section 6.

### State and data fetching

TODO: confirm the approach. The frontend presents results; it does not calculate finances.
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

```
TODO: confirm build command
TODO: confirm test command
TODO: confirm lint and format command
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
