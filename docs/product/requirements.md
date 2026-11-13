# CampusVenue — Product Requirements Specification

## 1. Problem Statement

Universities face substantial operational friction in managing campus venues and coordinating academic and student events:
- **Siloed Bookings**: Central facilities, academic departments, and student organizations maintain separate, conflicting reservation calendars, resulting in double-bookings and underutilized auditoriums.
- **Complex Invariants**: Venue reservations often fail to account for mandatory setup and teardown buffers, equipment provisioning, or departmental priority rules.
- **Seat Allocation Bottlenecks**: Academic conferences, guest lectures, and cultural events struggle with ticketing and seat allocation. High-demand events suffer race conditions and overbooking.
- **Lack of Real-Time Visibility**: Organizers and attendees lack real-time updates regarding room changes, registration confirmations, or waitlists.

**CampusVenue** solves this by providing a unified, auditable, high-reliability platform that integrates space discovery, timetable conflict detection, coordinator approvals, dynamic 2D seating reservations, and real-time event publishing for a university campus.

---

## 2. Scope & Non-Goals

### In Scope (Phased Delivery)
- Multi-department organizational hierarchy and role-based permissions.
- Comprehensive facility catalog: auditoriums, lecture rooms, seminar halls, outdoor grounds, and flexible multipurpose spaces.
- Availability calendar with academic timetable blockouts, maintenance windows, and buffer times.
- Two-stage reservation workflows: coordinator request and facility administrator review.
- Explainable venue recommendation engine based on attendee capacity, required AV assets, and accessibility constraints.
- Event lifecycle management: draft, published, registration window, live, completed, cancelled.
- Diverse participant admission modes: assigned seating (reserved), general admission (capacity capped), and open admission (no registration).
- Interactive 2D seating layout designer with version control.
- Concurrent-safe seat holds and allocations preventing double-booking under high contention.
- Real-time updates via SignalR and background notifications.
- Attendance verification, check-in tracking, and audit trail logs.

### Non-Goals (Out of Scope for Initial Releases)
- Financial payment gateways and ticket monetization (events are university-sponsored or free admission).
- Conversational AI chatbots or natural language booking bots.
- Native mobile applications (iOS/Android); fully responsive mobile web is standard.
- Premature distributed microservices or multi-cloud orchestrations.
- Deep integration with external legacy enterprise ERPs (LDAP/SIS integrations planned for future enterprise connectors).

---

## 3. User Personas

| Persona | Role | Key Objectives & Pain Points |
| ------- | ---- | ---------------------------- |
| **Dr. Karen Vance** | *University Administrator* | Needs governance over institutional policies, user role assignments, audit logs of all room allocations, and system-wide usage metrics. |
| **Marcus Chen** | *Facility Administrator* | Manages building inventory, defines room equipment/capacity, reviews reservation requests, resolves scheduling conflicts, and schedules maintenance blocks. |
| **Sarah Jenkins** | *Event Coordinator* (Faculty / Student Club Lead) | Seeks suitable venues for academic symposiums or student club meetings, submits booking requests, creates seating layouts, and publishes event registrations. |
| **Alex Rivera** | *Participant / Student* | Browses campus events, reserves assigned seats or registers for general admission, receives real-time schedule updates, and checks in via ticket/QR code. |

---

## 4. Role & Permission Matrix

| Capability / Action | University Admin | Facility Admin | Event Coordinator | Participant |
| ------------------- | :--------------: | :------------: | :---------------: | :---------: |
| Manage University Orgs & Roles | :white_check_mark: | :x: | :x: | :x: |
| Manage Buildings & Space Definitions | :white_check_mark: | :white_check_mark: | :x: | :x: |
| Set Timetable Blocks & Maintenance | :white_check_mark: | :white_check_mark: | :x: | :x: |
| Request Space Reservation | :white_check_mark: | :white_check_mark: | :white_check_mark: | :x: |
| Approve / Reject Space Reservation | :white_check_mark: | :white_check_mark: (Assigned Buildings) | :x: | :x: |
| Create & Publish Events | :white_check_mark: | :white_check_mark: | :white_check_mark: | :x: |
| Design 2D Hall Layouts | :white_check_mark: | :white_check_mark: | :white_check_mark: | :x: |
| Register for Event / Select Seat | :white_check_mark: | :white_check_mark: | :white_check_mark: | :white_check_mark: |
| Check-in Attendees & View Reports | :white_check_mark: | :white_check_mark: | :white_check_mark: (Own Events) | :x: |
| View System Audit Logs | :white_check_mark: | :x: | :x: | :x: |

---

## 5. Functional Requirements (FR)

- **FR-01 (Organization Management)**: Model departments, faculties, and student societies with assigned coordinators.
- **FR-02 (Space Catalog)**: Model physical buildings, floors, rooms, space types, capacity, AV equipment, and accessibility tags.
- **FR-03 (Conflict-Free Availability)**: Compute availability dynamically by checking confirmed reservations, academic timetable blocks, and mandatory setup/cleanup buffer windows.
- **FR-04 (Approval Workflow)**: Provide state machine for reservations (`Draft` &rarr; `Submitted` &rarr; `UnderReview` &rarr; `Approved` / `Rejected` / `Cancelled`).
- **FR-05 (Recommendation Engine)**: Algorithmic ranking of candidate spaces based on required headcount, accessibility, required audio-visual gear, and proximity.
- **FR-06 (Event & Session Lifecycle)**: Allow multi-session events with distinct schedules, descriptions, and admission models.
- **FR-07 (Interactive Seating)**: 2D grid/polygon layout editor with seat tiers (VIP, Standard, Accessible).
- **FR-08 (Concurrency Safe Booking)**: Temporary hold mechanism (e.g., 10-minute hold) with transactional safety preventing seat double-booking.
- **FR-09 (Real-Time Synchronization)**: Broadcast seat availability and reservation approvals over SignalR websockets.
- **FR-10 (Audit & Reporting)**: Append-only audit logging for state transitions, cancellations, and administrative overrides.

---

## 6. Non-Functional Requirements (NFR)

- **NFR-01 (Reliability & Consistency)**: Zero double-booking tolerance. Transactions on seat allocation must execute with ACID guarantees.
- **NFR-02 (Performance)**:
  - 95th percentile latency for availability queries under 200ms.
  - Seating hold requests process within 100ms under 500 concurrent requests.
- **NFR-03 (Security)**:
  - OWASP Top 10 mitigation: SQL injection prevention (EF Core parameterized queries), XSS sanitization, strict CORS, and CSRF protection.
  - Server-side role and organizational tenant enforcement on all endpoints.
- **NFR-04 (Maintainability)**:
  - Modular Monolith structure with clear domain boundaries.
  - 100% executable unit/integration test suite covering core domain invariants.
- **NFR-05 (Observability)**:
  - Structured JSON logs with correlation IDs.
  - OpenTelemetry readiness for traces, metrics, and health checks.

---

## 7. Fundamental Domain Distinctions

1. **Space vs. Reservation**: A *Space* is a permanent institutional asset (e.g., "Hall A"). A *Reservation* is a temporary granted entitlement for a specific time window.
2. **Event vs. Session**: An *Event* is the overarching concept (e.g., "Annual AI Symposium"). A *Session* is a distinct time-slotted occurrence (e.g., "Morning Keynote 09:00 - 11:00").
3. **Physical Seat vs. Seat Allocation**: A *Physical Seat* belongs permanently to a layout version. A *Seat Allocation* is a participant's assigned claim to a seat for an individual session.
4. **Space Type vs. Admission Mode**: A Space's type (Auditorium, Seminar Room, Open Lawn) does not dictate the event's Admission Mode (Assigned Seating, General Admission, No Registration).
5. **Setup/Cleanup Buffer**: Scheduling conflict queries must evaluate `[Reservation.Start - SetupTime]` through `[Reservation.End + CleanupTime]`.
6. **Layout Version Immutability**: Once an event opens registration on a layout version, that layout is immutable; alterations require publishing a new version with migration rules.
