# Preview stability implementation plan

**Goal:** Protect existing data and durable events before adding another feature. Leave one reviewed, unmerged pull request into `main`.

**Architecture:** Keep backup validation on the command and its immutable SQLite snapshot, and keep settings writes and their outbox rows on one database transaction. Change the outbox shutdown behavior only if the Windows failure demonstrates a defect in that behavior.

**Stack:** .NET 10, EF Core, SQLite, PostgreSQL, SQL Server, and the repository's xUnit tests. No new dependencies or public API freeze.

**Specification:** [Windows nightly](https://github.com/fluentcms/FluentCMS/issues/3730), [backup destination](https://github.com/fluentcms/FluentCMS/issues/3724), [backup consistency](https://github.com/fluentcms/FluentCMS/issues/3725), [settings outbox](https://github.com/fluentcms/FluentCMS/issues/3727), and the [roadmap](../../../ROADMAP.md).

## Review focus

Protect an existing database or archive even on failure. Check directory boundaries rather than filename prefixes. Decide which original files belong to a backup from its snapshot, not the current upload provider. Preserve optimistic concurrency, secret handling, and actor attribution for settings. Distinguish a scheduled retry from a lost delivery; a single passing run does not explain the failing nightly.

## Independently testable checkpoints

- [x] **Windows outbox (#3730).** The fixture, not the worker, cancelled a shared database read. The regression holds the read open across shutdown, checks the pending rows and their attempts and leases, and verifies all three deliveries on resume without changing production code. [Checkpoint PR](https://github.com/fluentcms/FluentCMS/pull/3731).
- [x] **Backup destination (#3724).** The command refuses the database, its journal paths, the media tree and ambiguous destinations before opening SQLite. A linked media root is supported; file links inside it cannot point to the output, and a nested directory link or junction is refused rather than followed or skipped. The [backup guide](../../backups.md) explains these refusals and the limits of path checks across mounts.
- [x] **Backup contents (#3725).** The staged ZIP must match the SQLite snapshot's local originals by exact key, size and checksum. A replacement during the copy leaves the previous archive intact or yields a complete new one. Extra uploads are allowed, and a valid named `.tmp` original is distinguished from upload scratch files. No media retention or SVG behavior changed.
- [x] **Settings and their event (#3727).** The values and handler rows are prepared before the database write lock, the settings change and event are committed together, and notifications follow the commit. The SQLite tests cover rollback, postcommit interruption, conflicts, no-ops, cold plugin state and actor/site attribution. PostgreSQL and SQL Server remain part of the CI data matrix. [Checkpoint PR](https://github.com/fluentcms/FluentCMS/pull/3732).
- [x] **1.0 checklist recorded.** The [roadmap](../../../ROADMAP.md#before-100) names the real Azure and Entra/OIDC evidence and the four developer-experience decisions. They remain open; the code checkpoints do not verify external services, build starters or freeze the API.
- [ ] **Integration (maintainer handoff).** The protected `feature/stabilize-preview-2` branch takes checkpoints through pull requests. After the maintainer merges the reviewed checkpoints into that branch, compare its combined diff with `main`, verify CI on that revision and open one final pull request into `main`. Link verified fixes only as closing references; explicitly list unresolved checks. Leave the final pull request unmerged and auto-merge off.
