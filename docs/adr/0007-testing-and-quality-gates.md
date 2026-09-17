# ADR-0007 - Testing and Quality Gates

**Status:** Accepted
**Date:** 2026-09-15

## Context

The highest Switchyard risks are concurrency, duplicate delivery, transaction boundaries, authorization and recovery from partial failure. A fixed test-pyramid ratio or coverage target would not prove those risks.

## Decision

Testing follows risk.

Use:

- infrastructure-free domain tests for invariants;
- application/use-case tests around explicit ports;
- real PostgreSQL integration tests for persistence/transaction/concurrency claims;
- the selected real broker where broker behaviour is being claimed;
- real ASP.NET Core request-pipeline tests;
- executable architecture tests;
- deterministic barriers/fault points for concurrency, idempotency and crash-window tests;
- focused Playwright E2E journeys;
- reproducible load tests only when performance claims are made.

CI grows in staged gates and blocks merges on relevant correctness/security failures.

Coverage is diagnostic, not a vanity target. Blanket flaky-test retries are forbidden.

## Consequences

- SQLite/in-memory providers cannot stand in for PostgreSQL evidence.
- Third-party provider sandboxes do not become mandatory PR dependencies.
- CI failure artifacts must help diagnosis while respecting redaction rules.
- Performance thresholds wait for measured baselines.

## Rejected alternatives

- 100% coverage target.
- Browser E2E for every behaviour.
- Random/sleep-based race tests.
- Retry-until-green flaky-test policy.
