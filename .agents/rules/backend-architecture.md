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
- **Vertical Slice Architecture & Use-Case Folders**:
  - Organize features by business capability rather than technical layering (e.g., Avoid artificial `Controllers`, `Services`, `Repositories` folders across the whole project).
  - Use backend module and use-case feature folders (e.g., `Modules/<Module>/Features/<UseCase>/`). Keep request contracts, validation, handler logic, database queries, and response DTOs co-located within their use-case folder.
  - Work one vertical feature at a time, delivering the database schema, API endpoints, React UI, tests, and documentation as a single runnable slice.
  - Reuse existing code, models, and utilities where appropriate. Avoid reorganizing working modules just to match an arbitrary folder template.
- **Application Startup Wiring**:
  - Keep `Program.cs` focused strictly on application wiring: dependency injection configuration, middleware pipeline ordering, and module endpoint group mapping (e.g., `app.MapAuthEndpoints()`).
  - Route handlers and endpoint logic belong in their dedicated feature slices, not directly inline in `Program.cs`.
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
- **Server-Side Validation & Authorization**:
  - Validate and authorize all requests on the server before executing business logic. Never trust client-provided claims, roles, or tenant IDs.
  - Client-side validation is for UX only; server-side invariants are authoritative.
  - Return HTTP 401 for unauthenticated requests and HTTP 403 for unauthorized requests with RFC 7807 Problem Details (no HTML redirects).

## 3. C# & .NET Standards

- **Code Clarity & Function Scope**:
  - Prefer clear, self-describing names and small, focused functions that accomplish a single responsibility.
- Target modern LTS/Current C# and ASP.NET Core with nullable reference types enabled (`<Nullable>enable</Nullable>`).
- Use file-scoped namespaces (`namespace CampusVenue.Api.Modules...;`).
- Prefer asynchronous I/O with `CancellationToken` support throughout all database and network operations.
- Leverage Dependency Injection with appropriate lifetimes: `Scoped` for DbContext and per-request services; `Singleton` for stateless utilities; `Transient` for lightweight factories.
- **Safe Structured Logging**:
  - Use structured logging via `ILogger<T>` with message templates (e.g., `_logger.LogInformation("Processing reservation request {ReservationId}", id)`), never string interpolation.
  - Keep passwords, tokens, cookies, connection strings, and personal test data out of logs, documentation, and Git.
