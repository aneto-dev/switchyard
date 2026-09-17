# ADR-0002 - Distributed Consistency and Reliable Messaging

**Status:** Accepted
**Date:** 2026-09-15

## Context

Ordering, Inventory, Payments and Fulfilment own different business state. A distributed transaction across them would increase coupling and operational risk.

Retries, duplicate delivery and process crashes are normal failure modes.

## Decision

Use:

- strong consistency inside one context transaction;
- eventual consistency across context boundaries;
- no distributed two-phase transaction;
- at-least-once message delivery;
- transactional outbox for durable publication;
- durable inbox/idempotent consumers;
- stable message identifiers and correlation/causation metadata;
- bounded retries followed by quarantine/dead-letter handling;
- compensation as a new business action rather than rollback.

Exactly-once processing is not claimed.

## Consequences

- Consumers must be idempotent.
- Workflow state must explain partial progress.
- Operational diagnostics must expose outbox age, retries, dead letters and stuck workflows.
- Tests must reproduce commit/publish and commit/acknowledgement crash windows.

## Rejected alternatives

- Cross-context distributed transactions.
- In-memory event publication after commit with no durable outbox.
- Treating broker duplicate detection as sufficient business idempotency.
- Claiming exactly-once delivery.
