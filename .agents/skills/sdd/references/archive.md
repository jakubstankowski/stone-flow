# Archive a finished task

Move a delivered task out of `tasks_dir` so the active list stays short. The artifacts
are kept, only their location changes.

## Gate

- `task.md` (or the inferred status, core section 4) must be `done`.
- If it is not, report the current status and the next stage, and stop. Archive anyway
  only when the user explicitly asks to, and record that in `task.md` under a `note` key.
- Repository mode only. In chat mode, tell the user which folder to move where.

## Steps

1. Accept one or more task IDs, or "all done tasks" — then list every folder in
   `tasks_dir` whose status is `done`, show the list, and ask before moving.
2. For each task, move `{tasks_dir}/{task-id}/` to `{archive_dir}/{task-id}/`. Use
   `git mv` when the folder is tracked, so history follows the files.
3. If the destination already exists, stop for that task and ask the user.
4. Update `task.md` in its new location: `status: archived`, `archived_at` (today),
   `updated`.
5. Search the repository for links to the old path (other task folders, docs, README).
   List them for the user; fix them only when the user agrees.

## Report to the user

- Every moved folder, old path → new path
- Links that still point at an old path
