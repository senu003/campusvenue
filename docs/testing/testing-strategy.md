# CampusVenue — Testing Strategy & Quality Assurance

This document defines the automated and manual testing standards for the CampusVenue monorepo.

---

## 1. Testing Pyramid

```
       /\
      /  \      E2E Tests (Playwright - Future Phases)
     /----\
    /      \    Integration Tests (WebApplicationFactory + PostgreSQL)
   /--------\
  /          \  Unit & Slice Tests (xUnit + Vitest + React Testing Library)
 /------------\
```

- **Unit Tests (70%)**: Fast, in-memory validation of business logic, invariant enforcement, interval math, recommendation scoring algorithms, and React UI components.
- **Integration Tests (25%)**: API pipeline testing, EF Core database operations against real PostgreSQL, transaction rollbacks, and authentication/authorization checks.
- **End-to-End Tests (5%)**: Planned in Phase 12 for critical journeys (seat hold & registration flow).

---

## 2. Backend Testing Practices

### Smoke & Health Verification
Smoke tests verify application bootstrapping, dependency injection container validity, database connectivity, and health check endpoints.

### Real Database vs. InMemory
- **Strict Rule**: EF Core InMemory provider does not enforce foreign keys, relational nullability, or PostgreSQL-specific locking behaviors (`FOR UPDATE`).
- Therefore, integration tests that involve queries, transactions, or migrations must target a real PostgreSQL container (e.g., via Testcontainers or local Docker Compose).

### Test Coverage Expectations
Every future business feature must include:
1. **Positive Path**: Valid data executes successfully and produces expected HTTP status (`200 OK` or `201 Created`).
2. **Validation Failure**: Invalid payload returns `400 Bad Request` or `422 Unprocessable Entity` with RFC 7807 validation errors.
3. **Authorization Failure**: Access by unauthenticated user returns `401 Unauthorized`; insufficient privileges return `403 Forbidden`.
4. **Boundary & Conflict Cases**: Concurrency conflict or schedule overlap returns `409 Conflict`.

---

## 3. Frontend Testing Practices

- Components are tested using **Vitest** and **React Testing Library**.
- Test behavioral outcomes from the user perspective rather than implementation details:
  - Query elements by role (`getByRole('button', { name: /check/i })`).
  - Verify that asynchronous requests correctly cycle through `Loading` &rarr; `Success` or `Error`.
  - Assert that error boundaries intercept failures gracefully.

---

## 4. Verification Protocol for AI & Developers

- **Rule**: Never assert or report tests as passing without having actually executed the command and inspected the output.
- Log command line strings, exit codes, passed/failed test counts, and duration in reports.
