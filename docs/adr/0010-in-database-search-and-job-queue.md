# ADR-0010: In-database full-text search and a durable job queue

- Status: accepted
- Date: 2026-10-16
- Related: SRC-01, CHK-03, PERF-02, MCP-08, API-05, OPS-02, AGT-02 (M2)

## Context

Agents and the desk need full-text and structured search over entries and entities (SRC-01). Checks run on every change set, the LLM-judged ones asynchronously with progress (CHK-03, PERF-02). Bulk tool calls return a change set id and continue in the background (MCP-08). Webhooks fire on publish (API-05). M2 adds scheduled and event-triggered agent runs (AGT-02). None of this may require a service beyond the database (OPS-02).

## Decision

- Search: a search interface in Core with two Infrastructure implementations, PostgreSQL `tsvector` with a GIN index and SQLite FTS5. Indexing happens in the same transaction as the write. Structured filters are ordinary EF queries over the relational tables and the reference index (ADR-0005). The interface is an extension point, so an external engine can be added later by whoever needs it.
- Background work: a jobs table in the application database (type, payload, status, attempts, scheduled time, lease) processed by a hosted service in Host with a bounded worker pool. Jobs are enqueued inside the transaction of the change that needs them, so a job cannot exist without its change or the other way round. Progress is written back to the job row, which tools and the desk read.
- Deterministic checks run inline on the request path for small sets and as a job otherwise, with the threshold tuned to PERF-02's ten-second target.
- M2 schedules add a cron-style trigger table feeding the same queue. No separate scheduler process.

## Consequences

- Zero extra infrastructure for a self-hoster. One database holds content, audit, search and jobs.
- Search quality is what the database offers: exact and prefix matching with language stemming, no semantic ranking. Acceptable for thousands of entries.
- The job table is polled on a short interval to start. PostgreSQL `LISTEN/NOTIFY` can cut latency later without changing the model.
- Restart safety comes from leases and attempt counts, not from an external broker.

## Alternatives considered

- Hangfire or Quartz.NET: mature, but each brings its own schema and dashboard, and Hangfire's core is LGPL. The job model needed here is small.
- Redis or RabbitMQ: external services, excluded by OPS-02 for the default deployment.
- In-memory channels only: lost on restart. Checks and webhooks must not be.
- Elasticsearch or Meilisearch: better relevance, external service. Available later through the search interface.
