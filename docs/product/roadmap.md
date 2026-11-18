# CampusVenue — Comprehensive Feature-by-Feature Product Roadmap

This document defines the complete multi-phase roadmap for CampusVenue, from Phase 0 (Foundation) through Phase 13 (Optional 3D Visualization).

---

## Roadmap Overview & Milestone Status

| Phase | Milestone Title | Status | Primary Focus |
| :---: | --------------- | :----: | ------------- |
| **Phase 0** | **Enterprise Foundation & Architecture** | **Completed & Verified** | Repository setup, 16 governance rules, .NET 10 LTS, dockerized PG, CI pipeline, health endpoints, minimal frontend shell |
| **Phase 1A.1** | **Secure Sign-In & Sign-Out (Auth Core)** | **Completed & Verified** | ASP.NET Identity, secure HttpOnly cookie auth, CSRF protection, lockout, session state, React login & account screen |
| **Phase 1A.2** | **User Registration & Email Verification** | **Completed & Verified** | Self-service sign-up, password policy, local in-memory mailbox (`/dev/mailbox`), email confirmation, unconfirmed account login defense |
| **Phase 1A.3** | Password Recovery & Reset | *Planned* | Secure password reset requests, reset tokens, and recovery workflow |
| **Phase 1B** | Roles & Authorization Policies (RBAC) | *Planned* | System roles (`UniversityAdmin`, `FacilityAdmin`, `Coordinator`, `Participant`), permission policies, access control |
| **Phase 1C** | University Organizations & Tenancy | *Planned* | Faculties, departments, student societies, coordinator membership assignment, organizational scoping |
| **Phase 2** | Buildings & Reservable Spaces | *Planned* | Space catalog, capacities, audio-visual equipment, accessibility tags, photo galleries |
| **Phase 3** | Availability Calendar & Constraints | *Planned* | Timetable blockouts, setup/teardown buffers, maintenance windows, interval math |
| **Phase 4** | Coordinator Space Requests & Approvals | *Planned* | Reservation workflows, approval state machine, notifications, conflict prevention |
| **Phase 5** | Explainable Hall Recommendations | *Planned* | Rule-based recommendation engine, score explanations, capacity fit, equipment match |
| **Phase 6** | Event Creation & Publication | *Planned* | Event lifecycle, multi-session management, public calendar, banners, draft/publish states |
| **Phase 7** | Participant Registration & Admission Modes | *Planned* | Assigned seating vs General Admission vs No Registration, participant tickets, QR codes |
| **Phase 8** | Interactive 2D Hall Layout Editor | *Planned* | Canvas/SVG layout builder, seat tiers, sections, rows, layout versioning |
| **Phase 9** | Concurrent-Safe Seat Reservation & Allocation | *Planned* | High-concurrency seat locking, 10-minute holds, ACID transactions, double-booking prevention |
| **Phase 10** | Real-Time Updates & Reliable Notifications | *Planned* | SignalR hub for live seat status & approvals, background worker email delivery |
| **Phase 11** | Attendance, Audit History & Reporting | *Planned* | QR check-in scanning, attendance logs, append-only audit trail, exportable analytics |
| **Phase 12** | Cloud Deployment, Observability & Hardening | *Planned* | Container registry, OpenTelemetry, Prometheus, rate limiting, security audits |
| **Phase 13** | 3D Hall Visualization (Optional) | *Planned* | Three.js / WebGL perspective hall walkthrough, seat sightline previews |

---

## Milestone Specifications

### Phase 0 — Enterprise Foundation, Rules, Architecture & CI
- **Business Objective**: Establish a rock-solid, production-grade monorepo foundation, development rules, containerized dependencies, automated CI, and architectural documentation before writing any business domain logic.
- **Backend Deliverables**:
  - ASP.NET Core 10 API (`CampusVenue.Api`) with Modular Monolith structure.
  - Environment-based configuration, structured logging, centralized RFC 7807 Problem Details exception handler.
  - Health check endpoint `/health` verifying service and PostgreSQL connectivity.
  - Development OpenAPI / Swagger setup.
  - Unit and architecture test suite (`CampusVenue.Tests`).
- **Frontend Deliverables**:
  - React + TypeScript (`strict: true`, no `any`) + Vite + Tailwind CSS shell (`frontend/`).
  - Landing / System status view demonstrating backend `/health` connectivity.
  - Standardized asynchronous UI states (loading, error, success).
  - Vitest + React Testing Library test suite.
- **Database Changes**:
  - PostgreSQL container configuration with persistent volume in `docker-compose.yml`.
  - EF Core DbContext configured with Npgsql without speculative business entities.
- **API Contracts**:
  - `GET /` &rarr; `200 OK` `{ "name": "CampusVenue API", "version": "1.0.0-phase0", "status": "Active" }`
  - `GET /health` &rarr; `200 OK` `{ "status": "Healthy", "checks": [ { "name": "postgresql", "status": "Healthy" } ] }`
- **Authorization Requirements**: Public health and metadata status.
- **Acceptance Criteria**:
  - Backend compiles with zero errors and passes unit tests in .NET 10 container.
  - Frontend builds and passes component tests.
  - Docker Compose spins up PostgreSQL and services reliably.
  - Continuous Integration runs automated tests on GitHub Actions.
- **Definition of Done**: Clean CI run, documentation approved, zero business code written prematurely.

---

### Phase 1 — Identity, Roles & University Organizations (Decomposed)

#### Phase 1A.1: Secure Sign-In & Sign-Out (Completed & Verified)
- **Business Objective**: Enable users to securely log in, verify session state, and log out using ASP.NET Core Identity with same-origin HttpOnly cookies and anti-CSRF protection.
- **Vertical Slice Scope**:
  - **Backend**: `Identity` module endpoints (`GET /api/auth/antiforgery`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`). ASP.NET Identity with password hashing, account lockout (5 attempts), rate limiting, and Data Protection key ring persistence.
  - **Frontend**: Accessible `LoginForm`, small protected `AccountScreen`, auth state context (`useAuth`), same-origin proxy configuration.
  - **Database**: Forward-only migration `20261118061958_AddIdentityCore` mapping Identity tables to snake_case (`users`, `roles`, `user_roles`, `user_claims`, etc.) with Guid keys.
  - **Tests**: 13 automated backend tests (signed-in/signed-out, lockout, throttling, CSRF, cookie attributes, session expiry) against real PostgreSQL; 7 frontend Vitest tests.
  - **Manual Verification**: Runbook provided with `coordinator@campus.internal` dev seeding.

#### Phase 1A.2: User Registration & Email Verification (Completed & Verified)
- **Business Objective**: Enable self-service registration, password requirements validation, local verification token delivery via in-memory mailbox, and email confirmation enforcement.
- **Vertical Slice Scope**:
  - **Backend**: `POST /api/auth/register`, `POST /api/auth/confirm-email`, `GET /api/auth/confirm-email`, `GET /dev/mailbox`, `GET /api/dev/mailbox`, `POST /api/dev/mailbox/clear`.
  - **Identity & Security**: Constant-time dummy password hash for duplicate registration prevention (no user enumeration), unconfirmed accounts blocked from sign-in (`EmailConfirmed = false`), tokens/links excluded from responses and logs.
  - **Frontend**: Accessible `RegisterForm` with live checklist, `ConfirmationNotice`, `EmailVerificationScreen`, embedded `DevMailbox` tab, unconfirmed login alert.
  - **Tests**: 26 backend integration tests in `CampusVenue.Tests`, 8 frontend Vitest tests.

#### Phase 1A.3: Password Recovery & Reset (Planned)
- **Business Objective**: Secure self-service password reset requests, reset tokens, and recovery workflow.

#### Phase 1B: Roles & Authorization Policies (RBAC)
- **Business Objective**: Implement institutional role hierarchies (`UniversityAdmin`, `FacilityAdmin`, `EventCoordinator`, `Participant`) and server-side policy enforcement.
- **Vertical Slice Scope**:
  - **Backend**: Role definitions, seed data for initial institutional admin, endpoint authorization attributes (`[Authorize(Policy = "...")]`), role assignment endpoints (`POST /api/users/{id}/roles`).
  - **Frontend**: Role-aware UI elements (navigation pills, admin badge, unauthorized access barriers).
  - **Database**: Identity role tables (`AspNetRoles`, `AspNetUserRoles`).
  - **Tests**: 401 Unauthorized and 403 Forbidden integration tests verifying policy evaluation.
  - **Manual Verification**: Log in as Participant and attempt admin endpoint (verify 403); log in as University Admin and verify access.

#### Phase 1C: University Organizations & Tenancy
- **Business Objective**: Model university faculties, academic departments, and student societies, assigning coordinators to specific organizational units.
- **Vertical Slice Scope**:
  - **Backend**: `Organizations` module: Organization CRUD, coordinator membership assignment (`POST /api/organizations/{id}/members`).
  - **Frontend**: Organization directory list, organization creation form for University Administrators, coordinator picker.
  - **Database**: Tables: `organizations`, `organization_memberships`.
  - **Tests**: Hierarchy integrity tests, organization-scoped query tests.
  - **Manual Verification**: Create "Department of Computer Science" under "Faculty of Engineering", assign a coordinator.

---

*(Phases 2 through 13 continue as previously defined in the master roadmap).*
