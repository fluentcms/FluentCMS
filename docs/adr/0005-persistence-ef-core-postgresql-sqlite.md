# ADR-0005: Persistence with EF Core on PostgreSQL and SQLite

- Status: accepted
- Date: 2026-10-16
- Related: CNT-02, CNT-05, ENT-04, CHG-05, CHG-06, REL-01, REL-02, AUD-N1, AUD-N2, OPS-01, OPS-04, PERF-01, PERF-03, D26

## Context

The data model is dominated by versioned documents (entry versions whose fields are defined by a schema), relational bookkeeping (types, change sets, operations, routing decisions, credentials) and an append-only audit log that must be written in the same transaction as the change it describes (AUD-N1). Publish and revert are atomic across many items (CHG-05, REL-01). "Where used" and blast radius need fast lookups from an entity field to every version that references it (CNT-05, ENT-04).

v1 supported six database engines. That breadth cost a large test matrix and held the model at the lowest common denominator. v2 targets thousands of entries and tens of change sets a day on one instance (D26), not enterprise scale.

## Decision

EF Core 10 with two providers: PostgreSQL (Npgsql) as the production target and SQLite for local development, tests and the quick-start experience. One `DbContext`, one migration set, with provider-specific pieces isolated behind the search and storage interfaces.

Model:

- The field values of an entry version are stored as one JSON document per version (EF Core JSON columns, `jsonb` on PostgreSQL). Schema definitions are JSON too.
- Anything queried or joined is relational: entries, versions and their pointers (latest draft, published), change sets and operations, audit events, agent credentials, media metadata.
- References from a version to an entity or entity field are extracted at write time into a reference index table. "Where used", blast radius and resolved views read that table, never the JSON.
- Every write creates a new version row (CNT-02). Publish moves pointers inside one transaction and writes the provenance rows in the same transaction (AUD-N1). Revert is a new change set that restores previous version pointers (CHG-06).
- The audit table is append-only. The application never updates or deletes its rows, and on PostgreSQL the application role can be denied `UPDATE` and `DELETE` on it. Retention purges are themselves logged (AUD-N2).
- Migrations are EF migrations applied at startup (OPS-04). Every migration has a tested `Down`.

Full-text search uses `tsvector` on PostgreSQL and FTS5 on SQLite behind the search interface (ADR-0010).

## Consequences

- One mental model familiar to most .NET contributors. Integration tests run against a PostgreSQL container and SQLite in-process.
- SQLite is single-writer and does not meet the concurrency target (PERF-01). The documentation says PostgreSQL for anything beyond one person trying it out.
- JSON querying differs between the two providers. The reference index keeps the hot paths relational so the difference stays small.
- Adding MySQL or SQL Server later is an Infrastructure change plus a test matrix entry, if someone wants it enough to maintain it.

## Alternatives considered

- Marten (PostgreSQL document store with event sourcing): the closest fit to versioned documents and change sets, but PostgreSQL only, which removes the zero-setup quick start, and less familiar to contributors than EF Core.
- MongoDB: document-native and supported in v1, but one more engine to test, and the relational bookkeeping around change sets is the larger part of the model.
- All v1 providers: the test matrix and lowest-common-denominator problem that motivated this decision.
- Dapper with hand-written SQL: more control, far more code for migrations across two dialects.
