---
name: sdd-spec
description: Turn a raw task (00-raw-task.md and optional UI screenshots) into a technical specification, 02-spec.md, after asking clarifying questions and waiting for the answers. Use when the user asks to write, generate or redo the spec for a task ID in the standev-sdd pipeline.
---

# sdd-spec

1. Read `references/core.md` and follow it.
2. Take the task ID from the user's message; ask for it if there is none.
3. Follow `references/spec.md` exactly. Its clarification step is mandatory: ask, then
   wait for the answers before writing the spec.
4. Write the spec from `assets/spec.md`.
