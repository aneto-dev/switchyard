# ADR-0006 - Observability and Diagnostics Model

**Status:** Accepted
**Date:** 2026-09-15

## Context

Switchyard must explain long-running asynchronous workflows without turning logs into a second business database or exposing sensitive telemetry publicly.

## Decision

Use OpenTelemetry as the application instrumentation standard for traces, metrics and structured logs.

Use W3C Trace Context where supported.

Durable `CorrelationId` and `CausationId` remain independent of trace retention and survive asynchronous/restart boundaries.

Business timeline and technical telemetry are separate models.

Long-running recovery may create new traces linked to earlier work rather than maintaining one artificial immortal trace.

Metric dimensions must be bounded. Entity identifiers such as OrderId, CustomerId and DemoSessionId are not general metric labels.

Raw telemetry backends are maintainer-only. Public evaluators see curated diagnostics scoped to their DemoSession.

Observability export failure must not roll back valid business work.

## Consequences

- Outbox/inbox/retry/reconciliation state becomes first-class operational evidence.
- Health, liveness and readiness are distinct and role-specific.
- Public portfolio evidence must come from real execution.
- Logs/traces must follow the security redaction rules.

## Rejected alternatives

- Business audit history stored only in logs.
- Public unrestricted Grafana/Azure Monitor access.
- High-cardinality IDs as metric labels.
- Making telemetry availability a transaction dependency.
