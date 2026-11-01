# Conventions

How code in this repository is structured and named. Plans are written in these terms
and reviews check against them.

## Repository layout

| Path | What | Stack |
|---|---|---|
| {path} | {what lives here} | {stack} |

## {Layer, e.g. Backend}

### Structure

{Where a new feature goes; one example path.}

### Naming

{Files, types, functions, routes.}

### Data access

{How queries and commands are written; projections; transactions.}

### Errors and validation

{Where validation happens; error response shape.}

## {Layer, e.g. Frontend}

### Structure and naming

### Components

### State and data fetching

### Styling

{Tokens, UI library, what is not allowed.}

## Business logic for `sdd-scaffold`

{What counts as business logic that the developer writes by hand, e.g. "the body of every
command and query handler".}

## Implementation order

{The order a fullstack plan follows, if it differs from the default in `sdd-plan`.}

## Verification

```
{build command}
{test command}
{lint command}
```

## Do not

- {…}
