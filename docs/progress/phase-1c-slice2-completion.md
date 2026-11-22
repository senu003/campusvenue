# Phase 1C Slice 2 Completion & Verification Report

## Status: Completed & Verified
**Milestone**: Phase 1C (Revised Booking Pilot) — Slice 2: Booking Requests and Venue Review<br />
**Project**: CampusVenue (University Venue Reservation & Event Management Platform)<br />
**Date**: 2026-11-22

---

## 1. Executive Summary & Scope

In accordance with Phase 1C of the revised University of Moratuwa (UoM) pilot scope, Phase 1C Slice 2 delivers the end-to-end booking request submission and venue manager review lifecycle. This slice integrates with the Phase 1C.1 venue catalog, verified calendar coverages, and temporal availability calculation engine.

### Key Capabilities Delivered:
1. **General Venue Rules Management**:
   - Extended `Venue` with `GeneralRules` (nullable text, max 4000 characters) to store university and venue-specific usage guidelines, curfew rules, equipment policies, and noise regulations.
   - Protected endpoint `PUT /api/venues/{id}/rules` enforces server-side authorization: `UniversityAdminWithMfa` can update rules for any venue, while `FacilityAdmin` can update rules only for venues they are explicitly assigned to manage.
   - General rules are prominently surfaced to event coordinators on venue cards and within the booking submission drawer, requiring explicit acknowledgment prior to submission.
2. **Server-Side Rechecked Booking Request Submission**:
   - Coordinator endpoint `POST /api/venues/{venueId}/booking-requests` accepts reservation requests with start/end timestamps, setup/cleanup buffers, expected attendance, title, and description.
   - **Zero Incomplete State Invariant**: The server re-evaluates the full occupied interval $[\text{Start} - \text{Setup}, \text{End} + \text{Cleanup}]$ using `AvailabilityService`. If the slot is `Unavailable` or `Availability unverified`, the submission is rejected immediately (`HTTP 400 Bad Request`), creating neither a booking request nor an approval notification.
   - **Orphan Venue Guard**: If a venue has zero assigned venue managers, the submission is rejected (`HTTP 400 Bad Request`) and the UI renders a prominent warning banner preventing submission.
   - **Idempotency**: Requests accept an optional `IdempotencyKey` header or property, preventing duplicate requests or spurious notification records on repeated submissions.
3. **Transactional Outbox & Development Mailbox Integration**:
   - Creation of `VenueBookingRequest` and its corresponding `BookingNotification` records occurs within an atomic database transaction wrapped in EF Core's `CreateExecutionStrategy()`.
   - Notifications are dispatched locally via `DevMailboxService` (`/dev/mailbox`), notifying all assigned facility managers upon submission and notifying the coordinator upon review decisions.
4. **Venue Manager Review Queue & Segregation of Duties**:
   - Review queue endpoint `GET /api/booking-requests/review-queue` returns pending requests scoped to assigned venues for `FacilityAdmin`, or university-wide for `UniversityAdminWithMfa`.
   - Review action endpoint `POST /api/booking-requests/{id}/review` supports three terminal/conditional outcomes:
     - `Approve`: Transitions directly to `AwaitingConfirmation`.
     - `ApproveWithConditions`: Transitions to `ApprovedWithConditions`, recording required conditions (e.g. fire safety sign-off, security bond, noise limit).
     - `Reject`: Transitions to `Rejected`, recording a mandatory rejection reason.
   - **Segregation of Duties**: A user cannot review their own booking request (`requesterId == reviewerId`). If an `EventCoordinator` is also a `FacilityAdmin` assigned to the venue, the server rejects their review attempt with `HTTP 403 Forbidden`, and the frontend UI disables action controls and flags the item with a "Self-requested / Requires independent reviewer" badge.
5. **Coordinator Condition Negotiation**:
   - Coordinator endpoint `POST /api/booking-requests/{id}/respond-conditions` allows the requester to review manager-stipulated conditions and choose:
     - `Accept`: Transitions status to `AwaitingConfirmation`.
     - `Decline`: Transitions status to `Cancelled`.
6. **Frontend Experience (`VenuesScreen.tsx`)**:
   - Organized into 4 functional tabs: `Catalog & Availability`, `My Requests`, `Review Queue`, and `Calendar Management`.
   - Live availability checker seamlessly opens the "Submit Booking Request" modal with rules acknowledgment.
   - "My Requests" tab displays all coordinator submissions with status badges, conditional approval callouts, and condition response actions.
   - "Review Queue" tab displays pending requests with venue metadata, occupied intervals, segregation of duties guards, and review modals.

---

## 2. Architecture & Design Principles

### Execution Strategy with Atomic Transactions
Because `CampusVenue` configures PostgreSQL connection resiliency (`EnableRetryOnFailure(3)`), all explicit database transactions must be wrapped inside an execution strategy:
```csharp
var strategy = dbContext.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    // 1. Insert VenueBookingRequest
    // 2. Insert BookingNotification
    // 3. Commit
});
```
This guarantees atomicity: a booking request is never created without its audit/notification record, and transient network errors do not cause orphan records.

### Segregation of Duties
To prevent conflicts of interest where a staff member books a venue and approves their own reservation, the review endpoint enforces:
```csharp
if (request.RequesterId == currentUserId)
{
    return Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Segregation of duties violation",
        detail: "You cannot review your own booking request. An independent authorized reviewer is required.");
}
```

### Calendar Blocking Boundary
In alignment with directive 5 (No premature complexity) and directive 16 (No claiming unverified features are complete):
- `VenueBookingRequest` in `PendingReview`, `ApprovedWithConditions`, or `AwaitingConfirmation` status **does not block the calendar**.
- The calendar continues to reflect verified coverage and calendar blocks.
- Final calendar blocking and PostgreSQL double-booking prevention (`tstzrange` exclusion constraints) are strictly encapsulated in **Phase 1C.3**.

---

## 3. Database Schema & Migration Details

**Migration**: `20261122041732_AddBookingRequestsAndVenueReview.cs`

### 1. `venues` (Table Alteration)
- `general_rules` (text, NULL, maximum 4000 characters)

### 2. `venue_booking_requests` (New Table)
- `id` (uuid, PK)
- `venue_id` (uuid, FK &rarr; `venues.id`, ON DELETE RESTRICT)
- `requester_id` (uuid, FK &rarr; `users.id`, ON DELETE RESTRICT)
- `title` (varchar(200), NOT NULL)
- `description` (varchar(2000), NULL)
- `start_utc` (timestamp with time zone, NOT NULL)
- `end_utc` (timestamp with time zone, NOT NULL)
- `setup_minutes` (integer, NOT NULL, DEFAULT 0)
- `cleanup_minutes` (integer, NOT NULL, DEFAULT 0)
- `expected_attendance` (integer, NOT NULL)
- `status` (varchar(50), NOT NULL, DEFAULT 'PendingReview')
- `reviewer_id` (uuid, FK &rarr; `users.id`, NULL, ON DELETE SET NULL)
- `reviewed_at_utc` (timestamp with time zone, NULL)
- `conditions` (text, NULL)
- `rejection_reason` (text, NULL)
- `conditions_accepted_at_utc` (timestamp with time zone, NULL)
- `idempotency_key` (varchar(100), NULL)
- `created_at_utc` (timestamp with time zone, NOT NULL)
- `updated_at_utc` (timestamp with time zone, NOT NULL)
- **Check Constraints**:
  - `ck_venue_booking_requests_dates`: `end_utc > start_utc`
  - `ck_venue_booking_requests_attendance`: `expected_attendance > 0`
  - `ck_venue_booking_requests_buffers`: `setup_minutes >= 0 AND cleanup_minutes >= 0`
- **Indexes**:
  - `ix_venue_booking_requests_venue_id_status`
  - `ix_venue_booking_requests_requester_id`
  - `ix_venue_booking_requests_idempotency_key`

### 3. `booking_notifications` (New Table)
- `id` (uuid, PK)
- `booking_request_id` (uuid, FK &rarr; `venue_booking_requests.id`, ON DELETE CASCADE)
- `recipient_email` (varchar(256), NOT NULL)
- `recipient_role` (varchar(50), NOT NULL)
- `notification_type` (varchar(50), NOT NULL)
- `subject` (varchar(300), NOT NULL)
- `message_body` (text, NOT NULL)
- `sent_at_utc` (timestamp with time zone, NOT NULL)
- **Indexes**:
  - `ix_booking_notifications_booking_request_id`

---

## 4. API Endpoints Delivered

| Method | Endpoint | Authorization | Policy / Scoping | Description |
| :---: | :--- | :---: | :---: | :--- |
| `PUT` | `/api/venues/{id}/rules` | Required | `UniversityAdminWithMfa` or Assigned Manager | Updates general venue rules and usage guidelines |
| `POST` | `/api/venues/{venueId}/booking-requests` | Required | `EventCoordinator` | Submits a booking request after rechecking temporal availability |
| `GET` | `/api/booking-requests/my` | Required | Authenticated | Lists all booking requests submitted by the current user |
| `GET` | `/api/booking-requests/review-queue` | Required | `UniversityAdminWithMfa` or `FacilityAdmin` | Lists pending booking requests for assigned venues |
| `GET` | `/api/venues/{venueId}/booking-requests` | Required | `UniversityAdminWithMfa` or Assigned Manager | Lists booking requests for a specific venue |
| `POST` | `/api/booking-requests/{id}/review` | Required | `UniversityAdminWithMfa` or Assigned Manager | Approves, conditionally approves, or rejects a booking request |
| `POST` | `/api/booking-requests/{id}/respond-conditions` | Required | Request Owner | Accepts or declines conditions on a conditionally approved request |

---

## 5. Automated Verification Results

### Backend Test Suite (`CampusVenue.Tests`)
- **Command**: `$env:CAMPUSVENUE_TEST_CONNECTION_STRING="Host=localhost;Port=5433;Database=campusvenue_dev;Username=campusvenue_user;Password=campusvenue_dev_secret_change_me"; dotnet test backend\CampusVenue.sln`
- **Total Tests**: **128 passed, 0 failed, 0 skipped**
- **Duration**: 24.2 seconds
- **Exit Code**: 0

#### Dedicated Integration Tests (`BookingRequestIntegrationTests.cs`):
1. `SubmitBookingRequest_AvailableSlot_CreatesRequestAndNotification`:
   - Validates that an available slot creates a `PendingReview` request and an atomic `BookingNotification` for assigned managers.
2. `SubmitBookingRequest_UnavailableSlot_ReturnsBadRequest_CreatesNoRequestOrNotification`:
   - Validates that collision with a calendar block returns HTTP 400 and creates zero rows in `venue_booking_requests` and `booking_notifications`.
3. `SubmitBookingRequest_OutsideCoverage_ReturnsBadRequest_CreatesNoNotification`:
   - Validates that an unverified slot outside coverage returns HTTP 400 and creates zero rows in database.
4. `SubmitBookingRequest_NoVenueManagerAssigned_ReturnsBadRequest`:
   - Validates that submitting for an orphan venue returns HTTP 400 and blocks request creation.
5. `SubmitBookingRequest_IdempotentResubmission_ReturnsExistingRequest`:
   - Validates that resubmitting with the same idempotency key returns the existing request without duplicating records.
6. `ReviewBookingRequest_Approve_TransitionsToAwaitingConfirmation`:
   - Validates venue manager approval and notification email dispatch.
7. `ReviewBookingRequest_ApproveWithConditions_CoordinatorAccepts_TransitionsToAwaitingConfirmation`:
   - Validates conditional approval, coordinator condition view, and transition to `AwaitingConfirmation` on acceptance.
8. `ReviewBookingRequest_ApproveWithConditions_CoordinatorDeclines_TransitionsToCancelled`:
   - Validates that declining conditions transitions the request to `Cancelled`.
9. `ReviewBookingRequest_SamePersonAsRequester_EnforcesSegregationOfDuties_Returns403Forbidden`:
   - Validates that a user holding both `EventCoordinator` and `FacilityAdmin` cannot review their own request.

### Frontend Verification
- **Typecheck**: `npm run typecheck` (`tsc --noEmit`) &rarr; **Exit 0, 0 errors**
- **Lint**: `npm run lint` (`eslint .`) &rarr; **Exit 0, 0 warnings/errors**
- **Production Build**: `npm run build` (`vite build`) &rarr; **Exit 0, built in 5.2s**
- **Vitest**: `npm test` &rarr; **3 test files passed, 46 tests passed (0 failed)**

---

## 6. Manual Verification Runbook

### Prerequisites
Start the PostgreSQL container, backend API, and frontend development server in PowerShell:
```powershell
# 1. Ensure PostgreSQL is running on port 5433
docker ps

# 2. Start Backend API
cd E:\CampusVenue\backend\CampusVenue.Api
$env:DOTNET_ROOT = "C:\Users\User\.dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
dotnet run

# 3. In another terminal, start Frontend
cd E:\CampusVenue\frontend
npm run dev
```

### Walkthrough Steps

#### Scenario 1: University Admin Manages General Venue Rules
1. Navigate to `http://localhost:5173` and sign in as `admin@campus.internal` / `AdminPass123!`.
2. Complete MFA with code `123456` (or TOTP authenticator).
3. Open **Venues** &rarr; **Calendar Management** tab.
4. Select *Civil Auditorium (J.G. Hall)*.
5. In the **Venue Rules & Policies** sub-tab, notice the pre-seeded rules. Edit the text and click **Save Rules**.
6. Switch to **Catalog & Availability** tab; expand the venue card and observe the updated General Rules text.

#### Scenario 2: Event Coordinator Submits a Booking Request
1. Sign out and sign in as an Event Coordinator account (e.g. `coordinator@campus.internal` or create one via Admin Users screen).
2. Open **Venues** &rarr; **Catalog & Availability** tab.
3. On *Civil Auditorium (J.G. Hall)*, click **Check Availability**.
4. Select a date within verified coverage (e.g., 2026-11-25 09:00 to 12:00 UTC+05:30).
5. When the slot indicates `Available`, click **Submit Booking Request**.
6. Review the General Rules, check **"I have read and agree to follow these venue rules"**, fill in Title and Attendance, and click **Submit Request**.
7. Observe the success confirmation and automatic navigation to the **My Requests** tab.
8. Verify the request is listed with status `Pending Review`.
9. Open `http://localhost:5000/dev/mailbox` in a new tab; verify that an email was dispatched to the assigned venue manager notifying them of the new request.

#### Scenario 3: Facility Manager Reviews & Approves with Conditions
1. Sign out and sign in as the assigned Facility Manager (e.g. `facility@campus.internal`).
2. Open **Venues** &rarr; **Review Queue** tab.
3. Observe the pending request submitted in Scenario 2.
4. Click **Conditions**, enter: `"Must provide certified security steward and submit sound level plan 48h prior."`, and click **Submit Conditional Approval**.
5. Observe the request status updates to `Approved (Conditions Set)`.
6. Verify `/dev/mailbox` contains an email to the coordinator detailing the conditions.

#### Scenario 4: Coordinator Accepts Conditions
1. Sign back in as the Event Coordinator.
2. Open **Venues** &rarr; **My Requests** tab.
3. Note the amber callout showing: `"Reviewer Conditions: Must provide certified security steward..."`.
4. Click **Accept Conditions**.
5. The request status updates to `Awaiting Confirmation`.

#### Scenario 5: Segregation of Duties Enforcement
1. Using the Admin Users screen or seeder, grant an account both `FacilityAdmin` and `EventCoordinator` roles and assign them to *Faculty of Business Room 101*.
2. Sign in as this dual-role user.
3. Submit a booking request for *Faculty of Business Room 101*.
4. Switch to the **Review Queue** tab.
5. Observe that the request displays a **"Self-requested / Requires independent reviewer"** badge, and the review action buttons are disabled.
6. Any direct API call to `/api/booking-requests/{id}/review` is rejected with `HTTP 403 Forbidden`.

---

## 7. Phase Completion Gates Assessment

| Gate | Requirement | Status | Evidence |
| :---: | :--- | :---: | :--- |
| **Gate 1** | Clean Build across all projects | **PASS** | Backend `net10.0` build succeeded with 0 warnings/errors. Frontend Vite build succeeded in 5.2s. |
| **Gate 2** | Passing Automated Tests | **PASS** | 128 backend tests (including 9 booking request tests) passing in 24.2s. 46 frontend Vitest tests passing. |
| **Gate 3** | Infrastructure Verification | **PASS** | PostgreSQL 16 migration `20261122041732_AddBookingRequestsAndVenueReview` applied with check constraints and foreign keys. |
| **Gate 4** | Secret & Credentials Audit | **PASS** | Zero hardcoded credentials or API tokens. Connection strings use standard local `.env` values. |
| **Gate 5** | Manual Verification Runbook | **PASS** | Complete 5-scenario PowerShell and browser walkthrough documented in Section 6. |
| **Gate 6** | Subsequent Phase Scoping | **PASS** | Clear boundaries established for Phase 1C.3 below. |

---

## 8. Subsequent Phase Scoping: Phase 1C.3

Phase 1C.3 delivers the final booking confirmation and concurrency safety layer:
1. **PostgreSQL Temporal Exclusion Constraints**:
   - `confirmed_reservations` table utilizing PostgreSQL `tstzrange` with `EXCLUDE USING gist` to provide hardware/kernel-level double-booking prevention under concurrent transaction load.
2. **ACID Booking Confirmation Endpoint**:
   - Atomic promotion of `AwaitingConfirmation` requests to confirmed reservations.
   - Immediate recheck of calendar blocks and coverage within serializable transaction.
3. **Calendar Integration**:
   - Confirmed reservations reflect automatically on the availability calendar as occupied slots.
4. **Handoff to Event Lifecycle**:
   - Reservation ID linked to Event publishing in Phase 6.
