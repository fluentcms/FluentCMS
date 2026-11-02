# 6. Publishing workflow & versioning — approved

Every write creates a version; publishing is an explicit step that humans or scheduled jobs take, and agents only ever draft. Versions, scheduling and the activity log are free in the first release because Payload and Directus ship them free and Strapi gates them. Approval workflows follow in a minor release, built on the same state machine, because they are the most paywalled editorial feature in the market and Umbraco charges EUR 2,800 a year for them.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | Draft and published states | Must | Each entry has at most one published version and one working draft; states Draft, Published, Modified (published with newer draft), Unpublished. Per-locale state when the type is localized. |
| 2 | Publish, unpublish, discard draft | Must | Explicit operations with permission checks, hooks and events; publish runs full validation; discard reverts the draft to the published version. |
| 3 | Version on every write | Must | Immutable snapshot per save with author, time, origin (admin, API, MCP, job) and optional message; configurable cap per entry with oldest-first pruning, published versions never pruned. |
| 4 | Diff and restore | Must | Field-level diff between any two versions, including blocks and rich text; restore creates a new draft from an old version. |
| 5 | Scheduled publish and unpublish | Must | Date-time per entry (and per locale) executed by the job runner; visible in list views; cancel or reschedule; time zone aware. |
| 6 | Job runner | Must | Durable queued and scheduled jobs with retries, backoff, dead-letter, concurrency limits and a visible job log; database-backed so no extra infrastructure is required; multi-instance safe. |
| 7 | Publish-time validation | Must | Required fields and custom validators enforced at publish, not at draft save; referenced entries must be published or the publish is blocked or warned, per type setting. |
| 8 | Optimistic concurrency | Must | Writes carry the version they were based on; conflicting saves are rejected with both versions available for the admin to show. |
| 9 | Soft delete and trash | Must | Delete moves to trash with retention and restore; permanent delete is a separate permission. |
| 10 | Publishing events | Must | Typed events for every transition (drafted, published, unpublished, scheduled, restored, trashed) feeding hooks, webhooks, cache invalidation and the activity log. |
| 11 | Agent-safe publishing | Must | MCP and job origins create drafts by default; a separate `publish` permission and tool are required to publish; every agent write is tagged in the version and activity log. |
| 12 | Approval workflows | Should | Configurable stages per type (Draft, In review, Approved), per-role transitions, assignees, comments on transitions, a required stage before publish, notifications by email and in the admin. |
| 13 | Workflow engine extension point | Should | Pluggable task types (group approval, external check, timed) in the Wagtail style, so plugins can add stages without forking the engine. |
| 14 | Document locking | Should | Lock on open with heartbeat, visible holder, take-over with notification; relies on the SignalR channel. |
| 15 | Autosave | Should | Debounced draft saves that do not create full versions until an explicit save; conflict-aware. |
| 16 | Version comments and labels | Should | Named versions ("Spring launch copy") and messages on save. |
| 17 | Bulk publish and schedule | Should | Publish, unpublish or schedule many entries from the list view with a result report. |
| 18 | Content calendar | Should | Calendar view of scheduled and recently published items across types. |
| 19 | Releases | Could | Named bundles of entries published or unpublished together at a scheduled time, validated as a set before publish; references not snapshots. |
| 20 | Per-field accept or reject on publish | Could | Compare the draft with the published version and accept changes field by field. |
| 21 | Compare with live preview | Could | Visual side-by-side of two versions rendered in the preview frame. |
| 22 | Expiry and review reminders | Could | Review-by dates with reminders; auto-unpublish on expiry. |
| 23 | Patch-based version storage | Could | Store diffs instead of snapshots to reduce storage and enable change feeds. |
| 24 | Realtime co-editing | Won't | Multi-cursor editing of one draft. |
| 25 | Git-based branching of content | Won't | Content branches and merges. |
| 26 | Gating versions, scheduling or workflows behind a tier | Won't | All free. |

## Decisions this category forces

- [ ] Version storage: full JSON snapshot per version (simple, fast restore) or snapshot plus stored diff. Recommendation: snapshot in the first release; diff computed on demand.
- [ ] Draft storage: draft and published as two rows per entry (Strapi) or a versions table with pointers (Payload). Recommendation: current-draft and current-published rows for fast reads, plus the versions table for history.
- [ ] Job runner: build on the database with `FOR UPDATE SKIP LOCKED` semantics (Postgres and SQL Server; polling on SQLite) or adopt Hangfire or Quartz. Recommendation: own minimal runner behind an interface, so hosts can swap in Hangfire.
- [ ] Reference integrity at publish: block, warn or allow publishing an entry that references an unpublished entry. Recommendation: warn by default, block per type when configured.
