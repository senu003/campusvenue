# CampusVenue — Agent Directives & Engineering Governance

This document establishes the mandatory engineering standards, development workflow, and permanent constraints for all AI agents and human contributors working on **CampusVenue** (University Venue Reservation & Event Management Platform).

---

## 1. The 16 Non-Negotiable Engineering Directives

Every contributor and AI agent must strictly adhere to the following 16 core directives:

1. **Modular Monolith Architecture**: Target ASP.NET Core with C# using a Modular Monolith. Partition functionality into dedicated business modules (`Identity`, `Organizations`, `Facilities`, `Reservations`, `Events`, `Registrations`, `Seating`, `Notifications`).
2. **Vertical Slice Architecture within Business Modules**: Organize code within modules by business capability/use-case (command/query, endpoint, validation, data access, and response DTO co-located) rather than global technical horizontal layers.
3. **One Complete End-to-End Feature at a Time**: Never construct disconnected backend layers across multiple modules while leaving the frontend for later. Every feature is delivered as an integrated, runnable slice.
4. **Full-Stack Delivery Slice**: Each feature must encompass backend endpoints, PostgreSQL schema/database changes where required, frontend UI, integration, automated tests, and manual verification instructions.
5. **No Premature Microservices or Unjustified Infrastructure**: Avoid distributed microservices, message brokers (Kafka/RabbitMQ), Redis caching, MediatR, or Kubernetes until an approved Architecture Decision Record (ADR) formally justifies the operational complexity.
6. **No Speculative Database Tables**: Schema migrations must be forward-only and strictly tied to the currently active phase. Never create speculative tables or empty entities for future roadmap milestones.
7. **No Mock Runtime Business Endpoints**: Production code paths must never contain synthetic hardcoded fakes or mock APIs masquerading as completed business features. Test doubles belong exclusively within automated test suites.
8. **Secure Environment Variables & Secrets Handling**: Never commit passwords, tokens, API keys, or connection strings containing real credentials. Use `.env.example` as a template; real `.env` is strictly git-ignored.
9. **Strong Server-Side Authorization & Input Validation**: Never trust client-provided claims, roles, or tenant IDs. Mutating endpoints must validate invariants and enforce role-based and organization-scoped authorization on the server.
10. **EF Core Migration Discipline**: All relational database schema modifications must be executed through forward-only Entity Framework Core migrations with database-level constraints (foreign keys, check constraints, unique indexes).
11. **Automated Testing & Truthful Result Reporting**: Never claim tests passed unless executed and verified in the environment. Report exact commands, test counts, execution durations, and exit codes truthfully.
12. **Manual Browser Verification by the Developer**: AI agents must not autonomously drive browser automation sessions for manual verification. Provide clear, reproducible Windows PowerShell commands and explicit browser walkthrough steps for the developer.
13. **No Autonomous Git Mutations**: The agent is strictly forbidden from executing mutating Git commands (`git commit`, `git push`, `git merge`, `git branch`, `git checkout`, `git reset`, `git rebase`, `git clean`) without explicit user authorization.
14. **Continuous Documentation Updates**: Maintain `README.md`, architecture specifications, and progress logs in lockstep with the actual working code as features evolve.
15. **Explicit Phase Completion Criteria**: A development phase cannot be marked complete without satisfying all 6 completion gates: clean build, passing automated tests, infrastructure verification, secret audit, manual runbook, and clear subsequent phase scoping.
16. **No Claiming Unverified Features are Complete**: Clearly distinguish between Implemented, Verified, Planned, and Not Implemented. Never represent design documentation or planned milestones as completed code.

---

## 2. Operating Principles & Phase Boundaries

- **Production-Grade Standard**: Treat CampusVenue as a real, auditable enterprise engineering product, never as a tutorial demo or quick CRUD script.
- **Phase Boundary Enforcement**: Work strictly within the current authorized phase. **Current Phase: Phase 0 (Foundation)**. Do not implement business logic, entities, or screens for future phases without explicit approval.
- **Technology Stack**:
  - Backend: ASP.NET Core 10 LTS / C# 14, EF Core 10 with Npgsql, PostgreSQL 16.
  - Frontend: React (18/19), TypeScript (`strict: true`, zero `any`), Vite, Tailwind CSS.
  - Infrastructure: Docker, Docker Compose, GitHub Actions.

---

## 3. Specialized Governance Rules

Detailed instructions are enforced via dedicated rule files in `.agents/rules/`:
- [Backend & Architecture Standards](.agents/rules/backend-architecture.md)
- [Frontend & TypeScript Standards](.agents/rules/frontend-typescript.md)
- [Database & Migration Standards](.agents/rules/database-migrations.md)
- [Testing & Verification Standards](.agents/rules/testing-verification.md)
- [Security Standards](.agents/rules/security-standards.md)
- [Git & Change Management Standards](.agents/rules/git-change-management.md)
- [Documentation & Phase Completion Standards](.agents/rules/documentation-phase-completion.md)
