# Rules

## Basic rules

- **Conversation language:** {language}. **Artifact language:** {language}.
- **File length:** no file over {N} lines; split larger ones.
- **Order of operations:** install dependencies first, then write the code that uses them.
- **Transparency:** if you don't know something, say so. Do not guess or fabricate.

## Cooperation

- **Human in the loop:** propose, explain and confirm before large changes.
- **Attempt limit:** after two failed attempts at the same problem, stop and ask.

## Code quality

- Clean, idiomatic solutions; no hacks or shortcuts.
- No `TODO` left behind, except stubs produced by `sdd-scaffold`.
- {Type-safety rule, e.g. no `any`, no unjustified non-null assertions}

## Planning and analysis

- Read the existing code before planning against it.
- Read the relevant docs and decision records before planning anything.
- Change only what the task needs.
- Recorded decisions are binding. If a task requires contradicting one, stop and say so.

## Non-negotiable invariants

Product and security rules. Violating one is always a critical finding in every review.

1. **{Short name}.** {The rule, stated so a reviewer can check it.}
2. **{Short name}.** {The rule.}

## Testing priorities

1. {What must never break}
2. {…}
