# Stage 3 — Specification → Implementation plan

Turn an approved specification into a concrete, step-by-step implementation plan. You
are not writing code; you are designing how it will be written.

## Gate

- `02-spec.md` and `03-spec-review.md` must exist in `{tasks_dir}/{task-id}/`.
- `spec_review` must be `PASS` or `PASS WITH ISSUES`. On `FAIL`, stop and tell the user
  the spec must be revised and reviewed again first.

## Choose the mode

| Single-concern plan | Fullstack plan |
|---|---|
| Only one layer changes (frontend, backend, a static site, a script) | Both a backend and a frontend change |
| A simple change: styling, copy, one endpoint, one component | A new feature needs storage + API + UI |
| | The spec defines a new contract between layers |
| | The spec describes data flowing from storage through an API to the UI |

Use the user's choice when they name one ("fullstack plan"). Otherwise pick from the
table and say which mode you picked and why. For the fullstack mode, follow
`plan-fullstack.md` instead of the steps below.

## Split into parts?

Before writing, judge whether the whole task fits one reviewable change. Propose a split
(core section 3, "Tasks split into parts") when one change would be too large to review
well, or when part of it can ship and be verified on its own — for example the backend
contract and migrations first, the UI after. Name each part, its scope, and which parts it
depends on, and wait for the user's yes. Without a yes, write one plan.

For a split task, write one plan per part with the structure below, each scoped to its
part. Add `## 0. Part` at the top of each: the letter, what the part delivers, and which
parts must be merged first. Record the letters in `task.md` under `parts`.

## Inputs

1. `02-spec.md`
2. `03-spec-review.md` — non-critical issues still need to be handled in the plan
3. `task.md` — risk and dependencies
4. The relevant `source_of_truth` documents
5. The existing code in the area. Read it before planning against it. Do not plan against
   an imagined codebase; when something is created from scratch, say so.

## Rules

- Do not invent requirements and do not change the spec.
- Do not write implementation code. Signatures for illustration only.
- Every plan item traces back to a spec requirement.
- Never assume a value is non-null unless its type guarantees it.
- Express the plan in the project's own terms — real paths, real module folders, real
  component names — as defined by the conventions in the context files.
- For every project invariant the task touches, plan the step that enforces it and the
  test that proves it.
- For a `high` risk task, plan the manual review step explicitly in Verification.

## Plan structure

```
# Implementation Plan — {TASK-ID}

## 1. Scope
One paragraph: what this plan delivers, and which layers it touches.

## 2. Affected areas
| Layer | Module / feature | Why |
|---|---|---|

## 3. Files
| File path | Action (create/modify) | Purpose |
|---|---|---|

## 4. Implementation steps
Ordered checklist. Each step is small enough to verify on its own and names the files it
touches. Dependencies first (schema, contracts, types), then logic, then wiring, then UI.

## 5. Data & contracts
- Storage changes (tables, columns, indexes, migrations) — or "None"
- API endpoints with request/response shapes — or "None"
- Shared types crossing a boundary — or "None"

## 6. Edge cases & null safety
Every edge case from the spec, mapped to where it is handled.

## 7. Invariants
| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
(Or "No project invariants touched.")

## 8. Tests
| Test | Type | What it proves |
|---|---|---|

## 9. Verification
Exact commands and manual checks that prove the task is done.

## 10. Risks
Anything ambiguous, risky, or likely to need a decision. If none, write "None."
```

No "possible improvements" section and no speculative features. The document must be
executable as a development checklist.

## Output

- Write `{tasks_dir}/{task-id}/04-implementation-plan.md`, or one
  `04-implementation-plan-{part}.md` per part.
- Update `task.md`: `status: planned`, `parts` for a split task, `updated`.

## Report to the user

- For a split task, the parts with their scope and merge order
- A bullet list of every planned file (created or modified), so the scope can be checked
- The Tests table
- The Risks section
- Next: implement, or `sdd-scaffold {task-id}` to get everything but the business logic;
  then `sdd-review-code {task-id}`
