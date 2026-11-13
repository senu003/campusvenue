# ADR 0005: Frontend Technology Stack (React, TypeScript, Vite, Tailwind CSS)

## Status
Accepted

## Date
2026-11-13

## Context
CampusVenue requires a modern, responsive, high-performance web frontend capable of rendering complex interactive UIs, including a calendar matrix, 2D vector seating layouts, real-time ticket counters, and administrative dashboards.

## Decision
We select:
- **React 18 / 19** with **TypeScript** (strict mode enabled).
- **Vite** as the build tool and development server.
- **Tailwind CSS** for styling and responsive design.
- **TanStack Query (React Query)** for server state management, caching, and mutation orchestration.
- **Vitest & React Testing Library** for automated component and unit testing.

## Justification
- **Ecosystem & Community**: React has the largest ecosystem of interactive components, canvas libraries, and rich calendar tooling.
- **Type Safety**: TypeScript provides end-to-end type safety aligning frontend interfaces with backend C# DTOs, reducing runtime errors.
- **Vite Developer Experience**: Lightning-fast Hot Module Replacement (HMR), rapid build speeds, and optimized Rollup-based production bundles.
- **Tailwind CSS Utility First**: Rapid UI prototyping without leaving markup, consistent design tokens, zero runtime CSS-in-JS overhead, and minimal bundle sizes via PurgeCSS.
- **Separation of Server vs. Client State**: TanStack Query completely isolates caching, refetching, and pagination from local UI state.

## Consequences
- Requires maintaining client-side type definitions synchronized with backend OpenAPI contracts.
- Frontend build steps are integrated into the monorepo CI workflow.
