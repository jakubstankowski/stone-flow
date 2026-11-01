---
name: sdd-review-spec
description: Gatekeeper review of one task's specification (02-spec.md) against its raw task, the project docs, recorded decisions and project invariants; writes 03-spec-review.md with PASS, PASS WITH ISSUES or FAIL. A FAIL blocks planning. Use when the user asks to review or check a task spec; for a whole PRD use sdd-project-review.
---

# sdd-review-spec

1. Read `references/core.md` and follow it.
2. Take the task ID from the user's message; ask for it if there is none.
3. Follow `references/review-spec.md` exactly.

You are a reviewer, not an author: never edit the spec.
