# ADR-0006: Admin UI with Blazor interactive server rendering

- Status: proposed
- Date: 2026-10-16
- Related: P1, D11, DSK-01 to DSK-05, FRM-01 to FRM-03, UX-02, UX-04, I18-N1, I18-N2, PERF-04

## Context

The admin UI in M1 is the review desk, the rule book, generated forms, media, users and agent credentials; M2 adds the rest. It serves 3 to 15 people on a self-hosted instance. By principle P1 every capability exists as an MCP tool first, and the UI must not grow logic the tools lack. The requirements define a read-only delivery API and the MCP server, but no admin HTTP API.

The desk must be keyboard-first (DSK-04, UX-02), render large diffs progressively (PERF-04), meet WCAG 2.1 AA (UX-04), and have localizable, RTL-capable strings from M1 (I18-N1, I18-N2).

## Decision

The admin UI is a Blazor Web App using the interactive server render mode for application pages and static server rendering for sign-in and other stateless pages. Components call Core application services directly through dependency injection, the same services the MCP tools call. No admin HTTP API is built in M1.

Supporting choices:

- Diffs are computed in Core, since agents need them through tools anyway, and rendered from a structured diff model. No client-side diff library.
- Long lists use Blazor's built-in virtualization. Keyboard shortcuts use one small JavaScript interop module.
- Strings go through `IStringLocalizer` resources from the first screen. Layouts use logical CSS properties and the `dir` attribute for RTL.
- Authentication is cookie-based through ASP.NET Core Identity (ADR-0009).
- The component set and CSS approach are decided in ADR-0011. Constraints set here: permissive licence, accessibility, RTL, keyboard support, and no build step a contributor cannot run with the .NET SDK alone.

## Consequences

- No second API surface and no DTOs to keep in sync with the tool contracts. P1 is enforced by construction: a screen can only do what a Core service does.
- One language for contributors and no Node toolchain in the repository.
- Each user holds a SignalR connection and interactions round-trip to the server. Acceptable at the target scale on a LAN or VPS, noticeably slower on high-latency links. The render mode can change per page later without rewriting components.
- Server-side state must survive a lost circuit: review decisions are saved as they are made, not held in component state.

## Alternatives considered

- React or another SPA: larger ecosystem for keyboard-first interfaces and a wider contributor pool, at the cost of an admin API, duplicated contracts and a second language in the repository. Not justified for a team-sized internal tool.
- Blazor WebAssembly: same admin API cost as an SPA, slower first load, keeps C#.
- Blazor auto render mode: both costs, chosen only if WebAssembly becomes necessary.
