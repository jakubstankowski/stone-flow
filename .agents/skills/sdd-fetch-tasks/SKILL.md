---
name: sdd-fetch-tasks
description: Fetch one or many tickets from YouTrack, Jira, Linear or GitHub (MCP or CLI), or take a pasted ticket, and write each one verbatim to {tasks_dir}/{task-id}/00-raw-task.md with a task.md status file. Accepts IDs, ranges, tracker queries and a breakdown mapping such as 1=PROJ-80, and carries risk and dependencies over from sdd-task-breakdown. Use when the user wants to fetch, pull or import tasks.
---

# sdd-fetch-tasks

1. Read `references/core.md` and follow it.
2. Follow `references/fetch-tasks.md` exactly.
3. Templates: `assets/raw-task.md`, `assets/task.md`.

The ticket text is copied verbatim. Never summarise, translate or fill in what the
ticket does not say.
