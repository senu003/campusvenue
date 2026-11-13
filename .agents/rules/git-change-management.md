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

## 2. File Change Transparency

- When files are created, edited, or removed, list every affected file path clearly in the response.
- Do not make opportunistic, sweeping refactorings of unrelated files.
- Preserve existing code and files created in prior steps unless explicit instructions require updates.
- If a conflict arises between requested changes and existing files, stop and explain the discrepancy before overwriting.
