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
- **Component Design & Clarity**:
  - Prefer clear, self-describing names and small, focused functions and composable components.
  - Separate container logic (data fetching, state management) from presentation components.
  - Explicitly handle three core states for asynchronous operations:
    1. **Loading state**: Clear skeletons or spinners (no jarring layout shifts).
    2. **Error state**: Clear user-friendly messages with recovery or retry actions.
    3. **Empty state**: Informative empty views when lists, records, or datasets are empty.

## 3. Styling & Accessibility

- Use Tailwind CSS utility classes; avoid inline styles.
- Maintain consistent spacing, typography, and semantic HTML elements (`<header>`, `<main>`, `<nav>`, `<section>`, `<article>`).
- Ensure accessible interactive elements (keyboard navigability, ARIA attributes where needed, legible contrast ratios).
- Responsive mobile-first design supporting mobile, tablet, and desktop breakpoints.

## 4. Architecture, Feature Folders & App Wiring

- **Feature Folders**:
  - Organize frontend code into dedicated feature folders (e.g., `src/features/auth/`, `src/features/facilities/`, `src/features/reservations/`).
  - Co-locate feature views, forms, API methods, and feature types within their respective feature folder.
  - Reuse existing components, contexts, and API functions where appropriate; avoid reorganizing working features just to match an arbitrary folder template.
- **Application Shell Wiring**:
  - Keep `App.tsx` focused on top-level application wiring: navigation tabs, top-level routing, root providers, and the root `ErrorBoundary`.
  - Feature-specific business screens, form dialogs, and workflows belong inside their feature folders, not inline in `App.tsx`.
- **Sensitive State & Secrets**:
  - Never store session tokens, auth cookies, or passwords in `localStorage` or `sessionStorage`.
  - Keep personal test data, passwords, and tokens out of client logs, documentation, and Git.
- **Shared Primitives & Utilities**:
  - Shared UI primitives belong in `src/components/ui/` (buttons, inputs, modals, alerts).
  - Shared utilities belong in `src/lib/` or `src/utils/`.
  - Never hardcode backend URLs; use `import.meta.env.VITE_API_BASE_URL`.
