# ADR-0001 - Architecture Boundaries and Runtime Model

**Status:** Accepted
**Date:** 2026-09-15

## Context

Switchyard has distinct business ownership boundaries but does not yet have independent teams, scaling profiles or release cadences that justify a microservice-per-context deployment model.

The portfolio must show architectural judgement rather than distributed-system complexity for its own sake.

## Decision

Switchyard will begin as one public monorepo and a modular monolith at the code/domain level.

Bounded contexts retain explicit ownership of their domain model, persistence schema and application contracts. A bounded context does not automatically become a separately deployed service.

The intended public runtime evolves toward four hosts:

- Customer Web/BFF;
- Operations Web/BFF;
- internal ASP.NET Core API;
- background Worker.

Hosts are added when they have real behaviour. Empty projects are not created merely to mirror the target diagram.

## Consequences

- Local development and repository navigation remain straightforward.
- Architecture tests can enforce context boundaries before network boundaries exist.
- Cross-context calls must still respect explicit contracts.
- Deployment boundaries can split later without redefining the domain model.
- The repository must resist direct cross-context persistence access simply because everything is in one solution.

## Rejected alternatives

- Microservice per bounded context from the first commit.
- Multiple repositories before independent release ownership exists.
- One undifferentiated application with shared domain/persistence ownership.
