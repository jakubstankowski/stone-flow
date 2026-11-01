# Rules

## Basic rules

- **Conversation language:** match the user. **Artifact language:** Polish. The product UI
  is Polish.
- **File length:** TODO: confirm the maximum lines per file.
- **Order of operations:** install dependencies first, then write the code that uses them.
- **Transparency:** if you don't know something, say so. Do not guess or fabricate.

## Cooperation

- **Human in the loop:** propose, explain and confirm before large changes.
- **Attempt limit:** after two failed attempts at the same problem, stop and ask.

## Code quality

- Clean, idiomatic solutions; no hacks or shortcuts.
- No `TODO` left behind, except stubs produced by `sdd-scaffold`.
- TypeScript strict mode with strict template checking.
- No business logic in API controllers or Angular components.
- DRY applies to business knowledge and rules; abstractions come from real need, not from
  a few similar lines.

## Planning and analysis

- Read the existing code before planning against it.
- Read the relevant docs and decision records before planning anything.
- Change only what the task needs.
- Recorded decisions are binding. If a task requires contradicting one, stop and say so.
- Do not implement an area blocked by an unapproved PRD decision (open: D-08, D-09). Stop
  and name the decision.
- Scope stops at the PRD's MVP. Inventory, invoicing, accounting, tax returns, KSeF,
  purchasing, CRM, customer portal, sales forecasting and multi-company support are out of
  confirmed scope.

## Non-negotiable invariants

Product and security rules. Violating one is always a critical finding in every review.

1. **Sales separate from cash.** Sales value and real inflows and outflows stay separate. An
   unpaid order does not increase inflows, a payment does not create a second sale, and a
   planned expense is not an outflow.
2. **Backend decimal money.** Financial calculations run on the backend only. Amounts use
   decimal types with explicit precision, scale and rounding; floating-point types are never
   used for money.
3. **API-enforced access.** The API authenticates and authorises every read and write,
   including reads of financial data, regardless of what the UI shows.
4. **Retries never duplicate.** Redelivered WooCommerce orders and rerun jobs create no
   duplicates, enforced by uniqueness on source plus external ID and on recurring definition
   plus period. A retry does not overwrite ERP-owned fields.
5. **Financial audit trail.** Changes to financial data record author, UTC time, object,
   operation and extent of change. Deleting never removes history without a trace.
6. **Secrets stay server-side.** WooCommerce keys, connection strings and other secrets never
   reach the repository, the browser bundle, API responses or logs. All WooCommerce
   communication goes through the backend.
7. **Module and layer boundaries.** A module does not touch another module's tables or
   internal entities; it uses explicit interfaces or internal events. Dependencies point
   inward, and the domain does not depend on HTTP, EF Core, Angular or hosting.
8. **No identifying data in docs.** Documents, examples and screenshots do not contain the
   shop's trade name or domain, customer data or secrets.

## Testing priorities

1. Financial and domain rules, unit tested: unpaid order, payment in a different month,
   correction, refund, planned expense, no double counting.
2. Duplicate protection: redelivered order, rerun or restarted recurring job, ended cycle.
3. Authorisation and persistence, integration tested: rejected unauthorised reads and writes,
   expired sessions, database constraints, concurrent edits.
4. Integration unavailability is visible and recoverable, with missed data backfilled.
5. Key processes, end to end: add an order, save an expense, handle a recurring expense,
   present a report.
