# ADR-0003 - Order Placement Orchestration and Financial Sequencing

**Status:** Accepted
**Date:** 2026-09-15

## Context

Order placement crosses Ordering, Inventory and Payments and must recover from partial failure, provider timeouts and retries.

The sequence also affects overselling and financial risk.

## Decision

Ordering owns a durable order-placement process manager.

The baseline flow is:

1. durably accept checkout;
2. reserve inventory;
3. authorise payment;
4. confirm the order;
5. progress to fulfilment.

Payment authorisation and capture are separate. Capture occurs at the fulfilment/dispatch commitment point defined by the Payments/Fulfilment workflow.

Payment-provider timeout can produce an `Indeterminate` outcome. The system reconciles against provider truth rather than guessing success or failure.

Compensation releases reservations or performs later financial corrections through explicit business actions.

## Consequences

- Placement progress is durable and restartable.
- Inventory is not paid before reservation succeeds.
- Provider idempotency/reconciliation identities are first-class.
- Cancellation/fulfilment races are resolved at the context that owns the contested state.
- Scenario-level tests must prove restart, duplicate and timeout recovery.

## Rejected alternatives

- Pure choreography for the placement workflow.
- Authorising payment before inventory reservation.
- Treating provider timeout as automatic decline.
- Capturing payment immediately merely because authorisation succeeded.
