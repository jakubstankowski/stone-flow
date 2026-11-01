---
name: sdd
description: Spec-driven development pipeline — PRD review, task breakdown, fetching tickets, writing and reviewing a task spec, implementation plan, scaffold, code review, delivery manifest, archive. Use when the user mentions sdd or standev-sdd, asks to spec, plan, review or close a task by its ID, or wants a PRD split into tasks. Works with a repository or by pasting text in chat.
---

# standev-sdd

One entry point to the whole pipeline. Read `references/core.md` first and follow it: it
defines the configuration, the task folder, the status file, the gates, and how to work
in chat mode without a repository.

## Pick the stage

Match the user's request to one stage, then follow that stage's reference file exactly.

| The user wants to… | Stage file |
|---|---|
| set up the workflow in a repository | `references/init.md` |
| check whether a PRD is ready to split | `references/project-review.md` |
| turn a reviewed PRD into a backlog | `references/task-breakdown.md` |
| bring tickets in from the tracker, or paste one | `references/fetch-tasks.md` |
| write the spec for a task | `references/spec.md` |
| review a task spec | `references/review-spec.md` |
| plan the implementation | `references/plan.md` (fullstack: `references/plan-fullstack.md`) |
| scaffold everything but the business logic | `references/scaffold.md` — only when asked by name |
| review the code for a task | `references/review-code.md` |
| record what was delivered | `references/done.md` |
| archive finished tasks | `references/archive.md` |

If the user names a task but no stage ("continue PROJ-12", "what's next for PROJ-12"),
read its `task.md` (or infer the status, core section 4) and propose the next stage
before running it.

If the request fits no stage, say what the pipeline covers and ask what they want.

Templates referenced by the stages are in `assets/`.
