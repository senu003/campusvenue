---
description: Git change management, branching safety, and commit approval rules.
trigger: always_on
---

# Git & Change Management Standards

## 1. Explicit Approval for All Git Operations

- **Autonomous Prohibition**:
  - The agent is **strictly forbidden** from executing any Git mutating commands autonomously. This includes:
    - `git commit`
    - `git push`
    - `git branch` (creation or deletion)
    - `git checkout` / `git switch`
    - `git merge`
    - `git rebase`
    - `git reset` (soft, mixed, or hard)
    - `git clean`
- **User Delegation**:
  - Committing, branching, and pushing are the sole prerogative of the user.
  - When changes are ready, the agent provides concise git commands that the user can review and execute manually.

## 2. File Change Transparency & Scope Discipline

- When files are created, edited, or removed, list every affected file path clearly in the response.
- Do not make opportunistic, sweeping refactorings of unrelated files.
- Preserve existing working code and files created in prior steps unless explicit instructions require updates.
- If a conflict arises between requested changes and existing files, stop and explain the discrepancy before overwriting.

## 3. Commit Granularity & Approval Workflow

- **Meaningful Commit Scope**:
  - Propose a commit when a meaningful change or feature slice is ready.
  - Keep dependent changes and their tests together: a vertical feature slice must bundle its database migration, backend endpoints, React UI, tests, and documentation updates into the same commit.
  - Separate independent bug fixes, performance improvements, or CI adjustments into distinct commits only when both resulting snapshots build and pass tests independently.
- **Pre-Commit Verification & Conventional Commits**:
  - Use Conventional Commit format: `type(scope): concise description` (e.g., `feat(auth): add sign-in and registration`, `fix(web): correct health card rendering`).
  - Describe the feature scope, architectural additions, and exact automated test results in the commit body.
  - Before asking for commit approval, verify that all checks matching the change pass (diff, formatting, and link checks for documentation; relevant tests, typecheck, lint, and build for application code).
  - Display the exact file inventory (or diff hunks) and confirm that no real credentials, personal test data, or build artifacts (`bin/`, `obj/`, `dist/`) are staged.
  - Always wait for explicit user approval before staging, committing, or pushing.
