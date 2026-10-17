# ADR-0001: Record architecture decisions

- Status: accepted
- Date: 2026-10-16
- Related: `docs/requirements/00-decision-record.md` (product decisions D01 to D27)

## Context

The product decisions for v2 live in the decision record and are owned by the product side. Technical decisions need the same treatment: a place where a contributor can find why the code is the way it is, and a process for changing it. An open source project with occasional contributors loses that reasoning quickly when it only exists in chat threads or pull request comments.

## Decision

Technical decisions are recorded as Architecture Decision Records in `docs/adr/`, one file per decision, named `NNNN-short-title.md`, with this structure: status, date, related requirement IDs, context, decision, consequences, alternatives considered.

Statuses: `proposed`, `accepted`, `superseded by ADR-NNNN`, `deprecated`. An accepted ADR is not edited except to change its status; a new ADR supersedes it.

Process: anyone proposes an ADR through an issue labelled `type:adr`. The ADR file is added in the pull request that implements the change, or in its own pull request when the decision precedes the work. Maintainers accept by merging and setting the status.

Product decisions stay in the decision record. An ADR cites the product decisions and requirement IDs it serves so the two can be read together.

## Consequences

- Every non-obvious technical choice has a findable rationale next to the code.
- Contributors challenge a decision through a visible process instead of a drive-by rewrite.
- Small overhead per decision, kept low by the short format.

## Alternatives considered

- Decisions in the wiki: not versioned with the code, easy to drift.
- Decisions only in pull request descriptions: not findable by topic, and lost when the branch is deleted.
