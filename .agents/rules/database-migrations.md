---
description: Database and migration standards for PostgreSQL and Entity Framework Core.
trigger: model_decision
---

# Database & Migration Standards

## 1. Relational Design & Modeling

- **Database Engine**: PostgreSQL 16+ via Npgsql and Entity Framework Core.
- **Naming Conventions**:
  - Tables: snake_case plural (e.g., `organizations`, `spaces`, `reservations`).
  - Columns: snake_case (e.g., `id`, `created_at`, `building_id`, `max_capacity`).
  - Foreign Keys: `fk_<source_table>_<target_table>_<column>`
  - Primary Keys: `pk_<table_name>`
  - Indexes: `ix_<table_name>_<columns>`
  - Unique Constraints: `ux_<table_name>_<columns>`
- **Identity & Keys**:
  - Use `Guid` (v7 / sequential) or `bigint` (identity) for primary keys. Never expose internal sequential numeric IDs in public URLs if they enable enumeration attacks.
- **Timestamps**:
  - Store all temporal data in UTC (`timestamp with time zone` or `timestamptz`).
  - Present local university timezone in presentation layer only.

## 2. Migration Discipline

- **Forward-Only Migrations**:
  - All schema mutations must be tracked via EF Core Code-First migrations.
  - Never edit or delete previously applied production migrations.
  - Review generated SQL (`dotnet ef migrations script`) before committing migration files.
  - No speculative schema creation: tables, foreign keys, and indexes are added ONLY when the corresponding business phase is being implemented.
- **Constraints & Invariants**:
  - Enforce critical invariants at the database level: foreign keys with proper cascade/restrict rules, `NOT NULL` where appropriate, `CHECK` constraints (e.g., positive capacity, end_time > start_time), and partial unique indexes (e.g., no overlapping active reservations).

## 3. High Concurrency & Seating Transactions

- When implementing seat reservations (Phase 9), use explicit transaction isolation levels (e.g., `Serializable` or `Repeatable Read` with optimistic concurrency tokens / row versions, or `SELECT FOR UPDATE` pessimistic locks) to prevent double-booking.
- Clean up expired temporary holds using an idempotent background job.
