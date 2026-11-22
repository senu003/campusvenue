# CampusVenue — Comprehensive Feature-by-Feature Product Roadmap

This document defines the complete multi-phase roadmap for CampusVenue, from Phase 0 (Foundation) through Phase 13 (Optional 3D Visualization).

---

## Roadmap Overview & Milestone Status

| Phase | Milestone Title | Status | Primary Focus |
| :---: | --------------- | :----: | ------------- |
| **Phase 0** | **Enterprise Foundation & Architecture** | **Completed & Verified** | Repository setup, 16 governance rules, .NET 10 LTS, dockerized PG, CI pipeline, health endpoints, minimal frontend shell |
| **Phase 1A.1** | **Secure Sign-In & Sign-Out (Auth Core)** | **Completed & Verified** | ASP.NET Identity, secure HttpOnly cookie auth, CSRF protection, lockout, session state, React login & account screen |
| **Phase 1A.2** | **User Registration & Email Verification** | **Completed & Verified** | Self-service sign-up, password policy, local in-memory mailbox (`/dev/mailbox`), email confirmation, unconfirmed account login defense |
| **Phase 1A.3** | **Password Recovery & Reset** | **Completed & Verified** | Secure password reset requests, reset tokens, uniform anti-enumeration responses, and security stamp session invalidation |
| **Phase 1A.4** | **Signed-In Password Change** | **Completed & Verified** | Authenticated password change in My Account, current password verification, session revocation, and security stamp invalidation |
| **Phase 1A.5** | **Sign Out Everywhere** | **Completed & Verified** | Revoke all active sessions via SecurityStamp rotation, immediate local termination, and delayed remote rejection |
| **Phase 1A.6** | **Authenticator MFA & Recovery Codes** | **Completed & Verified** | TOTP authenticator enrollment, local SVG QR codes, 10 single-use recovery codes, short-lived 2FA challenge flow, secure regenerate/disable |
| **Phase 1B.1** | **Administrator User Directory** | **Completed & Verified** | Institutional role definitions (`UniversityAdmin`, `FacilityAdmin`, `EventCoordinator`, `Participant`), operator-only elevation, paginated directory API, MFA session policy enforcement |
| **Phase 1B.2** | **Staff Role Assignment** | **Completed & Verified** | Audited staff role assignment (`FacilityAdmin`, `EventCoordinator`), transactional audit trail, concurrency conflict detection, old session invalidation |
| **Phase 1C.1** | **Venue Catalog & Verified Availability** | **Completed & Verified** | Admin venue catalog (central & departmental), verified calendar coverages, scoped blockouts, temporal availability engine with buffer collision isolation |
| **Phase 1C.2** | Space Requests & Approvals Workflow | *Planned* | Eligible slot booking request submission, venue & welfare approver state machines, transactional outbox email delivery |
| **Phase 1C.3** | Booking Confirmation & PostgreSQL Overlap Constraints | *Planned* | Coordinator approval conditions acceptance, PostgreSQL btree_gist exclusion constraints for zero double-booking |
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

#### Phase 1A.3: Password Recovery & Reset (Completed & Verified)
- **Business Objective**: Provide a secure, self-service password recovery flow for confirmed accounts using expiring tokens, uniform generic responses, URL fragment isolation, and automatic security stamp rotation to invalidate compromised sessions.
- **Vertical Slice Scope**:
  - **Backend**: `POST /api/auth/forgot-password` and `POST /api/auth/reset-password` under `Modules/Identity/Features/PasswordRecovery`.
  - **Security Invariants**:
    - Returns identical generic response for valid email formats whether unknown, unconfirmed, or confirmed (anti-enumeration defense).
    - URL fragment token isolation (`#userId=...&token=...`) ensuring reset tokens are excluded from initial HTTP requests.
    - Sends transactional reset email only for eligible confirmed accounts (`EmailConfirmed == true`).
    - Reset links/tokens and passwords excluded from API responses and server logs.
    - Protected with rate limiting (`AuthLimiter`) and anti-CSRF token verification (`X-XSRF-TOKEN`).
    - Dedicated token provider (`PasswordResetTokenProvider`) scoping 2-hour lifetime to password reset while preserving the default 24-hour lifetime for email confirmation.
    - Regression assertion ensuring password reset and email confirmation token providers are configured independently.
    - ASP.NET Core Identity rotates `SecurityStamp` upon reset; `SecurityStampValidator` configured with 5-minute interval to reject previously issued session cookies.
    - Reset tokens are strictly single-use; token reuse is rejected.
  - **Frontend**: Accessible `ForgotPasswordForm`, `ResetPasswordForm` with live requirements checklist, clear error/invalid/expired link states, URL fragment/query parameter cleanup, pre-filled login upon reset, and Dev Mailbox integration.
  - **Tests**: 43 backend integration tests in `CampusVenue.Tests`, 12 frontend Vitest tests.

#### Phase 1A.4: Signed-In Password Change (Completed & Verified)
- **Business Objective**: Enable authenticated users to securely change their password from My Account, verifying the current password, rotating the security stamp, terminating the current session, and rejecting other active sessions after the validation interval.
- **Vertical Slice Scope**:
  - **Backend**: `POST /api/auth/change-password` under `Modules/Identity/Features/ChangePassword`.
  - **Security Invariants**:
    - Requires authenticated session (`.RequireAuthorization()`); user identity is derived strictly from server-side claims (`ClaimTypes.NameIdentifier`).
    - Validates current password using ASP.NET Core Identity (`UserManager.ChangePasswordAsync`).
    - Enforces password complexity policy (8+ characters, uppercase, lowercase, digit, non-alphanumeric) and confirmation match.
    - Protected by anti-CSRF token verification (`X-XSRF-TOKEN`) and endpoint rate limiting (`AuthLimiter`).
    - Signs out current browser session (`SignInManager.SignOutAsync()`) and clears cookie.
    - Security stamp is automatically rotated upon password change; other existing sessions are invalidated and rejected by `SecurityStampValidator` after the configured interval.
  - **Frontend**: `ChangePasswordForm` in My Account (`AccountScreen.tsx`) with real-time requirements checklist, password visibility toggles, clear error states, and automatic redirection to Sign In with a success confirmation banner.
  - **Tests**: 50 backend integration tests (7 dedicated ChangePassword tests covering 401 unauthorized, 400 bad CSRF, wrong current password, weak new password, mismatched confirmation, successful password change with session termination, and remote session rejection), 15 frontend Vitest tests.

#### Phase 1A.5: Sign Out Everywhere (Completed & Verified)
- **Business Objective**: Enable authenticated users to invalidate all active sessions across all devices from My Account using security stamp rotation, terminating the current session immediately and revoking all other sessions within the validation interval.
- **Vertical Slice Scope**:
  - **Backend**: `POST /api/auth/sign-out-everywhere` under `Modules/Identity/Features/SignOutEverywhere`.
  - **Security Invariants**:
    - Requires active authenticated session (`.RequireAuthorization()`); user account is derived strictly from server-side claims (`ClaimTypes.NameIdentifier`).
    - Validates anti-CSRF request token (`X-XSRF-TOKEN`).
    - Invokes `UserManager.UpdateSecurityStampAsync` to rotate `user.SecurityStamp` in PostgreSQL.
    - Leaves current session intact and returns RFC 7807 problem details if security stamp update fails.
    - On success, terminates current session via `SignInManager.SignOutAsync()`, clears auth cookie, and issues a fresh anonymous antiforgery token.
    - Existing sessions on other browsers or devices are invalidated and rejected by `SecurityStampValidator` once the validation interval elapses.
    - User can immediately re-authenticate with their existing password.
  - **Frontend**: "Sign Out Everywhere" card in `AccountScreen` with accessible confirmation dialog detailing the 5-minute remote rejection window, loading state, error banner, and redirection to Sign In with a success confirmation banner.
  - **Tests**: 55 backend integration tests in `CampusVenue.Tests` (5 dedicated SignOutEverywhere integration tests covering unauthenticated rejection, missing CSRF rejection, immediate local browser sign-out, delayed remote session rejection, and same-password re-login), 18 frontend Vitest tests.

#### Phase 1A.6: Authenticator MFA and Recovery Codes (Completed & Verified)
- **Business Objective**: Enable users to enroll an authenticator app (TOTP) from My Account, present a locally generated vector QR code, supply 10 single-use recovery codes, require a short-lived 2FA challenge upon sign-in, and allow secure recovery code regeneration or MFA disabling.
- **Vertical Slice Scope**:
  - **Backend**: Minimal API endpoints (`/api/auth/2fa/status`, `/api/auth/2fa/setup`, `/api/auth/2fa/enable`, `/api/auth/2fa/disable`, `/api/auth/2fa/regenerate-recovery-codes`, `/api/auth/2fa/challenge`) under `Modules/Identity/Features/TwoFactor`.
  - **Security Invariants**:
    - 100% local, offline vector QR code SVG generation via `QRCoder` 1.8.1; zero external network requests or third-party chart APIs.
    - User account derived strictly from server-side claims (`ClaimTypes.NameIdentifier`) with zero client trust.
    - Valid password for MFA-enabled account issues temporary 5-minute cookie (`CampusVenue.TwoFactorUserId`) under `IdentityConstants.TwoFactorUserIdScheme`.
    - Protected endpoints (`/api/auth/me`) strictly return 401 Unauthorized while MFA is pending.
    - Full session cookie (`CampusVenue.Auth`) issued only upon valid 6-digit TOTP code or valid single-use recovery code.
    - Single-use recovery codes immediately invalidated upon redemption; reused codes return 400 Bad Request.
    - Failed 2FA attempts increment account lockout counters and are subject to rate limiting (`AuthLimiter`) and anti-CSRF token verification (`X-XSRF-TOKEN`).
    - Regenerating codes or disabling MFA requires re-verifying current password plus an active second factor.
    - Password reset leaves MFA enabled and active.
  - **Frontend**: My Account Two-Factor card, enrollment modal with local QR code and formatted setup key, 10 recovery codes display modal, Sign In form 2FA challenge mode (with TOTP / recovery code toggle), regeneration modal, and disable modal in `src/features/auth`.
  - **Tests**: 75 backend integration tests (20 dedicated TwoFactor tests), 24 frontend Vitest tests.

#### Phase 1B: Roles & Authorization Policies (RBAC)

##### Phase 1B.1: Administrator User Directory (Completed & Verified)
- **Business Objective**: Define standard institutional roles (`UniversityAdmin`, `FacilityAdmin`, `EventCoordinator`, `Participant`) using ASP.NET Core Identity and PostgreSQL, provide an operator-only startup elevation mechanism, and deliver a read-only, paginated Users screen and API strictly protected by a policy requiring UniversityAdmin and a session with completed MFA.
- **Vertical Slice Scope**:
  - **Backend**:
    - Defined `UniversityAdmin`, `FacilityAdmin`, `EventCoordinator`, and `Participant` in `CampusRoles`.
    - Automatic startup role provisioner (`IdentityRoleSeeder`).
    - Operator-only administrative elevation (`OperatorRoleProvisioner`) via explicit one-time CLI command `--grant-admin-email <email>` which performs elevation and immediately exits. Regular server startup contains zero automatic elevation paths. Rejects nonexistent or unconfirmed accounts; zero hardcoded admin passwords or public bootstrap endpoints.
    - Read-only user directory endpoint `GET /api/users` with server-side pagination (`page`, `pageSize`) and case-insensitive search (`search`).
    - Protected by `UniversityAdminWithMfa` policy requiring both `UniversityAdmin` role and an active session that actually completed MFA (`amr: mfa`). Password-only admin sessions strictly receive 403 Forbidden.
    - Updated `GET /api/auth/me` to return `roles` and `mfaCompleted` session state.
    - Public registration strictly preserves standard `Participant` access and cannot grant administrative roles.
  - **Frontend**:
    - Dedicated feature folder `frontend/src/features/users/` (`UsersScreen.tsx`, `api.ts`, `types.ts`).
    - Navigation tab for "Users" rendered only when session has verified access (`UniversityAdmin` + completed MFA).
    - Clear access denied handling explaining authorization gaps (signed out, non-admin role, or password-only session).
    - Search bar, role badges, MFA status indicator, loading skeleton, empty state, and pagination controls.
  - **Tests**: 92 backend integration tests in `CampusVenue.Tests` (16 dedicated UserDirectory tests), 33 frontend Vitest tests.

##### Phase 1B.2: Staff Role Assignment (Completed & Verified)
- **Business Objective**: Provide audited role assignment within the Users screen for `FacilityAdmin` and `EventCoordinator` roles, enforcing the `UniversityAdminWithMfa` policy, preserving operator-only elevation for `UniversityAdmin`, bundling role updates + stamp rotation + audit records in a single PostgreSQL transaction, detecting concurrent conflicts, and invalidating existing sessions.
- **Vertical Slice Scope**:
  - **Backend**:
    - `PUT /api/users/{id}/roles` with antiforgery, `UniversityAdminWithMfa` policy, actor resolution from session claims.
    - Transactional persistence in PostgreSQL: `user_roles` mutations, `security_stamp` & `concurrency_stamp` updates, and `role_assignment_audits` immutable record creation (`actor_id`, `target_user_id`, `previous_roles`, `new_roles`, `created_at_utc`).
    - Stale edit detection via `ConcurrencyStamp` / `If-Match` / `ExpectedRoles` returning HTTP 409 Conflict; duplicate submissions return 200 OK no-op without duplicate audit records.
    - Forward-only EF Core migration `20261119051453_AddRoleAssignmentAudit`.
  - **Frontend**:
    - "Manage roles" modal dialog in `UsersScreen.tsx` for eligible accounts (non-`UniversityAdmin` accounts; `UniversityAdmin` marked "Operator Managed").
    - Role toggles for `FacilityAdmin` and `EventCoordinator`, change summary impact preview, confirmation, optimistic update, 409 conflict handling with auto-reload of current user state.
  - **Tests**: 104 backend integration tests in `CampusVenue.Tests` (12 dedicated StaffRoleAssignment tests), 37 frontend Vitest tests.

#### Phase 1C: Revised University Booking Pilot

##### Phase 1C.1: Venue Catalog & Verified Availability (Completed & Verified)
- **Business Objective**: Deliver a unified venue catalog supporting both central auditoriums and departmental spaces at the University of Moratuwa, backed by verified calendar coverages, scoped facility blockouts, and a temporal availability calculation engine that detects direct and buffer-only schedule conflicts.
- **Vertical Slice Scope**:
  - **Backend**:
    - `Facilities` module domain: `Venue`, `VenueManager`, `VenueCalendarCoverage`, `VenueCalendarBlock`.
    - Catalog endpoints (`/api/venues`) protected by `UniversityAdminWithMfa` policy for create, update, and manager assignment.
    - Verified calendar coverage & blockout endpoints (`/api/venues/{id}/calendar-coverages`, `/api/venues/{id}/calendar-blocks`) scoped to `UniversityAdminWithMfa` and assigned `FacilityAdmin` users.
    - Temporal availability engine `POST /api/venues/{id}/check-availability` evaluating the full occupied interval `[Start - Setup, End + Cleanup]`, operating hours (UTC+05:30), advance notice, and coverage validity; returns `Available`, `Unavailable` (with explicit `isBufferConflictOnly` classification), or `Availability unverified`.
    - Pre-seeded local demo venues: *Civil Auditorium (J.G. Hall)* (`JG-HALL`) and *Faculty of Business Room 101* (`FOB-101`).
    - Forward-only EF Core migration `20261119130930_AddVenueCatalogAndVerifiedAvailability`.
  - **Frontend**:
    - Dedicated `frontend/src/features/venues/` slice (`VenuesScreen.tsx`, `api.ts`, `types.ts`).
    - Interactive "Venues" catalog view with capacity and limit chips.
    - Live availability checker drawer with dynamic buffer calculations, occupied interval breakdown, and rich visual status cards.
    - Calendar management tab with role-scoped permissions for facility staff and administrators.
  - **Tests**: 113 backend integration tests in `CampusVenue.Tests` (7 dedicated VenueAvailability tests), 42 frontend Vitest tests.

##### Phase 1C.2: Space Requests & Approvals Workflow (Completed & Verified)
- **Business Objective**: Enable event coordinators to submit booking requests for verified available slots, routing requests through assigned venue managers with a review queue, conditions negotiation, transactional outbox notifications, and segregation of duties.
- **Delivered Capabilities**:
  - **Backend**:
    - `Reservations` module domain: `VenueBookingRequest`, `BookingNotification`, `BookingRequestStatus`.
    - Extended `Venue` with `GeneralRules` (nullable text, max 4000) for general venue usage policies.
    - Venue rules update endpoint (`PUT /api/venues/{id}/rules`) protected by `CanManageVenueRulesAsync` (`UniversityAdminWithMfa` or assigned `FacilityAdmin`).
    - Booking request creation endpoint (`POST /api/venues/{venueId}/booking-requests`) rechecking full occupied interval `[Start - Setup, End + Cleanup]` via `AvailabilityService`. If `Unavailable` or `Availability unverified`, returns 400 Bad Request and creates zero requests and zero notifications.
    - Orphan venue protection: requests for venues with 0 assigned managers are rejected with 400 Bad Request to guarantee no request goes unmonitored.
    - Transactional notifications: requests and notification records are saved in a single atomic transaction wrapped in EF Core execution strategy, dispatching emails via `DevMailboxService`.
    - Idempotency key protection: duplicate submissions with the same key return the existing request without duplicate records or extra emails.
    - Manager review queue endpoints (`GET /api/booking-requests/review-queue`, `POST /api/booking-requests/{id}/review`) supporting `Approve`, `ApproveWithConditions`, and `Reject` (with reason).
    - Strict Segregation of Duties: a user cannot review their own booking request (`requesterId != reviewerId`). Requests where a coordinator is also a venue manager require another authorized reviewer.
    - Condition acceptance endpoint (`POST /api/booking-requests/{id}/respond-conditions`): coordinator can accept conditions (transitioning request to `AwaitingConfirmation`) or decline (transitioning request to `Cancelled`).
    - Forward-only EF Core migration `20261122041732_AddBookingRequestsAndVenueReview`.
  - **Frontend**:
    - Extended `frontend/src/features/venues/` with tabs: `Catalog & Availability`, `My Requests`, `Review Queue`, `Calendar Management`.
    - General rules display in venue cards and booking request submission modal with mandatory acknowledgment checkbox.
    - Orphan venue warning banner disabling submission if no managers are assigned.
    - "My Requests" tab with real-time status badges, conditional approval callouts, and "Accept Conditions" / "Decline & Cancel" action dialogs.
    - "Review Queue" tab scoped to assigned venues (or all for MFA admin), with segregation-of-duties warning banner and disabled actions on self-requested slots.
    - "Venue Rules & Policies" editor tab in calendar management for authorized staff.
  - **Tests**: 128 backend integration tests in `CampusVenue.Tests` (9 dedicated `BookingRequestIntegrationTests`), 46 frontend Vitest tests.

##### Phase 1C.3: Booking Confirmation & PostgreSQL Overlap Constraints (Planned)
- **Business Objective**: Atomically confirm bookings upon approval acceptance, enforcing database-level temporal exclusion constraints (`EXCLUDE USING gist`) to guarantee zero double-bookings under concurrent load.
- **Vertical Slice Scope**:
  - `confirmed_reservations` PostgreSQL table with `tstzrange` exclusion constraints.
  - ACID transaction rechecking coverage and locking the interval prior to confirmation.
  - Handoff from confirmed reservation to event creation.

---

*(Phases 2 through 13 continue as previously defined in the master roadmap).*
