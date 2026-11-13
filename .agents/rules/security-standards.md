---
description: Security standards for authentication, authorization, secret protection, CORS, and data safety.
trigger: always_on
---

# Security Standards

## 1. Credentials & Secrets Protection

- **Zero Secret Commits**:
  - Never commit API keys, connection strings with production/staging credentials, JWT private keys, or passwords.
  - Development credentials must reside in `.env` (strictly ignored by Git) or user secrets (`dotnet user-secrets`).
  - `.env.example` provides safe placeholder schemas with dummy values only.

## 2. Authentication & Authorization Guardrails

- **Zero Client Trust**:
  - Never trust client-sent user IDs, roles, organization memberships, or permissions. All identity and role claims must be parsed and verified from signed server tokens or secure HTTP-only cookies.
- **Server Enforcement**:
  - Every mutating endpoint must enforce role and organization-level scoping on the server.
  - A coordinator in Department A cannot reserve or approve venues assigned to Department B unless granted university-wide administrative privileges.
- **Session & Cookie Security**:
  - Auth cookies must be configured with `HttpOnly`, `Secure` (in non-local envs), and `SameSite=Strict` or `Lax`.
  - Protect against Cross-Site Request Forgery (CSRF) for state-altering requests.

## 3. Network & Transport Security

- **Strict CORS**:
  - Restrict CORS headers to explicitly whitelisted origin URLs (e.g., local frontend `http://localhost:3000`).
  - Never use `AllowAnyOrigin()` (`*`) with credentials enabled.
- **Data Protection in Transit & at Rest**:
  - Enforce HTTPS for external communication.
  - Sanitize all log output: never log raw passwords, access tokens, social security numbers, or sensitive PII.
  - Return RFC 7807 Problem Details without exposing internal stack traces, connection strings, or SQL syntax errors.
