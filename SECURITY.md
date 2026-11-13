# Security Policy — CampusVenue

The CampusVenue engineering team prioritizes application security, confidentiality of institutional data, and operational integrity.

---

## 1. Supported Versions

Only the active development branch and release tags receive security updates.

| Version / Branch | Supported          |
| ---------------- | ------------------ |
| `main`           | :white_check_mark: |
| Development tags | :white_check_mark: |
| Legacy / PoC     | :x:                |

---

## 2. Reporting a Vulnerability

If you discover a potential security vulnerability within CampusVenue, please do **NOT** open a public issue.

Instead, report the vulnerability privately:
- **Email**: `security@campusvenue.internal` (or repository administrator)
- **Required Details**:
  - Description of the vulnerability and affected component / endpoint.
  - Step-by-step reproduction steps or proof-of-concept payload.
  - Potential impact assessment (e.g., unauthorized access, data exposure, privilege escalation).
  - Mitigation suggestions if known.

We will acknowledge receipt within 48 hours and work with you on a responsible disclosure timeline.

---

## 3. Engineering Security Baseline

CampusVenue enforces the following mandatory security controls:

1. **Secret Zero-Exposure**:
   - Secrets and credentials must never be committed to Git.
   - All secret configurations must use environment variables or dedicated secret management tooling (e.g., Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault in cloud deployments).
   - `.env` files are excluded via `.gitignore`.
2. **Authentication & Authorization**:
   - Zero-trust model: All authorization checks are evaluated server-side.
   - Strict role-based and organization-scoped tenant isolation.
   - Future session tokens must utilize `HttpOnly`, `Secure`, and `SameSite` flags.
3. **Transport & Network**:
   - TLS 1.3 enforced for public endpoints.
   - Cross-Origin Resource Sharing (CORS) restricted to explicit trusted frontend origins.
4. **Data Protection**:
   - Sensitive PII and credentials must be masked or excluded from application logs.
   - SQL queries must be parameterized via EF Core to prevent SQL injection.
   - All external user inputs are sanitized and validated against whitelist constraints.
