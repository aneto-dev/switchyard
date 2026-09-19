# Switchyard

Switchyard is a production-style distributed commerce and fulfilment platform built to explore the engineering problems that appear after a checkout request leaves the happy path.

The project focuses on order management, inventory reservation, payments, fulfilment, cancellation, returns, refunds, asynchronous workflows, idempotency, recovery, security, observability and cloud delivery.

## Current status

**v0.4.0 Payments - in development**

The v0.2 Ordering core and v0.3 Inventory reservation lifecycle are complete. v0.4 now includes durable payment authorisation and provider-truth reconciliation for uncertain outcomes.

Implemented so far:

- Order aggregate, order lines and order-time product/price snapshots
- strongly typed order identities and Money
- guarded order-state transitions
- application use case for creating a pending order
- explicit repository, unit-of-work and order-number ports
- Ordering-owned EF Core/Npgsql persistence
- first Ordering PostgreSQL migration and order-number sequence
- real PostgreSQL persistence coverage
- HTTP endpoints for creating and reading orders
- idempotent order acceptance backed by PostgreSQL
- Inventory stock and reservation domain model
- durable reservation request idempotency
- database-enforced last-stock concurrency protection
- configurable reservation expiry timestamp
- idempotent reservation release for compensation and cancellation
- batched expiry execution with row locking and skip-locked processing
- release/expiry race protection so reserved stock is returned once
- Payments domain, application and persistence boundaries
- durable payment authorisation idempotency per logical request/order
- stable provider idempotency keys and retained provider references
- deterministic provider simulation for authorised, declined and indeterminate outcomes
- real PostgreSQL coverage for duplicate and concurrent authorisation safety
- provider-truth reconciliation for indeterminate payment authorisations
- durable reconciliation attempt tracking and safe authorised/declined resolution
- durable capture and void workflows after successful authorisation
- provider-idempotent settlement attempts with capture/void mutual exclusion
- indeterminate settlement outcomes retained for later provider reconciliation
- domain, application, API and architecture tests

Payment settlement reconciliation, reliable messaging and the durable order-placement workflow are still to come.

## Architecture direction

Switchyard starts as a modular monorepo. Business contexts keep explicit ownership but do not become separate microservices just because they are separate bounded contexts.

The target runtime evolves toward:

```text
Customer Web/BFF ----+
                      +----> internal Switchyard API ----> PostgreSQL
Operations Web/BFF --+                 |
                                        +----> durable outbox
                                                  |
                                             Switchyard Worker
                                                  |
                                           Azure Service Bus
```

The initial bounded contexts are:

- Catalogue
- Ordering
- Inventory
- Payments
- Fulfilment
- Returns
- Notifications
- Identity/Access

See the [Architecture overview](docs/architecture/overview.md), [Context map](docs/architecture/context-map.md) and [ADRs](docs/adr/README.md).

## Stack

- .NET 10 LTS and ASP.NET Core
- Next.js 16 with TypeScript and Tailwind CSS
- PostgreSQL 18
- EF Core and Npgsql
- xUnit v3 on Microsoft Testing Platform
- Testcontainers
- Docker Compose
- GitHub Actions and CodeQL

Azure Service Bus, OpenTelemetry, Azure Container Apps and Terraform are introduced when the corresponding behaviour needs them.

## Prerequisites

- .NET SDK 10.0.401 or a compatible supported 10.0.4xx patch
- Node.js 24 LTS
- npm
- Docker Desktop or another Docker-compatible engine
- Git

## Quick start

From a clean clone at the repository root:

```powershell
Copy-Item .env.example .env
npm ci
docker compose -f infrastructure/local/compose.yml up -d postgres
./scripts/apply-ordering-migrations.ps1
./scripts/apply-inventory-migrations.ps1
./scripts/apply-payments-migrations.ps1
dotnet restore Switchyard.sln
dotnet build Switchyard.sln -c Release --no-restore
dotnet test Switchyard.sln -c Release --no-build
npm run lint
npm run typecheck
npm run build:web
```

## Ordering API

Create an order:

```http
POST /api/orders
Content-Type: application/json
Idempotency-Key: checkout-20260918-001

{
  "lines": [
    {
      "skuCode": "BIKE-001",
      "productName": "Road Bike",
      "quantity": 1,
      "unitPriceAmount": 1299.99,
      "currency": "GBP"
    }
  ]
}
```

The first successful request returns `201 Created` with a `Location` header for `GET /api/orders/{orderId}`. Repeating the same request with the same `Idempotency-Key` returns `200 OK` with the same order. Reusing that key with a different request returns `409 Conflict`.

Database migrations remain explicit deployment work. The API does not migrate the database on startup.

`/health/live` stays process-only while `/health/ready` checks the Ordering PostgreSQL dependency.
## Verification

Run the repository check with an ephemeral local PostgreSQL dependency:

```powershell
./scripts/verify-foundation.ps1 -CleanupDockerCompose
```

## Repository layout

```text
apps/
  customer-web/
  operations-web/
src/
  Switchyard.Api/
  Switchyard.Inventory.Domain/
  Switchyard.Inventory.Application/
  Switchyard.Inventory.Infrastructure/
  Switchyard.Payments.Domain/
  Switchyard.Payments.Application/
  Switchyard.Payments.Infrastructure/
  Switchyard.Ordering.Domain/
  Switchyard.Ordering.Application/
  Switchyard.Ordering.Infrastructure/
tests/
  Switchyard.Api.Tests/
  Switchyard.IntegrationTests/
  Switchyard.Ordering.Domain.Tests/
  Switchyard.Ordering.Application.Tests/
  Switchyard.Payments.Domain.Tests/
  Switchyard.Payments.Application.Tests/
  Switchyard.Architecture.Tests/
infrastructure/
  local/
docs/
  architecture/
  adr/
  security/
  testing/
scripts/
```

More projects are added only when they contain real implementation.

## Roadmap

- v0.1 - Engineering foundation - complete
- v0.2 - Order Management Core - complete
- v0.3 - Inventory Reservation - complete
- v0.4 - Payments and provider simulation - in progress
- v0.5 - Messaging, outbox/inbox and durable placement workflow
- v0.6 - Fulfilment and cancellation
- v0.7 - Returns and refunds
- v0.8 - Customer and Operations product experience
- v0.9 - Security, observability and production hardening
- v1.0 - Public portfolio release

## License

MIT. See [LICENSE](LICENSE).
