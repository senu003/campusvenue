# CampusVenue — Local Developer Environment Setup

This guide walks you through setting up, configuring, and verifying the **CampusVenue** development environment on your local workstation.

---

## 1. Prerequisites

Ensure the following tools are installed:
- **Git** (v2.40+)
- **Docker & Docker Compose** (Docker Desktop on Windows/macOS or Docker Engine on Linux)
- **Node.js** (v20 LTS recommended, minimum v18+) & **npm** (v10+)
- **.NET SDK** (v10.0 LTS installed locally or accessed via the official Docker SDK container `mcr.microsoft.com/dotnet/sdk:10.0-alpine`)

---

## 2. Repository Configuration

1. Clone or navigate to the repository root:
   ```powershell
   cd E:\CampusVenue
   ```

2. Create your local environment configuration by copying `.env.example`:
   ```powershell
   Copy-Item .env.example .env
   ```
   > [!NOTE]
   > `.env` is git-ignored and holds local overrides. Never commit sensitive values.

---

## 3. Starting the Local Database (PostgreSQL)

CampusVenue uses Docker Compose to manage a PostgreSQL 16 instance:

```powershell
# Start PostgreSQL in background
docker compose up -d postgres

# Check status and health
docker compose ps
```

The database container includes an automatic health check and persists data in a Docker named volume (`campusvenue_pgdata`).

---

## 4. Running the Backend API

### Option A: Via Host .NET 10 SDK (if installed)
```powershell
cd backend/CampusVenue.Api
dotnet run
```
The API starts at `http://localhost:5000` (or configured port).
Swagger / OpenAPI is available in development mode at:
`http://localhost:5000/openapi/v1.json` or `http://localhost:5000/swagger`

### Option B: Via Docker (.NET 10 Container)
```powershell
docker compose up -d --build backend
```

---

## 5. Running the Frontend SPA

```powershell
cd frontend
npm install
npm run dev
```

The Vite dev server starts at `http://localhost:3000`.
Open your browser and navigate to `http://localhost:3000` to view the application landing and health verification page.

---

## 6. Verification Commands

Run the full automated test suites to verify integrity:

```powershell
# Run backend tests (.NET 10 Container)
docker run --rm -v "${PWD}/backend:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0-alpine dotnet test CampusVenue.sln

# Run frontend tests
cd frontend
npm run test

# Run frontend production build
npm run build
```
