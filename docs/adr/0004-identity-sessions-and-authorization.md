# ADR-0004 - Identity, Sessions and Authorization

**Status:** Accepted
**Date:** 2026-09-15

## Context

Switchyard has customer and operations experiences with materially different trust and privilege boundaries. Building a custom credential store would add risk without portfolio value.

## Decision

Use Microsoft Entra External ID as the initial delegated identity provider.

Browser authentication uses OIDC/OAuth 2.0 Authorization Code with PKCE and a BFF/session-cookie model.

Customer and Operations Web use separate logical application boundaries.

Customer authorization requires authenticated identity plus object ownership plus domain state.

Initial operations roles are:

- `OperationsReader`;
- `OperationsAgent`;
- `DemoController`;
- `PlatformAdmin`.

Operations mutations use explicit intent commands rather than generic status patching.

Public resource identifiers are opaque/non-sequential but are never treated as authorization secrets.

## Consequences

- Access/refresh tokens are not stored in JavaScript-readable browser storage.
- CSRF protection is required for cookie-authenticated state changes.
- Object-level authorization is tested explicitly.
- Role claims do not bypass domain rules.
- Email address is not the ownership key.

## Rejected alternatives

- Custom username/password storage.
- One shared customer/operations client and policy boundary.
- Frontend-only authorization.
- Sequential public IDs combined with obscurity.
