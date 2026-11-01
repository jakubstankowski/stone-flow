# Stage 3 — Fullstack implementation plan

Plan a task that changes both a backend and a frontend. Two specialist plans are written
independently, then checked against each other and merged.

The gate, inputs, rules and the split-into-parts step are the same as in `plan.md`. For a
split task, run the steps below once per part.

## Step 1 — Read the spec

Read `02-spec.md` and `03-spec-review.md` in full. Both specialists need the spec content
and the project context.

## Step 2 — Write the two specialist plans

**If you can run subagents in parallel** (for example Claude Code's Agent tool), start
two at the same time:

- **Backend specialist** — follows `plan-backend.md`
- **Frontend specialist** — follows `plan-frontend.md`

Give each one: the task ID, the full spec content, the paths of the context files, and
the instruction to return the plan as its response and write no files. Tell the frontend
specialist it does not have the backend contract yet and must derive the expected one
from the spec. Wait for both.

**If you cannot run subagents** (Codex without them, chat mode), write the backend plan
first by following `plan-backend.md`, then the frontend plan by following
`plan-frontend.md`. While writing the frontend plan, derive the expected contract from
the spec, not from the backend plan you just wrote — the integration check in Step 3
only catches gaps if the two sides were derived independently.

## Step 3 — Integration check

For every API call planned by the frontend:

- Does a matching endpoint exist in the backend plan?
- Do request and response names and shapes match?
- Are the method and route consistent?
- Do both sides agree on authentication, authorization and scoping (tenant, user,
  role)?

| Frontend expects | Backend provides | Gap |
|---|---|---|

If there are none, write "No integration gaps."

## Step 4 — Invariant check across the merged plan

For each project invariant, confirm where the merged plan enforces it. Any failure is a
blocker: report it to the user rather than writing a plan around it.

## Step 5 — Write the unified plan

Write `{tasks_dir}/{task-id}/04-implementation-plan.md`:

```
# Implementation Plan — {TASK-ID}

## Scope

## Backend Plan
(Backend specialist output)

## Frontend Plan
(Frontend specialist output)

## API Contract
(One endpoint table agreed by both sides)

## Integration Check
(Gaps table, or "No integration gaps.")

## Invariant Check
| Invariant | Where it is enforced | Test that proves it |
|---|---|---|

## Unified Implementation Order
(One ordered list across both sides. Take the order from the project conventions when
they define one. Otherwise: storage migrations → domain types → contracts/DTOs →
backend logic → endpoint wiring and authorization → background jobs → backend tests →
frontend models mirroring the contracts → frontend data access → shared UI components →
feature components and pages → routing and guards → end-to-end check against the
running API.)

## Tests

## Verification

## Risks
```

Update `task.md`: `status: planned`, `updated`.

## Report to the user

- Files planned: N backend, N frontend
- The API Contract table
- Integration gaps, if any
- The invariant check result
- Every risk flagged by either side
