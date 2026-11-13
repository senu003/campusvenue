# ADR 0002: Vertical Slice Architecture within Business Modules

## Status
Accepted

## Date
2026-11-13

## Context
Traditional Clean or N-Tier Architecture divides code by technical concern: `Controllers`, `Services`, `Interfaces`, `Repositories`, `Entities`, and `DTOs`. While conceptually structured, in practice this leads to high coupling across technical layers for even minor feature changes (the "shotgun surgery" smell) and creates heavy abstractions (generic repositories, service classes that merely forward calls) that add cognitive load without delivering architectural value.

## Decision
We adopt **Vertical Slice Architecture** within each business module.

Each business feature or use case (e.g., `ReserveSpaceCommand`, `GetSpaceAvailabilityQuery`) contains its endpoint definition, request validation, business logic, persistence operations, and response DTO co-located in a dedicated slice.

## Justification
- **High Cohesion, Low Coupling**: Code that changes together lives together. Modifying how space availability is computed touches only the availability slice, without risk of breaking other features.
- **Elimination of Artificial Abstractions**: No generic repository or unit-of-work classes wrapping EF Core. EF Core `DbContext` is already a sophisticated repository and unit-of-work implementation. Slices query `DbContext` directly with optimized LINQ projections (`Select`).
- **Targeted Performance Optimization**: Individual queries can tune projection and caching strategies (e.g., `AsNoTracking()`, custom SQL where needed) without polluting global generic repositories.
- **Ease of Onboarding & Auditing**: Reviewers can read a single file or directory to understand the entire lifecycle of an API request.

## Consequences
- Requires discipline to prevent code duplication across slices; truly shared domain logic or entities are placed in the module's shared domain folder.
- Does not mandate external libraries like MediatR; slices can be implemented directly using ASP.NET Core Minimal APIs or lightweight endpoint handlers.
