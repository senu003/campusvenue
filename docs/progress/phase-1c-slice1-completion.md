# Phase 1C Slice 1 Completion & Verification Report

## Status: Completed & Verified
**Milestone**: Phase 1C (Revised Booking Pilot) — Slice 1: Venue Catalog & Verified Availability<br />
**Project**: CampusVenue (University Venue Reservation & Event Management Platform)<br />
**Date**: 2026-11-19

---

## 1. Executive Summary & Scope

In accordance with the revised University of Moratuwa (UoM) pilot scope, Phase 1C Slice 1 delivers a centralized, multi-tier venue catalog and temporal availability engine. This slice replaces speculative organizational hierarchy with practical venue management and calendar verification for both centrally managed auditoriums and department-managed rooms.

### Key Capabilities Delivered:
1. **Admin-Managed Venue Catalog**:
   - Manages venue profiles: name, unique code, venue type (`CentralHall`, `Departmental`, `LectureTheater`, `MeetingRoom`, etc.), managing unit, capacity, daily operating hours (`OperatingHoursStart`, `OperatingHoursEnd`), and configurable booking limits (minimum notice hours, default setup/cleanup buffers in minutes, maximum continuous booking duration).
   - Fully unified schema and workflow supporting both central facilities and departmental venues.
   - Protected by the `UniversityAdminWithMfa` policy for creation, profile modification, and facility manager assignment.
2. **Verified Calendar Coverage & Scoped Blockouts**:
   - Authorized staff can register calendar coverage periods with authoritative source metadata (`source_name`, `source_reference`, `covers_from_utc`, `covers_to_utc`, `verified_by_user_id`).
   - Authorized staff can create calendar blockouts for academic lectures, exams, scheduled maintenance, and external bookings made outside CampusVenue.
   - **Role Scoping**: While `UniversityAdminWithMfa` possesses university-wide administrative access, `FacilityAdmin` users are strictly scoped to venues they are assigned to manage via `venue_managers`. Attempts to modify calendars of unassigned venues are rejected with `HTTP 403 Forbidden`.
3. **Temporal Availability & Buffer Mathematics Engine**:
   - Coordinator-facing endpoint `POST /api/venues/{id}/check-availability` evaluates requests against the full occupied interval:
     $$\text{Occupied Interval} = [\text{EventStart} - \text{SetupMinutes}, \text{EventEnd} + \text{CleanupMinutes}]$$
   - Validates venue operating hours in Sri Lanka Standard Time (UTC+05:30), minimum advance notice requirements, and maximum allowed booking duration.
   - Verifies that the occupied interval is completely bounded by an active, verified calendar coverage record. If coverage is missing or expired, returns `Availability unverified`.
   - Checks against all active calendar blocks. If an intersection exists, it identifies whether the collision is directly within the event period or isolated to the setup/cleanup buffer windows (`isBufferConflictOnly = true`, `conflictScope = 'SetupBuffer' | 'CleanupBuffer'`).
4. **Interactive React Venue & Availability UI**:
   - Accessible "Venues" tab and responsive catalog view (`frontend/src/features/venues/VenuesScreen.tsx`).
   - Interactive availability checker drawer allowing coordinators to select start/end times, configure custom setup/cleanup buffers, preview total occupied duration, and inspect visual status badges (`Available`, `Unavailable`, `Availability unverified`) with detailed explanations.
   - Dedicated "Calendar & Coverages" management tab visible to administrators and assigned facility managers for adding coverage windows and scheduling blockout periods.
5. **Two Local Demo Venues**:
   - **Central Hall**: *"Civil Auditorium (J.G. Hall)"* (`JG-HALL`), capacity 800, managed by General Administration / Maintenance Division.
   - **Department Room**: *"Faculty of Business Room 101"* (`FOB-101`), capacity 60, managed by the Faculty of Business.

---

## 2. University of Moratuwa Pilot Validation

### Validation Checklist & Source Alignment
| Venue | Managing Unit | Authoritative Availability Source | Venue Approver | Student Event Approver | Accepted Channel | Identified Limit / Buffer | Final Confirmation Artifact |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Civil Auditorium (J.G. Hall)** | General Administration & Maintenance Division | Staff Resource Booking System (`book.uom.lk`) | Registrar / Maintenance Custodian | Senior Assistant Registrar (Student Welfare) | Formal reservation form countersigned by Senior Treasurer | Minimum 72h advance notice; 60m setup / 45m cleanup | Official signed permit & RBS reservation ID |
| **Faculty of Business Room 101** | Faculty of Business (Dean's Office) | Department Timetable Portal (`uomba.net` / Dean's Office timetable) | Head of Department / Assistant Registrar (Business) | Staff Advisor / Patron of Society & Dean | Departmental request memo / online form | Minimum 24h advance notice; 30m setup / 30m cleanup | HOD approval endorsement & timetable lock |

### Published Facts vs. Items Requiring Staff Confirmation
- **Published Facts**:
  - UoM Staff Resource Booking System (RBS) is staff-authenticated; student societies cannot book directly through RBS without academic staff mediation.
  - Student Welfare (`uom.lk/welfare`) facilitates student societies and requires multi-tiered authorization (Patron &rarr; Senior Treasurer &rarr; SAR Welfare &rarr; Deputy Vice Chancellor / Vice Chancellor for large public events).
- **Details Needing Confirmation from Venue Managers**:
  1. *RBS Synchronization Frequency*: Whether CampusVenue will receive automated timetable feeds or if facility staff will periodically verify and sign off calendar blocks.
  2. *Dual-Stage Approval Routing*: Confirming whether J.G. Hall requires Welfare approval prior to venue custodian approval, or if venue hold is provisionally granted pending Welfare sign-off.
  3. *Weekend & Evening Curfew Rules*: Maximum operating cutoff for student events (e.g. 19:30 curfew for student activities).

---

## 3. Database Schema & Migration Details

**Migration**: `20261119130930_AddVenueCatalogAndVerifiedAvailability.cs`

### 1. `venues`
- `id` (uuid, PK)
- `name` (varchar(200), NOT NULL)
- `code` (varchar(50), NOT NULL, Unique Index `ix_venues_code`)
- `type` (varchar(50), NOT NULL)
- `managing_unit` (varchar(200), NOT NULL)
- `capacity` (integer, NOT NULL, Check: `capacity > 0`)
- `operating_hours_start` (time, NOT NULL)
- `operating_hours_end` (time, NOT NULL)
- `min_notice_hours` (integer, NOT NULL, Check: `min_notice_hours >= 0`)
- `default_setup_minutes` (integer, NOT NULL, Check: `default_setup_minutes >= 0`)
- `default_cleanup_minutes` (integer, NOT NULL, Check: `default_cleanup_minutes >= 0`)
- `max_booking_hours` (integer, NOT NULL, Check: `max_booking_hours > 0`)
- `requires_event_approval` (boolean, NOT NULL)
- `is_active` (boolean, NOT NULL, Default `true`)
- `created_at_utc`, `updated_at_utc` (timestamp with time zone)

### 2. `venue_managers`
- `venue_id` (uuid, FK &rarr; `venues.id`, ON DELETE CASCADE)
- `user_id` (uuid, FK &rarr; `users.id`, ON DELETE CASCADE)
- `assigned_at_utc` (timestamp with time zone)
- Primary Key: `(venue_id, user_id)`

### 3. `venue_calendar_coverages`
- `id` (uuid, PK)
- `venue_id` (uuid, FK &rarr; `venues.id`, ON DELETE CASCADE)
- `source_name` (varchar(100), NOT NULL)
- `source_reference` (varchar(200), NULL)
- `covers_from_utc` (timestamp with time zone, NOT NULL)
- `covers_to_utc` (timestamp with time zone, NOT NULL)
- `verified_by_user_id` (uuid, FK &rarr; `users.id`, ON DELETE RESTRICT)
- `verified_at_utc` (timestamp with time zone, NOT NULL)
- `notes` (text, NULL)
- `created_at_utc` (timestamp with time zone)
- Constraint: `covers_to_utc > covers_from_utc`

### 4. `venue_calendar_blocks`
- `id` (uuid, PK)
- `venue_id` (uuid, FK &rarr; `venues.id`, ON DELETE CASCADE)
- `title` (varchar(200), NOT NULL)
- `block_type` (varchar(50), NOT NULL)
- `start_utc` (timestamp with time zone, NOT NULL)
- `end_utc` (timestamp with time zone, NOT NULL)
- `created_by_user_id` (uuid, FK &rarr; `users.id`, ON DELETE RESTRICT)
- `external_reference` (varchar(100), NULL)
- `notes` (text, NULL)
- `created_at_utc` (timestamp with time zone)
- Constraint: `end_utc > start_utc`

---

## 4. API Endpoints Delivered

| Method | Endpoint | Authorization | Policy / Scoping | Description |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/venues` | Optional | Public / Authenticated | Lists all active venues with capacity and booking limits |
| `GET` | `/api/venues/{id}` | Optional | Public / Authenticated | Retrieves venue profile and manager details |
| `POST` | `/api/venues` | Required | `UniversityAdminWithMfa` | Creates a new venue profile |
| `PUT` | `/api/venues/{id}` | Required | `UniversityAdminWithMfa` | Updates venue properties and booking constraints |
| `POST` | `/api/venues/{id}/managers` | Required | `UniversityAdminWithMfa` | Assigns a `FacilityAdmin` user as manager |
| `DELETE` | `/api/venues/{id}/managers/{userId}` | Required | `UniversityAdminWithMfa` | Removes a manager assignment |
| `POST` | `/api/venues/{id}/check-availability` | Required | Authenticated | Calculates interval availability (`Available`, `Unavailable`, `Unverified`) |
| `GET` | `/api/venues/{id}/calendar-coverages` | Required | Authenticated | Lists verified calendar coverage periods |
| `POST` | `/api/venues/{id}/calendar-coverages` | Required | `UniversityAdminWithMfa` or Assigned Manager | Records verified coverage range |
| `GET` | `/api/venues/{id}/calendar-blocks` | Required | Authenticated | Retrieves calendar blocks within requested window |
| `POST` | `/api/venues/{id}/calendar-blocks` | Required | `UniversityAdminWithMfa` or Assigned Manager | Creates a blockout window |
| `DELETE` | `/api/venues/{id}/calendar-blocks/{blockId}` | Required | `UniversityAdminWithMfa` or Assigned Manager | Removes a calendar block |

---

## 5. Automated Verification Results

### Backend Test Suite (`CampusVenue.Tests`)
- **Command**: `dotnet test backend/CampusVenue.sln -c Release`
- **Database**: Fresh disposable PostgreSQL database (`campusvenue_verify_p1c_20261119_185927`)
- **Total Tests**: **113 passed, 0 failed, 0 skipped**
- **Duration**: 30 seconds
- **Exit Code**: 0

#### Dedicated Integration Tests (`VenueAvailabilityIntegrationTests.cs`):
1. `CheckAvailability_VerifiedFreeSlot_ReturnsAvailable`:
   - Validates that a requested event falling inside active coverage and clear of any blockouts returns `Available` with source reference metadata.
2. `CheckAvailability_CalendarBlock_ReturnsUnavailable`:
   - Validates that an event colliding directly with an exam blockout returns `Unavailable` with `isBufferConflictOnly = false` and conflict details.
3. `CheckAvailability_ConflictCausedOnlyByBuffer_ReturnsUnavailable_WithBufferExplanation`:
   - Verifies an event whose physical event time is completely clear of collisions, but whose 30-minute setup buffer intersects a prior lecture blockout, returns `Unavailable` with `isBufferConflictOnly = true`, `conflictScope = 'SetupBuffer'`, and an informative explanation.
4. `CheckAvailability_UnverifiedPeriod_OutsideCoverage_ReturnsAvailabilityUnverified`:
   - Validates that a request outside verified coverage dates returns `Availability unverified` with an explicit notice to contact venue staff.
5. `CalendarBlocks_FacilityStaff_AttemptingToEditAnotherVenue_Returns403Forbidden`:
   - Confirms that a `FacilityAdmin` assigned to Venue A receives `403 Forbidden` when attempting to add a block to Venue B, but receives `201 Created` for Venue A.
6. `CalendarBlocks_AdminWithMfa_CanManageAnyVenue`:
   - Confirms that `UniversityAdmin` with completed MFA can manage calendar entries across any venue.
7. `Venues_CatalogManagement_AdminWithMfa_Succeeds_AndCoordinatorForbidden`:
   - Verifies that venue catalog mutations strictly require the `UniversityAdminWithMfa` policy, returning `403 Forbidden` to event coordinators.

### Frontend Quality & Component Tests (`frontend/`)
- **Typecheck**: `npm run typecheck` &rarr; 0 errors (Exit code 0).
- **Linter**: `npm run lint` &rarr; 0 warnings, 0 errors (Exit code 0).
- **Test Suite**: `npm run test` &rarr; **42 passed across 3 test suites, 0 failed** (Exit code 0).
  - `src/features/venues/VenuesScreen.test.tsx` (5 tests passed):
    - Catalog rendering with central hall and department room.
    - Live availability check rendering `Available` with coverage badge.
    - Direct event conflict rendering `Unavailable`.
    - Buffer-only conflict rendering `Unavailable` with setup buffer banner.
    - Unverified period check rendering `Availability unverified`.
  - `src/features/users/UsersScreen.test.tsx` (11 tests passed).
  - `src/App.test.tsx` (26 tests passed).
- **Production Build**: `npm run build` &rarr; Clean bundle built in 13.27s:
  - `dist/index.html` (0.57 kB)
  - `dist/assets/index-CcsDvdlD.css` (31.10 kB)
  - `dist/assets/index-DrfXZPqo.js` (342.61 kB)

---

## 6. Manual Browser Verification Runbook

Follow these steps to manually walk through the venue catalog and availability check:

### Step 1: Start Backend and Frontend
In PowerShell terminal 1 (Backend):
```powershell
$env:DOTNET_ROOT = "C:\Users\User\.dotnet"
$env:PATH = "C:\Users\User\.dotnet;" + $env:PATH
cd E:\CampusVenue\backend\CampusVenue.Api
dotnet run --launch-profile http
```
*Backend listens on `http://localhost:5000` and automatically seeds demo venues and calendar data.*

In PowerShell terminal 2 (Frontend):
```powershell
cd E:\CampusVenue\frontend
npm run dev
```
*Frontend dev server starts at `http://localhost:3000`.*

### Step 2: Open Browser & Sign In as Event Coordinator
1. Navigate to `http://localhost:3000`.
2. Sign in with pre-seeded coordinator credentials:
   - **Email**: `coordinator@campus.internal`
   - **Password**: `Coordinator2026!`
3. Verify that the navigation bar displays tabs: **Status**, **Users** (hidden/inaccessible for coordinator), **Venues**, and **My Account**.

### Step 3: Browse Venue Catalog
1. Click the **Venues** tab in the navigation bar.
2. Confirm the presence of two pilot venues:
   - **Civil Auditorium (J.G. Hall)**: Capacity 800, CentralHall, Operating hours 08:00 - 22:00, Minimum notice 72h, Setup buffer 60m, Cleanup buffer 45m.
   - **Faculty of Business Room 101**: Capacity 60, Departmental, Operating hours 08:00 - 20:00, Minimum notice 24h, Setup buffer 30m, Cleanup buffer 30m.

### Step 4: Test Availability Scenarios (Interactive Drawer)
Click **Check Availability** on **Civil Auditorium (J.G. Hall)** to open the checker drawer.

1. **Test Available Slot**:
   - Set **Date**: A weekday 5 days from today.
   - Set **Start Time**: `10:00` | **End Time**: `13:00`.
   - Setup buffer: `60` min | Cleanup buffer: `45` min.
   - Click **Check Availability**.
   - **Expected Result**: A green badge labeled **AVAILABLE** appears. The reason text confirms: *"The slot is available within operating hours and verified coverage."* Detailed coverage source indicates *"UoM General Administration Calendar 2026"*.
2. **Test Calendar Block Conflict**:
   - Select a date/time coinciding with the pre-seeded block: 4 days from today between `09:00` and `12:00` (e.g. *Faculty Board Meeting*).
   - Click **Check Availability**.
   - **Expected Result**: A red badge labeled **UNAVAILABLE** appears. The conflict details name the conflicting block: *"Faculty Board Meeting (Meeting)"*.
3. **Test Buffer-Only Conflict**:
   - Select an event time starting right after a blockout (e.g. `12:00` to `15:00`).
   - Leave the setup buffer at `60` minutes (occupied start becomes `11:00`, which overlaps the blockout ending at `12:00`).
   - Click **Check Availability**.
   - **Expected Result**: A red badge labeled **UNAVAILABLE** appears. A warning note indicates: *"The event time is free, but its setup buffer collides with an existing block."*
4. **Test Unverified Period**:
   - Change the event date to 4 months in the future (outside verified coverage).
   - Click **Check Availability**.
   - **Expected Result**: An amber badge labeled **AVAILABILITY UNVERIFIED** appears with the explanation: *"Availability unverified: requested time falls outside verified calendar coverage (Contact facility administration)."*

### Step 5: Test Role-Scoped Calendar Management
1. Sign in as an administrator who has completed MFA:
   - **Email**: `admin@campus.internal`
   - **Password**: `AdminPassword2026!` (complete TOTP challenge using your authenticator or recovery code).
2. Go to **Venues** &rarr; switch to the **Calendar & Blocks** management tab.
3. Observe that administrators can view verified coverages, add calendar coverages, and schedule new calendar blockouts.
4. Try adding a test maintenance blockout for `JG-HALL` and verify it immediately causes colliding coordinator availability checks to report `Unavailable`.
