---
description: Documentation maintenance and phase-completion criteria.
trigger: model_decision
---

# Documentation & Phase Completion Standards

## 1. Documentation Authenticity & Maintenance

- **Never Misrepresent Status**:
  - Never document planned or future features as completed or live.
  - Distinguish between **Implemented (Phase 0)**, **In Progress**, and **Planned (Phases 1-13)**.
  - Do not fabricate benchmark figures, test percentages, deployment links, or fictional client rosters.
- **Continuous Documentation Sync**:
  - Keep root `README.md`, architecture specifications (`docs/architecture/`), and progress logs (`docs/progress/`) aligned with actual working code.
  - Architecture Decision Records (ADRs) must be updated whenever fundamental technologies or architectural boundaries are introduced or altered.

## 2. Phase Completion Gate

A phase cannot be marked complete until all following conditions are satisfied:
1. **Verification Executed**: All unit and integration test suites run, pass, and outputs are logged.
2. **Lint & Build Clean**: No unhandled compiler warnings, TypeScript errors, or broken imports.
3. **Infrastructure Validated**: Docker Compose configurations validated and container health verified.
4. **Secrets Audited**: Configuration files and Git tracking checked to ensure zero credential leaks.
5. **Manual Runbook Provided**: Explicit, reproducible Windows PowerShell commands and browser test URLs provided for user verification.
6. **Next Scope Clear**: Explicit summary of the completed phase and precise scope definition for the subsequent phase, with zero speculative future code written.
