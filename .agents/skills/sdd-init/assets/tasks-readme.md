# Tasks

Per-task artifacts produced by the [standev-sdd](https://github.com/jakubstankowski/standev-sdd)
pipeline. One folder per task, named with the tracker issue ID.

```
{tasks_dir}/
├── _project/
│   ├── project-review.md           PASS/FAIL gate for the whole PRD
│   └── task-breakdown.md           PRD → backlog, ready to paste into the tracker
├── _archive/                       finished tasks
└── proj-12/
    ├── task.md                     status, risk, dependencies
    ├── 00-raw-task.md              the ticket, verbatim
    ├── 02-spec.md                  technical specification
    ├── 03-spec-review.md           gate: does the spec hold up?
    ├── 04-implementation-plan.md   executable development checklist
    ├── 05-code-review.md           gate: does the code hold up?
    ├── 06-implementation-done.md   delivery manifest
    └── ui/                         optional screenshots and mockups read by sdd-spec
```

There is no `01-`. The numbering is kept from the original template so artifacts sort in
pipeline order.

## Running a task

```
sdd-fetch-tasks proj-12     ticket → 00-raw-task.md
sdd-spec proj-12            answer the clarifying questions, get 02-spec.md
sdd-review-spec proj-12     gate — must not be FAIL before planning
sdd-plan proj-12            single-concern or fullstack plan
... implement ...
sdd-review-code proj-12     gate
sdd-done proj-12            delivery manifest
sdd-archive proj-12         move to _archive/ after merge
```

Configuration lives in `.sdd/config.yml`.

## What belongs here

These folders record how one task was decided and delivered. They are not
documentation. Anything that outlives the task — an architectural decision, a module
contract, a product rule — goes into the project docs, and the task artifacts link to it.
