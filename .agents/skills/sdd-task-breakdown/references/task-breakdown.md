# Task breakdown (PRD → backlog)

Turn a PRD that passed `sdd-project-review` into an ordered backlog, with every task as a
block ready to paste into the tracker.

This skill does not create tickets and does not write specs. Specs are written per task
in the normal pipeline, after the tickets exist.

## Gate

1. `{project_dir}/project-review.md` must exist with `result: PASS`. If it is missing or
   `FAIL`, refuse and tell the user to run `sdd-project-review` first.
2. The PRD must not have changed since the review. For each document in the review's
   `documents` list, compare `git hash-object <path>` with the recorded `fingerprint`.
   On a mismatch, refuse and ask for a new review. When no fingerprint was recorded, ask
   the user to confirm the document has not changed since the review.
3. In chat mode, ask the user to paste both the PRD and the passing review.

## Step 1 — Find natural task boundaries

- Look for natural seams in the document: a user-visible capability, a data model, an
  integration, a background process. Do not aim for a fixed number of tasks.
- Do not split features that are tangled together. One honest larger task beats two
  artificial ones that cannot be reviewed separately.
- A task is the right size when one spec can describe it, one review can check it, and it
  can be merged on its own without breaking the product.
- Use the review's "Suggested initial breakdown" as a starting point, not as the answer.

## Step 2 — Map dependencies first

For every task, list what must exist before it can be built or verified: a data model,
an endpoint, a shared component, an integration, a decision. Record dependencies by local
number. Flag any cycle; a cycle means the boundary is wrong, so redraw it.

## Step 3 — Assign risk

| Risk | When |
|---|---|
| `high` | Money, customer or personal data, or irreversible operations. Always a human review before merge. |
| `medium` | Shared logic or data models that other tasks build on |
| `low` | Isolated or cosmetic |

Carry over every risky area flagged by the project review.

## Step 4 — Order by dependencies and risk

Order the backlog by dependencies first, then by risk — not by the order of the
document. Among tasks whose dependencies are met, put the ones that retire the most risk
or unblock the most work first, so problems surface early.

## Step 5 — Write the blocks

Local IDs are `1`, `2`, `3`… The tracker assigns the real IDs; folders are named with
those, never with the local numbers.

Write each Description in `language.artifacts` unless the user asks for another
language for the tracker.

## Output

Write `{project_dir}/task-breakdown.md`:

````
---
source_review: project-review.md
created: 2026-10-23
---

# Task breakdown — {project name}

| # | Title | Risk | Depends on | Tracker ID |
|---|---|---|---|---|
| 1 | … | high | — | |
| 2 | … | medium | 1 | |

---

## 1. {Title}

**Title**

```
{Title}
```

**Description**

```markdown
## Context
Why this task exists, and which part of the PRD it comes from (section names).

## Scope
- …

## Acceptance criteria
- [ ] …

## Out of scope
- …
```

**Metadata**

```
Breakdown ID: 1
Risk: high — human review required before merge
Depends on: none
PRD sections: …
```
````

Repeat the block for every task. The Tracker ID column stays empty;
`sdd-fetch-tasks` fills it in.

In chat mode, return the same content as one markdown block and tell the user to save it
as `{project_dir}/task-breakdown.md`.

## Report to the user

- The summary table
- Dependency cycles you broke and how
- Next: create the tickets in the tracker in table order, keeping the Metadata block in
  each description; then run `sdd-fetch-tasks` with the new IDs (for example
  `1=PROJ-80 2=PROJ-81`)
