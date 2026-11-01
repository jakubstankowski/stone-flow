---
name: sdd-scaffold
description: Optional standev-sdd stage — implement everything from a task's implementation plan except the business logic, which is left as documented stubs for the developer. Only when the user explicitly asks to scaffold a task.
disable-model-invocation: true
---

# sdd-scaffold

1. Read `references/core.md` and follow it.
2. Take the task ID from the user's message; ask for it if there is none.
3. Follow `references/scaffold.md` exactly.

Not one line of business logic goes into a stub.
