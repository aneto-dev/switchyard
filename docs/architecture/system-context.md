# System Context

```mermaid
flowchart LR
    Visitor[Portfolio visitor / customer]
    Operator[Operations user]
    IdP[Microsoft Entra External ID]
    Provider[Payment provider or simulator]
    Switchyard[Switchyard]
    Azure[Azure platform services]

    Visitor --> Switchyard
    Operator --> Switchyard
    Switchyard --> IdP
    Switchyard --> Provider
    Switchyard --> Azure
```

## External trust boundaries

- Customer and operations browser sessions are separate.
- Credentials are delegated to Microsoft Entra External ID.
- Provider financial outcomes are verified server-side.
- Public DemoSession access is capability-limited and does not become operations access.
- PostgreSQL is private-network only in the intended Azure deployment.
- Azure Service Bus Standard uses TLS and Entra RBAC with least privilege.
