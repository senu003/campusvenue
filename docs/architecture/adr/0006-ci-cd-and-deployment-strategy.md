# ADR 0006: Continuous Integration and Initial Container Strategy

## Status
Accepted

## Date
2026-11-13

## Context
A production-minded engineering product requires automated quality verification on every change and predictable containerized runtime environments across developer workstations and CI runners.

## Decision
We adopt:
1. **GitHub Actions** as the primary Continuous Integration (CI) platform.
2. **Docker & Docker Compose** for local development dependency orchestration (PostgreSQL database with healthchecks) and multi-stage container builds.
3. Multi-stage Dockerfiles for both Backend (`CampusVenue.Api`) and Frontend (`frontend`).

## Justification
- **Early Defect Detection**: CI enforces compilation, unit test execution, frontend linting, type-checking, and build verification on every pull request and push to `main`.
- **Environment Parity**: Docker Compose guarantees that all contributors run an identical version of PostgreSQL 16 with matching configuration, eliminating "works on my machine" inconsistencies.
- **Reproducible Artifacts**: Multi-stage Docker builds produce lean, minimal container images without shipping build SDKs or node development dependencies to runtime.

## Consequences
- Developers must have Docker Desktop or container runtime installed to spin up local PostgreSQL services.
- Cloud deployment and managed Kubernetes/App Services are deliberately deferred to Phase 12 to maintain velocity and avoid premature cloud operational costs.
