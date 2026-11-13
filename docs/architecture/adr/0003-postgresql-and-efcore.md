# ADR 0003: PostgreSQL and Entity Framework Core with Npgsql

## Status
Accepted

## Date
2026-11-13

## Context
CampusVenue requires a robust relational database capable of handling complex relational constraints, temporal date/time range queries (reservations, setup/cleanup buffers), high-concurrency transactional locking (preventing seat double-booking), and JSONB spatial coordinate storage (for 2D seating layouts).

## Decision
We select **PostgreSQL 16+** as the primary relational database, accessed through **Entity Framework Core (EF Core)** and the **Npgsql.EntityFrameworkCore.PostgreSQL** provider.

## Justification
- **Advanced Relational & Concurrency Features**: PostgreSQL offers row-level locking (`FOR UPDATE`, `SKIP LOCKED`), serializable isolation, range types (`tsrange`), and exclusion constraints (`EXCLUDE USING gist`) which are ideal for interval scheduling and conflict prevention.
- **Rich Semi-Structured & Spatial Data**: Native JSONB support enables flexible storage and querying of complex 2D vector seating layouts without requiring an auxiliary NoSQL database.
- **EF Core 9 / 10 Developer Ergonomics**: First-class LINQ support, strong typing, forward-only migrations, compiled models, and high performance via Npgsql.
- **Cost & Cloud Portability**: Open source, mature ecosystem, and universally supported across cloud providers (AWS RDS, Azure Database for PostgreSQL, GCP Cloud SQL, or self-hosted Docker).

## Consequences
- Requires PostgreSQL running for full integration testing (mitigated via Docker Compose locally and in CI).
- Developers must maintain migration hygiene and avoid speculative schema creation.
