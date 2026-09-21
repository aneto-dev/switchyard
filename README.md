# Switchyard

Switchyard is a production-style distributed commerce and fulfilment platform built to explore the engineering problems that appear after a checkout request leaves the happy path.

The project focuses on order management, inventory reservation, payments, fulfilment, cancellation, returns, refunds, asynchronous workflows, idempotency, recovery, security, observability and cloud delivery.

## Current status

**v0.5.0 Reliable Messaging - in development**

The v0.2 Ordering core, v0.3 Inventory reservation lifecycle and v0.4 Payments milestone are complete. v0.5 starts with the transactional messaging foundation used by the durable order-placement workflow.

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
- indeterminate settlement outcomes retained for provider reconciliation
- provider-truth reconciliation for uncertain capture and void outcomes
- durable settlement reconciliation tracking with Succeeded, NotApplied and still-unknown outcomes
- concurrency-safe reconciliation that preserves the first definite provider truth
- versioned OrderAccepted integration message written atomically with order acceptance
- durable Ordering outbox with stable message, correlation and causation metadata
- leased PostgreSQL outbox claims using FOR UPDATE SKIP LOCKED
- at-least-once batch dispatcher with durable retry scheduling and lease recovery
- commit/publish crash-window coverage that makes duplicate delivery an explicit design constraint
- durable Ordering inbox keyed by consumer and message ID
- transactional inbox handling that commits local state and outgoing outbox work with the receipt marker
- duplicate and concurrent delivery suppression with conflicting message-ID detection
- handler failure rollback so unsuccessful local work remains retryable
- dedicated Worker host for continuous Ordering outbox dispatch
- Azure.Messaging.ServiceBus sender adapter with stable message, correlation and causation metadata
- Microsoft Entra authentication for cloud Service Bus and connection-string support for the local emulator
- official Azure Service Bus emulator configuration for local development
- domain, application, API and architecture tests

Inbound Service Bus handling and the durable order-placement process manager are still to come in v0.5.

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


## Worker and local Service Bus

`Switchyard.Worker` dispatches durable Ordering outbox messages to the `switchyard-events` Service Bus topic.

For the public Azure environment, configure `SWITCHYARD_SERVICEBUS_NAMESPACE` with the fully qualified namespace and use Microsoft Entra RBAC. Do not configure a cloud connection string.

For local development, Switchyard includes configuration for the official Azure Service Bus emulator. Review the Microsoft Service Bus emulator and SQL Server container license terms before setting `SWITCHYARD_SERVICEBUS_ACCEPT_EULA=Y`. Then set a strong local-only SQL password and start the emulator:

```powershell
docker compose -f infrastructure/local/servicebus-emulator.compose.yml up -d
```

Set the Ordering PostgreSQL connection string and local emulator connection string in the Worker process environment, then run:

```powershell
dotnet run --project src/Switchyard.Worker
```

The emulator is development/test infrastructure only. Inbox idempotency remains authoritative even when broker duplicate-detection features are available.
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
./scripts/verify-repository.ps1 -CleanupDockerCompose
```

## Repository layout

```text
apps/
  customer-web/
  operations-web/
src/
  Switchyard.Api/
  Switchyard.Messaging/
  Switchyard.Messaging.ServiceBus/
  Switchyard.Worker/
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
  Switchyard.Messaging.Tests/
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
- v0.4 - Payments and provider simulation - complete
- v0.5 - Messaging, outbox/inbox and durable placement workflow - in progress
- v0.6 - Fulfilment and cancellation
- v0.7 - Returns and refunds
- v0.8 - Customer and Operations product experience
- v0.9 - Security, observability and production hardening
- v1.0 - Public portfolio release

## License

MIT. See [LICENSE](LICENSE).
