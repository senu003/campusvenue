---
description: Backend and architecture standards for ASP.NET Core, Vertical Slice Architecture, and C# code.
trigger: glob
globs:
  - "backend/**"
  - "**/*.cs"
  - "**/*.csproj"
  - "**/*.sln*"
---

# Backend & Architecture Standards

## 1. Architectural Structure & Modularity

- **Modular Monolith**: The backend is hosted as a single deployable ASP.NET Core application (`CampusVenue.Api`), partitioned into distinct business modules:
  - `Identity`: Authentication, roles, permissions, user profiles.
  - `Organizations`: University hierarchy, academic departments, student societies.
  - `Facilities`: Buildings, spaces (auditoriums, lecture halls, seminar rooms, flexible grounds).
  - `Reservations`: Timetable constraints, coordinator requests, conflict detection, approval workflow.
  - `Events`: Event details, multi-session management, public/private publishing.
  - `Registrations`: Participant registration, attendance policies, admission types.
  - `Seating`: Interactive 2D layout definitions, seat versioning, high-concurrency hold & allocation.
  - `Notifications`: Out-of-band email, in-app alerts, real-time broadcasts.
- **Vertical Slice Architecture**:
  - Organize features by business capability rather than technical layering (e.g., Avoid artificial `Controllers`, `Services`, `Repositories` folders across the whole project).
  - Keep request models, validation, handler logic, and response DTOs co-located within feature slices.
- **Dependency Guardrails**:
  - Direct database access across module boundaries is prohibited. Modules communicate via strongly typed internal interfaces or domain events.
  - No unnecessary generic repository or unit-of-work wrappers around EF Core `DbContext`.
  - Avoid introducing MediatR, CQRS libraries, or external message brokers without formal ADR approval.

## 2. API Design & Error Handling

- **REST Conventions**: Use standard HTTP methods (`GET`, `POST`, `PUT`, `PATCH`, `DELETE`) with appropriate status codes (`200 OK`, `201 Created`, `204 No Content`, `400 Bad Request`, `401 Unauthorized`, `403 Forbidden`, `404 Not Found`, `409 Conflict`, `422 Unprocessable Entity`).
- **Standardized Error Responses**:
  - All errors must adhere to RFC 7807 **Problem Details**.
  - Internal exceptions must be intercepted by `GlobalExceptionHandler` and mapped to clean Problem Details.
  - Do not expose stack traces or database schema details in production mode.
- **Server-Side Validation**:
  - Validate all command inputs on the server using FluentValidation or DataAnnotations before processing.
  - Client-side validation is for UX only; server invariants are authoritative.

## 3. C# & .NET Standards

- Target modern LTS/Current C# and ASP.NET Core with nullable reference types enabled (`<Nullable>enable</Nullable>`).
- Use file-scoped namespaces (`namespace CampusVenue.Api.Modules...;`).
- Prefer asynchronous I/O with `CancellationToken` support throughout all database and network operations.
- Leverage Dependency Injection with appropriate lifetimes: `Scoped` for DbContext and per-request services; `Singleton` for stateless utilities; `Transient` for lightweight factories.
- Use structured logging via `ILogger<T>` with message templates (e.g., `_logger.LogInformation("Processing reservation request {ReservationId}", id)`), never string interpolation.
