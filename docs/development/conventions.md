# CampusVenue — Code & Architecture Conventions

This document establishes development conventions to ensure consistency across the entire monorepo.

---

## 1. Naming Conventions

| Artifact | Convention | Example |
| -------- | ---------- | ------- |
| **C# Classes / Interfaces** | PascalCase | `CampusVenueDbContext`, `IReservationService` |
| **C# Methods / Properties** | PascalCase | `GetAvailabilityAsync`, `MaxCapacity` |
| **C# Private Fields** | `_camelCase` | `_logger`, `_dbContext` |
| **C# Local Variables** | camelCase | `reservationRequest`, `seatCount` |
| **C# Constants** | PascalCase | `DefaultBufferMinutes` |
| **API Endpoints (URIs)** | kebab-case plural | `/api/spaces`, `/api/event-sessions` |
| **JSON Properties** | camelCase | `{ "spaceId": "...", "startTime": "..." }` |
| **Database Tables** | snake_case plural | `spaces`, `reservations`, `hall_layouts` |
| **Database Columns** | snake_case | `created_at`, `max_capacity`, `building_id` |
| **TypeScript Types/Interfaces** | PascalCase | `SpaceDto`, `HealthStatusResponse` |
| **TypeScript Variables/Functions** | camelCase | `fetchHealthStatus`, `isLoading` |
| **React Components** | PascalCase | `SystemStatusCard`, `AppLayout` |
| **CSS Classes** | Tailwind utilities | `flex items-center text-sm font-medium` |

---

## 2. Directory Structure Conventions

### Backend: Vertical Slices
```
backend/CampusVenue.Api/
├── Common/                    # Shared DTOs, utilities, base records
├── Infrastructure/            # DbContext, Middleware, Health, External clients
└── Modules/                   # Business domain modules
    ├── Facilities/            # Feature module
    │   ├── Models/            # Domain entities
    │   ├── Endpoints/         # HTTP Route handlers
    │   └── Queries/           # Read query logic
    └── Reservations/
```

### Frontend: Feature-Driven Layout
```
frontend/src/
├── assets/                    # Static images, logos
├── components/                # Reusable presentation components (UI primitives)
│   └── ui/                    # Button, Card, Spinner, Alert
├── features/                  # Domain feature slices (mirroring backend)
│   └── status/
├── hooks/                     # Custom React hooks
├── services/                  # TanStack Query fetchers & API clients
└── types/                     # Shared TypeScript interfaces & types
```

---

## 3. Git Commit Conventions

Format: `<type>(<scope>): <concise subject>`
Examples:
- `feat(facilities): add space availability filter query`
- `fix(reservations): prevent overlapping reservation edge-case`
- `docs(adr): record modular monolith architectural decision`
- `chore(deps): update npgsql to latest patch release`
