---
description: Frontend standards for React, TypeScript, Tailwind CSS, and state management.
trigger: glob
globs:
  - "frontend/**"
  - "**/*.ts"
  - "**/*.tsx"
  - "**/*.css"
---

# Frontend & TypeScript Standards

## 1. Stack & Tooling

- **Core Stack**: React 18+ / 19, TypeScript (strict mode), Vite, Tailwind CSS.
- **Data Fetching**: Use TanStack Query (React Query) for server state caching, pagination, deduplication, and mutation management.
- **Routing**: React Router (when multi-page routing is required).
- **Testing**: Vitest + React Testing Library for unit and component testing.

## 2. Code Quality & Typing

- **Strict Typing**:
  - `strict: true` in `tsconfig.json`.
  - Zero tolerance for `any`. Use `unknown` with type guards or exact TypeScript interfaces/types matching backend API DTOs.
  - Export shared API types from `src/types/` or co-locate with feature slices.
- **Component Design**:
  - Small, focused, composable functional components.
  - Separate container logic (data fetching, state) from presentation components.
  - Always handle three core states for asynchronous operations:
    1. **Loading state**: Clear skeletons or spinners (no jarring layout shifts).
    2. **Error state**: Clear user-friendly messages with retry action.
    3. **Empty state**: Informative empty views when lists or data are empty.

## 3. Styling & Accessibility

- Use Tailwind CSS utility classes; avoid inline styles.
- Maintain consistent spacing, typography, and semantic HTML elements (`<header>`, `<main>`, `<nav>`, `<section>`, `<article>`).
- Ensure accessible interactive elements (keyboard navigability, ARIA attributes where needed, legible contrast ratios).
- Responsive mobile-first design supporting mobile, tablet, and desktop breakpoints.

## 4. Architecture & Boundaries

- Organize frontend code into feature slices matching backend modules where applicable (e.g., `src/features/facilities/`, `src/features/reservations/`).
- Shared UI primitives belong in `src/components/ui/` (buttons, inputs, modals, alerts).
- Shared utilities belong in `src/lib/` or `src/utils/`.
- Never hardcode backend URLs; use `import.meta.env.VITE_API_BASE_URL`.
