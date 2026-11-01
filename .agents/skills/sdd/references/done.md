# Stage 6 — Implementation done

Record what was delivered for the task. This is a **delivery manifest**: it tells a
reviewer, tester or manager what arrived and what did not, without opinions.

You are not reviewing quality, suggesting changes or validating architecture. You are
recording completion.

## Gate

- `02-spec.md`, `03-spec-review.md`, `04-implementation-plan.md` and `05-code-review.md`
  must exist. If any is missing, stop and report which one.
- `spec_review` must not be `FAIL`.
- If `code_review` is `FAIL`, tell the user and ask whether to record delivery anyway.
- A split task gets one manifest covering every part. Each part needs its plan and its code
  review; if a part has no code review yet, stop and name it.

## Steps

1. Find and read the changed files the same way `sdd-review-code` does.
2. Run the commands from the plan's Verification section (build, tests, lint) and record
   the actual outcome of each. A command you did not run is `Not run` — never `Passed`.
   In chat mode, ask the user for the outcomes and mark each as reported by the user.
3. Write the manifest.

## Rules

- Do not invent requirements, reinterpret the spec, evaluate correctness, criticise the
  solution or propose improvements.
- Deviations are facts only: renamed, merged, moved.

## Output

Write `{tasks_dir}/{task-id}/06-implementation-done.md`:

```
# Implementation done — {TASK-ID}

## Summary
Short factual description of what was delivered.

## Specification Coverage
| Requirement | Status | Note |
|---|---|---|
(Implemented / Not implemented)

## Files
### Created
### Modified

## Planned Units
| Item from the plan | Part | Status |
|---|---|---|
(Part column only for a split task.)
(Every module, feature, component, route, service, job and migration the plan lists:
Exists / Missing)

## Database
Migrations per database, or "None."

## Tests
| Test from the plan | Status | Run result |
|---|---|---|

## Verification
| Command | Outcome |
|---|---|
(Passed / Failed / Not run)

## Deviations
Facts only. If none, write "None."

## Additional Implementation
Anything in the code that is in neither the plan nor the spec, listed as "Additional
implementation not covered by the original documents." If none, write "None."

## Open Items
Unfinished work and every unresolved finding carried over from the code review (from every
part of a split task, marked with its part). For a
`high` risk task, the first item is "Human review required before merge" until the user
confirms it happened. If none, write "None."
```

Update `task.md`: `status: done`, `updated`.

## Report to the user

- The Specification Coverage table
- The Verification table
- The Open Items section
- Offer, without doing it unasked, to post a summary comment to the tracker issue when a
  tracker MCP is connected
- Next: `sdd-archive {task-id}` once the work is merged
