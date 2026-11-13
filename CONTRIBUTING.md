# Contributing to CampusVenue

Thank you for your interest in contributing to **CampusVenue — University Venue Reservation & Event Management Platform**!

CampusVenue is structured as a production-grade enterprise platform using modern .NET, React, PostgreSQL, and Docker. To ensure maintainability, reliability, and security, all contributors must adhere to the engineering standards detailed below.

---

## 1. Code of Conduct & Core Ethics

- Treat every commit and feature as production software.
- Respect domain invariants and architectural boundaries.
- No shortcuts, mock data in production code paths, or speculative abstractions.

---

## 2. Phased Development Model

CampusVenue is developed under a phased development lifecycle.
- **Current Milestone**: Phase 0 (Foundation & Rules).
- **Rule**: Contributors must only work on deliverables within the approved phase. Do not begin work on subsequent phases until the current phase passes its Definition of Done and is formally approved.

---

## 3. Architecture & Coding Conventions

### Backend (.NET / C#)
- Adhere to the **Modular Monolith** and **Vertical Slice Architecture** guidelines.
- Business logic must remain isolated within respective modules (`Identity`, `Organizations`, `Facilities`, `Reservations`, `Events`, `Registrations`, `Seating`, `Notifications`).
- Use standard HTTP status codes and return RFC 7807 **Problem Details** on all errors.
- Never wrap `DbContext` in artificial generic repository abstractions.
- All mutating endpoints must validate business invariants and authorization server-side.

### Frontend (React / TypeScript)
- Strict TypeScript configuration (`strict: true`, no `any`).
- Use Tailwind CSS for utility styling.
- Ensure all asynchronous components properly render **Loading**, **Error**, and **Empty** states.
- Maintain responsive layout across mobile, tablet, and desktop viewports.

### Database (PostgreSQL / EF Core)
- All schema changes must be driven by forward-only EF Core migrations.
- Timestamps must be recorded in UTC.
- Enforce relational constraints (foreign keys, check constraints, unique indexes) at the database layer.

---

## 4. Development Workflow & Verification

1. **Local Setup**: Follow instructions in [docs/development/local-setup.md](docs/development/local-setup.md).
2. **Automated Testing**:
   - Backend tests: Run xUnit suite (`dotnet test`).
   - Frontend tests: Run Vitest suite (`npm run test` in `frontend/`).
   - Integration tests: Prefer real PostgreSQL containers over in-memory providers.
3. **Verification Truth**: Never report tests as passing without having executed them and verified zero failures.
4. **Git Discipline**:
   - Create focused branches for each feature (`feature/phase-<N>-<feature-name>`).
   - Provide clear commit messages describing *why* a change was made.
   - Run linter, compiler, and tests before opening pull requests.

---

## 5. Antigravity AI Agent Rules

AI pairing agents assisting on this repository must strictly obey the directives in [AGENTS.md](AGENTS.md) and all specialized rules under [.agents/rules/](.agents/rules/).
