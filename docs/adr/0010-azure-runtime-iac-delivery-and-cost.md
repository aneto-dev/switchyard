# ADR-0010 - Azure Runtime, IaC, Delivery and Cost

**Status:** Accepted
**Date:** 2026-09-15

## Context

The public project needs real cloud delivery and operational evidence without Kubernetes or an unnecessarily expensive always-on estate.

## Decision

Use:

- Azure Container Apps Workload Profiles environment with the Consumption profile;
- external ingress for Customer Web and Operations Web;
- internal ingress for the core API;
- no public ingress for the Worker;
- Terraform for long-lived infrastructure;
- GitHub Actions for CI/CD;
- GitHub OIDC workload federation to Azure;
- Azure Key Vault for unavoidable secrets;
- least-privilege managed identities;
- GHCR for initial container images;
- Azure Monitor/Application Insights for public telemetry;
- on-demand Azure dev plus one stable public-demo environment;
- no permanent staging environment initially.

Public application releases use immutable images and Container Apps revisions. Database migrations run as a dedicated deployment step/job.

The normal public-demo target is approximately GBP 50/month or less before domain registration/unusual load, with budget alerts and bounded scaling.

## Consequences

- Web/API cold starts are acceptable in early portfolio stages.
- Temporary Azure dev resources are destroyed after use.
- No AKS, Front Door, API Management, Redis or ACR is added without a requirement.
- Cost measurements after deployment replace estimates.

## Rejected alternatives

- AKS/Kubernetes for v0.x.
- Permanent staging from day one.
- Long-lived Azure credentials in GitHub.
- Azure Container Registry before private-image requirements exist.
- Enterprise network products solely for architecture appearance.
