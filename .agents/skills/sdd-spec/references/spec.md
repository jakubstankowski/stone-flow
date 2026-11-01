# Stage 1 — Task → Specification

Convert a raw task into a technical specification that can be implemented without
guessing.

## Gate

`{tasks_dir}/{task-id}/00-raw-task.md` must exist. If it does not, stop and tell the user
to run `sdd-fetch-tasks {task-id}` or to write the file by hand. In chat mode, ask the
user to paste the ticket instead.

## Inputs

- `00-raw-task.md`
- Every image in `{tasks_dir}/{task-id}/ui/`, if the folder exists
- `task.md`, for risk and dependencies
- The `source_of_truth` documents relevant to the area: architecture, module specs, and
  every recorded decision (ADR) that touches it
- The existing code in the area, enough to name real modules and files

## Step 1 — Clarify before writing

Read the raw task carefully. Identify every requirement that is:

- **Ambiguous** — can be read in more than one way
- **Incomplete** — missing detail needed to implement, such as "what happens when the
  list is empty?"
- **Contradictory** — conflicts with another requirement, with the docs, or with a
  recorded decision

For each unclear point, write a specific, concrete question.

**Present the questions to the user and wait for the answers before continuing.** Do not
skip this step and do not answer the questions yourself.

If everything is clear, say "No clarifying questions — proceeding to spec." and continue.

A contradiction with a recorded decision is always a blocking question, never an
assumption.

## Step 2 — Write the specification

Use the raw task, the UI images, the answers, the docs, and `assets/spec.md`.

Rules:

- Do not invent requirements.
- List every assumption in the Assumptions section, including each answered question
  and its answer.
- Prefer existing conventions and existing module boundaries.
- Name the affected layers, modules and features explicitly, in the terms used by the
  project conventions.
- For every project invariant (core section 6) the task touches, state how the spec
  respects it in the Invariant impact section.
- If the task turns out riskier than `task.md` says (core section 7), raise `risk` and
  record why under Assumptions.

When reading UI images:

- Treat the images as the primary source of truth for the UI.
- Describe the layout, states and interactions that are visible.
- Do not guess hidden behavior; list it as a question or an assumption.

## Output

- Write `{tasks_dir}/{task-id}/02-spec.md`, only after the clarifying questions are
  answered.
- Update `task.md`: `status: specified`, `spec_review: null`, `updated`.

## Report to the user

- One paragraph on what the spec covers
- The list of assumptions it records
- Next: `sdd-review-spec {task-id}`
