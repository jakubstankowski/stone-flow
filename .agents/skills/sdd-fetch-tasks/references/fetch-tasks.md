# Stage 0 — Fetch tasks

Bring one or more tickets into the pipeline: write each ticket verbatim to
`00-raw-task.md` and start its `task.md`.

## Accepted input

- One ID: `PROJ-12`
- Several IDs or a range: `PROJ-12 PROJ-14`, `PROJ-12..PROJ-15`
- A tracker query, when the tracker supports search: `project: PROJ #Unresolved sprint: 7`
- A breakdown mapping after the tickets were created: `1=PROJ-80 2=PROJ-81 3=PROJ-82`
- Pasted ticket text, with the ID to file it under

For a query, list the matching issues first and ask before fetching more than ten.

## Sources

Use `tracker.type` from the config.

| Tracker | How to fetch |
|---|---|
| `youtrack` | YouTrack MCP: get the issue with recent comments, then the remaining comments if there are more |
| `jira` | Jira MCP (issue + comments) |
| `linear` | Linear MCP (issue + comments) |
| `github` | GitHub MCP, or `gh issue view {n} --json title,body,state,labels,assignees,comments,url` |
| `paste` | Ask the user to paste the ticket |

If the configured tracker's MCP or CLI is not available, say so and offer paste mode. If
an issue is not found, report it and continue with the others. Never invent content.

## For each ticket

1. Normalise the ID and the folder name (core section 3).
2. Write `{tasks_dir}/{task-id}/00-raw-task.md` from `assets/raw-task.md`:
   - `# {ISSUE-ID} — {summary}`
   - the metadata table: source URL, project, type, state, priority, sprint, assignee,
     reporter, estimate, created, updated, fetched (today); leave unknown cells empty
   - the description **verbatim**, with heading levels preserved
   - a `## Comments` section, each with author and timestamp, if there are any
   - a `## Attachments` section with names and URLs, if there are any
3. Do not summarise, reword, translate or clean up the description. It is the raw input
   for `sdd-spec`.
4. If the ticket contradicts itself, references a different issue ID, or lacks
   information the pipeline will need, add a short blockquote directly under the metadata
   table flagging it. Flag only; do not resolve.
5. If `00-raw-task.md` already exists, show what would change and ask before overwriting.
6. Create or update `task.md`: `id`, `title`, `source`, `status: fetched`, `created`,
   `updated`. Keep any status that is already further along.

## Risk and dependencies from the breakdown

Tickets created from `sdd-task-breakdown` carry a metadata block with a `Breakdown ID`,
a `Risk` and `Depends on` (local breakdown numbers).

- Copy `Risk` into `task.md` as `risk`.
- Translate each local dependency number into its tracker ID, using the Tracker ID
  column of `{project_dir}/task-breakdown.md` or the mapping the user gave, and write the
  result to `depends_on`.
- Record the tracker ID in the Tracker ID column of `task-breakdown.md` when it is empty.
- If a dependency has no tracker ID yet, keep the local number as `#3` in `depends_on`
  and list it in the report.

## Report to the user

| Task | Folder | State | Risk | Depends on | Flags |
|---|---|---|---|---|---|

Then the next step: `sdd-spec {task-id}` for each fetched task, starting with the ones
whose dependencies are already done.
