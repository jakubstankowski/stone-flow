# Stage 4b — Scaffold (optional)

Implement everything from the plan except the business logic. The developer writes the
logic. Run this stage only when the user asks for it by name.

This is the one stage where a `TODO` stub is the deliverable rather than a rule
violation.

## Gate

- `02-spec.md`, `03-spec-review.md` and `04-implementation-plan.md` must exist.
- `spec_review` must not be `FAIL`.
- A repository is required. In chat mode, say that scaffolding needs a repository and
  stop.

## What counts as business logic

Take the definition from the project conventions when they give one (for example "the
body of every CQRS handler" or "every service method"). If they do not, treat as business
logic: the body of every function the plan lists as owning a business rule from the
spec. Tell the user which definition you are using before writing code.

## Step 1 — Read the plan

Read `04-implementation-plan.md` in full — for a split task, the plan of the part being
scaffolded (core section 5). Identify every file to create or modify, every
unit that owns business logic, and the implementation order.

## Step 2 — Implement the scaffold

Follow the plan's order.

| Item | How far you go |
|---|---|
| Migrations and schema | Fully |
| Entities, types and mappings | Fully |
| Request/response contracts | Fully |
| Validation | Fully |
| Routing, endpoint registration, auth wiring | Fully |
| Authorization and scope resolution | Fully — infrastructure, not business logic |
| Dependency registration | Fully |
| Frontend models, data access, components, routes, guards, styling | Fully |
| Business-logic units | Signature only, body is a stub |

Every stub body states its contract and fails loudly when called. Write it in the
project's language, with the same five parts:

```
TODO: implement
Contract: one sentence — what this unit must do
Inputs: key parameters and their meaning
Returns: what is returned, and when
Edge cases: nulls, not found, validation, business rules
<throw the language's "not implemented" error>
```

Do not write a single line of business logic in a stub.

When a frontend piece depends on a stubbed unit, wire it against the contract and keep
its loading and error states working.

## Output

- The scaffolded source files.
- Update `task.md`: `status: scaffolded`, `updated`.

## Report to the user

```
Scaffold complete

Implemented:
- every created or modified file

Yours to implement:
- Unit.method — file path
  Contract: the contract line from the stub
```

Then: implement each stub, run the plan's verification commands, and continue with
`sdd-review-code {task-id}`.
