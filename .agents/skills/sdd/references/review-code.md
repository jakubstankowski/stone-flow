# Stage 5 — Code review (gate)

Review the implemented code against the specification, the implementation plan, the
project conventions and the project invariants.

You are a **peer reviewer**, not the author. Do not rewrite code. Do not invent
requirements. Do not propose features outside the spec.

Run this in a fresh session when you can. A reviewer that wrote the code tends to
approve it.

## Gate

`02-spec.md` and `04-implementation-plan.md` must exist. If either is missing, stop and
report which one.

For a split task, review one part per run: the part the user names, otherwise the first
part without `05-code-review-{part}.md`. Read that part's plan, and read the other parts'
plans only to tell which changes belong to which part. Code from a later part is not a
finding for this one.

## Find the changed files

In repository mode:

1. `git status --porcelain` for uncommitted work.
2. If the work is committed: `git diff --name-only {base_branch}...HEAD`. Ask the user for
   the base ref when the branch layout is unusual.
3. Cross-check against the Files section of the plan. A planned file missing from the
   diff is a finding in itself.

Read every changed file in full. Do not review from the diff alone when the surrounding
code matters.

In chat mode, ask the user to paste the diff, and the full content of any file whose
surroundings matter. Say in the report that the review covers only what was pasted.

## Review dimensions

### 1. Project invariants (critical by definition)

Check each configured invariant by name against the changed code. Any violation forces
`FAIL`.

### 2. Specification coverage

Every requirement: Covered / Partial / Missing.

### 3. Conventions

Against the conventions in the context files: structure, naming, layering, data access,
state handling, styling, copy and locale rules, and anything else they define.

### 4. Null safety

Unguarded nullables, non-null assertions without justification, missing empty or
zero-result handling.

### 5. Correctness

Logic errors, off-by-one, wrong boundary conditions, race conditions, unhandled failure
paths, swallowed exceptions.

### 6. Tests

Are the tests from the plan present and meaningful? Is there a test for every invariant
the change touches?

### 7. Code smells

Single-responsibility violations, duplication, magic values, dead code, leftover `TODO`
(scaffold stubs count once the task is past scaffolding).

### 8. Accessibility (frontend)

Keyboard navigation, focus management, roles and labels on non-native controls,
contrast.

### 9. Risk

For a `high` risk task, state that a human review is required before merge, regardless
of the result.

## Output

Write `{tasks_dir}/{task-id}/05-code-review.md` — for a split task
`05-code-review-{part}.md` — using exactly this structure:

```
# Code review — {TASK-ID} (part {part}, for a split task)

## Summary

- Overall result: PASS / PASS WITH ISSUES / FAIL
- Justification: 2–4 sentences
- Risk: low / medium / high (high: human review required before merge)

## Project Invariant Violations

| Invariant | File:line | What is wrong |
|---|---|---|

(If none, write "None." Any entry here forces FAIL.)

## Conventions Violations

### Critical (must fix before merge)

### Non-Critical (should fix)

## Specification Coverage

| Requirement | Status | Note |
|---|---|---|

## Plan Deviations

(Factual differences only. If none, write "None.")

## Null Safety Issues

## Correctness Issues

## Test Gaps

## Code Smells

## Recommendation

- Merge as-is / Fix critical issues before merge / Significant rework required
```

Write "None." under every empty section.

Update `task.md`: `code_review: <result>`, `updated`, and `status: code_reviewed`. For a
split task, follow core section 4: `code_review` is the weakest result across the reviewed
parts, and `status` changes only once every part has a review.

## Rules

- Report every problem with a file path and line.
- Be specific: "`src/orders/refund.ts:34` refunds without checking the order belongs to
  the caller", not "authorization looks weak".
- Do not propose architectural changes beyond what the conventions require.

## Report to the user

- The Summary
- The Project Invariant Violations table
- The number of critical issues
- The Recommendation
- Next: fix and re-run `sdd-review-code`, review the next part, or `sdd-done {task-id}`
  once every part is reviewed
