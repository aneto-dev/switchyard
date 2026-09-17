# Security Overview

Security is part of the architecture from the first implementation milestone.

## Identity and sessions

- Microsoft Entra External ID is the intended delegated identity provider.
- Customer and Operations Web use separate logical application boundaries.
- Browser authentication uses OIDC Authorization Code + PKCE.
- The intended browser model is a BFF with Secure, HttpOnly session cookies.
- Access and refresh tokens are not stored in JavaScript-readable browser storage.

## Authorization

- Customer access requires identity, object ownership and valid domain state.
- Operations uses explicit roles and policy checks.
- Public identifiers are opaque but never replace authorization.
- Operations mutations use explicit business intent rather than generic status patches.

## Public demo

A public DemoSession is capability-limited and scoped to its own synthetic resources. It cannot become a general operations identity, invoke arbitrary failure controls or reset global data.

## Data and secrets

- Public demo data is synthetic.
- Raw card data is never accepted or stored.
- Secrets are never committed.
- Azure deployment uses Key Vault and managed identity where appropriate.
- Logs, traces and business timeline data follow explicit redaction rules.
