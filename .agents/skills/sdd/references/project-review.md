# Project review (PASS/FAIL gate for the whole PRD)

Assess whether the project documentation — a PRD or a full project spec — is ready to be
split into tasks. This reviews the whole document once, before any ticket exists. It is
not `sdd-review-spec`, which checks one task against its own ticket.

| | `sdd-review-spec` | `sdd-project-review` |
|---|---|---|
| Scope | one task | the entire PRD |
| Timing | per task, before planning | once, before splitting into tasks |
| Core question | can this task be built and verified | can the whole thing be split sensibly at all |
| Output | `{tasks_dir}/{task-id}/03-spec-review.md` | `{project_dir}/project-review.md` |

## Preparation

1. Load the document from `prd` in the config, or the path the user gives. If neither
   exists, ask for it. In chat mode, ask the user to paste it.
2. Load the context files, when they exist, and the `source_of_truth` documents.
3. Review in a fresh context, with no memory of how the document was written. If the
   document was written in this session, tell the user to start a new session and run
   this skill there, and stop. A reviewer who watched the document being written tends to
   confirm its own earlier decisions.
4. Recommend running this with a stronger model than the one that wrote the document. An
   asymmetry between author and reviewer catches more real gaps.

## What to check

### Internal consistency

- Do sections contradict each other (for example "single-user system" in one place and
  several roles in another)?
- Is the terminology consistent, with no feature under two names?
- Are business assumptions (data models, pricing rules) stated once, in one place, and
  never restated differently?

### Completeness

- Does every feature have a clear "done" criterion that can be checked unambiguously?
- Are edge cases covered: returns, corrections, malformed or missing input, failure of
  external integrations?
- Is there an "out of scope" section as concrete as the "in scope" one?
- Are external integrations (APIs, webhooks, third-party services) described the same
  way everywhere they appear?

### Readiness for breakdown

- Can independent, sensible task boundaries be drawn, or are features so entangled that
  any split would be artificial?
- Can dependencies between future tasks be identified already?
- Is any part likely to turn into two tasks once someone writes its spec?

### Unambiguity

- Are requirements measurable ("responds in under 2 seconds", not "fast")?
- Does any part assume knowledge that the reader — or the agent later writing one task's
  spec — will not have?
- Are technical constraints stated without room for interpretation?

### Project invariants

- Does any part of the document contradict a configured invariant (core section 6)?
  A contradiction is blocking.

### Risk

- Which parts touch money, customer or personal data, or irreversible operations
  (payments, financial data, webhooks that change orders, deletions)? Flag every one.
  These get the tightest human oversight later, wherever they land in the backlog.

## Result

The result is always `PASS` or `FAIL`, never "looks fine". Any blocking finding means
`FAIL`.

## Output

In repository mode, compute a fingerprint of each reviewed document with
`git hash-object <path>` so `sdd-task-breakdown` can tell whether the document changed
after the review. When that is not possible, leave `fingerprint` empty.

Write `{project_dir}/project-review.md`. If it already exists, it is the previous review:
replace it, and say in the report whether the result changed.

```
---
result: PASS
reviewed: 2026-10-23
documents:
  - path: docs/PRD.md
    fingerprint: 3b18e512dba79e4c8300dd08aeb37f8e728b8dad
---

# Project documentation review — {project name}

## Result: PASS / FAIL

## ✅ Ready
- What is complete and consistent

## ❌ Blocking — fix before splitting into tasks
- {issue}: {description} → {exactly what to add or fix}
(If none, write "None.")

## ⚠️ Risky areas — tight oversight during implementation
- {area}: {why it needs extra attention}

## Suggested initial breakdown
- Possible task boundaries, if the document allows them — a starting point only, not the
  backlog
```

In chat mode, return the same content as one markdown block and tell the user to save it
as `{project_dir}/project-review.md`.

## Report to the user

- The result and every blocking item
- On `PASS`: next is `sdd-task-breakdown`
- On `FAIL`: fix the document, then run `sdd-project-review` again in a fresh session.
  Do not split a document that has not passed.
