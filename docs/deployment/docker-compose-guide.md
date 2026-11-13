# CampusVenue — Docker & Container Infrastructure Guide

This guide details the containerized infrastructure used for local development, testing, and continuous integration.

---

## 1. Services Overview

CampusVenue defines three primary services in `docker-compose.yml`:

| Service | Image / Base | Internal Port | Host Port | Purpose |
| ------- | ------------ | :-----------: | :-------: | ------- |
| `postgres` | `postgres:16-alpine` | 5432 | 5432 | Primary relational database for all modules |
| `backend` | .NET 9.0 SDK / ASP.NET runtime | 5000 | 5000 | Modular Monolith API (`CampusVenue.Api`) |
| `frontend` | Node.js 20 Alpine / Nginx | 3000 | 3000 | React + Vite client single-page app |

---

## 2. PostgreSQL Configuration

- **Image**: Official `postgres:16-alpine` (lightweight, security-patched).
- **Persistent Volume**: Named volume `campusvenue_pgdata` preserves database tables across container restarts.
- **Health Check**:
  Uses `pg_isready -U campusvenue_user -d campusvenue_dev` to signal readiness to dependent containers.
- **Development Credentials**:
  Standardized in `.env.example` for local developer convenience. **Never use local development credentials in production or public clouds.**

---

## 3. Docker Compose Commands

```powershell
# 1. Start PostgreSQL only (recommended when running API & Frontend locally on host)
docker compose up -d postgres

# 2. Start all services (PostgreSQL, Backend API, Frontend)
docker compose up -d

# 3. View live logs across all containers
docker compose logs -f

# 4. View logs for a specific service
docker compose logs -f postgres

# 5. Stop all services without deleting data volume
docker compose down

# 6. Stop and wipe all data volumes (complete reset)
docker compose down -v
```

---

## 4. Troubleshooting Container Issues

- **Port Conflict (5432 already in use)**:
  If a local PostgreSQL service is already running on port 5432 on your Windows host, alter `POSTGRES_PORT` in your `.env` file (e.g., `POSTGRES_PORT=5433`).
- **PostgreSQL Container Unhealthy**:
  Check logs via `docker compose logs postgres` to ensure permissions and initialization scripts succeeded.
