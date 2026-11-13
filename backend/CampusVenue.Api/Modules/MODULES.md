# CampusVenue Backend Module Boundaries & Conventions

This directory defines the boundary structure for all business modules in the **CampusVenue Modular Monolith**.

In accordance with Phase 0 directives, no empty dummy classes or superficial placeholder services are created. Instead, this specification establishes the code organization, dependency contracts, and vertical slice layout that each phase will populate.

---

## 1. Module Boundaries Overview

| Module Name | Domain Scope | Scheduled Phase | Key Invariants & Responsibilities |
| ----------- | ------------ | :-------------: | --------------------------------- |
| `Identity` | Authentication, tokens, password hashing, role permissions | Phase 1 | Verifies user authenticity, issues secure cookies/JWTs, manages RBAC claims. |
| `Organizations` | University hierarchy, departments, student societies | Phase 1 | Enforces organizational tenancy and coordinator assignment scopes. |
| `Facilities` | Physical assets: buildings, floors, rooms, equipment, space types | Phase 2 | Maintains authoritative space catalog, capacities, audio-visual tags, and accessibility flags. |
| `Reservations` | Timetable constraints, reservation requests, approvals | Phases 3 & 4 | Prevents schedule overlap (including setup/cleanup buffers), state machine transitions. |
| `Events` | Event records, multi-session schedules, public publishing | Phase 6 | Coordinates sessions against approved reservations, controls event lifecycle. |
| `Registrations` | Participant admissions, tickets, QR codes, capacity counts | Phase 7 | Enforces admission rules (Assigned Seating, General Admission, Open). |
| `Seating` | 2D vector layouts, versioning, high-concurrency seat holds | Phases 8 & 9 | Manages 10-minute seat holds using serializable/row-level locks to guarantee zero double-bookings. |
| `Notifications` | Asynchronous email alerts, SignalR real-time broadcasts | Phase 10 | Dispatches out-of-band emails and broadcasts WebSocket state updates. |

---

## 2. Vertical Slice Internal Structure (To Be Populated per Phase)

When a phase begins, the module adopts the vertical slice convention:

```
Modules/<ModuleName>/
├── Models/             # Domain entities, value objects, domain invariants
├── Features/           # Vertical slices for use cases
│   ├── Create<Feature>/
│   │   ├── Create<Feature>Endpoint.cs  # HTTP mapping & OpenAPI docs
│   │   ├── Create<Feature>Command.cs   # Request DTO & validation
│   │   └── Create<Feature>Handler.cs   # Business logic & DbContext operation
│   └── Get<Feature>/
│       ├── Get<Feature>Endpoint.cs
│       └── Get<Feature>Query.cs
└── Configurations/     # EF Core IEntityTypeConfiguration<T>
```

---

## 3. Inter-Module Interaction Rules

1. **No Cross-Module Database Table Access**: A slice in `Reservations` must not directly write to or update tables owned by `Identity` or `Facilities`.
2. **Explicit Contracts**: Inter-module communication must proceed through defined in-memory interfaces or published domain events.
3. **Database Transactions**: Cross-slice operations requiring atomic guarantees must be coordinated within explicit unit-of-work transactions in `CampusVenueDbContext`.
