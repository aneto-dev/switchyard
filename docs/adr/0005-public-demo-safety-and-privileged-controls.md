# ADR-0005 - Public Demo Safety and Privileged Controls

**Status:** Accepted
**Date:** 2026-09-15

## Context

The portfolio needs a low-friction evaluator experience, but public visitors must not receive general operations or chaos-engineering privileges.

## Decision

Provide a short-lived, capability-limited `DemoSession` for the frictionless public demo.

A DemoSession may:

- create and inspect only its own synthetic demo resources;
- select from a bounded allowlist of named scenarios;
- view a curated diagnostic/business timeline for its own workflow.

It may not:

- enter the general Operations Web;
- search unrelated orders;
- invoke arbitrary failure injection;
- reset global data;
- inspect raw telemetry/messages;
- access another DemoSession.

Generic/deeper failure controls require `DemoController`.

There is no unauthenticated global reset endpoint. Demo data is synthetic and automatically expired/cleaned.

## Consequences

- Part 1's early "open operations view" wording is refined to a curated DemoSession diagnostics view for anonymous evaluators.
- The full Operations Web remains authenticated and role protected.
- Public scenario APIs require rate limiting and cost bounds.
- Demo cleanup becomes observable background work.

## Rejected alternatives

- Shared public admin account.
- Public unrestricted operations console.
- Public arbitrary exception/delay controls.
- Manual database cleanup as the normal reset process.
