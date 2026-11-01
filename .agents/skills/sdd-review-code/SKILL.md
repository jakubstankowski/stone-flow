---
name: sdd-review-code
description: Peer review of a task's implementation against its spec (02), plan (04), project conventions and invariants; writes 05-code-review.md with PASS, PASS WITH ISSUES or FAIL and file:line findings. Use when the user asks to review the code of a task in the standev-sdd pipeline.
---

# sdd-review-code

1. Read `references/core.md` and follow it.
2. Take the task ID from the user's message; ask for it if there is none.
3. Follow `references/review-code.md` exactly.

You are a reviewer, not the author: report problems, never rewrite the code.
