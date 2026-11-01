# Set up standev-sdd in a repository

Create `.sdd/config.yml`, the project context files and the task folder README, based on
what the repository already says about itself. Repository mode only; in chat mode,
explain that setup needs a repository and stop.

## Step 1 — Look before asking

Read, when present:

- `.sdd/config.yml` — if it exists, this is an update: show the current values and ask
  what to change instead of starting over
- `AGENTS.md`, `CLAUDE.md`, `README.md`, `CONTRIBUTING.md`
- Package and build manifests (`package.json`, `*.csproj`, `pyproject.toml`, `go.mod`,
  `pom.xml`, `Cargo.toml` and similar) to identify the stack
- `docs/` — architecture docs, decision records (ADRs), a PRD or product doc
- Existing task folders: `docs/tasks/`, `.claude/specs/tasks/`, `specs/`, `tasks/`
- A previous install of `sdd-claude-workflow`: `.claude/context/*.md`,
  `.claude/commands/{spec,plan,done}.md`, `.claude/workflows/`

## Step 2 — Ask only what you could not find

Propose a value for each item from what you found, and let the user confirm or change it
in one round:

1. `tasks_dir` — default `docs/tasks`, or the existing task folder
2. Tracker — type (`youtrack`, `jira`, `linear`, `github`, `paste`), project key, and
   the issue URL pattern. Mention which tracker MCP or CLI you can see, if any.
3. `prd` — path to the product document, if the project has one
4. `language.artifacts` — default `en`
5. `source_of_truth` — the docs you found that every spec and plan should read
6. Invariants — if `AGENTS.md` or `CLAUDE.md` has a section of non-negotiable rules,
   propose `invariants: { source, section }` pointing at it; otherwise ask whether the
   project has any

## Step 3 — Draft the context files

Fill `assets/context/role.md`, `rules.md` and `conventions.md` from what the repository
shows: stack, layout, naming, testing, verification commands. Every statement must come
from a file you read; mark anything you could not confirm as `TODO: confirm`. Do not copy
long sections from `AGENTS.md` or `CLAUDE.md` — reference them instead.

When `.claude/context/*.md` from `sdd-claude-workflow` exists, propose moving those files
to `.sdd/context/` as they are, rather than drafting new ones.

## Step 4 — Show the plan, then write

List every file you will create or change, with a one-line purpose each, and wait for a
yes. Then write:

- `.sdd/config.yml` from `assets/config.yml`
- `.sdd/context/role.md`, `rules.md`, `conventions.md`
- `{tasks_dir}/README.md` from `assets/tasks-readme.md`, unless one already exists

Offer, without doing it unasked:

- adding a short "Spec-driven workflow" section to `AGENTS.md` or `CLAUDE.md` that points
  at `.sdd/config.yml` and lists the `sdd-*` skills
- for a previous `sdd-claude-workflow` install: moving `.claude/specs/tasks/*` into
  `tasks_dir` with `git mv`, and removing `.claude/commands/` and `.claude/workflows/`
  files that the `sdd-*` skills replace

## Report to the user

- The files written
- Every `TODO: confirm` left in the context files
- Next: `sdd-project-review` if there is a PRD to split, otherwise
  `sdd-fetch-tasks {task-id}`
