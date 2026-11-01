# Stage 2 — Specification review (gate)

Review a specification for correctness, completeness and alignment with the original
task and the project docs.

You are a **reviewer and gatekeeper**, not an author. Do not invent requirements. Do not
fix what you find. Do not modify the spec.

Run this in a fresh session when you can, and with a model at least as strong as the one
that wrote the spec. A reviewer that watched the spec being written tends to confirm it.

## Gate

`00-raw-task.md` and `02-spec.md` must exist in `{tasks_dir}/{task-id}/`. If either is
missing, stop and report which one.

## Inputs

1. `00-raw-task.md` — the original task
2. `02-spec.md` — the specification
3. The `source_of_truth` documents the spec touches, so conflicts are detected rather
   than assumed away
4. The project invariants (core section 6)

## Review criteria

### 1. Alignment with the task

- Does every requirement in the spec originate from the task?
- Are there requirements, features or constraints the task does not mention or imply?
- Are all explicit task requirements covered?

### 2. Alignment with docs and decisions

- Does the spec contradict the architecture, a module spec or the tech-stack docs?
- Does it contradict a recorded decision (ADR)?
- Does it place work in the correct layer and module?

A contradiction with a recorded decision is always a Critical Issue.

### 3. Project invariants

Check each configured invariant by name. A violation is always a Critical Issue and
forces `FAIL`.

### 4. Completeness

- Are all functional requirements fully specified?
- Are edge cases covered where relevant?
- Is UI behavior described, including loading, empty and error states?
- Are inputs, outputs and states unambiguous?

Something missing but implied is a **gap**, not a new requirement.

### 5. Assumptions

- Identify every assumption the spec makes.
- Check whether each one is listed in the Assumptions section.
- Call out hidden or implicit assumptions.

### 6. Clarity

- Are any sections vague or open to interpretation?
- Are domain terms used consistently?
- Are the acceptance criteria clear enough to implement without guessing?

### 7. Testability

- Can each acceptance criterion be verified?
- Does the spec cover the testing priorities named in the project context, if any?

### 8. Risk

- Does `risk` in `task.md` match what the spec describes (core section 7)?
- Flag anything touching money, customer data or irreversible operations that is not
  marked `high`.

### 9. Structure

- Does the spec follow the spec template?
- Is it written in `language.artifacts`?

## Output

Write a review report, not a rewritten spec, to
`{tasks_dir}/{task-id}/03-spec-review.md`, using exactly this structure:

```
# Spec review — {TASK-ID}

## Summary

- Overall assessment: PASS / PASS WITH ISSUES / FAIL
- Justification: 2–4 sentences

## Findings

### Critical Issues
(Must be fixed before planning. If none, write "None.")

### Non-Critical Issues

### Unclear or Ambiguous Sections
(Reference sections by heading.)

### Invented or Unsupported Requirements
(If none, write "None.")

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| {short name} | OK / Violated / Not applicable | |

## Risk

- Recorded: low / medium / high
- Assessed: low / medium / high, with the reason

## Assumptions Detected

(Each assumption and whether the spec states it.)

## Recommendation

- Proceed as-is / Revise specification / Revisit task definition
```

Update `task.md`: `status: spec_reviewed`, `spec_review: <result>`, `updated`. Leave
`risk` alone; report a mismatch instead.

## Rules

- Reference sections by heading, not by line number.
- Only report, classify and reference problems.

## Report to the user

- The Summary section
- The Project Invariant Check table
- Every Critical Issue
- On `FAIL`: say plainly that `sdd-plan` is blocked until the spec is revised. Otherwise:
  next is `sdd-plan {task-id}`.
