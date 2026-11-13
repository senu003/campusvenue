# ADR 0004: RESTful APIs and SignalR for Real-Time Communication

## Status
Accepted

## Date
2026-11-13

## Context
CampusVenue serves two communication paradigms:
1. Standard transactional request/response workflows (resource browsing, form submissions, approvals, administration).
2. Real-time multi-client synchronization (live seating map occupancy changes, instantaneous hold notifications, approval status alerts).

## Decision
We select a hybrid communication architecture:
1. **RESTful HTTP APIs** for all CRUD, command execution, and queries.
2. **ASP.NET Core SignalR** (WebSockets with automatic fallback to Server-Sent Events / Long Polling) for bidirectional real-time push events.

## Justification
- **Industry Standard & Cacheable**: RESTful APIs provide predictable, cacheable, well-documented endpoints adhering to HTTP standards and RFC 7807 Problem Details.
- **Built-in ASP.NET Core SignalR**: SignalR integrates seamlessly into ASP.NET Core dependency injection, authentication pipelines, and hub abstractions without requiring external brokers (like Redis) during early phases.
- **Immediate User Feedback**: During seat booking, when another user holds Seat B12, SignalR immediately informs all open browser clients to color Seat B12 as "Temporarily Held" without polling delays.

## Consequences
- Requires persistent WebSocket connection management on frontend clients.
- When scaling backend nodes across multiple servers in Phase 12, a SignalR backplane (e.g., Redis or Azure SignalR Service) will be evaluated if required.
