# Switchyard

Switchyard is a production-style distributed commerce and fulfilment platform built to explore the engineering problems that appear after a checkout request leaves the happy path.

The project focuses on order management, inventory reservation, payments, fulfilment, cancellation, returns, refunds, asynchronous workflows, idempotency, recovery, security, observability and cloud delivery.

## Current status

**v0.1.0 Foundation - released**

The first release is deliberately small. It establishes the runtime boundaries, local development setup, real PostgreSQL testing, CI and security automation before product behaviour is added.

Order management starts in v0.2.

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

## Foundation stack

- .NET 10 LTS and ASP.NET Core
- Next.js 16 with TypeScript and Tailwind CSS
- PostgreSQL 18
- xUnit v3 on Microsoft Testing Platform
- Testcontainers for real PostgreSQL integration tests
- Docker Compose for local dependencies
- GitHub Actions and CodeQL

Later milestones introduce Azure Service Bus, OpenTelemetry, Azure Container Apps, Terraform and Azure deployment when the corresponding behaviour exists.

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
dotnet restore Switchyard.sln
dotnet build Switchyard.sln -c Release --no-restore
dotnet test Switchyard.sln -c Release --no-build
npm run lint
npm run typecheck
npm run build:web
```

## Verification

Run the complete repository check with an ephemeral local PostgreSQL dependency:

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
tests/
  Switchyard.Api.Tests/
  Switchyard.IntegrationTests/
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
- v0.2 - Order Management Core - next
- v0.3 - Inventory Reservation
- v0.4 - Payments and provider simulation
- v0.5 - Messaging, outbox/inbox and durable placement workflow
- v0.6 - Fulfilment and cancellation
- v0.7 - Returns and refunds
- v0.8 - Customer and Operations product experience
- v0.9 - Security, observability and production hardening
- v1.0 - Public portfolio release

## License

MIT. See [LICENSE](LICENSE).
