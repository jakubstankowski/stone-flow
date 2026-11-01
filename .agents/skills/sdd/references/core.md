# standev-sdd — core rules

Every `sdd-*` skill follows this file before running its stage. It defines where things
live, how a task is identified, which stage may run when, and how to behave when there
is no repository to read.

---

## 1. Pipeline

```
PRD ─► sdd-project-review (PASS/FAIL) ─► sdd-task-breakdown ─► tickets in the tracker
                                                                     │
          ┌──────────────────────────────────────────────────────────┘
          ▼
   sdd-fetch-tasks ─► sdd-spec ─► sdd-review-spec ─► sdd-plan ─► implement
          ─► sdd-review-code ─► sdd-done ─► sdd-archive
```

| Stage | Skill | Reads | Writes |
|---|---|---|---|
| Project review | `sdd-project-review` | the PRD | `{project_dir}/project-review.md` |
| Breakdown | `sdd-task-breakdown` | PRD + passing project review | `{project_dir}/task-breakdown.md` |
| 0. Fetch | `sdd-fetch-tasks` | tracker issue(s) or pasted text | `00-raw-task.md`, `task.md` |
| 1. Spec | `sdd-spec` | `00-raw-task.md` + `ui/` | `02-spec.md` |
| 2. Spec review | `sdd-review-spec` | `00` + `02` | `03-spec-review.md` |
| 3. Plan | `sdd-plan` | `02` + `03` | `04-implementation-plan.md` |
| 4. Scaffold (optional) | `sdd-scaffold` | `04` | source files with stubs |
| 4. Implement | — (the developer or the agent) | `04` | source files |
| 5. Code review | `sdd-review-code` | source + `02` + `04` | `05-code-review.md` |
| 6. Done | `sdd-done` | source + all artifacts | `06-implementation-done.md` |
| Archive | `sdd-archive` | `task.md` | moves the folder to `{archive_dir}` |

There is no `01-`. The numbering is kept from the original template so existing task
folders stay valid.

The project-level stages are optional. A team that already has tickets starts at
`sdd-fetch-tasks`; a team without a tracker starts at `sdd-spec` with a hand-written
`00-raw-task.md`.

---

## 2. Configuration

Read `.sdd/config.yml` from the repository root. If it does not exist, use the defaults
below, tell the user once that `sdd-init` creates the file, and continue.

| Key | Default | Meaning |
|---|---|---|
| `tasks_dir` | `docs/tasks` | One folder per task lives here |
| `archive_dir` | `{tasks_dir}/_archive` | Where `sdd-archive` moves finished tasks |
| `project_dir` | `{tasks_dir}/_project` | Project review and breakdown output |
| `prd` | none | Path to the PRD read by the project-level stages |
| `task_id_case` | `lower` | Folder name case: `lower` (`proj-12`) or `upper` (`PROJ-12`) |
| `tracker.type` | `paste` | `youtrack`, `jira`, `linear`, `github` or `paste` |
| `tracker.project` | none | Tracker project key, e.g. `PROJ` |
| `tracker.issue_url` | none | URL pattern with `{ISSUE_ID}`, used in artifacts |
| `context` | see below | Files loaded as permanent context by every stage |
| `source_of_truth` | `[]` | Docs read before spec, review and plan (globs allowed) |
| `invariants` | none | Non-negotiable project rules, see section 6 |
| `language.conversation` | match the user | Language used when talking to the user |
| `language.artifacts` | `en` | Language of every file written under `tasks_dir` |
| `base_branch` | `main` | Base ref for finding changed files |

Default `context`, in order, skipping files that do not exist:

1. `.sdd/context/role.md`
2. `.sdd/context/rules.md`
3. `.sdd/context/conventions.md`
4. `AGENTS.md`, then `CLAUDE.md` — only when none of the three above exists

Never invent a config value. If a stage needs a value that is missing (for example
`prd` for `sdd-project-review`), ask the user for it.

---

## 3. Task IDs and folders

- The tracker ID keeps its own case in artifacts (`PROJ-12`).
- The folder name follows `task_id_case`: `{tasks_dir}/proj-12/`.
- Accept the ID in any case from the user and normalise it.
- A task without a tracker uses any short slug the user gives (`login-rate-limit`).
- Local breakdown numbers (`1`, `2`, `3`) are never folder names. A folder is created
  only once the tracker has assigned the real ID.

Task folder:

```
{tasks_dir}/{task-id}/
├── task.md                     status and metadata
├── 00-raw-task.md              the ticket, verbatim
├── 02-spec.md
├── 03-spec-review.md
├── 04-implementation-plan.md
├── 05-code-review.md
├── 06-implementation-done.md
└── ui/                         optional screenshots and mockups
```

### Tasks split into parts

A task too large for one reviewable change is delivered in parts, each with its own plan
and code review. Parts are lowercase letters; the spec, spec review and delivery manifest
stay single:

```
02-spec.md
03-spec-review.md
04-implementation-plan-a.md     04-implementation-plan-b.md
05-code-review-a.md             05-code-review-b.md
06-implementation-done.md       covers every part
```

- `sdd-plan` proposes a split; it never splits without the user's yes. The user can also
  ask for one.
- A part is a change that can be merged on its own. Its plan says which parts must be
  merged before it.
- `task.md` lists the parts in `parts: [a, b]`. Without that key (older folders), find the
  parts from the file names.
- Wherever this file and the stage files name `04-implementation-plan.md` or
  `05-code-review.md`, a split task uses the part file instead, and a rule about "the plan"
  or "the code review" applies to every part.
- A split task never has an unsuffixed `04` or `05` next to the part files.

---

## 4. Task status — `task.md`

`task.md` starts with YAML frontmatter. Every stage that writes an artifact updates it.
Create it from `assets/task.md` if it does not exist yet.

```yaml
---
id: PROJ-12
title: Short title from the ticket
status: specified
risk: medium
depends_on: [PROJ-10]
parts: []
spec_review: null
code_review: null
source: https://tracker.example.com/issue/PROJ-12
created: 2026-10-23
updated: 2026-10-23
archived_at: null
---
```

| `status` | Set by | Meaning |
|---|---|---|
| `fetched` | `sdd-fetch-tasks` | `00-raw-task.md` exists |
| `specified` | `sdd-spec` | `02-spec.md` exists |
| `spec_reviewed` | `sdd-review-spec` | `03-spec-review.md` exists; result in `spec_review` |
| `planned` | `sdd-plan` | `04-implementation-plan.md` exists |
| `scaffolded` | `sdd-scaffold` | stubs are in place, handler bodies are not |
| `code_reviewed` | `sdd-review-code` | `05-code-review.md` exists; result in `code_review` |
| `done` | `sdd-done` | `06-implementation-done.md` exists |
| `archived` | `sdd-archive` | folder moved to `archive_dir` |

`spec_review` and `code_review` hold `PASS`, `PASS WITH ISSUES` or `FAIL`. For a split task,
`code_review` holds the weakest result across the parts reviewed so far, and `status` moves
to `code_reviewed` only once every part has a code review.
`risk` holds `low`, `medium` or `high` (section 7), or `null` when unknown.
`parts` lists the part letters of a split task, or stays empty.

**Folders without `task.md`.** Older task folders have no status file. Infer the status
from the highest artifact present, counting part files (`04-implementation-plan-a.md`) as
their stage, and read review results from the `Overall assessment` or `Overall result` line
of each review artifact. Create `task.md` the next time a stage
writes into that folder.

**Re-running a stage.** If the artifact a stage writes already exists, show the user what
would change and ask before overwriting. Rewriting `02-spec.md` makes `03` and later
artifacts stale: set `status` back to `specified`, clear `spec_review`, and tell the user
which artifacts now need to be regenerated. Apply the same rule to every later stage.

---

## 5. Gates

Check the gate before doing any work. A failed gate stops the stage; report which input
is missing or which result blocks it, and name the skill that unblocks it.

| Stage | Requires |
|---|---|
| `sdd-task-breakdown` | `project-review.md` with `Result: PASS` for the current version of the PRD |
| `sdd-spec` | `00-raw-task.md` |
| `sdd-review-spec` | `00-raw-task.md`, `02-spec.md` |
| `sdd-plan` | `02-spec.md`, `03-spec-review.md`, and `spec_review` is not `FAIL` |
| `sdd-scaffold` | `02`, `03`, `04` (of the part being scaffolded), and `spec_review` is not `FAIL` |
| `sdd-review-code` | `02-spec.md`, `04` (of the part being reviewed) |
| `sdd-done` | `02`, `03`, `04` and `05` for every part, and `spec_review` is not `FAIL` |
| `sdd-archive` | `status: done` |

For a split task, `sdd-scaffold` and `sdd-review-code` work on one part at a time: the part
the user names, otherwise the first part that does not have the stage's artifact yet. Say
which part you picked.

If `code_review` is `FAIL`, `sdd-done` tells the user and asks before recording delivery.
A violated project invariant forces `FAIL` in every review stage.

---

## 6. Project invariants

Invariants are the project's non-negotiable rules — tenant isolation, approval before
publishing, secrets handling, and the like. Every review stage checks each one by name,
and any violation is a critical finding.

`invariants` in the config takes one of two forms:

```yaml
invariants:
  source: CLAUDE.md
  section: Non-negotiable invariants
```

```yaml
invariants:
  - Every query is scoped to the tenant resolved from the session.
  - Nothing is published without a recorded human approval.
```

With `source`, read the named section and treat each list item in it as one invariant.
Review tables get one row per invariant, using a short name for each. When no invariants
are configured, review stages write "No project invariants configured." in place of the
table.

---

## 7. Risk levels

| Risk | Meaning | Consequence |
|---|---|---|
| `high` | Money, customer data, or irreversible operations | Always a human review before merge |
| `medium` | Shared logic or data models used by other parts | Review against every consumer |
| `low` | Isolated or cosmetic change | Normal review |

`sdd-task-breakdown` assigns the risk; `sdd-fetch-tasks` copies it into `task.md`. A spec
or plan that reveals a higher risk raises it and says why. `sdd-review-code` and
`sdd-done` repeat the human-review requirement for every `high` task.

---

## 8. Context loading

Before the stage's own steps:

1. Load every `context` file from the config.
2. Load the `source_of_truth` documents relevant to the area the task touches.
3. Where code and docs disagree, say so instead of silently picking one.
4. Where the task would contradict a recorded decision (an ADR or similar), stop and
   tell the user — that needs a new decision, not a workaround.

---

## 9. Environments

Detect which mode you are in before the first step.

**Repository mode** — you can read and write files in the user's project (Claude Code,
Codex, Cursor, Copilot and similar). Read inputs from disk, write artifacts to disk,
update `task.md`.

**Chat mode** — there is no project on disk (Claude.ai, ChatGPT, or a coding tool opened
outside a repository). Then:

- Ask the user to paste each input the stage needs: the ticket, earlier artifacts, the
  PRD, or the diff for a code review.
- Never claim to have read a file you were not given.
- Return each artifact as one fenced markdown block headed by its file name, and tell the
  user where to save it (`{tasks_dir}/{task-id}/02-spec.md`).
- Skip `task.md` updates; mention the status the task would move to.
- `sdd-scaffold` needs a repository. In chat mode, say so and stop.

---

## 10. Language

- Talk to the user in `language.conversation`; by default, the language they write in.
- Write artifacts in `language.artifacts`; by default, English.
- Keep tracker IDs, file names, code identifiers and quoted ticket text unchanged.
- Quote ticket text verbatim; never translate it.

---

## 11. Working rules

- Do not invent requirements. Every statement in an artifact traces back to an input.
- List assumptions explicitly; never bury them in prose.
- Reviewers report; they do not fix. Authors write; they do not grade themselves.
- Never record a command as passing unless you ran it and saw it pass.
- Finish a stage by telling the user what was written, the new status, and the next skill
  to run.
