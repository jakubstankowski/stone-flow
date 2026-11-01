# Task Specification — {TASK-ID}

## Source

[{ISSUE-ID}]({issue_url})

## Goal

What is to be built, technically. One paragraph.

## Context

Where in the system this happens: which layers, modules and features, named the way the
project conventions name them.

## Scope

### In scope

- …

### Out of scope

- …

## Behavior

Step-by-step description of what happens. No code.

## Data / API

### Storage

- Schema changes per database — or "None"

### Endpoints

| Method | Route | Request | Response | Auth | Scope |
|---|---|---|---|---|---|

### Types crossing a boundary

- …

## Invariant impact

For each project invariant this task touches: how the spec respects it. Write "No project
invariants touched." if that is genuinely true.

| Invariant | How it is respected |
|---|---|

## Risk

low / medium / high, and why. High means money, customer data or irreversible operations,
and requires a human review before merge.

## UI

- Routes and pages
- Components
- States: loading, empty, error, loaded
- Design tokens and the primary action of each view
- Accessibility requirements

Write "Not applicable" for tasks without UI.

## Edge Cases

- …

## Dependencies

Tasks or decisions that must exist first. Write "None." if there are none.

## Assumptions

Every assumption made while writing this spec. Record each clarifying question and its
answer here.

- …

## Acceptance Criteria

- [ ] …

## Verification

Commands and manual checks that prove the task is done.

- build: …
- tests: …
- manual: …
