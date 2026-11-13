---
description: Testing and verification standards for unit, integration, and contract tests.
trigger: model_decision
---

# Testing & Verification Standards

## 1. Core Testing Rules

- **Execution Truthfulness**:
  - **NEVER claim tests passed unless actually executed in the environment.**
  - Always report exact commands run, full test counts, duration, and pass/fail summary.
  - If a test fails, report the error verbatim and provide the fix.
- **No Synthetic Runtime Fakes**:
  - Do not create fake API endpoints or hardcoded mock data in production code paths to simulate passing features.
  - Test doubles (mocks/stubs) belong strictly within automated test assemblies (`CampusVenue.Tests`).

## 2. Backend Testing

- **Framework**: xUnit, FluentAssertions, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`).
- **Integration Tests**:
  - Favor real PostgreSQL database integration tests (via Docker/Testcontainers) over EF Core InMemory provider (which does not validate relational constraints, foreign keys, or SQL transactions).
- **Test Categories**:
  - **Unit Tests**: Test pure business logic, calculations, domain invariants, and conflict detection algorithms.
  - **Integration Tests**: Test API endpoints end-to-end, middleware, authentication handlers, database queries, and transaction rollbacks.
  - **Edge-Case Coverage**: Every feature must test:
    - Positive path (happy path)
    - Negative validation errors (400 / 422)
    - Unauthorized / Forbidden access (401 / 403)
    - Concurrency conflicts / Overlaps (409)

## 3. Frontend Testing

- **Framework**: Vitest + React Testing Library.
- **Scope**:
  - Test user interactions (button clicks, form inputs).
  - Test rendering under all 3 asynchronous states: loading, error, and loaded data.
  - Mock API responses at the network boundary (e.g., using MSW - Mock Service Worker or Vitest fetch mocks) inside test suites.
