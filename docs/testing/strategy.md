# Testing Strategy

Switchyard testing follows risk rather than a fixed pyramid percentage.

The cheapest test that proves the behaviour without weakening the evidence should be used.

## Evidence by risk

- Domain invariants: fast domain tests.
- Application branching: use-case tests around explicit ports.
- PostgreSQL constraints and concurrency: real PostgreSQL integration tests.
- Broker delivery and redelivery: real selected broker/emulator when messaging exists.
- API validation and authorization: real ASP.NET Core request-pipeline tests.
- Context/layer boundaries: executable architecture tests once those projects exist.
- End-to-end evaluator journeys: focused browser E2E tests later.
- Performance claims: reproducible load tests with environment and methodology recorded.

## Foundation

v0.1 starts with API/component tests and a Testcontainers PostgreSQL smoke test. It deliberately does not create empty domain or architecture test projects before the corresponding implementation exists.

Coverage is diagnostic. A flaky test is a delivery-system defect and blanket retry-until-green behaviour is not accepted.
