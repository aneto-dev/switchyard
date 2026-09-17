# ADR-0008 - Persistence Topology and Migrations

**Status:** Accepted
**Date:** 2026-09-15

## Context

Switchyard needs explicit context ownership without paying the operational cost of a database server per bounded context during early portfolio development.

## Decision

Use PostgreSQL.

Initially:

- one physical PostgreSQL server/database;
- context-owned schemas;
- no cross-context writes through another context's persistence implementation;
- EF Core where it provides value;
- real PostgreSQL for integration testing.

Local development uses Docker PostgreSQL.

Public deployment targets Azure Database for PostgreSQL Flexible Server with private networking, Burstable/non-HA bias for the synthetic demo and managed identity/Entra authentication where implementation validation supports it.

Database migrations run once as explicit deployment work. Application replicas do not independently race to migrate on startup.

Prefer expand/contract migration patterns when old/new revisions overlap.

## Consequences

- Schema ownership must be enforced through code/architecture tests and reviews.
- Code rollback is not assumed to reverse schema.
- Migration CI starts from an empty supported PostgreSQL database.
- A later context split can move to an independent database without redefining ownership.

## Rejected alternatives

- Database server per context from v0.1.
- Shared tables written by multiple contexts.
- SQLite as production-behaviour evidence.
- Automatic migration from every API/worker replica.
