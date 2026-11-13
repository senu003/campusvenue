# ADR 0001: Modular Monolith Architecture over Microservices

## Status
Accepted

## Date
2026-11-13

## Context
CampusVenue serves university campus event scheduling, venue bookings, and high-concurrency seat reservation. When designing the backend architecture, the primary architectural options were:
1. Distributed Microservices Architecture (separate deployable services for Identity, Facilities, Reservations, Seating, etc.).
2. Traditional Layered Monolith (single codebase with monolithic horizontal layers: Controllers, Services, Repositories).
3. Modular Monolith Architecture (single deployable application partitioned into strictly bounded business modules).

## Decision
We choose the **Modular Monolith Architecture**.

All business capabilities will reside within a single deployable ASP.NET Core project (`CampusVenue.Api`), organized into well-defined module namespaces (`Identity`, `Organizations`, `Facilities`, `Reservations`, `Events`, `Registrations`, `Seating`, `Notifications`). Modules interact via explicitly typed in-process contracts and domain events rather than RPC/HTTP network hops.

## Justification
- **Operational Simplicity**: Avoids premature distributed systems overhead: distributed transactions, network latency, complex service meshes, multiple deployment pipelines, and observability silos.
- **Transactional Consistency**: Allows transactional seat locking and reservation approval workflows within PostgreSQL using native ACID transactions without requiring distributed saga coordinators.
- **Maintainability & Refactoring**: Boundary adjustments between modules during early product evolution are safe and refactorable within one C# compilation unit.
- **Future Extensibility**: Clean module boundaries ensure that if an individual module (e.g., high-throughput `Seating`) ever demands independent scaling in the future, it can be extracted into a separate service with minimal architectural friction.

## Consequences
- Requires strict discipline to prevent tight coupling across module boundaries (enforced via architecture tests and code reviews).
- Shared database must enforce module data encapsulation (no cross-module table joins without formal public query views or contracts).
