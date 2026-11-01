---
name: sdd-plan
description: Turn an approved task specification into an executable implementation plan, 04-implementation-plan.md — a single-concern plan, or a fullstack plan with backend and frontend specialists (parallel subagents where available) plus an integration check. Blocked while the spec review is FAIL. Use when the user asks to plan a task.
---

# sdd-plan

1. Read `references/core.md` and follow it.
2. Take the task ID from the user's message; ask for it if there is none.
3. Follow `references/plan.md`. For a fullstack task it sends you to
   `references/plan-fullstack.md`, which uses `references/plan-backend.md` and
   `references/plan-frontend.md`.

Plan only. Do not write implementation code.
