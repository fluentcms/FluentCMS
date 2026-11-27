# Preview stability implementation plan

**Goal:** Protect existing data and durable events before adding another feature. Leave one reviewed, unmerged pull request into `main`.

**Architecture:** Keep backup validation on the command and its immutable SQLite snapshot, and keep settings writes and their outbox rows on one database transaction. Change the outbox shutdown behavior only if the Windows failure demonstrates a defect in that behavior.

**Stack:** .NET 10, EF Core, SQLite, PostgreSQL, SQL Server, and the repository's xUnit tests. No new dependencies or public API freeze.

**Specification:** [Windows nightly](https://github.com/fluentcms/FluentCMS/issues/3730), [backup destination](https://github.com/fluentcms/FluentCMS/issues/3724), [backup consistency](https://github.com/fluentcms/FluentCMS/issues/3725), [settings outbox](https://github.com/fluentcms/FluentCMS/issues/3727), and the [roadmap](../../../ROADMAP.md).

## Review focus

Protect an existing database or archive even on failure. Check directory boundaries rather than filename prefixes. Decide which original files belong to a backup from its snapshot, not the current upload provider. Preserve optimistic concurrency, secret handling, and actor attribution for settings. Distinguish a scheduled retry from a lost delivery; a single passing run does not explain the failing nightly.

## Independently testable checkpoints

- [ ] **Windows outbox (#3730).** Compare the failed x64 run with `PluginSiteTests.AStopDuringTheReadOfThePluginStateSettlesWhatWasDeliveredAndGivesTheRestBack` and `OutboxDrainer.DeliverAsync`. Observe the three calls, row status/attempts, lease and availability at the interrupted and resumed drains. Exercise the cancellation ordering without arbitrary sleeps. Add a regression for the demonstrated cause; if evidence is insufficient, report the open investigation instead of closing the issue. Run the exact case, the plugin-site test class and relevant existing shutdown tests.
- [ ] **Backup destination (#3724).** In `BackupCommand.RunAsync`, before listing migrations or making files, refuse the database, its three journal paths, and the local media root or a descendant. Register a dedicated diagnostic and document it in `docs/backups.md`. Test with disposable paths and sentinel bytes; include sibling directories and replacement of a legitimate archive. Run the two `BackupCommandTests` classes.
- [ ] **Backup contents (#3725).** Read the original local media keys from the SQLite snapshot. Before publishing the new archive, confirm its entries contain those originals. A replaced or missing original must not replace the previous archive; extra uploads and a database without media remain valid. Do not retain replaced files or change SVG sanitization. Use an interposed snapshot and a disposable media directory in the command tests to prove the ordering without sleeps. Update the backup guide and rerun the backup tests.
- [ ] **Settings and their event (#3727).** Prepare/protect values before taking the write lock. In `SettingsService`, stage `SettingsChangedEvent` in the same short transaction as each successful write, reset or import, following `IdentityChanges`/`EventBuffer`. Invalidate and dispatch after commit. A failed stage rolls back; a failed post-commit dispatch leaves its durable row. Exercise conflicts, no-op resets, dry-run/skipped imports and caller/site attribution in `FluentCMS.Data.Tests/Settings`; exercise the settings endpoints. Run cross-provider cases in CI.
- [ ] **1.0 checklist.** Update the roadmap with the verification records for real Azure and Entra/OIDC, including who needs to run them and what evidence counts. Track ship/defer decisions for DX rows 11.7–11.10 and leave the contract freeze at release time. Do not mark a live check passed without a real run or build the starters/docs platform in this batch.
- [ ] **Integration.** Commit and push separately verified checkpoints, inspect the combined diff, run the relevant combined tests and submit one pull request into `main` using the repository template. Link verified fixes only as closing references; explicitly list unresolved checks. Do not merge or enable auto-merge.
