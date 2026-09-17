# ADR-0009 - Messaging Transport and Broker Security

**Status:** Accepted
**Date:** 2026-09-15

## Context

Part 2 requires durable asynchronous messaging. Part 6 must balance cloud realism, local reproducibility, security and portfolio operating cost.

## Decision

Use Azure Service Bus Standard for the public Azure environment.

Use the official Azure Service Bus emulator for local development/testing when messaging enters the implementation milestones.

Use `Azure.Messaging.ServiceBus` behind Switchyard-owned abstractions initially.

MassTransit is not part of v0.1 by default.

Runtime broker access uses TLS and Microsoft Entra RBAC with least-privilege sender/receiver roles. Local/SAS authentication is disabled in the public deployment where the final supported configuration allows it.

Service Bus Standard's Azure-managed public service endpoint is an accepted residual risk for the synthetic demo. Premium/private endpoint is the migration path if sensitivity, compliance or workload requirements justify it.

Application inbox/idempotency remains authoritative even if broker duplicate-detection features are used later.

## Consequences

- Local transport can stay close to cloud transport.
- Broker network isolation is intentionally weaker than PostgreSQL private networking at this cost tier.
- Service Bus entities are added when real workflows need them, not in the v0.1 foundation.
- MassTransit may be reconsidered if endpoint/service complexity later earns the abstraction.

## Rejected alternatives

- RabbitMQ locally with a different cloud transport merely for convenience.
- Service Bus Premium from day one.
- Kafka.
- Treating broker duplicate detection as exactly-once business processing.
