# 0005 Turn on READ_COMMITTED_SNAPSHOT in SQL Server databases with a system migration

## Status

Accepted.

## Date

2026-11-09

## Context

Under SQL Server's default `READ COMMITTED`, a read takes shared locks and waits for writers. A read and a write that lock the same rows in different orders deadlock, and one fails with error 1205. A typical case: the read goes through a nonclustered index to the clustered row, and the write changes the clustered row and then the index.

- [#2959](https://github.com/fluentcms/FluentCMS/issues/2959) found this in content writes: the winner's response read deadlocked with a loser's write. [#2961](https://github.com/fluentcms/FluentCMS/pull/2961) reruns a content deadlock victim up to five times and rereads a response after a deadlock.
- [#2963](https://github.com/fluentcms/FluentCMS/issues/2963): the outbox claim (`OutboxDrainer.ClaimAsync`) still failed with 1205 when drains ran together. The job claim, the job writers and the distributed lock read and write the same way.

PostgreSQL's `READ COMMITTED` reads the data committed when each statement started and never waits for a writer, and the code is validated there.

Options considered:

- **A. Rerun deadlock victims** in every claim and multi-statement writer, as `ContentService.TransactAsync` does. Every writer needs it, new ones too, and a rerun costs time under load. PostgreSQL never needs them.
- **B. Require `READ_COMMITTED_SNAPSHOT`** in the documentation and refuse to start without it. A database made with `CREATE DATABASE` has it off, so every on-premises install needs a manual step.
- **C. Turn it on in a system migration** when it is off, and fail with a clear message when it cannot be.

## Decision

We will adopt option C, and keep the content service's deadlock reruns as a backstop:

- **Migration.** `EnableReadCommittedSnapshot` in the SQL Server system set runs outside a transaction, because `ALTER DATABASE` cannot run in one.
  - When `sys.databases.is_read_committed_snapshot_on` is already 1, it does nothing and needs no permission. Azure SQL Database has it on by default.
  - Otherwise it runs `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE`, which needs `ALTER` on the database.
  - Without that permission it fails before the `ALTER` with error 50000. The message names the statement for an administrator to run. The migration is not recorded, so startup or `MigrateAsync` stops as it does for any failed migration. A host that does not migrate on start refuses with `FCMS1305`, naming the pending migration.
  - `Down` leaves it on.
  - Since the baseline of [#3052](https://github.com/fluentcms/FluentCMS/issues/3052), the statement is the first step of the SQL Server system migration `Initial`, so a failure leaves nothing applied.
- **Locks that stay.** `READ_COMMITTED_SNAPSHOT` changes only reads at `READ COMMITTED`:
  - The job claim keeps `UPDLOCK, READPAST, ROWLOCK`. `UPDLOCK` makes its read a locking read of the latest committed row, and `READPAST` skips rows another claimer holds, so two claimers still never take one job.
  - Writes still take update and exclusive locks, and an `UPDATE` qualifies rows on the latest committed version. So every conditional update still matches nothing once another writer has changed the row: the outbox claim and settles, job leases and settles, and the distributed lock's take, renewal and release.
  - The schema-history record keeps its `SERIALIZABLE` transaction, which the option does not change, and migrations keep EF's `sp_getapplock`.
- **Backstop.** `ContentService` keeps its deadlock reruns, because two writers can still deadlock with each other.

## Consequences

- SQL Server reads as PostgreSQL does: a read sees the data committed when its statement started and never waits for a writer, so reads and writes no longer deadlock. With it on, the outbox and job tests passed 50 runs in a row on SQL Server 2019 and 2025.
- Row versions are kept in `tempdb`, and a changed row grows by 14 bytes. A long transaction keeps versions longer.
- Turning it on needs the database to itself. The other sessions of the database are disconnected and their open transactions rolled back, once. Apply the system migrations from one process before the other nodes start, as [Docker](../docker.md) describes, or turn the option on beforehand. A node that waits for the migrations lock at that moment loses its connection and can fail to start once; it finds nothing to apply when it starts again.
- Nothing checks the option after the migration. An administrator who turns it off brings the blocking reads back, and only the content service's reruns cover them.
- The test server turns it on in `model`, so every SQL Server test database has it from the start, as a migrated one does. `Migrations/SqlServerSnapshotTests` turn it off to test the migration.
