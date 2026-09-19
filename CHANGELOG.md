# Changelog

All notable Switchyard changes will be recorded here.

## [Unreleased]

### Added

- First Ordering domain slice with the Order aggregate and order lines.
- Application use case for creating pending orders through explicit ports.
- Ordering-owned PostgreSQL persistence through EF Core and Npgsql.
- Initial Ordering schema migration and order-number sequence.
- Domain, application, architecture and real PostgreSQL persistence tests.
- Ordering HTTP endpoints for creating and reading orders.
- Durable idempotent order acceptance with retry and concurrent-duplicate coverage.
- Inventory stock and reservation domain model.
- Durable reservation idempotency and last-stock concurrency protection.
- Idempotent reservation release and batched expiry execution.
- Release/expiry race protection that returns reserved stock once.
- Payment authorisation with durable internal and provider idempotency.
- Deterministic payment-provider simulation for authorised, declined and indeterminate outcomes.
- Payments-owned PostgreSQL persistence with concurrent duplicate-authorisation coverage.
- Provider-truth reconciliation for indeterminate payment authorisations.
- Durable reconciliation attempt tracking with authorised, declined and still-unknown outcomes.
- Durable capture and void workflows after successful payment authorisation.
- Provider-idempotent settlement attempts with capture/void mutual exclusion and indeterminate outcomes.

## [0.1.0] - 2026-09-16

### Added

- API, Customer Web and Operations Web foundation.
- PostgreSQL 18 local development with Docker Compose.
- Real PostgreSQL integration testing with Testcontainers.
- xUnit v3 tests on Microsoft Testing Platform.
- Architecture decision records and supporting architecture notes.
- GitHub Actions CI, CodeQL and Dependabot.
- Repository verification covering the local quality gates.

### Fixed

- Corrected .NET 10 test discovery so the test projects execute in CI.
- Corrected the PostgreSQL 18 data-volume mount used by local Compose.

### Scope

v0.1.0 contains the engineering foundation only. Order-management behaviour starts in v0.2.0.
